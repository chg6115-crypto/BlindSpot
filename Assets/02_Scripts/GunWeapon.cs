using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
///
/// [네트워크 5단계]
///  - 피해는 Health.TakeDamage로 보내면 네트워크 플레이어는 호스트가 처리 (산탄은 대상별로 합쳐 한 번만 전송)
///  - 발사 결과(맞은 지점들)를 OnShotFx로 알림 → PlayerNetworkCombat이 다른 사람에게 전달
///  - 다른 사람 화면에서는 PlayRemoteShot()으로 소리 + 빔/피격 이펙트만 재생
/// </summary>
public class GunWeapon : MonoBehaviour
{
    public enum AmmoMode { Magazine, Cooldown, Battery }
    public enum ImpactSpawnMode { HitPoint, FromMuzzle }

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

    [Header("피격 이펙트")]
    [Tooltip("이펙트 생성 방식\n HitPoint: 맞은 지점에서 터짐 (ZapBeam이 붙어 있으면 맞은 지점→총구 빔)\n FromMuzzle: 총구에서 맞은 지점 방향으로 생성 (날아가는 탄 이펙트, 예: PlasmaShot)")]
    [SerializeField] private ImpactSpawnMode impactSpawnMode = ImpactSpawnMode.HitPoint;
    [Tooltip("생성할 이펙트 프리팹 (비우면 생성 안 함)")]
    [SerializeField] private GameObject impactPrefab;
    [Tooltip("생성 후 몇 초 뒤 삭제 (그을음이 남아 있는 시간보다 길게)")]
    [SerializeField, Min(0.1f)] private float impactLifetime = 3f;
    [Tooltip("맞은 면 방향으로 이펙트를 세움 (벽에 맞으면 벽에서 튀어나오게)")]
    [SerializeField] private bool alignToSurface = true;
    [Tooltip("이펙트를 표면에서 띄우는 거리 (벽 속에 묻히지 않게)")]
    [SerializeField, Min(0f)] private float impactSurfaceOffset = 0.01f;
    [Tooltip("총구 위치 (빔이 시작되는 곳). 비우면 Flash Local Offset 위치 사용")]
    [SerializeField] private Transform muzzlePoint;
    [Tooltip("아무것도 안 맞았을 때도 이펙트를 보여줄지 (ZapBeam 빔 / FromMuzzle 탄)")]
    [SerializeField] private bool beamOnMiss = true;
    [Tooltip("FromMuzzle 탄 이펙트를 강제로 지우는 시간 (초). 탄이 멀리 날아가니 Impact Lifetime보다 길게")]
    [SerializeField, Min(0.1f)] private float projectileLifetime = 6f;

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

    [Header("임시 HUD")]
    [SerializeField] private bool showHud = true;
    [SerializeField] private bool showCrosshair = true;
    [SerializeField] private bool debugDrawRay = true;

    /// <summary>발사했을 때 (첫 번째 맞은 지점, 맞은 Health 또는 null)</summary>
    public event Action<Vector3, Health> OnFired;
    public event Action OnReloadStarted;
    /// <summary>발사 연출 정보 (총, 펠릿별 맞은 지점, 면 방향, 명중 여부) - 네트워크 전달용</summary>
    public event Action<GunWeapon, Vector3[], Vector3[], bool[]> OnShotFx;
    /// <summary>어떤 총이든 총알이 닿은 지점들 (내가 쏜 것 + 다른 사람 화면 재생 모두). 자동차 경보 등 환경 반응용</summary>
    public static event Action<Vector3[]> AnyShotImpact;

    public string WeaponName => weaponName;
    public float BarrelLength => barrelLength;
    public AmmoMode Mode => ammoMode;
    public int Ammo { get; private set; }
    public int MagazineSize => magazineSize;
    public float Energy { get; private set; }
    public bool IsReloading { get; private set; }   // Magazine: 재장전 / Cooldown: 충전 / Battery: 과열

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

    private GUIStyle hudStyle;
    private Texture2D dot;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        owner = transform.root;
        isLocal = owner.CompareTag("Player");
        ownerHealth = owner.GetComponentInChildren<Health>();
        ownerController = owner.GetComponent<StarterAssets.FirstPersonController>();

        wallRetract = GetComponentInParent<WeaponWallRetract>(true);
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

        if (!isLocal || !CanAct()) return;

        var mouse = Mouse.current;
        var kb = Keyboard.current;

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

        Vector3 newPos = new Vector3(0f, 0f, -recoilPos);
        Quaternion newRot = Quaternion.Euler(-recoilRot, 0f, 0f);

        recoilTarget.localPosition += newPos - appliedPos;
        // 부모(카메라) 기준으로 회전 → 무기 모델 방향과 상관없이 항상 총구가 위로 들림
        recoilTarget.localRotation = newRot * Quaternion.Inverse(appliedRot) * recoilTarget.localRotation;

        appliedPos = newPos;
        appliedRot = newRot;
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

