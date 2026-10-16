using UnityEngine;

/// <summary>
/// 벽에 가까우면 총을 거둬서(뒤로 당기고 총구를 내림) 총이 벽에 묻히지 않게 한다.
/// WeaponHolder에 붙인다 (부모는 PlayerCameraRoot).
///  - 카메라 정면으로 짧게 SphereCast → 벽까지 거리가 무기 길이(GunWeapon.BarrelLength)보다 짧으면 거둠
///  - 많이 거둔 상태(Block Fire At 이상)에서는 발사 불가 (GunWeapon이 BlocksFire를 확인)
///  - Character 레이어(내 몸, 마네킹, 다른 플레이어)는 무시
/// </summary>
[DefaultExecutionOrder(100)]
public class WeaponWallRetract : MonoBehaviour
{
    [Header("감지")]
    [Tooltip("레이 시작점 (비우면 부모 = PlayerCameraRoot)")]
    [SerializeField] private Transform aimSource;
    [Tooltip("벽으로 볼 레이어 (Default, Ground, Obstacle)")]
    [SerializeField] private LayerMask wallMask = (1 << 0) | (1 << 7) | (1 << 8);
    [SerializeField, Min(0.001f)] private float probeRadius = 0.05f;
    [Tooltip("무기에 길이 정보가 없을 때 쓰는 길이 (m)")]
    [SerializeField, Min(0.1f)] private float defaultLength = 0.7f;
    [Tooltip("벽까지 이 거리 이하면 최대로 거둠 (캐릭터 반경 정도)")]
    [SerializeField, Min(0f)] private float minDistance = 0.3f;

    [Header("거두는 모양 (최대치)")]
    [Tooltip("카메라 기준 이동 (x 오른쪽, y 위, z 앞)")]
    [SerializeField] private Vector3 retractOffset = new Vector3(0f, -0.08f, -0.2f);
    [Tooltip("총구를 아래로 내리는 각도 (도)")]
    [SerializeField] private float retractPitch = 35f;
    [SerializeField, Min(0.1f)] private float speed = 12f;

    [Header("발사 제한")]
    [Tooltip("이 정도 이상 거두면 발사 불가 (0~1, 1이면 제한 없음)")]
    [SerializeField, Range(0f, 1f)] private float blockFireAt = 0.6f;

    [Header("디버그")]
    [SerializeField] private bool drawGizmos = false;

    /// <summary>현재 거둔 정도 (0 = 정상, 1 = 최대)</summary>
    public float Amount { get; private set; }
    public bool BlocksFire => blockFireAt < 1f && Amount >= blockFireAt;

    private WeaponSwitcher switcher;
    private Vector3 appliedPos;
    private Quaternion appliedRot = Quaternion.identity;
    private float lastHitDistance = -1f;

    private void Awake()
    {
        if (aimSource == null) aimSource = transform.parent != null ? transform.parent : transform;
        switcher = GetComponent<WeaponSwitcher>();
    }

    private void OnDisable()
    {
        // 원래 자리로
        transform.localPosition -= appliedPos;
        transform.localRotation = Quaternion.Inverse(appliedRot) * transform.localRotation;
        appliedPos = Vector3.zero;
        appliedRot = Quaternion.identity;
        Amount = 0f;
    }

    private void LateUpdate()
    {
        float length = defaultLength;
        if (switcher != null && switcher.Current != null) length = switcher.Current.BarrelLength;

        float target = 0f;
        lastHitDistance = -1f;
        if (Physics.SphereCast(aimSource.position, probeRadius, aimSource.forward, out RaycastHit hit,
                               length, wallMask, QueryTriggerInteraction.Ignore))
        {
            lastHitDistance = hit.distance;
            // length에서 0 → minDistance에서 1
            target = Mathf.InverseLerp(length, Mathf.Min(minDistance, length - 0.01f), hit.distance);
        }

        Amount = Mathf.Lerp(Amount, target, 1f - Mathf.Exp(-speed * Time.deltaTime));

        // 이전 프레임에 더한 값을 빼고 새 값을 더함 → Inspector에서 위치를 바꿔도 유지
        Vector3 newPos = retractOffset * Amount;
        Quaternion newRot = Quaternion.Euler(retractPitch * Amount, 0f, 0f);

        transform.localPosition += newPos - appliedPos;
        transform.localRotation = newRot * Quaternion.Inverse(appliedRot) * transform.localRotation;

        appliedPos = newPos;
        appliedRot = newRot;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || aimSource == null) return;
        float length = defaultLength;
        if (switcher != null && switcher.Current != null) length = switcher.Current.BarrelLength;
        Gizmos.color = lastHitDistance >= 0f ? Color.red : Color.green;
        Vector3 end = aimSource.position + aimSource.forward * (lastHitDistance >= 0f ? lastHitDistance : length);
        Gizmos.DrawLine(aimSource.position, end);
        Gizmos.DrawWireSphere(end, probeRadius);
    }
}
