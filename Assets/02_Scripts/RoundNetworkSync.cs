using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 라운드 진행 동기화. Player_FPS 프리팹 루트에 붙인다 (PlayerNetworkCombat 옆).
///
/// 씬에 따로 네트워크 오브젝트를 두지 않으려고, 호스트가 소유한 플레이어 오브젝트를 "방송 채널"로 쓴다.
/// (모든 컴퓨터에 호스트 플레이어가 있으므로 그 오브젝트로 보낸 신호는 모두가 받음)
///
///  호스트 RoundManager 이벤트 → 참가자에게 전달
///   라운드 시작 : 마네킹 배치 + 트랩 배치 + 라운드 번호 → 참가자가 같은 자리에 생성하고 카운트다운
///   GO          : 참가자도 동시에 조작 시작
///   라운드 종료 : 승자 (접속 번호로 보내서 각자 점수판 id로 변환)
///   경기 종료   : 우승자
///   데칼 점멸   : 호스트의 DecalBlinkManager가 점멸할 때마다 참가자도 같은 순간에 점멸
///   날씨        : 라운드마다 호스트가 정한 날씨 (WeatherManager)
///   번개        : 종류·위치·연출 시드 (LightningStorm)
///   라운드 무기 : 모두가 같은 총 (LoadoutSelector)
///   anti-camp   : 발동한 전등 번호 + 지점 + 대상 + 시작 반경 (AntiCampTrap)
/// </summary>
[DisallowMultipleComponent]
public class RoundNetworkSync : NetworkBehaviour
{
    /// <summary>호스트 소유 인스턴스 (모든 컴퓨터에서 같은 오브젝트)</summary>
    public static RoundNetworkSync Channel { get; private set; }

    private RoundManager round;
    private MannequinSpawner mannequins;
    private TrapSpawner traps;
    private bool hooked;

    private readonly List<Vector3> mannequinPositions = new List<Vector3>();
    private readonly List<float> mannequinYaws = new List<float>();

    // ───────── 스폰 ─────────

    public override void OnNetworkSpawn()
    {
        if (!IsOwnedByServer) return;   // 호스트 플레이어에 붙은 것만 사용
        Channel = this;
        FindSceneObjects();

        if (IsServer && round != null)
        {
            round.OnRoundStart += HostRoundStart;
            round.OnPlayStart += HostPlayStart;
            round.OnRoundEnd += HostRoundEnd;
            round.OnMatchEnd += HostMatchEnd;
            hooked = true;
        }
        if (IsServer) DecalBlinkManager.FlashStarted += HostFlash;
    }

    public override void OnNetworkDespawn()
    {
        if (hooked && round != null)
        {
            round.OnRoundStart -= HostRoundStart;
            round.OnPlayStart -= HostPlayStart;
            round.OnRoundEnd -= HostRoundEnd;
            round.OnMatchEnd -= HostMatchEnd;
        }
        hooked = false;
        DecalBlinkManager.FlashStarted -= HostFlash;
        if (Channel == this) Channel = null;
    }

    private void FindSceneObjects()
    {
        if (round == null) round = RoundManager.Instance;
        if (mannequins == null) mannequins = FindAnyObjectByType<MannequinSpawner>();
        if (traps == null) traps = FindAnyObjectByType<TrapSpawner>();
    }

    // ───────── 호스트 → 참가자 ─────────

    private void HostRoundStart(int roundNumber)
    {
        FindSceneObjects();

        // 이 시점에 호스트는 이미 마네킹·트랩을 배치함 (RoundManager.IntroPhase)
        if (mannequins != null) mannequins.GetLayout(mannequinPositions, mannequinYaws);
        else { mannequinPositions.Clear(); mannequinYaws.Clear(); }

        bool[] mapActive = new bool[0];
        Vector3[] trapPositions = new Vector3[0];
        Quaternion[] trapRotations = new Quaternion[0];
        if (traps != null) traps.GetLayout(out mapActive, out trapPositions, out trapRotations);

        RoundStartRpc(roundNumber, mannequinPositions.ToArray(), mannequinYaws.ToArray(),
                      mapActive, trapPositions, trapRotations);
    }

