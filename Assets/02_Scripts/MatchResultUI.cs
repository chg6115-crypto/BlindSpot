using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 경기 결과창. RoundManager와 같은 오브젝트에 붙인다.
///  - 경기 종료(OnMatchEnd) 시 표시: 우승자 / 순위표(라운드 승리, 킬, 데스) / 라운드별 승자
///  - 15초 뒤 자동으로 메인 메뉴, 또는 MAIN MENU 버튼 (누구나)
///  - 조기 종료 사유가 있으면 MATCH OVER 아래에 표시 (예: ALL PLAYERS LEFT)
///  연결 끊김 처리는 ConnectionWatcher가 담당
/// 화면은 1920x1080 기준으로 그리고 해상도에 맞게 확대/축소.
/// </summary>
public class MatchResultUI : MonoBehaviour
{
    [SerializeField] private Font font;
    [Tooltip("결과창 표시 후 메인 메뉴로 돌아가기까지 (초)")]
    [SerializeField, Min(3f)] private float autoReturnTime = 15f;

    [Header("색")]
    [SerializeField] private Color accent = new Color32(64, 230, 240, 255);
    [SerializeField] private Color gold = new Color32(255, 205, 80, 255);
    [SerializeField] private Color textColor = new Color32(220, 228, 245, 255);
    [SerializeField] private Color dimColor = new Color32(110, 130, 170, 255);
    [SerializeField] private Color panelColor = new Color32(10, 16, 34, 235);
    [SerializeField] private Color lineColor = new Color32(60, 90, 130, 255);
    [SerializeField] private Color highlightColor = new Color32(20, 60, 80, 230);

    private const float RefW = 1920f, RefH = 1080f;

    /// <summary>결과창이 떠 있는지 (연결이 끊겨도 안내 문구를 띄우지 않기 위해)</summary>
    public static bool IsShowing { get; private set; }

    private RoundManager round;
    private bool showing;
    private int winnerId = -1;
    private float returnAt;
    private Texture2D white;
    private GUIStyle sTitle, sWinnerLabel, sWinner, sHead, sCell, sTag, sSmall, sCount, sButton;

    // ───────── Unity ─────────

    private void Start()
    {
        round = GetComponent<RoundManager>();
        if (round == null) round = RoundManager.Instance;
        if (round != null) round.OnMatchEnd += HandleMatchEnd;
        IsShowing = false;
    }

    private void OnDestroy()
    {
        if (round != null) round.OnMatchEnd -= HandleMatchEnd;
        if (white != null) Destroy(white);
        IsShowing = false;
    }

    private void HandleMatchEnd(int winner)
    {
        winnerId = winner;
        showing = true;
        IsShowing = true;
        returnAt = Time.unscaledTime + autoReturnTime;
    }

    private void Update()
    {
        if (!showing) return;

        // 결과창 동안은 커서 풀기 (게임 쪽에서 다시 잠그는 것 방지)
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (Time.unscaledTime >= returnAt) GameExit.ToMainMenu();
    }

    // ───────── 그리기 ─────────

