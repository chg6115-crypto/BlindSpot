using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 오토바이 라이트 + 테마 색. 루트(Motorcycle)에 붙인다.
///  - 테마 색: 휠 림 발광(앞·뒤) + 전조등 렌즈 + 전조등 Spot Light 색을 한 번에 맞춤
///      Fixed : Theme Color 고정
///      Speed : 속도에 따라 Speed Colors(색 띠)를 따라 부드럽게 변함 (정지 = 왼쪽 끝 색). 빠를수록 림이 밝아짐
///  - 전조등: Z키로 켜기/끄기 (렌즈 발광 + Spot Light)
///  - 브레이크등: 스페이스를 누르는 동안만 켜짐 (Brake On Space Only 끄면 S 감속 때도). 기본 빨강, Tail Follows Theme 켜면 테마 색
/// 휠 림은 MaterialPropertyBlock으로 바꿔서 원본 머티리얼은 그대로 (휠 머티리얼의 Emission은 켜 두고 Emission Map = 림 마스크)
/// 외부에서 색을 바꾸려면 SetThemeColor() 사용 (나중에 트랩용)
/// </summary>
public class MotorcycleLights : MonoBehaviour
{
    public enum ColorMode { Fixed, Speed }

    [SerializeField] MotorcycleTestDrive drive;
    [Tooltip("이동 속도를 읽을 곳. 비우면 같은 오브젝트에서 자동 검색 (없으면 Drive 속도 사용)")]
    [SerializeField] MotorcycleWheelSpin wheelSpin;

    [Header("Theme Color")]
    [SerializeField] ColorMode colorMode = ColorMode.Speed;
    [Tooltip("Fixed 모드 색")]
    [SerializeField] Color themeColor = new Color(0.6f, 0.2f, 1f);
    [Tooltip("Speed 모드: 왼쪽 = 정지, 오른쪽 = Color Max Speed")]
    [SerializeField] Gradient speedColors = DefaultSpeedColors();
    [Tooltip("이 속도(m/s)에서 색 띠의 오른쪽 끝 색 (부스트 최고속도 18 기준)")]
    [SerializeField, Min(0.1f)] float colorMaxSpeed = 18f;
    [Tooltip("색이 바뀌는 부드러움 (클수록 빨리 따라감)")]
    [SerializeField, Min(0.1f)] float colorSmooth = 4f;

    [Header("Wheel Rim")]
    [Tooltip("림이 빛나는 휠 렌더러. 비우면 이름에 \"Wheel\"이 들어간 자식 자동 사용")]
    [SerializeField] Renderer[] rimRenderers;
    [Tooltip("림 밝기: 정지일 때 → Color Max Speed일 때")]
    [SerializeField] Vector2 rimIntensity = new Vector2(1.5f, 6f);

    [Header("Headlight")]
    [SerializeField] Renderer headlightRenderer;        // 발광할 메시 (Light_Head)
    [SerializeField] Light headlightLight;              // Spot Light (선택)
    [Tooltip("전조등 렌즈 밝기 (색은 테마 색)")]
    [SerializeField, Min(0f)] float headlightIntensity = 6f;
    [SerializeField] bool headlightOnAtStart = true;
#if ENABLE_INPUT_SYSTEM
    [Tooltip("전조등 켜기/끄기 키")]
    [SerializeField] Key headlightToggleKey = Key.Z;
#endif
    [Tooltip("끄면 키 입력 무시 (트랩 등 사람이 안 타는 오토바이)")]
    [SerializeField] bool playerControls = true;

    [Header("Brake / Tail light")]
    [SerializeField] Renderer brakeRenderer;            // 브레이크등 메시 (Light_Tail)
    [SerializeField] Light brakeLight;                  // 빨간 Point Light (선택)
    [SerializeField, ColorUsage(false, true)] Color tailEmission = Color.black;                                  // 평소
    [SerializeField, ColorUsage(false, true)] Color brakeEmission = new Color(1f, 0.05f, 0.03f) * 8f;            // 브레이크 시
    [Tooltip("켜면 브레이크등도 테마 색 (밝기는 Brake Emission 밝기 사용)")]
    [SerializeField] bool tailFollowsTheme = false;
    [SerializeField] float tailLightIntensity = 0f;
    [SerializeField] float brakeLightIntensity = 2f;
    [SerializeField] float fadeSpeed = 15f;
    [Tooltip("켜면 스페이스(핸드 브레이크)를 누를 때만 켜짐. 끄면 S로 감속할 때도 켜짐")]
    [SerializeField] bool brakeOnSpaceOnly = true;

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    Material headMat, brakeMat;
    MaterialPropertyBlock mpb;
    bool headlightOn;
    float brake01;
    float speed01Smoothed;
    Color currentColor;

