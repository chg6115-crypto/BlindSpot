using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬 전체 데칼 점멸 타이밍을 하나로 관리. 등록된 모든 DecalBlink가 동시에 점멸한다.
/// 씬에 하나만 배치.
/// </summary>
[DefaultExecutionOrder(-100)]
public class DecalBlinkManager : MonoBehaviour
{
    

    public static DecalBlinkManager Instance { get; private set; }

    [Header("점멸 간격 (초)")]
    [SerializeField] private Vector2 intervalRange = new Vector2(5f, 10f);

    [Header("한 번 빛날 때의 모양")]
    [SerializeField, Min(0.05f)] private float flashDuration = 0.6f;
    [SerializeField] private AnimationCurve flashCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.08f, 1f),
        new Keyframe(0.2f, 0.1f),
        new Keyframe(0.32f, 1f),
        new Keyframe(0.75f, 0.85f),
        new Keyframe(1f, 0f));

    private readonly List<DecalBlink> members = new List<DecalBlink>();

    /// <summary>현재 공용 밝기 (0~1)</summary>
    public float CurrentGlow { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable() => StartCoroutine(BlinkLoop());

    public void Register(DecalBlink b)   { if (!members.Contains(b)) members.Add(b); }
    public void Unregister(DecalBlink b) { members.Remove(b); }

    private IEnumerator BlinkLoop()
    {
        float min = Mathf.Max(0.1f, Mathf.Min(intervalRange.x, intervalRange.y));
        float max = Mathf.Max(min, Mathf.Max(intervalRange.x, intervalRange.y));

        while (true)
        {
            Broadcast(0f);
            yield return new WaitForSeconds(Random.Range(min, max));

            float t = 0f;
            while (t < flashDuration)
            {
                Broadcast(flashCurve.Evaluate(t / flashDuration));
                t += Time.deltaTime;
                yield return null;
            }
        }
    }

    private void Broadcast(float value)
    {
        CurrentGlow = value;
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i] == null) { members.RemoveAt(i); continue; }
            members[i].ApplyGlow(value);
        }
    }
}