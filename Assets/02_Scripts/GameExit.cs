using System;
using System.Linq;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임에서 메인 메뉴(로비 씬)로 완전히 돌아가기. 결과창 / 연결 끊김(ConnectionWatcher)에서 호출.
///  1) 참가 중인 멀티 세션(방)에서 나감
///  2) 네트워크 종료 + 남아 있던 NetworkManager 삭제 (로비 씬에 새 NetworkManager가 있으므로 중복 방지)
///  3) 로비 씬 로드, 안내 문구는 LobbyManager가 상태 메시지로 표시
/// </summary>
public static class GameExit
{
    public const string MenuScene = "Lobby_New";

    /// <summary>지금 메인 메뉴로 나가는 중인지 (연결 끊김 알림을 무시하기 위해)</summary>
    public static bool IsLeaving { get; private set; }

    /// <summary>
    /// 로비에서 네트워크 게임(싱글 포함)에 들어가는 중/플레이 중인지.
    /// 게임 씬이 열렸는데 네트워크가 이미 끊겨 있으면 ConnectionWatcher가 메인 메뉴로 돌려보냄 (로딩 중 끊김 대비)
    /// </summary>
    public static bool ExpectNetworkGame { get; set; }

    public static async void ToMainMenu(string message = "")
    {
        if (IsLeaving) return;
        IsLeaving = true;

        LobbyManager.PendingMessage = message ?? "";

        // 1) 세션(방) 나가기
        try
        {
            if (UnityServices.State == ServicesInitializationState.Initialized && MultiplayerService.Instance != null)
            {
                foreach (var s in MultiplayerService.Instance.Sessions.Values.ToList())
                {
                    try { await s.LeaveAsync(); }
                    catch (Exception e) { Debug.LogWarning("[GameExit] 세션 나가기 실패: " + e.Message); }
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[GameExit] " + e.Message); }

        // 2) 네트워크 정리
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening) nm.Shutdown();
            UnityEngine.Object.Destroy(nm.gameObject);
        }

        // 3) 메인 메뉴
        LobbyManager.ResetToMenu();
        ExpectNetworkGame = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(MenuScene, LoadSceneMode.Single);

        IsLeaving = false;
    }
}
