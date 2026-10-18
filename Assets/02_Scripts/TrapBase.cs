using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 감지형 트랩 공통.
///  - 플레이어가 범위에 들어오면 그래피티와 라이트가 켜져 주변을 노출
///  - 라이트: Light Hold 동안 유지 → Light Fade 동안 꺼짐
///  - 데칼  : 라이트와 함께 흐려지고, 라이트가 다 꺼진 뒤 Decal Lag만큼 늦게 완전히 꺼짐
///  - 발동 시점부터 Cooldown이 지나면 다시 작동. 범위 안에 계속 서 있으면 재발동
///  - Victim Reveal 옵션으로 걸린 플레이어의 데칼도 드러낼 수 있음 (프리팹마다 선택)
///  - 디버그: TrapBase.DebugShowAll = true 이면 대기 중에도 데칼이 항상 보임 (TrapSpawner 8키)
///
/// 데칼 표시는 머티리얼을 건드리지 않고 Projector 활성화 + Fade Factor로 제어한다.
/// (트랩 머티리얼의 Glow는 1로 고정해 둘 것)
/// </summary>
public class TrapBase : MonoBehaviour
{
    public enum VictimReveal
    {
        None,   // 그래피티와 빛만
        Flash,  // + 플레이어 데칼 "틱틱틱" 깜빡임
        Hold    // + 플레이어 데칼 일정 시간 계속 켜짐
    }

    [Header("구성")]
    [SerializeField] private DecalProjector decal;
    [SerializeField] private Light glowLight;
    [SerializeField] private AudioSource sfx;

    [Header("감지")]
    [SerializeField] private string targetTag = "Player";
    [Tooltip("체크: 한 번 발동 후 사라짐 / 해제: 쿨타임 뒤 다시 작동")]
    [SerializeField] private bool singleUse = false;
    [Tooltip("발동한 순간부터 다시 작동하기까지의 시간 (초). 서 있는 사람은 이 간격으로 반복 노출")]
    [SerializeField, Min(0f)] private float cooldown = 10f;

    [Header("밝기")]
    [Tooltip("발동 전 데칼 불투명도 (0이면 완전히 안 보임)")]
    [SerializeField, Range(0f, 1f)] private float idleGlow = 0f;
    [SerializeField] private float lightIntensity = 4f;

    [Header("타이밍 (초)")]
    [Tooltip("라이트와 데칼이 최대 밝기로 유지되는 시간")]
    [SerializeField, Min(0f)] private float lightHold = 1f;
    [Tooltip("라이트가 꺼지는 데 걸리는 시간 (데칼도 이 동안 함께 흐려짐)")]
    [SerializeField, Min(0f)] private float lightFade = 2f;
    [Tooltip("라이트가 다 꺼진 뒤 데칼이 완전히 꺼지기까지의 시간차")]
    [SerializeField, Range(0f, 0.5f)] private float decalLag = 0.15f;

    [Header("플레이어 데칼 노출")]
    [SerializeField] private VictimReveal victimReveal = VictimReveal.None;
    [Tooltip("Flash: 깜빡이는 횟수")]
    [SerializeField, Min(1)] private int flashCount = 3;
    [Tooltip("Flash: 한 번 켜져 있는 시간")]
    [SerializeField] private float flashOnTime = 0.08f;
    [Tooltip("Flash: 꺼져 있는 시간")]
    [SerializeField] private float flashOffTime = 0.12f;
    [Tooltip("Hold: 계속 켜두는 시간")]
    [SerializeField] private float holdDuration = 1f;

    private bool triggered;                                                   // 발동 중 또는 쿨타임 중
    private readonly HashSet<Transform> occupants = new HashSet<Transform>(); // 범위 안의 대상

    /// <summary>설치자 id (맵 트랩은 -1)</summary>
    public int OwnerId { get; set; } = -1;

    /// <summary>지금 밟으면 발동하는 상태인지</summary>
    public bool IsArmed => !triggered;

    /// <summary>디버그: 모든 트랩 데칼을 항상 보이게</summary>
    public static bool DebugShowAll;

