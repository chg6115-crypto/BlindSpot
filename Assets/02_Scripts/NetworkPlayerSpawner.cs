using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 씬(Warehouse)에서 접속한 플레이어마다 Player_FPS를 스폰한다. GameManager 아래에 하나 배치.
///  - 서버(호스트)만 스폰
///  - 스폰 시점 (먼저 오는 쪽, 중복 스폰은 막음)
///     ① 클라이언트 한 명이 씬 로드를 끝낼 때마다 (SceneEventType.LoadComplete)
///     ② 모든 클라이언트 로드 완료 (OnLoadEventCompleted)
///     ③ 안전장치: 0.5초마다 플레이어가 없는 접속자를 찾아 스폰 (이벤트를 놓쳐도 복구)
///        단, 씬 로드를 끝낸 사람만 (로드 중인 사람에게 먼저 만들면 라운드 시작을 놓침). 10초가 지나면 그냥 스폰
///  - RoundManager(호스트)는 AllPlayersSpawned가 될 때까지 경기 시작을 기다림
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
    [Tooltip("콘솔에 스폰 기록 출력 (문제 확인용)")]
    [SerializeField] private bool verboseLog = true;

    [Tooltip("씬 로드 완료 신호가 끝내 안 와도 이 시간이 지나면 스폰 (초)")]
    [SerializeField, Min(1f)] private float forceSpawnAfter = 10f;

    public static NetworkPlayerSpawner Instance { get; private set; }

    private readonly HashSet<ulong> spawned = new HashSet<ulong>();
    private readonly HashSet<ulong> loaded = new HashSet<ulong>();
    private readonly Dictionary<ulong, float> firstSeen = new Dictionary<ulong, float>();

    /// <summary>접속한 모든 사람의 플레이어가 생성됐는지 (오프라인이면 항상 true)</summary>
    public bool AllPlayersSpawned
    {
        get
        {
            if (nm == null || !nm.IsListening) return true;
            foreach (var id in nm.ConnectedClientsIds)
                if (!spawned.Contains(id)) return false;
            return true;
        }
    }
    private NetworkManager nm;
    private bool listening;
    private float nextCheck;

    private void Awake()
    {
        Instance = this;
        nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;   // 오프라인은 Start에서 처리
        if (!nm.IsServer) return;                     // 클라이언트는 스폰 안 함

        // 씬 오브젝트 Awake가 로드 완료 이벤트보다 먼저 실행되므로 여기서 구독
        nm.SceneManager.OnSceneEvent += HandleSceneEvent;
        nm.SceneManager.OnLoadEventCompleted += HandleLoadCompleted;
        nm.OnClientDisconnectCallback += HandleDisconnect;
        listening = true;
    }

    private void Start()
    {
        if (nm == null || !nm.IsListening) SpawnOffline();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (!listening || nm == null) return;
        if (nm.SceneManager != null)
        {
            nm.SceneManager.OnSceneEvent -= HandleSceneEvent;
            nm.SceneManager.OnLoadEventCompleted -= HandleLoadCompleted;
        }
        nm.OnClientDisconnectCallback -= HandleDisconnect;
    }

    private void Update()
    {
        // ③ 안전장치: 접속은 했는데 플레이어가 없는 사람
        if (!listening || nm == null || !nm.IsServer || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.5f;

        foreach (var id in nm.ConnectedClientsIds)
        {
            if (spawned.Contains(id)) continue;
            if (nm.ConnectedClients.TryGetValue(id, out var client) && client.PlayerObject != null)
            {
                spawned.Add(id);
                continue;
            }

            // 아직 씬을 불러오는 중이면 기다림 (너무 오래 걸리면 그냥 스폰)
            if (!firstSeen.ContainsKey(id)) firstSeen[id] = Time.unscaledTime;
            bool waitedTooLong = Time.unscaledTime - firstSeen[id] > forceSpawnAfter;
            if (!loaded.Contains(id) && !waitedTooLong) continue;

            Log($"플레이어가 없는 접속자 발견 → 스폰 (client {id}{(waitedTooLong ? ", 로드 신호 없음" : "")})");
            SpawnFor(id);
        }
    }

    // ───────── 네트워크 ─────────

    private void HandleSceneEvent(SceneEvent e)
    {
        // ① 클라이언트 한 명이 이 씬 로드를 끝냄
        if (e.SceneEventType != SceneEventType.LoadComplete) return;
        if (e.SceneName != gameObject.scene.name) return;
        Log($"씬 로드 완료: client {e.ClientId}");
        loaded.Add(e.ClientId);
        SpawnFor(e.ClientId);
    }

    private void HandleLoadCompleted(string sceneName, LoadSceneMode mode,
                                     List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameObject.scene.name) return;

        Log($"전체 로드 완료: 완료 [{string.Join(", ", clientsCompleted)}], 시간초과 [{string.Join(", ", clientsTimedOut ?? new List<ulong>())}]");

        // ② 모든 클라이언트 완료 (①에서 이미 스폰됐으면 건너뜀)
        foreach (var clientId in clientsCompleted)
        {
            loaded.Add(clientId);
            SpawnFor(clientId);
        }
    }

    private void SpawnFor(ulong clientId)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[NetworkPlayerSpawner] Player Prefab이 비어 있습니다.", this);
            return;
        }
        if (!nm.ConnectedClientsIds.Contains(clientId)) return;
        if (!spawned.Add(clientId)) return;   // 이미 스폰됨

        // 접속 순서대로 자리 배정 (호스트 0번)
        var order = nm.ConnectedClientsIds.OrderBy(id => id).ToList();
        GetPoint(order.IndexOf(clientId), out var pos, out var rot);

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
        Log($"스폰 완료: client {clientId}");
    }

    private void HandleDisconnect(ulong clientId)
    {
        // 플레이어 오브젝트는 NGO가 자동으로 제거. 재접속 시 다시 스폰할 수 있게 기록만 지움
        spawned.Remove(clientId);
        loaded.Remove(clientId);
        firstSeen.Remove(clientId);
    }

    private void Log(string msg)
    {
        if (verboseLog) Debug.Log($"[NetworkPlayerSpawner] {msg}", this);
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
