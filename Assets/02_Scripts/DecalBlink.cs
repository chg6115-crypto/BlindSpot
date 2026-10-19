using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 캐릭터(플레이어/마네킹)의 데칼 표시 담당.
///  - 정기 점멸 : DecalBlinkManager가 모든 캐릭터에 같은 값을 보냄 (5~10초 간격)
///  - ForceFlash : 이 캐릭터만 "틱틱틱" 짧게 깜빡임 (트랩 등, 정기 점멸과 별개)
///  - ForceGlow  : 이 캐릭터만 일정 시간 계속 켜둠
///  - 데칼 라이트 : 데칼과 함께 켜지는 약한 라이트 → 몸 실루엣이 살짝 보임
///                  (라이트 Rendering Layers = Player 로 두면 몸만 비춤)
///  - DebugForceOn : 디버그용, 모든 캐릭터 데칼/라이트 항상 켜기 (DebugMenu 9키)
/// </summary>
public class DecalBlink : MonoBehaviour
{
    public enum BlinkMode { Fade, Toggle }

    /// <summary>디버그: 모든 캐릭터 데칼 항상 켜기 (DebugMenu에서 토글)</summary>
    public static bool DebugForceOn;

    [Header("데칼 (얼굴, 등)")]
    [SerializeField] private DecalProjector[] decals;

    [Header("표시 방식")]
    [SerializeField] private BlinkMode mode = BlinkMode.Toggle;
    [SerializeField, Range(0f, 1f)] private float toggleThreshold = 0.5f;

    [Header("데칼 라이트 (선택)")]
    [Tooltip("데칼 근처에 둔 Point Light들. 비워두면 라이트 없이 동작")]
    [SerializeField] private Light[] glowLights;
    [Tooltip("평소 밝기 (0 = 꺼짐, 아주 작은 값이면 항상 희미한 실루엣)")]
    [SerializeField, Min(0f)] private float lightIdleIntensity = 0f;
    [Tooltip("데칼이 켜졌을 때 밝기")]
    [SerializeField, Min(0f)] private float lightGlowIntensity = 0.3f;

    private float sharedValue;          // 매니저가 보낸 정기 점멸 값
    private float currentValue;         // 현재 표시 중인 값 (디버그 해제 시 복귀용)
    private bool overriding;            // ForceFlash / ForceGlow 진행 중
    private Coroutine overrideRoutine;

    public bool IsGlowing { get; private set; }
    /// <summary>데칼이 실제로 화면에 켜져 있는지 (Toggle 모드는 기준값 이상일 때만). CharacterVisibility가 몸 표시에 사용</summary>
    public bool IsVisibleNow { get; private set; }

    // ───────── 등록 ─────────

    private void OnEnable()
    {
        SetGlow(0f);
        if (DecalBlinkManager.Instance != null)
            DecalBlinkManager.Instance.Register(this);
    }

    private void Start()
    {
        if (DecalBlinkManager.Instance != null)
            DecalBlinkManager.Instance.Register(this);
        else
            Debug.LogWarning("[DecalBlink] 씬에 DecalBlinkManager가 없습니다.", this);
    }

    private void OnDisable()
    {
        if (DecalBlinkManager.Instance != null)
            DecalBlinkManager.Instance.Unregister(this);
        StopOverride();
        SetGlow(0f);
    }

    private void OnValidate()
    {
        if (Application.isPlaying) SetGlow(currentValue);   // Play 중 Inspector 조정 즉시 반영
    }

    // ───────── 정기 점멸 (매니저가 호출) ─────────

    public void ApplyGlow(float value)
    {
        sharedValue = value;
        if (!overriding) SetGlow(value);   // 개별 효과 중에는 무시
    }

    // ───────── 개별 효과 ─────────

    /// <summary>이 캐릭터만 짧게 여러 번 깜빡임 (예: 3회, 켜짐 0.08초 / 꺼짐 0.12초)</summary>
    public void ForceFlash(int count = 3, float onTime = 0.08f, float offTime = 0.12f)
    {
        StartOverride(FlashRoutine(count, onTime, offTime));
    }

    /// <summary>이 캐릭터만 지정 시간 동안 계속 켜둠</summary>
    public void ForceGlow(float duration)
    {
        StartOverride(HoldRoutine(duration));
    }

    /// <summary>디버그 스위치(DebugForceOn) 변경 후 즉시 반영</summary>
    public void RefreshDebug()
    {
        SetGlow(currentValue);
    }

    private IEnumerator FlashRoutine(int count, float onTime, float offTime)
    {
        for (int i = 0; i < count; i++)
        {
            SetGlow(1f);
            yield return new WaitForSeconds(onTime);
            SetGlow(0f);
            yield return new WaitForSeconds(offTime);
        }
        EndOverride();
    }

    private IEnumerator HoldRoutine(float duration)
    {
        SetGlow(1f);
        yield return new WaitForSeconds(duration);
        EndOverride();
    }

    private void StartOverride(IEnumerator routine)
    {
        if (!isActiveAndEnabled) return;
        StopOverride();
        overriding = true;
        overrideRoutine = StartCoroutine(routine);
    }

    private void StopOverride()
    {
        if (overrideRoutine != null) StopCoroutine(overrideRoutine);
        overrideRoutine = null;
        overriding = false;
    }

    private void EndOverride()
    {
        overrideRoutine = null;
        overriding = false;
        SetGlow(sharedValue);   // 정기 점멸 상태로 복귀
    }

    // ───────── 실제 표시 ─────────

    private void SetGlow(float value)
    {
        currentValue = value;
        if (DebugForceOn) value = 1f;   // 디버그: 항상 켜기

        IsGlowing = value > 0.001f;
        IsVisibleNow = mode == BlinkMode.Fade ? IsGlowing : value > toggleThreshold;

        // 데칼
        float shown = value;   // 라이트에도 같은 값 사용
        if (decals != null)
        {
            foreach (var d in decals)
            {
                if (d == null) continue;
                if (mode == BlinkMode.Fade)
                {
                    if (!d.enabled) d.enabled = true;
                    d.fadeFactor = value;
                }
                else
                {
                    bool on = value > toggleThreshold;
                    d.fadeFactor = 1f;
                    d.enabled = on;
                    shown = on ? 1f : 0f;
                }
            }
        }

        // 라이트: 평소 밝기 ~ 켜졌을 때 밝기
        if (glowLights == null) return;
        float intensity = Mathf.Lerp(lightIdleIntensity, lightGlowIntensity, shown);
        foreach (var l in glowLights)
        {
            if (l == null) continue;
            l.intensity = intensity;
            l.enabled = intensity > 0.001f;
        }
    }
}