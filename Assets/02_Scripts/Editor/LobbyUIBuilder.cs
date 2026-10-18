#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lobby 씬 UI 자동 정리 (메뉴: BlindSpot > Build Lobby UI)
///  1) NicknameInput / CodeInput 을 TMP 입력칸으로 교체
///  2) RoomListWindow 에 RoomList 스크롤뷰 추가
///  3) RoomListItem 프리팹 생성 (03_Prefabs)
///  4) CreateRoom / FindRoom / Back 버튼 클릭 연결 정리
///  5) LobbyManager 오브젝트 생성 + Inspector 칸 전부 연결
///  6) 씬의 모든 글씨체를 Cybrpnuk2 로 통일 (TMP / 일반 Text)
/// Lobby 씬을 연 상태에서 실행. 여러 번 실행해도 이미 된 부분은 건너뜀.
/// </summary>
public static class LobbyUIBuilder
{
    const string FontTTF = "Assets/13_Fonts/Cybrpnuk2.ttf";
    const string FontTMP = "Assets/13_Fonts/Cybrpnuk2_TMP.asset";
    const string ButtonPrefabPath = "Assets/Unity UI Samples/Prefabs/SF Button.prefab";
    const string ItemPrefabPath = "Assets/03_Prefabs/RoomListItem.prefab";

    static readonly List<string> log = new List<string>();
    static UnityEngine.SceneManagement.Scene scene;

    [MenuItem("BlindSpot/Build Lobby UI")]
    static void Build()
    {
        scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Lobby")
        {
            EditorUtility.DisplayDialog("Build Lobby UI", "Lobby 씬을 열고 실행하세요.", "OK");
            return;
        }

        var ttf = AssetDatabase.LoadAssetAtPath<Font>(FontTTF);
        var tmp = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontTMP);
        if (ttf == null || tmp == null)
        {
            EditorUtility.DisplayDialog("Build Lobby UI", "폰트를 찾지 못했습니다:\n" + FontTTF + "\n" + FontTMP, "OK");
            return;
        }

        log.Clear();

        // ── 1) 입력칸 교체 ──
        var nickInput = EnsureInputField("Canvas/MainMenu/Window/NicknameInput", "NICKNAME", 12, tmp);
        var codeInput = EnsureInputField("Canvas/RoomListMenu/RoomListWindow/CodeInput", "ROOM CODE", 0, tmp);

        // ── 2) 방 목록 스크롤뷰 ──
        var content = EnsureScrollView("Canvas/RoomListMenu/RoomListWindow", "RoomList");

        // ── 3) RoomListItem 프리팹 ──
        var itemPrefab = EnsureItemPrefab(tmp, ttf);

        // ── 4) 버튼 연결 ──
        var pm = Find("Canvas/MenuManager")?.GetComponent<PanelManager>();
        var mainMenu = Find("Canvas/MainMenu")?.GetComponent<Animator>();
        var createMenu = Find("Canvas/CreateMenu")?.GetComponent<Animator>();
        var roomListMenu = Find("Canvas/RoomListMenu")?.GetComponent<Animator>();
        var roomMenu = Find("Canvas/RoomMenu")?.GetComponent<Animator>();

        if (pm != null)
        {
            SetOpenPanel("Canvas/MainMenu/Window/CreateRoom", pm, createMenu);
            SetOpenPanel("Canvas/MainMenu/Window/FindRoom", pm, roomListMenu);
            SetOpenPanel("Canvas/CreateMenu/CreateWindow/Back", pm, mainMenu);
            SetOpenPanel("Canvas/RoomListMenu/RoomListWindow/Back", pm, mainMenu);
        }
        else log.Add("! Canvas/MenuManager 의 PanelManager 를 찾지 못함 (버튼 연결 건너뜀)");

