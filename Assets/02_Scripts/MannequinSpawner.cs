using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 라운드 시작 시 마네킹 배치
///  ① 지난 라운드 발사 위치 중 일부 (shotRatio 비율)
///  ② 나머지는 랜덤 위치
///     - Zones 비어 있음 : NavMesh 전체에서 랜덤 (Spawn Areas에 포함된 Area만)
///     - Zones 지정      : 지정한 Box Collider 구역 안에서 랜덤
///  공통: NavMesh 바닥 위 + 캡슐 겹침 체크 + 최소 거리
/// 멀티: 호스트만 배치를 계산하고 참가자는 SpawnLayout으로 같은 자리에 생성 (발사 기록도 호스트가 모두 모음)
/// </summary>
public class MannequinSpawner : MonoBehaviour
{
    public struct ShotRecord
    {
        public Vector3 position;
        public Vector3 forward;
        public ShotRecord(Vector3 p, Vector3 f) { position = p; forward = f; }
    }

    [Header("마네킹")]
    [SerializeField] private GameObject mannequinPrefab;
    [SerializeField, Min(0)] private int count = 8;
    [SerializeField] private bool spawnOnStart = true;

    [Header("배치 범위")]
    [Tooltip("비워두면 NavMesh 전체에서 랜덤 배치")]
    [SerializeField] private BoxCollider[] zones;
    [Tooltip("마네킹 배치에 쓸 NavMesh Area 이름 (NoSpawn 영역 제외용)")]
    [SerializeField] private string[] spawnAreas = { "Walkable" };

    [Header("지난 라운드 발사 위치 비율")]
    [SerializeField, Range(0f, 1f)] private float shotRatio = 0.4f;

    [Header("겹침 체크")]
    [Tooltip("겹치면 안 되는 레이어 (Obstacle, Character). Ground는 넣지 않음")]
    [SerializeField] private LayerMask blockMask;
    [SerializeField] private float capsuleRadius = 0.35f;
    [SerializeField] private float capsuleHeight = 1.8f;
    [SerializeField] private float navSampleRadius = 1.5f;
    [SerializeField, Min(1)] private int maxAttemptsPerMannequin = 30;

    [Header("거리 제한")]
    [SerializeField] private float minDistanceBetween = 3f;
    [SerializeField] private float minDistanceFromPlayer = 6f;
    [Tooltip("비워두면 Tag가 Player인 오브젝트 위치 사용")]
    [SerializeField] private Transform[] playerSpawns;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Vector3> usedPositions = new List<Vector3>();
    private List<ShotRecord> previousShots = new List<ShotRecord>();
    private readonly List<ShotRecord> currentShots = new List<ShotRecord>();

    // NavMesh 삼각형 캐시 (전체 랜덤용)
    private Vector3[] navVerts;
    private int[] navTris;
    private float[] cumulativeArea;
    private float totalArea;

    // ───────── Unity ─────────

    private void Start()
    {
        BuildNavTriangles();
        if (spawnOnStart) SpawnForRound();
    }

    // ───────── 라운드 연동용 (외부에서 호출) ─────────

    /// <summary>무기 스크립트가 발사할 때 호출</summary>
    public void RecordShot(Vector3 position, Vector3 forward)
    {
        currentShots.Add(new ShotRecord(position, forward));
    }

    /// <summary>라운드 종료 시 호출: 이번 기록을 다음 라운드용으로 넘김</summary>
    public void EndRound()
    {
        previousShots = new List<ShotRecord>(currentShots);
        currentShots.Clear();
    }

