using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 네트워크 플레이어 초기화. Player_FPS 프리팹 루트(NetworkObject 있는 곳)에 붙인다.
///
///  [내 플레이어 (Owner)]  Player 태그 유지, PlayerInput 켜기, 카메라 연결, RoundManager에 등록
///  [다른 플레이어]        태그 변경, 입력/이동/스킬 끄기, 1인칭 총(뷰모델) 숨기기
///  [오프라인]             Warehouse 씬을 직접 실행한 경우 → 내 플레이어로 처리
///
/// 프리팹 준비:
///  - PlayerInput 컴포넌트는 체크 해제 (다른 플레이어가 내 키보드를 잡아가지 않게)
///  - WeaponHolder 오브젝트는 비활성 (GunWeapon/WeaponSwitcher가 Awake에서 태그로 "내 것"인지 판단하므로
///    태그를 정한 다음에 켜야 함)
///
/// 관전: 내 플레이어에 SpectatorCamera를 자동으로 붙임 (죽으면 살아 있는 플레이어 시점으로 전환)
/// </summary>
[DisallowMultipleComponent]
public class PlayerNetworkSetup : NetworkBehaviour
{
    [Tooltip("1인칭 총 묶음 (프리팹에서 비활성으로 둘 것). 비우면 이름으로 자동 검색")]
    [SerializeField] private GameObject weaponHolder;
    [Tooltip("카메라가 따라갈 지점. 비우면 PlayerCameraRoot 자동 검색")]
    [SerializeField] private Transform cameraRoot;

    [Header("다른 플레이어")]
    [Tooltip("다른 플레이어에게 붙일 태그 (Tags에 있는 이름이어야 함)")]
    [SerializeField] private string remoteTag = "Untagged";
    [Tooltip("다른 플레이어의 발소리도 주변을 드러낼지")]
    [SerializeField] private bool remoteFootsteps = false;
    [Tooltip("다른 플레이어 총소리가 들리는 최대 거리 (m)")]
    [SerializeField, Min(5f)] private float remoteShotHearDistance = 40f;

    private const string LocalTag = "Player";

    /// <summary>이 컴퓨터의 내 플레이어 (없으면 null)</summary>
    public static PlayerNetworkSetup Local { get; private set; }
    /// <summary>내 플레이어가 준비됐을 때</summary>
    public static event Action<PlayerNetworkSetup> LocalReady;

    private static readonly List<PlayerNetworkSetup> all = new List<PlayerNetworkSetup>();
    /// <summary>현재 씬의 모든 플레이어 (나 포함)</summary>
    public static IReadOnlyList<PlayerNetworkSetup> All => all;

    /// <summary>카메라가 따라가는 머리 위치 (관전 시 다른 사람 시점으로 사용)</summary>
    public Transform CameraRoot => cameraRoot;
    /// <summary>지금 내가 이 플레이어를 관전 중인지</summary>
    public bool IsSpectated { get; private set; }

    /// <summary>내 플레이어인지 (오프라인 포함). NGO의 IsLocalPlayer는 오프라인에서 false라 따로 둠</summary>
    public bool IsMine { get; private set; }

