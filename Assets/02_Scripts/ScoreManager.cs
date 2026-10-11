using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어별 킬/데스 기록. 점수가 바뀌면 OnScoreChanged 이벤트를 보낸다.
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
        public bool isLocal;
    }

    public static ScoreManager Instance { get; private set; }

    /// <summary>점수/인원이 바뀔 때 호출</summary>
    public event Action OnScoreChanged;

    [Header("로컬 플레이어")]
    [SerializeField] private string localPlayerName = "PLAYER_01";

    [Header("디버그 (5: 랜덤 킬 / 6: 내 데스 / 7: 초기화)")]
    [SerializeField] private bool debugKeys = true;
    [SerializeField, Min(0)] private int debugFakePlayers = 4;

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

    /// <summary>모든 킬/데스를 0으로 (새 경기 시작 시)</summary>
    public void ResetScores()
    {
        foreach (var s in scores.Values) { s.kills = 0; s.deaths = 0; }
        OnScoreChanged?.Invoke();
    }

    // ───────── 조회 ─────────

    /// <summary>순위순 정렬: 킬 많은 순 → 데스 적은 순 → 이름순</summary>
    public List<PlayerScore> GetSorted()
    {
        return scores.Values
            .OrderByDescending(s => s.kills)
            .ThenBy(s => s.deaths)
            .ThenBy(s => s.name)
            .ToList();
    }

    // ───────── 디버그 ─────────

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!debugKeys) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.digit5Key.wasPressedThisFrame)
        {
            EnsureFakePlayers();
            var ids = scores.Keys.ToList();
            int killer = ids[UnityEngine.Random.Range(0, ids.Count)];
            int victim;
            do { victim = ids[UnityEngine.Random.Range(0, ids.Count)]; } while (victim == killer && ids.Count > 1);
            AddKill(killer, victim);
        }

        if (kb.digit6Key.wasPressedThisFrame)
        {
            EnsureFakePlayers();
            var others = scores.Keys.Where(k => k != LocalPlayerId).ToList();
            int killer = others.Count > 0 ? others[UnityEngine.Random.Range(0, others.Count)] : -1;
            AddKill(killer, LocalPlayerId);
        }

        if (kb.digit7Key.wasPressedThisFrame)
            ResetScores();
#endif
    }

    private void EnsureFakePlayers()
    {
        int current = scores.Count - 1; // 로컬 제외
        for (int i = current; i < debugFakePlayers; i++)
            Register($"BOT_{i + 1:00}");
    }
}