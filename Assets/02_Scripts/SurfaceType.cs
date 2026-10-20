using System.Collections.Generic;
using UnityEngine;

/// <summary>총알이 맞은 표면 종류</summary>
public enum SurfaceKind { Stone = 0, Metal = 1, Wood = 2, Flesh = 3 }

/// <summary>
/// 총알 착탄 표면 지정. 오브젝트(프리팹 루트)에 붙이고 드롭다운으로 고른다. 자식 콜라이더 전부에 적용.
///  - 안 붙어 있으면 자동 추측: 캐릭터(Health) = Flesh / 이름·머티리얼에 wood·crate → Wood,
///    metal·steel·container·barrel·car 등 → Metal / 그 외 Stone
/// </summary>
[DisallowMultipleComponent]
public class SurfaceType : MonoBehaviour
{
    [SerializeField] private SurfaceKind kind = SurfaceKind.Stone;
    public SurfaceKind Kind => kind;

    private static readonly Dictionary<Collider, SurfaceKind> cache = new Dictionary<Collider, SurfaceKind>();
    private static readonly string[] WoodWords = { "wood", "crate", "plank", "pallet" };
    private static readonly string[] MetalWords = { "metal", "steel", "iron", "container", "cargo", "barrel", "gastank", "gas_tank", "car", "electric", "pipe", "rust" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => cache.Clear();

    /// <summary>콜라이더의 표면 종류</summary>
    public static SurfaceKind Resolve(Collider col)
    {
        if (col == null) return SurfaceKind.Stone;
        if (cache.TryGetValue(col, out var k)) return k;

        if (col.GetComponentInParent<Health>() != null) k = SurfaceKind.Flesh;
        else
        {
            var st = col.GetComponentInParent<SurfaceType>();
            k = st != null ? st.kind : Guess(col);
        }
        cache[col] = k;
        return k;
    }

    private static SurfaceKind Guess(Collider col)
    {
        // 이름 (자신 → 부모들)
        for (Transform t = col.transform; t != null; t = t.parent)
        {
            var g = Match(t.name);
            if (g.HasValue) return g.Value;
        }
        // 머티리얼 이름
        var r = col.GetComponent<Renderer>();
        if (r == null) r = col.GetComponentInParent<Renderer>();
        if (r != null)
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                var g = Match(m.name);
                if (g.HasValue) return g.Value;
            }
        return SurfaceKind.Stone;
    }

    private static SurfaceKind? Match(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string n = name.ToLowerInvariant();
        foreach (var w in WoodWords) if (n.Contains(w)) return SurfaceKind.Wood;
        foreach (var w in MetalWords) if (n.Contains(w)) return SurfaceKind.Metal;
        return null;
    }
}
