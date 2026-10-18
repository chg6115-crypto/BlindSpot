using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬 전체 데칼 점멸 타이밍을 하나로 관리. 등록된 모든 DecalBlink가 동시에 점멸한다.
/// 씬에 하나만 배치.
/// 연장전(라운드 제한 시간 초과)에는 점멸 간격이 짧아짐 → 숨어 있는 플레이어가 자주 드러남 (RoundManager가 SetOvertime 호출)
/// 멀티: 호스트만 점멸 시점을 정하고(FlashStarted), RoundNetworkSync가 참가자에게 전달 → 참가자는 PlayFlash()로 동시에 점멸
/// </summary>
[DefaultExecutionOrder(-100)]
public class DecalBlinkManager : MonoBehaviour
{
    

    public static DecalBlinkManager Instance { get; private set; }

    [Header("점멸 간격 (초)")]
    [SerializeField] private Vector2 intervalRange = new Vector2(5f, 10f);
    [Tooltip("연장전 점멸 간격 (초)")]
    [SerializeField] private Vector2 overtimeIntervalRange = new Vector2(1.5f, 3f);

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
    /// <summary>연장전 중인지 (점멸 간격 짧음)</summary>
    public bool IsOvertime { get; private set; }

    /// <summary>연장전 켜기/끄기. 켜는 순간 기다리던 긴 간격도 바로 짧아짐</summary>
    public void SetOvertime(bool on) => IsOvertime = on;

    /// <summary>호스트(또는 싱글)에서 점멸이 시작될 때 → 참가자에게 전달용</summary>
    public static event Action FlashStarted;

    private Coroutine flashRoutine;

    /// <summary>멀티 참가자: 점멸 시점은 호스트가 정함</summary>
    private static bool IsFollower
    {
        get
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm != null && nm.IsListening && !nm.IsServer;
        }
    }

    /// <summary>참가자: 호스트 신호를 받아 한 번 점멸</summary>
    public void PlayFlash()
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashOnce());
    }

    private IEnumerator FlashOnce()
    {
        float t = 0f;
        while (t < flashDuration)
        {
            Broadcast(flashCurve.Evaluate(t / flashDuration));
            t += Time.deltaTime;
            yield return null;
        }
        Broadcast(0f);
        flashRoutine = null;
    }

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

    private float NextInterval()
    {
        Vector2 r = IsOvertime ? overtimeIntervalRange : intervalRange;
        float min = Mathf.Max(0.1f, Mathf.Min(r.x, r.y));
        float max = Mathf.Max(min, Mathf.Max(r.x, r.y));
        return UnityEngine.Random.Range(min, max);
    }

    private IEnumerator BlinkLoop()
    {
        while (true)
        {
            // 참가자는 직접 점멸하지 않음 (호스트 신호 → PlayFlash)
            if (IsFollower)
            {
                yield return null;
                continue;
            }

            Broadcast(0f);

            // 대기 (도중에 연장전이 시작되면 남은 시간을 짧은 간격으로 줄임)
            bool wasOvertime = IsOvertime;
            float wait = NextInterval();
            while (wait > 0f)
            {
                if (IsOvertime && !wasOvertime)
                {
                    wasOvertime = true;
                    wait = Mathf.Min(wait, NextInterval());
                }
                wait -= Time.deltaTime;
                yield return null;
            }
            if (IsFollower) continue;

            FlashStarted?.Invoke();   // 멀티: 참가자에게도 지금 점멸하라고 알림

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