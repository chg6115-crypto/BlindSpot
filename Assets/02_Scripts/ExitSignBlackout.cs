using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 비상구 표지판 근접 소등.
///  - Player 태그가 반경 안에 들어오면 "타닥-" 깜빡인 뒤 표지판 발광(Emission) + 라이트가 offDuration초 동안 꺼짐
///  - 다시 켜질 때 형광등처럼 몇 번 깜빡이며 켜짐 (선택)
///  - 마네킹(Mannequin 태그)에는 반응하지 않음
/// 사용법: 표지판 아래에 빈 자식 오브젝트(Sensor)를 만들고 여기에 붙임.
///         Sensor 레이어는 Ignore Raycast 권장 (총알·레이캐스트가 트리거에 맞지 않게)
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class ExitSignBlackout : MonoBehaviour
{
    [Header("대상 (비워두면 부모에서 자동 검색)")]
    [Tooltip("발광 머티리얼이 들어 있는 표지판 렌더러")]
    [SerializeField] private Renderer[] signRenderers;
    [Tooltip("표지판 주변을 비추는 라이트")]
    [SerializeField] private Light[] signLights;

    [Header("감지")]
    [SerializeField] private string targetTag = "Player";
    [SerializeField, Min(0.1f)] private float radius = 3f;

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

    private struct EmissiveSlot
    {
        public Renderer renderer;
        public int materialIndex;
        public Color baseColor;
    }

    private readonly List<EmissiveSlot> slots = new List<EmissiveSlot>();
    private float[] baseIntensities;
    private MaterialPropertyBlock block;

    private readonly HashSet<Collider> occupants = new HashSet<Collider>();
    private Coroutine routine;
    private float readyTime;

    // ───────── 초기화 ─────────

    private void Reset()
    {
        var col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = radius;
    }

    private void OnValidate()
    {
        var col = GetComponent<SphereCollider>();
        if (col != null) { col.isTrigger = true; col.radius = radius; }
    }

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
    }

    private void OnDisable()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        occupants.Clear();
        SetLit(true);
    }

    // ───────── 감지 ─────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(targetTag)) return;
        occupants.Add(other);
        TryBlackout();
    }

    private void OnTriggerExit(Collider other)
    {
        occupants.Remove(other);
    }

    private void TryBlackout()
    {
        if (routine != null) return;              // 이미 진행 중
        if (Time.time < readyTime) return;        // 쿨타임
        routine = StartCoroutine(BlackoutRoutine());
    }

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

        // 다시 켜질 때 형광등처럼 깜빡임
        if (flickerOnReturn)
        {
            for (int i = 0; i < flickerCount; i++)
            {
                SetLit(true);
                yield return new WaitForSeconds(flickerOnTime);
                SetLit(false);
                yield return new WaitForSeconds(flickerOffTime);
            }
        }

        SetLit(true);
        readyTime = Time.time + cooldown;
        routine = null;

        // 계속 안에 있으면 쿨타임 뒤 다시 소등
        if (retriggerWhileInside && occupants.Count > 0)
        {
            yield return new WaitForSeconds(cooldown);
            occupants.RemoveWhere(c => c == null || !c.gameObject.activeInHierarchy);
            if (occupants.Count > 0) TryBlackout();
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
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}