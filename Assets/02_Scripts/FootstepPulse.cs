using UnityEngine;

/// <summary>
/// 이동 소리 범위를 실시간으로 표시. Player 루트에 붙임.
///  - 움직이는 동안 플레이어 주변 반경이 계속 유지되고 따라다님
///  - 걷기 ↔ 달리기 전환 시 반경이 부드럽게 변함
///  - 멈추거나 공중이면 서서히 꺼짐
/// 컨트롤러에 의존하지 않으므로 나중에 FPS 컨트롤러로 바꿔도 그대로 사용 가능.
/// </summary>
public class FootstepPulse : MonoBehaviour
{
    [Header("소리 반경 (m)")]
    [SerializeField] private float walkRadius = 10f;
    [SerializeField] private float runRadius = 15f;

    [Header("전환 속도 (초)")]
    [Tooltip("움직이기 시작했을 때 켜지는 시간")]
    [SerializeField, Min(0.01f)] private float fadeInTime = 0.3f;
    [Tooltip("멈췄을 때 꺼지는 시간")]
    [SerializeField, Min(0.01f)] private float fadeOutTime = 1f;
    [Tooltip("걷기↔달리기 반경이 바뀌는 시간")]
    [SerializeField, Min(0.01f)] private float radiusChangeTime = 0.5f;

    [Header("속도 기준 (m/s)")]
    [Tooltip("이 속도 이상이면 달리기 (Invector 기본: 일반 4, 스프린트 6)")]
    [SerializeField] private float runSpeedThreshold = 5f;
    [Tooltip("이 속도 미만이면 정지로 간주")]
    [SerializeField] private float minMoveSpeed = 0.5f;

    [Header("바닥 체크")]
    [Tooltip("Ground, Obstacle")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckDistance = 0.3f;

    private Vector3 lastPos;
    private float smoothSpeed;
    private float intensity;
    private float radius;

    private void OnEnable()
    {
        lastPos = transform.position;
        smoothSpeed = 0f;
        intensity = 0f;
        radius = walkRadius;
    }

    private void OnDisable()
    {
        SoundPulseManager.SetField(gameObject, transform.position, 0f, 0f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;
        delta.y = 0f;

        smoothSpeed = Mathf.Lerp(smoothSpeed, delta.magnitude / dt, 10f * dt);   // 프레임 튐 방지

        bool active = smoothSpeed >= minMoveSpeed && IsGrounded();
        bool running = smoothSpeed >= runSpeedThreshold;

        // 밝기: 켜질 땐 빠르게, 꺼질 땐 천천히
        float target = active ? 1f : 0f;
        float rate = active ? 1f / fadeInTime : 1f / fadeOutTime;
        intensity = Mathf.MoveTowards(intensity, target, rate * dt);

        // 반경: 걷기/달리기 사이를 부드럽게 (꺼지는 중에는 마지막 반경 유지)
        if (active)
        {
            float targetRadius = running ? runRadius : walkRadius;
            radius = Mathf.MoveTowards(radius, targetRadius, Mathf.Abs(runRadius - walkRadius) / radiusChangeTime * dt);
        }

        SoundPulseManager.SetField(gameObject, transform.position, radius, intensity);
    }

    private bool IsGrounded()
    {
        return Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down,
                               groundCheckDistance + 0.1f, groundMask, QueryTriggerInteraction.Ignore);
    }
}