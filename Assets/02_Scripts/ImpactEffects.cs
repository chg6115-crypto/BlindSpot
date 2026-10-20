using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 총알 착탄 이펙트 / 탄흔 / 총구 화염. 씬에 하나 (GameManager 아래 등).
///  - 표면(SurfaceType)별 착탄 이펙트: Stone / Metal / Wood / Flesh (ParticlePack Weapon Effects)
///    Spawn Particles를 끄면 파티클 없이 탄흔(+소리)만 남김 (기본: 전부 끔)
///  - 탄흔: 맞은 면에 작은 판을 붙여 라운드 끝까지 남김 (최대 Max Decals개, 넘으면 오래된 것부터 재사용)
///    탄흔 그림은 각 착탄 프리팹 안의 "Decal" 파티클 머티리얼을 그대로 사용. 사람(Flesh)은 탄흔 없음
///  - 총구 화염: 쏠 때 총구에서 (Muzzle Flash Prefab)
///  - 전부 미리 만들어 두고 돌려 씀 (오브젝트 풀링) → 연사해도 생성/삭제 없음
///  - 라운드가 시작되면 탄흔 전부 지움
///  - 착탄 프리팹 루트의 데모용 구체(MeshRenderer + Collider)는 풀에서 꺼둠
/// </summary>
[DisallowMultipleComponent]
public class ImpactEffects : MonoBehaviour
{
    public static ImpactEffects Instance { get; private set; }

    [System.Serializable]
    public class Surface
    {
        [Tooltip("착탄 이펙트 프리팹 (탄흔 머티리얼은 이 프리팹의 \"Decal\" 자식에서 가져옴)")]
        public GameObject prefab;
        [Tooltip("착탄 파티클(파편·먼지·피 안개)을 띄울지. 끄면 탄흔만")]
        public bool spawnParticles = false;
        [Tooltip("탄흔 크기 (최소~최대, m). 0이면 탄흔 없음")]
        public Vector2 decalSize = new Vector2(0.1f, 0.15f);
        [Tooltip("착탄 소리 (여러 개면 랜덤). 비우면 없음")]
        public AudioClip[] clips;
        [HideInInspector] public GameObject[] pool;
        [HideInInspector] public int next;
        [HideInInspector] public Material decalMaterial;
    }

    [Header("표면별 (Stone / Metal / Wood / Flesh 순서)")]
    [SerializeField] private Surface stone = new Surface { decalSize = new Vector2(0.1f, 0.15f) };
    [SerializeField] private Surface metal = new Surface { decalSize = new Vector2(0.06f, 0.1f) };
    [SerializeField] private Surface wood = new Surface { decalSize = new Vector2(0.1f, 0.2f) };
    [SerializeField] private Surface flesh = new Surface { decalSize = Vector2.zero };
    [Tooltip("표면별로 미리 만들어 둘 착탄 이펙트 수")]
    [SerializeField, Min(1)] private int poolPerSurface = 8;
    [Tooltip("사람 피격 이펙트 크기 배율 (짧고 작게)")]
    [SerializeField, Min(0.1f)] private float fleshScale = 0.6f;

    [Header("탄흔")]
    [SerializeField, Min(0)] private int maxDecals = 32;
    [Tooltip("면에서 살짝 띄우는 거리 (깜빡임 방지)")]
    [SerializeField, Min(0f)] private float decalOffset = 0.004f;

