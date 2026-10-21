using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// 총 발사 (히트스캔) + 탄약 방식 3종 + 절차적 반동.
/// 무기 오브젝트(예: Pistol_Compact_East.Rig, Shotgun, Sniper)에 붙인다.
///
/// 탄약 방식
///  - Magazine : 탄창. R로 재장전 (Reload 애니메이션 재생). 권총.
///  - Cooldown : N발 쏘면 자동으로 충전 시간 동안 사용 불가. 스나이퍼.
///  - Battery  : 쏠 때마다 에너지 소모, 안 쏘면 자동 충전. 0이 되면 과열 → 다 찰 때까지 사용 불가. 샷건.
///
/// 공통
///  - 좌클릭 발사, 화면 중앙(카메라)에서 레이캐스트 → Health.TakeDamage
///  - Pellet Count > 1이면 산탄 (Spread Angle 안에서 여러 발)
///  - 총소리 → SoundPulseManager 소나 펄스 + MannequinSpawner.RecordShot
///  - 반동: 무기를 뒤로 밀고 위로 들었다가 스프링처럼 복귀
///  - 섬광: Animator가 처리하는 무기(권총)는 끄고, 없는 무기는 Create Flash Light로 순간 조명 생성
///  - 로컬 플레이어(루트가 Player 태그)에서만 입력을 받음
///  - FirstPersonController가 꺼져 있으면(카운트다운/사망 Freeze) 발사 불가
///  - 우클릭 홀드 = 조준(ADS): 총을 화면 가운데로 일자로 들어 올림(조준점 자동 정렬), 화면 중앙 조준점 표시(평소엔 없음),
///    이동 속도 감소(달리기 불가), 탄 퍼짐 감소. 확대(FOV)는 Ads Zoom을 켰을 때만
///
/// [네트워크 5단계]
///  - 피해는 Health.TakeDamage로 보내면 네트워크 플레이어는 호스트가 처리 (산탄은 대상별로 합쳐 한 번만 전송)
///  - 발사 결과(맞은 지점들)를 OnShotFx로 알림 → PlayerNetworkCombat이 다른 사람에게 전달
///  - 다른 사람 화면에서는 PlayRemoteShot()으로 소리 + 빔/피격 이펙트만 재생
/// </summary>
public class GunWeapon : MonoBehaviour
{
    public enum AmmoMode { Magazine, Cooldown, Battery }

    [Header("기본")]
    [SerializeField] private string weaponName = "PISTOL";
    [SerializeField] private AmmoMode ammoMode = AmmoMode.Magazine;

    [Header("스펙")]
    [Tooltip("한 발(산탄이면 펠릿 하나)당 피해")]
    [SerializeField, Min(0f)] private float damage = 34f;
    [Tooltip("발사 간격 (초)")]
    [SerializeField, Min(0.01f)] private float fireInterval = 0.25f;
    [Tooltip("누르고 있으면 연사")]
    [SerializeField] private bool automatic = false;
    [SerializeField, Min(1f)] private float range = 100f;
    [Tooltip("맞출 레이어 (Default, Character, Ground, Obstacle)")]
    [SerializeField] private LayerMask hitMask = (1 << 0) | (1 << 6) | (1 << 7) | (1 << 8);

    [Header("벽 근접")]
    [Tooltip("카메라에서 총구까지 대략적인 길이 (m). 벽이 이보다 가까우면 WeaponWallRetract가 총을 거둠")]
    [SerializeField, Min(0.1f)] private float barrelLength = 0.7f;

    [Header("산탄")]
    [Tooltip("한 번에 나가는 탄 수 (1이면 단발)")]
    [SerializeField, Min(1)] private int pelletCount = 1;
    [Tooltip("탄이 퍼지는 각도 (도, 원뿔 반각)")]
    [SerializeField, Range(0f, 30f)] private float spreadAngle = 0f;

    [Header("Magazine (탄창)")]
    [SerializeField, Min(1)] private int magazineSize = 7;
    [Tooltip("재장전 시간 (초) - Reload 애니메이션 길이에 맞춤")]
    [SerializeField, Min(0.1f)] private float reloadTime = 2.35f;

    [Header("Cooldown (N발 후 충전)")]
    [SerializeField, Min(1)] private int shotsPerCharge = 1;
    [SerializeField, Min(0.1f)] private float chargeTime = 1.5f;

