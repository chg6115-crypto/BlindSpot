using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 자동차 경보 트랩. 차 모델(Sport Car) 루트에 붙인다.
///
///  [발동]  ① 플레이어가 차에서 Trigger Range(0.5m) 안으로 다가가면
///          ② 총알이 차에 맞으면 (내가 쏜 것 + 다른 사람이 쏜 것 모두)
///  [경보]  Alarm Duration 동안 비상등처럼 깜빡임 + 경보음 + 소리 파동(외곽선)
///          - 차의 라이트 머티리얼(front / rear / back)을 발광 (원본 머티리얼은 그대로, 차마다 따로)
///          - 실제 조명: 직접 배치한 Light들을 켰다 껐다 (Alarm Lights. 비우면 차 아래 자식 Light 자동 사용)
///            각 Light의 색·밝기·범위·위치는 Light에서 직접 설정. RevealLight가 없으면 자동으로 붙여 빛 안 캐릭터가 보임
///  [쿨타임] 발동 시점부터 Cooldown이 지나면 다시 작동
///
/// 멀티: 위치·총알 착탄 지점이 이미 동기화되므로 각 화면에서 같은 조건으로 판정 (네트워크 메시지 없음).
/// 충돌체가 없으면 차체 메시 크기로 Box Collider를 자동으로 붙임 (총알이 맞고, 지나가지 못하게).
/// 깜빡임 기본값은 실제 자동차 비상등(분당 약 90회)에 가깝게.
/// </summary>
[DisallowMultipleComponent]
public class CarAlarm : MonoBehaviour
{
    [Header("발동")]
    [Tooltip("차체에서 이 거리(m) 안으로 플레이어가 들어오면 발동")]
    [SerializeField, Min(0f)] private float triggerRange = 0.5f;
    [Tooltip("총알이 차에 맞으면 발동")]
    [SerializeField] private bool triggerOnShot = true;
    [Tooltip("발동 시점부터 다시 작동하기까지 (초)")]
    [SerializeField, Min(0f)] private float cooldown = 10f;

    [Header("경보")]
    [Tooltip("경보가 울리는 시간 (초)")]
    [SerializeField, Min(0.1f)] private float alarmDuration = 5f;
    [Tooltip("라이트가 켜져 있는 시간 (초)")]
    [SerializeField, Min(0.05f)] private float flashOnTime = 0.35f;
    [Tooltip("라이트가 꺼져 있는 시간 (초)")]
    [SerializeField, Min(0.05f)] private float flashOffTime = 0.3f;

    [Header("소리")]
    [Tooltip("경보음 (반복 재생). 비우면 소리 없음")]
    [SerializeField] private AudioClip alarmClip;
    [SerializeField, Range(0f, 1f)] private float alarmVolume = 1f;
    [Tooltip("경보음이 들리는 최대 거리 (m)")]
    [SerializeField, Min(1f)] private float alarmHearDistance = 40f;
    [Tooltip("발동 순간 소리 파동(외곽선) 반경 (m). 0이면 없음")]
    [SerializeField, Min(0f)] private float soundPulseRadius = 15f;

    [Header("라이트 머티리얼 발광 (이름에 포함된 글자로 찾음)")]
    [SerializeField] private string frontMaterialKeyword = "front";
    [SerializeField] private string[] rearMaterialKeywords = { "rear", "back" };
    [ColorUsage(false, true)] [SerializeField] private Color frontEmission = new Color(6f, 6f, 5.5f);
    [ColorUsage(false, true)] [SerializeField] private Color rearEmission = new Color(6f, 0.3f, 0.2f);

    [Header("실제 조명 (직접 배치)")]
    [Tooltip("경보 때 깜빡일 Light들. 비우면 이 오브젝트 아래 자식 Light를 전부 사용. 평소에는 꺼져 있음")]
    [SerializeField] private Light[] alarmLights;
    [Tooltip("자동으로 붙이는 RevealLight의 밝기 기준 (Light 밝기가 이 값 이상일 때 캐릭터 노출)")]
    [SerializeField, Min(0f)] private float revealMinIntensity = 0.05f;

