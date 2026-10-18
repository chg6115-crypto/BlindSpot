using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로비 (Lobby_New 씬).
///  - 패널 5개(Main / Create / RoomList / Room / Settings)를 켜고 끄는 방식으로 화면 전환
///  - 모든 버튼 클릭은 코드에서 연결 (Inspector의 On Click() 설정 불필요)
///  - 싱글 플레이: 서버/로그인 없이 내 PC에서 Host로 바로 시작 (인터넷 불필요)
///  - 멀티: 닉네임 → 방 만들기(공개/비공개) / 방 목록 / 코드 참가 → 방장 START → 전원 게임 씬 이동
/// </summary>
public class LobbyManager : MonoBehaviour
{
    public static string LocalNickname { get; private set; } = "";
    /// <summary>true면 싱글(오프라인) 플레이, false면 멀티. 기본값 true (Warehouse를 에디터에서 바로 실행해도 싱글 취급)</summary>
    public static bool IsSinglePlayer { get; private set; } = true;
    /// <summary>로비를 거쳐 멀티로 들어갔는지 (디버그 메뉴 제한 등에 사용)</summary>
    public static bool IsMultiplayer => !IsSinglePlayer;

    /// <summary>게임에서 메인 메뉴로 돌아올 때 보여줄 안내 문구 (GameExit이 설정)</summary>
    public static string PendingMessage { get; set; } = "";

    /// <summary>게임에서 메인 메뉴로 돌아올 때 모드 초기화 (GameExit이 호출)</summary>
    public static void ResetToMenu()
    {
        IsSinglePlayer = true;
    }

    private static string ConsumePendingMessage()
    {
        string m = PendingMessage ?? "";
        PendingMessage = "";
        return m;
    }

    const string NickKey = "nickname";
    const string NickProperty = "nick";
    const string PrivateProperty = "priv";
    static readonly Regex NickRule = new Regex("^[A-Za-z0-9]{4,8}$");
    const string NickHint = "Enter a nickname: 4-8 letters or numbers (A-Z, 0-9)";

    [Header("설정")]
    [SerializeField] private string gameSceneName = "BlindSpot_Warehouse";
    [SerializeField, Range(2, 8)] private int maxPlayers = 4;
    [Tooltip("혼자서도 시작 가능 (테스트용)")]
    [SerializeField] private bool allowSoloStart = true;
    [Tooltip("방 목록 화면에서 자동 새로고침 간격(초). 0이면 끔")]
    [SerializeField, Min(0f)] private float autoRefreshInterval = 5f;