    private void OnGUI()
    {
        if (!showing) return;
        EnsureStyles();

        // 1920x1080 기준 좌표 → 화면 크기에 맞게
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        Vector2 offset = new Vector2((Screen.width - RefW * scale) * 0.5f, (Screen.height - RefH * scale) * 0.5f);
        var oldMatrix = GUI.matrix;

        // 배경 어둡게 (화면 전체)
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.65f));

        GUI.matrix = Matrix4x4.TRS(new Vector3(offset.x, offset.y, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

        var sm = ScoreManager.Instance;

        // 상단: 우승자
        string reason = round != null ? round.MatchEndReason : "";
        Label(new Rect(0, 60, RefW, 60), string.IsNullOrEmpty(reason) ? "MATCH OVER" : $"MATCH OVER - {reason}", sTitle, dimColor);
        if (winnerId >= 0)
        {
            Label(new Rect(0, 125, RefW, 50), "WINNER", sWinnerLabel, gold);
            string name = sm != null ? sm.NameOf(winnerId) : null;
            Label(new Rect(0, 170, RefW, 120), (name ?? "PLAYER").ToUpperInvariant(), sWinner, accent);
        }
        else
        {
            Label(new Rect(0, 170, RefW, 120), "DRAW", sWinner, accent);
        }
        Fill(new Rect(RefW * 0.5f - 300, 302, 600, 3), accent);

        // 순위표
        var rows = sm != null ? sm.GetSorted() : new System.Collections.Generic.List<ScoreManager.PlayerScore>();
        int rowCount = Mathf.Clamp(rows.Count, 1, 8);
        float rowH = 72f;
        float panelX = 410, panelW = 1100, panelY = 340;
        float panelH = 90 + rowCount * rowH + 110;
        Fill(new Rect(panelX, panelY, panelW, panelH), panelColor);
        Outline(new Rect(panelX, panelY, panelW, panelH), accent, 2);

        float[] colX = { 440, 580, 1040, 1180, 1340 };
        string[] heads = { "RANK", "PLAYER", "KILLS", "DEATHS", "ROUNDS" };
        for (int i = 0; i < heads.Length; i++)
            Label(new Rect(colX[i], panelY + 22, 200, 40), heads[i], sHead, dimColor, TextAnchor.MiddleLeft);
        Fill(new Rect(panelX + 20, panelY + 70, panelW - 40, 2), lineColor);

        float y = panelY + 85;
        int rank = 0;
        for (int i = 0; i < rows.Count && i < 8; i++)
        {
            var r = rows[i];
            if (i == 0 || !ScoreManager.SameRank(rows[i - 1], r)) rank = i + 1;   // 같은 기록이면 같은 순위

            if (r.isLocal)
            {
                Fill(new Rect(panelX + 12, y - 4, panelW - 24, rowH - 8), highlightColor);
                Fill(new Rect(panelX + 12, y - 4, 8, rowH - 8), accent);
            }

            Color c = (rank == 1 && winnerId >= 0) ? gold : textColor;
            Label(new Rect(colX[0] + 20, y, 100, rowH - 16), rank.ToString(), sCell, c, TextAnchor.MiddleLeft);
            string pname = (r.name ?? "").ToUpperInvariant();
            Label(new Rect(colX[1], y, 440, rowH - 16), pname, sCell, c, TextAnchor.MiddleLeft);
            if (r.isLocal)
            {
                float w = sCell.CalcSize(new GUIContent(pname)).x;
                Label(new Rect(colX[1] + w + 16, y + 8, 80, rowH - 32), "YOU", sTag, accent, TextAnchor.MiddleLeft);
            }
            Label(new Rect(colX[2] + 20, y, 100, rowH - 16), r.kills.ToString(), sCell, c, TextAnchor.MiddleLeft);
            Label(new Rect(colX[3] + 30, y, 100, rowH - 16), r.deaths.ToString(), sCell, c, TextAnchor.MiddleLeft);
            Label(new Rect(colX[4] + 40, y, 100, rowH - 16), r.roundWins.ToString(), sCell, c, TextAnchor.MiddleLeft);
            y += rowH;
        }

        // 라운드별 승자
        float roundsY = panelY + 90 + rowCount * rowH + 10;
        Fill(new Rect(panelX + 20, roundsY, panelW - 40, 1), lineColor);
        Label(new Rect(colX[0], roundsY + 25, 160, 50), "ROUNDS", sHead, dimColor, TextAnchor.MiddleLeft);

        int total = round != null ? round.TotalRounds : 5;
        var winners = round != null ? round.RoundWinners : null;
        float boxW = Mathf.Min(170f, (panelW - 200f) / Mathf.Max(1, total) - 10f);
        float bx = panelX + 190;
        for (int i = 0; i < total; i++)
        {
            var box = new Rect(bx, roundsY + 20, boxW, 56);
            Fill(box, new Color32(14, 22, 44, 255));
            Outline(box, lineColor, 2);
            Label(new Rect(box.x + 10, box.y + 2, box.width - 12, 24), $"R{i + 1}", sSmall, dimColor, TextAnchor.MiddleLeft);

            string w = "-";
            bool isWinnerName = false;
            if (winners != null && i < winners.Count)
            {
                int id = winners[i];
                w = id >= 0 ? ((sm != null ? sm.NameOf(id) : null) ?? "-").ToUpperInvariant() : "DRAW";
                isWinnerName = id >= 0 && id == winnerId;
            }
            Label(new Rect(box.x + 10, box.y + 24, box.width - 12, 28), w, sSmall, isWinnerName ? accent : textColor, TextAnchor.MiddleLeft);
            bx += boxW + 10;
        }

        // 하단: 카운트다운 + 버튼
        float bottomY = panelY + panelH + 30;
        int left = Mathf.Max(0, Mathf.CeilToInt(returnAt - Time.unscaledTime));
        Label(new Rect(0, bottomY, RefW, 50), $"RETURNING TO MAIN MENU IN {left}", sCount, textColor);

        var btn = new Rect(RefW * 0.5f - 200, bottomY + 65, 400, 70);
        bool hover = btn.Contains(Event.current.mousePosition);
        Fill(btn, hover ? (Color)new Color32(30, 90, 110, 255) : highlightColor);
        Outline(btn, accent, 2);
        Fill(new Rect(btn.x, btn.y, 8, btn.height), accent);
        Label(btn, "MAIN MENU", sButton, hover ? Color.white : textColor);
        if (GUI.Button(btn, GUIContent.none, GUIStyle.none)) GameExit.ToMainMenu();

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

        sTitle = Make(46);
        sWinnerLabel = Make(34);
        sWinner = Make(110);
        sHead = Make(28);
        sCell = Make(40);
        sTag = Make(22);
        sSmall = Make(22);
        sCount = Make(34);
        sButton = Make(36);
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
