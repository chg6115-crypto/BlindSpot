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

    private void HostRoundEnd(int roundNumber, int winnerId)
        => RoundEndRpc(roundNumber, PlayerNetworkCombat.ClientOfPlayerId(winnerId));

    private void HostMatchEnd(int winnerId)
        => MatchEndRpc(PlayerNetworkCombat.ClientOfPlayerId(winnerId));

    // ───────── 참가자에서 실행 ─────────

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
