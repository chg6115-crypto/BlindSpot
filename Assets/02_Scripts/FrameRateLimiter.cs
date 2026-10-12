using UnityEngine;

/// <summary>
/// 프레임 제한. GameManager에 붙여서 사용.
/// - Use Monitor Refresh: 모니터 주사율(60/144/240 등)에 맞춤
/// - 끄면 Target Fps 값으로 고정
/// </summary>
public class FrameRateLimiter : MonoBehaviour
{
    [SerializeField] private bool useMonitorRefresh = true;
    [SerializeField, Min(30)] private int targetFps = 144;
    [Tooltip("VSync 사용 시 targetFrameRate는 무시됨 (화면 찢어짐 방지 대신 입력 지연 약간 증가)")]
    [SerializeField] private bool vSync = false;

    private void Awake() => Apply();

    private void OnValidate()
    {
        if (Application.isPlaying) Apply();   // Play 중 Inspector에서 바꿔도 바로 반영
    }

    public void Apply()
    {
        QualitySettings.vSyncCount = vSync ? 1 : 0;

        int fps = targetFps;
        if (useMonitorRefresh)
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            if (hz > 1) fps = Mathf.RoundToInt((float)hz);
        }
        Application.targetFrameRate = vSync ? -1 : fps;
    }
}