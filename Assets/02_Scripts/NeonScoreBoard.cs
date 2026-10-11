using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// ScoreManager 기록을 네온 전광판에 표시.
/// ListText 하나에 여러 줄로 출력하고, <pos> 태그로 열을 정렬한다.
/// </summary>
public class NeonScoreboard : MonoBehaviour
{
    [Header("텍스트")]
    [SerializeField] private TMP_Text headerText;
    [SerializeField] private TMP_Text listText;

    [Header("표시")]
    [SerializeField, Min(1)] private int maxRows = 8;
    [SerializeField, Min(3)] private int maxNameLength = 10;

    [Header("열 위치 (%)")]
    [SerializeField] private float rankPos = 0f;
    [SerializeField] private float namePos = 10f;
    [SerializeField] private float killPos = 70f;
    [SerializeField] private float deathPos = 85f;

    [Header("색")]
    [SerializeField] private Color normalColor = new Color(0.6f, 0.9f, 1f);
    [SerializeField] private Color leaderColor = new Color(1f, 0.25f, 0.35f);
    [SerializeField] private Color localColor = new Color(1f, 0.85f, 0.3f);

    [Header("갱신 연출")]
    [SerializeField] private bool flashOnChange = true;
    [SerializeField] private Light glowLight;
    [SerializeField] private int flashCount = 2;
    [SerializeField] private float flashInterval = 0.06f;

    private bool subscribed;
    private Coroutine flashRoutine;
    private float glowBaseIntensity;

    private void Awake()
    {
        if (glowLight != null) glowBaseIntensity = glowLight.intensity;
    }

    private void OnEnable()  => TrySubscribe();
    private void Start()     { TrySubscribe(); Refresh(false); }

    private void OnDisable()
    {
        if (subscribed && ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreChanged -= HandleChanged;
        subscribed = false;
    }

    private void TrySubscribe()
    {
        if (subscribed || ScoreManager.Instance == null) return;
        ScoreManager.Instance.OnScoreChanged += HandleChanged;
        subscribed = true;
    }

    private void HandleChanged() => Refresh(flashOnChange);

    private void Refresh(bool flash)
    {
        if (headerText != null)
        {
            headerText.text =
                $"<pos={rankPos}%>#<pos={namePos}%>NAME<pos={killPos}%>K<pos={deathPos}%>D";
        }

        if (listText == null || ScoreManager.Instance == null) return;

        var sorted = ScoreManager.Instance.GetSorted();
        var sb = new StringBuilder();
        int rows = Mathf.Min(maxRows, sorted.Count);

        for (int i = 0; i < rows; i++)
        {
            var s = sorted[i];
            Color c = s.isLocal ? localColor : (i == 0 && s.kills > 0 ? leaderColor : normalColor);
            string name = s.name.Length > maxNameLength ? s.name.Substring(0, maxNameLength) : s.name;

            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(c)}>")
              .Append($"<pos={rankPos}%>{i + 1}")
              .Append($"<pos={namePos}%>{name}")
              .Append($"<pos={killPos}%>{s.kills}")
              .Append($"<pos={deathPos}%>{s.deaths}")
              .Append("</color>");
            if (i < rows - 1) sb.Append('\n');
        }

        listText.text = sb.ToString();

        if (flash && isActiveAndEnabled)
        {
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(Flash());
        }
    }

    private IEnumerator Flash()
    {
        for (int i = 0; i < flashCount; i++)
        {
            SetVisible(false);
            yield return new WaitForSeconds(flashInterval);
            SetVisible(true);
            yield return new WaitForSeconds(flashInterval);
        }
        flashRoutine = null;
    }

    private void SetVisible(bool on)
    {
        if (listText != null) listText.enabled = on;
        if (glowLight != null) glowLight.intensity = on ? glowBaseIntensity : glowBaseIntensity * 0.3f;
    }
}