using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정 패널 UI ↔ GameSettings 연결.
/// 슬라이더/토글을 움직이면 즉시 적용·저장. 로비와 (나중에) 게임 중 ESC 메뉴에서 재사용 가능.
/// BACK 버튼은 이 패널을 연 쪽(LobbyManager 등)에서 처리.
/// </summary>
public class SettingsPanelUI : MonoBehaviour
{
    [Header("슬라이더 (0~100)")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider sensitivitySlider;

    [Header("숫자 표시")]
    [SerializeField] private TMP_Text masterValue;
    [SerializeField] private TMP_Text musicValue;
    [SerializeField] private TMP_Text sfxValue;
    [SerializeField] private TMP_Text sensitivityValue;

    [Header("기타")]
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Button resetButton;

    private bool refreshing;

    private void Awake()
    {
        Setup(masterSlider, 0f, 100f, v => GameSettings.SetMaster(v));
        Setup(musicSlider, 0f, 100f, v => GameSettings.SetMusic(v));
        Setup(sfxSlider, 0f, 100f, v => GameSettings.SetSfx(v));
        Setup(sensitivitySlider, GameSettings.MinSensitivity, GameSettings.MaxSensitivity, v => GameSettings.SetSensitivity(v));

        if (fullscreenToggle != null)
            fullscreenToggle.onValueChanged.AddListener(on => { if (!refreshing) GameSettings.SetFullscreen(on); });
        if (resetButton != null)
            resetButton.onClick.AddListener(GameSettings.ResetDefaults);
    }

    private void OnEnable()
    {
        GameSettings.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        GameSettings.Changed -= Refresh;
    }

    private void Setup(Slider s, float min, float max, System.Action<float> onChange)
    {
        if (s == null) return;
        s.minValue = min;
        s.maxValue = max;
        s.wholeNumbers = true;
        s.onValueChanged.AddListener(v => { if (!refreshing) onChange(v); });
        if (s == sensitivitySlider)   // 감도: 5 단위로 스냅 (슬라이더 손잡이도 맞춰 이동)
            s.onValueChanged.AddListener(v =>
            {
                if (refreshing) return;
                float snapped = GameSettings.SnapSensitivity(v);
                if (!Mathf.Approximately(snapped, v)) s.SetValueWithoutNotify(snapped);
            });
    }

    /// <summary>저장된 값으로 UI 갱신</summary>
    private void Refresh()
    {
        refreshing = true;
        Set(masterSlider, masterValue, GameSettings.Master);
        Set(musicSlider, musicValue, GameSettings.Music);
        Set(sfxSlider, sfxValue, GameSettings.Sfx);
        Set(sensitivitySlider, sensitivityValue, GameSettings.Sensitivity);
        if (sensitivityValue != null) sensitivityValue.text = GameSettings.SensitivityDisplay;   // 배율로 표시 (1.0)
        if (fullscreenToggle != null) fullscreenToggle.isOn = GameSettings.Fullscreen;
        refreshing = false;
    }

    private static void Set(Slider s, TMP_Text label, float value)
    {
        if (s != null) s.value = value;
        if (label != null) label.text = Mathf.RoundToInt(value).ToString();
    }
}
