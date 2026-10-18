using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tab 점수판. RoundManager와 같은 오브젝트에 붙인다. Tab을 누르고 있는 동안만 표시.
///  - 순위 · 닉네임 · 라운드 승리 · 킬 · 데스 (결과창과 같은 순위 규칙)
///  - 누가 살아 있는지는 숨기고, 남은 인원 수만 표시 (예: 2 / 4 ALIVE)
///  - 킬 알림(킬 피드)은 표시하지 않음
/// 화면은 1920x1080 기준으로 그리고 해상도에 맞게 확대/축소.
/// </summary>
public class TabScoreboardUI : MonoBehaviour
{
    [SerializeField] private Font font;
    [SerializeField] private Key key = Key.Tab;

    [Header("색")]
    [SerializeField] private Color accent = new Color32(64, 230, 240, 255);
    [SerializeField] private Color gold = new Color32(255, 205, 80, 255);
    [SerializeField] private Color textColor = new Color32(220, 228, 245, 255);
    [SerializeField] private Color dimColor = new Color32(110, 130, 170, 255);
    [SerializeField] private Color panelColor = new Color32(8, 12, 28, 215);
    [SerializeField] private Color lineColor = new Color32(60, 90, 130, 255);
    [SerializeField] private Color highlightColor = new Color32(20, 60, 80, 230);

    private const float RefW = 1920f, RefH = 1080f;

    private RoundManager round;
    private bool visible;
    private Texture2D white;
    private GUIStyle sTitle, sHead, sCell, sTag, sSmall, sAlive;

    private void Start()
    {
        round = GetComponent<RoundManager>();
        if (round == null) round = RoundManager.Instance;
    }

    private void OnDestroy()
    {
        if (white != null) Destroy(white);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        bool held = kb != null && kb[key].isPressed;

        // 경기 전 / 결과창이 떠 있으면 표시 안 함
        bool allowed = round != null
                       && round.CurrentState != RoundManager.State.Idle
                       && !MatchResultUI.IsShowing
                       && !PauseMenuUI.IsOpen;
        visible = held && allowed;
    }

    private void OnGUI()
    {
        if (!visible) return;
        EnsureStyles();

        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        Vector2 offset = new Vector2((Screen.width - RefW * scale) * 0.5f, (Screen.height - RefH * scale) * 0.5f);
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3(offset.x, offset.y, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

        var sm = ScoreManager.Instance;
        var rows = sm != null ? sm.GetSorted() : new System.Collections.Generic.List<ScoreManager.PlayerScore>();
        int rowCount = Mathf.Clamp(rows.Count, 1, 8);
        float rowH = 72f;

        float px = 460, pw = 1000, py = 230;
        float ph = 145 + rowCount * rowH + 80;
        Fill(new Rect(px, py, pw, ph), panelColor);
        Outline(new Rect(px, py, pw, ph), accent, 2);

        // 제목줄: SCOREBOARD / 남은 인원 / 라운드
        Label(new Rect(px + 30, py + 18, 400, 50), "SCOREBOARD", sTitle, accent, TextAnchor.MiddleLeft);
        int alive = round != null ? round.AliveIds.Count : 0;
        int total = Mathf.Max(alive, rows.Count);
        string aliveText = round != null && round.CurrentState == RoundManager.State.Playing
            ? $"{alive} / {total} ALIVE" : "";
        Label(new Rect(px, py + 22, pw, 44), aliveText, sAlive, textColor);
        string roundText = round != null ? $"ROUND {round.CurrentRound} / {round.TotalRounds}" : "";
        Label(new Rect(px + pw - 330, py + 22, 300, 44), roundText, sHead, dimColor, TextAnchor.MiddleRight);
        Fill(new Rect(px + 20, py + 80, pw - 40, 2), lineColor);

        // 열 제목
        float cRank = px + 30, cName = px + 150, cRounds = px + 600, cKills = px + 740, cDeaths = px + 870;
        Label(new Rect(cRank, py + 92, 120, 36), "RANK", sHead, dimColor, TextAnchor.MiddleLeft);
        Label(new Rect(cName, py + 92, 300, 36), "PLAYER", sHead, dimColor, TextAnchor.MiddleLeft);
        Label(new Rect(cRounds - 40, py + 92, 140, 36), "ROUNDS", sHead, dimColor, TextAnchor.MiddleCenter);
        Label(new Rect(cKills - 40, py + 92, 120, 36), "KILLS", sHead, dimColor, TextAnchor.MiddleCenter);
        Label(new Rect(cDeaths - 40, py + 92, 120, 36), "DEATHS", sHead, dimColor, TextAnchor.MiddleCenter);

        // 줄
        float y = py + 140;
        int rank = 0;
        for (int i = 0; i < rows.Count && i < 8; i++)
        {
            var r = rows[i];
            if (i == 0 || !ScoreManager.SameRank(rows[i - 1], r)) rank = i + 1;

            if (r.isLocal)
            {
                Fill(new Rect(px + 12, y - 4, pw - 24, rowH - 8), highlightColor);
                Fill(new Rect(px + 12, y - 4, 8, rowH - 8), accent);
            }

            bool leader = rank == 1 && (r.roundWins > 0 || r.kills > 0);
            Color c = leader ? gold : textColor;
            string pname = (r.name ?? "").ToUpperInvariant();
            Label(new Rect(cRank + 20, y, 80, rowH - 16), rank.ToString(), sCell, c, TextAnchor.MiddleLeft);
            Label(new Rect(cName, y, 420, rowH - 16), pname, sCell, c, TextAnchor.MiddleLeft);
            if (r.isLocal)
            {
                float w = sCell.CalcSize(new GUIContent(pname)).x;
                Label(new Rect(cName + w + 14, y + 8, 80, rowH - 32), "YOU", sTag, accent, TextAnchor.MiddleLeft);
            }
            Label(new Rect(cRounds - 40, y, 140, rowH - 16), r.roundWins.ToString(), sCell, c, TextAnchor.MiddleCenter);
            Label(new Rect(cKills - 40, y, 120, rowH - 16), r.kills.ToString(), sCell, c, TextAnchor.MiddleCenter);
            Label(new Rect(cDeaths - 40, y, 120, rowH - 16), r.deaths.ToString(), sCell, c, TextAnchor.MiddleCenter);
            y += rowH;
        }

        // 아래줄
        float footY = py + ph - 64;
        Fill(new Rect(px + 20, footY, pw - 40, 1), lineColor);
        Label(new Rect(px + 30, footY + 14, 300, 36), "HOLD TAB", sSmall, dimColor, TextAnchor.MiddleLeft);
        Label(new Rect(px + pw - 630, footY + 14, 600, 36), "LAST ONE STANDING WINS THE ROUND", sSmall, dimColor, TextAnchor.MiddleRight);

        GUI.matrix = oldMatrix;
    }

    // ───────── 그리기 도우미 ─────────

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

        sTitle = Make(34);
        sHead = Make(24);
        sCell = Make(36);
        sTag = Make(20);
        sSmall = Make(22);
        sAlive = Make(28);
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
