using System;
using UnityEngine;

/// <summary>
/// 라운드 무기 지급 (BlindFire 방식). 씬의 RoundManager 오브젝트에 붙인다.
///  - 라운드가 시작될 때(카운트다운) 호스트가 Weapon Names 중 하나를 랜덤으로 정함 → 모든 플레이어가 같은 총
///    참가자는 호스트가 보낸 값을 사용 (RoundNetworkSync.RoundWeaponRpc). 싱글은 혼자 정함
///  - 내 플레이어: 그 총만 허용하고 손에 쥐어줌 (라운드 동안 전환 불가)
///  - 마네킹·봇: ThirdPersonWeapon이 RoundWeaponChanged를 받아 같은 총을 듦 (총으로 구분 안 되게)
///  - 카운트다운 동안 화면에 "WEAPON: 이름" 표시
/// Weapon Names 순서 = WeaponHolder 자식(GunWeapon) 순서. 이름은 GunWeapon.WeaponName / 3인칭 WeaponSocket 자식 이름과 같게.
/// </summary>
public class LoadoutSelector : MonoBehaviour
{
    [Tooltip("지급 후보 (WeaponHolder 자식 순서와 같게)")]
    [SerializeField] private string[] weaponNames = { "A3500X", "SCAR-L" };
    [Tooltip("비우면 스폰된 내 플레이어(PlayerNetworkSetup.Local)에서 자동 검색")]
    [SerializeField] private WeaponSwitcher switcher;
    [Tooltip("RoundManager가 없을 때(테스트 씬)도 랜덤 1개로 제한할지")]
    [SerializeField] private bool lockWithoutRoundManager = false;

    [Header("UI (카운트다운 중 무기 이름)")]
    [SerializeField] private bool showUI = true;
    [SerializeField] private int fontSize = 26;
    [SerializeField, Range(0f, 1f)] private float panelY = 0.72f;
    [SerializeField] private Color textColor = new Color(0.3f, 1f, 0.9f, 1f);

    /// <summary>이번 라운드 무기 번호 (-1 = 아직 없음)</summary>
    public static int RoundWeaponIndex { get; private set; } = -1;
    /// <summary>이번 라운드 무기 이름 (null = 아직 없음)</summary>
    public static string RoundWeaponName { get; private set; }
    /// <summary>라운드 무기가 정해질 때 (이름)</summary>
    public static event Action<string> RoundWeaponChanged;

    public static LoadoutSelector Instance { get; private set; }

    private RoundManager round;
    private GUIStyle style;
    private bool applied;   // 현재 무기를 내 손에 적용했는지

    private void Awake()
    {
        Instance = this;
        RoundWeaponIndex = -1;
        RoundWeaponName = null;
    }

    private void Start()
    {
        round = RoundManager.Instance;
        if (round != null) round.OnRoundStart += HandleRoundStart;
        else if (lockWithoutRoundManager) SetRoundWeapon(PickRandom());
    }

    private void OnDestroy()
    {
        if (round != null) round.OnRoundStart -= HandleRoundStart;
        if (Instance == this) Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        RoundWeaponIndex = -1;
        RoundWeaponName = null;
        RoundWeaponChanged = null;
    }

    // ───────── 결정 ─────────

    private int PickRandom() => weaponNames != null && weaponNames.Length > 0 ? UnityEngine.Random.Range(0, weaponNames.Length) : -1;

    private void HandleRoundStart(int roundNumber)
    {
        if (round != null && round.IsFollower) return;   // 참가자는 호스트가 보낸 값 사용
        int idx = PickRandom();
        SetRoundWeapon(idx);
        var ch = RoundNetworkSync.Channel;
        if (ch != null) ch.SendRoundWeapon(idx);
    }

    /// <summary>디버그: 다음 무기로 바꿈 (호스트 / 싱글, 모두에게 적용)</summary>
    public void DebugCycle()
    {
        if (round != null && round.IsFollower) return;
        if (weaponNames == null || weaponNames.Length == 0) return;
        int idx = (RoundWeaponIndex + 1) % weaponNames.Length;
        SetRoundWeapon(idx);
        var ch = RoundNetworkSync.Channel;
        if (ch != null) ch.SendRoundWeapon(idx);
        Debug.Log($"[LoadoutSelector] 무기: {RoundWeaponName}");
    }

    /// <summary>참가자: 호스트가 정한 라운드 무기</summary>
    public void ApplyFromHost(int index) => SetRoundWeapon(index);

    private void SetRoundWeapon(int index)
    {
        if (weaponNames == null || index < 0 || index >= weaponNames.Length) return;
        RoundWeaponIndex = index;
        RoundWeaponName = weaponNames[index];
        applied = false;
        TryApply();
        RoundWeaponChanged?.Invoke(RoundWeaponName);
    }

    // ───────── 내 손에 적용 ─────────

    private void Update()
    {
        if (applied && switcher == null) applied = false;   // 플레이어가 다시 생성됨
        if (!applied) TryApply();   // 내 플레이어가 늦게 스폰돼도 찾는 즉시 적용
    }

    private bool EnsureSwitcher()
    {
        if (switcher != null && switcher.Count > 0) return true;
        var local = PlayerNetworkSetup.Local;
        if (local != null) switcher = local.GetComponentInChildren<WeaponSwitcher>(true);
        return switcher != null && switcher.Count > 0;
    }

    private void TryApply()
    {
        if (RoundWeaponIndex < 0 || !EnsureSwitcher()) return;
        int slot = FindSlot(RoundWeaponName);
        if (slot < 0) slot = Mathf.Clamp(RoundWeaponIndex, 0, switcher.Count - 1);

        for (int i = 0; i < switcher.Count; i++) switcher.SetAllowed(i, true);
        switcher.Select(slot, true);
        for (int i = 0; i < switcher.Count; i++)
            if (i != slot) switcher.SetAllowed(i, false);
        switcher.enabled = true;   // 다른 슬롯이 잠겨 있어 키/휠로 바뀌지 않음
        applied = true;
    }

    /// <summary>WeaponHolder에서 이름이 같은 무기 슬롯</summary>
    private int FindSlot(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        for (int i = 0; i < switcher.Count; i++)
        {
            var w = switcher.GetWeapon(i);
            if (w != null && string.Equals(w.WeaponName, name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    // ───────── UI ─────────

    private void OnGUI()
    {
        if (!showUI || string.IsNullOrEmpty(RoundWeaponName)) return;
        if (round == null || round.CurrentState != RoundManager.State.Intro) return;

        if (style == null)
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        style.fontSize = fontSize;

        var old = GUI.color;
        GUI.color = textColor;
        float h = fontSize * 2f;
        GUI.Label(new Rect(0, Screen.height * panelY, Screen.width, h), $"WEAPON: {RoundWeaponName.ToUpperInvariant()}", style);
        GUI.color = old;
    }
}
