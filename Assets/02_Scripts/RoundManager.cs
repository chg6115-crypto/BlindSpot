using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// 라운드/경기 진행 관리. GameManager 아래에 하나만 배치.
///  [인트로] 플레이어 스폰 → 마네킹·트랩 배치 → 내 머리 위 스포트라이트 + 내 데칼 ON → 카운트다운 → 소등 → 시작
///  [진행]   제한 시간 / 사망 처리 / 1명만 남으면 종료
///  [종료]   라운드 승자 표시 → 이번 라운드 발사 위치를 다음 라운드 마네킹용으로 넘김
///  [경기]   totalRounds 라운드 후 총 킬 1위가 우승
/// 사망은 ReportKill(killerId, victimId)로 알림 (나중에 체력 시스템에서 호출).
/// MannequinSpawner / TrapSpawner 의 Spawn On Start 는 꺼둘 것 (RoundManager가 배치함).
/// Freeze Components가 비어 있으면 플레이어의 FirstPersonController, vThirdPersonInput, SonarSkill을 자동으로 찾음.
///
/// 스포트라이트는 "나에게만" 보이도록: 내 캐릭터 렌더러에 LocalPlayer 렌더링 레이어를 추가하고,
/// 스포트라이트는 Default(바닥) + LocalPlayer(내 몸)만 비추게 설정 → 마네킹·다른 플레이어는 안 비춤.
///
/// OnPlayStart(GO 순간) 이벤트: 1인칭 연출 등 연결용.
///
/// [네트워크 4단계] 플레이어는 씬에 미리 두지 않고 NetworkPlayerSpawner가 스폰한다.
/// 내 플레이어가 스폰되면 PlayerNetworkSetup이 SetLocalPlayer()를 호출 → 그때 경기 시작.
///
/// 라운드 규칙: 마지막 1명이 남으면 종료. 제한 시간이 지나면 연장전(OVERTIME) → 데칼 점멸이 빨라지고 1명 남을 때까지 계속
///   (혼자 플레이 중이면 제한 시간에 종료). 라운드 무승부 없음 (동시에 전멸한 경우만 무승부)
/// 경기 우승: ① 라운드 승리 수 ② 킬 ③ 데스 적은 순, 셋 다 같으면 무승부. 경기 종료 시 MatchResultUI가 결과창 표시
///
/// [네트워크 6단계] 멀티에서는 호스트만 라운드를 진행한다.
///  - 호스트: 지금까지와 똑같이 진행 (마네킹·트랩 배치, 승패 판정). RoundNetworkSync가 각 단계를 참가자에게 알림
///  - 참가자: 직접 진행하지 않고(IsFollower) 호스트 신호(NetBeginRound / NetBeginPlay / NetEndRound / NetEndMatch)를 따라감
///  - 싱글 / 오프라인은 호스트와 같음
/// </summary>
[DefaultExecutionOrder(50)]
public class RoundManager : MonoBehaviour
{
    public enum State { Idle, Intro, Playing, RoundEnd, MatchEnd }

    public static RoundManager Instance { get; private set; }

    [Header("경기")]
    [SerializeField, Min(1)] private int totalRounds = 5;
    [SerializeField] private bool autoStart = true;
    [Tooltip("경기 종료 후 자동 재시작까지 시간 (0이면 자동 재시작 안 함)")]
    [SerializeField, Min(0f)] private float autoRestartDelay = 0f;

    [Header("라운드")]
    [Tooltip("라운드 제한 시간 (초)")]
    [SerializeField, Min(10f)] private float roundTime = 60f;
    [Tooltip("라운드 결과를 보여주는 시간")]
    [SerializeField, Min(0f)] private float roundEndDelay = 4f;

    [Header("시작 연출")]
    [Tooltip("카운트다운 시간 (초)")]
    [SerializeField, Min(0f)] private float introTime = 3f;
    [Tooltip("카운트다운 동안 내 데칼 켜기")]
    [SerializeField] private bool introOwnDecal = true;

