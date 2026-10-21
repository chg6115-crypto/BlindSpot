using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 노출 조명. 캐릭터를 드러내는 라이트(형광등, 트랩 라이트 등)의 Light 오브젝트에 붙인다.
///
///  - 이 라이트가 켜져 있고(Min Intensity 이상) 범위 안 + 라이트와 사이에 벽이 없으면 캐릭터 몸이 보임
///    (CharacterVisibility가 IsPointLit으로 확인. 플레이어 / 마네킹 / 봇 공통)
///  - Point: 반경(Range) 안 / Spot: 원뿔(Spot Angle) 안
///  - Directional: 빛이 오는 방향(하늘 쪽)으로 Directional Reach 거리까지 막힌 게 없으면 (창문 빛 자국, 실외)
///  - Area는 지원 안 함
///  - LightPassThrough가 붙은 물체(창문 유리 등)는 빛을 막지 않음
///  - 깜빡이는 라이트(NeonLightFlicker 등)는 켜진 순간에만 보임
///  - 라이트가 캐릭터 몸도 비추도록 Rendering Layer를 전부로 설정 (Light Characters)
///  - TrapBase는 라이트에 이 컴포넌트를 자동으로 붙임
///  - Draw Debug: 플레이 중 Scene 뷰에 판정 결과를 선으로 표시 (캐릭터 가슴/머리 높이)
///      초록 = 드러남 / 노랑 = 원뿔 밖 / 빨강 = 막힘(막은 물체 이름 표시) / 회색 = 라이트 꺼짐·어두움
///      범위(Range) 밖 캐릭터는 그리지 않음
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public class RevealLight : MonoBehaviour
{
    [Tooltip("라이트 밝기(Intensity)가 이 값 이상일 때만 캐릭터가 보임")]
    [SerializeField, Min(0f)] private float minIntensity = 0.1f;
    [Tooltip("라이트가 캐릭터 몸도 비추게 (Rendering Layer 전부)")]
    [SerializeField] private bool lightCharacters = true;
    [Tooltip("벽 검사에서 무시할 오브젝트 (등 기구 자체 콜라이더 등). 비우면 부모 오브젝트")]
    [SerializeField] private Transform ignoreRoot;
    [Tooltip("Directional 라이트: 빛이 오는 쪽으로 이 거리(m)까지 막힌 게 없으면 비춰지는 것으로 봄")]
    [SerializeField, Min(1f)] private float directionalReach = 200f;

    [Header("디버그")]
    [Tooltip("플레이 중 Scene 뷰에 캐릭터별 판정 결과를 선으로 표시 (Gizmos 버튼이 켜져 있어야 보임)")]
    [SerializeField] private bool drawDebug = false;

    /// <summary>판정 결과</summary>
    public enum LitResult { Lit, LightOff, OutOfRange, OutOfCone, Blocked, Unsupported }

    private static readonly List<RevealLight> all = new List<RevealLight>();
    private static readonly RaycastHit[] hits = new RaycastHit[8];
    private const int CharacterLayer = 6;

    // CharacterVisibility와 같은 판정 높이 (발밑 기준)
    private static readonly float[] CheckHeights = { 1.0f, 1.6f };

    private Light lightComp;

    /// <summary>밝기 기준 (TrapBase가 설정)</summary>
    public float MinIntensity { get => minIntensity; set => minIntensity = Mathf.Max(0f, value); }

    // ───────── 등록 ─────────

    private void Awake()
    {
        lightComp = GetComponent<Light>();
        if (ignoreRoot == null) ignoreRoot = transform.parent != null ? transform.parent : transform;

        if (lightCharacters)
        {
            lightComp.renderingLayerMask = -1;
            if (TryGetComponent(out UniversalAdditionalLightData data))
                data.renderingLayers = uint.MaxValue;
        }
    }

    private void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
    }

    private void OnDisable()
    {
        all.Remove(this);
    }

    // ───────── 판정 ─────────

    /// <summary>이 위치가 켜진 노출 조명에 비춰지는지</summary>
    public static bool IsPointLit(Vector3 point)
    {
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var r = all[i];
            if (r == null) { all.RemoveAt(i); continue; }
            if (r.Evaluate(point, out _) == LitResult.Lit) return true;
        }
        return false;
    }

    /// <summary>이 라이트 하나 기준으로 판정 (blocker: 막은 콜라이더)</summary>
    public LitResult Evaluate(Vector3 point, out Collider blocker)
    {
        blocker = null;
        var l = lightComp != null ? lightComp : (lightComp = GetComponent<Light>());
        if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || l.intensity < minIntensity || l.intensity <= 0.001f)
            return LitResult.LightOff;

        if (l.type == LightType.Directional)
        {
            Vector3 toLight = -l.transform.forward;
            blocker = FindBlocker(point, toLight, directionalReach);
            return blocker != null ? LitResult.Blocked : LitResult.Lit;
        }

        Vector3 lp = l.transform.position;
        Vector3 d = point - lp;
        float dist = d.magnitude;
        if (dist > l.range) return LitResult.OutOfRange;
        if (dist < 0.05f) return LitResult.Lit;
        Vector3 dir = d / dist;

        switch (l.type)
        {
            case LightType.Point:
                break;
            case LightType.Spot:
                if (Vector3.Angle(l.transform.forward, dir) > l.spotAngle * 0.5f) return LitResult.OutOfCone;
                break;
            default:
                return LitResult.Unsupported;   // Area 미지원
        }

        blocker = FindBlocker(lp, dir, dist);
        return blocker != null ? LitResult.Blocked : LitResult.Lit;
    }

    /// <summary>빛을 막는 콜라이더 (없으면 null)</summary>
    private Collider FindBlocker(Vector3 from, Vector3 dir, float dist)
    {
        int n = Physics.RaycastNonAlloc(from, dir, hits, dist, ~(1 << CharacterLayer), QueryTriggerInteraction.Ignore);
        Collider nearest = null;
        float nearestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var col = hits[i].collider;
            if (col == null) continue;
            if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot)) continue;   // 등 기구 / 트랩 자신
            if (col.GetComponentInParent<Health>() != null) continue;                 // 캐릭터/마네킹은 벽이 아님
            if (col.GetComponentInParent<LightPassThrough>() != null) continue;       // 창문 유리 등 빛이 통과하는 것
            if (hits[i].distance < nearestDist) { nearestDist = hits[i].distance; nearest = col; }
        }
        return nearest;
    }

    // ───────── 디버그 표시 ─────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawDebug || !Application.isPlaying) return;
        var l = lightComp != null ? lightComp : GetComponent<Light>();
        if (l == null) return;

        // 원뿔 / 범위 윤곽
        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.35f);
        if (l.type == LightType.Spot)
        {
            float r = Mathf.Tan(l.spotAngle * 0.5f * Mathf.Deg2Rad) * l.range;
            Vector3 c = l.transform.position + l.transform.forward * l.range;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Vector3 off = (l.transform.right * Mathf.Cos(a) + l.transform.up * Mathf.Sin(a)) * r;
                Gizmos.DrawLine(l.transform.position, c + off);
            }
        }
        else if (l.type == LightType.Point)
        {
            Gizmos.DrawWireSphere(l.transform.position, l.range);
        }

        // 캐릭터별 판정
        var chars = FindObjectsByType<CharacterVisibility>(FindObjectsSortMode.None);
        foreach (var cv in chars)
        {
            if (cv == null) continue;
            Vector3 feet = cv.transform.position;
            foreach (float h in CheckHeights)
            {
                Vector3 p = feet + Vector3.up * h;
                var res = Evaluate(p, out Collider blocker);
                if (res == LitResult.OutOfRange) continue;

                Color col;
                switch (res)
                {
                    case LitResult.Lit: col = Color.green; break;
                    case LitResult.OutOfCone: col = Color.yellow; break;
                    case LitResult.Blocked: col = Color.red; break;
                    default: col = Color.gray; break;
                }
                Gizmos.color = col;
                Gizmos.DrawLine(l.transform.position, p);
                Gizmos.DrawWireSphere(p, 0.08f);

                if (res == LitResult.Blocked && blocker != null)
                {
                    Vector3 hp = blocker.ClosestPoint(l.transform.position);
                    Gizmos.DrawSphere(hp, 0.06f);
                    Handles.color = Color.red;
                    Handles.Label(hp + Vector3.up * 0.15f, "Blocked: " + blocker.name);
                }
                else if (res == LitResult.LightOff)
                {
                    Handles.Label(p + Vector3.up * 0.15f, "Light off / too dim");
                }
            }
        }
    }
#endif
}
