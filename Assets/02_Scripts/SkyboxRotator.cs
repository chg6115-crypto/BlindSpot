using UnityEngine;

/// <summary>
/// 게임 진행 중 Skybox를 천천히 회전시킨다 (구름/하늘이 흐르는 연출).
/// 씬의 아무 오브젝트(예: Environment, RoundManager)에 붙인다.
///  - 원본 Skybox 머티리얼 파일은 건드리지 않도록 실행 중에만 복사본을 만들어 사용
///  - Rotation 속성이 있는 Skybox 셰이더(6 Sided, Cubemap, Panoramic)에서 동작
///  - 연출용이라 멀티에서도 동기화하지 않음 (각자 화면에서 돌아감)
/// </summary>
public class SkyboxRotator : MonoBehaviour
{
    [Tooltip("초당 회전 각도. 0.5~2 정도가 자연스러움")]
    [SerializeField] private float degreesPerSecond = 1f;
    [Tooltip("시작 각도 (0~360)")]
    [SerializeField, Range(0f, 360f)] private float startAngle = 0f;
    [Tooltip("Time.timeScale 영향 받지 않음 (일시정지 중에도 회전)")]
    [SerializeField] private bool useUnscaledTime = false;

    private static readonly int RotationId = Shader.PropertyToID("_Rotation");

    private Material originalSkybox;
    private Material runtimeSkybox;
    private float angle;

    private void OnEnable()
    {
        originalSkybox = RenderSettings.skybox;
        if (originalSkybox == null || !originalSkybox.HasProperty(RotationId))
        {
            Debug.LogWarning("[SkyboxRotator] Skybox가 없거나 Rotation 속성이 없는 셰이더입니다.", this);
            enabled = false;
            return;
        }

        // 원본 파일이 Play 중 바뀌지 않도록 복사본 사용
        runtimeSkybox = new Material(originalSkybox) { name = originalSkybox.name + " (Runtime)" };
        RenderSettings.skybox = runtimeSkybox;

        angle = startAngle;
        runtimeSkybox.SetFloat(RotationId, angle);
    }

    private void Update()
    {
        if (runtimeSkybox == null) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        angle = Mathf.Repeat(angle + degreesPerSecond * dt, 360f);
        runtimeSkybox.SetFloat(RotationId, angle);
    }

    private void OnDisable()
    {
        // 원래 Skybox로 복구
        if (runtimeSkybox != null)
        {
            if (RenderSettings.skybox == runtimeSkybox) RenderSettings.skybox = originalSkybox;
            Destroy(runtimeSkybox);
            runtimeSkybox = null;
        }
    }
}