using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 소나(Q) 실루엣. Player_FPS 프리팹 루트에 붙인다 (SonarSkill 옆).
///
///  - 내 화면에서만 동작: Q를 쓴 순간부터 판정 파동이 일정한 속도로 맵 전체에 퍼짐
///    (외곽선 효과의 반경과 별개. 속도 = SonarSkill 반경 ÷ 퍼짐 시간 → 기본 초당 20m, 먼 상대일수록 늦게 보임)
///  - 파동이 닿은 상대를 Frame Count(3)번, Frame Interval(0.2초)마다 다시 찍어서 보여줌
///    → 끊기는 스톱모션처럼 3단계로 움직이는 모습 (총 0.6초). 각 프레임은 다음 프레임이 찍힐 때 교체
///  - 대상: 상대 플레이어 + F6 디버그 봇 (Mannequin 태그, 내 캐릭터, 죽은 대상은 제외)
///  - 벽 너머는 안 보임: ① 프레임을 찍을 때마다 소나 중심 → 상대 몸 광선 검사 ② 셰이더 깊이 검사 (ZTest)
///  - 들킨 쪽에는 아무 알림 없음. 네트워크 신호도 없음 (상대 위치/애니메이션은 이미 동기화됨)
///
/// 몸이 숨겨져 있어도(CharacterVisibility) 몸 렌더러를 직접 스냅샷하므로 그대로 보임.
/// 스냅샷 메시는 풀에서 꺼내 재사용.
/// </summary>
[DisallowMultipleComponent]
public class SonarReveal : MonoBehaviour
{
    [Header("표시")]
    [Tooltip("BlindSpot/SonarHologram 셰이더 머티리얼 (SonarHologram_Mat)")]
    [SerializeField] private Material hologramMaterial;
    [Tooltip("한 상대를 몇 번 찍어서 보여줄지 (스톱모션 프레임 수)")]
    [SerializeField, Min(1)] private int frameCount = 3;
    [Tooltip("프레임 하나를 보여주는 시간 = 다음 프레임을 찍는 간격 (초)")]
    [SerializeField, Min(0.02f)] private float frameInterval = 0.2f;
    [Tooltip("3인칭 총 등 일반 메시도 같이 표시")]
    [SerializeField] private bool includeMeshRenderers = true;

    [Header("판정 파동")]
    [Tooltip("판정이 퍼지는 속도 (m/초). 0이면 SonarSkill의 반경 ÷ 퍼짐 시간 (기본 20m/초)")]
    [SerializeField, Min(0f)] private float revealSpeed = 0f;
    [Tooltip("판정 최대 거리 (m). 맵 전체를 덮을 만큼 크게")]
    [SerializeField, Min(1f)] private float maxRevealDistance = 150f;

    [Header("벽 검사")]
    [Tooltip("벽으로 판정할 레이어 (Character 레이어는 제외)")]
    [SerializeField] private LayerMask wallMask = ~(1 << 6);
    [Tooltip("광선 출발 높이 (소나 중심 기준, m)")]
    [SerializeField] private float originHeight = 1.2f;
    [Tooltip("상대 몸에서 검사할 높이들 (발 기준, m). 하나라도 보이면 표시")]
    [SerializeField] private float[] targetHeights = { 0.5f, 1.0f, 1.6f };
    [Tooltip("파동이 닿았는지 볼 때 상대 기준 높이 (발 기준, m)")]
    [SerializeField] private float reachHeight = 1.0f;

    [Header("디버그")]
    [SerializeField] private bool verboseLog = false;

    // ───────── 내부 ─────────

    private struct Part
    {
        public Mesh mesh;
        public Matrix4x4 matrix;
        public int subMeshCount;
        public bool pooled;   // 스냅샷 메시(풀 반환 대상)인지
    }

    private class Ghost
    {
        public readonly List<Part> parts = new List<Part>();
    }

    /// <summary>파동이 닿은 상대 하나의 스톱모션 진행 상태</summary>
    private class Track
    {
        public Health target;
        public int framesTaken;
        public float nextTime;   // 다음 프레임을 찍을 시각 (마지막 프레임 이후엔 사라질 시각)
        public Ghost ghost;      // 지금 보여주는 프레임 (가려졌으면 null)
        public Vector3 origin;   // 이 상대를 잡은 소나 중심 (벽 검사용)
    }

    private SonarSkill skill;
    private Health myHealth;

    // 진행 중인 판정 파동
    private bool pulseActive;
    private Vector3 pulseOrigin;
    private float pulseSpeed, pulseStart;
    private readonly List<Health> candidates = new List<Health>();

