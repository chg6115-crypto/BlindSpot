using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 비상구 표지판 근접 소등.
///  - 플레이어가 반경 안에 들어오면 "타닥-" 깜빡인 뒤 표지판 발광(Emission) + 라이트가 offDuration초 동안 꺼짐
///  - 다시 켜질 때 형광등처럼 몇 번 깜빡이며 켜짐 (선택)
///  - 모든 플레이어 위치로 판정 (PlayerNetworkSetup.All) → 누가 다가가든 모든 화면에서 같은 표지판이 꺼짐
///    (태그/트리거는 화면마다 내 캐릭터만 Player라서 쓰지 않음. 네트워크 메시지 없음)
///  - 살아 있는 플레이어만. 마네킹·디버그 봇에는 반응하지 않음
///  - 싱글(네트워크 플레이어 없음)일 때는 Target Tag 오브젝트로 판정
///  - ForceOff / ForceOffAll: 외부(번개 등)에서 N초 동안 강제 소등 → 끝나면 깜빡이며 복귀
/// 사용법: 표지판 아래에 빈 자식 오브젝트(Sensor)를 만들고 여기에 붙임. (예전 SphereCollider는 있어도 무시됨)
///         표지판 라이트에 RevealLight를 붙이면 켜져 있는 동안 빛 안 캐릭터가 보임 (꺼지면 자동으로 노출도 꺼짐)
/// </summary>
public class ExitSignBlackout : MonoBehaviour
{
    [Header("대상 (비워두면 부모에서 자동 검색)")]
    [Tooltip("발광 머티리얼이 들어 있는 표지판 렌더러")]
    [SerializeField] private Renderer[] signRenderers;
    [Tooltip("표지판 주변을 비추는 라이트")]
    [SerializeField] private Light[] signLights;

    [Header("감지")]
    [Tooltip("싱글(네트워크 플레이어 없음)일 때만 사용하는 태그")]
    [SerializeField] private string targetTag = "Player";
    [Tooltip("Sensor에서 이 거리(m) 안에 플레이어 몸(발~머리)이 들어오면 소등")]
    [SerializeField, Min(0.1f)] private float radius = 3f;
    [Tooltip("판정 간격 (초)")]
    [SerializeField, Min(0.02f)] private float checkInterval = 0.1f;
    [Tooltip("플레이어 키 (발 위치에서 이만큼 위까지를 몸으로 봄)")]
    [SerializeField, Min(0.1f)] private float playerHeight = 1.8f;

    [Header("소등")]
    [Tooltip("완전히 꺼져 있는 시간 (깜빡임 시간 제외)")]
    [SerializeField, Min(0f)] private float offDuration = 3f;
    [Tooltip("다시 켜진 뒤 이 시간 동안은 재소등 안 함")]
    [SerializeField, Min(0f)] private float cooldown = 1f;
    [Tooltip("켜진 뒤에도 플레이어가 계속 안에 있으면 다시 꺼짐")]
    [SerializeField] private bool retriggerWhileInside = false;

    [Header("꺼질 때 깜빡임")]
    [Tooltip("꺼지기 전 '타닥' 횟수 (0이면 바로 꺼짐)")]
    [SerializeField, Min(0)] private int flickerOffCount = 2;
    [Tooltip("깜빡일 때 꺼져 있는 시간")]
    [SerializeField, Min(0.01f)] private float flickerOffBlinkOff = 0.05f;
    [Tooltip("깜빡일 때 다시 켜져 있는 시간")]
    [SerializeField, Min(0.01f)] private float flickerOffBlinkOn = 0.07f;

    [Header("다시 켜질 때 깜빡임")]
    [SerializeField] private bool flickerOnReturn = true;
    [SerializeField, Min(0)] private int flickerCount = 3;
    [SerializeField, Min(0.01f)] private float flickerOnTime = 0.06f;
    [SerializeField, Min(0.01f)] private float flickerOffTime = 0.1f;

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly List<ExitSignBlackout> all = new List<ExitSignBlackout>();

    private struct EmissiveSlot
    {
        public Renderer renderer;
        public int materialIndex;
        public Color baseColor;
    }

    private readonly List<EmissiveSlot> slots = new List<EmissiveSlot>();
    private float[] baseIntensities;
    private MaterialPropertyBlock block;

    private Coroutine routine;
    private float readyTime;
    private float nextCheck;
    private bool wasInside;
    private bool forced;
    private float forcedUntil;

    /// <summary>지금 꺼져 있는(소등 중인) 상태인지</summary>
    public bool IsBlackedOut => routine != null || forced;

    // ───────── 외부 강제 소등 (번개 등) ─────────

