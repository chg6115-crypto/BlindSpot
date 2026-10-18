using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 씬(Warehouse)에서 접속한 플레이어마다 Player_FPS를 스폰한다. GameManager 아래에 하나 배치.
///  - 서버(호스트)만 스폰. 모든 클라이언트가 씬 로드를 마친 시점(OnLoadEventCompleted)에 실행
///  - 스폰 위치: RoundManager의 Spawn Points (없으면 이 컴포넌트의 Spawn Points)
///  - 오프라인(씬 직접 Play): 네트워크 없이 프리팹을 그냥 생성
///
/// NetworkManager의 Player Prefab 칸은 비워둘 것 (채우면 로비에서 플레이어가 생겨버림).
/// 플레이어는 경기당 한 번만 생성되므로 오브젝트 풀링은 쓰지 않음.
/// </summary>
public class NetworkPlayerSpawner : MonoBehaviour
{
    [Tooltip("NetworkObject가 붙은 Player_FPS 프리팹 (NetworkManager의 Network Prefabs 목록에도 있어야 함)")]
    [SerializeField] private GameObject playerPrefab;
    [Tooltip("비우면 RoundManager의 Spawn Points 사용")]
    [SerializeField] private Transform[] spawnPoints;

    private readonly HashSet<ulong> spawned = new HashSet<ulong>();
    private NetworkManager nm;

    private void Awake()
    {
        nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;   // 오프라인은 Start에서 처리
        if (!nm.IsServer) return;                     // 클라이언트는 스폰 안 함

        // 씬 오브젝트 Awake가 로드 완료 이벤트보다 먼저 실행되므로 여기서 구독
        nm.SceneManager.OnLoadEventCompleted += HandleLoadCompleted;
        nm.OnClientDisconnectCallback += HandleDisconnect;
    }

    private void Start()
    {
        if (nm == null || !nm.IsListening) SpawnOffline();
    }

    private void OnDestroy()
    {
        if (nm == null) return;
        if (nm.SceneManager != null) nm.SceneManager.OnLoadEventCompleted -= HandleLoadCompleted;
        nm.OnClientDisconnectCallback -= HandleDisconnect;
    }

    // ───────── 네트워크 ─────────

    private void HandleLoadCompleted(string sceneName, LoadSceneMode mode,
                                     List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameObject.scene.name) return;

        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            Debug.LogWarning($"[NetworkPlayerSpawner] 씬 로드 시간 초과: {string.Join(", ", clientsTimedOut)}", this);

        // 접속 순서대로 자리 배정 (호스트 0번)
        var order = nm.ConnectedClientsIds.OrderBy(id => id).ToList();
        foreach (var clientId in clientsCompleted)
            SpawnFor(clientId, order.IndexOf(clientId));
    }

    private void SpawnFor(ulong clientId, int slot)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[NetworkPlayerSpawner] Player Prefab이 비어 있습니다.", this);
            return;
        }
        if (!spawned.Add(clientId)) return;   // 이미 스폰됨

        GetPoint(slot, out var pos, out var rot);
        var go = Instantiate(playerPrefab, pos, rot);
        var no = go.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError("[NetworkPlayerSpawner] Player Prefab에 NetworkObject가 없습니다.", this);
            Destroy(go);
            spawned.Remove(clientId);
            return;
        }
        no.SpawnAsPlayerObject(clientId, true);   // 씬이 바뀌면 같이 삭제
    }

    private void HandleDisconnect(ulong clientId)
    {
        // 플레이어 오브젝트는 NGO가 자동으로 제거. 재접속 시 다시 스폰할 수 있게 기록만 지움
        spawned.Remove(clientId);
    }

    // ───────── 오프라인 ─────────

    private void SpawnOffline()
    {
        if (playerPrefab == null) return;
        if (PlayerNetworkSetup.Local != null) return;

        GetPoint(0, out var pos, out var rot);
        Instantiate(playerPrefab, pos, rot);   // PlayerNetworkSetup.Start에서 내 플레이어로 설정됨
        Debug.Log("[NetworkPlayerSpawner] 네트워크 없이 실행 → 오프라인 플레이어 생성", this);
    }

    // ───────── 스폰 위치 ─────────

    private void GetPoint(int slot, out Vector3 pos, out Quaternion rot)
    {
        var points = spawnPoints;
        if ((points == null || points.Length == 0) && RoundManager.Instance != null)
            points = RoundManager.Instance.SpawnPoints;

        var valid = points != null ? points.Where(p => p != null).ToList() : new List<Transform>();
        if (valid.Count == 0)
        {
            pos = transform.position;
            rot = transform.rotation;
            return;
        }

        var p = valid[Mathf.Abs(slot) % valid.Count];
        pos = p.position;
        rot = Quaternion.Euler(0f, p.eulerAngles.y, 0f);
    }
}