    private void HostPlayStart(int roundNumber) => PlayStartRpc(roundNumber);

    private void HostFlash()
    {
        if (IsSpawned) FlashRpc();
    }

    private void HostRoundEnd(int roundNumber, int winnerId)
        => RoundEndRpc(roundNumber, PlayerNetworkCombat.ClientOfPlayerId(winnerId));

    private void HostMatchEnd(int winnerId)
        => MatchEndRpc(PlayerNetworkCombat.ClientOfPlayerId(winnerId));

    /// <summary>호스트: 날씨를 참가자에게 (WeatherManager가 호출)</summary>
    public void SendWeather(int roundNumber, int weather)
    {
        if (IsSpawned && IsServer) WeatherRpc(roundNumber, weather);
    }

    /// <summary>호스트: 번개를 참가자에게 (LightningStorm이 호출)</summary>
    public void SendLightning(int kind, Vector3 position, int seed)
    {
        if (IsSpawned && IsServer) LightningRpc(kind, position, seed);
    }

    /// <summary>호스트: 라운드 무기를 참가자에게 (LoadoutSelector가 호출)</summary>
    public void SendRoundWeapon(int index)
    {
        if (IsSpawned && IsServer) RoundWeaponRpc(index);
    }

    /// <summary>호스트: anti-camp 전등 발동을 참가자에게 (AntiCampTrap이 호출)</summary>
    public void SendAntiCamp(int lampIndex, Vector3 spot, ulong targetNetId, float startRadius)
    {
        if (IsSpawned && IsServer) AntiCampRpc(lampIndex, spot, targetNetId, startRadius);
    }

    // ───────── 참가자에서 실행 ─────────

    [Rpc(SendTo.NotServer)]
    private void AntiCampRpc(int lampIndex, Vector3 spot, ulong targetNetId, float startRadius)
    {
        if (AntiCampTrap.Instance != null) AntiCampTrap.Instance.PlayLampFromHost(lampIndex, spot, targetNetId, startRadius);
    }

    [Rpc(SendTo.NotServer)]
    private void RoundWeaponRpc(int index)
    {
        if (LoadoutSelector.Instance != null) LoadoutSelector.Instance.ApplyFromHost(index);
    }

    [Rpc(SendTo.NotServer)]
    private void WeatherRpc(int roundNumber, int weather)
    {
        if (WeatherManager.Instance != null) WeatherManager.Instance.ApplyFromHost(weather);
    }

    [Rpc(SendTo.NotServer)]
    private void LightningRpc(int kind, Vector3 position, int seed)
    {
        if (LightningStorm.Instance != null) LightningStorm.Instance.PlayStrike(kind, position, seed);
    }

    [Rpc(SendTo.NotServer)]
    private void RoundStartRpc(int roundNumber, Vector3[] mannequinPos, float[] mannequinYaw,
                               bool[] mapTrapActive, Vector3[] trapPos, Quaternion[] trapRot)
    {
        FindSceneObjects();
        if (mannequins != null) mannequins.SpawnLayout(mannequinPos, mannequinYaw);
        if (traps != null) traps.ApplyLayout(mapTrapActive, trapPos, trapRot);
        if (round != null) round.NetBeginRound(roundNumber);
    }

    [Rpc(SendTo.NotServer)]
    private void FlashRpc()
    {
        if (DecalBlinkManager.Instance != null) DecalBlinkManager.Instance.PlayFlash();
    }

    [Rpc(SendTo.NotServer)]
    private void PlayStartRpc(int roundNumber)
    {
        FindSceneObjects();
        if (round != null) round.NetBeginPlay(roundNumber);
    }

    [Rpc(SendTo.NotServer)]
    private void RoundEndRpc(int roundNumber, ulong winnerClientId)
    {
        FindSceneObjects();
        if (round != null) round.NetEndRound(roundNumber, PlayerNetworkCombat.PlayerIdOfClient(winnerClientId));
    }

    [Rpc(SendTo.NotServer)]
    private void MatchEndRpc(ulong winnerClientId)
    {
        FindSceneObjects();
        if (round != null) round.NetEndMatch(PlayerNetworkCombat.PlayerIdOfClient(winnerClientId));
    }
}
