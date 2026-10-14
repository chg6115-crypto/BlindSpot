using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 총 발사 / 재장전 (히트스캔).
/// 총 오브젝트(Animator가 있는 쪽, 예: Pistol_Compact_East.Rig)에 붙인다.
///
///  - 좌클릭: 발사 (반자동, 한 번 클릭 = 한 발)
///  - R: 재장전 (탄창이 비면 클릭 시 자동 재장전)
///  - 화면 중앙(카메라)에서 레이캐스트 → 맞은 대상의 Health.TakeDamage
///  - 총소리 → SoundPulseManager 소나 펄스 + MannequinSpawner.RecordShot
///  - Animator의 "Fire" / "Reload" 상태 재생 (총구 섬광 라이트는 Fire 애니메이션이 처리)
///  - 로컬 플레이어(루트가 Player 태그)에서만 입력을 받음
///  - FirstPersonController가 꺼져 있으면(카운트다운/사망 Freeze) 발사 불가
/// </summary>
public class GunWeapon : MonoBehaviour
{
    [Header("스펙")]
    [SerializeField, Min(0f)] private float damage = 34f;
    [Tooltip("발사 간격 (초)")]
    [SerializeField, Min(0.01f)] private float fireInterval = 0.25f;
    [Tooltip("누르고 있으면 연사")]
    [SerializeField] private bool automatic = false;
    [SerializeField, Min(1)] private int magazineSize = 7;
    [Tooltip("재장전 시간 (초) - Reload 애니메이션 길이에 맞춤")]
    [SerializeField, Min(0.1f)] private float reloadTime = 2.35f;
    [SerializeField, Min(1f)] private float range = 100f;
    [Tooltip("맞출 레이어 (Default, Character, Ground, Obstacle)")]
    [SerializeField] private LayerMask hitMask = (1 << 0) | (1 << 6) | (1 << 7) | (1 << 8);

    [Header("소리 / 소나")]
    [SerializeField] private AudioClip fireClip;
    [SerializeField] private AudioClip reloadClip;
    [SerializeField] private AudioClip emptyClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [Tooltip("총소리 소나 반경 (0이면 소나 없음)")]
    [SerializeField, Min(0f)] private float sonarRadius = 20f;
    [SerializeField, Min(0.01f)] private float sonarExpand = 0.4f;
    [SerializeField, Min(0f)] private float sonarHold = 0.5f;
    [SerializeField, Min(0.01f)] private float sonarFade = 1.5f;
    [Tooltip("발사 위치를 다음 라운드 마네킹 배치용으로 기록")]
    [SerializeField] private bool recordShotForMannequins = true;

    [Header("애니메이터 상태 이름")]
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

    /// <summary>발사했을 때 (맞은 지점, 맞은 Health 또는 null)</summary>
    public event Action<Vector3, Health> OnFired;
    public event Action OnReloadStarted;

    public int Ammo { get; private set; }
    public int MagazineSize => magazineSize;
    public bool IsReloading { get; private set; }

    private Transform owner;              // 플레이어 루트
    private Health ownerHealth;
    private Behaviour ownerController;    // FirstPersonController (Freeze 판정용)
    private MannequinSpawner mannequinSpawner;
    private bool isLocal;
    private float nextFireTime;
    private float reloadEndTime;
    private int fireHash, reloadHash;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private GUIStyle hudStyle;
    private Texture2D dot;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        owner = transform.root;
        isLocal = owner.CompareTag("Player");
        ownerHealth = owner.GetComponentInChildren<Health>();
        ownerController = owner.GetComponent<StarterAssets.FirstPersonController>();

