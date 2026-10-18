using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 3인칭 몸(다른 사람이 보는 모습)의 손에 총을 쥐여준다.
/// CharacterBody 루트에 붙인다. 오른손 소품 뼈(B-handProp.R) 아래 WeaponSocket의 자식들이 무기 모델.
///  - 자식 이름 = 무기 이름 (Pistol / Shotgun / Sniper). GunWeapon.WeaponName과 대소문자 무시하고 비교
///  - 플레이어: WeaponSwitcher가 무기를 바꾸면 같은 이름의 모델만 켬
///  - 로컬 플레이어(나): 1인칭 총이 따로 있으므로 3인칭 총은 그림자만 (Shadows Only)
///  - 마네킹/봇(WeaponSwitcher 없음): Default Weapon을 들고 있음 ("Random"이면 랜덤)
///  - 총의 Rendering Layer를 몸과 똑같이 맞춤 → 몸이 안 보이는 곳에서는 총도 안 보임
///    (총 모델은 기본값 Default라 맵 조명·스포트라이트를 받아서, 가까이 가면 총만 보이던 문제)
/// 위치/회전은 CharacterBody 프리팹을 열고 WeaponSocket 아래 각 무기 오브젝트를 직접 맞추면 됨.
///
/// 무기별 자세(애니메이터 레이어):
///  - Weapon Layers에 "무기 이름 ↔ 애니메이터 레이어 이름"을 넣으면, 그 무기를 들 때 해당 레이어 Weight가 1이 됨
///  - 예: Pistol ↔ PistolUpper (상체 마스크 레이어) → 권총을 들면 상체만 권총 자세, 다리는 기존 이동 애니메이션
/// </summary>
public class ThirdPersonWeapon : MonoBehaviour
{
    [Tooltip("비우면 자식 중 이름이 WeaponSocket인 오브젝트를 찾음")]
    [SerializeField] private Transform socket;
    [Tooltip("WeaponSwitcher가 없을 때(마네킹/봇) 들 무기 이름. Random = 랜덤, None = 빈손")]
    [SerializeField] private string defaultWeapon = "Random";
    [Tooltip("Random일 때 고를 무기 이름")]
    [SerializeField] private string[] randomPool = { "Shotgun", "Sniper", "Pistol" };
    [Tooltip("로컬 플레이어(나)는 3인칭 총을 그림자만 보이게")]
    [SerializeField] private bool localShadowOnly = true;
    [Tooltip("총의 Rendering Layer를 몸(SkinnedMeshRenderer)과 같게 → 몸과 똑같은 조명만 받음")]
    [SerializeField] private bool matchBodyLighting = true;

    [System.Serializable]
    public struct WeaponLayer
    {
        [Tooltip("무기 이름 (Pistol / Shotgun / Sniper)")]
        public string weaponName;
        [Tooltip("애니메이터 레이어 이름 (Animator 창 Layers 탭의 이름과 똑같이)")]
        public string layerName;
    }

    [Header("무기별 자세 (애니메이터 레이어)")]
    [SerializeField] private WeaponLayer[] weaponLayers = { new WeaponLayer { weaponName = "Pistol", layerName = "PistolUpper" } };
    [Tooltip("자세가 바뀌는 속도 (클수록 빠름)")]
    [SerializeField, Min(0.1f)] private float layerBlendSpeed = 8f;

    public string Current { get; private set; }

    private readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
    private WeaponSwitcher switcher;
    private Animator animator;
    private int[] layerIndex;
    private float[] layerTarget;

    private void Awake()
    {
        if (socket == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "WeaponSocket") { socket = t; break; }
        }
        if (socket == null)
        {
            Debug.LogWarning("[ThirdPersonWeapon] WeaponSocket을 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        foreach (Transform child in socket)
            models[child.name.ToLowerInvariant()] = child.gameObject;

        if (matchBodyLighting) MatchBodyRenderingLayer();

        // 무기별 레이어 찾기
        animator = GetComponent<Animator>();
        layerIndex = new int[weaponLayers.Length];
        layerTarget = new float[weaponLayers.Length];
        for (int i = 0; i < weaponLayers.Length; i++)
        {
            layerIndex[i] = animator != null ? animator.GetLayerIndex(weaponLayers[i].layerName) : -1;
            if (animator != null && layerIndex[i] < 0 && !string.IsNullOrEmpty(weaponLayers[i].layerName))
                Debug.LogWarning($"[ThirdPersonWeapon] 애니메이터에 '{weaponLayers[i].layerName}' 레이어가 없습니다.", this);
        }
    }

    /// <summary>몸 메시의 Rendering Layer를 총 모델 전체에 복사</summary>
    private void MatchBodyRenderingLayer()
    {
        SkinnedMeshRenderer body = null;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.transform.IsChildOf(socket)) continue;
            body = smr;
            break;
        }
        if (body == null) return;

        uint mask = body.renderingLayerMask;
        foreach (var r in socket.GetComponentsInChildren<Renderer>(true))
            r.renderingLayerMask = mask;
    }

    private void Update()
    {
        if (animator == null) return;
        for (int i = 0; i < layerIndex.Length; i++)
        {
            int li = layerIndex[i];
            if (li < 0) continue;
            float w = Mathf.MoveTowards(animator.GetLayerWeight(li), layerTarget[i], layerBlendSpeed * Time.deltaTime);
            animator.SetLayerWeight(li, w);
        }
    }

    private void Start()
    {
        if (socket == null) return;
        Transform root = transform.root;

        // 로컬 플레이어: 그림자만
        if (localShadowOnly && root.CompareTag("Player"))
        {
            foreach (var r in socket.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        switcher = root.GetComponentInChildren<WeaponSwitcher>(true);
        if (switcher != null)
        {
            switcher.OnSwitched += HandleSwitched;
            Show(switcher.Current != null ? switcher.Current.WeaponName : null);
        }
        else
        {
            ShowDefault();
        }
    }

    private void OnDestroy()
    {
        if (switcher != null) switcher.OnSwitched -= HandleSwitched;
    }

    private void HandleSwitched(int index, GunWeapon weapon) => Show(weapon != null ? weapon.WeaponName : null);

    private void ShowDefault()
    {
        if (string.IsNullOrEmpty(defaultWeapon) || defaultWeapon.Equals("None", System.StringComparison.OrdinalIgnoreCase))
        {
            Show(null);
            return;
        }
        if (defaultWeapon.Equals("Random", System.StringComparison.OrdinalIgnoreCase) && randomPool.Length > 0)
        {
            Show(randomPool[Random.Range(0, randomPool.Length)]);
            return;
        }
        Show(defaultWeapon);
    }

    /// <summary>이름이 같은 무기 모델만 켬 (null이면 전부 끔)</summary>
    public void Show(string weaponName)
    {
        string key = string.IsNullOrEmpty(weaponName) ? null : weaponName.ToLowerInvariant();
        foreach (var kv in models)
            kv.Value.SetActive(kv.Key == key);
        Current = key != null && models.ContainsKey(key) ? weaponName : null;

        // 무기별 자세 레이어: 이 무기의 레이어만 1, 나머지 0
        if (layerTarget != null)
            for (int i = 0; i < weaponLayers.Length; i++)
                layerTarget[i] = key != null && string.Equals(weaponLayers[i].weaponName, weaponName, System.StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
    }
}
