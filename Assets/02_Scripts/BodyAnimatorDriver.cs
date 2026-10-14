using UnityEngine;
using StarterAssets;

// 부모의 CharacterController 속도를 읽어 Invector 애니메이터 파라미터를 구동한다.
// CharacterBody(Animator가 있는 오브젝트)에 붙인다.
// Mannequin처럼 CharacterController가 없으면 자동으로 꺼진다.
[RequireComponent(typeof(Animator))]
public class BodyAnimatorDriver : MonoBehaviour
{
    [Header("속도 기준 (FirstPersonController 값과 맞추기)")]
    public float walkSpeed = 4f;     // FirstPersonController.MoveSpeed
    public float sprintSpeed = 6f;   // FirstPersonController.SprintSpeed

    [Header("부드럽게")]
    public float dampTime = 0.1f;

    Animator anim;
    CharacterController cc;
    FirstPersonController fpc;
    StarterAssetsInputs input;

    static readonly int H = Animator.StringToHash("InputHorizontal");
    static readonly int V = Animator.StringToHash("InputVertical");
    static readonly int Mag = Animator.StringToHash("InputMagnitude");
    static readonly int Strafing = Animator.StringToHash("IsStrafing");
    static readonly int Sprinting = Animator.StringToHash("IsSprinting");
    static readonly int Grounded = Animator.StringToHash("IsGrounded");

    void Awake()
    {
        anim = GetComponent<Animator>();
        cc = GetComponentInParent<CharacterController>();
        fpc = GetComponentInParent<FirstPersonController>();
        input = GetComponentInParent<StarterAssetsInputs>();

        if (cc == null)
        {
            enabled = false;   // Mannequin 등
            return;
        }

        anim.applyRootMotion = false;
        if (fpc != null)
        {
            walkSpeed = fpc.MoveSpeed;
            sprintSpeed = fpc.SprintSpeed;
        }
    }

    void Update()
    {
        Vector3 vel = cc.velocity;
        vel.y = 0f;

        // Freeze 중(FirstPersonController 꺼짐)이거나 CharacterController가 꺼져 있으면 정지
        bool frozen = (fpc != null && !fpc.enabled) || !cc.enabled;
        if (frozen) vel = Vector3.zero;

        // 루트 기준 로컬 방향
        Vector3 local = cc.transform.InverseTransformDirection(vel);
        float speed = vel.magnitude;

        bool sprint = !frozen && input != null && input.sprint && speed > walkSpeed * 0.5f;

        // Invector 기준: 걷기 0.5 / 달리기 1 / 스프린트 1.5
        float scale = 1f / Mathf.Max(walkSpeed, 0.01f);
        float h = Mathf.Clamp(local.x * scale, -1f, 1f);
        float v = Mathf.Clamp(local.z * scale, -1f, 1f);
        float mag = Mathf.Clamp01(speed * scale);
        if (sprint) mag = 1.5f;

        anim.SetFloat(H, h, dampTime, Time.deltaTime);
        anim.SetFloat(V, v, dampTime, Time.deltaTime);
        anim.SetFloat(Mag, mag, dampTime, Time.deltaTime);
        anim.SetBool(Strafing, true);   // 1인칭은 항상 스트레이프
        anim.SetBool(Sprinting, sprint);
        anim.SetBool(Grounded, fpc == null || !fpc.enabled || fpc.Grounded);
    }
}