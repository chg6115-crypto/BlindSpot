using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 로컬 플레이어(1인칭) 몸 표시 방식.
/// Player_FPS 루트에 붙인다.
///
/// HideHead   : 머리 뼈만 크기 0 → 팔/몸은 보이고 카메라에 머리 안쪽이 안 걸림 (총 IK용, 기본)
/// ShadowOnly : 몸 전체를 그림자만 남김 (팔 없는 1인칭용)
/// Visible    : 그대로 보임 (3인칭/인트로 연출용)
/// </summary>
public class LocalBodyView : MonoBehaviour
{
    public enum ViewMode { HideHead, ShadowOnly, Visible }

    [Tooltip("몸 Animator (비우면 자식 중 Humanoid Animator 자동 검색 — 총의 Animator는 무시)")]
    public Animator bodyAnimator;

    [Tooltip("1인칭일 때 몸 표시 방식")]
    public ViewMode firstPersonMode = ViewMode.HideHead;

    [Tooltip("시작 시 1인칭 모드 적용 (Player 태그일 때만)")]
    public bool applyOnStart = true;

    [Tooltip("HideHead일 때 머리 뼈 크기 (0이면 완전히 숨김)")]
    [Range(0f, 1f)] public float hiddenHeadScale = 0f;

    private Renderer[] bodyRenderers;
    private ShadowCastingMode[] originalModes;
    private Transform headBone;
    private Vector3 originalHeadScale = Vector3.one;
    private ViewMode currentMode = ViewMode.Visible;

    private void Awake()
    {
        if (bodyAnimator == null) bodyAnimator = FindHumanoid();

        if (bodyAnimator == null)
        {
            Debug.LogWarning("[LocalBodyView] Humanoid Animator를 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        bodyRenderers = bodyAnimator.GetComponentsInChildren<Renderer>(true);
        originalModes = new ShadowCastingMode[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++)
            originalModes[i] = bodyRenderers[i].shadowCastingMode;

        headBone = bodyAnimator.GetBoneTransform(HumanBodyBones.Head);
        if (headBone != null) originalHeadScale = headBone.localScale;
    }

    private void Start()
    {
        if (applyOnStart && CompareTag("Player"))
            SetFirstPerson(true);
    }

    // Animator가 매 프레임 뼈를 덮어쓸 수 있으므로 LateUpdate에서 머리 크기 유지
    private void LateUpdate()
    {
        if (currentMode == ViewMode.HideHead && headBone != null)
            headBone.localScale = originalHeadScale * hiddenHeadScale;
    }

    /// <summary>true = 1인칭 표시 방식 적용, false = 원래대로 보임</summary>
    public void SetFirstPerson(bool firstPerson)
    {
        SetMode(firstPerson ? firstPersonMode : ViewMode.Visible);
    }

    public void SetMode(ViewMode mode)
    {
        if (bodyRenderers == null) return;
        currentMode = mode;

        // 렌더러: ShadowOnly일 때만 그림자 전용, 나머지는 원래대로
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            var r = bodyRenderers[i];
            if (r == null) continue;
            r.shadowCastingMode = mode == ViewMode.ShadowOnly ? ShadowCastingMode.ShadowsOnly : originalModes[i];
        }

        // 머리 크기
        if (headBone != null)
            headBone.localScale = mode == ViewMode.HideHead ? originalHeadScale * hiddenHeadScale : originalHeadScale;
    }

    private Animator FindHumanoid()
    {
        foreach (var a in GetComponentsInChildren<Animator>(true))
            if (a.isHuman) return a;
        return null;
    }
}