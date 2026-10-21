using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;

/// <summary>
/// 로비 배경 연출 (한 번만 재생). 로비 씬의 빈 오브젝트에 붙인다. 메뉴 UI는 처음부터 떠 있고 뒤에서 연출만 진행.
///
///  1. 시작: 씬 조명을 Start Brightness(기본 60%)로 낮춘 조금 어두운 상태로 Start Delay 대기
///  2. 번개 Strike Count번: 섬광에 맞춰 조명이 원래 밝기(100%)까지 확 밝아짐 (+ 번개 파티클, 천둥)
///  3. 마지막 번개가 가장 밝은 순간 → 불이 전부 꺼짐 (라이트, 네온, 환경광·반사광, 지정 오브젝트)
///  4. 어둠 속에서 캐릭터(CharacterBody) 얼굴 데칼이 몇 번 반짝인 뒤 켜진 채로 유지
///  5. BLINDSPOT 네온(Neon Objects)이 깜빡이며 다시 켜짐
///
/// 정전 뒤에는 꺼둔 라이트를 매 프레임 계속 꺼둠 (다른 스크립트가 다시 켜지 않게).
/// </summary>
[DisallowMultipleComponent]
public class LobbyIntro : MonoBehaviour
{
    [Header("시작")]
    [Tooltip("씬 시작 후 첫 번개까지 (초)")]
    [SerializeField, Min(0f)] private float startDelay = 2f;
    [Tooltip("시작 상태 밝기 (원래 조명 대비, 1 = 원래 그대로)")]
    [SerializeField, Range(0f, 1f)] private float startBrightness = 0.6f;

    [Header("번개")]
    [Tooltip("번쩍일 Directional Light (평소엔 꺼둠)")]
    [SerializeField] private Light flashLight;
    [SerializeField, Min(0f)] private float flashIntensity = 3f;
    [Tooltip("번개 파티클 (씬의 Lightning_Narrow / Lightning_Wide). 번개마다 번갈아 재생, 위치는 그대로")]
    [SerializeField] private ParticleSystem[] boltEffects;
    [Tooltip("번개 횟수 (마지막 번개 직후 정전)")]
    [SerializeField, Min(1)] private int strikeCount = 2;
    [Tooltip("번개 사이 간격 (최소~최대, 초)")]
    [SerializeField] private Vector2 strikeInterval = new Vector2(0.8f, 1.4f);
    [Tooltip("천둥 (여러 개면 랜덤). 비우면 소리 없음")]
    [SerializeField] private AudioClip[] thunderClips;
    [SerializeField, Range(0f, 1f)] private float thunderVolume = 1f;
    [Tooltip("섬광 후 천둥까지 (최소~최대, 초)")]
    [SerializeField] private Vector2 thunderDelay = new Vector2(0.1f, 0.4f);

    [Header("정전")]
    [Tooltip("마지막 번개가 가장 밝아진 뒤 정전까지 (초)")]
    [SerializeField, Min(0f)] private float blackoutDelay = 0.15f;
    [Tooltip("밝기를 조절하고 정전 때 끌 라이트. 비우면 씬의 모든 라이트 (번개·캐릭터·네온 라이트 제외)")]
    [SerializeField] private Light[] controlledLights;
    [Tooltip("정전 때 끄고 계속 꺼둘 오브젝트 (달, 빛나는 형광등 메시 등)")]
    [SerializeField] private GameObject[] objectsToTurnOff;
    [Tooltip("정전 때 Weight를 0 → 1로 올릴 Volume (예: Color Adjustments Post Exposure -2). 비우면 없음")]
    [SerializeField] private Volume blackoutVolume;
    [SerializeField, Min(0f)] private float blackoutFade = 0.3f;
    [Tooltip("정전 순간 소리 (선택)")]
    [SerializeField] private AudioClip powerOffClip;
    [SerializeField, Range(0f, 1f)] private float powerOffVolume = 0.8f;

    [Header("얼굴 데칼")]
    [Tooltip("정전 후 얼굴 데칼이 켜질 때까지 (초)")]
    [SerializeField, Min(0f)] private float faceDelay = 1.2f;
    [Tooltip("캐릭터(CharacterBody)의 DecalBlink. 비우면 씬에서 자동으로 찾음")]
    [SerializeField] private DecalBlink faceDecal;
    [Tooltip("켜지기 전 반짝이는 횟수")]
    [SerializeField, Min(0)] private int faceFlashCount = 3;
    [SerializeField, Min(0.01f)] private float faceFlashOn = 0.08f;
    [SerializeField, Min(0.01f)] private float faceFlashOff = 0.15f;