    /// <summary>라운드 시작 시 호출</summary>
    public void SpawnForRound()
    {
        if (mannequinPrefab == null)
        {
            Debug.LogWarning("[MannequinSpawner] Mannequin Prefab이 비어 있습니다.", this);
            return;
        }

        ClearSpawned();

        int fromShots = Mathf.Min(Mathf.RoundToInt(count * shotRatio), previousShots.Count);
        int placed = SpawnFromShots(fromShots);

        int remaining = count - placed;
        int failed = 0;
        for (int i = 0; i < remaining; i++)
        {
            if (TryGetRandomPoint(out Vector3 pos))
                Spawn(pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            else
                failed++;
        }

        if (failed > 0)
            Debug.LogWarning($"[MannequinSpawner] {failed}개 배치 실패. Count/거리 설정이나 NavMesh를 확인하세요.", this);
    }

    /// <summary>배치된 마네킹 전부 제거</summary>
    public void ClearSpawned()
    {
        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear();
        usedPositions.Clear();
    }

    // ───────── 네트워크 (RoundNetworkSync / PlayerNetworkCombat) ─────────

    /// <summary>호스트: 이번 라운드 배치 (위치, Y 회전)를 꺼냄</summary>
    public void GetLayout(List<Vector3> positions, List<float> yaws)
    {
        positions.Clear();
        yaws.Clear();
        foreach (var go in spawned)
        {
            if (go == null) continue;
            positions.Add(go.transform.position);
            yaws.Add(go.transform.eulerAngles.y);
        }
    }

    /// <summary>참가자: 호스트가 보낸 배치 그대로 생성 (순서 = 마네킹 번호)</summary>
    public void SpawnLayout(Vector3[] positions, float[] yaws)
    {
        ClearSpawned();
        if (mannequinPrefab == null || positions == null) return;
        for (int i = 0; i < positions.Length; i++)
        {
            float yaw = yaws != null && i < yaws.Length ? yaws[i] : 0f;
            Spawn(positions[i], Quaternion.Euler(0f, yaw, 0f));
        }
    }

    /// <summary>마네킹 번호 (없으면 -1)</summary>
    public int IndexOf(GameObject mannequin) => spawned.IndexOf(mannequin);

    /// <summary>번호로 마네킹 Health 찾기</summary>
    public Health GetMannequin(int index)
    {
        if (index < 0 || index >= spawned.Count || spawned[index] == null) return null;
        var h = spawned[index].GetComponent<Health>();
        return h != null ? h : spawned[index].GetComponentInChildren<Health>();
    }

    /// <summary>맵(NavMesh)을 런타임에 다시 구웠을 때 호출</summary>
    public void RefreshNavMesh() => BuildNavTriangles();

    // ───────── 발사 위치 배치 ─────────

    private int SpawnFromShots(int want)
    {
        if (want <= 0) return 0;

        // 순서를 섞어서 매번 다른 기록이 선택되게
        var pool = new List<ShotRecord>(previousShots);
        for (int i = 0; i < pool.Count; i++)
        {
            int j = Random.Range(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int placed = 0;
        foreach (var shot in pool)
        {
            if (placed >= want) break;

            // 점프 중에 쐈을 수도 있으니 바닥에 붙임
            if (!NavMesh.SamplePosition(shot.position, out NavMeshHit hit, navSampleRadius, GetAreaMask())) continue;
            if (!IsValid(hit.position)) continue;

            Vector3 f = shot.forward; f.y = 0f;
            Quaternion rot = f.sqrMagnitude > 0.001f ? Quaternion.LookRotation(f) : Quaternion.identity;
            Spawn(hit.position, rot);
            placed++;
        }
        return placed;
    }

    // ───────── 랜덤 위치 ─────────

    private bool TryGetRandomPoint(out Vector3 result)
    {
        result = default;
        bool useZones = zones != null && zones.Length > 0;
        if (!useZones && (cumulativeArea == null || cumulativeArea.Length == 0))
        {
            Debug.LogWarning("[MannequinSpawner] NavMesh가 없습니다. Bake 했는지 확인하세요.", this);
            return false;
        }

        for (int attempt = 0; attempt < maxAttemptsPerMannequin; attempt++)
        {
            Vector3 candidate;

            if (useZones)
            {
                var zone = zones[Random.Range(0, zones.Length)];
                if (zone == null) continue;
                Bounds bz = zone.bounds;

                candidate = new Vector3(
                    Random.Range(bz.min.x, bz.max.x),
                    bz.center.y,
                    Random.Range(bz.min.z, bz.max.z));

                // NavMesh 바닥에 붙이기 (벽 속, 공중 제외)
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navSampleRadius, GetAreaMask())) continue;

                // 붙인 결과가 구역 밖으로 튀어나가면 제외
                if (!bz.Contains(new Vector3(hit.position.x, bz.center.y, hit.position.z))) continue;

                candidate = hit.position;
            }
            else
            {
                candidate = RandomPointOnNavMesh(); // 이미 NavMesh 위의 점
            }

            if (!IsValid(candidate)) continue;

            result = candidate;
            return true;
        }
        return false;
    }

    private void BuildNavTriangles()
    {
        var tri = NavMesh.CalculateTriangulation();
        navVerts = tri.vertices;
        int allowedMask = GetAreaMask();

        var tris = new List<int>();
        var cum = new List<float>();
        totalArea = 0f;

        for (int i = 0; i < tri.areas.Length; i++)
        {
            if ((allowedMask & (1 << tri.areas[i])) == 0) continue;

            int i0 = tri.indices[i * 3], i1 = tri.indices[i * 3 + 1], i2 = tri.indices[i * 3 + 2];
            Vector3 a = navVerts[i0], b = navVerts[i1], c = navVerts[i2];

            totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            tris.Add(i0); tris.Add(i1); tris.Add(i2);
            cum.Add(totalArea);
        }

        navTris = tris.ToArray();
        cumulativeArea = cum.ToArray();
    }

    private Vector3 RandomPointOnNavMesh()
    {
        // 넓은 삼각형일수록 자주 뽑히게 (면적 비례)
        float r = Random.value * totalArea;
        int t = System.Array.BinarySearch(cumulativeArea, r);
        if (t < 0) t = ~t;
        t = Mathf.Clamp(t, 0, cumulativeArea.Length - 1);

        Vector3 a = navVerts[navTris[t * 3]];
        Vector3 b = navVerts[navTris[t * 3 + 1]];
        Vector3 c = navVerts[navTris[t * 3 + 2]];

        // 삼각형 안 균등 랜덤 점
        float u = Random.value, v = Random.value;
        if (u + v > 1f) { u = 1f - u; v = 1f - v; }
        return a + (b - a) * u + (c - a) * v;
    }

    private int GetAreaMask()
    {
        if (spawnAreas == null || spawnAreas.Length == 0) return NavMesh.AllAreas;

        int mask = 0;
        foreach (var a in spawnAreas)
        {
            int idx = NavMesh.GetAreaFromName(a);
            if (idx >= 0) mask |= 1 << idx;
        }
        return mask == 0 ? NavMesh.AllAreas : mask;
    }

    // ───────── 검사 ─────────

    private bool IsValid(Vector3 feet)
    {
        // 캡슐 겹침 체크 (트리거는 무시)
        Vector3 bottom = feet + Vector3.up * (capsuleRadius + 0.05f);
        Vector3 top = feet + Vector3.up * (capsuleHeight - capsuleRadius);
        if (Physics.CheckCapsule(bottom, top, capsuleRadius, blockMask, QueryTriggerInteraction.Ignore))
            return false;

        // 다른 마네킹과 거리
        float minSq = minDistanceBetween * minDistanceBetween;
        foreach (var p in usedPositions)
            if ((p - feet).sqrMagnitude < minSq) return false;

        // 플레이어와 거리
        float playerSq = minDistanceFromPlayer * minDistanceFromPlayer;
        foreach (var p in GetPlayerPositions())
            if ((p - feet).sqrMagnitude < playerSq) return false;

        return true;
    }

    private IEnumerable<Vector3> GetPlayerPositions()
    {
        // 직접 넣은 목록 → 없으면 RoundManager의 스폰 위치 사용
        Transform[] points = (playerSpawns != null && playerSpawns.Length > 0)
            ? playerSpawns
            : (RoundManager.Instance != null ? RoundManager.Instance.SpawnPoints : null);

        if (points != null)
            foreach (var t in points)
                if (t != null) yield return t.position;

        // 현재 플레이어 위치도 항상 피함
        foreach (var p in GameObject.FindGameObjectsWithTag("Player"))
            yield return p.transform.position;
    }

    // ───────── 생성 ─────────

    private void Spawn(Vector3 pos, Quaternion rot)
    {
        var go = Instantiate(mannequinPrefab, pos, rot, transform);
        spawned.Add(go);
        usedPositions.Add(pos);

        // 애니메이션 시작 시점을 랜덤으로 → 마네킹끼리 동작이 겹치지 않게
        var anim = go.GetComponentInChildren<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            anim.Update(0f);
            var st = anim.GetCurrentAnimatorStateInfo(0);
            anim.Play(st.fullPathHash, 0, Random.value);
        }
    }

    // ───────── 에디터 표시 ─────────

    private void OnDrawGizmosSelected()
    {
        if (zones == null) return;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
        foreach (var z in zones)
            if (z != null) Gizmos.DrawCube(z.bounds.center, z.bounds.size);
    }
}