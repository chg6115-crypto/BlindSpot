using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 캐릭터(플레이어/마네킹)에 붙는 데칼 표시 담당.
/// 점멸 타이밍은 DecalBlinkManager가 결정하고, 여기서는 값을 적용만 한다.
/// </summary>
public class DecalBlink : MonoBehaviour
{
    public enum BlinkMode { Fade, Toggle }

    [Header("데칼 (얼굴, 등)")]
    [SerializeField] private DecalProjector[] decals;

    [Header("표시 방식")]
    [SerializeField] private BlinkMode mode = BlinkMode.Fade;
    [SerializeField, Range(0f, 1f)] private float toggleThreshold = 0.5f;

    private float forcedUntil;
    public bool IsGlowing { get; private set; }

    private void OnEnable()
    {
        SetGlow(0f);
        if (DecalBlinkManager.Instance != null)
            DecalBlinkManager.Instance.Register(this);
        else
            Debug.LogWarning("[DecalBlink] 씬에 DecalBlinkManager가 없습니다.", this);
    }

    private void Start()
    {
        // 매니저가 이 오브젝트보다 늦게 켜진 경우 대비
        if (DecalBlinkManager.Instance != null)
            DecalBlinkManager.Instance.Register(this);
    }

    private void OnDisable()
    {
        if (DecalBlinkManager.Instance != null)
            DecalBlinkManager.Instance.Unregister(this);
        forcedUntil = 0f;
        SetGlow(0f);
    }

    /// <summary>이 캐릭터만 지정 시간 동안 계속 켜둔다 (트랩, 피격 등).</summary>
    public void ForceGlow(float duration)
    {
        forcedUntil = Mathf.Max(forcedUntil, Time.time + duration);
        SetGlow(1f);
    }

    /// <summary>매니저가 매 프레임 호출.</summary>
    public void ApplyGlow(float sharedValue)
    {
        SetGlow(Time.time < forcedUntil ? 1f : sharedValue);
    }

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