    private bool configured;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        if (weaponHolder == null)
        {
            var t = FindChild(transform, "WeaponHolder");
            if (t != null) weaponHolder = t.gameObject;
        }
        if (cameraRoot == null) cameraRoot = FindChild(transform, "PlayerCameraRoot");
        if (!all.Contains(this)) all.Add(this);
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[PlayerNetworkSetup] 스폰: client {OwnerClientId} ({(IsOwner ? "내 플레이어" : "다른 플레이어")})", this);
        if (IsOwner) SetupLocal();
        else SetupRemote();
    }

    private void Start()
    {
        // 네트워크 없이 실행 (Warehouse 씬 직접 Play) → 내 플레이어로
        if (!configured && !IsSpawned) SetupLocal();
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
    }

    public override void OnDestroy()
    {
        all.Remove(this);
        if (Local == this) Local = null;
        base.OnDestroy();
    }

    // ───────── 내 플레이어 ─────────

    private void SetupLocal()
    {
        configured = true;
        IsMine = true;
        gameObject.tag = LocalTag;
        gameObject.name = "Player_FPS (Local)";

        var pi = GetComponent<PlayerInput>();
        if (pi != null) pi.enabled = true;

        // 씬의 PlayerFollowCamera가 내 머리를 따라가게
        var vcam = FindAnyObjectByType<CinemachineCamera>();
        if (vcam != null && cameraRoot != null) vcam.Follow = cameraRoot;
        else Debug.LogWarning("[PlayerNetworkSetup] CinemachineCamera 또는 PlayerCameraRoot를 찾지 못했습니다.", this);

        // 태그를 정한 뒤에 총을 켜야 GunWeapon/WeaponSwitcher가 "내 것"으로 인식
        if (weaponHolder != null) weaponHolder.SetActive(true);

        // 죽으면 다른 플레이어 관전
        if (GetComponent<SpectatorCamera>() == null) gameObject.AddComponent<SpectatorCamera>();

        Local = this;
        if (RoundManager.Instance != null) RoundManager.Instance.SetLocalPlayer(transform);
        LocalReady?.Invoke(this);
    }

    // ───────── 다른 플레이어 ─────────

    private void SetupRemote()
    {
        configured = true;
        IsMine = false;
        gameObject.tag = remoteTag;
        gameObject.name = $"Player_FPS (Remote {OwnerClientId})";

        // 입력 / 이동 (위치는 NetworkTransform이 맞춰줌)
        Disable(GetComponent<PlayerInput>());
        Disable(GetComponent<StarterAssets.StarterAssetsInputs>());
        Disable(GetComponent<StarterAssets.FirstPersonController>());

        // 내 컴퓨터에서만 돌아야 하는 것들
        Disable(GetComponent<SonarSkill>());
        Disable(GetComponent<MouseSensitivityApplier>());
        if (!remoteFootsteps) Disable(GetComponent<FootstepPulse>());

        foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string n = mb.GetType().Name;
            // 몸 애니메이션은 NetworkAnimator가 받아오므로 로컬 계산은 끔
            if (n == "BodyAnimatorDriver" || n == "BasicRigidBodyPush") mb.enabled = false;
        }

        // 1인칭 총: 무기 정보와 총소리는 필요하니 켜되(3인칭 무기 표시 / 원격 사격 연출용), 화면에는 안 보이게
        if (weaponHolder != null)
        {
            weaponHolder.SetActive(true);
            foreach (var r in weaponHolder.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;
            foreach (var l in weaponHolder.GetComponentsInChildren<Light>(true)) l.enabled = false;
            // 총소리: 쏜 사람 위치에서 3D로 (멀수록 작게)
            foreach (var a in weaponHolder.GetComponentsInChildren<AudioSource>(true))
            {
                a.spatialBlend = 1f;
                a.rolloffMode = AudioRolloffMode.Linear;
                a.minDistance = 2f;
                a.maxDistance = remoteShotHearDistance;
            }
        }
    }

    // ───────── 관전 ─────────

    /// <summary>
    /// 내가 이 (다른) 플레이어를 관전하기 시작/끝낼 때 SpectatorCamera가 호출.
    /// 관전 중에는 그 사람 머리를 숨기고 1인칭 총을 보여줌 → 그 사람 화면처럼 보임
    /// </summary>
    public void SetSpectated(bool on)
    {
        if (IsMine || IsSpectated == on) return;
        IsSpectated = on;

        var body = GetComponent<LocalBodyView>();
        if (body != null) body.SetFirstPerson(on);

        if (weaponHolder != null)
            foreach (var r in weaponHolder.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !on;
    }

    // ───────── 유틸 ─────────

    private static void Disable(Behaviour b)
    {
        if (b != null) b.enabled = false;
    }

    private static Transform FindChild(Transform root, string childName)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }
}