    [Header("BLINDSPOT 네온")]
    [Tooltip("정전 때 같이 꺼졌다가 마지막에 다시 켜질 오브젝트 (BlindSpot_NeonSign, NeonPointLight_L/C/R 등)")]
    [SerializeField] private GameObject[] neonObjects;
    [Tooltip("얼굴 데칼이 켜진 뒤 네온이 다시 켜질 때까지 (초)")]
    [SerializeField, Min(0f)] private float neonDelay = 1f;
    [Tooltip("다시 켜질 때 깜빡이는 횟수")]
    [SerializeField, Min(0)] private int neonFlickers = 3;

    private AudioSource sfx;
    private readonly List<Light> lights = new List<Light>();
    private readonly List<float> lightBase = new List<float>();
    private bool blackedOut;

    // 원래 환경광
    private float ambientIntensity, reflectionIntensity;
    private Color ambientLight, ambientSky, ambientEquator, ambientGround;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        if (faceDecal == null) faceDecal = FindAnyObjectByType<DecalBlink>();
        if (faceDecal == null) Debug.LogWarning("[LobbyIntro] 씬에 DecalBlink(CharacterBody)가 없어 얼굴 데칼을 켜지 않습니다.", this);

        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;
        var mixer = Resources.Load<AudioMixer>("MainMixer");
        if (mixer != null)
        {
            var groups = mixer.FindMatchingGroups("SFX");
            if (groups != null && groups.Length > 0) sfx.outputAudioMixerGroup = groups[0];
        }

        if (flashLight != null) { flashLight.intensity = 0f; flashLight.enabled = false; }
        if (blackoutVolume != null) blackoutVolume.weight = 0f;
        if (boltEffects != null)
            foreach (var b in boltEffects)
            {
                if (b == null) continue;
                var main = b.main;
                main.loop = false;
                main.playOnAwake = false;
                b.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

        CollectLights();

        ambientIntensity = RenderSettings.ambientIntensity;
        reflectionIntensity = RenderSettings.reflectionIntensity;
        ambientLight = RenderSettings.ambientLight;
        ambientSky = RenderSettings.ambientSkyColor;
        ambientEquator = RenderSettings.ambientEquatorColor;
        ambientGround = RenderSettings.ambientGroundColor;

        SetBrightness(startBrightness);
    }

