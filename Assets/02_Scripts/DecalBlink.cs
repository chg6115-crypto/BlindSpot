using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 캐릭터(플레이어/마네킹)의 데칼 표시 담당.
///  - 정기 점멸 : DecalBlinkManager가 모든 캐릭터에 같은 값을 보냄 (5~10초 간격)
///  - ForceFlash : 이 캐릭터만 "틱틱틱" 짧게 깜빡임 (트랩 등, 정기 점멸과 별개)
///  - ForceGlow  : 이 캐릭터만 일정 시간 계속 켜둠
/// </summary>
public class DecalBlink : MonoBehaviour
{
    public enum BlinkMode { Fade, Toggle }

    [Header("데칼 (얼굴, 등)")]
    [SerializeField] private DecalProjector[] decals;

    [Header("표시 방식")]
    [SerializeField] private BlinkMode mode = BlinkMode.Toggle;
    [SerializeField, Range(0f, 1f)] private float toggleThreshold = 0.5f;

    private float sharedValue;          // 매니저가 보낸 정기 점멸 값
    private bool overriding;            // ForceFlash / ForceGlow 진행 중
    private Coroutine overrideRoutine;

    public bool IsGlowing { get; private set; }

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
        IsGlowing = value > 0.001f;
        if (decals == null) return;

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
                d.fadeFactor = 1f;
                d.enabled = value > toggleThreshold;
            }
        }
    }
}