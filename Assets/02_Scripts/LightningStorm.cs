using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 폭풍 날씨의 번개. WeatherManager와 같은 오브젝트에 붙인다.
///
///  - 폭풍(Storm) 라운드의 진행(Playing) 중에만 침
///    첫 번개는 진행 시작 후 First Strike Delay, 그 뒤로 Strike Interval 간격 (랜덤)
///  - 번개 1번 = 번개 이펙트(Narrow/Wide 중 랜덤, 건물 밖 하늘) + 섬광 + 천둥 + 정전
///  - 섬광: 창문 쪽 Directional Light(그림자 켜짐)를 번쩍임
///          RevealLight(Directional)를 자동으로 붙여, 창문 빛 자국 안의 캐릭터가 그 순간 보임
///  - 정전: 섬광 직후 PowerOutage (형광등·네온·비상구) Outage Duration초
///  - 시점·종류·위치는 호스트가 정해 모두에게 보냄 (RoundNetworkSync.LightningRpc) → 모든 화면에서 같은 번개
/// </summary>
[DisallowMultipleComponent]
public class LightningStorm : MonoBehaviour
{
    public static LightningStorm Instance { get; private set; }

    [Header("타이밍 (초)")]
    [Tooltip("진행 시작 후 첫 번개까지 (최소~최대)")]
    [SerializeField] private Vector2 firstStrikeDelay = new Vector2(8f, 15f);
    [Tooltip("그 다음 번개 간격 (최소~최대)")]
    [SerializeField] private Vector2 strikeInterval = new Vector2(20f, 40f);

    [Header("번개 이펙트 (씬의 Lightning_Narrow / Lightning_Wide)")]
    [SerializeField] private ParticleSystem[] boltEffects;
    [Tooltip("번개가 칠 영역의 중심 = 건물 (이 박스 바깥 링에서 랜덤)")]
    [SerializeField] private Vector3 buildingCenter = new Vector3(6.2f, 0f, -6.8f);
    [SerializeField] private Vector2 buildingSize = new Vector2(55f, 34f);
    [Tooltip("건물 벽에서 바깥으로 이 거리 사이 (m)")]
    [SerializeField] private Vector2 outsideDistance = new Vector2(12f, 40f);
    [Tooltip("번개 이펙트 높이 (y)")]
    [SerializeField] private float boltHeight = 17.3f;

    [Header("섬광")]
    [Tooltip("창문 쪽 Directional Light (Shadow 켜짐). 평소엔 꺼둠")]
    [SerializeField] private Light flashLight;
    [SerializeField, Min(0f)] private float flashIntensity = 3f;
    [Tooltip("이 밝기 이상일 때 빛 안 캐릭터가 보임")]
    [SerializeField, Min(0f)] private float revealMinIntensity = 0.5f;

    [Header("정전")]
    [SerializeField, Min(0f)] private float outageDuration = 5f;

    [Header("소리")]
    [Tooltip("천둥 (여러 개면 랜덤). 비우면 소리 없음")]
    [SerializeField] private AudioClip[] thunderClips;
    [SerializeField, Range(0f, 1f)] private float thunderVolume = 1f;
    [Tooltip("섬광 후 천둥까지 (최소~최대)")]
    [SerializeField] private Vector2 thunderDelay = new Vector2(0.3f, 1.5f);
    [Tooltip("정전 순간 소리 (선택)")]
    [SerializeField] private AudioClip powerOffClip;
    [SerializeField, Range(0f, 1f)] private float powerOffVolume = 0.8f;

    private AudioSource sfx;
    private float nextStrikeTime = -1f;
    private RoundManager hookedRound;
    private Coroutine strikeRoutine;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;
        var mixer = Resources.Load<AudioMixer>("MainMixer");
        if (mixer != null)
        {
            var groups = mixer.FindMatchingGroups("SFX");
            if (groups != null && groups.Length > 0) sfx.outputAudioMixerGroup = groups[0];
        }

