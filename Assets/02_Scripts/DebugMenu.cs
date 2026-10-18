using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// 모든 디버그 기능을 한 곳에서 관리 (에디터 / Development Build 전용)
///  - 일반 빌드(Development Build 체크 해제)에서는 자동으로 꺼짐
///  - 키패드 숫자로 실행, 키패드 0으로 메뉴 창 열기/닫기 (메뉴의 버튼을 눌러도 실행)
///  - 일반 숫자키 1·2는 무기 전환용으로 비워둠
///  - 각 시스템의 공개 함수를 호출하는 방식
///  - RoundManager가 있으면 킬/데스·라운드 넘기기를 라운드 시스템으로 보냄
/// DebugTools 오브젝트에 붙여서 사용.
/// </summary>
public class DebugMenu : MonoBehaviour
{
    [Header("조명")]
    [Tooltip("카메라 자식 Spot Light")]
    [SerializeField] private Light flashlight;
    [Tooltip("Shadow Type = No Shadows 인 Directional Light")]
    [SerializeField] private Light debugSun;
    [SerializeField] private Color debugAmbient = new Color(0.5f, 0.5f, 0.5f);

    [Header("시스템 (비워두면 자동 검색)")]
    [SerializeField] private MannequinSpawner mannequinSpawner;
    [SerializeField] private TrapSpawner trapSpawner;

    [Header("봇 소환 (F6)")]
    [Tooltip("PlayerVariant_Mannequin 프리팹")]
    [SerializeField] private GameObject botPrefab;
    [Tooltip("내 앞 몇 m에 소환할지")]
    [SerializeField, Min(1f)] private float botSpawnDistance = 3f;

    [Header("점수 테스트")]
    [Tooltip("랜덤 킬 시 자동 등록할 가짜 플레이어 수 (나 포함 최대 4명이면 3)")]
    [SerializeField, Min(0)] private int fakePlayers = 3;

    [Header("메뉴")]
    [SerializeField] private Key menuKey = Key.Numpad0;
    [SerializeField] private bool showOnStart = false;
    [SerializeField] private int fontSize = 16;

    private class Entry
    {
        public Key key;
        public string label;
        public Action action;
        public Func<string> state;   // null이면 상태 표시 없음
    }

    private readonly List<Entry> entries = new List<Entry>();
    private bool visible;
    private bool brightOn;
    private Color savedAmbient;
    private int botCount;
    private GUIStyle boxStyle, labelStyle, titleStyle, buttonStyle;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        // 일반 빌드: 디버그 기능 전부 끔 (Build Profiles에서 Development Build 체크 시에만 사용 가능)
        if (!Application.isEditor && !Debug.isDebugBuild)
        {
            // Start가 안 불리므로 디버그 조명은 여기서 끔 (안 끄면 맵이 밝게 보임)
            if (flashlight != null) flashlight.enabled = false;
            if (debugSun != null) debugSun.enabled = false;
            enabled = false;
            return;
        }
        visible = showOnStart;
        if (mannequinSpawner == null) mannequinSpawner = FindAnyObjectByType<MannequinSpawner>();
        if (trapSpawner == null) trapSpawner = FindAnyObjectByType<TrapSpawner>();

