using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 네트워크 전투 동기화. Player_FPS 프리팹 루트(NetworkObject, Health 있는 곳)에 붙인다.
///
///  [체력]   호스트만 값을 바꿈 (NetworkVariable) → 모든 화면의 Health에 반영
///  [피격]   쏜 사람 화면에서 맞은 대상 판정 → 호스트에 피해 요청 (보낸 사람 = 공격자)
///  [사망]   호스트가 체력 0 판정 → 모두에게 킬 알림 → 각자 ScoreManager / RoundManager에 기록
///  [부활]   호스트의 라운드 시작 때 체력 회복 → 모두에게 반영
///  [무기]   내가 든 무기 번호 공유 → 상대 화면의 3인칭 몸에 같은 총
///  [사격]   내가 쏜 빔 / 피격 지점 공유 → 상대 화면에서도 번개 빔 + 총소리
///  [이름]   로비 닉네임 공유 → 점수판 / 승자 표시
///  [마네킹] 내가 맞힌 마네킹 번호 공유 → 모든 화면에서 같은 마네킹이 쓰러짐
///           호스트는 모든 플레이어의 발사 위치를 모아 다음 라운드 마네킹 배치에 사용
///
/// 라운드 진행은 RoundNetworkSync가 호스트 기준으로 맞춤.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class PlayerNetworkCombat : NetworkBehaviour
{
    public const ulong NoClient = ulong.MaxValue;

    // 접속 번호 → 플레이어 (킬 알림 때 공격자 찾기용)
    private static readonly Dictionary<ulong, PlayerNetworkCombat> byClient = new Dictionary<ulong, PlayerNetworkCombat>();
    public static bool TryGet(ulong clientId, out PlayerNetworkCombat player) => byClient.TryGetValue(clientId, out player);

    /// <summary>이 컴퓨터 점수판 id → 접속 번호 (없으면 NoClient)</summary>
    public static ulong ClientOfPlayerId(int playerId)
    {
        if (playerId < 0) return NoClient;
        foreach (var kv in byClient)
            if (kv.Value != null && kv.Value.health.PlayerId == playerId) return kv.Key;
        return NoClient;
    }

    /// <summary>접속 번호 → 이 컴퓨터 점수판 id (없으면 -1)</summary>
    public static int PlayerIdOfClient(ulong clientId)
    {
        return clientId != NoClient && TryGet(clientId, out var p) ? p.health.PlayerId : -1;
    }

    private readonly NetworkVariable<float> hp = new NetworkVariable<float>(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> weaponIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public string DisplayName => playerName.Value.IsEmpty ? $"PLAYER {OwnerClientId + 1}" : playerName.Value.ToString();

    private Health health;
    private WeaponSwitcher switcher;
    private bool hooked;
    private bool scoreRegistered;
    private GameObject lastLocalAttacker;   // 호스트에서만: 네트워크 플레이어가 아닌 공격자 (디버그 봇 등)
    private MannequinSpawner mannequinSpawner;
    private bool mannequinHooked;

    // ───────── 스폰 ─────────

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    public override void OnNetworkSpawn()
    {
        byClient[OwnerClientId] = this;

        hp.OnValueChanged += HandleHpChanged;
        weaponIndex.OnValueChanged += HandleWeaponChanged;
        playerName.OnValueChanged += HandleNameChanged;

        if (IsServer) hp.Value = health.Max;
        health.SetNetworkHealth(hp.Value, false);

        RegisterScore();

        if (IsOwner)
        {
            string nick = LobbyManager.LocalNickname;
            if (!string.IsNullOrEmpty(nick)) playerName.Value = new FixedString32Bytes(nick);
        }
    }

    private void Start()
    {
        if (!IsSpawned) return;

        // 무기 묶음(WeaponHolder)은 PlayerNetworkSetup이 스폰 때 켜므로 Start에서 연결
        switcher = GetComponentInChildren<WeaponSwitcher>(true);
        if (switcher != null)
        {
            if (IsOwner)
            {
                switcher.OnSwitched += HandleLocalSwitched;
                if (switcher.CurrentIndex >= 0) weaponIndex.Value = switcher.CurrentIndex;
            }
            else if (weaponIndex.Value >= 0)
            {
                switcher.Select(weaponIndex.Value, true);
            }

            // 내가 쏘면 다른 사람에게 연출 전달
            if (IsOwner)
            {
                for (int i = 0; i < switcher.Count; i++)
                {
                    var gun = switcher.GetWeapon(i);
                    if (gun != null) gun.OnShotFx += HandleLocalShot;
                }
            }
            hooked = true;
        }

        // 호스트: 라운드 시작마다 모든 플레이어 체력 회복
        if (IsServer && RoundManager.Instance != null)
            RoundManager.Instance.OnRoundStart += ServerHandleRoundStart;

        // 내가 맞힌 마네킹을 다른 사람에게 알림
        mannequinSpawner = FindAnyObjectByType<MannequinSpawner>();
        if (IsOwner)
        {
            Health.MannequinHitLocally += HandleLocalMannequinHit;
            mannequinHooked = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (byClient.TryGetValue(OwnerClientId, out var p) && p == this) byClient.Remove(OwnerClientId);

        hp.OnValueChanged -= HandleHpChanged;
        weaponIndex.OnValueChanged -= HandleWeaponChanged;
        playerName.OnValueChanged -= HandleNameChanged;

        if (hooked && switcher != null)
        {
            switcher.OnSwitched -= HandleLocalSwitched;
            for (int i = 0; i < switcher.Count; i++)
            {
                var gun = switcher.GetWeapon(i);
                if (gun != null) gun.OnShotFx -= HandleLocalShot;
            }
        }

        if (RoundManager.Instance != null)
            RoundManager.Instance.OnRoundStart -= ServerHandleRoundStart;

        if (mannequinHooked)
        {
            Health.MannequinHitLocally -= HandleLocalMannequinHit;
            mannequinHooked = false;
        }

        // 나간 플레이어는 생존자 / 점수판에서 제거 (내 항목은 ScoreManager 소유라 두기)
        if (scoreRegistered && !IsOwner)
        {
            if (RoundManager.Instance != null) RoundManager.Instance.RemoveParticipant(health.PlayerId);
            if (ScoreManager.Instance != null) ScoreManager.Instance.Unregister(health.PlayerId);
        }
    }

    private void RegisterScore()
    {
        var sm = ScoreManager.Instance;
        if (sm == null)
        {
            health.AssignNetworkPlayer(-1, IsOwner);
            return;
        }

        int id = IsOwner ? sm.LocalPlayerId : sm.Register(DisplayName);
        health.AssignNetworkPlayer(id, IsOwner);
        scoreRegistered = true;

        if (IsOwner) sm.SetName(id, DisplayName);
        else if (RoundManager.Instance != null) RoundManager.Instance.RegisterParticipant(id);
    }

    // ───────── 피해 ─────────

    /// <summary>
    /// Health.TakeDamage에서 호출 (맞은 쪽 오브젝트, 쏜 사람 컴퓨터에서 실행됨).
    /// 호스트면 바로 적용, 아니면 호스트에 요청.
    /// </summary>
    public void RequestDamage(float amount, GameObject attacker)
    {
        bool byPlayer = attacker != null && attacker.GetComponentInParent<PlayerNetworkCombat>() != null;

        if (IsServer)
        {
            ulong attackerId = byPlayer ? NetworkManager.LocalClientId : NoClient;
            lastLocalAttacker = byPlayer ? null : attacker;
            ServerApplyDamage(amount, attackerId);
        }
        else
        {
            DamageRpc(amount, byPlayer);
        }
    }

    [Rpc(SendTo.Server)]
    private void DamageRpc(float amount, bool byPlayer, RpcParams rpcParams = default)
    {
        // 공격자 = 실제로 이 메시지를 보낸 사람 (조작 불가)
        ulong sender = rpcParams.Receive.SenderClientId;
        lastLocalAttacker = null;
        ServerApplyDamage(amount, byPlayer ? sender : NoClient);
    }

    private void ServerApplyDamage(float amount, ulong attackerId)
    {
        if (amount <= 0f || hp.Value <= 0f) return;

        // 라운드 진행 중에만 (호스트 라운드 기준)
        var rm = RoundManager.Instance;
        if (rm != null && rm.CurrentState != RoundManager.State.Playing) return;

        // 자기 자신(트랩 등)은 킬로 치지 않음
        if (attackerId == OwnerClientId) attackerId = NoClient;

        hp.Value = Mathf.Max(0f, hp.Value - amount);
        if (hp.Value <= 0f) KillRpc(attackerId);
    }

    /// <summary>호스트 → 모두: 이 플레이어가 죽음</summary>
    [Rpc(SendTo.Everyone)]
    private void KillRpc(ulong attackerId)
    {
        GameObject killerObj = null;
        int killerId = -1;

        if (attackerId != NoClient && TryGet(attackerId, out var killer))
        {
            killerObj = killer.gameObject;
            killerId = killer.health.PlayerId;
        }
        else if (lastLocalAttacker != null)
        {
            // 호스트 화면에서만: 디버그 봇 등 네트워크 플레이어가 아닌 공격자
            killerObj = lastLocalAttacker;
            var kh = lastLocalAttacker.GetComponentInParent<Health>();
            if (kh != null) killerId = kh.PlayerId;
        }
        lastLocalAttacker = null;

        health.NetDie(killerObj);

        int victimId = health.PlayerId;
        if (victimId < 0) return;
        if (RoundManager.Instance != null) RoundManager.Instance.ReportKill(killerId, victimId);
        else if (ScoreManager.Instance != null) ScoreManager.Instance.AddKill(killerId, victimId);
    }

    // ───────── 부활 ─────────

    private void ServerHandleRoundStart(int round)
    {
        if (IsServer) hp.Value = health.Max;
    }

    private void HandleHpChanged(float oldValue, float newValue)
    {
        health.SetNetworkHealth(newValue, true);
        if (oldValue <= 0f && newValue > 0f) health.NetRevive();
    }

    // ───────── 무기 ─────────

    private void HandleLocalSwitched(int index, GunWeapon weapon)
    {
        if (IsOwner && IsSpawned) weaponIndex.Value = index;
    }

    private void HandleWeaponChanged(int oldValue, int newValue)
    {
        if (IsOwner || switcher == null || newValue < 0) return;
        if (switcher.CurrentIndex != newValue) switcher.Select(newValue, true);
    }

    // ───────── 사격 연출 ─────────

    private void HandleLocalShot(GunWeapon gun, Vector3[] points, Vector3[] normals, byte[] hits)
    {
        if (!IsOwner || !IsSpawned || switcher == null) return;

        int index = -1;
        for (int i = 0; i < switcher.Count; i++)
            if (switcher.GetWeapon(i) == gun) { index = i; break; }
        if (index < 0) return;

        ShotFxRpc(index, points, normals, hits);
    }

    /// <summary>쏜 사람 → 나머지 모두: 빔 / 피격 이펙트 / 총소리 재생</summary>
    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    private void ShotFxRpc(int index, Vector3[] points, Vector3[] normals, byte[] hits)
    {
        if (switcher == null) switcher = GetComponentInChildren<WeaponSwitcher>(true);
        if (switcher == null) return;

        if (switcher.CurrentIndex != index) switcher.Select(index, true);
        var gun = switcher.GetWeapon(index);
        if (gun != null) gun.PlayRemoteShot(points, normals, hits);

        // 호스트: 다른 플레이어 발사 위치도 다음 라운드 마네킹 배치용으로 기록
        if (IsServer && mannequinSpawner != null)
            mannequinSpawner.RecordShot(transform.position, transform.forward);
    }

    // ───────── 마네킹 ─────────

    private void HandleLocalMannequinHit(Health mannequin, GameObject attacker)
    {
        // 내 총에 맞은 것만 (다른 사람 것은 그 사람이 보냄)
        if (!IsOwner || !IsSpawned || attacker != gameObject || mannequin == null) return;
        if (mannequinSpawner == null) mannequinSpawner = FindAnyObjectByType<MannequinSpawner>();
        if (mannequinSpawner == null) return;

        int index = mannequinSpawner.IndexOf(mannequin.gameObject);
        if (index >= 0) MannequinHitRpc(index);
    }

    /// <summary>쏜 사람 → 나머지 모두: 같은 번호의 마네킹을 쓰러뜨림 (공격자 = 이 플레이어)</summary>
    [Rpc(SendTo.NotMe)]
    private void MannequinHitRpc(int index)
    {
        if (mannequinSpawner == null) mannequinSpawner = FindAnyObjectByType<MannequinSpawner>();
        var h = mannequinSpawner != null ? mannequinSpawner.GetMannequin(index) : null;
        if (h != null) h.RemoteMannequinHit(gameObject);
    }

    // ───────── 이름 ─────────

    private void HandleNameChanged(FixedString32Bytes oldValue, FixedString32Bytes newValue)
    {
        if (ScoreManager.Instance != null && health.PlayerId >= 0)
            ScoreManager.Instance.SetName(health.PlayerId, DisplayName);
    }
}