    [Header("패널")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject createPanel;
    [SerializeField] private GameObject roomListPanel;
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private GameObject settingsPanel;

    [Header("Main")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button singleButton;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button findRoomButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("Create")]
    [SerializeField] private TMP_Text createRoomNameText;
    [SerializeField] private Toggle privateToggle;
    [SerializeField] private Button createButton;
    [SerializeField] private Button createBackButton;

    [Header("Room List")]
    [SerializeField] private Transform roomListContent;
    [SerializeField] private RoomListItem roomListItemPrefab;
    [SerializeField] private TMP_InputField codeInput;
    [SerializeField] private Button joinCodeButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button roomListBackButton;

    [Header("Room")]
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text roomPrivacyText;
    [SerializeField] private TMP_Text playersCountText;
    [SerializeField] private TMP_Text playersText;
    [SerializeField] private Button startButton;
    [SerializeField] private Button leaveButton;

    [Header("Settings")]
    [SerializeField] private Button settingsBackButton;

    [Header("상태 메시지")]
    [SerializeField] private TMP_Text statusText;

    private ISession session;
    private bool busy;
    private bool ready;
    private float refreshTimer;
    private bool allButtonsOn = true;      // SetAllButtons 상태 (처리 중이면 false)
    private bool onlineButtonsOn;          // SetOnlineButtons 상태 (로그인 완료 시 true)
    private readonly List<RoomListItem> listItems = new List<RoomListItem>();

    // ───────── 시작 ─────────

    private async void Start()
    {
        if (nicknameInput != null)
        {
            nicknameInput.characterLimit = 8;
            nicknameInput.contentType = TMP_InputField.ContentType.Alphanumeric;
            string saved = PlayerPrefs.GetString(NickKey, "");
            nicknameInput.text = NickRule.IsMatch(saved) ? saved : "";   // 예전 규칙(12자)으로 저장된 닉네임은 비움
            nicknameInput.onValueChanged.AddListener(_ => OnNicknameChanged());
        }

        Bind(singleButton, StartSingle);
        Bind(createRoomButton, OpenCreate);
        Bind(findRoomButton, OpenRoomList);
        Bind(settingsButton, () => ShowPanel(settingsPanel));
        Bind(quitButton, Quit);

        Bind(createButton, () => _ = CreateRoom());
        Bind(createBackButton, () => ShowPanel(mainPanel));

        Bind(joinCodeButton, () => _ = JoinByCode());
        Bind(refreshButton, () => _ = RefreshRoomList());
        Bind(roomListBackButton, () => ShowPanel(mainPanel));

        Bind(startButton, () => _ = StartGame());
        Bind(leaveButton, () => _ = LeaveRoom());

        Bind(settingsBackButton, () => ShowPanel(mainPanel));

        ShowPanel(mainPanel);
        SetOnlineButtons(false);

        try
        {
            SetStatus("Connecting...");
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // 같은 PC에서 여러 창으로 테스트할 때 서로 다른 플레이어로 인식되도록 랜덤 프로필
                var options = new InitializationOptions();
                options.SetProfile("p" + UnityEngine.Random.Range(0, 1000000));
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            ready = true;
            string pending = ConsumePendingMessage();   // 게임에서 돌아왔으면 안내 문구 (예: 호스트가 나감)
            SetStatus(!string.IsNullOrEmpty(pending) ? pending : (IsNickValid() ? "" : NickHint));
            SetOnlineButtons(true);
        }
        catch (Exception e)
        {
            string pending = ConsumePendingMessage();
            SetStatus(!string.IsNullOrEmpty(pending) ? pending
                      : (IsNickValid() ? "Offline - single play only" : "Offline - " + NickHint));
            Debug.LogException(e);
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Update()
    {
        // 방 목록 화면이 열려 있으면 주기적으로 새로고침
        if (!ready || busy || session != null || autoRefreshInterval <= 0f) return;
        if (roomListPanel == null || !roomListPanel.activeInHierarchy) return;

        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = autoRefreshInterval;
            _ = RefreshRoomList();
        }
    }

    private static void Bind(Button b, UnityEngine.Events.UnityAction action)
    {
        if (b != null) b.onClick.AddListener(action);
    }

    // ───────── 화면 전환 ─────────

    private void ShowPanel(GameObject target)
    {
        foreach (var p in new[] { mainPanel, createPanel, roomListPanel, roomPanel, settingsPanel })
            if (p != null) p.SetActive(p == target);
    }

    private void OpenCreate()
    {
        if (!ready || busy) return;
        if (!ValidateNickname()) return;
        if (createRoomNameText != null) createRoomNameText.text = LocalNickname;
        if (privateToggle != null) privateToggle.isOn = false;
        SetStatus("");
        ShowPanel(createPanel);
    }

    private void OpenRoomList()
    {
        if (!ready || busy) return;
        SetStatus("");
        ShowPanel(roomListPanel);
        refreshTimer = autoRefreshInterval;
        _ = RefreshRoomList();   // 열자마자 한 번 새로고침
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ───────── 닉네임 ─────────

    private string CurrentNick() => nicknameInput != null ? nicknameInput.text.Trim() : "";

    /// <summary>닉네임이 규칙(4~8자, 영문/숫자)에 맞는지</summary>
    private bool IsNickValid() => NickRule.IsMatch(CurrentNick());

    /// <summary>닉네임 입력이 바뀔 때: 버튼 활성화 갱신 + 안내 문구</summary>
    private void OnNicknameChanged()
    {
        RefreshNickGate();
        if (busy || session != null) return;

        string current = statusText != null ? statusText.text : "";
        bool showingHint = current == NickHint || current == "Offline - " + NickHint;
        if (!IsNickValid())
        {
            if (string.IsNullOrEmpty(current) || showingHint) SetStatus(ready ? NickHint : "Offline - " + NickHint);
        }
        else if (showingHint)
        {
            SetStatus(ready ? "" : "Offline - single play only");
        }
    }

    /// <summary>닉네임 검사 (4~8자, 영문/숫자). 통과하면 저장</summary>
    private bool ValidateNickname()
    {
        string nick = CurrentNick();
        if (!NickRule.IsMatch(nick))
        {
            SetStatus(NickHint);
            return false;
        }
        LocalNickname = nick;
        PlayerPrefs.SetString(NickKey, nick);
        PlayerPrefs.Save();
        return true;
    }

    private Dictionary<string, PlayerProperty> MyProperties()
    {
        return new Dictionary<string, PlayerProperty>
        {
            { NickProperty, new PlayerProperty(LocalNickname, VisibilityPropertyOptions.Member) }
        };
    }

    // ───────── 싱글 플레이 ─────────

    /// <summary>
    /// 서버/로그인 없이 내 PC에서 Host로 시작 → 게임 씬 이동.
    /// 멀티와 같은 경로(NetworkManager)를 쓰므로 나중에 네트워크 플레이어로 바꿔도 그대로 동작.
    /// </summary>
    private void StartSingle()
    {
        if (busy || session != null) return;
        if (!ValidateNickname()) return;   // 닉네임 필수

        var nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("NetworkManager missing"); return; }

        busy = true;
        SetAllButtons(false);

        IsSinglePlayer = true;

        // Relay 설정이 남아 있어도 로컬 직접 연결로 덮어씀
        var utp = nm.GetComponent<UnityTransport>();
        if (utp != null) utp.SetConnectionData("127.0.0.1", 7777);

        if (nm.IsListening) nm.Shutdown();
        if (!nm.StartHost())
        {
            SetStatus("Single start failed");
            busy = false;
            SetAllButtons(true);
            SetOnlineButtons(ready);
            return;
        }

        SetStatus("Starting single play...");
        GameExit.ExpectNetworkGame = true;
        nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    // ───────── 방 만들기 ─────────

    private async Task CreateRoom()
    {
        if (busy || !ready || !ValidateNickname()) return;
        busy = true;
        SetAllButtons(false);
        SetStatus("Creating room...");

        try
        {
            bool isPrivate = privateToggle != null && privateToggle.isOn;
            var options = new SessionOptions
            {
                Name = LocalNickname,          // 방 제목 = 닉네임
                MaxPlayers = maxPlayers,
                IsPrivate = isPrivate,         // 비공개면 목록에 안 보이고 코드로만 참가
                PlayerProperties = MyProperties(),
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { PrivateProperty, new SessionProperty(isPrivate ? "1" : "0", VisibilityPropertyOptions.Member) }
                }
            }.WithRelayNetwork();              // 방장이 Host로 자동 시작, 다른 네트워크끼리도 연결

            session = await MultiplayerService.Instance.CreateSessionAsync(options);
            IsSinglePlayer = false;
            EnterRoom();
        }
        catch (Exception e)
        {
            SetStatus("Create failed: " + e.Message);
            Debug.LogException(e);
        }
        SetAllButtons(true);
        SetOnlineButtons(ready);
        busy = false;
    }

    // ───────── 방 목록 ─────────

    private async Task RefreshRoomList()
    {
        if (busy || !ready || session != null) return;
        busy = true;
        if (refreshButton != null) refreshButton.interactable = false;

        try
        {
            var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions { Count = 20 });
            ClearList();

            foreach (var info in results.Sessions)
            {
                if (info.IsLocked || info.AvailableSlots <= 0) continue;   // 게임 중이거나 꽉 찬 방 제외

                string id = info.Id;
                int players = info.MaxPlayers - info.AvailableSlots;
                var item = Instantiate(roomListItemPrefab, roomListContent);
                item.Setup(string.IsNullOrEmpty(info.Name) ? "ROOM" : info.Name, players, info.MaxPlayers,
                           () => _ = JoinById(id));
                listItems.Add(item);
            }

            if (roomListPanel != null && roomListPanel.activeInHierarchy)
                SetStatus(listItems.Count == 0 ? "No rooms. Create one!" : "");
        }
        catch (Exception e)
        {
            SetStatus("Refresh failed: " + e.Message);
            Debug.LogException(e);
        }

        if (refreshButton != null) refreshButton.interactable = true;
        busy = false;
    }

    private void ClearList()
    {
        foreach (var item in listItems)
            if (item != null) Destroy(item.gameObject);
        listItems.Clear();
    }

    // ───────── 참가 ─────────

    private async Task JoinById(string sessionId)
    {
        if (busy || !ready || !ValidateNickname()) return;
        await Join(() => MultiplayerService.Instance.JoinSessionByIdAsync(
            sessionId, new JoinSessionOptions { PlayerProperties = MyProperties() }));
    }

    private async Task JoinByCode()
    {
        if (busy || !ready || !ValidateNickname()) return;
        string code = codeInput != null ? codeInput.text.Trim().ToUpperInvariant() : "";
        if (string.IsNullOrEmpty(code))
        {
            SetStatus("Enter a room code");
            return;
        }
        await Join(() => MultiplayerService.Instance.JoinSessionByCodeAsync(
            code, new JoinSessionOptions { PlayerProperties = MyProperties() }));
    }

    private async Task Join(Func<Task<ISession>> joinCall)
    {
        busy = true;
        SetAllButtons(false);
        SetStatus("Joining...");

        try
        {
            session = await joinCall();
            IsSinglePlayer = false;
            EnterRoom();
        }
        catch (Exception e)
        {
            SetStatus("Join failed: " + e.Message);
            Debug.LogException(e);
        }
        SetAllButtons(true);
        SetOnlineButtons(ready);
        busy = false;
    }

    // ───────── 나가기 ─────────

    private async Task LeaveRoom()
    {
        if (session == null) return;
        if (leaveButton != null) leaveButton.interactable = false;
        Unsubscribe();

        try { await session.LeaveAsync(); }
        catch (Exception e) { Debug.LogException(e); }

        BackToMenu("");
    }

    private void BackToMenu(string message)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        session = null;
        IsSinglePlayer = true;
        GameExit.ExpectNetworkGame = false;
        if (leaveButton != null) leaveButton.interactable = true;
        SetAllButtons(true);
        SetOnlineButtons(ready);
        ShowPanel(mainPanel);
        SetStatus(message);
    }

