using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 연결 끊김 감시. 게임 시작 시 자동으로 생성되어 씬이 바뀌어도 유지된다 (씬에 배치할 필요 없음).
///  - 게임 씬에서 내 연결이 끊김
///      게스트: 호스트가 나감        → 메인 메뉴 + "Disconnected from host"
///      호스트: 인터넷/Relay 끊김     → 메인 메뉴 + "Connection lost"
///  - 게임 씬 로딩 중에 끊겨서 네트워크 없이 게임 씬이 열림 → 메인 메뉴 + "Connection lost"
///  - 결과창이 떠 있을 때 끊기면 문구 없이 메인 메뉴
///  로비 씬(대기실)의 끊김은 LobbyManager가 처리.
/// </summary>
public class ConnectionWatcher : MonoBehaviour
{
    private static ConnectionWatcher instance;
    private NetworkManager hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("ConnectionWatcher");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ConnectionWatcher>();
    }

    private void OnEnable() => SceneManager.sceneLoaded += HandleSceneLoaded;

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Unhook();
    }

    private void Update()
    {
        // NetworkManager가 바뀌면(로비로 돌아오면 새로 생김) 다시 연결
        var nm = NetworkManager.Singleton;
        if (nm == hooked) return;
        Unhook();
        hooked = nm;
        if (hooked != null)
        {
            hooked.OnClientStopped += HandleClientStopped;
            hooked.OnTransportFailure += HandleTransportFailure;
        }
    }

    private void Unhook()
    {
        if (hooked != null)
        {
            hooked.OnClientStopped -= HandleClientStopped;
            hooked.OnTransportFailure -= HandleTransportFailure;
        }
        hooked = null;
    }

    private static bool InGameScene => SceneManager.GetActiveScene().name != GameExit.MenuScene;

    private void HandleClientStopped(bool wasHost)
    {
        if (GameExit.IsLeaving || !InGameScene) return;

        string msg = MatchResultUI.IsShowing ? "" : (wasHost ? "Connection lost" : "Disconnected from host");
        GameExit.ToMainMenu(msg);
    }

    private void HandleTransportFailure()
    {
        if (GameExit.IsLeaving || !InGameScene) return;
        GameExit.ToMainMenu("Connection lost");
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == GameExit.MenuScene || GameExit.IsLeaving) return;
        if (!GameExit.ExpectNetworkGame) return;   // 에디터에서 게임 씬을 바로 실행한 경우 등

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) GameExit.ToMainMenu("Connection lost");
    }
}