    // 스톱모션 진행 중인 상대들
    private readonly List<Track> tracks = new List<Track>();
    private readonly Stack<Track> trackPool = new Stack<Track>();
    private readonly Stack<Ghost> ghostPool = new Stack<Ghost>();
    private readonly Stack<Mesh> meshPool = new Stack<Mesh>();
    private readonly List<Mesh> allMeshes = new List<Mesh>();

    private MaterialPropertyBlock mpb;
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private readonly List<Renderer> rendererBuffer = new List<Renderer>();

    // ───────── 초기화 ─────────

    private void Awake()
    {
        skill = GetComponent<SonarSkill>();
        myHealth = GetComponent<Health>();
        mpb = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        if (skill != null) skill.OnUsed += HandleSonarUsed;
    }

    private void OnDisable()
    {
        if (skill != null) skill.OnUsed -= HandleSonarUsed;
        pulseActive = false;
        candidates.Clear();
        for (int i = tracks.Count - 1; i >= 0; i--) EndTrack(i);
    }

    private void OnDestroy()
    {
        foreach (var m in allMeshes) if (m != null) Destroy(m);
        allMeshes.Clear();
        meshPool.Clear();
    }

    // ───────── 소나 사용 ─────────

    /// <summary>SonarSkill.OnUsed (내 플레이어에서만 발생)</summary>
    private void HandleSonarUsed()
    {
        if (hologramMaterial == null)
        {
            Debug.LogWarning("[SonarReveal] Hologram Material이 비어 있습니다.", this);
            return;
        }
        if (myHealth != null && myHealth.IsDead) return;

        pulseActive = true;
        pulseOrigin = skill.LastOrigin;
        pulseSpeed = revealSpeed > 0f ? revealSpeed : skill.Radius / Mathf.Max(0.01f, skill.ExpandTime);
        pulseStart = Time.time;

        // 대상 후보: 마네킹 / 나 / 죽은 대상 제외
        candidates.Clear();
        foreach (var h in FindObjectsByType<Health>(FindObjectsInactive.Exclude))
        {
            if (!IsValidTarget(h)) continue;
            if (HasTrack(h)) continue;   // 이미 스톱모션 중이면 중복 X
            candidates.Add(h);
        }

        if (verboseLog) Debug.Log($"[SonarReveal] 소나 사용: 후보 {candidates.Count}명, 속도 {pulseSpeed:0.#}m/s", this);
    }

    private bool IsValidTarget(Health h)
    {
        if (h == null || !h.isActiveAndEnabled) return false;
        if (h.IsDead || h.IsMannequin) return false;
        if (h.transform.root == transform.root) return false;   // 내 캐릭터
        return true;
    }

    private bool HasTrack(Health h)
    {
        foreach (var t in tracks) if (t.target == h) return true;
        return false;
    }

    // ───────── 매 프레임 ─────────

    private void LateUpdate()
    {
        if (pulseActive) UpdatePulse();
        UpdateTracks();
        DrawGhosts();
    }

    /// <summary>판정 파동: 일정한 속도로 퍼지면서 닿은 상대의 스톱모션 시작</summary>
    private void UpdatePulse()
    {
        float front = pulseSpeed * (Time.time - pulseStart);

        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            var h = candidates[i];
            if (!IsValidTarget(h)) { candidates.RemoveAt(i); continue; }

            Vector3 p = h.transform.position + Vector3.up * reachHeight;
            if (Vector3.Distance(pulseOrigin, p) > front) continue;   // 아직 안 닿음

            candidates.RemoveAt(i);
            StartTrack(h);
        }