        // 번개 이펙트: 자동 반복 끄고 대기
        if (boltEffects != null)
            foreach (var ps in boltEffects)
            {
                if (ps == null) continue;
                foreach (var p in ps.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = p.main;
                    main.loop = false;
                    main.playOnAwake = false;
                }
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

        // 섬광: 평소 꺼둠 + 노출 판정
        if (flashLight != null)
        {
            flashLight.enabled = false;
            if (!flashLight.TryGetComponent(out RevealLight reveal))
                reveal = flashLight.gameObject.AddComponent<RevealLight>();
            reveal.MinIntensity = revealMinIntensity;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (hookedRound != null) hookedRound.OnPlayStart -= HandlePlayStart;
    }

    private void Update()
    {
        var rm = RoundManager.Instance;
        if (rm != hookedRound)
        {
            if (hookedRound != null) hookedRound.OnPlayStart -= HandlePlayStart;
            hookedRound = rm;
            if (rm != null) rm.OnPlayStart += HandlePlayStart;
        }

        // 번개 시점은 호스트 / 싱글만 정함
        if (rm == null || rm.IsFollower) return;
        if (rm.CurrentState != RoundManager.State.Playing) return;
        var wm = WeatherManager.Instance;
        if (wm == null || wm.Current != WeatherManager.Weather.Storm) return;

        if (nextStrikeTime < 0f) nextStrikeTime = Time.time + Random.Range(firstStrikeDelay.x, firstStrikeDelay.y);
        if (Time.time >= nextStrikeTime)
        {
            nextStrikeTime = Time.time + Random.Range(strikeInterval.x, strikeInterval.y);
            HostStrike();
        }
    }

    private void HandlePlayStart(int round)
    {
        nextStrikeTime = -1f;   // 새 라운드: 첫 번개 타이머부터
    }

    // ───────── 번개 ─────────

    /// <summary>디버그: 지금 번개 (호스트 / 싱글, 날씨와 무관)</summary>
    public void DebugStrikeNow()
    {
        var rm = RoundManager.Instance;
        if (rm != null && rm.IsFollower) return;
        HostStrike();
    }

    private void HostStrike()
    {
        int kind = (boltEffects != null && boltEffects.Length > 0) ? Random.Range(0, boltEffects.Length) : -1;
        Vector3 pos = RandomOutsidePoint();
        int seed = Random.Range(int.MinValue, int.MaxValue);

        PlayStrike(kind, pos, seed);
        var ch = RoundNetworkSync.Channel;
        if (ch != null) ch.SendLightning(kind, pos, seed);
    }

    private Vector3 RandomOutsidePoint()
    {
        // 건물 박스 밖 링: 네 방향 중 하나 + 벽에서 바깥 거리
        Vector2 half = buildingSize * 0.5f;
        float d = Random.Range(outsideDistance.x, outsideDistance.y);
        int side = Random.Range(0, 4);
        float x, z;
        switch (side)
        {
            case 0: x = Random.Range(-half.x, half.x); z = half.y + d; break;    // 북
            case 1: x = Random.Range(-half.x, half.x); z = -half.y - d; break;   // 남
            case 2: x = -half.x - d; z = Random.Range(-half.y, half.y); break;   // 서
            default: x = half.x + d; z = Random.Range(-half.y, half.y); break;   // 동
        }
        return new Vector3(buildingCenter.x + x, boltHeight, buildingCenter.z + z);
    }

    /// <summary>번개 연출 (모든 화면에서 같은 값으로 실행)</summary>
    public void PlayStrike(int kind, Vector3 pos, int seed)
    {
        if (strikeRoutine != null) StopCoroutine(strikeRoutine);
        strikeRoutine = StartCoroutine(StrikeRoutine(kind, pos, seed));
    }

    private IEnumerator StrikeRoutine(int kind, Vector3 pos, int seed)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // 번개 이펙트
        if (boltEffects != null && kind >= 0 && kind < boltEffects.Length && boltEffects[kind] != null)
        {
            var bolt = boltEffects[kind];
            bolt.transform.position = pos;
            bolt.Clear(true);
            bolt.Play(true);
        }

        // 천둥 (섬광과 따로 진행)
        if (thunderClips != null && thunderClips.Length > 0)
            StartCoroutine(Thunder(R(thunderDelay.x, thunderDelay.y), thunderClips[rng.Next(thunderClips.Length)]));

        // 섬광: 번쩍 - 꺼짐 - 더 크게 번쩍 - 서서히 사라짐 (실제 번개의 여러 번 치는 느낌)
        if (flashLight != null)
        {
            flashLight.enabled = true;
            flashLight.intensity = flashIntensity * R(0.7f, 0.9f);
            yield return new WaitForSeconds(R(0.05f, 0.09f));
            flashLight.intensity = 0f;
            yield return new WaitForSeconds(R(0.04f, 0.08f));
            float peak = flashIntensity * R(1.0f, 1.2f);
            flashLight.intensity = peak;
            yield return new WaitForSeconds(R(0.1f, 0.15f));
            float fade = R(0.3f, 0.45f), t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                flashLight.intensity = Mathf.Lerp(peak, 0f, t / fade);
                yield return null;
            }
            flashLight.intensity = 0f;
            flashLight.enabled = false;
        }

        // 정전
        if (outageDuration > 0f)
        {
            PowerOutage.Trigger(outageDuration);
            if (powerOffClip != null && sfx != null) sfx.PlayOneShot(powerOffClip, powerOffVolume);
        }
        strikeRoutine = null;
    }

    private IEnumerator Thunder(float delay, AudioClip clip)
    {
        yield return new WaitForSeconds(delay);
        if (clip != null && sfx != null) sfx.PlayOneShot(clip, thunderVolume);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.7f, 1f, 0.5f);
        Vector3 c = new Vector3(buildingCenter.x, boltHeight, buildingCenter.z);
        Gizmos.DrawWireCube(c, new Vector3(buildingSize.x, 0.1f, buildingSize.y));
        Gizmos.color = new Color(1f, 1f, 0.4f, 0.3f);
        Gizmos.DrawWireCube(c, new Vector3(buildingSize.x + outsideDistance.y * 2f, 0.1f, buildingSize.y + outsideDistance.y * 2f));
    }
}