    // ───────── 내부 ─────────

    private MeshRenderer body;
    private MeshFilter bodyFilter;
    private Bounds localBounds;           // 차체 메시 크기 (body 기준 로컬)
    private Collider bodyCollider;
    private readonly List<int> frontSlots = new List<int>();
    private readonly List<int> rearSlots = new List<int>();
    private MaterialPropertyBlock mpb;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private AudioSource audioSource;

    private bool alarming;
    private float readyTime;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        FindBody();
        if (body == null)
        {
            Debug.LogWarning("[CarAlarm] 차체 MeshRenderer를 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        EnsureCollider();
        SetupLights();
        CreateAudio();
        SetLit(false);
    }

    private void OnEnable()
    {
        GunWeapon.AnyShotImpact += HandleShotImpact;
    }

    private void OnDisable()
    {
        GunWeapon.AnyShotImpact -= HandleShotImpact;
        StopAllCoroutines();
        alarming = false;
        if (body != null) SetLit(false);
        if (audioSource != null) audioSource.Stop();
    }

    /// <summary>라이트 머티리얼이 들어 있는 MeshRenderer = 차체</summary>
    private void FindBody()
    {
        MeshRenderer best = null;
        int bestCount = -1;
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            int lightSlots = 0;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && IsLightMaterial(mats[i].name)) lightSlots++;
            int score = lightSlots * 100 + mats.Length;
            if (score > bestCount) { bestCount = score; best = r; }
        }
        body = best;
        if (body == null) return;

        bodyFilter = body.GetComponent<MeshFilter>();
        localBounds = bodyFilter != null && bodyFilter.sharedMesh != null
            ? bodyFilter.sharedMesh.bounds
            : new Bounds(body.transform.InverseTransformPoint(body.bounds.center), body.bounds.size);

