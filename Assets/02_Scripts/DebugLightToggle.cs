using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 디버그용 조명 토글 (에디터 / Development Build 에서만 동작)
///  1키 : 손전등 On/Off
///  2키 : 맵 전체 밝게 (디버그 Directional Light + 환경광) On/Off
/// </summary>
public class DebugLightToggle : MonoBehaviour
{
    [Header("1키: 손전등 (카메라 자식 Spot Light)")]
    [SerializeField] private Light flashlight;

    [Header("2키: 맵 전체 밝게")]
    [Tooltip("Shadow Type = No Shadows 로 설정된 Directional Light (컴포넌트는 꺼둔 상태로 저장)")]
    [SerializeField] private Light debugSun;
    [Tooltip("디버그 모드일 때 환경광 밝기")]
    [SerializeField] private Color debugAmbient = new Color(0.5f, 0.5f, 0.5f);

    private Color savedAmbient;
    private bool debugBright;

    private void Start()
    {
        if (flashlight != null) flashlight.enabled = false;
        if (debugSun != null) debugSun.enabled = false;
        savedAmbient = RenderSettings.ambientLight;
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var kb = Keyboard.current;
        if (kb == null) return;

        // 1키: 손전등
        if (kb.digit1Key.wasPressedThisFrame && flashlight != null)
            flashlight.enabled = !flashlight.enabled;

        // 2키: 맵 전체 밝게
        if (kb.digit2Key.wasPressedThisFrame)
            SetDebugBright(!debugBright);
#endif
    }

    private void SetDebugBright(bool on)
    {
        debugBright = on;
        RenderSettings.ambientLight = on ? debugAmbient : savedAmbient;
        if (debugSun != null) debugSun.enabled = on;
    }

    // 오브젝트가 꺼지거나 Play 종료 시 원래 상태로 복구
    private void OnDisable()
    {
        if (debugBright) SetDebugBright(false);
    }
}