using UnityEngine;

/// <summary>
/// 하늘의 달 (Quad + 달 이미지). Moon 오브젝트에 붙인다.
///  - 항상 카메라(플레이어 시점)를 정면으로 바라봄
///  - Follow Camera: 씬에 놓은 위치를 "하늘 방향 + 거리"로 기억하고 카메라를 따라 이동
///    → 맵 어디로 가든 하늘의 같은 자리, 같은 크기로 보임 (스카이박스처럼 가까워지지 않음)
///  - 위치는 씬에서 Moon을 옮겨서 정함 (월드 원점 기준 방향/거리로 사용)
/// 충돌체가 있으면 총알·레이캐스트에 걸리지 않게 자동으로 끔.
/// </summary>
[DisallowMultipleComponent]
public class MoonBillboard : MonoBehaviour
{
    [Tooltip("카메라를 따라 이동 (하늘의 같은 자리에 고정). 끄면 제자리에서 회전만")]
    [SerializeField] private bool followCamera = true;
    [Tooltip("비우면 Main Camera 자동 사용")]
    [SerializeField] private Camera targetCamera;

    private Vector3 skyOffset;   // 월드 원점 기준 달 위치 (방향 × 거리)

    private void Awake()
    {
        skyOffset = transform.position;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
    }

    private void LateUpdate()
    {
        var cam = targetCamera;
        if (cam == null || !cam.isActiveAndEnabled)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        Vector3 camPos = cam.transform.position;
        if (followCamera) transform.position = camPos + skyOffset;

        // Quad의 보이는 면(-Z 쪽)이 카메라를 향하도록: +Z가 카메라 반대쪽
        Vector3 away = transform.position - camPos;
        if (away.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.7f, 0.8f, 1f, 0.6f);
        Gizmos.DrawLine(Vector3.zero, transform.position);
    }
}
