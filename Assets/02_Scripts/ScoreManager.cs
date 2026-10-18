using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 플레이어별 라운드 승리 / 킬 / 데스 기록. 점수가 바뀌면 OnScoreChanged 이벤트를 보낸다.
/// 순위 규칙: ① 라운드 승리 수 많은 순 ② 킬 많은 순 ③ 데스 적은 순 (셋 다 같으면 같은 순위 = 무승부)
/// GameManager 아래에 하나만 배치.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    [Serializable]
    public class PlayerScore
    {
        public int id;
        public string name;
        public int kills;
        public int deaths;
        public int roundWins;
        public bool isLocal;
    }

    public static ScoreManager Instance { get; private set; }

    /// <summary>점수/인원이 바뀔 때 호출</summary>
    public event Action OnScoreChanged;

    [Header("로컬 플레이어")]
    [SerializeField] private string localPlayerName = "PLAYER_01";

    private readonly Dictionary<int, PlayerScore> scores = new Dictionary<int, PlayerScore>();
    private int nextId = 0;

    public int LocalPlayerId { get; private set; } = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        LocalPlayerId = Register(localPlayerName, true);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ───────── 등록 ─────────

    /// <summary>플레이어 등록. 반환값(id)을 보관해 두고 킬/데스 기록에 사용</summary>
    public int Register(string playerName, bool isLocal = false)
    {
        int id = nextId++;
        scores[id] = new PlayerScore { id = id, name = playerName, isLocal = isLocal };
        OnScoreChanged?.Invoke();
        return id;
    }

    /// <summary>이름 변경 (네트워크 닉네임이 나중에 도착할 때)</summary>
    public void SetName(int id, string playerName)
    {
        if (string.IsNullOrEmpty(playerName)) return;
        if (scores.TryGetValue(id, out var s) && s.name != playerName)
        {
            s.name = playerName;
            OnScoreChanged?.Invoke();
        }
    }

    public void Unregister(int id)
    {
        if (scores.Remove(id)) OnScoreChanged?.Invoke();
    }

    // ───────── 기록 ─────────

    /// <summary>killerId가 victimId를 처치. 자살/환경 사망이면 killerId = -1</summary>
    public void AddKill(int killerId, int victimId)
    {
        if (killerId >= 0 && killerId != victimId && scores.TryGetValue(killerId, out var killer))
            killer.kills++;
        if (scores.TryGetValue(victimId, out var victim))
            victim.deaths++;
        OnScoreChanged?.Invoke();
    }

    /// <summary>라운드 승리 기록</summary>
    public void AddRoundWin(int id)
    {
        if (scores.TryGetValue(id, out var s))
        {
            s.roundWins++;
            OnScoreChanged?.Invoke();
        }
    }

    /// <summary>모든 기록을 0으로 (새 경기 시작 시)</summary>
    public void ResetScores()
    {
        foreach (var s in scores.Values) { s.kills = 0; s.deaths = 0; s.roundWins = 0; }
        OnScoreChanged?.Invoke();
    }

    // ───────── 조회 ─────────

    /// <summary>순위순 정렬: 라운드 승리 많은 순 → 킬 많은 순 → 데스 적은 순 → 이름순</summary>
    public List<PlayerScore> GetSorted()
    {
        return scores.Values
            .OrderByDescending(s => s.roundWins)
            .ThenByDescending(s => s.kills)
            .ThenBy(s => s.deaths)
            .ThenBy(s => s.name)
            .ToList();
    }

    /// <summary>순위 기준(라운드 승리 / 킬 / 데스)이 모두 같은지 → 같은 순위</summary>
    public static bool SameRank(PlayerScore a, PlayerScore b)
    {
        return a != null && b != null && a.roundWins == b.roundWins && a.kills == b.kills && a.deaths == b.deaths;
    }

    /// <summary>id로 이름 찾기 (없으면 null)</summary>
    public string NameOf(int id)
    {
        return scores.TryGetValue(id, out var s) ? s.name : null;
    }
}
