using UnityEngine;

/// <summary>
/// 1인칭 총을 텍스처 없이 검은 실루엣으로만 보이게 한다.
/// WeaponHolder에 붙인다. 자식(모든 무기)의 렌더러 머티리얼을 실루엣 머티리얼로 바꿈.
///  - 로컬 플레이어(루트가 Player 태그)에서만 적용
///  - SetSilhouette(false)로 원래 머티리얼 복구 (디버그/무기 선택 화면 등)
/// 실루엣 머티리얼: 05_Materials/WeaponSilhouette_Mat (URP Lit, 검정, 반사/하이라이트 끔)
/// </summary>
public class ViewModelSilhouette : MonoBehaviour
{
    [SerializeField] private bool silhouette = true;
    [Tooltip("비우면 실행 중에 검은 머티리얼을 만들어 사용")]
    [SerializeField] private Material silhouetteMaterial;
    [SerializeField] private bool localPlayerOnly = true;

    public bool IsSilhouette => silhouette;

    private Renderer[] renderers;
    private Material[][] originals;
    private bool applied;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        originals = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
            originals[i] = renderers[i].sharedMaterials;
    }

    private void Start()
    {
        if (localPlayerOnly && !transform.root.CompareTag("Player")) return;
        Apply(silhouette);
    }

    public void SetSilhouette(bool on)
    {
        silhouette = on;
        Apply(on);
    }

    private void Apply(bool on)
    {
        if (renderers == null) return;

        if (on && silhouetteMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            silhouetteMaterial = new Material(shader) { name = "WeaponSilhouette (Runtime)" };
            silhouetteMaterial.SetColor("_BaseColor", Color.black);
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;

            if (on)
            {
                var mats = new Material[originals[i].Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = silhouetteMaterial;
                r.sharedMaterials = mats;
            }
            else
            {
                r.sharedMaterials = originals[i];
            }
        }
        applied = on;
    }

#if UNITY_EDITOR
    // Play 중 Inspector에서 체크를 바꾸면 바로 반영
    private void OnValidate()
    {
        if (Application.isPlaying && renderers != null && applied != silhouette) Apply(silhouette);
    }
#endif
}