    /// <summary>지금 테마 색 (밝기 제외)</summary>
    public Color CurrentColor => currentColor;

    static Gradient DefaultSpeedColors()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.6f, 0.2f, 1f), 0.45f),   // 보라
                new GradientColorKey(new Color(1f, 0.2f, 0.75f), 0.75f),  // 분홍
                new GradientColorKey(new Color(1f, 0.15f, 0.2f), 1f),     // 빨강 (부스트)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    void Awake()
    {
        if (drive == null) drive = GetComponent<MotorcycleTestDrive>();
        if (wheelSpin == null) wheelSpin = GetComponent<MotorcycleWheelSpin>();
        if (rimRenderers == null || rimRenderers.Length == 0)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.name.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) >= 0) list.Add(r);
            rimRenderers = list.ToArray();
        }

        mpb = new MaterialPropertyBlock();
        if (headlightRenderer != null) { headMat = headlightRenderer.material; headMat.EnableKeyword("_EMISSION"); }
        if (brakeRenderer != null) { brakeMat = brakeRenderer.material; brakeMat.EnableKeyword("_EMISSION"); }
        headlightOn = headlightOnAtStart;

        speed01Smoothed = 0f;
        currentColor = colorMode == ColorMode.Fixed ? themeColor : speedColors.Evaluate(0f);
        ApplyTheme();
        ApplyHeadlight();
    }

    void Update()
    {
        if (playerControls && ToggleHeadlightPressed()) { headlightOn = !headlightOn; ApplyHeadlight(); }

        // ── 테마 색 (속도에 따라 부드럽게) ──
        float k = 1f - Mathf.Exp(-colorSmooth * Time.deltaTime);
        float target01 = Mathf.Clamp01(Mathf.Abs(CurrentSpeed()) / colorMaxSpeed);
        speed01Smoothed = Mathf.Lerp(speed01Smoothed, target01, k);
        Color targetColor = colorMode == ColorMode.Speed ? speedColors.Evaluate(speed01Smoothed) : themeColor;
        currentColor = Color.Lerp(currentColor, targetColor, k);
        ApplyTheme();

        // ── 브레이크등 ──
        bool braking = playerControls && drive != null && (brakeOnSpaceOnly ? drive.IsHandBraking : drive.IsBraking);
        brake01 = Mathf.MoveTowards(brake01, braking ? 1f : 0f, fadeSpeed * Time.deltaTime);

        Color brakeOn = tailFollowsTheme ? currentColor * brakeEmission.maxColorComponent : brakeEmission;
        if (brakeMat != null) brakeMat.SetColor(EmissionColorId, Color.Lerp(tailEmission, brakeOn, brake01));
        if (brakeLight != null)
        {
            brakeLight.intensity = Mathf.Lerp(tailLightIntensity, brakeLightIntensity, brake01);
            if (tailFollowsTheme) brakeLight.color = currentColor;
        }
    }

    float CurrentSpeed()
    {
        if (wheelSpin != null) return wheelSpin.CurrentSpeed;
        return drive != null ? drive.Speed : 0f;
    }

    /// <summary>림 + 전조등 색 적용</summary>
    void ApplyTheme()
    {
        float intensity = Mathf.Lerp(rimIntensity.x, rimIntensity.y, colorMode == ColorMode.Speed ? speed01Smoothed : 0f);
        Color rim = currentColor * intensity;
        foreach (var r in rimRenderers)
        {
            if (r == null) continue;
            for (int i = 0; i < r.sharedMaterials.Length; i++)
            {
                r.GetPropertyBlock(mpb, i);
                mpb.SetColor(EmissionColorId, rim);
                r.SetPropertyBlock(mpb, i);
            }
        }

        if (headlightOn)
        {
            if (headMat != null) headMat.SetColor(EmissionColorId, currentColor * headlightIntensity);
            if (headlightLight != null) headlightLight.color = currentColor;
        }
    }

    void ApplyHeadlight()
    {
        if (headMat != null) headMat.SetColor(EmissionColorId, headlightOn ? currentColor * headlightIntensity : Color.black);
        if (headlightLight != null)
        {
            headlightLight.enabled = headlightOn;
            headlightLight.color = currentColor;
        }
    }

    /// <summary>외부에서 테마 색 지정 (Fixed 모드로 전환)</summary>
    public void SetThemeColor(Color c)
    {
        colorMode = ColorMode.Fixed;
        themeColor = c;
    }

    bool ToggleHeadlightPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[headlightToggleKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Z);
#endif
    }

    void OnDestroy()
    {
        if (headMat != null) Destroy(headMat);
        if (brakeMat != null) Destroy(brakeMat);
    }
}
