using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 전조등(Z키 토글) + 브레이크등(스페이스를 누르는 동안만 켜짐, Brake On Space Only 끄면 S 감속 때도 켜짐)
/// 발광 렌더러는 머티리얼 Emission을 바꾸고, Light 컴포넌트는 켜고 끈다.
/// 루트(Motorcycle)에 붙인다.
/// </summary>
public class MotorcycleLights : MonoBehaviour
{
    [SerializeField] MotorcycleTestDrive drive;

    [Header("Headlight")]
    [SerializeField] Renderer headlightRenderer;        // 발광할 메시 (예: Headlight_LED)
    [SerializeField] Light headlightLight;              // Spot Light (선택)
    [SerializeField, ColorUsage(false, true)] Color headlightEmission = new Color(1f, 0.97f, 0.9f) * 6f;
    [SerializeField] bool headlightOnAtStart = true;
#if ENABLE_INPUT_SYSTEM
    [Tooltip("전조등 켜기/끄기 키")]
    [SerializeField] Key headlightToggleKey = Key.Z;
#endif

    [Header("Brake / Tail light")]
    [SerializeField] Renderer brakeRenderer;            // 브레이크등 메시
    [SerializeField] Light brakeLight;                  // 빨간 Point Light (선택)
    [SerializeField, ColorUsage(false, true)] Color tailEmission = new Color(1f, 0.05f, 0.03f) * 1.5f; // 미등 (평소)
    [SerializeField, ColorUsage(false, true)] Color brakeEmission = new Color(1f, 0.05f, 0.03f) * 8f;   // 브레이크 시
    [SerializeField] float tailLightIntensity = 0.3f;
    [SerializeField] float brakeLightIntensity = 2f;
    [SerializeField] float fadeSpeed = 15f;
    [Tooltip("켜면 스페이스(핸드 브레이크)를 누를 때만 켜짐. 끄면 S로 감속할 때도 켜짐")]
    [SerializeField] bool brakeOnSpaceOnly = true;

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    Material headMat, brakeMat;
    bool headlightOn;
    float brake01;

    void Awake()
    {
        if (drive == null) drive = GetComponent<MotorcycleTestDrive>();
        if (headlightRenderer != null) { headMat = headlightRenderer.material; headMat.EnableKeyword("_EMISSION"); }
        if (brakeRenderer != null) { brakeMat = brakeRenderer.material; brakeMat.EnableKeyword("_EMISSION"); }
        headlightOn = headlightOnAtStart;
        ApplyHeadlight();
    }

    void Update()
    {
        if (ToggleHeadlightPressed()) { headlightOn = !headlightOn; ApplyHeadlight(); }

        bool braking = drive != null && (brakeOnSpaceOnly ? drive.IsHandBraking : drive.IsBraking);
        brake01 = Mathf.MoveTowards(brake01, braking ? 1f : 0f, fadeSpeed * Time.deltaTime);

        if (brakeMat != null) brakeMat.SetColor(EmissionColorId, Color.Lerp(tailEmission, brakeEmission, brake01));
        if (brakeLight != null) brakeLight.intensity = Mathf.Lerp(tailLightIntensity, brakeLightIntensity, brake01);
    }

    void ApplyHeadlight()
    {
        if (headMat != null) headMat.SetColor(EmissionColorId, headlightOn ? headlightEmission : Color.black);
        if (headlightLight != null) headlightLight.enabled = headlightOn;
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