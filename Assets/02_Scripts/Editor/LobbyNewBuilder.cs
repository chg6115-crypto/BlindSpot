#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Lobby_New 씬 자동 구성 (메뉴: BlindSpot > Build Lobby_New)
///  - 카메라 / EventSystem / NetworkManager(+Unity Transport) / Managers(LobbyManager, LobbyBGM)
///  - Canvas: 배경, 타이틀, 패널 5개(Main/Create/RoomList/Room/Settings), StatusText
///  - RoomListItem_New 프리팹 생성, LobbyManager / SettingsPanelUI 칸 자동 연결
///  - Player_FPS 프리팹에 MouseSensitivityApplier 추가
///  - Build Profiles 씬 목록: 0 Lobby_New, 1 BlindSpot_Warehouse (기존 Lobby는 체크 해제)
/// Lobby_New 씬을 연 상태에서 실행. Canvas가 이미 있으면 UI는 다시 만들지 않음 (지우고 다시 실행하면 새로 생성).
/// </summary>
public static class LobbyNewBuilder
{
    const string SceneName = "Lobby_New";
    const string FontTMP = "Assets/13_Fonts/Cybrpnuk2_TMP.asset";
    const string ItemPrefabPath = "Assets/03_Prefabs/RoomListItem_New.prefab";
    const string PlayerPrefabPath = "Assets/03_Prefabs/Player_FPS.prefab";
    const string BgmFolder = "Assets/08_Audio/BGM";
    const string MixerPath = "Assets/Resources/MainMixer.mixer";
    const string WarehouseScene = "Assets/01_Scene/BlindSpot_Warehouse.unity";
    const string OldLobbyScene = "Assets/01_Scene/Lobby.unity";

    static readonly Color Cyan = new Color32(64, 230, 240, 255);
    static readonly Color Dim = new Color32(170, 185, 215, 255);
    static readonly Color White = new Color32(235, 240, 255, 255);
    static readonly Color PanelBg = new Color32(10, 16, 34, 215);
    static readonly Color ScreenBg = new Color32(8, 10, 22, 255);

    static TMP_FontAsset font;
    static readonly List<string> log = new List<string>();

