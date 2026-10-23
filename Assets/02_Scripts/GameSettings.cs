using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 게임 설정 (볼륨 / 마우스 감도 / 전체화면) 저장·불러오기·적용.
/// 로비와 게임 씬 어디서나 같은 값을 사용. 값은 PlayerPrefs에 저장되어 다음 실행에도 유지.
///  - Master : AudioListener.volume (믹서 없이도 동작)
///  - Music / SFX : Resources/MainMixer 의 노출 파라미터 "MusicVolume", "SFXVolume"
///    (믹서가 없으면 Music/SFX 슬라이더는 효과 없음)
///  - 마우스 감도 : MouseSensitivityApplier 가 SensitivityMultiplier 를 읽어 적용 (50 = 기본 감도)
///    내부 값은 5~100 (5 단위), 화면에는 배율(값/50)로 표시: 50 = 1.0, 55 = 1.1, 5 = 0.1, 100 = 2.0
///    [ ] 키로 한 단계(5 = 0.1배)씩 조절 (MouseSensitivityApplier)
/// </summary>
public static class GameSettings
{
    public const float DefaultMaster = 80f;
    public const float DefaultMusic = 60f;
    public const float DefaultSfx = 90f;
    public const float DefaultSensitivity = 50f;
    public const bool DefaultFullscreen = true;
    public const float MinSensitivity = 5f;      // 0.1배
    public const float MaxSensitivity = 100f;    // 2.0배
    public const float SensitivityStep = 5f;     // 0.1배

    const string KeyMaster = "set_master";
    const string KeyMusic = "set_music";
    const string KeySfx = "set_sfx";
    const string KeySens = "set_sens";
    const string KeyFull = "set_fullscreen";

    const string MixerPath = "MainMixer";        // Assets/Resources/MainMixer.mixer
    const string MusicParam = "MusicVolume";
    const string SfxParam = "SFXVolume";

    public static float Master { get; private set; } = DefaultMaster;
    public static float Music { get; private set; } = DefaultMusic;
    public static float Sfx { get; private set; } = DefaultSfx;
    public static float Sensitivity { get; private set; } = DefaultSensitivity;
    public static bool Fullscreen { get; private set; } = DefaultFullscreen;

    /// <summary>50 = 1배. 마우스 회전 속도에 곱하는 값</summary>
    public static float SensitivityMultiplier => Mathf.Max(0.02f, Sensitivity / 50f);

    /// <summary>화면 표시용 배율 (예: "1.0")</summary>
    public static string SensitivityDisplay => ToSensitivityDisplay(Sensitivity);

    /// <summary>내부 값(5~100) → 표시 배율 문자열</summary>
    public static string ToSensitivityDisplay(float value) => (value / 50f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>5 단위로 맞춘 감도 값 (슬라이더 스냅용)</summary>
    public static float SnapSensitivity(float v) =>
        Mathf.Clamp(Mathf.Round(v / SensitivityStep) * SensitivityStep, MinSensitivity, MaxSensitivity);

    /// <summary>값이 바뀔 때마다 호출 (UI 갱신, 감도 적용 등)</summary>
    public static event Action Changed;

    private static AudioMixer mixer;
    private static bool mixerSearched;
    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        Load();
        ApplyAudio();
        ApplyFullscreen();
    }

    public static void Load()
    {
        Master = PlayerPrefs.GetFloat(KeyMaster, DefaultMaster);
        Music = PlayerPrefs.GetFloat(KeyMusic, DefaultMusic);
        Sfx = PlayerPrefs.GetFloat(KeySfx, DefaultSfx);
        Sensitivity = SnapSensitivity(PlayerPrefs.GetFloat(KeySens, DefaultSensitivity));
        Fullscreen = PlayerPrefs.GetInt(KeyFull, DefaultFullscreen ? 1 : 0) == 1;
        loaded = true;
    }

    private static void EnsureLoaded()
    {
        if (!loaded) Load();
    }

    // ───────── 값 변경 ─────────

    public static void SetMaster(float v) { EnsureLoaded(); Master = Mathf.Clamp(v, 0f, 100f); PlayerPrefs.SetFloat(KeyMaster, Master); ApplyAudio(); Notify(); }
    public static void SetMusic(float v) { EnsureLoaded(); Music = Mathf.Clamp(v, 0f, 100f); PlayerPrefs.SetFloat(KeyMusic, Music); ApplyAudio(); Notify(); }
    public static void SetSfx(float v) { EnsureLoaded(); Sfx = Mathf.Clamp(v, 0f, 100f); PlayerPrefs.SetFloat(KeySfx, Sfx); ApplyAudio(); Notify(); }
    public static void SetSensitivity(float v)
    {
        EnsureLoaded();
        float nv = SnapSensitivity(v);
        if (Mathf.Approximately(nv, Sensitivity)) return;
        Sensitivity = nv;
        PlayerPrefs.SetFloat(KeySens, Sensitivity);
        Notify();
    }

    /// <summary>감도를 한 단계(0.1배) 올리거나 내림. dir: +1 / -1</summary>
    public static void StepSensitivity(int dir)
    {
        EnsureLoaded();
        SetSensitivity(SnapSensitivity(Sensitivity) + Mathf.Sign(dir) * SensitivityStep);
    }

    public static void SetFullscreen(bool on)
    {
        EnsureLoaded();
        Fullscreen = on;
        PlayerPrefs.SetInt(KeyFull, on ? 1 : 0);
        ApplyFullscreen();
        Notify();
    }

    public static void ResetDefaults()
    {
        Master = DefaultMaster;
        Music = DefaultMusic;
        Sfx = DefaultSfx;
        Sensitivity = DefaultSensitivity;
        Fullscreen = DefaultFullscreen;
        PlayerPrefs.SetFloat(KeyMaster, Master);
        PlayerPrefs.SetFloat(KeyMusic, Music);
        PlayerPrefs.SetFloat(KeySfx, Sfx);
        PlayerPrefs.SetFloat(KeySens, Sensitivity);
        PlayerPrefs.SetInt(KeyFull, Fullscreen ? 1 : 0);
        ApplyAudio();
        ApplyFullscreen();
        Notify();
    }

    private static void Notify()
    {
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    // ───────── 적용 ─────────

    public static void ApplyAudio()
    {
        EnsureLoaded();
        AudioListener.volume = Master / 100f;

        if (!mixerSearched)
        {
            mixer = Resources.Load<AudioMixer>(MixerPath);
            mixerSearched = true;
            if (mixer == null)
                Debug.LogWarning("[GameSettings] Assets/Resources/MainMixer 가 없어 Music/SFX 볼륨은 적용되지 않습니다.");
        }
        if (mixer != null)
        {
            mixer.SetFloat(MusicParam, ToDecibel(Music));
            mixer.SetFloat(SfxParam, ToDecibel(Sfx));
        }
    }

    public static void ApplyFullscreen()
    {
#if !UNITY_EDITOR
        if (Fullscreen)
        {
            var res = Screen.currentResolution;
            Screen.SetResolution(res.width, res.height, FullScreenMode.FullScreenWindow);
        }
        else
        {
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
        }
#endif
    }

    /// <summary>0~100 → 데시벨 (-80 ~ 0)</summary>
    private static float ToDecibel(float value)
    {
        if (value <= 0.01f) return -80f;
        return Mathf.Log10(value / 100f) * 20f;
    }
}