    [Header("시작 스포트라이트 (나에게만 보임)")]
    [Tooltip("씬에 만든 Spot Light. 비워두면 스포트라이트 연출 없음")]
    [SerializeField] private Light introSpotlight;
    [Tooltip("플레이어 발 위치에서 라이트까지 높이 (m)")]
    [SerializeField, Min(0.5f)] private float spotHeight = 3.5f;
    [SerializeField, Min(0.01f)] private float spotFadeIn = 0.3f;
    [Tooltip("시작(GO) 순간 꺼지는 시간")]
    [SerializeField, Min(0.01f)] private float spotFadeOut = 0.5f;
    [Tooltip("내 캐릭터에 추가할 렌더링 레이어 번호 (Project Settings > Tags and Layers > Rendering Layers의 LocalPlayer)")]
    [SerializeField, Range(1, 31)] private int localPlayerRenderingLayer = 2;

    [Header("플레이어")]
    [SerializeField] private Transform[] spawnPoints;
    [Tooltip("비워두면 스폰된 내 플레이어가 자동 등록됨 (PlayerNetworkSetup)")]
    [SerializeField] private Transform localPlayer;
    [Tooltip("카운트다운·사망·라운드 종료 중 꺼둘 컴포넌트. 비워두면 FirstPersonController, vThirdPersonInput, SonarSkill 자동 검색")]
    [SerializeField] private Behaviour[] freezeComponents;

    [Header("연결 (비워두면 자동 검색)")]
    [SerializeField] private MannequinSpawner mannequinSpawner;
    [SerializeField] private TrapSpawner trapSpawner;

    [Header("임시 HUD")]
    [SerializeField] private bool showHud = true;
    [SerializeField] private int hudFontSize = 20;
    [Tooltip("가운데 카운트다운 / 결과 글자 크기")]
    [SerializeField] private int bannerFontSize = 28;
    [Tooltip("HUD 글꼴 (ttf). 비우면 기본 글꼴")]
    [SerializeField] private Font hudFont;

    /// <summary>라운드 시작 - 인트로 시작 시점 (라운드 번호)</summary>
    public event Action<int> OnRoundStart;
    /// <summary>GO - 실제 플레이 시작 시점 (라운드 번호)</summary>
    public event Action<int> OnPlayStart;
    /// <summary>라운드 종료 (라운드 번호, 승자 id / 무승부 -1)</summary>
    public event Action<int, int> OnRoundEnd;
    /// <summary>경기 종료 (우승자 id / 무승부 -1)</summary>
    public event Action<int> OnMatchEnd;

    public State CurrentState { get; private set; } = State.Idle;
    public int CurrentRound { get; private set; }
    public int TotalRounds => totalRounds;
    public float TimeLeft { get; private set; }
    public IReadOnlyCollection<int> AliveIds => alive;
    /// <summary>라운드별 승자 (점수판 id, 무승부 -1). 결과창 표시용</summary>
    public IReadOnlyList<int> RoundWinners => roundWinners;
    /// <summary>연장전 중인지 (제한 시간 초과)</summary>
    public bool IsOvertime { get; private set; }
    /// <summary>경기가 일찍 끝난 이유 (예: ALL PLAYERS LEFT). 정상 종료면 빈 문자열</summary>
    public string MatchEndReason { get; private set; } = "";
    public Transform[] SpawnPoints => spawnPoints;