    [Header("Battery (에너지)")]
    [SerializeField, Min(1f)] private float batteryCapacity = 100f;
    [Tooltip("한 번 쏠 때 소모")]
    [SerializeField, Min(0f)] private float shotCost = 34f;
    [Tooltip("초당 충전량")]
    [SerializeField, Min(0f)] private float rechargeRate = 30f;
    [Tooltip("마지막 발사 후 충전 시작까지 대기 (초)")]
    [SerializeField, Min(0f)] private float rechargeDelay = 0.6f;

    [Header("반동")]
    [Tooltip("반동을 줄 대상 (비우면 이 오브젝트)")]
    [SerializeField] private Transform recoilTarget;
    [Tooltip("뒤로 밀리는 거리 (m)")]
    [SerializeField, Min(0f)] private float recoilKickBack = 0.03f;
    [Tooltip("위로 들리는 각도 (도)")]
    [SerializeField, Min(0f)] private float recoilKickUp = 3f;
    [Tooltip("복귀 속도 (클수록 빨리 돌아옴)")]
    [SerializeField, Min(0.1f)] private float recoilReturnSpeed = 12f;

    [Header("섬광 (애니메이션에 섬광이 없는 무기용)")]
    [SerializeField] private bool createFlashLight = false;
    [Tooltip("이 오브젝트 기준 섬광 위치 (로컬)")]
    [SerializeField] private Vector3 flashLocalOffset = new Vector3(0f, 0f, 0.6f);
    [SerializeField] private Color flashColor = new Color(0.6f, 0.85f, 1f);
    [SerializeField, Min(0f)] private float flashIntensity = 6f;
    [SerializeField, Min(0.1f)] private float flashRange = 10f;
    [SerializeField, Min(0.01f)] private float flashDuration = 0.07f;

    [Header("총구")]
    [Tooltip("총구 위치 (총구 화염이 나오는 곳). 비우면 Flash Local Offset 위치 사용")]
    [SerializeField] private Transform muzzlePoint;

    [Header("조준 (우클릭 홀드)")]
    [Tooltip("조준점 (총 윗면 뒤쪽, 눈을 대는 곳). 비우면 총 모델 크기로 자동 계산 (윗면 뒤쪽 가운데)")]
    [SerializeField] private Transform aimPoint;
    [Tooltip("조준 시 조준점이 화면 중앙보다 이만큼 아래 (m). 0이면 정중앙")]
    [SerializeField] private float adsSightDrop = 0.02f;
    [Tooltip("조준 시 카메라에서 조준점까지 거리 (m). 0이면 평소 거리 유지")]
    [SerializeField, Min(0f)] private float adsSightDistance = 0f;
    [Tooltip("조준 시 확대 (끄면 FOV 그대로)")]
    [SerializeField] private bool adsZoom = false;
    [Tooltip("확대할 때 시야각. 카메라 기본 FOV(70)보다 작을수록 더 확대")]
    [SerializeField, Range(20f, 90f)] private float adsFov = 55f;
    [Tooltip("조준 시 이동 속도 배율 (조준 중에는 달리기 불가)")]
    [SerializeField, Range(0.1f, 1f)] private float adsMoveMultiplier = 0.5f;
    [Tooltip("조준 시 탄 퍼짐(Spread Angle) 배율. 1 = 그대로")]
    [SerializeField, Range(0f, 1f)] private float adsSpreadMultiplier = 0.5f;
    [Tooltip("자동 정렬 후 추가 미세 조정 (부모 WeaponHolder 기준, x=좌우 y=위아래 z=앞뒤). 플레이 중 우클릭 누른 채로 조절")]
    [SerializeField] private Vector3 adsPositionOffset = Vector3.zero;
    [Tooltip("조준 전환 속도 (클수록 빠름)")]
    [SerializeField, Min(0.1f)] private float adsSpeed = 12f;

    [Header("소리 / 소나")]
    [SerializeField] private AudioClip fireClip;
    [SerializeField] private AudioClip reloadClip;
    [SerializeField] private AudioClip emptyClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [SerializeField, Range(0.3f, 2f)] private float firePitch = 1f;
    [Tooltip("총소리 소나 반경 (0이면 소나 없음)")]
    [SerializeField, Min(0f)] private float sonarRadius = 20f;
    [SerializeField, Min(0.01f)] private float sonarExpand = 0.4f;
    [SerializeField, Min(0f)] private float sonarHold = 0.5f;
    [SerializeField, Min(0.01f)] private float sonarFade = 1.5f;
    [Tooltip("발사 위치를 다음 라운드 마네킹 배치용으로 기록")]
    [SerializeField] private bool recordShotForMannequins = true;

