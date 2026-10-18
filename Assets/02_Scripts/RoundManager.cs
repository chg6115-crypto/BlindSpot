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
/// (지금은 각자 자기 RoundManager를 돌림. 서버 주도 라운드는 6단계에서)
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
    public Transform[] SpawnPoints => spawnPoints;

    private readonly HashSet<int> alive = new HashSet<int>();
    private readonly Dictionary<int, int> roundKills = new Dictionary<int, int>();
    private int participantCount;
    private bool forceEnd;
    private float spotMaxIntensity;
    private DecalBlink localDecal;
    private Coroutine matchRoutine;
    private string banner = "";
    private GUIStyle hudStyle, bannerStyle;

    // ───────── Unity ─────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        if (mannequinSpawner == null) mannequinSpawner = FindAnyObjectByType<MannequinSpawner>();
        if (trapSpawner == null) trapSpawner = FindAnyObjectByType<TrapSpawner>();

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
        while (localPlayer == null) yield return null;

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
        if (matchRoutine != null) StopCoroutine(matchRoutine);
        SetSpotlight(0f);
        if (ScoreManager.Instance != null) ScoreManager.Instance.ResetScores();
        matchRoutine = StartCoroutine(MatchLoop());
    }

    /// <summary>진행 중인 라운드를 바로 종료 (디버그용)</summary>
    public void ForceEndRound()
    {
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

    // ───────── 경기 흐름 ─────────

    private IEnumerator MatchLoop()
    {
        for (int r = 1; r <= totalRounds; r++)
        {
            CurrentRound = r;
            yield return IntroPhase();
            yield return PlayPhase();
            yield return EndPhase();
        }
        yield return MatchEndPhase();
    }

    private IEnumerator IntroPhase()
    {
        CurrentState = State.Intro;
        SetFrozen(true);

        SpawnPlayers();
        if (mannequinSpawner != null) mannequinSpawner.SpawnForRound();
        if (trapSpawner != null) trapSpawner.SpawnForRound();
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

    private IEnumerator PlayPhase()
    {
        CurrentState = State.Playing;
        forceEnd = false;
        TimeLeft = roundTime;
        banner = "HIDE IN THE DARK";
        float bannerOff = Time.time + 1.5f;
        SetFrozen(false);
        OnPlayStart?.Invoke(CurrentRound);

        while (!forceEnd && TimeLeft > 0f)
        {
            if (participantCount >= 2 && alive.Count <= 1) break;   // 1명만 남음
            if (banner.Length > 0 && Time.time > bannerOff) banner = "";
            TimeLeft -= Time.deltaTime;
            yield return null;
        }

        TimeLeft = Mathf.Max(0f, TimeLeft);
        banner = "";
    }

    private IEnumerator EndPhase()
    {
        CurrentState = State.RoundEnd;
        SetFrozen(true);

        int winner = DecideRoundWinner();
        banner = winner >= 0 ? $"ROUND {CurrentRound} WINNER\n{NameOf(winner)}" : $"ROUND {CurrentRound}\nDRAW";
        OnRoundEnd?.Invoke(CurrentRound, winner);

        // 이번 라운드 발사 위치 → 다음 라운드 마네킹 배치용
        if (mannequinSpawner != null) mannequinSpawner.EndRound();

        yield return new WaitForSeconds(roundEndDelay);
        banner = "";
    }

    private IEnumerator MatchEndPhase()
    {
        CurrentState = State.MatchEnd;
        SetFrozen(true);

        int winner = DecideMatchWinner();
        banner = winner >= 0 ? $"MATCH OVER\nWINNER  {NameOf(winner)}" : "MATCH OVER\nDRAW";
        OnMatchEnd?.Invoke(winner);
        matchRoutine = null;

        if (autoRestartDelay > 0f)
        {
            yield return new WaitForSeconds(autoRestartDelay);
            StartMatch();
        }
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

        var sorted = sm.GetSorted();   // 킬 많은 순 → 데스 적은 순
        if (sorted.Count == 0) return -1;
        if (sorted.Count == 1) return sorted[0].id;

        var a = sorted[0];
        var b = sorted[1];
        bool clearWin = a.kills > b.kills || (a.kills == b.kills && a.deaths < b.deaths);
        return clearWin ? a.id : -1;
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
        if (!showHud || CurrentState == State.Idle) return;

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hudFontSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.9f, 1f) }
            };
            bannerStyle = new GUIStyle(hudStyle)
            {
                fontSize = hudFontSize * 3,
                normal = { textColor = Color.white }
            };
        }

        // 상단: 라운드 / 남은 시간
        int sec = Mathf.CeilToInt(TimeLeft);
        string time = CurrentState == State.Playing ? $"   {sec / 60}:{sec % 60:00}" : "";
        GUI.Label(new Rect(0, 12, Screen.width, 30), $"ROUND {CurrentRound}/{totalRounds}{time}", hudStyle);

        // 내가 죽었으면 표시
        var sm = ScoreManager.Instance;
        if (CurrentState == State.Playing && sm != null && !alive.Contains(sm.LocalPlayerId))
            GUI.Label(new Rect(0, 44, Screen.width, 30), "YOU ARE DEAD", hudStyle);

        // 가운데: 카운트다운 / 결과
        if (!string.IsNullOrEmpty(banner))
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, Screen.height * 0.3f), banner, bannerStyle);
    }
}