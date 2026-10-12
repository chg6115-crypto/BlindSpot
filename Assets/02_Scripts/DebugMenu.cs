using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 모든 디버그 기능을 한 곳에서 관리 (에디터 / Development Build 전용)
///  - 숫자 키로 실행, 0키로 메뉴 창 열기/닫기 (메뉴의 버튼을 눌러도 실행)
///  - 각 시스템의 공개 함수를 호출하므로, 개별 스크립트의 디버그 키는 꺼둘 것
/// DebugTools 오브젝트에 붙여서 사용. (DebugLightToggle 기능 포함 → 그 컴포넌트는 제거)
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

    [Header("점수 테스트")]
    [Tooltip("랜덤 킬 시 자동 등록할 가짜 플레이어 수")]
    [SerializeField, Min(0)] private int fakePlayers = 4;

    [Header("메뉴")]
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
    private GUIStyle boxStyle, labelStyle, titleStyle, buttonStyle;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        visible = showOnStart;
        if (mannequinSpawner == null) mannequinSpawner = FindFirstObjectByType<MannequinSpawner>();
        if (trapSpawner == null) trapSpawner = FindFirstObjectByType<TrapSpawner>();

        // 키 배정은 여기 한 곳에서 관리
        Add(Key.Digit1, "손전등", ToggleFlashlight, () => OnOff(flashlight != null && flashlight.enabled));
        Add(Key.Digit2, "맵 전체 밝게", ToggleBright, () => OnOff(brightOn));
        Add(Key.Digit3, "현재 위치를 발사 위치로 기록", RecordShot, null);
        Add(Key.Digit4, "라운드 넘기기 (마네킹·트랩 재배치)", NextRound, null);
        Add(Key.Digit5, "랜덤 킬", RandomKill, null);
        Add(Key.Digit6, "내 데스 +1", LocalDeath, null);
        Add(Key.Digit7, "점수 초기화", ResetScores, null);
        Add(Key.Digit8, "트랩 데칼 항상 표시", ToggleTrapDecals, () => OnOff(TrapBase.DebugShowAll));
    }

    private void Start()
    {
        if (flashlight != null) flashlight.enabled = false;
        if (debugSun != null) debugSun.enabled = false;
        savedAmbient = RenderSettings.ambientLight;
        TrapBase.DebugShowAll = false;
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

    // ───────── 입력 ─────────

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[Key.Digit0].wasPressedThisFrame) visible = !visible;

        foreach (var e in entries)
            if (kb[e.key].wasPressedThisFrame) e.action?.Invoke();
#endif
    }

    // ───────── 조명 ─────────

    private void ToggleFlashlight()
    {
        if (flashlight != null) flashlight.enabled = !flashlight.enabled;
    }

    private void ToggleBright() => SetBright(!brightOn);

    private void SetBright(bool on)
    {
        brightOn = on;
        RenderSettings.ambientLight = on ? debugAmbient : savedAmbient;
        if (debugSun != null) debugSun.enabled = on;
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
        if (mannequinSpawner != null)
        {
            mannequinSpawner.EndRound();
            mannequinSpawner.SpawnForRound();
        }
        if (trapSpawner != null) trapSpawner.SpawnForRound();
        Debug.Log("[DebugMenu] 라운드 넘김");
    }

    // ───────── 점수 ─────────

    private void RandomKill()
    {
        var sm = ScoreManager.Instance;
        if (sm == null) return;

        EnsureFakePlayers(sm);
        var ids = sm.GetSorted().Select(s => s.id).ToList();
        if (ids.Count < 2) return;

        int killer = ids[UnityEngine.Random.Range(0, ids.Count)];
        int victim;
        do { victim = ids[UnityEngine.Random.Range(0, ids.Count)]; } while (victim == killer);
        sm.AddKill(killer, victim);
    }

    private void LocalDeath()
    {
        var sm = ScoreManager.Instance;
        if (sm == null) return;

        EnsureFakePlayers(sm);
        var others = sm.GetSorted().Where(s => s.id != sm.LocalPlayerId).Select(s => s.id).ToList();
        int killer = others.Count > 0 ? others[UnityEngine.Random.Range(0, others.Count)] : -1;
        sm.AddKill(killer, sm.LocalPlayerId);
    }

    private void ResetScores()
    {
        if (ScoreManager.Instance != null) ScoreManager.Instance.ResetScores();
    }

    private void EnsureFakePlayers(ScoreManager sm)
    {
        int bots = sm.GetSorted().Count(s => !s.isLocal);
        for (int i = bots; i < fakePlayers; i++)
            if (sm.Register($"BOT_{i + 1:00}") < 0) break;   // 최대 인원이면 중단
    }

    // ───────── 트랩 ─────────

    private void ToggleTrapDecals()
    {
        TrapBase.DebugShowAll = !TrapBase.DebugShowAll;
        foreach (var t in FindObjectsByType<TrapBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            t.RefreshIdle();
    }

    // ───────── 메뉴 창 ─────────

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (!visible) return;
        EnsureStyles();

        float lineH = fontSize + 12;
        Rect area = new Rect(16, 16, 460f, 60f + lineH * (entries.Count + 2));

        GUI.Box(area, GUIContent.none, boxStyle);
        GUILayout.BeginArea(new Rect(area.x + 12, area.y + 10, area.width - 24, area.height - 20));

        GUILayout.Label("DEBUG MENU   [0] 닫기", titleStyle);
        GUILayout.Space(6);

        foreach (var e in entries)
        {
            GUILayout.BeginHorizontal();
            string keyName = e.key.ToString().Replace("Digit", "");
            string state = e.state != null ? $"  <b>{e.state()}</b>" : "";
            if (GUILayout.Button($"[{keyName}]  {e.label}{state}", buttonStyle))
                e.action?.Invoke();
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6);
        GUILayout.Label($"FPS: {(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)):0}", labelStyle);

        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (boxStyle != null) return;

        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
        bg.Apply();

        boxStyle = new GUIStyle(GUI.skin.box) { normal = { background = bg } };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = true, normal = { textColor = Color.white } };
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