    /// <summary>씬의 모든 비상구 표지판을 duration초 동안 강제로 끔</summary>
    public static void ForceOffAll(float duration)
    {
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (all[i] == null) { all.RemoveAt(i); continue; }
            all[i].ForceOff(duration);
        }
    }

    /// <summary>이 표지판을 duration초 동안 강제로 끔 (근접 소등 중이면 중단하고 덮어씀). 끝나면 깜빡이며 복귀</summary>
    public void ForceOff(float duration)
    {
        if (!isActiveAndEnabled) return;
        float until = Time.time + Mathf.Max(0f, duration);
        if (forced)
        {
            forcedUntil = Mathf.Max(forcedUntil, until);   // 진행 중이면 연장만
            return;
        }

        if (routine != null) StopCoroutine(routine);
        forced = true;
        forcedUntil = until;
        routine = StartCoroutine(ForcedRoutine());
    }

    private IEnumerator ForcedRoutine()
    {
        SetLit(false);
        while (Time.time < forcedUntil) yield return null;

        yield return ReturnFlicker();
        SetLit(true);
        forced = false;
        readyTime = Time.time + cooldown;
        routine = null;
        wasInside = AnyPlayerInside();   // 복귀 직후 바로 다시 꺼지지 않게 (새로 들어와야 소등)
    }

    // ───────── 초기화 ─────────

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        var parent = transform.parent != null ? transform.parent : transform;
        if (signRenderers == null || signRenderers.Length == 0)
            signRenderers = parent.GetComponentsInChildren<Renderer>(true);
        if (signLights == null || signLights.Length == 0)
            signLights = parent.GetComponentsInChildren<Light>(true);

        // 발광하는 머티리얼 칸만 기록
        foreach (var r in signRenderers)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.HasProperty(EmissionId) || !m.IsKeywordEnabled("_EMISSION")) continue;
                slots.Add(new EmissiveSlot { renderer = r, materialIndex = i, baseColor = m.GetColor(EmissionId) });
            }
        }

        baseIntensities = new float[signLights.Length];
        for (int i = 0; i < signLights.Length; i++)
            if (signLights[i] != null) baseIntensities[i] = signLights[i].intensity;

        if (slots.Count == 0 && signLights.Length == 0)
            Debug.LogWarning("[ExitSignBlackout] 발광 머티리얼(Emission 켜짐)이나 라이트를 찾지 못했습니다.", this);

        // 예전 방식의 트리거는 더 이상 사용하지 않음 (총알·레이캐스트에도 안 걸리게 끔)
        if (TryGetComponent(out SphereCollider oldTrigger) && oldTrigger.isTrigger) oldTrigger.enabled = false;
    }

    private void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
    }

    private void OnDisable()
    {
        all.Remove(this);
        if (routine != null) StopCoroutine(routine);
        routine = null;
        forced = false;
        wasInside = false;
        SetLit(true);
    }

    // ───────── 감지 (모든 플레이어 위치) ─────────

    private void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + checkInterval;

        bool inside = AnyPlayerInside();
        bool entered = inside && !wasInside;
        wasInside = inside;

        if (forced || routine != null) return;    // 이미 꺼져 있음
        if (Time.time < readyTime) return;        // 쿨타임

        if (entered || (retriggerWhileInside && inside))
            routine = StartCoroutine(BlackoutRoutine());
    }

    private bool AnyPlayerInside()
    {
        Vector3 center = transform.position;
        float r = radius * MaxScale(transform.lossyScale);
        float r2 = r * r;

        var players = PlayerNetworkSetup.All;
        if (players != null && players.Count > 0)
        {
            foreach (var p in players)
            {
                if (p == null || !p.isActiveAndEnabled) continue;
                var h = p.GetComponent<Health>();
                if (h != null && h.IsDead) continue;
                if (BodyDistanceSqr(center, p.transform.position) <= r2) return true;
            }
            return false;
        }

        // 싱글: 태그로
        if (string.IsNullOrEmpty(targetTag)) return false;
        GameObject[] tagged;
        try { tagged = GameObject.FindGameObjectsWithTag(targetTag); }
        catch (UnityException) { return false; }   // 없는 태그
        foreach (var go in tagged)
        {
            if (go == null) continue;
            var h = go.GetComponent<Health>();
            if (h != null && h.IsDead) continue;
            if (BodyDistanceSqr(center, go.transform.position) <= r2) return true;
        }
        return false;
    }

    /// <summary>Sensor에서 플레이어 몸(발~머리 선분)까지 가장 가까운 거리²</summary>
    private float BodyDistanceSqr(Vector3 center, Vector3 feet)
    {
        float y = Mathf.Clamp(center.y, feet.y, feet.y + playerHeight);
        Vector3 closest = new Vector3(feet.x, y, feet.z);
        return (closest - center).sqrMagnitude;
    }

    private static float MaxScale(Vector3 s) => Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));

    // ───────── 타닥 → 소등 → 깜빡이며 복귀 ─────────

    private IEnumerator BlackoutRoutine()
    {
        // 꺼지기 전 '타닥-' : 꺼짐 → 켜짐 을 몇 번 반복한 뒤 완전히 꺼짐
        for (int i = 0; i < flickerOffCount; i++)
        {
            SetLit(false);
            yield return new WaitForSeconds(flickerOffBlinkOff);
            SetLit(true);
            yield return new WaitForSeconds(flickerOffBlinkOn);
        }

        SetLit(false);
        yield return new WaitForSeconds(offDuration);

        yield return ReturnFlicker();

        SetLit(true);
        readyTime = Time.time + cooldown;
        routine = null;
    }

    /// <summary>다시 켜질 때 형광등처럼 깜빡임</summary>
    private IEnumerator ReturnFlicker()
    {
        if (!flickerOnReturn) yield break;
        for (int i = 0; i < flickerCount; i++)
        {
            SetLit(true);
            yield return new WaitForSeconds(flickerOnTime);
            SetLit(false);
            yield return new WaitForSeconds(flickerOffTime);
        }
    }

    private void SetLit(bool lit)
    {
        foreach (var s in slots)
        {
            if (s.renderer == null) continue;
            s.renderer.GetPropertyBlock(block, s.materialIndex);
            block.SetColor(EmissionId, lit ? s.baseColor : Color.black);
            s.renderer.SetPropertyBlock(block, s.materialIndex);
        }

        if (signLights == null) return;
        for (int i = 0; i < signLights.Length; i++)
        {
            var l = signLights[i];
            if (l == null) continue;
            l.intensity = baseIntensities[i];
            l.enabled = lit;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, radius * MaxScale(transform.lossyScale));
    }
}