    // ───────── 게임 시작 (방장만) ─────────

    private async Task StartGame()
    {
        if (session == null || !session.IsHost) return;
        if (startButton != null) startButton.interactable = false;
        SetStatus("Starting...");

        // 게임 중에는 새로 못 들어오게 잠금 (목록에서도 사라짐)
        try
        {
            var host = session.AsHost();
            host.IsLocked = true;
            await host.SavePropertiesAsync();
        }
        catch (Exception e) { Debug.LogWarning("Lock failed: " + e.Message); }

        // 서버(방장)가 씬을 바꾸면 접속한 모두가 같이 이동
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    // ───────── 방 화면 ─────────

    private void EnterRoom()
    {
        session.Changed += RefreshRoom;
        session.RemovedFromSession += OnRemovedFromSession;

        // 대기실에서 호스트가 나가 네트워크가 끊기면 감지 (게스트)
        GameExit.ExpectNetworkGame = true;
        if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped += OnNetworkStoppedInRoom;

        ClearList();
        ShowPanel(roomPanel);
        SetStatus("");
        RefreshRoom();
    }

    private void RefreshRoom()
    {
        if (session == null) return;

        if (roomNameText != null) roomNameText.text = session.Name;
        if (roomCodeText != null) roomCodeText.text = "CODE: " + session.Code;

        if (roomPrivacyText != null)
        {
            bool isPrivate = session.Properties != null
                             && session.Properties.TryGetValue(PrivateProperty, out var priv)
                             && priv.Value == "1";
            roomPrivacyText.text = isPrivate ? "PRIVATE" : "PUBLIC";
        }

        if (playersCountText != null)
            playersCountText.text = $"PLAYERS {session.PlayerCount} / {session.MaxPlayers}";

        if (playersText != null)
        {
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            foreach (var p in session.Players)
            {
                string nick = p.Properties != null && p.Properties.TryGetValue(NickProperty, out var prop)
                    ? prop.Value : "PLAYER";
                bool isHostPlayer = p.Id == session.Host;
                sb.Append(nick);
                if (isHostPlayer) sb.Append("<pos=80%><color=#40E6F0><size=70%>HOST</size></color>");
                sb.Append('\n');
                shown++;
            }
            for (int i = shown; i < session.MaxPlayers; i++)
                sb.Append("<color=#5A698C>- EMPTY -</color>\n");
            playersText.text = sb.ToString();
        }

        bool isHost = session.IsHost;
        if (startButton != null)
        {
            startButton.gameObject.SetActive(isHost);
            startButton.interactable = isHost && (allowSoloStart || session.PlayerCount >= 2);
        }

        SetStatus(isHost ? "" : "Waiting for host to start...");
    }

    private void OnRemovedFromSession()
    {
        Unsubscribe();
        BackToMenu("Host closed the room");
    }

    /// <summary>대기실에서 내 네트워크 연결이 끊김 (게스트: 호스트가 방을 나감)</summary>
    private void OnNetworkStoppedInRoom(bool wasHost)
    {
        if (session == null || wasHost) return;
        var s = session;
        Unsubscribe();
        _ = LeaveQuietly(s);
        BackToMenu("Host closed the room");
    }

    private static async Task LeaveQuietly(ISession s)
    {
        try { await s.LeaveAsync(); }
        catch (Exception) { /* 이미 방이 없어졌을 수 있음 */ }
    }

    private void Unsubscribe()
    {
        if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped -= OnNetworkStoppedInRoom;
        if (session == null) return;
        session.Changed -= RefreshRoom;
        session.RemovedFromSession -= OnRemovedFromSession;
    }

    // ───────── 공통 ─────────

    /// <summary>서버 연결이 필요한 버튼 (방 만들기 / 찾기 / 참가)</summary>
    private void SetOnlineButtons(bool on)
    {
        onlineButtonsOn = on;
        foreach (var b in new[] { createRoomButton, findRoomButton, createButton, joinCodeButton, refreshButton })
            if (b != null) b.interactable = on;
        foreach (var item in listItems)
            if (item != null) item.SetInteractable(on);
        RefreshNickGate();
    }

    /// <summary>닉네임이 없거나 규칙에 안 맞으면 SINGLE / CREATE ROOM / FIND ROOM 비활성화</summary>
    private void RefreshNickGate()
    {
        bool nick = IsNickValid();
        if (singleButton != null) singleButton.interactable = allButtonsOn && nick;
        if (createRoomButton != null) createRoomButton.interactable = allButtonsOn && onlineButtonsOn && nick;
        if (findRoomButton != null) findRoomButton.interactable = allButtonsOn && onlineButtonsOn && nick;
    }

    /// <summary>처리 중(busy)일 때 전체 버튼 잠금. 싱글/설정/종료 포함</summary>
    private void SetAllButtons(bool on)
    {
        allButtonsOn = on;
        foreach (var b in new[] { singleButton, createRoomButton, findRoomButton, settingsButton, quitButton,
                                  createButton, createBackButton, joinCodeButton, refreshButton, roomListBackButton })
            if (b != null) b.interactable = on;
        foreach (var item in listItems)
            if (item != null) item.SetInteractable(on);
        RefreshNickGate();
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }
}
