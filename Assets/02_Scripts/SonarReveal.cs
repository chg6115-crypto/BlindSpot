using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 소나(Q) 실루엣. Player_FPS 프리팹 루트에 붙인다 (SonarSkill 옆).
///
///  - 내 화면에서만 동작: 내가 Q를 쓴 순간부터 펄스가 퍼지는 동안, 펄스 앞부분이 닿은 상대를 찾음
///  - 대상: 상대 플레이어 + F6 디버그 봇 (Mannequin 태그, 내 캐릭터, 죽은 대상은 제외)
///  - 닿은 순간 몸 자세를 스냅샷으로 떠서 그 자리에 Hold Time 동안 유지 → Fade Time 동안 사라짐
///    (상대가 움직여도 실루엣은 그 자리에 고정)
///  - 벽 너머는 안 보임: ① 소나 중심 → 상대 몸으로 광선 검사 ② 셰이더 깊이 검사 (ZTest)
///  - 들킨 쪽에는 아무 알림 없음. 네트워크 신호도 없음 (상대 위치/애니메이션은 이미 동기화됨)
///
/// 스냅샷 메시는 풀에서 꺼내 재사용 (매번 새로 만들지 않음).
/// </summary>
[DisallowMultipleComponent]
public class SonarReveal : MonoBehaviour
{
    [Header("표시")]
    [Tooltip("BlindSpot/SonarHologram 셰이더 머티리얼 (SonarHologram_Mat)")]
    [SerializeField] private Material hologramMaterial;
    [Tooltip("실루엣이 그대로 유지되는 시간 (초)")]
    [SerializeField, Min(0f)] private float holdTime = 0.25f;
    [Tooltip("유지 후 사라지는 시간 (초). 0이면 바로 사라짐")]
    [SerializeField, Min(0f)] private float fadeTime = 0.15f;
    [Tooltip("3인칭 총 등 일반 메시도 같이 표시")]
    [SerializeField] private bool includeMeshRenderers = true;

    [Header("벽 검사")]
    [Tooltip("벽으로 판정할 레이어 (Character 레이어는 제외)")]
    [SerializeField] private LayerMask wallMask = ~(1 << 6);
    [Tooltip("광선 출발 높이 (소나 중심 기준, m)")]
    [SerializeField] private float originHeight = 1.2f;
    [Tooltip("상대 몸에서 검사할 높이들 (발 기준, m). 하나라도 보이면 표시")]
    [SerializeField] private float[] targetHeights = { 0.5f, 1.0f, 1.6f };
    [Tooltip("펄스가 닿았는지 볼 때 상대 기준 높이 (발 기준, m)")]
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
        public float startTime;
    }

    private SonarSkill skill;
    private Health myHealth;

    // 진행 중인 펄스
    private bool pulseActive;
    private Vector3 pulseOrigin;
    private float pulseRadius, pulseExpand, pulseStart;
    private readonly List<Health> candidates = new List<Health>();

    // 표시 중인 실루엣
    private readonly List<Ghost> ghosts = new List<Ghost>();
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
        for (int i = ghosts.Count - 1; i >= 0; i--) ReleaseGhost(i);
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
        pulseRadius = skill.Radius;
        pulseExpand = Mathf.Max(0.01f, skill.ExpandTime);
        pulseStart = Time.time;

        // 대상 후보: 마네킹 / 나 / 죽은 대상 제외
        candidates.Clear();
        var all = FindObjectsByType<Health>(FindObjectsInactive.Exclude);
        foreach (var h in all)
        {
            if (!IsValidTarget(h)) continue;
            candidates.Add(h);
        }

        if (verboseLog) Debug.Log($"[SonarReveal] 소나 사용: 후보 {candidates.Count}명", this);
    }

    private bool IsValidTarget(Health h)
    {
        if (h == null || !h.isActiveAndEnabled) return false;
        if (h.IsDead || h.IsMannequin) return false;
        if (h.transform.root == transform.root) return false;   // 내 캐릭터
        return true;
    }

    // ───────── 매 프레임 ─────────

    private void LateUpdate()
    {
        if (pulseActive) UpdatePulse();
        DrawGhosts();
    }

    private void UpdatePulse()
    {
        // SoundPulseManager와 같은 퍼짐 곡선 (ease-out)
        float e = Mathf.Clamp01((Time.time - pulseStart) / pulseExpand);
        float front = pulseRadius * (1f - (1f - e) * (1f - e));

        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            var h = candidates[i];
            if (!IsValidTarget(h)) { candidates.RemoveAt(i); continue; }

            Vector3 p = h.transform.position + Vector3.up * reachHeight;
            float dist = Vector3.Distance(pulseOrigin, p);
            if (dist > front) continue;   // 아직 안 닿음

            candidates.RemoveAt(i);
            if (HasLineOfSight(h)) Snapshot(h);
            else if (verboseLog) Debug.Log($"[SonarReveal] {h.name}: 벽에 가려짐", this);
        }

        if (e >= 1f)
        {
            // 끝까지 퍼졌는데 못 닿은 대상 = 범위 밖
            candidates.Clear();
            pulseActive = false;
        }
    }

    // ───────── 벽 검사 ─────────

    private bool HasLineOfSight(Health target)
    {
        Vector3 from = pulseOrigin + Vector3.up * originHeight;
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

    private void Snapshot(Health target)
    {
        var ghost = ghostPool.Count > 0 ? ghostPool.Pop() : new Ghost();
        ghost.parts.Clear();
        ghost.startTime = Time.time;

        rendererBuffer.Clear();
        target.GetComponentsInChildren(false, rendererBuffer);

        foreach (var r in rendererBuffer)
        {
            if (r == null || !r.enabled || r.forceRenderingOff) continue;
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
            return;
        }

        ghosts.Add(ghost);
        if (verboseLog) Debug.Log($"[SonarReveal] {target.name} 실루엣 표시 (메시 {ghost.parts.Count}개)", this);
    }

    private Mesh GetMesh()
    {
        if (meshPool.Count > 0) return meshPool.Pop();
        var m = new Mesh { name = "SonarGhost" };
        m.MarkDynamic();
        allMeshes.Add(m);
        return m;
    }

    private void ReleaseGhost(int index)
    {
        var g = ghosts[index];
        foreach (var part in g.parts)
            if (part.pooled && part.mesh != null) meshPool.Push(part.mesh);
        g.parts.Clear();
        ghosts.RemoveAt(index);
        ghostPool.Push(g);
    }

    // ───────── 그리기 ─────────

    private void DrawGhosts()
    {
        if (ghosts.Count == 0 || hologramMaterial == null) return;

        float life = holdTime + fadeTime;
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            var g = ghosts[i];
            float age = Time.time - g.startTime;
            if (age >= life) { ReleaseGhost(i); continue; }

            float alpha = age <= holdTime || fadeTime <= 0f ? 1f : 1f - (age - holdTime) / fadeTime;
            mpb.SetFloat(AlphaId, alpha);

            var rp = new RenderParams(hologramMaterial)
            {
                matProps = mpb,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = 0
            };

            foreach (var part in g.parts)
                for (int s = 0; s < part.subMeshCount; s++)
                    Graphics.RenderMesh(rp, part.mesh, s, part.matrix);
        }
    }
}
