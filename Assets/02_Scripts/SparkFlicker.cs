using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 배전반 스파크. 스파크 파티클(ElectricalSparksEffect 등) 오브젝트에 붙인다.
///  - "타닥-타다닥" 묶음으로 번쩍임: 번쩍일 때마다 빛(밝기 랜덤) + 불꽃 파티클 터짐 + 소리
///  - 평소엔 파티클이 계속 나오지 않음 (번쩍일 때만 Emit)
///  - 정전(PowerOutage) 중에는 더 자주 튐
///  - 빛에 RevealLight가 붙어 있어 번쩍이는 순간 빛 안 캐릭터가 보임
///  - Spark Light를 비우면 파티클 앞쪽에 Point Light를 자동으로 만듦 (직접 단 Light를 넣으면 그걸 사용)
///  - 멀티: 서버 시간 + 위치 기반 시드로 모든 화면이 같은 순간에 튐 (네트워크 메시지 없음)
/// </summary>
[DisallowMultipleComponent]
public class SparkFlicker : MonoBehaviour
{
    [Header("대상 (비우면 자동)")]
    [Tooltip("불꽃 파티클. 비우면 이 오브젝트의 ParticleSystem")]
    [SerializeField] private ParticleSystem sparks;
    [Tooltip("번쩍일 빛. 비우면 자동 생성")]
    [SerializeField] private Light sparkLight;

    [Header("자동 생성 빛")]
    [Tooltip("파티클 위치에서 앞쪽(파티클 정면)으로 이만큼 (m)")]
    [SerializeField] private float lightForwardOffset = 0.15f;
    [SerializeField] private Color lightColor = new Color(0.7f, 0.85f, 1f);
    [SerializeField, Min(0.1f)] private float lightRange = 2.5f;
    [Tooltip("번쩍일 때 그림자 (주변 물체 그림자가 튀어서 실감 ↑, 비용 조금)")]
    [SerializeField] private bool lightShadows = true;

    [Header("번쩍임")]
    [Tooltip("번쩍일 때 밝기 (최소~최대 랜덤)")]
    [SerializeField] private Vector2 intensity = new Vector2(3f, 6f);
    [Tooltip("한 번 켜져 있는 시간 (초)")]
    [SerializeField] private Vector2 onTime = new Vector2(0.02f, 0.06f);
    [Tooltip("번쩍임 사이 꺼져 있는 시간 (초)")]
    [SerializeField] private Vector2 offTime = new Vector2(0.03f, 0.1f);
    [Tooltip("번쩍일 때마다 터지는 불꽃 수")]
    [SerializeField] private Vector2Int particlesPerFlash = new Vector2Int(8, 20);
    [Tooltip("이 밝기 이상일 때 빛 안 캐릭터가 보임")]
    [SerializeField, Min(0f)] private float revealMinIntensity = 1f;

    [Header("평소")]
    [Tooltip("묶음 사이 간격 (초)")]
    [SerializeField] private Vector2 burstInterval = new Vector2(2f, 8f);
    [Tooltip("한 묶음 번쩍임 횟수")]
    [SerializeField] private Vector2Int flashesPerBurst = new Vector2Int(2, 5);

    [Header("정전 중")]
    [SerializeField] private Vector2 outageBurstInterval = new Vector2(0.5f, 2f);
    [SerializeField] private Vector2Int outageFlashesPerBurst = new Vector2Int(3, 7);

    [Header("소리 (비우면 없음)")]
    [Tooltip("지직 소리 (여러 개면 랜덤). 번쩍일 때마다")]
    [SerializeField] private AudioClip[] sparkClips;
    [SerializeField, Range(0f, 1f)] private float volume = 0.7f;
    [SerializeField, Min(1f)] private float hearDistance = 15f;

    [Header("동기화")]
    [Tooltip("같은 위치의 스파크끼리 패턴을 다르게 하고 싶을 때 바꿈")]
    [SerializeField] private int seedOffset = 0;

    private const float SlotLength = 0.25f;   // 이 간격마다 "지금 묶음을 시작할지" 판정

    private AudioSource audioSource;
    private uint seed;
    private long lastSlot = long.MinValue;
    private bool bursting;
    private float savedRate = -1f;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        if (sparks == null) sparks = GetComponent<ParticleSystem>();
        if (sparks != null)
        {
            // 계속 나오는 스트림 끄기 (번쩍일 때만 Emit)
            var em = sparks.emission;
            savedRate = em.rateOverTimeMultiplier;
            em.rateOverTimeMultiplier = 0f;
            var main = sparks.main;
            main.loop = true;
            if (!sparks.isPlaying) sparks.Play();
        }

