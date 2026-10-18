using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 무기 전환. WeaponHolder에 붙인다.
///  - 자식 중 GunWeapon이 있는 오브젝트를 순서대로 무기 슬롯으로 사용 (Hierarchy 순서)
///  - 숫자 키 1, 2, 3 ... 으로 해당 슬롯 선택, 마우스 휠로 순환
///  - 선택한 무기만 켜고 나머지는 끔 (꺼진 무기는 손 IK 목표도 자동으로 해제됨)
///  - 로컬 플레이어(루트가 Player 태그)에서만 입력을 받음
/// 나중에 "주무기 선택"이 생기면 SetAllowed()로 쓸 수 있는 슬롯을 제한하면 됨.
/// </summary>
public class WeaponSwitcher : MonoBehaviour
{
    [Tooltip("비우면 자식에서 자동 검색 (Hierarchy 순서)")]
    [SerializeField] private GunWeapon[] weapons;
    [Tooltip("시작 시 선택할 슬롯 (0부터)")]
    [SerializeField, Min(0)] private int startIndex = 0;
    [Tooltip("휠 한 칸으로 전환되는 최소 간격 (초)")]
    [SerializeField, Min(0f)] private float wheelCooldown = 0.15f;
    [Tooltip("휠 방향 반대로")]
    [SerializeField] private bool invertWheel = false;

    /// <summary>무기가 바뀌었을 때 (새 슬롯 번호, 무기)</summary>
    public event Action<int, GunWeapon> OnSwitched;

    public int CurrentIndex { get; private set; } = -1;
    public GunWeapon Current => CurrentIndex >= 0 && CurrentIndex < weapons.Length ? weapons[CurrentIndex] : null;
    public int Count => weapons != null ? weapons.Length : 0;
    /// <summary>슬롯 번호로 무기 가져오기 (없으면 null)</summary>
    public GunWeapon GetWeapon(int index) => weapons != null && index >= 0 && index < weapons.Length ? weapons[index] : null;

    private bool[] allowed;
    private bool isLocal;
    private float nextWheelTime;

    private static readonly Key[] NumberKeys =
        { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };

    private void Awake()
    {
        if (weapons == null || weapons.Length == 0)
            weapons = GetComponentsInChildren<GunWeapon>(true);

        allowed = new bool[weapons.Length];
        for (int i = 0; i < allowed.Length; i++) allowed[i] = true;

        isLocal = transform.root.CompareTag("Player");
        Select(Mathf.Clamp(startIndex, 0, Mathf.Max(0, weapons.Length - 1)), true);
    }

    private void Update()
    {
        if (!isLocal || weapons.Length == 0) return;
        if (PauseMenuUI.IsOpen) return;   // ESC 메뉴 중

        var kb = Keyboard.current;
        if (kb != null)
        {
            for (int i = 0; i < weapons.Length && i < NumberKeys.Length; i++)
                if (kb[NumberKeys[i]].wasPressedThisFrame) Select(i);
        }

        var mouse = Mouse.current;
        if (mouse != null && Time.time >= nextWheelTime)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                int dir = scroll > 0f ? -1 : 1;   // 휠 위 = 이전 슬롯
                if (invertWheel) dir = -dir;
                Cycle(dir);
                nextWheelTime = Time.time + wheelCooldown;
            }
        }
    }

    /// <summary>다음/이전 사용 가능한 슬롯으로</summary>
    public void Cycle(int dir)
    {
        if (weapons.Length == 0) return;
        int i = CurrentIndex;
        for (int n = 0; n < weapons.Length; n++)
        {
            i = (i + dir + weapons.Length) % weapons.Length;
            if (allowed[i]) { Select(i); return; }
        }
    }

    /// <summary>슬롯 선택</summary>
    public void Select(int index, bool force = false)
    {
        if (index < 0 || index >= weapons.Length) return;
        if (!allowed[index]) return;
        if (!force && index == CurrentIndex) return;

        for (int i = 0; i < weapons.Length; i++)
            if (weapons[i] != null) weapons[i].gameObject.SetActive(i == index);

        CurrentIndex = index;
        OnSwitched?.Invoke(index, weapons[index]);
    }

    /// <summary>슬롯 사용 가능 여부 (주무기 선택 등). 현재 무기가 막히면 첫 번째 가능한 슬롯으로</summary>
    public void SetAllowed(int index, bool value)
    {
        if (index < 0 || index >= allowed.Length) return;
        allowed[index] = value;
        if (!value && index == CurrentIndex)
        {
            for (int i = 0; i < allowed.Length; i++)
                if (allowed[i]) { Select(i, true); return; }
        }
    }
}