        // 키 배정은 여기 한 곳에서 관리 (키패드)
        Add(Key.Numpad1, "손전등", ToggleFlashlight, () => OnOff(flashlight != null && flashlight.enabled));
        Add(Key.Numpad2, "맵 전체 밝게", ToggleBright, () => OnOff(brightOn));
        Add(Key.Numpad3, "현재 위치를 발사 위치로 기록", RecordShot, null);
        Add(Key.Numpad4, "라운드 강제 종료", NextRound, RoundState);
        Add(Key.Numpad5, "랜덤 킬", RandomKill, null);
        Add(Key.Numpad6, "내 데스 +1", LocalDeath, null);
        Add(Key.Numpad7, "점수 초기화", ResetScores, null);
        Add(Key.Numpad8, "트랩 데칼 항상 표시", ToggleTrapDecals, () => OnOff(TrapBase.DebugShowAll));
        Add(Key.Numpad9, "캐릭터 데칼 항상 켜기", ToggleCharacterDecals, () => OnOff(DecalBlink.DebugForceOn));
        Add(Key.F5, "경기 재시작", RestartMatch, null);
        Add(Key.F6, "적 봇 소환 (내 앞)", SpawnBot, null);
    }

    private void Start()
    {
        if (flashlight != null) flashlight.enabled = false;
        if (debugSun != null) debugSun.enabled = false;
        savedAmbient = RenderSettings.ambientLight;
        TrapBase.DebugShowAll = false;
        DecalBlink.DebugForceOn = false;

        // 멀티 플레이 중에는 디버그 기능 끔 (싱글 / 에디터에서 Warehouse 바로 실행 시에는 켜짐)
        //if (LobbyManager.IsMultiplayer) enabled = false;
    }
    private void OnDisable()
    {
        if (brightOn) SetBright(false);   // Play 종료 시 환경광 복구
    }

    private void Add(Key key, string label, Action action, Func<string> state)
    {
        entries.Add(new Entry { key = key, label = label, action = action, state = state });
    }

    private static string OnOff(bool on) => on ? "ON" : "OFF";

    private static string KeyName(Key key) => key.ToString().Replace("Numpad", "Num ");

    private static string RoundState()
    {
        var rm = RoundManager.Instance;
        return rm != null ? $"{rm.CurrentState} {rm.CurrentRound}/{rm.TotalRounds}" : "RoundManager 없음";
    }

    // ───────── 입력 ─────────

    private void Update()
    {
#if UNITY_EDITOR || DEBUG
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[menuKey].wasPressedThisFrame) visible = !visible;

        foreach (var e in entries)
            if (kb[e.key].wasPressedThisFrame) e.action?.Invoke();