        if (sparkLight == null) sparkLight = CreateLight();
        sparkLight.enabled = false;
        if (!sparkLight.TryGetComponent(out RevealLight reveal)) reveal = sparkLight.gameObject.AddComponent<RevealLight>();
        reveal.MinIntensity = revealMinIntensity;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = hearDistance;
        var mixer = Resources.Load<AudioMixer>("MainMixer");
        if (mixer != null)
        {
            var groups = mixer.FindMatchingGroups("SFX");
            if (groups != null && groups.Length > 0) audioSource.outputAudioMixerGroup = groups[0];
        }

        // 위치 기반 시드 → 모든 화면에서 같은 값
        Vector3 p = transform.position;
        seed = Hash((uint)Mathf.RoundToInt(p.x * 100f), (uint)Mathf.RoundToInt(p.y * 100f), (uint)Mathf.RoundToInt(p.z * 100f), (uint)seedOffset);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        bursting = false;
        if (sparkLight != null) sparkLight.enabled = false;
    }

    private void OnDestroy()
    {
        if (sparks != null && savedRate >= 0f)
        {
            var em = sparks.emission;
            em.rateOverTimeMultiplier = savedRate;
        }
    }

    private Light CreateLight()
    {
        var go = new GameObject("SparkLight");
        go.transform.SetParent(transform, false);
        go.transform.position = transform.position + transform.forward * lightForwardOffset;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = lightColor;
        l.range = lightRange;
        l.shadows = lightShadows ? LightShadows.Hard : LightShadows.None;
        return l;
    }

    // ───────── 스케줄 (모든 화면 동일) ─────────

    private static double SharedTime()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) return nm.ServerTime.Time;
        return Time.timeAsDouble;
    }

    private void Update()
    {
        long slot = (long)System.Math.Floor(SharedTime() / SlotLength);
        if (lastSlot == long.MinValue || slot - lastSlot > 40) lastSlot = slot - 1;   // 처음 / 오래 멈춤 후

        for (long s = lastSlot + 1; s <= slot; s++)
        {
            if (bursting) continue;
            bool outage = PowerOutage.IsOut;
            Vector2 iv = outage ? outageBurstInterval : burstInterval;
            float avg = Mathf.Max(SlotLength, (iv.x + iv.y) * 0.5f);
            float chance = SlotLength / avg;   // 평균 간격이 되도록 슬롯마다 확률
            if (Rand01(seed, (uint)s, 0u) < chance)
            {
                Vector2Int fl = outage ? outageFlashesPerBurst : flashesPerBurst;
                StartCoroutine(Burst((uint)s, fl));
            }
        }
        lastSlot = slot;
    }

    private IEnumerator Burst(uint slot, Vector2Int flashes)
    {
        bursting = true;
        uint k = 1;
        float R(Vector2 r) => Mathf.Lerp(r.x, r.y, Rand01(seed, slot, k++));
        int RI(Vector2Int r) => Mathf.RoundToInt(Mathf.Lerp(r.x, r.y, Rand01(seed, slot, k++)));

        int count = Mathf.Max(1, RI(flashes));
        for (int i = 0; i < count; i++)
        {
            Flash(R(intensity), RI(particlesPerFlash), Rand01(seed, slot, k++));
            yield return new WaitForSeconds(R(onTime));
            sparkLight.enabled = false;
            if (i < count - 1) yield return new WaitForSeconds(R(offTime));
        }

        // 묶음 사이 최소 간격 (다음 판정은 Update의 슬롯 확률로)
        yield return new WaitForSeconds(SlotLength);
        bursting = false;
    }

    private void Flash(float lightIntensity, int particleCount, float pick)
    {
        sparkLight.intensity = lightIntensity;
        sparkLight.enabled = true;
        if (sparks != null && particleCount > 0) sparks.Emit(particleCount);
        if (sparkClips != null && sparkClips.Length > 0 && audioSource != null)
        {
            var clip = sparkClips[Mathf.Clamp((int)(pick * sparkClips.Length), 0, sparkClips.Length - 1)];
            if (clip != null) audioSource.PlayOneShot(clip, volume);
        }
    }

    // ───────── 결정적 난수 ─────────

    private static uint Hash(uint a, uint b, uint c, uint d)
    {
        uint h = 2166136261u;
        h = (h ^ a) * 16777619u; h = (h ^ b) * 16777619u; h = (h ^ c) * 16777619u; h = (h ^ d) * 16777619u;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
        return h;
    }

    private static float Rand01(uint s, uint slot, uint k) => (Hash(s, slot, k, 0x9E3779B9u) & 0xFFFFFF) / (float)0x1000000;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(lightColor.r, lightColor.g, lightColor.b, 0.4f);
        Gizmos.DrawWireSphere(transform.position + transform.forward * lightForwardOffset, lightRange);
    }
}
