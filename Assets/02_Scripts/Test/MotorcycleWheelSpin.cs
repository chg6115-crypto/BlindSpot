using UnityEngine;

/// <summary>
/// 오토바이 루트(Motorcycle)에 붙이는 바퀴 회전 스크립트.
/// 루트가 이동한 거리만큼 바퀴를 굴린다. (Transform / Rigidbody / CharacterController 이동 모두 OK)
/// 회전각 = 이동거리 / 반지름 → 미끄러지지 않고 실제로 굴러가는 것처럼 보임
/// </summary>
public class MotorcycleWheelSpin : MonoBehaviour
{
    [Header("Wheels")]
    [SerializeField] Transform frontWheel;
    [SerializeField] Transform rearWheel;

    [Header("Radius (0 = 시작할 때 자동 계산)")]
    [SerializeField] float frontRadius = 0f;   // 현재 모델 기준 약 0.25
    [SerializeField] float rearRadius = 0f;    // 현재 모델 기준 약 0.20

    [Header("Axis")]
    [SerializeField] Vector3 localAxis = Vector3.right; // 바퀴 축 (Blender X → Unity X)
    [SerializeField] bool invert = false;               // 바퀴가 반대로 돌면 체크

    [Header("Safety")]
    [Tooltip("한 프레임에 이 거리 이상 움직이면 순간이동으로 보고 회전하지 않음")]
    [SerializeField] float teleportThreshold = 5f;

    /// <summary>현재 전진 속도 (m/s, 후진이면 음수)</summary>
    public float CurrentSpeed { get; private set; }

    Vector3 lastPos;

    void Start()
    {
        if (frontRadius <= 0f) frontRadius = AutoRadius(frontWheel);
        if (rearRadius <= 0f) rearRadius = AutoRadius(rearWheel);
        lastPos = transform.position;
    }

    void LateUpdate()
    {
        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;

        if (delta.magnitude > teleportThreshold) return;

        // 앞뒤 방향 성분만 사용 → 후진하면 바퀴도 반대로 돈다
        float dist = Vector3.Dot(delta, transform.forward);
        CurrentSpeed = Time.deltaTime > 0f ? dist / Time.deltaTime : 0f;
        if (Mathf.Approximately(dist, 0f)) return;

        if (invert) dist = -dist;
        Spin(frontWheel, frontRadius, dist);
        Spin(rearWheel, rearRadius, dist);
    }

    void Spin(Transform wheel, float radius, float dist)
    {
        if (wheel == null || radius <= 0f) return;
        float deg = dist / radius * Mathf.Rad2Deg;
        wheel.Rotate(localAxis, deg, Space.Self);
    }

    // 세워진 상태 기준: 바퀴 렌더러 높이의 절반 = 반지름
    static float AutoRadius(Transform wheel)
    {
        if (wheel == null) return 0f;
        var r = wheel.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.extents.y : 0f;
    }
}