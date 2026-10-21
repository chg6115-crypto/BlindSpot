using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// anti-camp 천장 전등 하나. AntiCampTrap 오브젝트의 자식(AntiCamper 프리팹)에 붙인다.
///  - Beam Light: 비추는 스포트라이트. 비우면 자식 Spot Light를 쓰고, 없으면 자동으로 만듦
///  - Aim Pivot: 대상 쪽으로 회전할 Transform (전등 머리 등). 비우면 Beam Light만 회전
///    Beam Light가 Aim Pivot의 자식이면 둘 사이 각도를 유지한 채 돌려서 빛이 정확히 대상을 향함
///  - 연출 (AntiCampTrap이 지시)
///      예고: 머리가 대상을 따라 돌면서 짧게 2번 번쩍 → 꺼짐
///      조명: Light Time 동안 대상을 따라가며 비추고, 바닥 원 반경이 시작 반경 → Max Radius로 퍼짐
///            끝날 때 대상이 아직 그 자리(Stay Radius 안)에 있으면 끄지 않고 최대 반경 그대로 이어서 비춤
///            (예고 없이, 반경이 줄지 않음). 움직여서 벗어나면 그 단계가 끝날 때 꺼짐
///  - 밝기는 거리·기울기에 맞춰 자동 보정 (높이·각도가 달라도 바닥에서 같은 밝기). Range는 거리의 2배로 넉넉하게
///  - 전등 자신의 메시는 그림자를 만들지 않음 (머리가 비스듬히 돌 때 몸체·받침이 빛을 가리지 않게)
///  - 비추던 대상이 죽으면 즉시 꺼지고 전등이 비워짐
///  - 렌즈 발광: 평소엔 꺼져 있고, 예고 번쩍임 / 조명 동안만 렌즈 머티리얼이 빛남
///    (렌즈 머티리얼은 Emission 색을 검정으로 둔 것을 사용. 이 전등만 쓰는 복사본을 실행 중에 만들어 바꿈)
/// </summary>
[DisallowMultipleComponent]
public class AntiCampLamp : MonoBehaviour
{
    [Header("연결 (비우면 자동)")]
    [SerializeField] private Light beamLight;
    [SerializeField] private Transform aimPivot;

    [Header("렌즈 발광 (켜질 때만)")]
    [Tooltip("빛날 렌즈 Renderer. 비우면 Aim Pivot의 Renderer")]
    [SerializeField] private Renderer lensRenderer;
    [Tooltip("렌즈 머티리얼 번호 (-1 = 마지막 머티리얼)")]
    [SerializeField] private int lensMaterialIndex = -1;
    [Tooltip("조명이 켜졌을 때 렌즈 발광 색 (HDR, 클수록 블룸이 강함)")]
    [ColorUsage(false, true)] [SerializeField] private Color lensEmission = new Color(4f, 4.4f, 4.8f);

    [Header("소리 (비우면 AntiCampTrap 설정 사용)")]
    [SerializeField] private AudioClip[] crackleClips;

    private AudioSource audioSource;
    private RevealLight reveal;
    private Coroutine routine;
    private Quaternion beamOffset = Quaternion.identity;   // Aim Pivot 기준 Beam Light 회전
    private Material lensMat;
    private Material[] ownedMats;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private float innerRatio = 0.6f;    // 현재 연출 설정 (Run 시작 시)
    private float baseIntensity = 3f;

    /// <summary>지금 예고 중이거나 비추는 중</summary>
    public bool IsBusy => routine != null;
    /// <summary>빛이 나오는 위치 (가림 검사용)</summary>
    public Vector3 Origin => beamLight != null ? beamLight.transform.position : transform.position;
    /// <summary>지금 비추는 대상 (디버그 표시용)</summary>
    public Transform CurrentTarget { get; private set; }
    /// <summary>예고가 끝나고 조명이 켜진 상태인지 (디버그 표시용)</summary>
    public bool IsLit { get; private set; }
    /// <summary>현재 바닥 원 반경 (디버그 표시용)</summary>
    public float CurrentRadius { get; private set; }
    /// <summary>현재 비추는 지점 (디버그 표시용)</summary>
    public Vector3 CurrentSpot { get; private set; }

