using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 정전 때 꺼지는 조명 기구. 형광등 등 기구의 루트(프리팹 루트)에 붙인다.
///  - 자식의 Light 전부 + 발광(Emission) 머티리얼을 PowerOutage 동안 끔
///  - 켜져 있던 Light만 다시 켬 (원래 꺼둔 Light는 그대로)
///  - 발광은 프로퍼티 블록으로 끄므로 원본 머티리얼은 그대로
/// </summary>
[DisallowMultipleComponent]
public class PowerGridFixture : MonoBehaviour
{
    [Tooltip("비우면 자식 Light 전부")]
    [SerializeField] private Light[] lights;
    [Tooltip("발광 머티리얼도 끔")]
    [SerializeField] private bool dimEmission = true;

    private struct Slot { public Renderer r; public int index; public Color baseColor; }

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private readonly List<Slot> slots = new List<Slot>();
    private readonly List<Light> wasOn = new List<Light>();
    private MaterialPropertyBlock mpb;
    private bool off;

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (lights == null || lights.Length == 0) lights = GetComponentsInChildren<Light>(true);

        if (dimEmission)
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty(EmissionId) || !m.IsKeywordEnabled("_EMISSION")) continue;
                    slots.Add(new Slot { r = r, index = i, baseColor = m.GetColor(EmissionId) });
                }
            }
    }

    private void OnEnable()
    {
        PowerOutage.Changed += Apply;
        if (PowerOutage.IsOut) Apply(true);
    }

    private void OnDisable()
    {
        PowerOutage.Changed -= Apply;
        Apply(false);
    }

    private void Apply(bool outage)
    {
        if (outage == off) return;
        off = outage;

        if (outage)
        {
            wasOn.Clear();
            foreach (var l in lights)
                if (l != null && l.enabled) { wasOn.Add(l); l.enabled = false; }
        }
        else
        {
            foreach (var l in wasOn) if (l != null) l.enabled = true;
            wasOn.Clear();
        }

        foreach (var s in slots)
        {
            if (s.r == null) continue;
            s.r.GetPropertyBlock(mpb, s.index);
            mpb.SetColor(EmissionId, outage ? Color.black : s.baseColor);
            s.r.SetPropertyBlock(mpb, s.index);
        }
    }
}