    private void CollectLights()
    {
        lights.Clear();
        lightBase.Clear();
        if (controlledLights != null && controlledLights.Length > 0)
        {
            foreach (var l in controlledLights) if (l != null) Add(l);
            return;
        }
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l == null || l == flashLight) continue;
            if (faceDecal != null && l.transform.IsChildOf(faceDecal.transform)) continue;   // 캐릭터 데칼 라이트
            if (UnderAny(l.transform, neonObjects)) continue;                                // 네온은 따로
            Add(l);
        }

        void Add(Light l)
        {
            lights.Add(l);
            lightBase.Add(l.intensity);
        }
    }

    private static bool UnderAny(Transform t, GameObject[] roots)
    {
        if (roots == null) return false;
        foreach (var r in roots) if (r != null && t.IsChildOf(r.transform)) return true;
        return false;
    }

    private void Start() => StartCoroutine(Run());

    private void LateUpdate()
    {
        // 정전 뒤: 다른 스크립트가 다시 켜도 계속 꺼둠
        if (!blackedOut) return;
        foreach (var l in lights) if (l != null && l.enabled) l.enabled = false;
    }

    /// <summary>씬 조명 밝기 (원래 대비 비율)</summary>
    private void SetBrightness(float k)
    {
        for (int i = 0; i < lights.Count; i++)
            if (lights[i] != null) lights[i].intensity = lightBase[i] * k;

        RenderSettings.ambientIntensity = ambientIntensity * k;
        RenderSettings.reflectionIntensity = reflectionIntensity * k;
        RenderSettings.ambientLight = ambientLight * k;
        RenderSettings.ambientSkyColor = ambientSky * k;
        RenderSettings.ambientEquatorColor = ambientEquator * k;
        RenderSettings.ambientGroundColor = ambientGround * k;
    }

    // ───────── 연출 ─────────

    private IEnumerator Run()
    {
        yield return new WaitForSeconds(startDelay);

        for (int i = 0; i < strikeCount; i++)
        {
            bool last = i == strikeCount - 1;
            yield return Strike(i, last);
            if (!last) yield return new WaitForSeconds(Random.Range(strikeInterval.x, strikeInterval.y));
        }

        Blackout();
        if (blackoutVolume != null)
        {
            float t = 0f;
            while (t < blackoutFade)
            {
                t += Time.deltaTime;
                blackoutVolume.weight = Mathf.Clamp01(t / blackoutFade);
                yield return null;
            }
            blackoutVolume.weight = 1f;
        }

        yield return new WaitForSeconds(faceDelay);
        yield return FaceOn();

        yield return new WaitForSeconds(neonDelay);
        yield return NeonOn();
    }

    /// <summary>번개 1번. 섬광에 맞춰 조명이 시작 밝기 → 원래 밝기로. last면 가장 밝은 순간에서 멈춤 (바로 정전)</summary>
    private IEnumerator Strike(int index, bool last)
    {
        if (boltEffects != null && boltEffects.Length > 0)
        {
            var bolt = boltEffects[index % boltEffects.Length];
            if (bolt != null) { bolt.Clear(true); bolt.Play(true); }
        }
        if (thunderClips != null && thunderClips.Length > 0)
            StartCoroutine(Thunder(Random.Range(thunderDelay.x, thunderDelay.y), thunderClips[Random.Range(0, thunderClips.Length)]));

        // 번쩍 - 잠깐 꺼짐 - 더 크게 번쩍
        Flash(Random.Range(0.7f, 0.9f));
        yield return new WaitForSeconds(Random.Range(0.05f, 0.09f));
        Flash(0f);
        yield return new WaitForSeconds(Random.Range(0.04f, 0.08f));
        Flash(1f);
        yield return new WaitForSeconds(Random.Range(0.1f, 0.15f));

        if (last)
        {
            // 가장 밝은 상태(원래 조명 그대로)로 잠깐 → 정전
            yield return new WaitForSeconds(blackoutDelay);
            yield break;
        }

        // 서서히 원래(시작 밝기)로
        float fade = Random.Range(0.3f, 0.45f), t = 0f;
        while (t < fade)
        {
            t += Time.deltaTime;
            Flash(1f - Mathf.Clamp01(t / fade));
            yield return null;
        }
        Flash(0f);
    }

    /// <summary>섬광 세기 k (0~1): 번개 라이트 + 씬 조명을 시작 밝기 ~ 원래 밝기 사이로</summary>
    private void Flash(float k)
    {
        if (flashLight != null)
        {
            flashLight.enabled = k > 0.001f;
            flashLight.intensity = flashIntensity * k;
        }
        SetBrightness(Mathf.Lerp(startBrightness, 1f, k));
    }

    private IEnumerator Thunder(float delay, AudioClip clip)
    {
        yield return new WaitForSeconds(delay);
        if (clip != null) sfx.PlayOneShot(clip, thunderVolume);
    }

    private void Blackout()
    {
        if (flashLight != null) { flashLight.intensity = 0f; flashLight.enabled = false; }
        foreach (var l in lights) if (l != null) l.enabled = false;
        SetBrightness(0f);

        if (objectsToTurnOff != null)
            foreach (var go in objectsToTurnOff) if (go != null) go.SetActive(false);
        SetNeon(false);

        if (powerOffClip != null) sfx.PlayOneShot(powerOffClip, powerOffVolume);
        blackedOut = true;
    }

    private IEnumerator FaceOn()
    {
        if (faceDecal == null) yield break;
        if (faceFlashCount > 0)
        {
            faceDecal.ForceFlash(faceFlashCount, faceFlashOn, faceFlashOff);
            yield return new WaitForSeconds(faceFlashCount * (faceFlashOn + faceFlashOff));
        }
        faceDecal.ForceGlow(float.MaxValue);   // 켜진 채로 유지
    }

    private IEnumerator NeonOn()
    {
        for (int i = 0; i < neonFlickers; i++)
        {
            SetNeon(true);
            yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
            SetNeon(false);
            yield return new WaitForSeconds(Random.Range(0.06f, 0.2f));
        }
        SetNeon(true);
    }

    private void SetNeon(bool on)
    {
        if (neonObjects == null) return;
        foreach (var go in neonObjects) if (go != null) go.SetActive(on);
    }
}
