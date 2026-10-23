using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 HUD. Player_FPS 루트에 붙인다. 내 플레이어(Health.IsLocal)일 때만 화면에 그림.
///  - 좌측 하단: "HP" 글자 + HP 프레임 이미지(HUD_HP_UI) 안의 막대 (맞으면 잠깐 붉게)
///  - 우측 하단: 탄약 숫자 (남은 탄 / 탄창, 재장전 중엔 흐리게 깜빡임)
///               소나 아이콘(HUD_Sonar_UI) — 쿨타임 동안 시계방향으로 차오르고, 준비되면 밝게 + 아래 Q
///  - 죽었을 때(관전 중)는 숨김
///  - 감도가 바뀌면 ([ ] 키 등) 화면 위쪽 가운데에 "SENSITIVITY 1.1"을 잠깐 표시
/// 캔버스와 UI는 실행 시 코드로 만듦 (프리팹 필요 없음).
/// 이미지를 비워두면 기본 도형(막대 / 원형 링)으로 그림.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHUD : MonoBehaviour
{
    [Header("스타일")]
    [Tooltip("HUD 색")]
    [SerializeField] private Color themeColor = new Color(0.3f, 1f, 0.9f, 1f);
    [Tooltip("비활성(쿨타임 중, 빈 HP 등) 색")]
    [SerializeField] private Color dimColor = new Color(0.3f, 1f, 0.9f, 0.2f);
    [Tooltip("맞았을 때 HP 막대 색")]
    [SerializeField] private Color hitColor = new Color(1f, 0.25f, 0.2f, 1f);
    [Tooltip("HUD 글꼴 (TMP Font Asset). 비우면 TMP 기본 글꼴")]
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("화면 가장자리에서 떨어진 거리 (px, 1920x1080 기준)")]
    [SerializeField] private Vector2 margin = new Vector2(40f, 36f);

    [Header("HP (좌측 하단)")]
    [Tooltip("HP 프레임 이미지 (HUD_HP_UI)")]
    [SerializeField] private Sprite hpFrameSprite;
    [Tooltip("HP 프레임 가로 크기 (px). 세로는 이미지 비율대로")]
    [SerializeField] private float hpWidth = 300f;
    [Tooltip("프레임 안에서 막대가 차지하는 영역 (0~1, 왼쪽 아래 → 오른쪽 위)")]
    [SerializeField] private Vector4 hpFillArea = new Vector4(0.21f, 0.3f, 0.925f, 0.68f);
    [SerializeField] private float hpLabelSize = 18f;

    [Header("소나 / 탄약 (우측 하단)")]
    [Tooltip("소나 아이콘 이미지 (HUD_Sonar_UI)")]
    [SerializeField] private Sprite sonarSprite;
    [SerializeField] private float sonarSize = 76f;
    [SerializeField] private float keyLabelSize = 16f;
    [SerializeField] private float ammoFontSize = 40f;

    [Header("감도 변경 표시 (위쪽 가운데)")]
    [SerializeField] private float toastFontSize = 22f;
    [Tooltip("위쪽 가장자리에서 떨어진 거리 (px)")]
    [SerializeField] private float toastTop = 120f;
    [SerializeField] private float toastHold = 1.5f;
    [SerializeField] private float toastFade = 0.5f;

    private Health health;
    private WeaponSwitcher switcher;
    private SonarSkill sonar;

    private Canvas canvas;
    private Image hpFill, hpBack, hpFrame;
    private TextMeshProUGUI hpLabel;
    private TextMeshProUGUI ammoText;
    private Image sonarFill, sonarBack;
    private TextMeshProUGUI sonarKey;
    private TextMeshProUGUI toastText;
    private float toastUntil = -1f;
    private float lastSensitivity = -1f;

    private float hitFlash;
    private float sonarTotal = 1f;
    private float lastSonarRemaining;
    private int lastAmmo = -1, lastMag = -1;
    private bool lastReloading;

    private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
    private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

    // ───────── 초기화 ─────────

    private void Awake()
    {
        health = GetComponent<Health>();
        switcher = GetComponentInChildren<WeaponSwitcher>(true);
        sonar = GetComponent<SonarSkill>();
    }

    private void OnEnable()
    {
        if (health != null) health.OnDamaged += HandleDamaged;
        GameSettings.Changed += HandleSettingsChanged;
        lastSensitivity = GameSettings.Sensitivity;
    }

    private void OnDisable()
    {
        if (health != null) health.OnDamaged -= HandleDamaged;
        GameSettings.Changed -= HandleSettingsChanged;
        if (canvas != null) canvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void HandleDamaged(Health h, float amount, GameObject attacker) => hitFlash = 1f;

    /// <summary>감도 값이 실제로 바뀌었을 때만 표시 (볼륨 등 다른 설정 변경은 무시)</summary>
    private void HandleSettingsChanged()
    {
        float s = GameSettings.Sensitivity;
        if (Mathf.Approximately(s, lastSensitivity)) return;
        lastSensitivity = s;
        if (toastText != null) toastText.text = "SENSITIVITY " + GameSettings.SensitivityDisplay;
        toastUntil = Time.unscaledTime + toastHold + toastFade;
    }

    private void Build()
    {
        var root = new GameObject("PlayerHUD", typeof(RectTransform));
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Transform t = root.transform;
        Sprite white = MakeWhiteSprite();

        // ── 좌측 하단: HP ──
        float hpHeight = hpFrameSprite != null
            ? hpWidth * hpFrameSprite.rect.height / Mathf.Max(1f, hpFrameSprite.rect.width)
            : 8f;
        if (hpFrameSprite != null)
        {
            hpFrame = MakeImage(t, "HPFrame", hpFrameSprite, themeColor, BottomLeft, margin, new Vector2(hpWidth, hpHeight));
            hpFrame.preserveAspect = true;
            // 막대는 프레임 안 영역에 맞춰 늘어남
            hpBack = MakeFillChild(hpFrame.transform, "HPBack", white, dimColor);
            hpFill = MakeFillChild(hpFrame.transform, "HPFill", white, themeColor);
        }
        else
        {
            hpBack = MakeImage(t, "HPBack", white, dimColor, BottomLeft, margin, new Vector2(hpWidth, hpHeight));
            hpFill = MakeImage(t, "HPFill", white, themeColor, BottomLeft, margin, new Vector2(hpWidth, hpHeight));
        }
        hpFill.type = Image.Type.Filled;
        hpFill.fillMethod = Image.FillMethod.Horizontal;
        hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;

        hpLabel = MakeText(t, "HPLabel", "HP", hpLabelSize, TextAlignmentOptions.BottomLeft, BottomLeft,
                           new Vector2(margin.x + 4f, margin.y + hpHeight + 4f), new Vector2(120f, hpLabelSize + 6f));
        hpLabel.color = themeColor;

        // ── 우측 하단: 소나 아이콘 + Q ──
        float keyH = keyLabelSize + 6f;
        Vector2 sonarPos = new Vector2(-margin.x, margin.y + keyH);
        Vector2 sonarBox = new Vector2(sonarSize, sonarSize);
        Sprite icon = sonarSprite != null ? sonarSprite : MakeRingSprite(128, 10f);
        sonarBack = MakeImage(t, "SonarBack", icon, dimColor, BottomRight, sonarPos, sonarBox);
        sonarFill = MakeImage(t, "SonarFill", icon, themeColor, BottomRight, sonarPos, sonarBox);
        sonarBack.preserveAspect = sonarFill.preserveAspect = true;
        sonarFill.type = Image.Type.Filled;
        sonarFill.fillMethod = Image.FillMethod.Radial360;
        sonarFill.fillOrigin = (int)Image.Origin360.Top;
        sonarFill.fillClockwise = true;
        sonarKey = MakeText(t, "SonarKey", "Q", keyLabelSize, TextAlignmentOptions.Bottom, BottomRight,
                            new Vector2(-margin.x, margin.y), new Vector2(sonarSize, keyH));

        // ── 위쪽 가운데: 감도 변경 표시 ──
        toastText = MakeText(t, "SensitivityToast", "", toastFontSize, TextAlignmentOptions.Top, new Vector2(0.5f, 1f),
                             new Vector2(0f, -toastTop), new Vector2(600f, toastFontSize + 10f));
        toastText.text = "SENSITIVITY " + GameSettings.SensitivityDisplay;
        toastText.alpha = 0f;

        // ── 우측 하단: 탄약 (소나 왼쪽) ──
        float ammoRight = margin.x + sonarSize + 24f;
        ammoText = MakeText(t, "Ammo", "", ammoFontSize, TextAlignmentOptions.BottomRight, BottomRight,
                            new Vector2(-ammoRight, margin.y + keyH), new Vector2(240f, sonarSize));
    }

    private Image MakeFillChild(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(hpFillArea.x, hpFillArea.y);
        rt.anchorMax = new Vector2(hpFillArea.z, hpFillArea.w);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    // ───────── 매 프레임 ─────────

    private void LateUpdate()
    {
        bool show = health != null && health.IsLocal && !health.IsDead;
        if (show && canvas == null) Build();
        if (canvas == null) return;
        canvas.enabled = show;
        if (!show) return;

        // HP
        float hp = health.Max > 0f ? Mathf.Clamp01(health.Current / health.Max) : 0f;
        hpFill.fillAmount = hp;
        hitFlash = Mathf.MoveTowards(hitFlash, 0f, Time.deltaTime * 3f);
        Color hc = Color.Lerp(themeColor, hitColor, hitFlash);
        hpFill.color = hc;
        if (hpFrame != null) hpFrame.color = hc;
        hpLabel.color = hc;

        // 감도 변경 표시 (유지 후 서서히 사라짐)
        float left = toastUntil - Time.unscaledTime;
        toastText.alpha = left <= 0f ? 0f : Mathf.Clamp01(left / Mathf.Max(0.01f, toastFade));

        // 탄약
        var gun = switcher != null ? switcher.Current : null;
        if (gun != null)
        {
            if (gun.Ammo != lastAmmo || gun.MagazineSize != lastMag)
            {
                lastAmmo = gun.Ammo;
                lastMag = gun.MagazineSize;
                ammoText.text = $"{gun.Ammo}<size=50%><alpha=#66> / {gun.MagazineSize}</size>";
            }
            if (gun.IsReloading)
            {
                float a = 0.35f + 0.25f * Mathf.Sin(Time.time * 10f);
                ammoText.color = new Color(themeColor.r, themeColor.g, themeColor.b, a);
            }
            else if (lastReloading || ammoText.color != themeColor) ammoText.color = gun.Ammo > 0 ? themeColor : hitColor;
            lastReloading = gun.IsReloading;
        }
        else ammoText.text = "";

        // 소나
        if (sonar != null)
        {
            float remain = sonar.CooldownRemaining;
            if (remain > lastSonarRemaining + 0.01f) sonarTotal = Mathf.Max(0.01f, remain);   // 새 쿨타임 시작
            lastSonarRemaining = remain;

            if (sonar.IsLockedByRound)
            {
                sonarFill.fillAmount = 0f;
                sonarKey.color = dimColor;
            }
            else if (sonar.IsReady)
            {
                sonarFill.fillAmount = 1f;
                sonarFill.color = themeColor;
                sonarKey.color = themeColor;
            }
            else
            {
                sonarFill.fillAmount = 1f - Mathf.Clamp01(remain / sonarTotal);
                sonarFill.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.55f);
                sonarKey.color = dimColor;
            }
        }
        else
        {
            sonarFill.enabled = sonarBack.enabled = sonarKey.enabled = false;
        }
    }

    // ───────── UI 생성 도우미 ─────────

    private static Image MakeImage(Transform parent, string name, Sprite sprite, Color color, Vector2 corner, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = corner;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private TextMeshProUGUI MakeText(Transform parent, string name, string text, float size, TextAlignmentOptions align, Vector2 corner, Vector2 pos, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = corner;
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.characterSpacing = 4f;
        t.raycastTarget = false;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.color = themeColor;
        return t;
    }

    private static Sprite MakeWhiteSprite()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
    }

    /// <summary>원형 테두리(링) 스프라이트 (소나 이미지가 없을 때 대신 사용)</summary>
    private static Sprite MakeRingSprite(int size, float thickness)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float outer = size * 0.5f - 1f;
        float inner = outer - thickness;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