    [MenuItem("BlindSpot/Build Lobby_New")]
    static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != SceneName)
        {
            EditorUtility.DisplayDialog("Build Lobby_New", SceneName + " 씬을 열고 실행하세요.", "OK");
            return;
        }
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontTMP);
        if (font == null)
        {
            EditorUtility.DisplayDialog("Build Lobby_New", "폰트를 찾지 못했습니다: " + FontTMP, "OK");
            return;
        }
        log.Clear();

        var roots = scene.GetRootGameObjects();
        GameObject Root(string n) => roots.FirstOrDefault(g => g.name == n);

        // ── 카메라 ──
        var cam = Object.FindAnyObjectByType<Camera>();
        if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); log.Add("+ Main Camera"); }
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ScreenBg;
        if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();

        // ── EventSystem ──
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            log.Add("+ EventSystem (Input System)");
        }

        // ── NetworkManager ──
        var nm = Object.FindAnyObjectByType<NetworkManager>();
        if (nm == null)
        {
            var go = new GameObject("NetworkManager");
            nm = go.AddComponent<NetworkManager>();
            log.Add("+ NetworkManager");
        }
        var utp = nm.GetComponent<UnityTransport>();
        if (utp == null) utp = nm.gameObject.AddComponent<UnityTransport>();
        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = utp;
        nm.NetworkConfig.EnableSceneManagement = true;
        var defaultPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
        if (defaultPrefabs != null && !nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(defaultPrefabs))
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(defaultPrefabs);
        EditorUtility.SetDirty(nm);

        // ── Managers ──
        var managers = Root("Managers") ?? new GameObject("Managers");
        var lmGo = managers.transform.Find("LobbyManager")?.gameObject;
        if (lmGo == null) { lmGo = new GameObject("LobbyManager"); lmGo.transform.SetParent(managers.transform, false); }
        var lm = lmGo.GetComponent<LobbyManager>();
        if (lm == null) lm = lmGo.AddComponent<LobbyManager>();

        var bgmGo = managers.transform.Find("LobbyBGM")?.gameObject;
        if (bgmGo == null) { bgmGo = new GameObject("LobbyBGM"); bgmGo.transform.SetParent(managers.transform, false); log.Add("+ LobbyBGM"); }
        var src = bgmGo.GetComponent<AudioSource>();
        if (src == null) src = bgmGo.AddComponent<AudioSource>();
        if (src.clip == null)
        {
            var guid = AssetDatabase.FindAssets("t:AudioClip", new[] { BgmFolder }).FirstOrDefault();
            if (guid != null) src.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            else log.Add("! BGM 파일 없음 (" + BgmFolder + ")");
        }
        src.playOnAwake = true;
        src.loop = true;
        src.volume = 0.5f;
        src.spatialBlend = 0f;
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (mixer != null)
        {
            var music = mixer.FindMatchingGroups("Music").FirstOrDefault();
            if (music != null) { src.outputAudioMixerGroup = music; log.Add("* LobbyBGM → Music 그룹"); }
            else log.Add("! MainMixer에 Music 그룹 없음");
        }
        else log.Add("! " + MixerPath + " 없음 → Music/SFX 볼륨은 믹서를 만든 뒤 적용됨");

        // ── UI ──
        var canvasGo = Root("Canvas");
        if (canvasGo != null)
        {
            log.Add("= Canvas가 이미 있어 UI 생성은 건너뜀 (새로 만들려면 Canvas 삭제 후 다시 실행)");
        }
        else
        {
            var itemPrefab = EnsureItemPrefab();
            BuildUI(lm, itemPrefab);
        }

        // ── Player_FPS 에 감도 적용기 ──
        AddSensitivityApplier();

        // ── Build Profiles 씬 목록 ──
        UpdateBuildScenes(scene.path);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string msg = string.Join("\n", log);
        Debug.Log("[LobbyNewBuilder]\n" + msg);
        EditorUtility.DisplayDialog("Build Lobby_New 완료", msg + "\n\n씬 저장됨.", "OK");
    }

    // ───────── UI 전체 ─────────

    static void BuildUI(LobbyManager lm, RoomListItem itemPrefab)
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.layer = 5;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var c = canvasGo.transform;

        // 배경
        var bg = MakeRect("Background", c);
        Stretch(bg);
        bg.gameObject.AddComponent<Image>().color = ScreenBg;

        // 타이틀
        var title = MakeText(c, "Title", "BLINDSPOT", 96, Cyan, TextAlignmentOptions.TopLeft);
        TopLeft(title.rectTransform, 90, 60, 900, 120);
        var line = MakeRect("TitleLine", c);
        TopLeft(line, 92, 178, 470, 3);
        line.gameObject.AddComponent<Image>().color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);

        // ── Main ──
        var main = Panel(c, "MainPanel");
        MakeText(main, "NicknameLabel", "NICKNAME", 22, Dim, TextAlignmentOptions.Left);
        var nick = MakeInput(main, "NicknameInput", "NICKNAME", 60);
        nick.characterLimit = 12;
        nick.contentType = TMP_InputField.ContentType.Alphanumeric;
        MakeText(main, "NicknameHint", "4-12 LETTERS OR NUMBERS", 16, new Color32(110, 125, 160, 255), TextAlignmentOptions.Left);
        var single = MakeButton(main, "SinglePlayButton", "SINGLE PLAY", 70, TextAlignmentOptions.Left);
        var createRoom = MakeButton(main, "CreateRoomButton", "CREATE ROOM", 70, TextAlignmentOptions.Left);
        var findRoom = MakeButton(main, "FindRoomButton", "FIND ROOM", 70, TextAlignmentOptions.Left);
        var settings = MakeButton(main, "SettingsButton", "SETTINGS", 70, TextAlignmentOptions.Left);
        var quit = MakeButton(main, "QuitButton", "QUIT", 70, TextAlignmentOptions.Left);

        // ── Create ──
        var create = Panel(c, "CreatePanel");
        MakeText(create, "Title", "CREATE ROOM", 44, Cyan, TextAlignmentOptions.Left);
        var nameRow = Row(create, "RoomNameRow", 34);
        Flexible(MakeText(nameRow, "Label", "ROOM NAME", 22, Dim, TextAlignmentOptions.Left));
        var createName = MakeText(nameRow, "RoomNameText", "PLAYER01", 22, White, TextAlignmentOptions.Right);
        Flexible(createName);
        var privRow = Row(create, "PrivateRow", 40);
        Flexible(MakeText(privRow, "Label", "PRIVATE (CODE ONLY)", 24, Dim, TextAlignmentOptions.Left));
        var privToggle = MakeToggle(privRow, "PrivateToggle");
        Space(create, 6);
        var createBtn = MakeButton(create, "CreateButton", "CREATE", 70, TextAlignmentOptions.Center);
        var createBack = MakeButton(create, "BackButton", "BACK", 64, TextAlignmentOptions.Center);

        // ── Room List ──
        var list = Panel(c, "RoomListPanel");
        MakeText(list, "Title", "ROOMS", 44, Cyan, TextAlignmentOptions.Left);
        var content = ScrollList(list, "RoomList", 300);
        var codeRow = Row(list, "CodeRow", 58);
        var code = MakeInput(codeRow, "CodeInput", "ROOM CODE", 58);
        code.contentType = TMP_InputField.ContentType.Alphanumeric;
        code.characterLimit = 10;
        Flexible(code, 2.2f);
        var join = MakeButton(codeRow, "JoinCodeButton", "JOIN", 58, TextAlignmentOptions.Center);
        Flexible(join, 1f);
        var listBtnRow = Row(list, "ButtonRow", 64);
        var refresh = MakeButton(listBtnRow, "RefreshButton", "REFRESH", 64, TextAlignmentOptions.Center);
        Flexible(refresh);
        var listBack = MakeButton(listBtnRow, "BackButton", "BACK", 64, TextAlignmentOptions.Center);
        Flexible(listBack);

        // ── Room ──
        var room = Panel(c, "RoomPanel");
        var roomName = MakeText(room, "RoomNameText", "PLAYER01", 44, Cyan, TextAlignmentOptions.Left);
        var codeInfoRow = Row(room, "CodeInfoRow", 34);
        var roomCode = MakeText(codeInfoRow, "RoomCodeText", "CODE: ------", 26, White, TextAlignmentOptions.Left);
        Flexible(roomCode, 2f);
        var privacy = MakeText(codeInfoRow, "PrivacyText", "PUBLIC", 20, Dim, TextAlignmentOptions.Right);
        Flexible(privacy, 1f);
        Divider(room);
        var count = MakeText(room, "PlayersCountText", "PLAYERS 1 / 4", 24, Dim, TextAlignmentOptions.Left);
        var players = MakeText(room, "PlayersText", "PLAYER01\n- EMPTY -", 28, White, TextAlignmentOptions.TopLeft);
        players.lineSpacing = 10;
        players.richText = true;
        Height(players.gameObject, 190);
        var roomBtnRow = Row(room, "ButtonRow", 64);
        var start = MakeButton(roomBtnRow, "StartButton", "START", 64, TextAlignmentOptions.Center);
        Flexible(start);
        var leave = MakeButton(roomBtnRow, "LeaveButton", "LEAVE", 64, TextAlignmentOptions.Center);
        Flexible(leave);

        // ── Settings ──
        var set = Panel(c, "SettingsPanel");
        MakeText(set, "Title", "SETTINGS", 44, Cyan, TextAlignmentOptions.Left);
        var (master, masterVal) = SliderRow(set, "MasterRow", "MASTER VOLUME");
        var (musicS, musicVal) = SliderRow(set, "MusicRow", "MUSIC");
        var (sfxS, sfxVal) = SliderRow(set, "SfxRow", "SFX");
        var (sens, sensVal) = SliderRow(set, "SensitivityRow", "MOUSE SENSITIVITY");
        var fullRow = Row(set, "FullscreenRow", 40);
        Flexible(MakeText(fullRow, "Label", "FULLSCREEN", 24, Dim, TextAlignmentOptions.Left));
        var fullToggle = MakeToggle(fullRow, "FullscreenToggle");
        Space(set, 6);
        var setBtnRow = Row(set, "ButtonRow", 64);
        var reset = MakeButton(setBtnRow, "ResetButton", "RESET", 64, TextAlignmentOptions.Center);
        Flexible(reset);
        var setBack = MakeButton(setBtnRow, "BackButton", "BACK", 64, TextAlignmentOptions.Center);
        Flexible(setBack);

        var spui = set.gameObject.AddComponent<SettingsPanelUI>();
        var sso = new SerializedObject(spui);
        sso.FindProperty("masterSlider").objectReferenceValue = master;
        sso.FindProperty("musicSlider").objectReferenceValue = musicS;
        sso.FindProperty("sfxSlider").objectReferenceValue = sfxS;
        sso.FindProperty("sensitivitySlider").objectReferenceValue = sens;
        sso.FindProperty("masterValue").objectReferenceValue = masterVal;
        sso.FindProperty("musicValue").objectReferenceValue = musicVal;
        sso.FindProperty("sfxValue").objectReferenceValue = sfxVal;
        sso.FindProperty("sensitivityValue").objectReferenceValue = sensVal;
        sso.FindProperty("fullscreenToggle").objectReferenceValue = fullToggle;
        sso.FindProperty("resetButton").objectReferenceValue = reset;
        sso.ApplyModifiedPropertiesWithoutUndo();

        // 상태 메시지
        var status = MakeText(c, "StatusText", "", 26, new Color32(200, 210, 235, 255), TextAlignmentOptions.Center);
        var srt = status.rectTransform;
        srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0); srt.pivot = new Vector2(0.5f, 0);
        srt.anchoredPosition = new Vector2(0, 25); srt.sizeDelta = new Vector2(-40, 50);

        // 처음엔 Main만 보이게
        create.gameObject.SetActive(false);
        list.gameObject.SetActive(false);
        room.gameObject.SetActive(false);
        set.gameObject.SetActive(false);

        // ── LobbyManager 연결 ──
        var so = new SerializedObject(lm);
        void A(string f, Object v) { var p = so.FindProperty(f); if (p == null) log.Add("! 필드 없음: " + f); else p.objectReferenceValue = v; }
        A("mainPanel", main.gameObject); A("createPanel", create.gameObject); A("roomListPanel", list.gameObject);
        A("roomPanel", room.gameObject); A("settingsPanel", set.gameObject);
        A("nicknameInput", nick); A("singleButton", single); A("createRoomButton", createRoom);
        A("findRoomButton", findRoom); A("settingsButton", settings); A("quitButton", quit);
        A("createRoomNameText", createName); A("privateToggle", privToggle); A("createButton", createBtn); A("createBackButton", createBack);
        A("roomListContent", content); A("roomListItemPrefab", itemPrefab); A("codeInput", code);
        A("joinCodeButton", join); A("refreshButton", refresh); A("roomListBackButton", listBack);
        A("roomNameText", roomName); A("roomCodeText", roomCode); A("roomPrivacyText", privacy);
        A("playersCountText", count); A("playersText", players); A("startButton", start); A("leaveButton", leave);
        A("settingsBackButton", setBack); A("statusText", status);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(lm);

        log.Add("+ Canvas / 패널 5개 / StatusText 생성, LobbyManager 연결 완료");
    }

    // ───────── 프리팹 ─────────

    static RoomListItem EnsureItemPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<RoomListItem>(ItemPrefabPath);
        if (existing != null) { log.Add("= 이미 있음: RoomListItem_New"); return existing; }

        var temp = new GameObject("Temp", typeof(RectTransform));
        var btn = MakeButton(temp.transform, "RoomListItem", "ROOM<pos=78%>1 / 4", 56, TextAlignmentOptions.Left);
        var label = btn.GetComponentInChildren<TMP_Text>();
        label.fontSize = 26;
        label.richText = true;
        var go = btn.gameObject;
        go.transform.SetParent(null, false);
        Object.DestroyImmediate(temp);

        var item = go.AddComponent<RoomListItem>();
        var so = new SerializedObject(item);
        so.FindProperty("nameText").objectReferenceValue = label;
        so.FindProperty("joinButton").objectReferenceValue = btn;
        so.ApplyModifiedPropertiesWithoutUndo();

        var saved = PrefabUtility.SaveAsPrefabAsset(go, ItemPrefabPath);
        Object.DestroyImmediate(go);
        log.Add("+ 프리팹 생성: " + ItemPrefabPath);
        return saved.GetComponent<RoomListItem>();
    }

    static void AddSensitivityApplier()
    {
        var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null) { log.Add("! Player_FPS 프리팹 없음"); return; }
        try
        {
            if (root.GetComponentInChildren<MouseSensitivityApplier>(true) != null)
            {
                log.Add("= Player_FPS 에 MouseSensitivityApplier 이미 있음");
                return;
            }
            var fpc = root.GetComponentsInChildren<MonoBehaviour>(true)
                          .FirstOrDefault(m => m != null && m.GetType().Name == "FirstPersonController");
            var target = fpc != null ? fpc.gameObject : root;
            target.AddComponent<MouseSensitivityApplier>();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            log.Add("+ Player_FPS 에 MouseSensitivityApplier 추가 (" + target.name + ")");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void UpdateBuildScenes(string lobbyPath)
    {
        var list = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(lobbyPath, true),
            new EditorBuildSettingsScene(WarehouseScene, true)
        };
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.path == lobbyPath || s.path == WarehouseScene) continue;
            list.Add(new EditorBuildSettingsScene(s.path, false));   // 나머지(기존 Lobby 등)는 체크 해제로 유지
        }
        if (!list.Any(s => s.path == OldLobbyScene) && System.IO.File.Exists(OldLobbyScene))
            list.Add(new EditorBuildSettingsScene(OldLobbyScene, false));
        EditorBuildSettings.scenes = list.ToArray();
        log.Add("* 빌드 씬 목록: 0 " + SceneName + ", 1 BlindSpot_Warehouse (나머지 체크 해제)");
    }

    // ───────── UI 부품 ─────────

    static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void TopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align)
    {
        var rt = MakeRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        t.fontSharedMaterial = font.material;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.richText = true;
        return t;
    }

    static Transform Panel(Transform canvas, string name)
    {
        var rt = MakeRect(name, canvas);
        TopLeft(rt, 80, 250, 520, 600);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = PanelBg;
        var ol = rt.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f);
        ol.effectDistance = new Vector2(2, -2);
        var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(40, 40, 36, 40);
        vlg.spacing = 18;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var csf = rt.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    static Transform Row(Transform parent, string name, float height)
    {
        var rt = MakeRect(name, parent);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 20;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        Height(rt.gameObject, height);
        return rt;
    }

    static void Space(Transform parent, float h)
    {
        Height(MakeRect("Space", parent).gameObject, h);
    }

    static void Divider(Transform parent)
    {
        var rt = MakeRect("Divider", parent);
        rt.gameObject.AddComponent<Image>().color = new Color32(60, 90, 130, 255);
        Height(rt.gameObject, 2);
    }

    static void Height(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = h;
        le.preferredHeight = h;
    }

    static void Flexible(Component c, float weight = 1f)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = weight;
        le.preferredWidth = 0;
    }

    static Button MakeButton(Transform parent, string name, string label, float height, TextAlignmentOptions align)
    {
        var rt = MakeRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color32(16, 24, 46, 230);
        var ol = rt.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color32(60, 90, 130, 255);
        ol.effectDistance = new Vector2(2, -2);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        Height(rt.gameObject, height);

        var bar = MakeRect("Accent", rt);
        bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(0, 1);
        bar.pivot = new Vector2(0, 0.5f);
        bar.anchoredPosition = Vector2.zero; bar.sizeDelta = new Vector2(6, 0);
        var barImg = bar.gameObject.AddComponent<Image>();
        barImg.color = Cyan;
        barImg.raycastTarget = false;
        barImg.enabled = false;

        var t = MakeText(rt, "Label", label, 32, Cyan, align);
        Stretch(t.rectTransform);
        t.margin = new Vector4(align == TextAlignmentOptions.Left ? 30 : 10, 0, 16, 0);

        var hs = rt.gameObject.AddComponent<UIHoverStyle>();
        hs.background = img; hs.outline = ol; hs.accentBar = barImg; hs.label = t;
        return btn;
    }

    static TMP_InputField MakeInput(Transform parent, string name, string placeholder, float height)
    {
        var go = TMP_DefaultControls.CreateInputField(TmpRes());
        go.name = name;
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = null;
        img.color = new Color(0, 0, 0, 0.67f);
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color32(90, 110, 150, 255);
        ol.effectDistance = new Vector2(2, -2);
        var input = go.GetComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        if (input.textComponent != null)
        {
            input.textComponent.font = font;
            input.textComponent.fontSharedMaterial = font.material;
            input.textComponent.fontSize = 28;
            input.textComponent.color = White;
            input.textComponent.alignment = TextAlignmentOptions.Left;
        }
        if (input.placeholder is TMP_Text ph)
        {
            ph.text = placeholder;
            ph.font = font;
            ph.fontSharedMaterial = font.material;
            ph.fontSize = 26;
            ph.color = new Color32(120, 130, 160, 255);
            ph.fontStyle = FontStyles.Normal;
            ph.alignment = TextAlignmentOptions.Left;
        }
        var area = go.transform.Find("Text Area") as RectTransform;
        if (area != null) { area.offsetMin = new Vector2(16, 4); area.offsetMax = new Vector2(-16, -4); }
        Height(go, height);
        return input;
    }

    static Toggle MakeToggle(Transform parent, string name)
    {
        var go = DefaultControls.CreateToggle(UiRes());
        go.name = name;
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var label = go.transform.Find("Label");
        if (label != null) Object.DestroyImmediate(label.gameObject);

        var toggle = go.GetComponent<Toggle>();
        var bgRt = go.transform.Find("Background") as RectTransform;
        if (bgRt != null)
        {
            Stretch(bgRt);
            var bgImg = bgRt.GetComponent<Image>();
            bgImg.sprite = null;
            bgImg.color = new Color(0, 0, 0, 0.67f);
            var ol = bgRt.gameObject.AddComponent<Outline>();
            ol.effectColor = Cyan;
            ol.effectDistance = new Vector2(2, -2);
            var check = bgRt.Find("Checkmark") as RectTransform;
            if (check != null)
            {
                check.anchorMin = Vector2.zero; check.anchorMax = Vector2.one;
                check.offsetMin = new Vector2(8, 8); check.offsetMax = new Vector2(-8, -8);
                var ci = check.GetComponent<Image>();
                ci.sprite = null;
                ci.color = Cyan;
            }
        }
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 34;
        le.minHeight = le.preferredHeight = 34;
        le.flexibleWidth = 0;
        toggle.isOn = false;
        return toggle;
    }

    static (Slider, TMP_Text) SliderRow(Transform parent, string name, string label)
    {
        var box = MakeRect(name, parent);
        var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        var head = Row(box, "Header", 30);
        Flexible(MakeText(head, "Label", label, 24, Dim, TextAlignmentOptions.Left), 3f);
        var val = MakeText(head, "Value", "0", 24, White, TextAlignmentOptions.Right);
        Flexible(val, 1f);

        var go = DefaultControls.CreateSlider(UiRes());
        go.name = "Slider";
        go.layer = 5;
        go.transform.SetParent(box, false);
        Height(go, 24);
        var slider = go.GetComponent<Slider>();
        slider.minValue = 0; slider.maxValue = 100; slider.wholeNumbers = true;

        var bg = go.transform.Find("Background")?.GetComponent<Image>();
        if (bg != null) { bg.sprite = null; bg.color = new Color32(40, 55, 85, 255); }
        var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
        if (fill != null) { fill.sprite = null; fill.color = Cyan; }
        var handleRt = go.transform.Find("Handle Slide Area/Handle") as RectTransform;
        if (handleRt != null)
        {
            handleRt.sizeDelta = new Vector2(18, 0);
            var hi = handleRt.GetComponent<Image>();
            hi.sprite = null;
            hi.color = White;
        }
        return (slider, val);
    }

    static Transform ScrollList(Transform parent, string name, float height)
    {
        var go = DefaultControls.CreateScrollView(UiRes());
        go.name = name;
        go.layer = 5;
        go.transform.SetParent(parent, false);
        Height(go, height);

        var sr = go.GetComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        if (sr.horizontalScrollbar != null)
        {
            Object.DestroyImmediate(sr.horizontalScrollbar.gameObject);
            sr.horizontalScrollbar = null;
        }
        var bg = go.GetComponent<Image>();
        bg.sprite = null;
        bg.color = new Color(0, 0, 0, 0.45f);

        if (sr.verticalScrollbar != null)
        {
            var sbImg = sr.verticalScrollbar.GetComponent<Image>();
            if (sbImg != null) { sbImg.sprite = null; sbImg.color = new Color32(40, 55, 85, 255); }
            var handle = sr.verticalScrollbar.handleRect != null ? sr.verticalScrollbar.handleRect.GetComponent<Image>() : null;
            if (handle != null) { handle.sprite = null; handle.color = Cyan; }
            var sbRt = (RectTransform)sr.verticalScrollbar.transform;
            sbRt.sizeDelta = new Vector2(10, sbRt.sizeDelta.y);
        }

        var content = sr.content;
        content.pivot = new Vector2(0.5f, 1f);
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.padding = new RectOffset(8, 18, 8, 8);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var csf = content.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return content;
    }

    static TMP_DefaultControls.Resources TmpRes() => new TMP_DefaultControls.Resources
    {
        standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
        background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
        inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
        knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
        checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
        dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
        mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
    };

    static DefaultControls.Resources UiRes() => new DefaultControls.Resources
    {
        standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
        background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
        inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
        knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
        checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
        dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
        mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
    };
}
#endif
