using UnityEngine;

/// <summary>
/// 총에 맞으면 깨지는 소품 (라디오 등). 멀쩡한 모델 루트에 붙인다.
///  - 처음 맞으면: 멀쩡한 모델을 숨기고 깨진 조각(Broken Root)을 켜서 맞은 곳에서 바깥으로 튕김
///  - 깨진 뒤: 조각이 맞으면 맞은 방향으로 밀림
///  - 새 라운드 시작 시 원래대로
///  - 탄흔: 깨질 때 / 복구될 때 이 소품에 붙은 탄흔을 지움 (ImpactEffects)
/// 판정은 GunWeapon.AnyShotImpact (내 총 + 다른 사람 총 모두) → 멀티는 각자 로컬 물리
/// </summary>
public class BreakableProp : MonoBehaviour
{
    [Header("멀쩡한 모델")]
    [SerializeField] private Renderer intactRenderer;
    [SerializeField] private Collider intactCollider;

    [Header("깨진 모델 (처음엔 꺼둠)")]
    [SerializeField] private GameObject brokenRoot;
    [SerializeField] private Rigidbody[] pieces;

    [Header("힘")]
    [Tooltip("깨질 때 조각이 튕기는 힘")]
    [SerializeField] private float breakForce = 3f;
    [Tooltip("조각이 맞았을 때 밀리는 힘")]
    [SerializeField] private float hitForce = 4f;
    [Tooltip("이 거리(m) 안에 탄이 맞으면 맞은 것으로 봄")]
    [SerializeField] private float hitRadius = 0.05f;

    private Vector3[] startPos;
    private Quaternion[] startRot;
    private Collider[] pieceCols;
    private bool broken;
    private RoundManager round;

    private void Awake()
    {
        startPos = new Vector3[pieces.Length];
        startRot = new Quaternion[pieces.Length];
        pieceCols = new Collider[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            startPos[i] = pieces[i].transform.localPosition;
            startRot[i] = pieces[i].transform.localRotation;
            pieceCols[i] = pieces[i].GetComponent<Collider>();
        }
        Restore();
    }

    private void OnEnable() => GunWeapon.AnyShotImpact += HandleShot;

    private void OnDisable()
    {
        GunWeapon.AnyShotImpact -= HandleShot;
        if (round != null) round.OnRoundStart -= HandleRoundStart;
    }

    private void Start()
    {
        round = RoundManager.Instance;
        if (round != null) round.OnRoundStart += HandleRoundStart;
    }

    private void HandleRoundStart(int r) => Restore();

    private void HandleShot(Vector3[] points)
    {
        if (points == null) return;
        foreach (var p in points)
        {
            if (!broken)
            {
                if (Near(intactCollider, p)) Break(p);
            }
            else
            {
                for (int i = 0; i < pieces.Length; i++)
                    if (Near(pieceCols[i], p)) Push(pieces[i], p, hitForce);
            }
        }
    }

    private bool Near(Collider c, Vector3 p)
    {
        if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) return false;
        return (c.ClosestPoint(p) - p).sqrMagnitude <= hitRadius * hitRadius;
    }

    private void Break(Vector3 p)
    {
        if (ImpactEffects.Instance != null) ImpactEffects.Instance.ClearDecalsOn(transform);   // 멀쩡한 모델에 남은 탄흔 제거
        broken = true;
        intactRenderer.enabled = false;
        intactCollider.enabled = false;
        brokenRoot.SetActive(true);
        foreach (var rb in pieces)
        {
            rb.isKinematic = false;
            Vector3 dir = rb.worldCenterOfMass - p;
            dir.y = 0f;
            dir = dir.normalized + Vector3.up * 0.3f;   // 옆으로 갈라지며 살짝 들림
            rb.AddForceAtPosition(dir * breakForce, p, ForceMode.Impulse);
        }
    }

    private static void Push(Rigidbody rb, Vector3 p, float force)
    {
        Vector3 dir = (rb.worldCenterOfMass - p).normalized;
        rb.AddForceAtPosition(dir * force, p, ForceMode.Impulse);
    }

    /// <summary>원래대로 (멀쩡한 모델 표시, 조각은 제자리로 돌려놓고 꺼둠)</summary>
    public void Restore()
    {
        if (ImpactEffects.Instance != null) ImpactEffects.Instance.ClearDecalsOn(transform);   // 조각에 붙은 탄흔 제거
        broken = false;
        if (intactRenderer != null) intactRenderer.enabled = true;
        if (intactCollider != null) intactCollider.enabled = true;
        for (int i = 0; i < pieces.Length; i++)
        {
            var rb = pieces[i];
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
            rb.transform.localPosition = startPos[i];
            rb.transform.localRotation = startRot[i];
        }
        if (brokenRoot != null) brokenRoot.SetActive(false);
    }
}