        // 버튼 / 제목 글자
        SetLabel("Canvas/MainMenu/Window/CreateRoom", "CREATE ROOM");
        SetLabel("Canvas/MainMenu/Window/FindRoom", "FIND ROOM");
        SetLabel("Canvas/CreateMenu/CreateWindow/Title", "CREATE ROOM");
        SetLabel("Canvas/CreateMenu/CreateWindow/CreateButton", "CREATE");
        SetLabel("Canvas/CreateMenu/CreateWindow/PrivateToggle", "PRIVATE");
        SetLabel("Canvas/RoomListMenu/RoomListWindow/Title", "ROOMS");
        SetLabel("Canvas/RoomListMenu/RoomListWindow/JoinCodeButton", "JOIN");
        SetLabel("Canvas/RoomListMenu/RoomListWindow/RefreshButton", "REFRESH");
        SetLabel("Canvas/RoomMenu/RoomWindow/Title", "ROOM");
        SetLabel("Canvas/RoomMenu/RoomWindow/StartButton", "START");
        SetLabel("Canvas/RoomMenu/RoomWindow/LeaveButton", "LEAVE");

        // ── 5) LobbyManager ──
        var lmGo = FindRoot("LobbyManager");
        if (lmGo == null)
        {
            lmGo = new GameObject("LobbyManager");
            log.Add("+ LobbyManager 오브젝트 생성");
        }
        var lm = lmGo.GetComponent<LobbyManager>();
        if (lm == null) lm = lmGo.AddComponent<LobbyManager>();

        var so = new SerializedObject(lm);
        Assign(so, "panelManager", pm);
        Assign(so, "mainMenu", mainMenu);
        Assign(so, "roomListMenu", roomListMenu);
        Assign(so, "roomMenu", roomMenu);
        Assign(so, "nicknameInput", nickInput);
        Assign(so, "privateToggle", Find("Canvas/CreateMenu/CreateWindow/PrivateToggle")?.GetComponentInChildren<Toggle>(true));
        Assign(so, "createButton", GetButton("Canvas/CreateMenu/CreateWindow/CreateButton"));
        Assign(so, "roomListContent", content);
        Assign(so, "roomListItemPrefab", itemPrefab);
        Assign(so, "codeInput", codeInput);
        Assign(so, "joinCodeButton", GetButton("Canvas/RoomListMenu/RoomListWindow/JoinCodeButton"));
        Assign(so, "refreshButton", GetButton("Canvas/RoomListMenu/RoomListWindow/RefreshButton"));
        Assign(so, "roomNameText", Find("Canvas/RoomMenu/RoomWindow/RoomNameText")?.GetComponent<TMP_Text>());
        Assign(so, "roomCodeText", Find("Canvas/RoomMenu/RoomWindow/RoomCodeText")?.GetComponent<TMP_Text>());
        Assign(so, "playersText", Find("Canvas/RoomMenu/RoomWindow/PlayersText")?.GetComponent<TMP_Text>());
        Assign(so, "startButton", GetButton("Canvas/RoomMenu/RoomWindow/StartButton"));
        Assign(so, "leaveButton", GetButton("Canvas/RoomMenu/RoomWindow/LeaveButton"));
        Assign(so, "statusText", Find("Canvas/StatusText")?.GetComponent<TMP_Text>());
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(lm);

