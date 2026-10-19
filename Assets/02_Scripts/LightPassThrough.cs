using UnityEngine;

/// <summary>
/// 빛이 통과하는 물체 표시 (창문 유리 등). 콜라이더가 있는 오브젝트나 그 부모에 붙인다.
///  - RevealLight의 노출 판정(벽 검사)에서 이 물체는 막지 않는 것으로 봄
///  - 총알·이동 충돌 등 다른 판정에는 영향 없음
/// </summary>
[DisallowMultipleComponent]
public class LightPassThrough : MonoBehaviour
{
}
