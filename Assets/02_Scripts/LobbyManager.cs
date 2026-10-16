using System;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로비: 방 만들기 / 코드로 참가 / 방장이 시작 → 게임 씬으로 모두 이동.
/// Lobby 씬의 빈 오브젝트에 붙인다. 같은 씬에 NetworkManager(Enable Scene Management 체크)가 있어야 함.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    [Header("설정")]
    [SerializeField] private string gameSceneName = "BlindSpot_Warehouse";
    [SerializeField, Range(2, 8)] private int maxPlayers = 4;
    [Tooltip("혼자서도 시작 가능 (테스트용)")]
    [SerializeField] private bool allowSoloStart = true;

    [Header("UI - 메뉴")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private Button createButton;
    [SerializeField] private TMP_InputField codeInput;
    [SerializeField] private Button joinButton;

    [Header("UI - 방")]
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text playersText;
    [SerializeField] private Button startButton;
    [SerializeField] private Button leaveButton;

    [Header("UI - 상태")]
    [SerializeField] private TMP_Text statusText;

    private ISession session;
    private bool busy;

    private async void Start()
    {
        ShowMenu();
        SetMenuInteractable(false);

        createButton.onClick.AddListener(() => _ = CreateRoom());
        joinButton.onClick.AddListener(() => _ = JoinRoom());
        startButton.onClick.AddListener(() => _ = StartGame());
        leaveButton.onClick.AddListener(() => _ = LeaveRoom());

        try
        {
            SetStatus("서버 연결 중...");
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // 같은 PC에서 여러 개 띄워 테스트할 때 서로 다른 플레이어로 인식되도록 프로필을 랜덤으로
                var options = new InitializationOptions();
                options.SetProfile("p" + UnityEngine.Random.Range(0, 1000000));
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            SetStatus("");
            SetMenuInteractable(true);
        }
        catch (Exception e)
        {
            SetStatus("연결 실패: " + e.Message);
            Debug.LogException(e);
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    // ───────── 방 만들기 / 참가 / 나가기 ─────────

    private async Task CreateRoom()
    {
        if (busy) return;
        busy = true;
        SetMenuInteractable(false);
        SetStatus("방 만드는 중...");

        try
        {
            var options = new SessionOptions
            {
                MaxPlayers = maxPlayers,
                IsPrivate = true          // 목록에는 안 보이고 코드로만 참가
            }.WithRelayNetwork();         // 다른 네트워크끼리도 연결 + 방장이 Host로 자동 시작

            session = await MultiplayerService.Instance.CreateSessionAsync(options);
            EnterRoom();
        }
        catch (Exception e)
        {
            SetStatus("방 만들기 실패: " + e.Message);
            Debug.LogException(e);
            SetMenuInteractable(true);
        }
        busy = false;
    }

    private async Task JoinRoom()
    {
        if (busy) return;
        string code = codeInput.text.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            SetStatus("방 코드를 입력하세요");
            return;
        }

        busy = true;
        SetMenuInteractable(false);
        SetStatus("참가하는 중...");

        try
        {
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            EnterRoom();
        }
        catch (Exception e)
        {
            SetStatus("참가 실패: " + e.Message);
            Debug.LogException(e);
            SetMenuInteractable(true);
        }
        busy = false;
    }

    private async Task LeaveRoom()
    {
        if (session == null) return;
        Unsubscribe();
        try { await session.LeaveAsync(); }
        catch (Exception e) { Debug.LogException(e); }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        session = null;
        ShowMenu();
        SetMenuInteractable(true);
        SetStatus("");
    }

    // ───────── 게임 시작 (방장만) ─────────

    private async Task StartGame()
    {
        if (session == null || !session.IsHost) return;
        startButton.interactable = false;
        SetStatus("게임 시작...");

        // 게임 중에는 새로 못 들어오게 잠금
        try
        {
            var host = session.AsHost();
            host.IsLocked = true;
            await host.SavePropertiesAsync();
        }
        catch (Exception e) { Debug.LogWarning("방 잠금 실패: " + e.Message); }

        // 서버(방장)가 씬을 바꾸면 접속한 모두가 같이 이동
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    // ───────── 방 화면 ─────────

    private void EnterRoom()
    {
        session.Changed += RefreshRoom;
        session.RemovedFromSession += OnRemovedFromSession;

        menuPanel.SetActive(false);
        roomPanel.SetActive(true);
        roomCodeText.text = "방 코드: " + session.Code;
        SetStatus("");
        RefreshRoom();
    }

    private void RefreshRoom()
    {
        if (session == null) return;
        playersText.text = $"인원 {session.PlayerCount} / {session.MaxPlayers}";

        bool isHost = session.IsHost;
        startButton.gameObject.SetActive(isHost);
        startButton.interactable = isHost && (allowSoloStart || session.PlayerCount >= 2);

        if (!isHost) SetStatus("방장이 시작하길 기다리는 중...");
    }

    private void OnRemovedFromSession()
    {
        Unsubscribe();
        session = null;
        ShowMenu();
        SetMenuInteractable(true);
        SetStatus("방에서 나가졌습니다");
    }

    private void Unsubscribe()
    {
        if (session == null) return;
        session.Changed -= RefreshRoom;
        session.RemovedFromSession -= OnRemovedFromSession;
    }

    // ───────── UI 도우미 ─────────

    private void ShowMenu()
    {
        menuPanel.SetActive(true);
        roomPanel.SetActive(false);
    }

    private void SetMenuInteractable(bool on)
    {
        createButton.interactable = on;
        joinButton.interactable = on;
        codeInput.interactable = on;
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }
}