    private void Awake()
    {
        if (beamLight == null)
        {
            foreach (var l in GetComponentsInChildren<Light>(true))
                if (l.type == LightType.Spot) { beamLight = l; break; }
        }
        if (beamLight == null)
        {
            var go = new GameObject("AntiCampBeam");
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.LookRotation(Vector3.down);
            beamLight = go.AddComponent<Light>();
            beamLight.type = LightType.Spot;
        }
        if (aimPivot == null) aimPivot = beamLight.transform;
        beamOffset = Quaternion.Inverse(aimPivot.rotation) * beamLight.transform.rotation;

        beamLight.enabled = false;
        beamLight.shadowNearPlane = 0.3f;   // 빛 바로 앞(머리 안쪽)의 메시는 그림자에서 제외
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetupLens();
        if (!beamLight.TryGetComponent(out reveal)) reveal = beamLight.gameObject.AddComponent<RevealLight>();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 1f;
        var mixer = Resources.Load<AudioMixer>("MainMixer");
        if (mixer != null)
        {
            var groups = mixer.FindMatchingGroups("SFX");
            if (groups != null && groups.Length > 0) audioSource.outputAudioMixerGroup = groups[0];
        }
    }

    private void OnDisable() => Stop();

    private void OnDestroy()
    {
        if (ownedMats != null) foreach (var m in ownedMats) if (m != null) Destroy(m);
    }

    private void SetupLens()
    {
        if (lensRenderer == null && aimPivot != null) lensRenderer = aimPivot.GetComponent<Renderer>();
        if (lensRenderer == null) return;
        ownedMats = lensRenderer.materials;   // 이 전등 전용 복사본 (다른 전등·원본 머티리얼은 그대로)
        if (ownedMats.Length == 0) return;
        int idx = lensMaterialIndex < 0 ? ownedMats.Length - 1 : Mathf.Min(lensMaterialIndex, ownedMats.Length - 1);
        var m = ownedMats[idx];
        if (m == null || !m.HasProperty(EmissionId))
        {
            Debug.LogWarning($"[AntiCampLamp] 렌즈 머티리얼에 _EmissionColor가 없어 발광을 쓰지 않습니다: {name}", this);
            return;
        }
        m.EnableKeyword("_EMISSION");
        lensMat = m;
        SetLens(0f);
    }

    private void SetLens(float k)
    {
        if (lensMat != null) lensMat.SetColor(EmissionId, lensEmission * k);
    }

    /// <summary>예고 후 대상을 따라가며 비춤. target이 없으면 spot 고정</summary>
    /// <param name="stayRadius">이 반경 안에 계속 있으면 이어서 비춤</param>
    /// <param name="radiusStep">(사용 안 함 - 다음 적발 시작 반경은 AntiCampTrap이 정함)</param>
    /// <param name="onContinue">이어서 비출 때 호출 (호스트가 적발 횟수 기록)</param>
    public void Play(Vector3 spot, Transform target, float startRadius, float stayRadius, float radiusStep,
                     AntiCampTrap.BeamSettings s, AudioClip[] fallbackClips, Action<Transform> onContinue)
    {
        Stop();
        var clips = crackleClips != null && crackleClips.Length > 0 ? crackleClips : fallbackClips;
        routine = StartCoroutine(Run(spot, target, startRadius, stayRadius, radiusStep, s, clips, onContinue));
    }