    [Header("총구 화염")]
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField, Min(1)] private int muzzlePoolSize = 8;
    [SerializeField, Min(0.1f)] private float muzzleFlashScale = 1f;

    [Header("수명")]
    [Tooltip("이펙트를 재생하고 이 시간(초) 뒤에 꺼서 풀로 돌려놓음")]
    [SerializeField, Min(0.1f)] private float effectLifetime = 2f;

    [Header("소리")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.7f;
    [SerializeField, Min(1f)] private float hearDistance = 20f;
    [SerializeField, Min(1)] private int audioSourceCount = 6;

    private Surface[] surfaces;
    private GameObject[] muzzlePool;
    private int muzzleNext;
    private Transform[] decals;
    private MeshRenderer[] decalRenderers;
    private int decalNext;
    private AudioSource[] audioPool;
    private int audioNext;
    private Transform poolRoot;
    private readonly Dictionary<GameObject, float> activeUntil = new Dictionary<GameObject, float>();
    private readonly List<GameObject> expired = new List<GameObject>();
    private RoundManager hookedRound;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        poolRoot = new GameObject("[ImpactPool]").transform;
        poolRoot.SetParent(transform, false);

        surfaces = new[] { stone, metal, wood, flesh };
        for (int i = 0; i < surfaces.Length; i++)
            BuildSurfacePool(surfaces[i], i == (int)SurfaceKind.Flesh ? fleshScale : 1f);

        if (muzzleFlashPrefab != null)
        {
            muzzlePool = new GameObject[muzzlePoolSize];
            for (int i = 0; i < muzzlePoolSize; i++)
            {
                var go = Instantiate(muzzleFlashPrefab, poolRoot);
                go.transform.localScale *= muzzleFlashScale;
                PrepareEffect(go);
                muzzlePool[i] = go;
            }
        }

        BuildDecals();
        BuildAudio();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (hookedRound != null) hookedRound.OnRoundStart -= HandleRoundStart;
    }

    private void Update()
    {
        var rm = RoundManager.Instance;
        if (rm != hookedRound)
        {
            if (hookedRound != null) hookedRound.OnRoundStart -= HandleRoundStart;
            hookedRound = rm;
            if (rm != null) rm.OnRoundStart += HandleRoundStart;
        }

        // 수명이 끝난 이펙트 끄기
        if (activeUntil.Count == 0) return;
        float now = Time.time;
        expired.Clear();
        foreach (var kv in activeUntil) if (now >= kv.Value) expired.Add(kv.Key);
        foreach (var go in expired)
        {
            activeUntil.Remove(go);
            if (go != null) go.SetActive(false);
        }
    }

    private void HandleRoundStart(int round) => ClearDecals();

    private void BuildSurfacePool(Surface s, float scale)
    {
        if (s.prefab == null) return;

        // 탄흔 머티리얼: 프리팹 안 "Decal" 파티클의 머티리얼
        foreach (var r in s.prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
            if (r.gameObject.name == "Decal" && r.sharedMaterial != null) { s.decalMaterial = r.sharedMaterial; break; }

        if (!s.spawnParticles) return;   // 파티클 없이 탄흔만 → 이펙트 풀 안 만듦
        s.pool = new GameObject[poolPerSurface];
        for (int i = 0; i < poolPerSurface; i++)
        {
            var go = Instantiate(s.prefab, poolRoot);
            go.transform.localScale *= scale;

            // 프리팹 안 "Decal" 파티클(3초짜리 탄흔)은 끄고, 그 머티리얼을 오래 남는 탄흔에 사용
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.gameObject.name != "Decal") continue;
                ps.gameObject.SetActive(false);   // 3초짜리 탄흔 파티클 대신 오래 남는 탄흔 사용
            }
            PrepareEffect(go);
            s.pool[i] = go;
        }
    }

    /// <summary>반복 재생 끄고 대기 상태로 (데모용 구체 메시·콜라이더는 끔)</summary>
    private static void PrepareEffect(GameObject go)
    {
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.stopAction = ParticleSystemStopAction.None;
        }
        foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
        go.SetActive(false);
    }

    private void BuildDecals()
    {
        decals = new Transform[maxDecals];
        decalRenderers = new MeshRenderer[maxDecals];
        int layer = LayerMask.NameToLayer("NoSonar");   // 소나 외곽선에 탄흔 테두리가 생기지 않게
        if (layer < 0) layer = 0;
        for (int i = 0; i < maxDecals; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "BulletHole";
            Destroy(q.GetComponent<Collider>());
            q.layer = layer;
            q.transform.SetParent(poolRoot, false);
            var mr = q.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            q.SetActive(false);
            decals[i] = q.transform;
            decalRenderers[i] = mr;
        }
    }

    private void BuildAudio()
    {
        audioPool = new AudioSource[audioSourceCount];
        AudioMixerGroup group = null;
        var mixer = Resources.Load<AudioMixer>("MainMixer");
        if (mixer != null)
        {
            var g = mixer.FindMatchingGroups("SFX");
            if (g != null && g.Length > 0) group = g[0];
        }
        for (int i = 0; i < audioSourceCount; i++)
        {
            var go = new GameObject("ImpactAudio");
            go.transform.SetParent(poolRoot, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 1f;
            a.maxDistance = hearDistance;
            a.outputAudioMixerGroup = group;
            audioPool[i] = a;
        }
    }

    // ───────── 외부 호출 ─────────

    /// <summary>착탄 (맞은 지점, 면 방향, 표면)</summary>
    public void PlayImpact(Vector3 point, Vector3 normal, SurfaceKind kind)
    {
        if (surfaces == null) return;
        var s = surfaces[(int)kind];
        if (normal.sqrMagnitude < 0.0001f) normal = Vector3.up;
        normal.Normalize();

        if (s.pool != null && s.pool.Length > 0)
        {
            var go = s.pool[s.next];
            s.next = (s.next + 1) % s.pool.Length;
            Play(go, point + normal * 0.01f, Quaternion.LookRotation(normal));
        }

        if (s.decalMaterial != null && s.decalSize.y > 0f) PlaceDecal(point, normal, s);
        PlaySound(s.clips, point);
    }

    /// <summary>총구 화염 (총구 위치, 쏘는 방향)</summary>
    public void PlayMuzzleFlash(Vector3 position, Vector3 direction)
    {
        if (muzzlePool == null || muzzlePool.Length == 0) return;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
        var go = muzzlePool[muzzleNext];
        muzzleNext = (muzzleNext + 1) % muzzlePool.Length;
        Play(go, position, Quaternion.LookRotation(direction.normalized));
    }

    /// <summary>탄흔 전부 지움 (라운드 시작 때 자동)</summary>
    public void ClearDecals()
    {
        if (decals == null) return;
        foreach (var d in decals) if (d != null) d.gameObject.SetActive(false);
        decalNext = 0;
    }

    // ───────── 내부 ─────────

    private void Play(GameObject go, Vector3 pos, Quaternion rot)
    {
        // 끈 상태에서 옮겨야 이전 위치에서 새 위치까지 파티클이 줄줄이 생기지 않음
        go.SetActive(false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.SetActive(true);
        activeUntil[go] = Time.time + effectLifetime;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Clear(false);
            ps.Play(false);
        }
    }

    private void PlaceDecal(Vector3 point, Vector3 normal, Surface s)
    {
        if (decals.Length == 0) return;
        int i = decalNext;
        decalNext = (decalNext + 1) % decals.Length;   // 다 차면 가장 오래된 것부터 재사용

        var t = decals[i];
        float size = Random.Range(s.decalSize.x, s.decalSize.y);
        // Quad는 -Z 쪽이 앞면 → -Z가 면 바깥(normal)을 보도록
        t.SetPositionAndRotation(point + normal * decalOffset,
                                 Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        t.localScale = new Vector3(size, size, 1f);
        decalRenderers[i].sharedMaterial = s.decalMaterial;
        t.gameObject.SetActive(true);
    }

    private void PlaySound(AudioClip[] clips, Vector3 point)
    {
        if (clips == null || clips.Length == 0 || audioPool == null) return;
        var clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        var a = audioPool[audioNext];
        audioNext = (audioNext + 1) % audioPool.Length;
        a.transform.position = point;
        a.PlayOneShot(clip, volume);
    }
}
