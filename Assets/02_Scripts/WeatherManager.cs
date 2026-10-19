using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 라운드별 날씨. 씬의 WeatherManager 오브젝트에 붙인다 (LightningStorm과 같은 오브젝트).
///
///  날씨 3종
///   - Moonlit (달밤, 기본) : 달 보임
///   - Rain    (비)         : 창문 밖 비 + 빗소리, 달 꺼짐
///   - Storm   (폭풍)       : 비 + 번개(LightningStorm) + 정전, 달 꺼짐
///
///  결정 (호스트 / 싱글만, 라운드가 시작될 때마다)
///   - 폭풍: 경기당 딱 1번, 2라운드 ~ 마지막 라운드 중 하나
///           아직 안 나왔으면 1 / (남은 라운드 수) 확률 → 마지막 라운드에는 반드시
///   - 나머지 라운드: Rain Chance 확률로 비, 아니면 달밤
///  참가자는 호스트가 보낸 날씨를 그대로 적용 (RoundNetworkSync.WeatherRpc)
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public class WeatherManager : MonoBehaviour
{
    public enum Weather { Moonlit = 0, Rain = 1, Storm = 2 }

    public static WeatherManager Instance { get; private set; }
    public static event System.Action<Weather> Changed;

    [Header("결정")]
    [Tooltip("폭풍이 아닌 라운드에서 비가 올 확률")]
    [SerializeField, Range(0f, 1f)] private float rainChance = 0.3f;
    [Tooltip("폭풍이 나올 수 있는 첫 라운드")]
    [SerializeField, Min(1)] private int stormFromRound = 2;

    [Header("오브젝트")]
    [Tooltip("비 파티클 (건물 밖). 비·폭풍일 때 켜짐")]
    [SerializeField] private GameObject[] rainObjects;
    [Tooltip("달 (Moon). 달밤일 때만 보임")]
    [SerializeField] private GameObject moonObject;

    [Header("소리")]
    [Tooltip("빗소리 (반복, 2D). 비우면 소리 없음")]
    [SerializeField] private AudioClip rainLoop;
    [SerializeField, Range(0f, 1f)] private float rainVolume = 0.6f;
    [Tooltip("폭풍일 때 빗소리 배율")]
    [SerializeField, Min(0f)] private float stormRainVolumeMul = 1.3f;
    [SerializeField, Min(0.01f)] private float rainFadeTime = 2f;

    public Weather Current { get; private set; } = Weather.Moonlit;

    private bool stormUsed;
    private int decidedRound = -1;
    private AudioSource rainSource;
    private Coroutine fadeRoutine;
    private RoundManager hookedRound;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        rainSource = gameObject.AddComponent<AudioSource>();
        rainSource.playOnAwake = false;
        rainSource.loop = true;
        rainSource.spatialBlend = 0f;
        rainSource.volume = 0f;
        var mixer = Resources.Load<AudioMixer>("MainMixer");
        if (mixer != null)
        {
            var groups = mixer.FindMatchingGroups("SFX");
            if (groups != null && groups.Length > 0) rainSource.outputAudioMixerGroup = groups[0];
        }

        Apply(Weather.Moonlit, true);
    }

    private void Start() => Hook();

    private void OnEnable() => Hook();

    private void OnDisable()
    {
        if (hookedRound != null) hookedRound.OnRoundStart -= HandleRoundStart;
        hookedRound = null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Hook()
    {
        var rm = RoundManager.Instance;
        if (rm == null || rm == hookedRound) return;
        if (hookedRound != null) hookedRound.OnRoundStart -= HandleRoundStart;
        hookedRound = rm;
        rm.OnRoundStart += HandleRoundStart;
    }

    private void Update()
    {
        if (hookedRound == null) Hook();   // RoundManager가 늦게 생겨도 연결
    }

    // ───────── 결정 (호스트 / 싱글) ─────────

    private void HandleRoundStart(int round)
    {
        var rm = RoundManager.Instance;
        if (rm != null && rm.IsFollower) return;   // 참가자는 호스트가 보낸 값 사용
        if (round == decidedRound) return;
        decidedRound = round;

        if (round <= 1) stormUsed = false;   // 새 경기
        int total = rm != null ? rm.TotalRounds : round;

        Weather w = Weather.Moonlit;
        if (!stormUsed && round >= stormFromRound && total >= stormFromRound)
        {
            int remaining = Mathf.Max(1, total - round + 1);
            if (Random.value < 1f / remaining) { w = Weather.Storm; stormUsed = true; }
        }
        if (w != Weather.Storm && Random.value < rainChance) w = Weather.Rain;

        SetAndBroadcast(w, round);
    }

    private void SetAndBroadcast(Weather w, int round)
    {
        Apply(w, false);
        var ch = RoundNetworkSync.Channel;
        if (ch != null) ch.SendWeather(round, (int)w);
    }

    /// <summary>참가자: 호스트가 보낸 날씨 적용</summary>
    public void ApplyFromHost(int weather)
    {
        if (weather < 0 || weather > 2) weather = 0;
        Apply((Weather)weather, false);
    }

    /// <summary>디버그: 다음 날씨로 (호스트 / 싱글)</summary>
    public void DebugCycle()
    {
        var rm = RoundManager.Instance;
        if (rm != null && rm.IsFollower) return;
        var next = (Weather)(((int)Current + 1) % 3);
        SetAndBroadcast(next, rm != null ? rm.CurrentRound : 0);
        Debug.Log($"[WeatherManager] 날씨: {next}");
    }

    // ───────── 적용 (모든 화면) ─────────

    private void Apply(Weather w, bool instant)
    {
        Current = w;
        bool rain = w != Weather.Moonlit;

        if (moonObject != null) moonObject.SetActive(w == Weather.Moonlit);
        SetRain(rain, instant);

        float targetVol = !rain ? 0f : rainVolume * (w == Weather.Storm ? stormRainVolumeMul : 1f);
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        if (instant || !isActiveAndEnabled) SetRainVolume(targetVol);
        else fadeRoutine = StartCoroutine(FadeRain(targetVol));

        Changed?.Invoke(w);
    }

    private void SetRain(bool on, bool instant)
    {
        if (rainObjects == null) return;
        foreach (var go in rainObjects)
        {
            if (go == null) continue;
            if (on)
            {
                go.SetActive(true);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    if (!ps.isPlaying) ps.Play(false);
            }
            else if (instant || !go.activeInHierarchy)
            {
                go.SetActive(false);
            }
            else
            {
                // 내리던 빗방울은 마저 떨어지고 멈춤
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    private void SetRainVolume(float v)
    {
        if (rainSource == null) return;
        rainSource.volume = v;
        if (v > 0.001f && rainLoop != null)
        {
            if (rainSource.clip != rainLoop) rainSource.clip = rainLoop;
            if (!rainSource.isPlaying) rainSource.Play();
        }
        else if (rainSource.isPlaying) rainSource.Stop();
    }

    private IEnumerator FadeRain(float target)
    {
        if (rainLoop != null && target > 0f && !rainSource.isPlaying)
        {
            rainSource.clip = rainLoop;
            rainSource.volume = 0f;
            rainSource.Play();
        }
        float start = rainSource.volume;
        float t = 0f;
        while (t < rainFadeTime)
        {
            t += Time.deltaTime;
            rainSource.volume = Mathf.Lerp(start, target, t / rainFadeTime);
            yield return null;
        }
        SetRainVolume(target);
        fadeRoutine = null;
    }
}
