using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 게임 중 ESC 메뉴. RoundManager와 같은 오브젝트에 붙인다 (싱글 / 멀티 공통).
///
///  [메인]     RESUME / SETTINGS / MAIN MENU
///  [설정]     마스터·음악·효과음 볼륨, 마우스 감도, 전체화면, 기본값 복원 (로비 설정과 같은 값 = GameSettings)
///  [확인]     MAIN MENU → "LEAVE THE MATCH?" YES / NO (호스트면 방이 닫힌다는 경고 추가)
///
///  - 게임은 멈추지 않음 (싱글도 동일). 메뉴가 열린 동안 내 조작만 막고 커서를 풂
///    · 이동/시점/점프: PlayerInput 끄고 입력값 0
///    · 사격: 커서가 풀리면 GunWeapon이 알아서 막음
///    · 소나/무기 전환/무기 선택/관전 전환/Tab 점수판: 각 스크립트가 IsOpen을 확인
///  - ESC: 열기/닫기. 설정·확인 화면에서는 한 단계 뒤로
///  - 결과창이 떠 있으면 열리지 않음 (열려 있었으면 닫힘)
/// 화면은 1920x1080 기준으로 그리고 해상도에 맞게 확대/축소.
/// </summary>
[DefaultExecutionOrder(-200)]   // 다른 스크립트보다 먼저 ESC 처리 → 같은 프레임에 IsOpen 반영
public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] private Font font;

    [Header("색")]
    [SerializeField] private Color accent = new Color32(64, 230, 240, 255);
    [SerializeField] private Color warnColor = new Color32(255, 120, 90, 255);
    [SerializeField] private Color textColor = new Color32(220, 228, 245, 255);
    [SerializeField] private Color dimColor = new Color32(110, 130, 170, 255);
    [SerializeField] private Color panelColor = new Color32(10, 16, 34, 235);
    [SerializeField] private Color lineColor = new Color32(60, 90, 130, 255);
    [SerializeField] private Color buttonColor = new Color32(20, 60, 80, 230);
    [SerializeField] private Color buttonHoverColor = new Color32(30, 90, 110, 255);

    private const float RefW = 1920f, RefH = 1080f;

    /// <summary>ESC 메뉴가 열려 있는지 (입력 차단용)</summary>
    public static bool IsOpen { get; private set; }

    private enum Page { Main, Settings, Confirm }
    private Page page;

    // 열기 전 상태 (닫을 때 복구)
    private PlayerInput savedInput;
    private bool savedInputEnabled;
    private StarterAssetsInputs savedSai;

    private Texture2D white;
    private GUIStyle sTitle, sButton, sLabel, sValue, sSmall, sWarn, sSliderBg, sThumb;

    // ───────── Unity ─────────

    private void Awake()
    {
        IsOpen = false;
    }

    private void OnDestroy()
    {
        if (IsOpen) RestoreInput();
        IsOpen = false;
        if (white != null) Destroy(white);
    }

    private void Update()
    {
        // 결과창이 뜨면 닫음 (결과창이 커서를 관리)
        if (MatchResultUI.IsShowing || GameExit.IsLeaving)
        {
            if (IsOpen) Close(false);
            return;
        }

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            if (!IsOpen) Open();
            else if (page != Page.Main) page = Page.Main;   // 한 단계 뒤로
            else Close(true);
        }

        if (IsOpen) HoldInput();
    }

    // ───────── 열기 / 닫기 ─────────

    private void Open()
    {
        IsOpen = true;
        page = Page.Main;

        var local = PlayerNetworkSetup.Local;
        if (local != null)
        {
            savedInput = local.GetComponent<PlayerInput>();
            savedInputEnabled = savedInput != null && savedInput.enabled;
            if (savedInput != null) savedInput.enabled = false;

            savedSai = local.GetComponent<StarterAssetsInputs>();
            if (savedSai != null) savedSai.cursorLocked = false;   // 창 포커스가 돌아와도 다시 잠기지 않게
        }
        HoldInput();
    }

    private void Close(bool relockCursor)
    {
        IsOpen = false;
        page = Page.Main;
        PlayerPrefs.Save();
        RestoreInput();

        if (relockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void RestoreInput()
    {
        if (savedSai != null)
        {
            savedSai.cursorLocked = true;
            ZeroInput(savedSai);
        }
        if (savedInput != null && savedInputEnabled) savedInput.enabled = true;

        savedInput = null;
        savedSai = null;
        savedInputEnabled = false;
    }

    /// <summary>메뉴가 열린 동안: 커서 풀고, 이동/시점 입력 0 (그 자리에 멈춤, 중력은 그대로)</summary>
    private void HoldInput()
    {
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (savedSai != null) ZeroInput(savedSai);
    }

    private static void ZeroInput(StarterAssetsInputs sai)
    {
        sai.move = Vector2.zero;
        sai.look = Vector2.zero;
        sai.jump = false;
        sai.sprint = false;
    }

    private static bool IsHostWithOthers()
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening && nm.IsServer && nm.ConnectedClientsIds.Count > 1;
    }

    // ───────── 그리기 ─────────

    private void OnGUI()
    {
        if (!IsOpen) return;
        EnsureStyles();
        GUI.depth = -100;   // 다른 HUD 위에

        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        Vector2 offset = new Vector2((Screen.width - RefW * scale) * 0.5f, (Screen.height - RefH * scale) * 0.5f);
        var oldMatrix = GUI.matrix;

        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
        GUI.matrix = Matrix4x4.TRS(new Vector3(offset.x, offset.y, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

        switch (page)
        {
            case Page.Main: DrawMain(); break;
            case Page.Settings: DrawSettings(); break;
            case Page.Confirm: DrawConfirm(); break;
        }

        GUI.matrix = oldMatrix;
    }

    private void DrawMain()
    {
        var panel = Panel(560, 500);
        Label(new Rect(panel.x, panel.y + 30, panel.width, 70), "PAUSED", sTitle, accent);
        Fill(new Rect(panel.center.x - 140, panel.y + 108, 280, 3), accent);

        float bx = panel.center.x - 200, by = panel.y + 150;
        if (Button(new Rect(bx, by, 400, 76), "RESUME")) Close(true);
        if (Button(new Rect(bx, by + 100, 400, 76), "SETTINGS")) page = Page.Settings;
        if (Button(new Rect(bx, by + 200, 400, 76), "MAIN MENU")) page = Page.Confirm;
    }

    private void DrawConfirm()
    {
        bool host = IsHostWithOthers();
        var panel = Panel(820, host ? 400 : 340);
        Label(new Rect(panel.x, panel.y + 40, panel.width, 70), "LEAVE THE MATCH?", sTitle, textColor);

        float y = panel.y + 130;
        if (host)
        {
            Label(new Rect(panel.x, y, panel.width, 50), "THE ROOM WILL CLOSE FOR ALL PLAYERS", sWarn, warnColor);
            y += 70;
        }
        else y += 20;

        if (Button(new Rect(panel.center.x - 330, y, 300, 76), "YES"))
        {
            Close(false);
            GameExit.ToMainMenu();
        }
        if (Button(new Rect(panel.center.x + 30, y, 300, 76), "NO")) page = Page.Main;
    }

    private void DrawSettings()
    {
        var panel = Panel(1000, 760);
        Label(new Rect(panel.x, panel.y + 30, panel.width, 70), "SETTINGS", sTitle, accent);
        Fill(new Rect(panel.center.x - 160, panel.y + 108, 320, 3), accent);

        float x = panel.x + 70, w = panel.width - 140;
        float y = panel.y + 150;

        float v;
        v = SliderRow(new Rect(x, y, w, 70), "MASTER", GameSettings.Master, 0f, 100f);
        if (!Mathf.Approximately(v, GameSettings.Master)) GameSettings.SetMaster(v);
        y += 85;
        v = SliderRow(new Rect(x, y, w, 70), "MUSIC", GameSettings.Music, 0f, 100f);
        if (!Mathf.Approximately(v, GameSettings.Music)) GameSettings.SetMusic(v);
        y += 85;
        v = SliderRow(new Rect(x, y, w, 70), "SFX", GameSettings.Sfx, 0f, 100f);
        if (!Mathf.Approximately(v, GameSettings.Sfx)) GameSettings.SetSfx(v);
        y += 85;
        v = SliderRow(new Rect(x, y, w, 70), "SENSITIVITY", GameSettings.Sensitivity, GameSettings.MinSensitivity, GameSettings.MaxSensitivity, true);
        if (!Mathf.Approximately(v, GameSettings.Sensitivity)) GameSettings.SetSensitivity(v);
        y += 95;

        // 전체화면
        Label(new Rect(x, y, 360, 60), "FULLSCREEN", sLabel, textColor, TextAnchor.MiddleLeft);
        if (Button(new Rect(x + w - 220, y, 220, 60), GameSettings.Fullscreen ? "ON" : "OFF"))
            GameSettings.SetFullscreen(!GameSettings.Fullscreen);
        y += 100;

        Fill(new Rect(x, y - 15, w, 2), lineColor);
        if (Button(new Rect(panel.center.x - 330, y + 10, 300, 70), "RESET")) GameSettings.ResetDefaults();
        if (Button(new Rect(panel.center.x + 30, y + 10, 300, 70), "BACK"))
        {
            PlayerPrefs.Save();
            page = Page.Main;
        }
    }

    /// <summary>이름 + 슬라이더 + 숫자 한 줄. 정수로 반올림한 값 반환</summary>
    /// <param name="sensitivity">감도 슬라이더: 5 단위 스냅 + 배율(1.0) 표시</param>
    private float SliderRow(Rect r, string label, float value, float min, float max, bool sensitivity = false)
    {
        Label(new Rect(r.x, r.y, 330, r.height), label, sLabel, textColor, TextAnchor.MiddleLeft);

        var track = new Rect(r.x + 340, r.center.y - 6, r.width - 340 - 110, 12);
        float t = Mathf.InverseLerp(min, max, value);
        Fill(track, new Color32(14, 22, 44, 255));
        Fill(new Rect(track.x, track.y, track.width * t, track.height), accent);
        Outline(track, lineColor, 1);

        // 실제 조작은 투명 슬라이더 (손잡이만 보임)
        var hit = new Rect(track.x, r.center.y - 22, track.width, 44);
        float nv = GUI.HorizontalSlider(hit, value, min, max, sSliderBg, sThumb);

        if (sensitivity) nv = GameSettings.SnapSensitivity(nv);
        string shown = sensitivity ? GameSettings.ToSensitivityDisplay(nv) : Mathf.RoundToInt(nv).ToString();
        Label(new Rect(r.xMax - 100, r.y, 100, r.height), shown, sValue, accent, TextAnchor.MiddleRight);
        return Mathf.Round(nv);
    }

    // ───────── 그리기 도우미 ─────────

    private Rect Panel(float w, float h)
    {
        var r = new Rect((RefW - w) * 0.5f, (RefH - h) * 0.5f, w, h);
        Fill(r, panelColor);
        Outline(r, accent, 2);
        return r;
    }

    private bool Button(Rect r, string text)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Fill(r, hover ? buttonHoverColor : buttonColor);
        Outline(r, accent, 2);
        Fill(new Rect(r.x, r.y, 8, r.height), accent);
        Label(r, text, sButton, hover ? Color.white : textColor);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    private void EnsureStyles()
    {
        if (white == null)
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }
        if (sTitle != null) return;

        GUIStyle Make(int size)
        {
            var st = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow
            };
            if (font != null) st.font = font;
            else st.fontStyle = FontStyle.Bold;
            return st;
        }

        sTitle = Make(56);
        sButton = Make(36);
        sLabel = Make(32);
        sValue = Make(32);
        sSmall = Make(24);
        sWarn = Make(28);

        // 슬라이더: 배경은 투명 (트랙은 직접 그림), 손잡이는 흰 사각형
        sSliderBg = new GUIStyle { fixedHeight = 44f };
        sThumb = new GUIStyle
        {
            fixedWidth = 18f,
            fixedHeight = 44f
        };
        sThumb.normal.background = white;
        sThumb.hover.background = white;
        sThumb.active.background = white;
    }

    private void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, white);
        GUI.color = old;
    }

    private void Outline(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    private static void Label(Rect r, string text, GUIStyle style, Color color, TextAnchor? anchor = null)
    {
        var oldAnchor = style.alignment;
        if (anchor.HasValue) style.alignment = anchor.Value;
        style.normal.textColor = color;
        GUI.Label(r, text, style);
        style.alignment = oldAnchor;
    }
}
