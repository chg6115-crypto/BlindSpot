using System.Collections.Generic;
using UnityEngine;
using StarterAssets;

// 부모의 CharacterController 속도를 읽어 몸 애니메이터 파라미터를 구동한다.
// CharacterBody(Animator가 있는 오브젝트)에 붙인다.
// Mannequin처럼 CharacterController가 없으면 자동으로 꺼진다.
//
// 지원 파라미터 (컨트롤러에 있는 것만 설정 → 없는 파라미터 경고 없음)
//  - Mixamo 컨트롤러 (BlindSpot > Animation 메뉴로 생성): MoveX, MoveZ, IsGrounded
//      MoveX/MoveZ = 몸 기준 속도 / 이동속도  → 걷기 ≈0.35, 달리기 1, 스프린트 1.5
//  - 예전 Invector 컨트롤러: InputHorizontal, InputVertical, InputMagnitude, IsStrafing, IsSprinting, IsGrounded
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
    readonly HashSet<int> has = new HashSet<int>();

    static readonly int MoveX = Animator.StringToHash("MoveX");
    static readonly int MoveZ = Animator.StringToHash("MoveZ");
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
        CacheParameters();
    }

    /// <summary>컨트롤러를 바꿨을 때 다시 호출</summary>
    public void CacheParameters()
    {
        has.Clear();
        if (anim.runtimeAnimatorController == null) return;
        foreach (var p in anim.parameters) has.Add(p.nameHash);
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
        float scale = 1f / Mathf.Max(walkSpeed, 0.01f);

        bool sprint = !frozen && input != null && input.sprint && speed > walkSpeed * 0.5f;
        bool grounded = fpc == null || !fpc.enabled || fpc.Grounded;
        float dt = Time.deltaTime;

        // Mixamo 컨트롤러
        if (has.Contains(MoveX)) anim.SetFloat(MoveX, Mathf.Clamp(local.x * scale, -1.6f, 1.6f), dampTime, dt);
        if (has.Contains(MoveZ)) anim.SetFloat(MoveZ, Mathf.Clamp(local.z * scale, -1.6f, 1.6f), dampTime, dt);

        // 예전 Invector 컨트롤러 (걷기 0.5 / 달리기 1 / 스프린트 1.5)
        if (has.Contains(H)) anim.SetFloat(H, Mathf.Clamp(local.x * scale, -1f, 1f), dampTime, dt);
        if (has.Contains(V)) anim.SetFloat(V, Mathf.Clamp(local.z * scale, -1f, 1f), dampTime, dt);
        if (has.Contains(Mag)) anim.SetFloat(Mag, sprint ? 1.5f : Mathf.Clamp01(speed * scale), dampTime, dt);
        if (has.Contains(Strafing)) anim.SetBool(Strafing, true);   // 1인칭은 항상 스트레이프
        if (has.Contains(Sprinting)) anim.SetBool(Sprinting, sprint);

        if (has.Contains(Grounded)) anim.SetBool(Grounded, grounded);
    }
}
