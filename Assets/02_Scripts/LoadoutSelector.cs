using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 라운드 시작 카운트다운 동안 무기 1개를 고르게 한다. (권총 / 샷건 / 스나이퍼 중 하나)
/// 씬의 RoundManager 오브젝트(또는 아무 오브젝트)에 붙인다.
///  - OnRoundStart(카운트다운 시작): 선택 UI 표시, 숫자 키 1/2/3으로 선택 → 고른 무기를 바로 손에 보여줌
///  - OnPlayStart(GO): 고른 무기만 허용, 나머지 슬롯은 잠금 → 라운드 동안 무기 전환 불가
///  - 안 고르면 지난 라운드 무기 유지 (첫 라운드는 Default Index)
/// 멀티: 내 플레이어가 늦게 스폰돼도 동작 (라운드 신호는 바로 구독, 무기는 찾는 즉시 현재 상태 적용)
/// 슬롯 순서 = WeaponSwitcher의 무기 순서 (WeaponHolder 자식 순서: Pistol, Shotgun, Sniper)
/// </summary>
public class LoadoutSelector : MonoBehaviour
{
    [Tooltip("비우면 스폰된 내 플레이어(PlayerNetworkSetup.Local)에서 자동 검색")]
    [SerializeField] private WeaponSwitcher switcher;
    [Tooltip("첫 라운드 기본 무기 슬롯 (0부터)")]
    [SerializeField, Min(0)] private int defaultIndex = 0;
    [Tooltip("RoundManager가 없을 때(테스트 씬)도 무기 1개로 제한할지")]
    [SerializeField] private bool lockWithoutRoundManager = false;

    [Header("UI")]
    [SerializeField] private bool showUI = true;
    [SerializeField] private int fontSize = 22;
    [SerializeField, Range(0f, 1f)] private float panelY = 0.72f;
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.55f);
    [SerializeField] private Color selectedColor = new Color(0.3f, 1f, 0.9f, 1f);

    public int SelectedIndex { get; private set; } = -1;
    public bool IsSelecting { get; private set; }

    private RoundManager round;
    private static readonly Key[] NumberKeys =
        { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };

    private GUIStyle style;

    private bool ready;          // 내 무기(WeaponSwitcher)를 찾았는지
    private bool pendingLock;    // 무기를 찾기 전에 GO 신호가 와서 잠금을 미뤄둠

    private void Start()
    {
        // 라운드 신호는 바로 구독 (내 플레이어가 늦게 스폰돼도 신호를 놓치지 않게)
        round = RoundManager.Instance;
        if (round != null)
        {
            round.OnRoundStart += HandleRoundStart;
            round.OnPlayStart += HandlePlayStart;
        }
    }

    /// <summary>내 플레이어의 WeaponSwitcher 찾기 (네트워크 스폰이 늦을 수 있어 매 프레임 확인)</summary>
    private bool EnsureSwitcher()
    {
        if (switcher != null && switcher.Count > 0) return true;

        var local = PlayerNetworkSetup.Local;
        if (local != null) switcher = local.GetComponentInChildren<WeaponSwitcher>(true);
        if (switcher == null || switcher.Count == 0) return false;

        if (SelectedIndex < 0) SelectedIndex = Mathf.Clamp(defaultIndex, 0, switcher.Count - 1);
        return true;
    }

    /// <summary>무기를 처음 찾았을 때 현재 라운드 상태에 맞춰 적용</summary>
    private void OnSwitcherReady()
    {
        ready = true;

        if (round == null)
        {
            if (lockWithoutRoundManager) Lock();
            return;
        }

        if (round.CurrentState == RoundManager.State.Intro || IsSelecting)
        {
            IsSelecting = true;
            ApplySelecting();
        }
        else if (pendingLock || round.CurrentState == RoundManager.State.Playing)
        {
            Lock();
        }
        pendingLock = false;
    }

    private void OnDestroy()
    {
        if (round != null)
        {
            round.OnRoundStart -= HandleRoundStart;
            round.OnPlayStart -= HandlePlayStart;
        }
    }

    // ───────── 라운드 흐름 ─────────

    private void HandleRoundStart(int roundNumber)
    {
        IsSelecting = true;
        pendingLock = false;
        if (EnsureSwitcher()) ApplySelecting();   // 아직 무기가 없으면 찾는 순간 적용 (OnSwitcherReady)
    }

    private void HandlePlayStart(int roundNumber)
    {
        IsSelecting = false;
        if (EnsureSwitcher()) Lock();
        else pendingLock = true;
    }

    /// <summary>선택 중: WeaponSwitcher 입력(숫자 키/휠)을 끄고, 모든 슬롯을 열어 미리보기 가능하게</summary>
    private void ApplySelecting()
    {
        switcher.enabled = false;
        for (int i = 0; i < switcher.Count; i++) switcher.SetAllowed(i, true);
        switcher.Select(SelectedIndex, true);
    }

    /// <summary>선택한 무기만 허용하고 손에 쥐어줌</summary>
    private void Lock()
    {
        for (int i = 0; i < switcher.Count; i++) switcher.SetAllowed(i, true);
        switcher.Select(SelectedIndex, true);
        for (int i = 0; i < switcher.Count; i++)
            if (i != SelectedIndex) switcher.SetAllowed(i, false);
        switcher.enabled = true;   // 다른 슬롯이 잠겨 있으니 키/휠을 눌러도 바뀌지 않음
    }

    // ───────── 입력 ─────────

    private void Update()
    {
        if (!ready)
        {
            if (!EnsureSwitcher()) return;
            OnSwitcherReady();
        }

        if (!IsSelecting || switcher == null) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        for (int i = 0; i < switcher.Count && i < NumberKeys.Length; i++)
        {
            if (kb[NumberKeys[i]].wasPressedThisFrame)
            {
                Choose(i);
                break;
            }
        }
    }

    /// <summary>무기 선택 (UI 버튼에서 호출해도 됨)</summary>
    public void Choose(int index)
    {
        if (!EnsureSwitcher() || index < 0 || index >= switcher.Count) return;
        SelectedIndex = index;
        if (IsSelecting) switcher.Select(index, true);   // 미리보기
    }

    // ───────── 임시 UI ─────────

    private void OnGUI()
    {
        if (!showUI || !IsSelecting || switcher == null) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }
        style.fontSize = fontSize;

        int n = switcher.Count;
        float slotW = fontSize * 8f;
        float h = fontSize * 2f;
        float totalW = slotW * n;
        float x = (Screen.width - totalW) * 0.5f;
        float y = Screen.height * panelY;

        var old = GUI.color;
        GUI.color = normalColor;
        GUI.Label(new Rect(0, y - h, Screen.width, h), "SELECT WEAPON", style);

        for (int i = 0; i < n; i++)
        {
            var w = switcher.GetWeapon(i);
            string name = w != null ? w.WeaponName.ToUpperInvariant() : $"SLOT {i + 1}";
            bool sel = i == SelectedIndex;
            GUI.color = sel ? selectedColor : normalColor;
            string text = sel ? $"▶ [{i + 1}] {name} ◀" : $"[{i + 1}] {name}";
            GUI.Label(new Rect(x + slotW * i, y, slotW, h), text, style);
        }
        GUI.color = old;
    }
}
