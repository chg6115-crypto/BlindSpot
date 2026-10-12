using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소리 → 주변 외곽선 표시 관리. GameManager 아래에 하나만 배치.
///  ① 펄스 (Emit)     : 총소리 등 한 번 퍼졌다 사라짐 (expand → hold → fade)
///  ② 필드 (SetField) : 이동 소리처럼 실시간으로 유지/추적 (위치·반경·밝기를 매 프레임 갱신)
/// 셰이더(BlindSpot/SonarOutline)에 전역 배열로 전달. 필드 + 펄스 합쳐 최대 MaxPulses개.
/// </summary>
[DefaultExecutionOrder(-90)]
public class SoundPulseManager : MonoBehaviour
{
    public const int MaxPulses = 16;   // 셰이더의 MAX_PULSES와 같아야 함

    public static SoundPulseManager Instance { get; private set; }

    /// <summary>펄스 소리가 날 때마다 호출 (나중에 AI 청각 등에 사용)</summary>
    public static event Action<Vector3, float, GameObject> OnSound;

    [Header("펄스 모양 (초)")]
    [Tooltip("반경이 0 → 목표 반경까지 퍼지는 시간")]
    [SerializeField, Min(0.01f)] private float expandTime = 0.8f;
    [Tooltip("다 퍼진 뒤 최대 밝기로 유지되는 시간")]
    [SerializeField, Min(0f)] private float holdTime = 0.5f;
    [Tooltip("서서히 사라지는 시간")]
    [SerializeField, Min(0.01f)] private float fadeTime = 2f;

    private struct Pulse
    {
        public Vector3 position;
        public float radius;
        public float age;
        public GameObject source;
    }

    private struct Field
    {
        public Vector3 position;
        public float radius;
        public float intensity;
    }

    private readonly List<Pulse> pulses = new List<Pulse>();
    private readonly Dictionary<GameObject, Field> fields = new Dictionary<GameObject, Field>();
    private readonly List<GameObject> deadKeys = new List<GameObject>();

    private readonly Vector4[] shaderPulses = new Vector4[MaxPulses];
    private readonly Vector4[] shaderData = new Vector4[MaxPulses];

    private static readonly int PulsesId = Shader.PropertyToID("_SonarPulses");
    private static readonly int DataId = Shader.PropertyToID("_SonarPulseData");
    private static readonly int CountId = Shader.PropertyToID("_SonarPulseCount");

    private float Lifetime => expandTime + holdTime + fadeTime;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        Upload(0);
    }

    private void OnDestroy()
    {
        if (Instance == this) { Instance = null; Shader.SetGlobalInt(CountId, 0); }
    }

    // ───────── 외부 호출 ─────────

    /// <summary>① 펄스: 한 번 퍼졌다 사라지는 소리 (총소리 등)</summary>
    public static void Emit(Vector3 position, float radius, GameObject source = null)
    {
        OnSound?.Invoke(position, radius, source);
        if (Instance != null) Instance.AddPulse(position, radius, source);
    }

    /// <summary>
    /// ② 필드: 소유자별로 하나씩 유지되는 실시간 소리 범위 (이동 소리 등).
    /// 매 프레임 호출해서 위치/반경/밝기 갱신. intensity가 0 이하면 제거.
    /// </summary>
    public static void SetField(GameObject owner, Vector3 position, float radius, float intensity)
    {
        if (Instance == null || owner == null) return;

        if (intensity <= 0.001f || radius <= 0.001f)
        {
            Instance.fields.Remove(owner);
            return;
        }
        Instance.fields[owner] = new Field { position = position, radius = radius, intensity = Mathf.Clamp01(intensity) };
    }

    private void AddPulse(Vector3 position, float radius, GameObject source)
    {
        var p = new Pulse { position = position, radius = radius, age = 0f, source = source };

        if (pulses.Count < MaxPulses) { pulses.Add(p); return; }

        // 가득 찼으면 가장 오래된 펄스를 교체
        int oldest = 0;
        for (int i = 1; i < pulses.Count; i++)
            if (pulses[i].age > pulses[oldest].age) oldest = i;
        pulses[oldest] = p;
    }

    // ───────── 매 프레임 ─────────

    private void LateUpdate()   // 필드는 각 오브젝트 Update에서 갱신되므로 그 이후에 업로드
    {
        // 파괴된 소유자(라운드 전환으로 삭제된 마네킹 등) 정리
        deadKeys.Clear();
        foreach (var key in fields.Keys) if (key == null) deadKeys.Add(key);
        foreach (var key in deadKeys) fields.Remove(key);

        float dt = Time.deltaTime;
        float life = Lifetime;

        // 펄스 수명 처리
        for (int i = pulses.Count - 1; i >= 0; i--)
        {
            var p = pulses[i];
            p.age += dt;
            if (p.age >= life) { pulses.RemoveAt(i); continue; }
            pulses[i] = p;
        }

        int n = 0;

        // ② 필드 먼저 (항상 표시되어야 하므로)
        foreach (var f in fields.Values)
        {
            if (n >= MaxPulses) break;
            shaderPulses[n] = new Vector4(f.position.x, f.position.y, f.position.z, f.radius);
            shaderData[n] = new Vector4(f.intensity, 0f, 0f, 0f);   // 링 강조 없음
            n++;
        }

        // ① 펄스
        foreach (var p in pulses)
        {
            if (n >= MaxPulses) break;

            // 퍼짐: 빠르게 시작해서 부드럽게 멈춤 (ease-out)
            float e = Mathf.Clamp01(p.age / expandTime);
            float radiusNow = p.radius * (1f - (1f - e) * (1f - e));

            // 밝기: expand+hold 동안 1 → fade 동안 0
            float fadeT = Mathf.Clamp01((p.age - expandTime - holdTime) / fadeTime);
            float intensity = 1f - fadeT;

            // 앞쪽 링 강조: 퍼지는 동안만
            float ring = 1f - e;

            shaderPulses[n] = new Vector4(p.position.x, p.position.y, p.position.z, radiusNow);
            shaderData[n] = new Vector4(intensity, ring, 0f, 0f);
            n++;
        }

        Upload(n);
    }

    private void Upload(int count)
    {
        for (int i = count; i < MaxPulses; i++) { shaderPulses[i] = Vector4.zero; shaderData[i] = Vector4.zero; }
        Shader.SetGlobalVectorArray(PulsesId, shaderPulses);
        Shader.SetGlobalVectorArray(DataId, shaderData);
        Shader.SetGlobalInt(CountId, count);
    }
}