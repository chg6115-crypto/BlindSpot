using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 오토바이 3인칭 추적 카메라 (Main Camera에 붙이고 Target에 Motorcycle 루트)
/// - 마우스 드래그(기본: 우클릭)로 시점 회전, 휠로 확대/축소
/// - 오토바이가 기울어도 카메라는 수평 유지
/// - 달리는 중 마우스 입력이 없으면 자동으로 뒤쪽 시점 복귀
/// </summary>
[RequireComponent(typeof(Camera))]
public class MotorcycleFollowCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] Transform target;
    [SerializeField] MotorcycleTestDrive drive;

    [Header("Orbit (Mouse)")]
    [Tooltip("체크: 우클릭을 누른 채 드래그할 때만 회전 / 해제: 항상 마우스로 회전")]
    [SerializeField] bool requireRightMouse = true;
    [SerializeField] bool lockCursorWhileOrbiting = true;
    [SerializeField] float mouseSensitivity = 3f;
    [SerializeField] bool invertY = false;
    [SerializeField] float defaultPitch = 15f;
    [SerializeField] float minPitch = -10f;
    [SerializeField] float maxPitch = 70f;

    [Header("Zoom (Wheel)")]
    [SerializeField] float distance = 3.0f;
    [SerializeField] float minDistance = 1.2f;
    [SerializeField] float maxDistance = 8f;
    [SerializeField] float zoomStep = 0.5f;         // 휠 한 칸당 거리
    [SerializeField] float zoomSmooth = 10f;
    [SerializeField] bool invertZoom = false;
    [SerializeField] bool debugScroll = false;      // 체크하면 휠 값을 Console에 출력

    [Header("Auto Recenter")]
    [SerializeField] bool autoRecenter = true;
    [SerializeField] float recenterDelay = 1.5f;
    [SerializeField] float recenterSpeed = 2f;
    [SerializeField] float recenterMinSpeed = 1f;

    [Header("Framing")]
    [SerializeField] float lookHeight = 0.6f;
    [SerializeField] float lookAhead = 1.5f;

    [Header("Smoothing")]
    [SerializeField] float positionSmooth = 10f;
    [SerializeField] float followYawSmooth = 4f;

    [Header("Speed Effect")]
    [SerializeField] float maxSpeedForEffect = 18f;
    [SerializeField] float extraDistanceAtMax = 1.0f;
    [SerializeField] float baseFov = 60f;
    [SerializeField] float maxFov = 72f;

    [Header("Collision")]
    [SerializeField] bool avoidWalls = true;
    [SerializeField] LayerMask collisionMask = ~0;
    [SerializeField] float collisionRadius = 0.2f;

    Camera cam;
    float followYaw, orbitYaw, pitch;
    float targetDistance, currentDistance;
    float idleTimer;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (drive == null && target != null) drive = target.GetComponent<MotorcycleTestDrive>();
    }

    void Start()
    {
        pitch = defaultPitch;
        targetDistance = currentDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        if (target == null) return;
        followYaw = target.eulerAngles.y;
        transform.position = ComputeDesiredPosition(0f);
        transform.rotation = ComputeRotation(transform.position);
    }

    void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;

        HandleMouse(dt);

        followYaw = Mathf.LerpAngle(followYaw, target.eulerAngles.y, 1f - Mathf.Exp(-followYawSmooth * dt));
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, 1f - Mathf.Exp(-zoomSmooth * dt));

        float speed = drive != null ? drive.Speed : 0f;
        float speed01 = Mathf.Clamp01(Mathf.Abs(speed) / maxSpeedForEffect);

        if (autoRecenter && idleTimer > recenterDelay && Mathf.Abs(speed) > recenterMinSpeed)
        {
            float k = 1f - Mathf.Exp(-recenterSpeed * dt);
            orbitYaw = Mathf.LerpAngle(orbitYaw, 0f, k);
            pitch = Mathf.Lerp(pitch, defaultPitch, k);
        }

        Vector3 desired = ComputeDesiredPosition(extraDistanceAtMax * speed01);
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-positionSmooth * dt));
        transform.rotation = ComputeRotation(transform.position);

        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, Mathf.Lerp(baseFov, maxFov, speed01), 1f - Mathf.Exp(-3f * dt));
    }

    // ---------------- Input ----------------
    void HandleMouse(float dt)
    {
        ReadMouse(out Vector2 delta, out float scroll, out bool rightHeld);

        bool orbiting = !requireRightMouse || rightHeld;

        if (lockCursorWhileOrbiting && requireRightMouse)
        {
            Cursor.lockState = rightHeld ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !rightHeld;
        }

        if (orbiting && delta.sqrMagnitude > 0.0001f)
        {
            orbitYaw += delta.x * mouseSensitivity;
            pitch += (invertY ? delta.y : -delta.y) * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            idleTimer = 0f;
        }
        else idleTimer += dt;

        if (scroll != 0f)
        {
            if (invertZoom) scroll = -scroll;
            targetDistance = Mathf.Clamp(targetDistance - scroll * zoomStep, minDistance, maxDistance);
        }
    }

    void ReadMouse(out Vector2 delta, out float scroll, out bool rightHeld)
    {
        float rawScroll;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) { delta = Vector2.zero; scroll = 0f; rightHeld = false; return; }
        delta = m.delta.ReadValue() * 0.05f;
        rawScroll = m.scroll.ReadValue().y;
        rightHeld = m.rightButton.isPressed;
#else
        delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        rawScroll = Input.mouseScrollDelta.y;
        rightHeld = Input.GetMouseButton(1);
#endif
        // Input System 버전에 따라 한 칸 = 120 또는 1 근처(정규화) → 둘 다 '칸 수'로 변환
        if (Mathf.Abs(rawScroll) >= 20f) scroll = rawScroll / 120f;
        else scroll = rawScroll;

        if (debugScroll && rawScroll != 0f) Debug.Log($"[Camera] raw scroll = {rawScroll}, steps = {scroll}");
    }

    // ---------------- Camera math ----------------
    Vector3 Pivot => target.position + Vector3.up * lookHeight;

    Vector3 ComputeDesiredPosition(float extraDistance)
    {
        Quaternion rot = Quaternion.Euler(pitch, followYaw + orbitYaw, 0f);
        Vector3 pivot = Pivot;
        Vector3 desired = pivot + rot * new Vector3(0f, 0f, -(currentDistance + extraDistance));

        if (avoidWalls)
        {
            Vector3 dir = desired - pivot;
            if (Physics.SphereCast(pivot, collisionRadius, dir.normalized, out RaycastHit hit, dir.magnitude,
                                   collisionMask, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(target))
            {
                desired = pivot + dir.normalized * Mathf.Max(hit.distance - 0.05f, minDistance * 0.5f);
            }
        }
        return desired;
    }

    Quaternion ComputeRotation(Vector3 camPos)
    {
        float behind = Mathf.Clamp01(1f - Mathf.Abs(Mathf.DeltaAngle(0f, orbitYaw)) / 60f);
        Vector3 forward = Quaternion.Euler(0f, followYaw, 0f) * Vector3.forward;
        Vector3 lookPoint = Pivot + forward * (lookAhead * behind);
        return Quaternion.LookRotation(lookPoint - camPos, Vector3.up);
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}