    [Header("애니메이터 상태 이름 (Animator가 있을 때만)")]
    [SerializeField] private string fireState = "Fire";
    [SerializeField] private string reloadState = "Reload";

    [Header("연결 (비우면 자동 검색)")]
    [SerializeField] private Animator gunAnimator;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private AudioSource audioSource;

    [Header("모델")]
    [Tooltip("1인칭 총 모델은 그림자를 만들지 않음 (3인칭 총이 그림자를 담당)")]
    [SerializeField] private bool modelShadowsOff = true;
    [Tooltip("모델(렌더러)들의 레이어를 Model Layer로 맞춤")]
    [SerializeField] private bool setModelLayer = true;
    [Tooltip("1인칭 총 모델 레이어. Default(0)면 소나 외곽선에 총 윤곽이 그려지고, 총 뒤 배경 선이 총을 뚫고 보이지 않음")]
    [SerializeField] private int modelLayer = 0;

    [Header("임시 HUD")]
    [SerializeField] private bool showHud = true;
    [SerializeField] private bool showCrosshair = true;
    [SerializeField] private bool debugDrawRay = true;

    /// <summary>발사했을 때 (첫 번째 맞은 지점, 맞은 Health 또는 null)</summary>
    public event Action<Vector3, Health> OnFired;
    public event Action OnReloadStarted;
    /// <summary>발사 연출 정보 (총, 펠릿별 맞은 지점, 면 방향, 명중 여부) - 네트워크 전달용</summary>
    /// <param>표면 코드: 0 = 안 맞음, 1 + SurfaceKind (1 Stone, 2 Metal, 3 Wood, 4 Flesh)</param>
    public event Action<GunWeapon, Vector3[], Vector3[], byte[]> OnShotFx;
    /// <summary>어떤 총이든 총알이 닿은 지점들 (내가 쏜 것 + 다른 사람 화면 재생 모두). 자동차 경보 등 환경 반응용</summary>
    public static event Action<Vector3[]> AnyShotImpact;

    public string WeaponName => weaponName;
    public float BarrelLength => barrelLength;
    public AmmoMode Mode => ammoMode;
    public int Ammo { get; private set; }
    public int MagazineSize => magazineSize;
    public float Energy { get; private set; }
    public bool IsReloading { get; private set; }   // Magazine: 재장전 / Cooldown: 충전 / Battery: 과열
    /// <summary>우클릭 조준 중인지</summary>
    public bool IsAiming => aiming;

    private Transform owner;              // 플레이어 루트
    private Health ownerHealth;
    private Behaviour ownerController;    // FirstPersonController (Freeze 판정용)
    private WeaponWallRetract wallRetract;
    private MannequinSpawner mannequinSpawner;
    private bool isLocal;
    private float nextFireTime;
    private float reloadEndTime;
    private float lastShotTime = -999f;
    private float disabledAt = -1f;
    private int fireHash, reloadHash;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private static readonly Dictionary<Health, float> damageBuffer = new Dictionary<Health, float>();

    // 반동 (이전 프레임에 더한 값을 빼고 새 값을 더함 → Inspector에서 위치를 바꿔도 유지됨)
    private float recoilPos, recoilRot;
    private Vector3 appliedPos;
    private Quaternion appliedRot = Quaternion.identity;

    private Light flashLight;
    private float flashOffTime;

    // 조준
    private bool aiming;
    private float adsT;                  // 0 = 평소, 1 = 완전히 조준
    private CinemachineCamera vcam;
    private float baseFov = -1f;
    private bool fovTouched;
    private float baseMoveSpeed, baseSprintSpeed;
    private bool speedApplied;
    private StarterAssets.StarterAssetsInputs ownerInput;
    private Vector3 adsAlignPos;                         // 자동 정렬: 기본 자세 대비 위치 (부모 기준)
    private Quaternion adsAlignRot = Quaternion.identity; // 자동 정렬: 기본 자세 대비 회전 (부모 기준)

    private GUIStyle hudStyle;
    private Texture2D dot;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        owner = transform.root;
        isLocal = owner.CompareTag("Player");
        ownerHealth = owner.GetComponentInChildren<Health>();
        ownerController = owner.GetComponent<StarterAssets.FirstPersonController>();
        ownerInput = owner.GetComponent<StarterAssets.StarterAssetsInputs>();

        wallRetract = GetComponentInParent<WeaponWallRetract>(true);

