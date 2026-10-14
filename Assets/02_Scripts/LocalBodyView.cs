using UnityEngine;
using UnityEngine.Rendering;

// 로컬 플레이어(1인칭)일 때 자기 몸은 안 보이고 그림자만 남게 한다.
// Player_FPS 루트에 붙인다.
public class LocalBodyView : MonoBehaviour
{
    [Tooltip("몸 모델 루트 (비우면 자식 Animator를 자동으로 찾음)")]
    public Transform bodyRoot;

    [Tooltip("시작 시 1인칭 모드(몸 숨김)로 할지")]
    public bool hideOnStart = true;

    Renderer[] bodyRenderers;
    ShadowCastingMode[] originalModes;

    void Awake()
    {
        if (bodyRoot == null)
        {
            var anim = GetComponentInChildren<Animator>(true);
            if (anim != null) bodyRoot = anim.transform;
        }

        if (bodyRoot == null)
        {
            Debug.LogWarning("[LocalBodyView] bodyRoot를 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        bodyRenderers = bodyRoot.GetComponentsInChildren<Renderer>(true);
        originalModes = new ShadowCastingMode[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++)
            originalModes[i] = bodyRenderers[i].shadowCastingMode;
    }

    void Start()
    {
        // 로컬 플레이어(Player 태그)일 때만 숨김
        if (hideOnStart && CompareTag("Player"))
            SetFirstPerson(true);
    }

    /// <summary>true = 몸 숨김(그림자만), false = 몸 보임</summary>
    public void SetFirstPerson(bool firstPerson)
    {
        if (bodyRenderers == null) return;

        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            var r = bodyRenderers[i];
            if (r == null) continue;
            r.shadowCastingMode = firstPerson ? ShadowCastingMode.ShadowsOnly : originalModes[i];
        }
    }
}