    /// <summary>대기 상태일 때의 데칼 밝기 (디버그 표시 중이면 1)</summary>
    private float IdleValue => DebugShowAll ? 1f : idleGlow;

    /// <summary>디버그 표시 상태가 바뀌었을 때 대기 중인 트랩의 표시를 갱신</summary>
    public void RefreshIdle()
    {
        if (!triggered) SetDecal(IdleValue);
    }

    // ───────── Unity ─────────

    private void OnEnable()
    {
        triggered = false;
        occupants.Clear();
        SetDecal(IdleValue);
        SetLight(0f);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        occupants.Clear();
    }

    // ───────── 감지 ─────────

    private void OnTriggerEnter(Collider other)
    {
        Transform target = GetTarget(other);
        if (target == null) return;

        occupants.Add(target);
        if (!triggered) StartCoroutine(Activate(target));
    }

    private void OnTriggerExit(Collider other)
    {
        Transform target = GetTarget(other);
        if (target != null) occupants.Remove(target);
    }

    private Transform GetTarget(Collider other)
    {
        Transform root = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
        if (root.CompareTag(targetTag)) return root;

        // 멀티: 다른 플레이어가 밟아도 내 화면에서 발동 (위치가 동기화되므로 모든 화면에서 같이 보임)
        return root.GetComponent<PlayerNetworkSetup>() != null ? root : null;
    }

    // ───────── 발동 ─────────

    private IEnumerator Activate(Transform victim)
    {
        triggered = true;
        float startTime = Time.time;

        RevealVictim(victim);
        if (sfx != null) sfx.Play();

        // ① 라이트, 데칼 최대
        SetLight(1f);
        SetDecal(1f);
        if (lightHold > 0f) yield return new WaitForSeconds(lightHold);

        // ② 라이트와 데칼이 함께 흐려짐. 데칼은 decalLag만큼 늦게 완전히 꺼짐
        float lightDur = Mathf.Max(0.0001f, lightFade);
        float decalDur = Mathf.Max(0.0001f, lightFade + decalLag);
        float t = 0f;
        while (t < decalDur)
        {
            SetLight(Mathf.Clamp01(1f - t / lightDur));
            SetDecal(Mathf.Clamp01(1f - t / decalDur));
            t += Time.deltaTime;
            yield return null;
        }
        SetLight(0f);
        SetDecal(0f);

        if (singleUse)
        {
            gameObject.SetActive(false);   // 소멸
            yield break;
        }

        // ③ 발동 시점부터 cooldown이 지날 때까지 대기 (이 동안은 밟아도 반응 없음)
        float remaining = cooldown - (Time.time - startTime);
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        // 다시 작동 가능
        SetDecal(IdleValue);
        triggered = false;

        // 범위 안에 계속 서 있는 사람이 있으면 재발동
        Transform stayer = GetAnyOccupant();
        if (stayer != null) StartCoroutine(Activate(stayer));
    }

    private Transform GetAnyOccupant()
    {
        occupants.RemoveWhere(o => o == null || !o.gameObject.activeInHierarchy);
        foreach (var o in occupants) return o;
        return null;
    }

    private void RevealVictim(Transform victim)
    {
        if (victimReveal == VictimReveal.None || victim == null) return;

        var blink = victim.GetComponentInChildren<DecalBlink>();
        if (blink == null) return;

        if (victimReveal == VictimReveal.Flash)
            blink.ForceFlash(flashCount, flashOnTime, flashOffTime);
        else
            blink.ForceGlow(holdDuration);
    }

    // ───────── 표시 ─────────

    /// <summary>0이면 데칼 비활성화, 0~1은 불투명도(Fade Factor)</summary>
    private void SetDecal(float v)
    {
        if (decal == null) return;

        if (v <= 0.001f)
        {
            decal.enabled = false;
        }
        else
        {
            decal.fadeFactor = v;
            if (!decal.enabled) decal.enabled = true;
        }
    }

    /// <summary>0이면 라이트 꺼짐, 0~1은 lightIntensity 비율</summary>
    private void SetLight(float k)
    {
        if (glowLight == null) return;
        glowLight.enabled = k > 0.001f;
        glowLight.intensity = lightIntensity * k;
    }
}