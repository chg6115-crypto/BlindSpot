using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 라운드마다 트랩 배치
///  ① 맵 트랩 (Bear, Lightning) : 미리 배치한 후보 중 Min~Max개 활성화
///  ② Reveal 트랩 (BlackSkull)  : 벽/바닥에 Min~Max개 랜덤 생성
///     - 벽: NavMesh 경계(벽 바로 앞)를 찾아 그 바깥쪽으로 Raycast → 회전된 컨테이너 벽도 자동 정렬
///     - 그림 네 귀퉁이까지 표면이 있는지 확인 → 끝에서 잘리지 않게
///     - 플레이어 시작 위치 근처 제외
///  디버그 8키: 모든 트랩 데칼 항상 표시 ON/OFF
/// Reveal 트랩 프리팹은 "루트 = 표면 위의 점, Z축 = 표면 안쪽" 구조여야 한다.
/// </summary>
public class TrapSpawner : MonoBehaviour
{
    [Header("① 맵 트랩 (미리 배치한 후보)")]
    [SerializeField] private TrapBase[] mapTrapCandidates;
    [SerializeField, Min(0)] private int mapTrapMin = 1;
    [SerializeField, Min(0)] private int mapTrapMax = 2;

    [Header("② Reveal 트랩 (랜덤 생성)")]
    [SerializeField] private TrapBase revealPrefab;
    [SerializeField, Min(0)] private int revealMin = 4;
    [SerializeField, Min(0)] private int revealMax = 8;
    [Tooltip("바닥에 붙을 확률 (나머지는 벽)")]
    [SerializeField, Range(0f, 1f)] private float floorChance = 0.4f;
    [Tooltip("벽 자리를 끝내 못 찾으면 바닥에 대신 배치")]
    [SerializeField] private bool fallbackToFloor = true;

    [Header("벽 탐색")]
    [Tooltip("벽으로 인정할 레이어 (컨테이너, 벽 = Obstacle)")]
    [SerializeField] private LayerMask wallMask;
    [Tooltip("NavMesh 경계에서 벽까지 광선을 쏘는 최대 거리")]
    [SerializeField] private float wallSearchDistance = 1.5f;
    [Tooltip("그림 중심이 붙는 높이 범위 (바닥 기준)")]
    [SerializeField] private Vector2 wallHeightRange = new Vector2(0.9f, 1.8f);
    [Tooltip("벽으로 인정할 최대 기울기 (법선의 y 절댓값, 0 = 완전 수직)")]
    [SerializeField, Range(0f, 1f)] private float maxWallNormalY = 0.3f;
    [Tooltip("귀퉁이 검사: 중심 지점과 벽 깊이 차이 허용치 (컨테이너 골판 요철 감안)")]
    [SerializeField] private float edgeDepthTolerance = 0.2f;

    [Header("배치 제한")]
    [SerializeField] private float minDistanceBetween = 3f;
    [SerializeField] private float minDistanceFromPlayer = 6f;
    [Tooltip("비워두면 Tag가 Player인 오브젝트 위치 사용")]
    [SerializeField] private Transform[] playerSpawns;
    [SerializeField, Min(1)] private int maxAttemptsPerTrap = 60;
    [SerializeField] private bool spawnOnStart = true;

    [Header("디버그")]
    [Tooltip("8키로 모든 트랩 데칼 항상 표시 ON/OFF")]
    [SerializeField] private bool debugKeys = true;

    private readonly List<TrapBase> revealPool = new List<TrapBase>();
    private readonly List<Vector3> usedPositions = new List<Vector3>();
    private Vector2 halfSize = new Vector2(0.75f, 0.8f);   // 그림 절반 크기 (프리팹 데칼에서 읽음)

    // NavMesh 삼각형 캐시
    private Vector3[] navVerts;
    private int[] navIdx;
    private float[] cumArea;
    private float totalArea;

    // ───────── Unity ─────────

