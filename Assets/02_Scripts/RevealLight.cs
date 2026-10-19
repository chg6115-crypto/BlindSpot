using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

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

    private static readonly List<RevealLight> all = new List<RevealLight>();
    private static readonly RaycastHit[] hits = new RaycastHit[8];
    private const int CharacterLayer = 6;

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
            if (r.Illuminates(point)) return true;
        }
        return false;
    }

    private bool Illuminates(Vector3 point)
    {
        var l = lightComp;
        if (l == null || !l.enabled || l.intensity < minIntensity || l.intensity <= 0.001f) return false;

        if (l.type == LightType.Directional)
        {
            Vector3 toLight = -l.transform.forward;
            return !IsBlocked(point, toLight, directionalReach);
        }

        Vector3 lp = l.transform.position;
        Vector3 d = point - lp;
        float dist = d.magnitude;
        if (dist > l.range) return false;
        if (dist < 0.05f) return true;
        Vector3 dir = d / dist;

        switch (l.type)
        {
            case LightType.Point:
                break;
            case LightType.Spot:
                if (Vector3.Angle(l.transform.forward, dir) > l.spotAngle * 0.5f) return false;
                break;
            default:
                return false;   // Area 미지원
        }

        return !IsBlocked(lp, dir, dist);
    }

    private bool IsBlocked(Vector3 from, Vector3 dir, float dist)
    {
        int n = Physics.RaycastNonAlloc(from, dir, hits, dist, ~(1 << CharacterLayer), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = hits[i].collider;
            if (col == null) continue;
            if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot)) continue;   // 등 기구 / 트랩 자신
            if (col.GetComponentInParent<Health>() != null) continue;                 // 캐릭터/마네킹은 벽이 아님
            if (col.GetComponentInParent<LightPassThrough>() != null) continue;       // 창문 유리 등 빛이 통과하는 것
            return true;
        }
        return false;
    }
}