        // 히트스캔 (산탄이면 여러 발). 피해는 대상별로 합쳐서 한 번만 보냄 (네트워크 메시지 절약)
        Vector3 firstPoint = Vector3.zero;
        Health firstHealth = null;
        var fxPoints = new Vector3[pelletCount];
        var fxNormals = new Vector3[pelletCount];
        var fxHits = new bool[pelletCount];
        damageBuffer.Clear();
        for (int i = 0; i < pelletCount; i++)
        {
            Health h = Shoot(out Vector3 point, out Vector3 normal, out bool didHit);
            fxPoints[i] = point;
            fxNormals[i] = normal;
            fxHits[i] = didHit;
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

    private Health Shoot(out Vector3 hitPoint, out Vector3 hitNormal, out bool didHit)
    {
        Transform cam = aimCamera != null ? aimCamera.transform : transform;
        Vector3 origin = cam.position;
        Vector3 dir = cam.forward;

        if (spreadAngle > 0f)
        {
            // 원뿔 안에서 랜덤 방향
            Vector2 r = UnityEngine.Random.insideUnitCircle * Mathf.Tan(spreadAngle * Mathf.Deg2Rad);
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
        didHit = bestCol != null;

        // 피격 이펙트 / 빔
        SpawnImpact(hitPoint, bestNormal, didHit);

        if (debugDrawRay)
            Debug.DrawLine(origin, hitPoint, bestCol != null ? Color.red : Color.yellow, 1f);

        return bestCol != null ? bestCol.GetComponentInParent<Health>() : null;
    }

    /// <summary>
    /// 다른 플레이어가 쏜 총의 연출만 재생 (판정/탄약/반동 없음). PlayerNetworkCombat이 호출.
    /// 소리는 쏜 사람 위치에서 3D로 들림 (PlayerNetworkSetup이 다른 플레이어 AudioSource를 3D로 설정)
    /// </summary>
    public void PlayRemoteShot(Vector3[] points, Vector3[] normals, bool[] hitFlags)
    {
        AnyShotImpact?.Invoke(points);

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
            bool h = hitFlags != null && i < hitFlags.Length && hitFlags[i];
            SpawnImpact(points[i], n, h);
        }
    }

    /// <summary>총구 위치 (Muzzle Point가 없으면 Flash Local Offset)</summary>
    public Vector3 MuzzlePosition => muzzlePoint != null ? muzzlePoint.position : transform.TransformPoint(flashLocalOffset);

    /// <summary>
    /// 날아가는 탄 이펙트가 사거리(또는 맞은 지점)까지만 날아가게 루트 파티클 수명을 맞춤.
    /// 수명 = 거리 / 탄 속도. 맞았을 때는 충돌 이펙트가 터지도록 조금 여유를 줌.
    /// </summary>
    private void LimitProjectileRange(GameObject shot, float distance, bool hit)
    {
        var ps = shot.GetComponent<ParticleSystem>();
        if (ps == null) return;

        var main = ps.main;
        float speed = main.startSpeed.constantMax;
        if (speed < 0.01f) return;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        main.startLifetime = distance / speed + (hit ? 0.05f : 0f);
        ps.Play(true);
    }

    private void SpawnImpact(Vector3 point, Vector3 normal, bool hit)
    {
        if (impactPrefab == null) return;

        // 총구에서 날아가는 탄 이펙트 (PlasmaShot 등): 총구에서 맞은 지점을 바라보게 생성
        if (impactSpawnMode == ImpactSpawnMode.FromMuzzle)
        {
            if (!hit && !beamOnMiss) return;
            Vector3 from = MuzzlePosition;
            Vector3 dir = point - from;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            GameObject shot = Instantiate(impactPrefab, from, Quaternion.LookRotation(dir.normalized));
            LimitProjectileRange(shot, dir.magnitude, hit);
            Destroy(shot, projectileLifetime);
            return;
        }

        // ZapBeam이 붙은 프리팹: 총구 → 맞은 지점 빔
        var beamOnPrefab = impactPrefab.GetComponent<ZapBeam>();
        if (beamOnPrefab != null)
        {
            if (!hit && !beamOnMiss) return;
            GameObject beamFx = Instantiate(impactPrefab, point, Quaternion.identity);
            Vector3 end = hit ? point + normal * impactSurfaceOffset : point;
            beamFx.GetComponent<ZapBeam>().Fire(MuzzlePosition, end, hit);
            Destroy(beamFx, impactLifetime);
            return;
        }

        // 일반 피격 이펙트: 맞았을 때만, 맞은 면 방향으로
        if (!hit) return;
        Quaternion rot = alignToSurface
            ? Quaternion.FromToRotation(Vector3.up, normal)
            : Quaternion.identity;

        GameObject fx = Instantiate(impactPrefab, point + normal * impactSurfaceOffset, rot);
        Destroy(fx, impactLifetime);
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

        if (showCrosshair)
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
