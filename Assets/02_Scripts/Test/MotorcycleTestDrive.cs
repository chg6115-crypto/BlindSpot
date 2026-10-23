using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 테스트용 오토바이 이동
/// W/S 전진·후진, A/D 회전 + 기울기, Shift 부스트, Space 브레이크
/// </summary>
public class MotorcycleTestDrive : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] float maxSpeed = 10f;
    [SerializeField] float acceleration = 6f;
    [SerializeField] float brake = 12f;          // 반대 방향 입력 시 감속
    [SerializeField] float coastDecel = 3f;      // 아무 입력 없을 때 자연 감속
    [SerializeField] float reverseMaxSpeed = 3f;
    [SerializeField] float turnSpeed = 90f;

    [Header("Boost (Shift)")]
    [SerializeField] float boostMaxSpeed = 18f;
    [SerializeField] float boostAcceleration = 12f;
    [SerializeField] float boostReleaseDecel = 4f;

    [Header("Brake (Space)")]
    [SerializeField] float handBrakeDecel = 25f; // 스페이스 브레이크 감속도 (m/s²)

    [Header("Lean")]
    [SerializeField] float maxLeanAngle = 35f;
    [SerializeField] float leanSmooth = 6f;
    [SerializeField] float fullLeanSpeed = 6f;
    [SerializeField] bool invertLean = false;

    public float Speed => speed;
    public float SpeedKmh => speed * 3.6f;
    public float MaxSpeed => maxSpeed;
    public float BoostMaxSpeed => boostMaxSpeed;
    public bool IsBoosting { get; private set; }
    /// <summary>브레이크등용: 스페이스를 누르거나, 전진 중 S를 누르면 true</summary>
    public bool IsBraking { get; private set; }
    /// <summary>스페이스(핸드 브레이크)를 누르고 있는 동안만 true</summary>
    public bool IsHandBraking { get; private set; }
    public float LeanAngle => lean;

    float speed, yaw, lean;

    void Start() => yaw = transform.eulerAngles.y;

    void Update()
    {
        ReadInput(out float v, out float h, out bool boostKey, out bool brakeKey);
        float dt = Time.deltaTime;

        IsBoosting = boostKey && v > 0f && !brakeKey;
        IsBraking = brakeKey || (v < 0f && speed > 0.1f) || (v > 0f && speed < -0.1f);
        IsHandBraking = brakeKey;

        if (brakeKey)
        {
            // 스페이스: 입력과 상관없이 강하게 0으로
            speed = Mathf.MoveTowards(speed, 0f, handBrakeDecel * dt);
        }
        else
        {
            float target;
            if (v > 0f) target = IsBoosting ? boostMaxSpeed : maxSpeed;
            else if (v < 0f) target = -reverseMaxSpeed;
            else target = 0f;

            float rate;
            if (target != 0f && Mathf.Sign(target) != Mathf.Sign(speed) && Mathf.Abs(speed) > 0.01f)
                rate = brake;
            else if (Mathf.Abs(target) > Mathf.Abs(speed))
                rate = IsBoosting ? boostAcceleration : acceleration;
            else if (v > 0f && speed > maxSpeed)
                rate = boostReleaseDecel;
            else
                rate = coastDecel;
            speed = Mathf.MoveTowards(speed, target, rate * dt);
        }

        float speed01 = Mathf.Clamp(speed / maxSpeed, -1f, 1f);
        yaw += h * turnSpeed * speed01 * dt;

        float leanFactor = Mathf.Clamp01(Mathf.Max(speed, 0f) / fullLeanSpeed);
        lean = Mathf.Lerp(lean, h * maxLeanAngle * leanFactor, 1f - Mathf.Exp(-leanSmooth * dt));

        float roll = invertLean ? lean : -lean;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, roll);
        transform.position += Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * speed * dt;
    }

    void ReadInput(out float v, out float h, out bool boost, out bool brakeKey)
    {
        v = 0f; h = 0f; boost = false; brakeKey = false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
        boost = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        brakeKey = kb.spaceKey.isPressed;
#else
        v = Input.GetAxisRaw("Vertical");
        h = Input.GetAxisRaw("Horizontal");
        boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        brakeKey = Input.GetKey(KeyCode.Space);
#endif
    }
}