        var shared = body.sharedMaterials;
        for (int i = 0; i < shared.Length; i++)
        {
            if (shared[i] == null) continue;
            string n = shared[i].name.ToLowerInvariant();
            if (n.Contains(frontMaterialKeyword.ToLowerInvariant())) frontSlots.Add(i);
            else if (MatchesAny(n, rearMaterialKeywords)) rearSlots.Add(i);
        }
        if (frontSlots.Count == 0 && rearSlots.Count == 0)
            Debug.LogWarning("[CarAlarm] 라이트 머티리얼(front/rear/back)을 찾지 못했습니다. 발광 없이 조명만 켜집니다.", this);
    }

    private bool IsLightMaterial(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains(frontMaterialKeyword.ToLowerInvariant()) || MatchesAny(n, rearMaterialKeywords);
    }

    private static bool MatchesAny(string lowerName, string[] keywords)
    {
        foreach (var k in keywords)
            if (!string.IsNullOrEmpty(k) && lowerName.Contains(k.ToLowerInvariant())) return true;
        return false;
    }

    private void EnsureCollider()
    {
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (!c.isTrigger) { bodyCollider = c; return; }

        var box = body.gameObject.AddComponent<BoxCollider>();
        box.center = localBounds.center;
        box.size = localBounds.size;
        bodyCollider = box;
    }

    /// <summary>직접 배치한 Light 준비: 평소엔 끄고, RevealLight가 없으면 붙임</summary>
    private void SetupLights()
    {
        if (alarmLights == null || alarmLights.Length == 0)
            alarmLights = GetComponentsInChildren<Light>(true);

        foreach (var l in alarmLights)
        {
            if (l == null) continue;
            l.enabled = false;
            if (!l.TryGetComponent(out RevealLight reveal))
            {
                reveal = l.gameObject.AddComponent<RevealLight>();   // 빛 안 캐릭터 노출 + 몸도 비춤
                reveal.MinIntensity = revealMinIntensity;
            }
        }
        if (alarmLights.Length == 0)
            Debug.Log("[CarAlarm] 배치된 Light가 없습니다. 머티리얼 발광만 깜빡입니다.", this);
    }

    private void CreateAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 3f;
        audioSource.maxDistance = alarmHearDistance;
        audioSource.volume = alarmVolume;

        var mixer = Resources.Load<AudioMixer>("MainMixer");
        if (mixer != null)
        {
            var groups = mixer.FindMatchingGroups("SFX");
            if (groups != null && groups.Length > 0) audioSource.outputAudioMixerGroup = groups[0];
        }
    }

    // ───────── 발동 감지 ─────────

    private void Update()
    {
        if (alarming || Time.time < readyTime || triggerRange <= 0f) return;

        foreach (var p in PlayerNetworkSetup.All)
        {
            if (p == null || !p.isActiveAndEnabled) continue;
            var h = p.GetComponent<Health>();
            if (h != null && h.IsDead) continue;
            if (IsNearBody(p.transform.position)) { Trigger(); return; }
        }
    }

    /// <summary>발 위치가 차체 박스 + Trigger Range 안인지 (차가 회전해 있어도 정확하게)</summary>
    private bool IsNearBody(Vector3 worldPos)
    {
        Transform bt = body.transform;
        Vector3 local = bt.InverseTransformPoint(worldPos);
        Vector3 scale = bt.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
        float sy = Mathf.Max(0.0001f, Mathf.Abs(scale.y));
        float sz = Mathf.Max(0.0001f, Mathf.Abs(scale.z));

        Vector3 ext = localBounds.extents + new Vector3(triggerRange / sx, triggerRange / sy + 1f / sy, triggerRange / sz);
        Vector3 d = local - localBounds.center;
        return Mathf.Abs(d.x) <= ext.x && Mathf.Abs(d.y) <= ext.y && Mathf.Abs(d.z) <= ext.z;
    }

    private void HandleShotImpact(Vector3[] points)
    {
        if (!triggerOnShot || alarming || Time.time < readyTime || points == null || bodyCollider == null) return;

        foreach (var pt in points)
        {
            Vector3 closest = bodyCollider.ClosestPoint(pt);
            if ((closest - pt).sqrMagnitude < 0.15f * 0.15f) { Trigger(); return; }
        }
    }

    // ───────── 경보 ─────────

    /// <summary>경보 시작 (외부에서 호출해도 됨)</summary>
    public void Trigger()
    {
        if (alarming || Time.time < readyTime) return;
        StartCoroutine(AlarmRoutine());
    }

    private IEnumerator AlarmRoutine()
    {
        alarming = true;
        readyTime = Time.time + cooldown;

        if (soundPulseRadius > 0f)
            SoundPulseManager.Emit(transform.position, soundPulseRadius, gameObject);
        if (audioSource != null && alarmClip != null)
        {
            audioSource.clip = alarmClip;
            audioSource.volume = alarmVolume;
            audioSource.Play();
        }

        float end = Time.time + alarmDuration;
        while (Time.time < end)
        {
            SetLit(true);
            yield return new WaitForSeconds(flashOnTime);
            SetLit(false);
            if (Time.time >= end) break;
            yield return new WaitForSeconds(flashOffTime);
        }

        SetLit(false);
        if (audioSource != null) audioSource.Stop();
        alarming = false;
    }

    private void SetLit(bool on)
    {
        // 머티리얼 발광 (프로퍼티 블록 → 원본 머티리얼은 그대로)
        foreach (int s in frontSlots) SetSlotEmission(s, on ? frontEmission : Color.black);
        foreach (int s in rearSlots) SetSlotEmission(s, on ? rearEmission : Color.black);

        if (alarmLights != null)
            foreach (var l in alarmLights)
                if (l != null) l.enabled = on;
    }

    private void SetSlotEmission(int slot, Color c)
    {
        body.GetPropertyBlock(mpb, slot);
        mpb.SetColor(EmissionId, c);
        body.SetPropertyBlock(mpb, slot);
    }
}
