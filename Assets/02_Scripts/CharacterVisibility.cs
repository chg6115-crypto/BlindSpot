using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 몸 표시 규칙 (플레이어 / 마네킹 / 디버그 봇 공통). Health.Awake에서 자동으로 붙음 (프리팹 수정 불필요).
///
///  - 다른 사람 화면에서는 평소에 몸을 안 그림 (몸 메시 + 3인칭 총 + 그림자)
///    → 빛을 등져도 검은 실루엣이 드러나지 않음
///  - 데칼이 켜져 있는 동안 보임 (정기 점멸, 트랩, 마네킹 피격 깜빡임, 디버그 "데칼 항상 켜기")
///  - 켜진 노출 조명(RevealLight: 트랩 라이트, 형광등 등) 범위 안에 있으면 보임 (라이트와 사이에 벽이 없을 때)
///  - 내 캐릭터는 내 화면에서 항상 보임 (LocalBodyView 설정 그대로)
///  - 관전 중인 대상은 보임 (그 사람 1인칭 시점)
///  - 충돌·피격·애니메이션은 그대로 (렌더링만 끔)
/// 소나 실루엣(SonarReveal)은 BodyRenderers를 직접 스냅샷하므로 숨겨져 있어도 동작.
/// </summary>
[DisallowMultipleComponent]
public class CharacterVisibility : MonoBehaviour
{
    private DecalBlink decal;
    private PlayerNetworkSetup net;
    private readonly List<Renderer> bodyRenderers = new List<Renderer>();
    private int lastState = -1;   // -1 미정, 0 숨김, 1 보임

    /// <summary>몸 렌더러 목록 (몸 메시 + 3인칭 총). 소나 스냅샷용</summary>
    public IReadOnlyList<Renderer> BodyRenderers => bodyRenderers;

    /// <summary>지금 이 화면에서 몸이 보이는 상태인지</summary>
    public bool IsShown => lastState == 1;

    private void Awake()
    {
        decal = GetComponentInChildren<DecalBlink>(true);
        net = GetComponent<PlayerNetworkSetup>();
        CollectRenderers();
    }

    private void CollectRenderers()
    {
        bodyRenderers.Clear();

        // 몸 루트: 데칼이 붙은 CharacterBody (없으면 Animator 위치)
        Transform root = decal != null ? decal.transform : null;
        if (root == null)
        {
            var anim = GetComponentInChildren<Animator>(true);
            root = anim != null ? anim.transform : transform;
        }

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            // 메시만 (빔·파티클 등 효과는 건드리지 않음)
            if (r is SkinnedMeshRenderer || r is MeshRenderer) bodyRenderers.Add(r);
        }

        // 몸을 안 그려도 뼈는 계속 움직이게 (소나 스냅샷 자세가 멈추지 않도록)
        foreach (var anim in root.GetComponentsInChildren<Animator>(true))
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private bool IsLocalCharacter()
    {
        if (net != null) return net.IsMine;
        return CompareTag("Player");
    }

    private void LateUpdate()
    {
        bool show = IsLocalCharacter()
                    || (net != null && net.IsSpectated)
                    || (decal != null && decal.IsVisibleNow)
                    || InTrapLight();

        int state = show ? 1 : 0;
        if (state == lastState) return;
        lastState = state;

        foreach (var r in bodyRenderers)
            if (r != null) r.forceRenderingOff = !show;
    }

    /// <summary>켜진 노출 조명이 몸(가슴 또는 머리 높이)을 비추는지</summary>
    private bool InTrapLight()
    {
        Vector3 p = transform.position;
        return RevealLight.IsPointLit(p + Vector3.up * 1.0f) || RevealLight.IsPointLit(p + Vector3.up * 1.6f);
    }

    private void OnDisable()
    {
        // 컴포넌트가 꺼지면 원래대로 보이게
        foreach (var r in bodyRenderers)
            if (r != null) r.forceRenderingOff = false;
        lastState = -1;
    }
}