    public void Stop()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        if (beamLight != null) beamLight.enabled = false;
        if (audioSource != null) audioSource.Stop();
        SetLens(0f);
        CurrentTarget = null;
        IsLit = false;
    }

    private IEnumerator Run(Vector3 spot, Transform target, float startRadius, float stayRadius, float radiusStep,
                            AntiCampTrap.BeamSettings s, AudioClip[] clips, Action<Transform> onContinue)
    {
        innerRatio = s.innerAngleRatio;
        baseIntensity = s.intensity;
        CurrentTarget = target;
        IsLit = false;
        beamLight.color = s.color;
        beamLight.shadows = s.shadows ? LightShadows.Soft : LightShadows.None;
        audioSource.maxDistance = s.hearDistance;
        audioSource.volume = s.volume;
        if (clips != null && clips.Length > 0)
        {
            audioSource.clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            audioSource.loop = true;
            audioSource.Play();
        }

        Quaternion startRot = aimPivot.rotation;

        // ── 예고: 대상을 따라 돌면서 짧게 번쩍 (Flash Count번) → 꺼짐 ──
        float t = 0f;
        float flashCycle = s.flashOnTime + s.flashOffTime;
        while (t < s.warningTime)
        {
            if (IsDead(target)) { Stop(); yield break; }   // 대상이 죽으면 즉시 끄고 전등 비움
            spot = TargetPoint(target, spot);
            float turn = s.turnTime > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / s.turnTime)) : 1f;
            float full = Aim(spot, startRadius, startRot, turn);

            bool on = flashCycle > 0f && t < flashCycle * s.flashCount && (t % flashCycle) < s.flashOnTime;
            beamLight.enabled = on;
            beamLight.intensity = full * s.warningIntensityRatio;
            reveal.MinIntensity = full * 0.7f;   // 예고 번쩍임으로는 노출되지 않음
            SetLens(on ? s.warningIntensityRatio : 0f);

            t += Time.deltaTime;
            yield return null;
        }
        audioSource.loop = false;
        audioSource.Stop();

        // ── 조명: 대상을 따라가며 비추고 반경이 퍼짐. 계속 그 자리에 있으면 끄지 않고 이어서 ──
        beamLight.enabled = true;
        SetLens(1f);
        IsLit = true;
        float maxR = Mathf.Max(s.radius, s.maxRadius);
        float segStart = Mathf.Min(startRadius, maxR);
        Vector3 anchor = TargetPoint(target, spot);
        while (true)
        {
            float endRadius = Mathf.Max(segStart, s.maxRadius);
            t = 0f;
            while (t < s.lightTime)
            {
                if (IsDead(target)) { Stop(); yield break; }
                spot = TargetPoint(target, spot);
                float k = Mathf.Clamp01(t / s.lightTime);
                float radius = Mathf.Lerp(segStart, endRadius, k);
                float full = Aim(spot, radius, startRot, 1f);
                beamLight.intensity = full;
                reveal.MinIntensity = full * 0.7f;

                t += Time.deltaTime;
                yield return null;
            }

            if (!StillCamping(target, anchor, stayRadius)) break;
            anchor = target.position;
            segStart = endRadius;   // 이어서 비출 때는 줄이지 않고 지금 반경(최대) 유지
            onContinue?.Invoke(target);
        }

        beamLight.enabled = false;
        SetLens(0f);
        routine = null;
        CurrentTarget = null;
        IsLit = false;
    }

    private static bool IsDead(Transform target)
        => target != null && target.TryGetComponent(out Health h) && h.IsDead;

    /// <summary>대상이 살아서 기준점 근처에 그대로 있는지</summary>
    private static bool StillCamping(Transform target, Vector3 anchor, float stayRadius)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        if (target.TryGetComponent(out Health h) && h.IsDead) return false;
        return (target.position - anchor).sqrMagnitude <= stayRadius * stayRadius;
    }

    /// <summary>대상 발밑 (대상이 없거나 사라지면 마지막 위치)</summary>
    private static Vector3 TargetPoint(Transform target, Vector3 last)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return last;
        return target.position;
    }

    /// <summary>머리를 지점 쪽으로 돌리고, 바닥 반경에 맞게 원뿔 각도·범위를 맞춤. 보정된 밝기 반환</summary>
    private float Aim(Vector3 spot, float radius, Quaternion startRot, float turn)
    {
        Vector3 from = beamLight.transform.position;
        Vector3 dir = spot - from;
        float dist = Mathf.Max(0.5f, dir.magnitude);

        Vector3 up = beamLight.transform.up;   // 현재 회전 기준 → 바로 아래를 볼 때도 비틀림 없이 이어짐
        Quaternion beamRot = Quaternion.LookRotation(dir / dist, up);
        Quaternion targetRot = beamRot * Quaternion.Inverse(beamOffset);
        aimPivot.rotation = turn >= 1f ? targetRot : Quaternion.Slerp(startRot, targetRot, turn);

        float angle = 2f * Mathf.Atan(radius / dist) * Mathf.Rad2Deg;
        beamLight.spotAngle = Mathf.Clamp(angle, 1f, 179f);
        beamLight.innerSpotAngle = beamLight.spotAngle * innerRatio;

        // Range를 넉넉하게 (끝으로 갈수록 0이 되는 URP 감쇠를 거의 없앰)
        float range = Mathf.Max(dist * 2f, dist + 5f);
        beamLight.range = range;
        CurrentRadius = radius;
        CurrentSpot = spot;

        // 밝기 보정: 거리 제곱 감쇠 + URP Range 감쇠(smooth) + 비스듬히 닿는 만큼(cos)을 되돌려서 바닥 밝기 = Intensity
        float f = (dist * dist) / (range * range);
        float smooth = Mathf.Clamp01(1f - f * f);
        smooth *= smooth;
        float cosIncidence = Mathf.Clamp(-dir.y / dist, 0.35f, 1f);
        return baseIntensity * dist * dist / (Mathf.Max(0.05f, smooth) * cosIncidence);
    }
}
