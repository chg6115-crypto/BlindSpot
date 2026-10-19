using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 맵 정전 (번개 등). 정적 클래스라 씬에 따로 둘 것 없음.
///  PowerOutage.Trigger(5f) → 5초 동안 정전, 끝나면 형광등처럼 깜빡이며 복귀
///
///  꺼지는 것
///   - 비상구 표시등 : ExitSignBlackout.ForceOffAll (자체 깜빡임 복귀)
///   - 형광등 등     : PowerGridFixture가 붙은 기구 (Light + 발광 머티리얼)
///   - 네온사인      : NeonSignController / NeonLightFlicker 가 IsOut을 확인
///  트랩·자동차 경보·소나·번개 섬광은 영향 없음.
/// </summary>
public static class PowerOutage
{
    /// <summary>지금 정전(또는 복귀 깜빡임 중 꺼진 순간)인지</summary>
    public static bool IsOut { get; private set; }

    /// <summary>IsOut이 바뀔 때마다 (true = 꺼짐)</summary>
    public static event Action<bool> Changed;

    private static PowerOutageRunner runner;

    /// <summary>duration초 동안 정전. 진행 중이면 남은 시간을 늘림</summary>
    public static void Trigger(float duration)
    {
        if (duration <= 0f) return;
        if (runner == null)
        {
            var go = new GameObject("[PowerOutage]");
            go.hideFlags = HideFlags.HideInHierarchy;
            runner = go.AddComponent<PowerOutageRunner>();
        }
        ExitSignBlackout.ForceOffAll(duration);
        runner.Run(duration);
    }

    internal static void Set(bool off)
    {
        if (IsOut == off) return;
        IsOut = off;
        Changed?.Invoke(off);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOut = false;
        Changed = null;
        runner = null;
    }
}

/// <summary>PowerOutage 타이머 (자동 생성, 씬 전환 시 함께 사라짐)</summary>
internal class PowerOutageRunner : MonoBehaviour
{
    private const int ReturnFlickers = 3;
    private float endTime;
    private Coroutine routine;

    public void Run(float duration)
    {
        endTime = Mathf.Max(endTime, Time.time + duration);
        if (routine == null) routine = StartCoroutine(Routine());
    }

    private IEnumerator Routine()
    {
        PowerOutage.Set(true);
        while (Time.time < endTime) yield return null;

        // 형광등처럼 깜빡이며 복귀
        for (int i = 0; i < ReturnFlickers; i++)
        {
            PowerOutage.Set(false);
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.04f, 0.09f));
            PowerOutage.Set(true);
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.08f, 0.18f));
            if (Time.time < endTime) { i = -1; while (Time.time < endTime) yield return null; }   // 도중에 연장됨
        }
        PowerOutage.Set(false);
        routine = null;
    }

    private void OnDestroy()
    {
        PowerOutage.Set(false);
    }
}