#endif
    }

    // ───────── 조명 ─────────

    private void ToggleFlashlight()
    {
        if (flashlight == null) return;
        flashlight.gameObject.SetActive(true);   // 오브젝트가 꺼져 있어도 동작하게
        flashlight.enabled = !flashlight.enabled;
    }

    private void ToggleBright() => SetBright(!brightOn);

    private UnityEngine.Rendering.AmbientMode savedAmbientMode;
    private float savedAmbientIntensity;
    private bool savedAmbientStored;

    private void SetBright(bool on)
    {
        if (on && !savedAmbientStored)
        {
            // 현재 환경광 설정 저장 (Skybox 모드여도 복구 가능하게)
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbientIntensity = RenderSettings.ambientIntensity;
            savedAmbient = RenderSettings.ambientLight;
            savedAmbientStored = true;
        }

        brightOn = on;

        if (on)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;   // Color 모드
            RenderSettings.ambientLight = debugAmbient;
        }
        else if (savedAmbientStored)
        {
            RenderSettings.ambientMode = savedAmbientMode;
            RenderSettings.ambientIntensity = savedAmbientIntensity;
            RenderSettings.ambientLight = savedAmbient;
            savedAmbientStored = false;
        }

        if (debugSun != null)
        {
            debugSun.gameObject.SetActive(true);   // 오브젝트가 꺼져 있어도 동작하게
            debugSun.enabled = on;
        }
    }

    // ───────── 마네킹 / 라운드 ─────────

    private void RecordShot()
    {
        var p = GameObject.FindWithTag("Player");
        if (mannequinSpawner == null || p == null) return;
        mannequinSpawner.RecordShot(p.transform.position, p.transform.forward);
        Debug.Log("[DebugMenu] 발사 위치 기록");
    }

    private void NextRound()
    {
        var rm = RoundManager.Instance;
        if (rm != null)
        {
            rm.ForceEndRound();
            Debug.Log("[DebugMenu] 라운드 강제 종료");
            return;
        }

        // RoundManager가 없을 때: 예전처럼 바로 재배치
        if (mannequinSpawner != null)
        {
            mannequinSpawner.EndRound();
            mannequinSpawner.SpawnForRound();
        }
        if (trapSpawner != null) trapSpawner.SpawnForRound();
        Debug.Log("[DebugMenu] 라운드 넘김 (재배치)");
    }

    private void RestartMatch()
    {
        if (RoundManager.Instance != null) RoundManager.Instance.StartMatch();
    }

    private void SpawnBot()
    {
        var p = GameObject.FindWithTag("Player");
        if (botPrefab == null)
        {
            Debug.LogWarning("[DebugMenu] Bot Prefab이 비어 있습니다 (PlayerVariant_Mannequin 연결).", this);
            return;
        }
        if (p == null) return;

        Vector3 pos = p.transform.position + p.transform.forward * botSpawnDistance;
        if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas))
            pos = hit.position;

        var bot = Instantiate(botPrefab, pos, Quaternion.LookRotation(-p.transform.forward));
        bot.name = $"EnemyBot_{++botCount:00}";

        var h = bot.GetComponent<Health>();
        if (h != null) h.SetupAsBot($"ENEMY_{botCount:00}");

        Debug.Log($"[DebugMenu] 적 봇 소환: {bot.name}");
    }

    // ───────── 점수 ─────────

    private void RandomKill()
    {
        var sm = ScoreManager.Instance;
        if (sm == null) return;

        EnsureFakePlayers(sm);
        var rm = RoundManager.Instance;

        List<int> ids = rm != null
            ? rm.AliveIds.ToList()
            : sm.GetSorted().Select(s => s.id).ToList();
        if (ids.Count < 2) return;

        int killer = ids[UnityEngine.Random.Range(0, ids.Count)];
        int victim;
        do { victim = ids[UnityEngine.Random.Range(0, ids.Count)]; } while (victim == killer);

        if (rm != null) rm.ReportKill(killer, victim);
        else sm.AddKill(killer, victim);
    }

    private void LocalDeath()
    {
        var sm = ScoreManager.Instance;
        if (sm == null) return;

        EnsureFakePlayers(sm);
        var rm = RoundManager.Instance;

        var pool = rm != null ? rm.AliveIds.ToList() : sm.GetSorted().Select(s => s.id).ToList();
        var others = pool.Where(id => id != sm.LocalPlayerId).ToList();
        int killer = others.Count > 0 ? others[UnityEngine.Random.Range(0, others.Count)] : -1;

        if (rm != null) rm.ReportKill(killer, sm.LocalPlayerId);
        else sm.AddKill(killer, sm.LocalPlayerId);
    }

    private void ResetScores()
    {
        if (ScoreManager.Instance != null) ScoreManager.Instance.ResetScores();
    }

    private void EnsureFakePlayers(ScoreManager sm)
    {
        int bots = sm.GetSorted().Count(s => !s.isLocal);
        for (int i = bots; i < fakePlayers; i++)
        {
            int id = sm.Register($"BOT_{i + 1:00}");
            if (id < 0) break;   // 최대 인원이면 중단
            if (RoundManager.Instance != null) RoundManager.Instance.RegisterParticipant(id);
        }
    }

    // ───────── 트랩 / 데칼 ─────────

    private void ToggleTrapDecals()
    {
        TrapBase.DebugShowAll = !TrapBase.DebugShowAll;
        foreach (var t in FindObjectsByType<TrapBase>(FindObjectsInactive.Include))
            t.RefreshIdle();
    }

    private void ToggleCharacterDecals()
    {
        DecalBlink.DebugForceOn = !DecalBlink.DebugForceOn;
        foreach (var d in FindObjectsByType<DecalBlink>(FindObjectsInactive.Include))
            d.RefreshDebug();
    }

    // ───────── 메뉴 창 ─────────

#if UNITY_EDITOR || DEBUG
    private void OnGUI()
    {
        if (!visible) return;
        EnsureStyles();

        // 영역은 넉넉하게 잡고, 박스(Vertical)가 내용 크기만큼만 그려지게
        GUILayout.BeginArea(new Rect(16, 16, 480f, Screen.height - 32));
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label($"DEBUG MENU   [{KeyName(menuKey)}] 닫기", titleStyle);
        GUILayout.Space(6);

        foreach (var e in entries)
        {
            string state = e.state != null ? $"  <b>{e.state()}</b>" : "";
            if (GUILayout.Button($"[{KeyName(e.key)}]  {e.label}{state}", buttonStyle))
                e.action?.Invoke();
        }

        GUILayout.Space(6);
        GUILayout.Label($"FPS: {(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)):0}", labelStyle);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (boxStyle != null) return;

        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
        bg.Apply();

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = bg },
            padding = new RectOffset(12, 12, 10, 10)
        };
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            richText = true,
            normal = { textColor = Color.white }
        };
        titleStyle = new GUIStyle(labelStyle)
        {
            fontSize = fontSize + 4,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.85f, 0.3f) }
        };
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = fontSize,
            richText = true,
            alignment = TextAnchor.MiddleLeft
        };
    }
#endif
}