        if (setModelLayer)
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer || r is SkinnedMeshRenderer) r.gameObject.layer = modelLayer;
        if (modelShadowsOff)
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer || r is SkinnedMeshRenderer) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (recoilTarget == null) recoilTarget = transform;
        if (gunAnimator == null) gunAnimator = GetComponent<Animator>();
        if (audioSource == null)
        {
            audioSource = GetComponentInChildren<AudioSource>(true);
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        if (createFlashLight)
        {
            var go = new GameObject("FlashLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = flashLocalOffset;
            flashLight = go.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = flashColor;
            flashLight.range = flashRange;
            flashLight.intensity = flashIntensity;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;
        }

        fireHash = Animator.StringToHash(fireState);
        reloadHash = Animator.StringToHash(reloadState);
        Refill();
    }

    private void Start()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        mannequinSpawner = FindAnyObjectByType<MannequinSpawner>();

        if (RoundManager.Instance != null)
            RoundManager.Instance.OnRoundStart += HandleRoundStart;
    }

    private void OnDestroy()
    {
        if (RoundManager.Instance != null)
            RoundManager.Instance.OnRoundStart -= HandleRoundStart;
    }

    private void OnEnable()
    {
        // 꺼져 있던 동안의 충전 반영 (무기 교체)
        if (disabledAt >= 0f)
        {
            float away = Time.time - disabledAt;
            if (ammoMode == AmmoMode.Battery)
                Energy = Mathf.Min(batteryCapacity, Energy + rechargeRate * away);
            disabledAt = -1f;
        }
    }

    private void OnDisable()
    {
        disabledAt = Time.time;

        // 탄창 재장전은 교체하면 취소 (충전/과열은 시간 기준으로 계속 진행)
        if (ammoMode == AmmoMode.Magazine) IsReloading = false;

        // 반동/섬광 원상복구
        if (recoilTarget != null)
        {
            recoilTarget.localPosition -= appliedPos;
            recoilTarget.localRotation = Quaternion.Inverse(appliedRot) * recoilTarget.localRotation;
        }
        appliedPos = Vector3.zero;
        appliedRot = Quaternion.identity;
        recoilPos = recoilRot = 0f;
        if (flashLight != null) flashLight.enabled = false;

        // 조준 해제 (무기 교체 / 사망 등)
        ReleaseAim();
    }

    private void HandleRoundStart(int round) => Refill();

    private void Refill()
    {
        Ammo = ammoMode == AmmoMode.Cooldown ? shotsPerCharge : magazineSize;
        Energy = batteryCapacity;
        IsReloading = false;
    }

    // ───────── 매 프레임 ─────────

    private void Update()
    {
        UpdateAmmoState();

        if (flashLight != null && flashLight.enabled && Time.time >= flashOffTime)
            flashLight.enabled = false;

        if (!isLocal) return;

        var mouse = Mouse.current;
        var kb = Keyboard.current;

        bool canAct = CanAct();
        UpdateAim(canAct && mouse != null && mouse.rightButton.isPressed);
        if (!canAct) return;

        bool firePressed = mouse != null &&
            (automatic ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame);
        bool reloadPressed = kb != null && kb.rKey.wasPressedThisFrame;

        if (reloadPressed) TryReload();
        else if (firePressed) TryFire();
    }

    private void UpdateAmmoState()
    {
        switch (ammoMode)
        {
            case AmmoMode.Magazine:
                if (IsReloading && Time.time >= reloadEndTime)
                {
                    IsReloading = false;
                    Ammo = magazineSize;
                }
                break;

            case AmmoMode.Cooldown:
                if (IsReloading && Time.time >= reloadEndTime)
                {
                    IsReloading = false;
                    Ammo = shotsPerCharge;
                }
                break;

            case AmmoMode.Battery:
                if (Time.time - lastShotTime >= rechargeDelay)
                    Energy = Mathf.Min(batteryCapacity, Energy + rechargeRate * Time.deltaTime);
                if (IsReloading && Energy >= batteryCapacity) IsReloading = false;   // 과열 해제
                break;
        }
    }

    private void LateUpdate()
    {
        // 반동 복귀
        float k = 1f - Mathf.Exp(-recoilReturnSpeed * Time.deltaTime);
        recoilPos = Mathf.Lerp(recoilPos, 0f, k);
        recoilRot = Mathf.Lerp(recoilRot, 0f, k);

        if (recoilTarget == null) return;

        Vector3 newPos = new Vector3(0f, 0f, -recoilPos) + (adsAlignPos + adsPositionOffset) * adsT;
        Quaternion newRot = Quaternion.Euler(-recoilRot, 0f, 0f) * Quaternion.Slerp(Quaternion.identity, adsAlignRot, adsT);

        recoilTarget.localPosition += newPos - appliedPos;
        // 부모(카메라) 기준으로 회전 → 무기 모델 방향과 상관없이 항상 총구가 위로 들림
        recoilTarget.localRotation = newRot * Quaternion.Inverse(appliedRot) * recoilTarget.localRotation;

        appliedPos = newPos;
        appliedRot = newRot;
    }

    // ───────── 조준 (ADS) ─────────

    private void UpdateAim(bool want)
    {
        if (want != aiming)
        {
            if (want && adsT < 0.05f) ComputeAdsAlign();   // 조준 시작: 총 정렬 위치 계산
            aiming = want;
            ApplyMoveSpeed(want);
        }
        if (aiming && ownerInput != null) ownerInput.sprint = false;   // 조준 중 달리기 불가

        float target = aiming ? 1f : 0f;
        adsT = Mathf.Lerp(adsT, target, 1f - Mathf.Exp(-adsSpeed * Time.deltaTime));
        if (Mathf.Abs(adsT - target) < 0.001f) adsT = target;
        ApplyFov();
    }

    private void ApplyMoveSpeed(bool aim)
    {
        var fpc = ownerController as StarterAssets.FirstPersonController;
        if (fpc == null) return;
        if (aim && !speedApplied)
        {
            baseMoveSpeed = fpc.MoveSpeed;
            baseSprintSpeed = fpc.SprintSpeed;
            fpc.MoveSpeed = baseMoveSpeed * adsMoveMultiplier;
            fpc.SprintSpeed = fpc.MoveSpeed;
            speedApplied = true;
        }
        else if (!aim && speedApplied)
        {
            fpc.MoveSpeed = baseMoveSpeed;
            fpc.SprintSpeed = baseSprintSpeed;
            speedApplied = false;
        }
    }

    /// <summary>
    /// 조준했을 때 총의 위치·회전 계산: 총 몸통 축(뒤 가운데 → 총구)을 카메라 정면과 일직선으로,
    /// 조준점(윗면 뒤쪽)을 화면 중앙 바로 아래로
    /// </summary>
    private void ComputeAdsAlign()
    {
        adsAlignPos = Vector3.zero;
        adsAlignRot = Quaternion.identity;
        Transform cam = aimCamera != null ? aimCamera.transform : null;
        Transform gun = recoilTarget;
        Transform parent = gun != null ? gun.parent : null;
        if (cam == null || gun == null || parent == null) return;

        // 총 모델 범위 (카메라 기준) → 윗면 뒤쪽 가운데 / 뒤 가운데 / 앞 가운데
        bool any = false;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            Bounds b = r.localBounds;
            Transform space = r is SkinnedMeshRenderer sk && sk.rootBone != null ? sk.rootBone : r.transform;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 pc = cam.InverseTransformPoint(space.TransformPoint(c));
                min = Vector3.Min(min, pc);
                max = Vector3.Max(max, pc);
                any = true;
            }
        }
        if (!any) return;
        Vector3 center = (min + max) * 0.5f;

        Vector3 sightW = aimPoint != null ? aimPoint.position : cam.TransformPoint(new Vector3(center.x, max.y, min.z));
        Vector3 rearW = cam.TransformPoint(new Vector3(center.x, center.y, min.z));
        Vector3 frontW = muzzlePoint != null ? muzzlePoint.position : cam.TransformPoint(new Vector3(center.x, center.y, max.z));

        // 총 기준 로컬 (자세와 무관)
        Vector3 sightL = gun.InverseTransformPoint(sightW);
        Vector3 rearL = gun.InverseTransformPoint(rearW);
        Vector3 frontL = gun.InverseTransformPoint(frontW);

        // 기본 자세 (반동·조준 오프셋을 뺀 상태)
        Vector3 baseLocalPos = gun.localPosition - appliedPos;
        Quaternion baseLocalRot = Quaternion.Inverse(appliedRot) * gun.localRotation;
        Vector3 p0 = parent.TransformPoint(baseLocalPos);
        Quaternion r0 = parent.rotation * baseLocalRot;
        Vector3 s = gun.lossyScale;
        Vector3 W(Vector3 l) => p0 + r0 * Vector3.Scale(l, s);

        // 1) 회전: 몸통 축을 카메라 정면으로
        Vector3 axis = W(frontL) - W(rearL);
        if (axis.sqrMagnitude < 1e-6f) return;
        Quaternion turn = Quaternion.FromToRotation(axis.normalized, cam.forward);
        Quaternion r1 = turn * r0;
        Vector3 s1 = p0 + turn * (W(sightL) - p0);

        // 2) 위치: 조준점을 화면 중앙 바로 아래로
        float depth = adsSightDistance > 0f ? adsSightDistance : Vector3.Dot(s1 - cam.position, cam.forward);
        Vector3 target = cam.position + cam.forward * depth - cam.up * adsSightDrop;
        Vector3 p1 = p0 + (target - s1);

        adsAlignPos = parent.InverseTransformPoint(p1) - baseLocalPos;
        adsAlignRot = (Quaternion.Inverse(parent.rotation) * r1) * Quaternion.Inverse(baseLocalRot);
    }

    private void ApplyFov()
    {
        if (!adsZoom)
        {
            if (fovTouched) SetFov(baseFov);   // 확대 끔: 혹시 바뀐 FOV는 원래대로
            fovTouched = false;
            return;
        }
        if (vcam == null)
        {
            if (adsT <= 0f) return;
            vcam = FindAnyObjectByType<CinemachineCamera>();
            if (vcam == null) return;
        }

        if (adsT <= 0f)
        {
            if (fovTouched) SetFov(baseFov);   // 원래 값으로 한 번 되돌림
            fovTouched = false;
            return;
        }

        if (!fovTouched)
        {
            baseFov = vcam.Lens.FieldOfView;   // 조준 시작 순간의 기본 FOV
            fovTouched = true;
        }
        SetFov(Mathf.Lerp(baseFov, adsFov, adsT));
    }

    private void SetFov(float fov)
    {
        if (vcam == null) return;
        var lens = vcam.Lens;
        lens.FieldOfView = fov;
        vcam.Lens = lens;
    }

    private void ReleaseAim()
    {
        aiming = false;
        ApplyMoveSpeed(false);
        adsT = 0f;
        if (fovTouched) SetFov(baseFov);
        fovTouched = false;
    }

    private bool CanAct()
    {
        if (ownerController != null && !ownerController.enabled) return false;   // Freeze 중
        if (ownerHealth != null && ownerHealth.IsDead) return false;
        if (Cursor.lockState != CursorLockMode.Locked) return false;           // 커서 풀린 상태(메뉴 등)
        if (wallRetract != null && wallRetract.BlocksFire) return false;       // 벽에 붙어 총을 거둔 상태
        return true;
    }

    // ───────── 발사 ─────────

    private bool HasAmmo()
    {
        switch (ammoMode)
        {
            case AmmoMode.Battery: return Energy >= shotCost && !IsReloading;
            default: return Ammo > 0;
        }
    }

    /// <summary>발사 시도. 성공하면 true</summary>
    public bool TryFire()
    {
        if (IsReloading || Time.time < nextFireTime) return false;

        if (!HasAmmo())
        {
            if (emptyClip != null) audioSource.PlayOneShot(emptyClip, volume);
            if (ammoMode == AmmoMode.Magazine) TryReload();
            return false;
        }

        ConsumeAmmo();
        nextFireTime = Time.time + fireInterval;
        lastShotTime = Time.time;

        if (gunAnimator != null && gunAnimator.runtimeAnimatorController != null)
            gunAnimator.Play(fireHash, 0, 0f);
        if (fireClip != null)
        {
            audioSource.pitch = firePitch;
            audioSource.PlayOneShot(fireClip, volume);
        }

        // 반동 / 섬광
        recoilPos += recoilKickBack;
        recoilRot += recoilKickUp;
        if (flashLight != null)
        {
            flashLight.enabled = true;
            flashOffTime = Time.time + flashDuration;
        }
        if (ImpactEffects.Instance != null)
        {
            Transform aim = aimCamera != null ? aimCamera.transform : transform;
            ImpactEffects.Instance.PlayMuzzleFlash(MuzzlePosition, aim.forward, muzzlePoint != null ? muzzlePoint : transform);   // 총구를 따라감
        }

        // 히트스캔 (산탄이면 여러 발). 피해는 대상별로 합쳐서 한 번만 보냄 (네트워크 메시지 절약)
        Vector3 firstPoint = Vector3.zero;
        Health firstHealth = null;
        var fxPoints = new Vector3[pelletCount];
        var fxNormals = new Vector3[pelletCount];
        var fxHits = new byte[pelletCount];
        damageBuffer.Clear();
        for (int i = 0; i < pelletCount; i++)
        {
            Health h = Shoot(out Vector3 point, out Vector3 normal, out byte surface);
            fxPoints[i] = point;
            fxNormals[i] = normal;
            fxHits[i] = surface;
            if (i == 0) { firstPoint = point; firstHealth = h; }
            if (h != null) damageBuffer[h] = (damageBuffer.TryGetValue(h, out float d) ? d : 0f) + damage;
        }
        foreach (var kv in damageBuffer)
            if (kv.Key != null) kv.Key.TakeDamage(kv.Value, owner.gameObject);
        damageBuffer.Clear();

        // 총소리 → 소나 / 마네킹 기록
        Vector3 shooterPos = owner.position;
        if (sonarRadius > 0f)
            SoundPulseManager.Emit(shooterPos, sonarRadius, owner.gameObject, sonarExpand, sonarHold, sonarFade);
        if (recordShotForMannequins && mannequinSpawner != null)
            mannequinSpawner.RecordShot(shooterPos, owner.forward);

        OnFired?.Invoke(firstPoint, firstHealth);
        OnShotFx?.Invoke(this, fxPoints, fxNormals, fxHits);
        AnyShotImpact?.Invoke(fxPoints);
        return true;
    }

    private void ConsumeAmmo()
    {
        switch (ammoMode)
        {
            case AmmoMode.Magazine:
                Ammo--;
                break;

            case AmmoMode.Cooldown:
                Ammo--;
                if (Ammo <= 0) StartCharge(chargeTime, false);
                break;

            case AmmoMode.Battery:
                Energy = Mathf.Max(0f, Energy - shotCost);
                if (Energy < shotCost) IsReloading = true;   // 과열 → 다 찰 때까지 사용 불가
                break;
        }
    }

    private Health Shoot(out Vector3 hitPoint, out Vector3 hitNormal, out byte surface)
    {
        Transform cam = aimCamera != null ? aimCamera.transform : transform;
        Vector3 origin = cam.position;
        Vector3 dir = cam.forward;

        float spread = aiming ? spreadAngle * adsSpreadMultiplier : spreadAngle;
        if (spread > 0f)
        {
            // 원뿔 안에서 랜덤 방향
            Vector2 r = UnityEngine.Random.insideUnitCircle * Mathf.Tan(spread * Mathf.Deg2Rad);
            dir = (cam.forward + cam.right * r.x + cam.up * r.y).normalized;
        }

        hitPoint = origin + dir * range;
        int count = Physics.RaycastNonAlloc(origin, dir, hits, range, hitMask, QueryTriggerInteraction.Ignore);

        // 가장 가까운 것부터, 내 몸은 건너뜀
        float best = float.MaxValue;
        Collider bestCol = null;
        Vector3 bestNormal = Vector3.up;
        for (int i = 0; i < count; i++)
        {
            var h = hits[i];
            if (h.collider.transform.root == owner) continue;
            if (h.distance < best)
            {
                best = h.distance;
                bestCol = h.collider;
                hitPoint = h.point;
                bestNormal = h.normal;
            }
        }

        hitNormal = bestNormal;
        bool didHit = bestCol != null;
        surface = didHit ? (byte)(1 + (int)SurfaceType.Resolve(bestCol)) : (byte)0;

        // 착탄 이펙트
        SpawnImpact(hitPoint, bestNormal, surface);

        if (debugDrawRay)
            Debug.DrawLine(origin, hitPoint, bestCol != null ? Color.red : Color.yellow, 1f);

        return bestCol != null ? bestCol.GetComponentInParent<Health>() : null;
    }

    /// <summary>
    /// 다른 플레이어가 쏜 총의 연출만 재생 (판정/탄약/반동 없음). PlayerNetworkCombat이 호출.
    /// 소리는 쏜 사람 위치에서 3D로 들림 (PlayerNetworkSetup이 다른 플레이어 AudioSource를 3D로 설정)
    /// </summary>
    public void PlayRemoteShot(Vector3[] points, Vector3[] normals, byte[] surfaces)
    {
        AnyShotImpact?.Invoke(points);

        // 총구 화염: 다른 사람 화면에서는 3인칭 총구 (관전 중이면 1인칭 총구)
        if (ImpactEffects.Instance != null)
        {
            Transform follow = RemoteMuzzle(out Vector3 from);
            Vector3 to = points != null && points.Length > 0 ? points[0] : from + owner.forward;
            ImpactEffects.Instance.PlayMuzzleFlash(from, to - from, follow);
        }

        if (fireClip != null && audioSource != null && audioSource.isActiveAndEnabled)
        {
            audioSource.spatialBlend = 1f;   // 원격 총소리는 3D (Awake가 0으로 되돌릴 수 있어 매번 지정)
            audioSource.pitch = firePitch;
            audioSource.PlayOneShot(fireClip, volume);
        }

        if (points == null) return;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 n = normals != null && i < normals.Length ? normals[i] : Vector3.up;
            byte sf = surfaces != null && i < surfaces.Length ? surfaces[i] : (byte)0;
            SpawnImpact(points[i], n, sf);
        }
    }

    /// <summary>총구 위치 (Muzzle Point가 없으면 Flash Local Offset)</summary>
    public Vector3 MuzzlePosition => muzzlePoint != null ? muzzlePoint.position : transform.TransformPoint(flashLocalOffset);

    /// <summary>다른 사람이 쏜 총의 총구: 3인칭 총의 MuzzlePoint (관전 중이면 1인칭 총구). 화염이 따라갈 Transform 반환</summary>
    private Transform RemoteMuzzle(out Vector3 position)
    {
        var net = owner != null ? owner.GetComponent<PlayerNetworkSetup>() : null;
        if (net == null || !net.IsSpectated)
        {
            var tpw = owner != null ? owner.GetComponentInChildren<ThirdPersonWeapon>() : null;
            var m = tpw != null ? tpw.CurrentMuzzle : null;
            if (m != null) { position = m.position; return m; }
        }
        position = MuzzlePosition;
        return muzzlePoint != null ? muzzlePoint : transform;
    }

    /// <param name="surface">0 = 안 맞음, 1 + SurfaceKind</param>
    private void SpawnImpact(Vector3 point, Vector3 normal, byte surface)
    {
        // 표면별 착탄 이펙트 + 탄흔 (씬의 ImpactEffects). 안 맞았으면 없음
        if (surface == 0 || ImpactEffects.Instance == null) return;
        ImpactEffects.Instance.PlayImpact(point, normal, (SurfaceKind)(surface - 1));
    }

    // ───────── 재장전 / 충전 ─────────

    /// <summary>재장전 시도 (Magazine: 탄창 교체, Cooldown: 남은 탄 버리고 충전, Battery: 없음)</summary>
    public bool TryReload()
    {
        if (IsReloading) return false;

        switch (ammoMode)
        {
            case AmmoMode.Magazine:
                if (Ammo >= magazineSize) return false;
                StartCharge(reloadTime, true);
                return true;

            case AmmoMode.Cooldown:
                if (Ammo >= shotsPerCharge) return false;
                StartCharge(chargeTime, false);
                return true;

            default:
                return false;   // Battery는 자동 충전
        }
    }

    private void StartCharge(float duration, bool playReloadAnim)
    {
        IsReloading = true;
        reloadEndTime = Time.time + duration;

        if (playReloadAnim && gunAnimator != null && gunAnimator.runtimeAnimatorController != null)
            gunAnimator.Play(reloadHash, 0, 0f);
        if (reloadClip != null)
        {
            audioSource.pitch = 1f;
            audioSource.PlayOneShot(reloadClip, volume);
        }

        OnReloadStarted?.Invoke();
    }

    // ───────── 임시 HUD ─────────

    private void OnGUI()
    {
        if (!isLocal) return;

        if (showCrosshair && adsT > 0.5f)   // 조준점은 우클릭 조준 중에만 표시 (평소엔 없음)
        {
            if (dot == null)
            {
                dot = new Texture2D(1, 1);
                dot.SetPixel(0, 0, Color.white);
                dot.Apply();
            }
            var c = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - 2f, Screen.height * 0.5f - 2f, 4f, 4f), dot);
            GUI.color = c;
        }

        if (!showHud) return;

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
        }

        string text;
        bool warn;
        switch (ammoMode)
        {
            case AmmoMode.Cooldown:
                float left = Mathf.Max(0f, reloadEndTime - Time.time);
                text = IsReloading ? $"CHARGING {left:0.0}s" : (shotsPerCharge > 1 ? $"READY {Ammo}" : "READY");
                warn = IsReloading;
                break;

            case AmmoMode.Battery:
                int pct = Mathf.RoundToInt(Energy / batteryCapacity * 100f);
                text = IsReloading ? $"OVERHEAT {pct}%" : $"ENERGY {pct}%";
                warn = IsReloading || Energy < shotCost;
                break;

            default:
                text = IsReloading ? "RELOADING..." : $"{Ammo} / {magazineSize}";
                warn = IsReloading || Ammo == 0;
                break;
        }

        hudStyle.normal.textColor = warn ? new Color(1f, 0.6f, 0.4f) : new Color(0.85f, 0.9f, 1f);
        GUI.Label(new Rect(Screen.width - 424, Screen.height - 60, 400, 30), $"{weaponName}   {text}", hudStyle);
    }
}