        if (front >= maxRevealDistance || candidates.Count == 0)
        {
            candidates.Clear();
            pulseActive = false;
        }
    }

    private void StartTrack(Health h)
    {
        var t = trackPool.Count > 0 ? trackPool.Pop() : new Track();
        t.target = h;
        t.framesTaken = 0;
        t.nextTime = Time.time;   // 바로 첫 프레임
        t.ghost = null;
        t.origin = pulseOrigin;
        tracks.Add(t);
    }

    /// <summary>스톱모션: Frame Interval마다 이전 프레임을 지우고 새로 찍음</summary>
    private void UpdateTracks()
    {
        float now = Time.time;
        for (int i = tracks.Count - 1; i >= 0; i--)
        {
            var t = tracks[i];
            if (now < t.nextTime) continue;

            ReleaseGhost(t.ghost);
            t.ghost = null;

            // 마지막 프레임까지 보여줬으면 종료
            if (t.framesTaken >= frameCount) { EndTrack(i); continue; }

            t.framesTaken++;
            t.nextTime += frameInterval;
            if (t.nextTime < now) t.nextTime = now + frameInterval;   // 프레임이 많이 밀렸을 때

            if (!IsValidTarget(t.target)) { EndTrack(i); continue; }   // 죽음 / 나감

            if (HasLineOfSight(t.target, t.origin))
                t.ghost = Snapshot(t.target);
            else if (verboseLog)
                Debug.Log($"[SonarReveal] {t.target.name}: 프레임 {t.framesTaken} 벽에 가려짐", this);
        }
    }

    private void EndTrack(int index)
    {
        var t = tracks[index];
        ReleaseGhost(t.ghost);
        t.ghost = null;
        t.target = null;
        tracks.RemoveAt(index);
        trackPool.Push(t);
    }

    // ───────── 벽 검사 ─────────

    private bool HasLineOfSight(Health target, Vector3 origin)
    {
        Vector3 from = origin + Vector3.up * originHeight;
        foreach (float height in targetHeights)
        {
            Vector3 to = target.transform.position + Vector3.up * height;
            if (!IsBlocked(from, to)) return true;
        }
        return false;
    }

    private bool IsBlocked(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 0.01f) return false;

        int n = Physics.RaycastNonAlloc(from, dir / len, hits, len, wallMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = hits[i].collider;
            if (col == null) continue;
            // 캐릭터/마네킹 몸은 벽이 아님
            if (col.GetComponentInParent<Health>() != null) continue;
            return true;
        }
        return false;
    }

    // ───────── 스냅샷 ─────────

    private Ghost Snapshot(Health target)
    {
        var ghost = ghostPool.Count > 0 ? ghostPool.Pop() : new Ghost();
        ghost.parts.Clear();

        // 몸 렌더러 목록: CharacterVisibility가 있으면 그 목록 (숨겨진 상태여도 찍음)
        rendererBuffer.Clear();
        var vis = target.GetComponent<CharacterVisibility>();
        bool fromVisibility = vis != null && vis.BodyRenderers.Count > 0;
        if (fromVisibility) { foreach (var r in vis.BodyRenderers) rendererBuffer.Add(r); }
        else target.GetComponentsInChildren(false, rendererBuffer);

        foreach (var r in rendererBuffer)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!fromVisibility && r.forceRenderingOff) continue;   // 목록이 없을 때만: 숨겨진 1인칭 총 등 제외
            if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;

            if (r is SkinnedMeshRenderer smr)
            {
                if (smr.sharedMesh == null) continue;
                var mesh = GetMesh();
                smr.BakeMesh(mesh);   // 현재 자세 그대로 (렌더러 위치·회전 기준, 크기 미포함)
                ghost.parts.Add(new Part
                {
                    mesh = mesh,
                    matrix = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one),
                    subMeshCount = mesh.subMeshCount,
                    pooled = true
                });
            }
            else if (includeMeshRenderers && r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                ghost.parts.Add(new Part
                {
                    mesh = mf.sharedMesh,
                    matrix = r.transform.localToWorldMatrix,   // 그 순간 위치 고정
                    subMeshCount = mf.sharedMesh.subMeshCount,
                    pooled = false
                });
            }
        }

        if (ghost.parts.Count == 0)
        {
            ghostPool.Push(ghost);
            return null;
        }

        if (verboseLog) Debug.Log($"[SonarReveal] {target.name} 프레임 표시 (메시 {ghost.parts.Count}개)", this);
        return ghost;
    }

    private Mesh GetMesh()
    {
        if (meshPool.Count > 0) return meshPool.Pop();
        var m = new Mesh { name = "SonarGhost" };
        m.MarkDynamic();
        allMeshes.Add(m);
        return m;
    }

    private void ReleaseGhost(Ghost g)
    {
        if (g == null) return;
        foreach (var part in g.parts)
            if (part.pooled && part.mesh != null) meshPool.Push(part.mesh);
        g.parts.Clear();
        ghostPool.Push(g);
    }

    // ───────── 그리기 ─────────

    private void DrawGhosts()
    {
        if (tracks.Count == 0 || hologramMaterial == null) return;

        mpb.SetFloat(AlphaId, 1f);
        var rp = new RenderParams(hologramMaterial)
        {
            matProps = mpb,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            layer = 0
        };

        foreach (var t in tracks)
        {
            if (t.ghost == null) continue;
            foreach (var part in t.ghost.parts)
                for (int s = 0; s < part.subMeshCount; s++)
                    Graphics.RenderMesh(rp, part.mesh, s, part.matrix);
        }
    }
}