    /// <summary>멀티 참가자(호스트 아님) → 라운드를 직접 진행하지 않고 호스트 신호를 따라감</summary>
    public bool IsFollower
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening && !nm.IsServer;
        }
    }

    private readonly HashSet<int> alive = new HashSet<int>();
    private readonly Dictionary<int, int> roundKills = new Dictionary<int, int>();
    private int participantCount;
    private bool forceEnd;
    private float spotMaxIntensity;
    private DecalBlink localDecal;
    private Coroutine matchRoutine;
    private string banner = "";
    private readonly List<int> roundWinners = new List<int>();
    private bool hasResultUI;
    private bool startedWithOthers;   // 호스트: 다른 플레이어와 함께 시작한 경기인지
    private GUIStyle hudStyle, bannerStyle;

    // ───────── Unity ─────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        if (mannequinSpawner == null) mannequinSpawner = FindAnyObjectByType<MannequinSpawner>();
        if (trapSpawner == null) trapSpawner = FindAnyObjectByType<TrapSpawner>();
        hasResultUI = GetComponent<MatchResultUI>() != null;

        // Inspector에 직접 넣어둔 플레이어가 있으면 바로 등록 (예전 방식 호환)
        if (localPlayer != null) SetLocalPlayer(localPlayer);

        // 스포트라이트: Inspector의 Intensity를 최대 밝기로 쓰고 처음엔 꺼둠
        if (introSpotlight != null)
        {
            spotMaxIntensity = introSpotlight.intensity;
            introSpotlight.enabled = false;
        }
    }

    private IEnumerator Start()
    {
        yield return null;   // 스포너들이 NavMesh 정보를 준비한 다음 시작

        // 내 플레이어가 스폰될 때까지 대기 (네트워크 스폰은 씬 로드보다 늦게 옴)
        // 스폰 순간 등록을 놓쳤어도 여기서 직접 찾아 등록
        while (localPlayer == null)
        {
            var local = PlayerNetworkSetup.Local;
            if (local != null) SetLocalPlayer(local.transform);
            else yield return null;
        }

        // 참가자는 호스트의 라운드 시작 신호를 기다림
        if (IsFollower) yield break;

        // 호스트: 모든 참가자의 플레이어가 생성될 때까지 기다린 뒤 시작 (최대 15초)
        //  → 늦게 로드된 참가자도 1라운드 무기 선택 / 카운트다운을 받음
        var spawner = NetworkPlayerSpawner.Instance;
        if (spawner != null && !spawner.AllPlayersSpawned)
        {
            banner = "WAITING FOR PLAYERS";
            float giveUp = Time.unscaledTime + 15f;
            while (!spawner.AllPlayersSpawned && Time.unscaledTime < giveUp) yield return null;
            banner = "";
            yield return new WaitForSeconds(0.5f);   // 참가자 쪽에 플레이어가 만들어질 시간
        }

        if (autoStart) StartMatch();
    }

    /// <summary>
    /// 내 플레이어 등록. PlayerNetworkSetup이 스폰 직후 호출.
    /// 데칼 / 스포트라이트 렌더링 레이어 / Freeze 대상 설정.
    /// </summary>
    public void SetLocalPlayer(Transform player)
    {
        if (player == null) return;
        localPlayer = player;

        localDecal = localPlayer.GetComponentInChildren<DecalBlink>();

        // 내 캐릭터에만 LocalPlayer 렌더링 레이어 추가 → 스포트라이트가 나만 비춤
        uint bit = 1u << localPlayerRenderingLayer;
        foreach (var r in localPlayer.GetComponentsInChildren<Renderer>(true))
            r.renderingLayerMask |= bit;

        // Freeze Components가 비어 있거나 (씬 플레이어를 지워서) 전부 끊겼으면 자동으로 찾기
        if (freezeComponents == null || freezeComponents.All(c => c == null))
        {
            var list = new List<Behaviour>();

            // FPS 컨트롤러 (이동·시점·중력 모두 정지)
            var fpc = localPlayer.GetComponentInChildren<StarterAssets.FirstPersonController>();
            if (fpc != null) list.Add(fpc);

            // 예전 TPS 컨트롤러 (있으면)
            var input = localPlayer.GetComponentInChildren<Invector.vCharacterController.vThirdPersonInput>();
            if (input != null) list.Add(input);

            var sonar = localPlayer.GetComponentInChildren<SonarSkill>();
            if (sonar != null) list.Add(sonar);

            freezeComponents = list.ToArray();
            Debug.Log($"[RoundManager] Freeze Components 자동 설정: {list.Count}개", this);
        }

        // 이미 경기 중이면 현재 상태에 맞춰 정지/해제
        if (CurrentState != State.Idle) SetFrozen(CurrentState != State.Playing);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ───────── 외부 호출 ─────────

    /// <summary>경기 처음부터 시작 (점수 초기화)</summary>
    public void StartMatch()
    {
        if (IsFollower) return;   // 참가자는 호스트를 따라감
        if (matchRoutine != null) StopCoroutine(matchRoutine);
        SetSpotlight(0f);
        if (ScoreManager.Instance != null) ScoreManager.Instance.ResetScores();
        roundWinners.Clear();
        MatchEndReason = "";
        var nm = NetworkManager.Singleton;
        startedWithOthers = nm != null && nm.IsListening && nm.IsServer && nm.ConnectedClientsIds.Count >= 2;
        matchRoutine = StartCoroutine(MatchLoop());
    }

    /// <summary>진행 중인 라운드를 바로 종료 (디버그용)</summary>
    public void ForceEndRound()
    {
        if (IsFollower) return;
        if (CurrentState == State.Playing) forceEnd = true;
    }

    /// <summary>처치 알림. 라운드 진행 중에만 반영</summary>
    public void ReportKill(int killerId, int victimId)
    {
        if (CurrentState != State.Playing) return;
        if (!alive.Contains(victimId)) return;   // 이미 사망

        if (ScoreManager.Instance != null) ScoreManager.Instance.AddKill(killerId, victimId);

        if (killerId >= 0 && killerId != victimId)
            roundKills[killerId] = (roundKills.TryGetValue(killerId, out int k) ? k : 0) + 1;

        alive.Remove(victimId);

        // 내가 죽으면 다음 라운드까지 조작 정지
        if (ScoreManager.Instance != null && victimId == ScoreManager.Instance.LocalPlayerId)
            SetFrozen(true);
    }

    /// <summary>라운드 도중 참가자 추가 (디버그 봇 등)</summary>
    public void RegisterParticipant(int id)
    {
        if (CurrentState == State.Playing || CurrentState == State.Intro)
        {
            if (alive.Add(id)) participantCount++;
        }
    }

    public bool IsAlive(int id) => alive.Contains(id);

    private void Update()
    {
        // 호스트: 같이 시작한 플레이어가 전부 나가면 경기 즉시 종료 → 결과창
        if (!startedWithOthers || IsFollower) return;
        if (CurrentState == State.Idle || CurrentState == State.MatchEnd) return;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer) return;
        if (nm.ConnectedClientsIds.Count <= 1) EndMatchEarly("ALL PLAYERS LEFT");
    }

    /// <summary>경기를 바로 끝내고 결과창으로 (호스트)</summary>
    public void EndMatchEarly(string reason)
    {
        if (IsFollower || CurrentState == State.MatchEnd) return;
        startedWithOthers = false;
        MatchEndReason = reason ?? "";
        if (matchRoutine != null) StopCoroutine(matchRoutine);
        SetSpotlight(0f);
        matchRoutine = StartCoroutine(MatchEndPhase(true, -1));
    }

    /// <summary>나간 플레이어를 생존자에서 제거 (남은 사람이 1명이면 라운드 종료로 이어짐)</summary>
    public void RemoveParticipant(int id)
    {
        alive.Remove(id);
    }

    // ───────── 경기 흐름 ─────────

    private IEnumerator MatchLoop()
    {
        for (int r = 1; r <= totalRounds; r++)
        {
            CurrentRound = r;
            yield return IntroPhase(true);
            yield return PlayPhase(true);
            yield return EndPhase(true, -1);
        }
        yield return MatchEndPhase(true, -1);
    }

    /// <param name="host">true: 마네킹·트랩을 직접 배치 / false: 참가자 (호스트가 보낸 배치를 이미 적용함)</param>
    private IEnumerator IntroPhase(bool host)
    {
        CurrentState = State.Intro;
        SetFrozen(true);
        SetOvertime(false);

        SpawnPlayers();
        if (host)
        {
            if (mannequinSpawner != null) mannequinSpawner.SpawnForRound();
            if (trapSpawner != null) trapSpawner.SpawnForRound();
        }
        ResetAlive();
        OnRoundStart?.Invoke(CurrentRound);

        // 내 데칼만 켜기
        if (introOwnDecal && localDecal != null)
            localDecal.ForceGlow(introTime + spotFadeOut);

        // 내 머리 위 스포트라이트 켜기
        PlaceSpotlight();
        yield return FadeSpotlight(0f, 1f, spotFadeIn);

        // 카운트다운
        int count = Mathf.Max(1, Mathf.CeilToInt(introTime));
        for (int i = count; i > 0; i--)
        {
            banner = $"ROUND {CurrentRound}\n{i}";
            yield return new WaitForSeconds(introTime / count);
        }

        // 소등
        yield return FadeSpotlight(1f, 0f, spotFadeOut);
    }

    /// <param name="host">true: 종료 조건 판정 / false: 참가자 (호스트의 종료 신호까지 타이머만 표시)</param>
    private IEnumerator PlayPhase(bool host)
    {
        CurrentState = State.Playing;
        forceEnd = false;
        TimeLeft = roundTime;
        banner = "HIDE IN THE DARK";
        float bannerOff = Time.time + 1.5f;
        SetFrozen(false);
        OnPlayStart?.Invoke(CurrentRound);

        while (!forceEnd)
        {
            if (host && participantCount >= 2 && alive.Count <= 1) break;   // 1명만 남음

            // 제한 시간 초과 → 연장전 (혼자면 그냥 종료)
            if (!IsOvertime && TimeLeft <= 0f)
            {
                if (host && participantCount < 2) break;
                SetOvertime(true);
                banner = "OVERTIME";
                bannerOff = Time.time + 1.5f;
            }

            if (banner.Length > 0 && Time.time > bannerOff) banner = "";
            TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
            yield return null;
        }

        TimeLeft = Mathf.Max(0f, TimeLeft);
        banner = "";
    }

    /// <param name="host">true: 승자 직접 판정 / false: 호스트가 보낸 승자(givenWinner) 사용</param>
    private IEnumerator EndPhase(bool host, int givenWinner)
    {
        CurrentState = State.RoundEnd;
        SetFrozen(true);

        SetOvertime(false);

        int winner = host ? DecideRoundWinner() : givenWinner;
        roundWinners.Add(winner);
        if (winner >= 0 && ScoreManager.Instance != null) ScoreManager.Instance.AddRoundWin(winner);

        banner = winner >= 0 ? $"ROUND {CurrentRound} WINNER\n{NameOf(winner)}" : $"ROUND {CurrentRound}\nDRAW";
        OnRoundEnd?.Invoke(CurrentRound, winner);

        // 이번 라운드 발사 위치 → 다음 라운드 마네킹 배치용
        if (mannequinSpawner != null) mannequinSpawner.EndRound();

        yield return new WaitForSeconds(roundEndDelay);
        banner = "";
    }

    private IEnumerator MatchEndPhase(bool host, int givenWinner)
    {
        CurrentState = State.MatchEnd;
        SetFrozen(true);

        SetOvertime(false);
        int winner = host ? DecideMatchWinner() : givenWinner;
        // 결과창이 있으면 그쪽에서 표시
        banner = hasResultUI ? "" : (winner >= 0 ? $"MATCH OVER\nWINNER  {NameOf(winner)}" : "MATCH OVER\nDRAW");
        OnMatchEnd?.Invoke(winner);
        matchRoutine = null;

        if (host && autoRestartDelay > 0f)
        {
            yield return new WaitForSeconds(autoRestartDelay);
            StartMatch();
        }
    }

    // ───────── 참가자: 호스트 신호 (RoundNetworkSync가 호출) ─────────

    /// <summary>호스트: 라운드 시작 (카운트다운). 마네킹·트랩 배치는 호출 전에 이미 적용됨</summary>
    public void NetBeginRound(int round)
    {
        if (!IsFollower) return;
        if (matchRoutine != null) StopCoroutine(matchRoutine);
        if (round <= 1)   // 새 경기
        {
            if (ScoreManager.Instance != null) ScoreManager.Instance.ResetScores();
            roundWinners.Clear();
            MatchEndReason = "";
        }
        SetSpotlight(0f);
        CurrentRound = round;
        matchRoutine = StartCoroutine(IntroPhase(false));
    }

    /// <summary>호스트: GO (플레이 시작)</summary>
    public void NetBeginPlay(int round)
    {
        if (!IsFollower) return;
        if (matchRoutine != null) StopCoroutine(matchRoutine);
        SetSpotlight(0f);   // 카운트다운 연출이 덜 끝났어도 바로 소등

        // 라운드 시작 신호를 놓쳤으면 (늦게 들어옴 등) 생존자 목록부터 채움 → 'YOU ARE DEAD' 오표시 방지
        if (CurrentState != State.Intro || CurrentRound != round)
        {
            CurrentRound = round;
            ResetAlive();
        }
        matchRoutine = StartCoroutine(PlayPhase(false));
    }

    /// <summary>호스트: 라운드 종료 (승자 = 이 컴퓨터 점수판 id, 무승부 -1)</summary>
    public void NetEndRound(int round, int winnerId)
    {
        if (!IsFollower) return;
        if (matchRoutine != null) StopCoroutine(matchRoutine);
        CurrentRound = round;
        matchRoutine = StartCoroutine(EndPhase(false, winnerId));
    }

    /// <summary>호스트: 경기 종료</summary>
    public void NetEndMatch(int winnerId)
    {
        if (!IsFollower) return;
        if (matchRoutine != null) StopCoroutine(matchRoutine);
        matchRoutine = StartCoroutine(MatchEndPhase(false, winnerId));
    }

    // ───────── 스포트라이트 ─────────

    private void PlaceSpotlight()
    {
        if (introSpotlight == null || localPlayer == null) return;
        introSpotlight.transform.SetPositionAndRotation(
            localPlayer.position + Vector3.up * spotHeight,
            Quaternion.LookRotation(Vector3.down, localPlayer.forward));   // 바로 아래를 비춤
    }

    private IEnumerator FadeSpotlight(float from, float to, float duration)
    {
        if (introSpotlight == null) yield break;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            SetSpotlight(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetSpotlight(to);
    }

    private void SetSpotlight(float k)
    {
        if (introSpotlight == null) return;
        introSpotlight.intensity = spotMaxIntensity * k;
        introSpotlight.enabled = k > 0.001f;
    }

    // ───────── 승자 판정 ─────────

    private int DecideRoundWinner()
    {
        // 1명만 살아남음
        if (participantCount >= 2 && alive.Count == 1) return alive.First();

        // 시간 종료: 생존자 중 이번 라운드 킬 1위 (동점이면 무승부)
        var candidates = alive.Count > 0 ? alive.ToList() : roundKills.Keys.ToList();
        if (candidates.Count == 0) return -1;
        if (candidates.Count == 1) return candidates[0];

        var ranked = candidates
            .Select(id => new { id, kills = roundKills.TryGetValue(id, out int k) ? k : 0 })
            .OrderByDescending(x => x.kills)
            .ToList();
        return ranked[0].kills > ranked[1].kills ? ranked[0].id : -1;
    }

    private int DecideMatchWinner()
    {
        var sm = ScoreManager.Instance;
        if (sm == null) return -1;

        var sorted = sm.GetSorted();   // 라운드 승리 → 킬 → 데스 적은 순
        if (sorted.Count == 0) return -1;
        if (sorted.Count == 1) return sorted[0].id;

        // 1, 2위의 라운드 승리 / 킬 / 데스가 모두 같으면 무승부
        return ScoreManager.SameRank(sorted[0], sorted[1]) ? -1 : sorted[0].id;
    }

    // ───────── 스폰 / 생존자 ─────────

    private void SpawnPlayers()
    {
        if (localPlayer == null || spawnPoints == null) return;

        var valid = spawnPoints.Where(s => s != null).ToList();
        if (valid.Count == 0)
        {
            Debug.LogWarning("[RoundManager] Spawn Points가 비어 있습니다.", this);
            return;
        }

        Transform point;
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !LobbyManager.IsSinglePlayer)
        {
            // 멀티: 각자 자기 RoundManager를 돌리므로 겹치지 않게 클라이언트 번호로 자리 고정
            // (호스트 0번, 다음 참가자 1번 ...). 라운드마다 시작 칸이 한 칸씩 밀려 위치가 바뀜
            int slot = (int)(nm.LocalClientId % (ulong)valid.Count);
            point = valid[(slot + CurrentRound - 1) % valid.Count];
        }
        else
        {
            // 싱글 / 오프라인: 무작위
            point = valid[UnityEngine.Random.Range(0, valid.Count)];
        }

        Teleport(localPlayer, point);
    }

    private static void Teleport(Transform target, Transform point)
    {
        Vector3 pos = point.position;
        Quaternion rot = Quaternion.Euler(0f, point.eulerAngles.y, 0f);

        // CharacterController는 켜진 상태에서 위치를 바꾸면 되돌아갈 수 있어 잠깐 끔
        var cc = target.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        var rb = target.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.rotation = rot;
        }

        // 네트워크 플레이어: Teleport로 옮겨야 다른 사람 화면에서 미끄러지듯 이동하지 않음
        var nt = target.GetComponent<NetworkTransform>();
        if (nt != null && nt.IsSpawned && nt.CanCommitToTransform)
            nt.Teleport(pos, rot, target.localScale);
        else
            target.SetPositionAndRotation(pos, rot);
        Physics.SyncTransforms();

        if (cc != null) cc.enabled = true;
    }

    private void ResetAlive()
    {
        alive.Clear();
        roundKills.Clear();

        var sm = ScoreManager.Instance;
        if (sm != null)
            foreach (var s in sm.GetSorted()) alive.Add(s.id);

        participantCount = alive.Count;
    }

    private void SetOvertime(bool on)
    {
        IsOvertime = on;
        if (DecalBlinkManager.Instance != null) DecalBlinkManager.Instance.SetOvertime(on);
    }

    private void SetFrozen(bool frozen)
    {
        if (freezeComponents != null)
            foreach (var c in freezeComponents)
                if (c != null) c.enabled = !frozen;

        if (frozen && localPlayer != null)
        {
            var rb = localPlayer.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        }
    }

    private static string NameOf(int id)
    {
        var sm = ScoreManager.Instance;
        if (sm == null) return $"#{id}";
        var s = sm.GetSorted().FirstOrDefault(x => x.id == id);
        return s != null ? s.name : $"#{id}";
    }

    // ───────── 임시 HUD ─────────

    private void OnGUI()
    {
        if (!showHud || (CurrentState == State.Idle && string.IsNullOrEmpty(banner))) return;
        if (CurrentState == State.MatchEnd && hasResultUI) return;   // 결과창이 화면을 덮음

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hudFontSize,
                font = hudFont,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.9f, 1f) }
            };
            bannerStyle = new GUIStyle(hudStyle)
            {
                fontSize = bannerFontSize,
                normal = { textColor = Color.white }
            };
        }

        // 상단: 라운드 / 남은 시간
        int sec = Mathf.CeilToInt(TimeLeft);
        string time = CurrentState != State.Playing ? "" : (IsOvertime ? "   OVERTIME" : $"   {sec / 60}:{sec % 60:00}");
        if (CurrentState != State.Idle)
            GUI.Label(new Rect(0, 12, Screen.width, 30), $"ROUND {CurrentRound}/{totalRounds}{time}", hudStyle);

        // 내가 죽었으면 표시
        var sm = ScoreManager.Instance;
        if (CurrentState == State.Playing && localPlayer != null && sm != null && !alive.Contains(sm.LocalPlayerId))
            GUI.Label(new Rect(0, 44, Screen.width, 30), "YOU ARE DEAD", hudStyle);

        // 가운데: 카운트다운 / 결과
        if (!string.IsNullOrEmpty(banner))
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, Screen.height * 0.3f), banner, bannerStyle);
    }
}