        if (gunAnimator == null) gunAnimator = GetComponent<Animator>();
        if (audioSource == null)
        {
            audioSource = GetComponentInChildren<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;

        fireHash = Animator.StringToHash(fireState);
        reloadHash = Animator.StringToHash(reloadState);
        Ammo = magazineSize;
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

    private void OnDisable()
    {
        // 무기 교체로 꺼지면 재장전 취소
        IsReloading = false;
    }

    private void HandleRoundStart(int round)
    {
        Ammo = magazineSize;
        IsReloading = false;
    }

    // ───────── 입력 ─────────

    private void Update()
    {
        // 재장전 완료
        if (IsReloading && Time.time >= reloadEndTime)
        {
            IsReloading = false;
            Ammo = magazineSize;
        }

        if (!isLocal || !CanAct()) return;

        var mouse = Mouse.current;
        var kb = Keyboard.current;

        bool firePressed = mouse != null &&
            (automatic ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame);
        bool reloadPressed = kb != null && kb.rKey.wasPressedThisFrame;

        if (reloadPressed) TryReload();
        else if (firePressed) TryFire();
    }

    private bool CanAct()
    {
        if (ownerController != null && !ownerController.enabled) return false;   // Freeze 중
        if (ownerHealth != null && ownerHealth.IsDead) return false;
        if (Cursor.lockState != CursorLockMode.Locked) return false;           // 커서 풀린 상태(메뉴 등)
        return true;
    }

    // ───────── 발사 ─────────

    /// <summary>발사 시도. 성공하면 true</summary>
    public bool TryFire()
    {
        if (IsReloading || Time.time < nextFireTime) return false;

        if (Ammo <= 0)
        {
            if (emptyClip != null) audioSource.PlayOneShot(emptyClip, volume);
            TryReload();
            return false;
        }

        Ammo--;
        nextFireTime = Time.time + fireInterval;

        if (gunAnimator != null) gunAnimator.Play(fireHash, 0, 0f);
        if (fireClip != null) audioSource.PlayOneShot(fireClip, volume);

        // 히트스캔
        Vector3 hitPoint;
        Health hitHealth = Shoot(out hitPoint);
        if (hitHealth != null) hitHealth.TakeDamage(damage, owner.gameObject);

        // 총소리 → 소나 / 마네킹 기록
        Vector3 shooterPos = owner.position;
        if (sonarRadius > 0f)
            SoundPulseManager.Emit(shooterPos, sonarRadius, owner.gameObject, sonarExpand, sonarHold, sonarFade);
        if (recordShotForMannequins && mannequinSpawner != null)
            mannequinSpawner.RecordShot(shooterPos, owner.forward);

        OnFired?.Invoke(hitPoint, hitHealth);
        return true;
    }

    private Health Shoot(out Vector3 hitPoint)
    {
        Transform cam = aimCamera != null ? aimCamera.transform : transform;
        Vector3 origin = cam.position;
        Vector3 dir = cam.forward;
        hitPoint = origin + dir * range;

        int count = Physics.RaycastNonAlloc(origin, dir, hits, range, hitMask, QueryTriggerInteraction.Ignore);

        // 가장 가까운 것부터, 내 몸은 건너뜀
        float best = float.MaxValue;
        Collider bestCol = null;
        for (int i = 0; i < count; i++)
        {
            var h = hits[i];
            if (h.collider.transform.root == owner) continue;
            if (h.distance < best)
            {
                best = h.distance;
                bestCol = h.collider;
                hitPoint = h.point;
            }
        }

        if (debugDrawRay)
            Debug.DrawLine(origin, hitPoint, bestCol != null ? Color.red : Color.yellow, 1f);

        return bestCol != null ? bestCol.GetComponentInParent<Health>() : null;
    }

    // ───────── 재장전 ─────────

    /// <summary>재장전 시도. 이미 꽉 찼거나 재장전 중이면 false</summary>
    public bool TryReload()
    {
        if (IsReloading || Ammo >= magazineSize) return false;

        IsReloading = true;
        reloadEndTime = Time.time + reloadTime;

        if (gunAnimator != null) gunAnimator.Play(reloadHash, 0, 0f);
        if (reloadClip != null) audioSource.PlayOneShot(reloadClip, volume);

        OnReloadStarted?.Invoke();
        return true;
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

        string text = IsReloading ? "RELOADING..." : $"{Ammo} / {magazineSize}";
        hudStyle.normal.textColor = (IsReloading || Ammo == 0) ? new Color(1f, 0.6f, 0.4f) : new Color(0.85f, 0.9f, 1f);
        GUI.Label(new Rect(Screen.width - 324, Screen.height - 60, 300, 30), text, hudStyle);
    }
}
