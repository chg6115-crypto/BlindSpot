using UnityEngine;

/// <summary>
/// 총을 잡는 손 IK + 팔꿈치 방향 고정 + 시선 방향으로 상체/머리 숙이기.
/// CharacterBody(Humanoid Animator가 있는 오브젝트)에 붙인다.
/// Animator Controller의 Base Layer에서 "IK Pass"를 반드시 켤 것.
/// </summary>
[RequireComponent(typeof(Animator))]
public class HandIK : MonoBehaviour
{
    [Header("손 목표 (비우면 부모 아래에서 이름으로 자동 검색)")]
    public Transform rightHandTarget;
    public Transform leftHandTarget;
    [SerializeField] private string rightTargetName = "HandIKRight";
    [SerializeField] private string leftTargetName = "HandIKLeft";

    [Header("손 IK")]
    [Range(0f, 1f)] public float handPositionWeight = 1f;
    [Range(0f, 1f)] public float handRotationWeight = 0.5f;
    [Tooltip("손 회전이 어색할 때 보정 (도)")]
    public Vector3 rightHandRotationOffset;
    public Vector3 leftHandRotationOffset;
    [Tooltip("무기 전환 시 IK 켜고 끄는 속도")]
    [SerializeField] private float blendSpeed = 10f;

    [Header("팔꿈치 힌트 (걸을 때 팔꿈치가 튀어오르는 것 방지)")]
    [Range(0f, 1f)] public float elbowHintWeight = 1f;
    [Tooltip("어깨 기준 팔꿈치를 놓을 방향: x=바깥쪽, y=아래(음수), z=앞뒤 (m, 몸 기준)")]
    public Vector3 rightElbowOffset = new Vector3(0.25f, -0.35f, 0.05f);
    public Vector3 leftElbowOffset = new Vector3(-0.25f, -0.35f, 0.05f);

    [Header("시선 (상체/머리 숙이기)")]
    [Tooltip("비우면 부모 아래의 PlayerCameraRoot 자동 검색")]
    public Transform lookTarget;
    [SerializeField] private string lookTargetName = "PlayerCameraRoot";
    [Range(0f, 1f)] public float lookWeight = 1f;
    [Range(0f, 1f)] public float bodyWeight = 0.2f;
    [Range(0f, 1f)] public float headWeight = 1f;
    [Range(0f, 1f)] public float clampWeight = 0.5f;
    [SerializeField] private float lookDistance = 10f;

    [Header("디버그")]
    [SerializeField] private bool drawGizmos = true;

    private Animator anim;
    private Transform rightShoulder, leftShoulder;
    private float rightBlend, leftBlend;
    private Vector3 lastRightHint, lastLeftHint;

    private void Awake()
    {
        anim = GetComponent<Animator>();

        Transform root = transform.root;
        if (rightHandTarget == null) rightHandTarget = FindDeep(root, rightTargetName);
        if (leftHandTarget == null) leftHandTarget = FindDeep(root, leftTargetName);
        if (lookTarget == null) lookTarget = FindDeep(root, lookTargetName);

        if (anim.isHuman)
        {
            rightShoulder = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftShoulder = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        }
    }

    /// <summary>무기 교체 시 호출해서 손 목표를 바꿈 (null이면 IK 해제)</summary>
    public void SetTargets(Transform right, Transform left)
    {
        rightHandTarget = right;
        leftHandTarget = left;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (anim == null) return;

        float dt = Time.deltaTime * blendSpeed;
        rightBlend = Mathf.MoveTowards(rightBlend, IsUsable(rightHandTarget) ? 1f : 0f, dt);
        leftBlend = Mathf.MoveTowards(leftBlend, IsUsable(leftHandTarget) ? 1f : 0f, dt);

        // 시선
        if (lookTarget != null && lookWeight > 0f)
        {
            anim.SetLookAtWeight(lookWeight, bodyWeight, headWeight, 0f, clampWeight);
            anim.SetLookAtPosition(lookTarget.position + lookTarget.forward * lookDistance);
        }
        else
        {
            anim.SetLookAtWeight(0f);
        }

        // 손
        ApplyHand(AvatarIKGoal.RightHand, rightHandTarget, rightBlend, rightHandRotationOffset);
        ApplyHand(AvatarIKGoal.LeftHand, leftHandTarget, leftBlend, leftHandRotationOffset);

        // 팔꿈치
        lastRightHint = ApplyElbow(AvatarIKHint.RightElbow, rightShoulder, rightElbowOffset, rightBlend);
        lastLeftHint = ApplyElbow(AvatarIKHint.LeftElbow, leftShoulder, leftElbowOffset, leftBlend);
    }

    private void ApplyHand(AvatarIKGoal goal, Transform target, float blend, Vector3 rotOffset)
    {
        anim.SetIKPositionWeight(goal, handPositionWeight * blend);
        anim.SetIKRotationWeight(goal, handRotationWeight * blend);

        if (blend <= 0f || target == null) return;

        anim.SetIKPosition(goal, target.position);
        anim.SetIKRotation(goal, target.rotation * Quaternion.Euler(rotOffset));
    }

    private Vector3 ApplyElbow(AvatarIKHint hint, Transform shoulder, Vector3 offset, float blend)
    {
        float w = elbowHintWeight * blend;
        anim.SetIKHintPositionWeight(hint, w);
        if (w <= 0f || shoulder == null) return Vector3.zero;

        // 캐릭터(루트) 방향 기준으로 어깨에서 offset만큼 떨어진 곳을 팔꿈치 목표로
        Transform basis = transform.root;
        Vector3 pos = shoulder.position + basis.rotation * offset;
        anim.SetIKHintPosition(hint, pos);
        return pos;
    }

    private static bool IsUsable(Transform t) => t != null && t.gameObject.activeInHierarchy;

    private static Transform FindDeep(Transform parent, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !Application.isPlaying) return;
        Gizmos.color = Color.cyan;
        if (lastRightHint != Vector3.zero) Gizmos.DrawWireSphere(lastRightHint, 0.04f);
        if (lastLeftHint != Vector3.zero) Gizmos.DrawWireSphere(lastLeftHint, 0.04f);
    }
}