        // ── 6) 글씨체 통일 ──
        int tmpCount = 0, txtCount = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.font == tmp) continue;
                t.font = tmp;
                t.fontSharedMaterial = tmp.material;
                Rec(t); tmpCount++;
            }
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (t.font == ttf) continue;
                t.font = ttf;
                Rec(t); txtCount++;
            }
        }
        log.Add($"* 글씨체 변경: TMP {tmpCount}개, Text {txtCount}개");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string msg = string.Join("\n", log);
        Debug.Log("[LobbyUIBuilder]\n" + msg);
        EditorUtility.DisplayDialog("Build Lobby UI 완료", msg + "\n\n씬 저장됨. 위치/크기는 Scene 뷰에서 조정하세요.", "OK");
    }

    // ───────── 위치 배치 (메뉴: BlindSpot > Layout Lobby UI) ─────────
    // 기준: Canvas Scaler 800x600 (높이 기준) → 각 창은 약 530 x 500, 가운데가 (0,0)

    [MenuItem("BlindSpot/Layout Lobby UI")]
    static void Layout()
    {
        scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Lobby")
        {
            EditorUtility.DisplayDialog("Layout Lobby UI", "Lobby 씬을 열고 실행하세요.", "OK");
            return;
        }
        log.Clear();

        // 메인 메뉴: 닉네임 입력칸은 Quit 아래
        Place("Canvas/MainMenu/Window/NicknameInput", 0, -215, 250, 50);

        // 방 만들기 창
        Place("Canvas/CreateMenu/CreateWindow/PrivateToggle", -40, 50, 30, 30);
        Place("Canvas/CreateMenu/CreateWindow/CreateButton", 0, -40, 250, 70);
        Place("Canvas/CreateMenu/CreateWindow/Back", 0, -140, 250, 70);

        // 방 목록 창
        Place("Canvas/RoomListMenu/RoomListWindow/RoomList", 0, 25, 460, 190);
        Place("Canvas/RoomListMenu/RoomListWindow/CodeInput", -110, -115, 220, 56);
        Place("Canvas/RoomListMenu/RoomListWindow/JoinCodeButton", 120, -115, 200, 64);
        Place("Canvas/RoomListMenu/RoomListWindow/RefreshButton", -110, -195, 200, 64);
        Place("Canvas/RoomListMenu/RoomListWindow/Back", 120, -195, 200, 64);

        // 방 안 창
        Place("Canvas/RoomMenu/RoomWindow/RoomNameText", 0, 105, 460, 50);
        Place("Canvas/RoomMenu/RoomWindow/RoomCodeText", 0, 62, 460, 36);
        Place("Canvas/RoomMenu/RoomWindow/PlayersText", 0, -35, 460, 150);
        Place("Canvas/RoomMenu/RoomWindow/StartButton", -115, -180, 210, 64);
        Place("Canvas/RoomMenu/RoomWindow/LeaveButton", 115, -180, 210, 64);

        StyleText("Canvas/RoomMenu/RoomWindow/RoomNameText", 34, TextAlignmentOptions.Center, "ROOM NAME");
        StyleText("Canvas/RoomMenu/RoomWindow/RoomCodeText", 24, TextAlignmentOptions.Center, "CODE: ------");
        StyleText("Canvas/RoomMenu/RoomWindow/PlayersText", 24, TextAlignmentOptions.Top, "PLAYERS 1 / 4");

        // 상태 메시지: 화면 아래쪽 가로 전체
        var status = Find("Canvas/StatusText") as RectTransform;
        if (status != null)
        {
            status.anchorMin = new Vector2(0f, 0f);
            status.anchorMax = new Vector2(1f, 0f);
            status.pivot = new Vector2(0.5f, 0f);
            status.anchoredPosition = new Vector2(0f, 20f);
            status.sizeDelta = new Vector2(-40f, 50f);
            Rec(status);
            StyleText("Canvas/StatusText", 24, TextAlignmentOptions.Center, "");
            log.Add("+ 배치: StatusText (화면 아래)");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string msg = string.Join("\n", log);
        Debug.Log("[LobbyUIBuilder Layout]\n" + msg);
        EditorUtility.DisplayDialog("Layout Lobby UI 완료", msg + "\n\n씬 저장됨.", "OK");
    }

    static void Place(string path, float x, float y, float w, float h)
    {
        var rt = Find(path) as RectTransform;
        if (rt == null) { log.Add("! 없음: " + path); return; }
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
        Rec(rt);
        log.Add($"+ 배치: {rt.name} ({x}, {y}) {w}x{h}");
    }

    static void StyleText(string path, float size, TextAlignmentOptions align, string sample)
    {
        var t = Find(path)?.GetComponent<TMP_Text>();
        if (t == null) return;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.overflowMode = TextOverflowModes.Overflow;
        t.text = sample;
        Rec(t);
    }

    // ───────── 입력칸 ─────────

    static TMP_InputField EnsureInputField(string path, string placeholder, int limit, TMP_FontAsset font)
    {
        var old = Find(path);
        if (old == null) { log.Add("! 없음: " + path); return null; }

        var existing = old.GetComponent<TMP_InputField>();
        if (existing != null) { log.Add("= 이미 입력칸: " + old.name); return existing; }

        var parent = old.parent;
        var oldRt = (RectTransform)old;
        int sibling = old.GetSiblingIndex();
        string name = old.name;

        var go = TMP_DefaultControls.CreateInputField(TmpResources());
        go.name = name;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.SetSiblingIndex(sibling);
        rt.anchorMin = oldRt.anchorMin;
        rt.anchorMax = oldRt.anchorMax;
        rt.pivot = oldRt.pivot;
        rt.anchoredPosition = oldRt.anchoredPosition;
        rt.localScale = Vector3.one;
        rt.sizeDelta = oldRt.sizeDelta.x < 150f ? new Vector2(360f, 60f) : new Vector2(oldRt.sizeDelta.x, Mathf.Max(oldRt.sizeDelta.y, 50f));

        Object.DestroyImmediate(old.gameObject);

        var input = go.GetComponent<TMP_InputField>();
        input.characterLimit = limit;
        input.contentType = TMP_InputField.ContentType.Alphanumeric;
        input.lineType = TMP_InputField.LineType.SingleLine;

        var bg = go.GetComponent<Image>();
        if (bg != null) bg.color = new Color(0f, 0f, 0f, 0.6f);

        if (input.textComponent != null)
        {
            input.textComponent.font = font;
            input.textComponent.fontSharedMaterial = font.material;
            input.textComponent.color = Color.white;
            input.textComponent.fontSize = 28;
        }
        if (input.placeholder is TMP_Text ph)
        {
            ph.text = placeholder;
            ph.font = font;
            ph.fontSharedMaterial = font.material;
            ph.color = new Color(1f, 1f, 1f, 0.35f);
            ph.fontSize = 28;
        }

        log.Add("+ 입력칸 교체: " + name);
        return input;
    }

    // ───────── 스크롤뷰 ─────────

    static Transform EnsureScrollView(string parentPath, string name)
    {
        var parent = Find(parentPath);
        if (parent == null) { log.Add("! 없음: " + parentPath); return null; }

        var existing = parent.Find(name);
        GameObject go;
        if (existing != null && existing.GetComponent<ScrollRect>() != null)
        {
            go = existing.gameObject;
            log.Add("= 이미 스크롤뷰: " + name);
        }
        else
        {
            go = DefaultControls.CreateScrollView(UiResources());
            go.name = name;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            // 창 안쪽 가운데 영역 (제목 아래 ~ 입력칸 위). 필요하면 Scene 뷰에서 조정
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(40f, 160f);
            rt.offsetMax = new Vector2(-40f, -120f);
            log.Add("+ 스크롤뷰 추가: " + name);
        }

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
        if (bg != null) bg.color = new Color(0f, 0f, 0f, 0.45f);

        var content = sr.content;
        if (content == null) { log.Add("! 스크롤뷰 Content 없음"); return null; }

        content.pivot = new Vector2(0.5f, 1f);
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = content.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = content.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return content;
    }

    // ───────── RoomListItem 프리팹 ─────────

    static RoomListItem EnsureItemPrefab(TMP_FontAsset font, Font ttf)
    {
        var existing = AssetDatabase.LoadAssetAtPath<RoomListItem>(ItemPrefabPath);
        if (existing != null) { log.Add("= 이미 프리팹: RoomListItem"); return existing; }

        var src = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
        if (src == null) { log.Add("! SF Button 프리팹 없음: " + ButtonPrefabPath); return null; }

        var go = Object.Instantiate(src);   // 원본과 연결 없는 복사본
        go.name = "RoomListItem";

        // 일반 Text → TMP 로 교체
        TMP_Text label = null;
        var legacy = go.GetComponentInChildren<Text>(true);
        if (legacy != null)
        {
            var labelGo = legacy.gameObject;
            Object.DestroyImmediate(legacy);
            var t = labelGo.AddComponent<TextMeshProUGUI>();
            t.text = "ROOM   1 / 4";
            t.font = font;
            t.fontSharedMaterial = font.material;
            t.fontSize = 26;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            label = t;
        }
        else
        {
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var t = labelGo.AddComponent<TextMeshProUGUI>();
            t.text = "ROOM   1 / 4";
            t.font = font;
            t.fontSharedMaterial = font.material;
            t.fontSize = 26;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            label = t;
        }

        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = 60f;
        le.preferredHeight = 60f;

        var button = go.GetComponent<Button>();
        if (button == null) button = go.GetComponentInChildren<Button>(true);
        var item = go.AddComponent<RoomListItem>();
        var so = new SerializedObject(item);
        so.FindProperty("nameText").objectReferenceValue = label;
        so.FindProperty("joinButton").objectReferenceValue = button;
        so.ApplyModifiedPropertiesWithoutUndo();

        var saved = PrefabUtility.SaveAsPrefabAsset(go, ItemPrefabPath);
        Object.DestroyImmediate(go);
        log.Add("+ 프리팹 생성: " + ItemPrefabPath);
        return saved != null ? saved.GetComponent<RoomListItem>() : null;
    }

    // ───────── 버튼 ─────────

    static void SetOpenPanel(string buttonPath, PanelManager pm, Animator target)
    {
        var btn = GetButton(buttonPath);
        if (btn == null) { log.Add("! 버튼 없음: " + buttonPath); return; }
        if (target == null) { log.Add("! 열 창 없음: " + buttonPath); return; }

        for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(btn.onClick, i);
        UnityEventTools.AddObjectPersistentListener<Animator>(btn.onClick, pm.OpenPanel, target);
        Rec(btn);
        log.Add($"+ 클릭 연결: {btn.name} → {target.name}");
    }

    static void SetLabel(string path, string label)
    {
        var t = Find(path);
        if (t == null) return;
        var txt = t.GetComponentInChildren<Text>(true);
        if (txt != null) { txt.text = label; Rec(txt); return; }
        var tm = t.GetComponentInChildren<TMP_Text>(true);
        if (tm != null) { tm.text = label; Rec(tm); }
    }

    static Button GetButton(string path)
    {
        var t = Find(path);
        if (t == null) return null;
        var b = t.GetComponent<Button>();
        return b != null ? b : t.GetComponentInChildren<Button>(true);
    }

    // ───────── 공통 ─────────

    static void Assign(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null) { log.Add("! LobbyManager 필드 없음: " + field); return; }
        p.objectReferenceValue = value;
        if (value == null) log.Add("! 비어 있음: LobbyManager." + field);
    }

    static Transform Find(string path)
    {
        var parts = path.Split('/');
        var root = FindRoot(parts[0]);
        if (root == null) return null;
        var t = root.transform;
        for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
        return t;
    }

    static GameObject FindRoot(string name)
    {
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }

    static void Rec(Object o)
    {
        EditorUtility.SetDirty(o);
        if (PrefabUtility.IsPartOfPrefabInstance(o))
            PrefabUtility.RecordPrefabInstancePropertyModifications(o);
    }

    static TMP_DefaultControls.Resources TmpResources()
    {
        return new TMP_DefaultControls.Resources
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

    static DefaultControls.Resources UiResources()
    {
        return new DefaultControls.Resources
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
}
#endif