    private void Awake()
    {
        TrapBase.DebugShowAll = false;   // Play 시작 시 항상 꺼진 상태로

        if (revealPrefab == null) return;

        // 그림 크기를 프리팹 데칼에서 읽어 귀퉁이 검사에 사용
        var dp = revealPrefab.GetComponentInChildren<DecalProjector>(true);
        if (dp != null) halfSize = new Vector2(dp.size.x, dp.size.y) * 0.5f;

        // 최대 개수만큼 미리 만들어 두고 라운드마다 재사용
        for (int i = 0; i < revealMax; i++)
        {
            var t = Instantiate(revealPrefab, transform);
            t.gameObject.SetActive(false);
            revealPool.Add(t);
        }
    }

    private void Start()
    {
        BuildNavTriangles();
        if (spawnOnStart) SpawnForRound();
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!debugKeys) return;
        var kb = Keyboard.current;
        if (kb == null || !kb.digit8Key.wasPressedThisFrame) return;

        TrapBase.DebugShowAll = !TrapBase.DebugShowAll;

        foreach (var t in revealPool) if (t != null) t.RefreshIdle();
        if (mapTrapCandidates != null)
            foreach (var t in mapTrapCandidates) if (t != null) t.RefreshIdle();

        Debug.Log($"[TrapSpawner] 트랩 데칼 항상 표시: {(TrapBase.DebugShowAll ? "ON" : "OFF")}");
#endif
    }

    // ───────── 라운드 ─────────

    public void SpawnForRound()
    {
        SpawnMapTraps();
        SpawnRevealTraps();
    }

    private void SpawnMapTraps()
    {
        if (mapTrapCandidates == null) return;

        var list = new List<TrapBase>();
        foreach (var t in mapTrapCandidates)
        {
            if (t == null) continue;
            t.gameObject.SetActive(false);
            list.Add(t);
        }
        Shuffle(list);

        int n = Mathf.Min(Random.Range(mapTrapMin, mapTrapMax + 1), list.Count);
        for (int i = 0; i < n; i++) list[i].gameObject.SetActive(true);
    }

    private void SpawnRevealTraps()
    {
        foreach (var t in revealPool) t.gameObject.SetActive(false);
        usedPositions.Clear();
        if (revealPool.Count == 0 || cumArea == null || cumArea.Length == 0) return;

        int want = Mathf.Min(Random.Range(revealMin, revealMax + 1), revealPool.Count);
        int placed = 0, onWall = 0;

        for (int i = 0; i < want; i++)
        {
            bool wantFloor = Random.value < floorChance;   // 트랩마다 한 번만 결정

            bool ok = TryFind(wantFloor, out Vector3 pos, out Quaternion rot);
            if (!ok && !wantFloor && fallbackToFloor)
                ok = TryFind(true, out pos, out rot);
            else if (ok && !wantFloor)
                onWall++;

            if (!ok) continue;

            var trap = revealPool[placed++];
            trap.transform.SetPositionAndRotation(pos, rot);
            trap.gameObject.SetActive(true);
            usedPositions.Add(pos);
        }

        Debug.Log($"[TrapSpawner] Reveal 트랩 {placed}/{want}개 배치 (벽 {onWall}, 바닥 {placed - onWall})", this);
    }

    // ───────── 표면 찾기 ─────────

    private bool TryFind(bool floor, out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = default;

        for (int a = 0; a < maxAttemptsPerTrap; a++)
        {
            Vector3 ground = RandomPointOnNavMesh();
            bool found = floor ? TryFloor(ground, out pos, out rot)
                               : TryWall(ground, out pos, out rot);
            if (found && IsValidPosition(pos)) return true;
        }
        return false;
    }

    private bool TryFloor(Vector3 ground, out Vector3 pos, out Quaternion rot)
    {
        Quaternion yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        pos = ground;
        rot = Quaternion.LookRotation(Vector3.down, yaw * Vector3.forward);

        // 네 귀퉁이가 모두 같은 높이의 바닥(NavMesh) 위인지 확인
        Vector3 right = yaw * Vector3.right;
        Vector3 fwd = yaw * Vector3.forward;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 corner = ground + right * (halfSize.x * sx) + fwd * (halfSize.y * sy);
                if (!NavMesh.SamplePosition(corner, out NavMeshHit h, 0.2f, NavMesh.AllAreas)) return false;
                if (Mathf.Abs(h.position.y - ground.y) > 0.1f) return false;
            }
        return true;
    }

    private bool TryWall(Vector3 ground, out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = default;

        // ① 가장 가까운 NavMesh 경계 = 벽이나 장애물 바로 앞
        if (!NavMesh.FindClosestEdge(ground, out NavMeshHit edge, NavMesh.AllAreas)) return false;

        Vector3 toWall = -edge.normal;          // 경계 법선은 걸을 수 있는 쪽을 향함 → 반대가 벽 쪽
        toWall.y = 0f;
        if (toWall.sqrMagnitude < 0.0001f) return false;
        toWall.Normalize();

        // ② 경계에서 벽 쪽으로 짧게 광선
        Vector3 origin = edge.position + Vector3.up * Random.Range(wallHeightRange.x, wallHeightRange.y) - toWall * 0.1f;
        if (!Physics.Raycast(origin, toWall, out RaycastHit hit, wallSearchDistance, wallMask, QueryTriggerInteraction.Ignore))
            return false;
        if (Mathf.Abs(hit.normal.y) > maxWallNormalY) return false;   // 기울어진 면, 천장 제외

        rot = Quaternion.LookRotation(-hit.normal, Vector3.up);       // Z = 벽 안쪽, 그림 위쪽 = 하늘
        pos = hit.point;

        // ③ 그림 네 귀퉁이에도 같은 벽면이 있는지 확인 → 위쪽 끝, 모서리에서 잘리지 않게
        Vector3 right = rot * Vector3.right;
        Vector3 up = rot * Vector3.up;
        Vector3 inward = rot * Vector3.forward;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 cornerPoint = hit.point + right * (halfSize.x * sx) + up * (halfSize.y * sy);
                Vector3 start = cornerPoint - inward * 0.5f;
                if (!Physics.Raycast(start, inward, out RaycastHit ch, 0.5f + edgeDepthTolerance, wallMask, QueryTriggerInteraction.Ignore))
                    return false;
                if (Vector3.Dot(ch.normal, hit.normal) < 0.9f) return false;   // 다른 면(모서리 너머)
            }

        // 그림 아래쪽 끝이 바닥 아래로 내려가지 않게
        if (hit.point.y - halfSize.y < edge.position.y + 0.05f) return false;

        return true;
    }

    private bool IsValidPosition(Vector3 p)
    {
        float sq = minDistanceBetween * minDistanceBetween;
        foreach (var u in usedPositions)
            if ((u - p).sqrMagnitude < sq) return false;

        float psq = minDistanceFromPlayer * minDistanceFromPlayer;
        foreach (var pp in GetPlayerPositions())
            if ((pp - p).sqrMagnitude < psq) return false;

        return true;
    }

    private IEnumerable<Vector3> GetPlayerPositions()
    {
        if (playerSpawns != null && playerSpawns.Length > 0)
        {
            foreach (var t in playerSpawns) if (t != null) yield return t.position;
        }
        else
        {
            foreach (var p in GameObject.FindGameObjectsWithTag("Player")) yield return p.transform.position;
        }
    }

    // ───────── NavMesh 랜덤 지점 ─────────

    private void BuildNavTriangles()
    {
        var tri = NavMesh.CalculateTriangulation();
        navVerts = tri.vertices;
        navIdx = tri.indices;

        int count = navIdx.Length / 3;
        cumArea = new float[count];
        totalArea = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = navVerts[navIdx[i * 3]], b = navVerts[navIdx[i * 3 + 1]], c = navVerts[navIdx[i * 3 + 2]];
            totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumArea[i] = totalArea;
        }
    }

    private Vector3 RandomPointOnNavMesh()
    {
        float r = Random.value * totalArea;
        int t = System.Array.BinarySearch(cumArea, r);
        if (t < 0) t = ~t;
        t = Mathf.Clamp(t, 0, cumArea.Length - 1);

        Vector3 a = navVerts[navIdx[t * 3]], b = navVerts[navIdx[t * 3 + 1]], c = navVerts[navIdx[t * 3 + 2]];
        float u = Random.value, v = Random.value;
        if (u + v > 1f) { u = 1f - u; v = 1f - v; }
        return a + (b - a) * u + (c - a) * v;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}