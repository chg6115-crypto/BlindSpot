using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// anti-camp 천장 조명 트랩. 빈 오브젝트(AntiCampTrap)에 붙이고, 전등(AntiCampLamp)들을 그 자식으로 둔다.
///
///  [판정] 플레이어가 한 지점에서 Radius(1m) 안에 Dwell Time(3초) 이상 머물면 발동
///         - 1m 밖으로 움직이면 그 위치가 새 기준점이 되고 시간도 처음부터
///         - 마네킹 / 죽은 플레이어 / 라운드 진행 중(Playing)이 아닐 때는 제외
///  [전등] 비어 있는(작동 중이 아닌) 전등 중 그 지점이 가려지지 않은 가장 가까운 전등을 사용
///         - 전등 바로 아래 기준 Max Angle(55°) 밖은 맡지 않음 (먼 전등이 비스듬히 비추지 않게)
///           현재 전등 10개 배치에서 바닥 전체가 들어오는 최소 각도는 약 51°
///  [연출] 예고(Warning Time): 전등 머리가 대상을 따라 돎 (+ 지지직, Flash Count만큼 번쩍임 - 기본 0)
///         조명(Light Time): 대상을 따라가며 비추고, 바닥 원 반경이 시작 반경 → Max Radius(3m)로 퍼짐
///         - 빛에 RevealLight가 붙어 있어 빛 안 캐릭터가 보임 (예고 번쩍임으로는 노출 안 됨)
///         - 조명이 끝날 때 그 사람이 아직 그 자리(Radius 안)에 있으면 꺼지지 않고 이어서 비춤 (다음 단계 반경)
///         - 같은 라운드에서 걸린 횟수만큼 시작 반경이 커짐 (1m → 2m → 3m). 라운드가 바뀌면 초기화
///  [멀티] 판정은 호스트(싱글은 자신)가 하고, 전등 번호 + 지점 + 대상 + 시작 반경을 참가자에게 보냄 (RoundNetworkSync)
///         전등 번호는 이 오브젝트 아래 순서 → 모든 화면에서 같은 씬이라 같음
/// </summary>
[DisallowMultipleComponent]
public class AntiCampTrap : MonoBehaviour
{
    [System.Serializable]
    public struct BeamSettings
    {
        [Tooltip("처음 걸렸을 때 바닥 원의 시작 반경 (m)")]
        [Min(0.2f)] public float radius;
        [Tooltip("조명 동안 퍼지는 최대 반경 (m)")]
        [Min(0.2f)] public float maxRadius;
        [Tooltip("예고 시간 (초). 번쩍임이 끝난 뒤 남은 시간은 꺼진 상태")]
        [Min(0f)] public float warningTime;
        [Tooltip("예고 동안 전등 머리가 대상 쪽으로 처음 도는 시간 (초)")]
        [Min(0f)] public float turnTime;
        [Tooltip("예고 번쩍임 횟수 (0 = 번쩍임 없이 머리만 돌아감)")]
        [Min(0)] public int flashCount;
        [Tooltip("번쩍일 때 켜져 있는 시간 (초)")]
        [Min(0.01f)] public float flashOnTime;
        [Tooltip("번쩍임 사이 꺼져 있는 시간 (초)")]
        [Min(0.01f)] public float flashOffTime;
        [Tooltip("조명 시간 (초)")]
        [Min(0.1f)] public float lightTime;
        [Tooltip("바닥에서의 밝기 (전등 높이에 맞춰 Light 밝기를 자동 보정)")]
        [Min(0f)] public float intensity;
        [Tooltip("예고 번쩍임 밝기 (조명 밝기 대비 비율, 0.7 미만이면 번쩍임으로는 노출 안 됨)")]
        [Range(0.05f, 0.6f)] public float warningIntensityRatio;
        [Tooltip("원 가장자리 부드러움 (안쪽 원뿔 비율, 작을수록 부드러움)")]
        [Range(0f, 1f)] public float innerAngleRatio;
        public Color color;
        [Tooltip("조명 그림자 (주변 물체 그림자가 생겨 실감 ↑)")]
        public bool shadows;
        [Tooltip("지지직 소리가 들리는 거리 (m)")]
        [Min(1f)] public float hearDistance;
        [Range(0f, 1f)] public float volume;
    }

    public static AntiCampTrap Instance { get; private set; }

    [Header("판정")]
    [Tooltip("이 반경(m) 안에 머무는 것으로 봄")]
    [SerializeField, Min(0.1f)] private float radius = 1f;
    [Tooltip("이 시간(초) 이상 머물면 발동")]
    [SerializeField, Min(0.1f)] private float dwellTime = 3f;
    [Tooltip("전등 바로 아래 기준 이 각도(°) 안의 지점만 맡음. 넓힐수록 먼 전등도 비스듬히 비춤")]
    [SerializeField, Range(10f, 89f)] private float maxAngle = 55f;
    [Tooltip("가림 검사에서 무시할 레이어 (Character 등)")]
    [SerializeField] private LayerMask blockMask = ~(1 << 6);

    [Header("연출")]
    [SerializeField] private BeamSettings beam = new BeamSettings
    {
        radius = 1f,
        maxRadius = 3f,
        warningTime = 1f,
        turnTime = 0.4f,
        flashCount = 0,
        flashOnTime = 0.2f,
        flashOffTime = 0.2f,
        lightTime = 3f,
        intensity = 3f,
        warningIntensityRatio = 0.5f,
        innerAngleRatio = 0.6f,
        color = new Color(0.85f, 0.93f, 1f),
        shadows = true,
        hearDistance = 25f,
        volume = 0.8f,
    };
    [Tooltip("같은 라운드에서 다시 걸릴 때마다 시작 반경이 이만큼 커짐 (m). 최대는 Max Radius")]
    [SerializeField, Min(0f)] private float radiusPerCatch = 1f;
    [Tooltip("지지직 소리 (여러 개면 랜덤, 반복 재생). 비우면 소리 없이 번쩍임만")]
    [SerializeField] private AudioClip[] crackleClips;

    [Header("디버그")]
    [SerializeField] private bool drawGizmos = true;

    private AntiCampLamp[] lamps;
    private readonly Dictionary<Health, Camp> camps = new Dictionary<Health, Camp>();
    private readonly Dictionary<Health, int> catches = new Dictionary<Health, int>();   // 이번 라운드에 걸린 횟수
    private readonly List<Health> players = new List<Health>();
    private readonly List<Health> removeBuffer = new List<Health>();
    private readonly Health[] lampTarget = new Health[32];
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private float nextScan;
    private RoundManager hookedRound;

    private class Camp
    {
        public Vector3 anchor;
        public float since;
    }

    /// <summary>멀티 참가자면 판정하지 않고 호스트 신호만 따름</summary>
    private static bool IsFollower
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening && !nm.IsServer;
        }
    }

    // ───────── 초기화 ─────────

    private void Awake()
    {
        Instance = this;
        lamps = GetComponentsInChildren<AntiCampLamp>(true);
        if (lamps.Length == 0) Debug.LogWarning("[AntiCampTrap] 자식에 AntiCampLamp가 없습니다.", this);
        if (lamps.Length > lampTarget.Length) Debug.LogWarning("[AntiCampTrap] 전등은 32개까지 사용합니다.", this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (hookedRound != null) hookedRound.OnRoundStart -= HandleRoundStart;
    }

    private void HandleRoundStart(int round) => StopAll();

    /// <summary>모든 전등 끄고 판정 초기화</summary>
    public void StopAll()
    {
        if (lamps != null) foreach (var l in lamps) if (l != null) l.Stop();
        System.Array.Clear(lampTarget, 0, lampTarget.Length);
        camps.Clear();
        catches.Clear();
    }

    // ───────── 판정 (호스트 / 싱글) ─────────

    private void Update()
    {
        var rm = RoundManager.Instance;
        if (rm != hookedRound)
        {
            if (hookedRound != null) hookedRound.OnRoundStart -= HandleRoundStart;
            hookedRound = rm;
            if (rm != null) rm.OnRoundStart += HandleRoundStart;
        }

        // 끝난 전등의 대상 해제
        for (int i = 0; i < lamps.Length && i < lampTarget.Length; i++)
            if (lampTarget[i] != null && (lamps[i] == null || !lamps[i].IsBusy)) lampTarget[i] = null;

        if (IsFollower || lamps.Length == 0) return;
        if (rm == null || rm.CurrentState != RoundManager.State.Playing) { camps.Clear(); return; }

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 1f;
            players.Clear();
            foreach (var h in FindObjectsByType<Health>(FindObjectsSortMode.None))
                if (h != null && !h.IsMannequin) players.Add(h);
        }

        float now = Time.time;
        foreach (var h in players)
        {
            if (h == null) continue;
            if (h.IsDead || !h.gameObject.activeInHierarchy) { camps.Remove(h); continue; }

            Vector3 p = h.transform.position;
            if (!camps.TryGetValue(h, out var c))
            {
                camps[h] = new Camp { anchor = p, since = now };
                continue;
            }
            if ((p - c.anchor).sqrMagnitude > radius * radius)
            {
                c.anchor = p;
                c.since = now;
                continue;
            }
            if (now - c.since < dwellTime || IsTargeted(h)) continue;

            if (TryTrigger(h, c.anchor))
            {
                c.anchor = p;      // 조명이 끝난 뒤에도 그대로 있으면 다시 걸림
                c.since = now;
            }
        }

        // 사라진 플레이어 정리
        removeBuffer.Clear();
        foreach (var kv in camps) if (kv.Key == null) removeBuffer.Add(kv.Key);
        foreach (var k in removeBuffer) camps.Remove(k);
    }

    private bool IsTargeted(Health h)
    {
        for (int i = 0; i < lamps.Length && i < lampTarget.Length; i++)
            if (lampTarget[i] == h) return true;
        return false;
    }

    private bool TryTrigger(Health target, Vector3 spot)
    {
        int best = -1;
        float bestDist = float.MaxValue;
        Vector3 chest = spot + Vector3.up * 1.0f;
        for (int i = 0; i < lamps.Length && i < lampTarget.Length; i++)
        {
            var l = lamps[i];
            if (l == null || !l.isActiveAndEnabled || l.IsBusy) continue;
            float d = (l.Origin - spot).sqrMagnitude;
            if (d >= bestDist) continue;
            if (!InAngle(l.Origin, spot)) continue;
            if (IsBlocked(l, chest)) continue;
            best = i;
            bestDist = d;
        }
        if (best < 0) return false;

        catches.TryGetValue(target, out int count);
        count++;
        catches[target] = count;
        float startRadius = Mathf.Min(beam.radius + radiusPerCatch * (count - 1), Mathf.Max(beam.radius, beam.maxRadius));

        lampTarget[best] = target;
        PlayLamp(best, spot, target.transform, startRadius);

        var ch = RoundNetworkSync.Channel;
        if (ch != null)
        {
            var no = target.GetComponent<NetworkObject>();
            ch.SendAntiCamp(best, spot, no != null && no.IsSpawned ? no.NetworkObjectId : ulong.MaxValue, startRadius);
        }
        return true;
    }

    /// <summary>전등 바로 아래 기준 Max Angle 안인지</summary>
    private bool InAngle(Vector3 origin, Vector3 point)
    {
        Vector3 d = point - origin;
        if (d.y >= -0.01f) return false;   // 전등보다 위
        float horizontal = new Vector2(d.x, d.z).magnitude;
        return Mathf.Atan2(horizontal, -d.y) * Mathf.Rad2Deg <= maxAngle;
    }

    private bool IsBlocked(AntiCampLamp lamp, Vector3 point)
    {
        Vector3 from = lamp.Origin;
        Vector3 d = point - from;
        float dist = d.magnitude;
        if (dist < 0.05f) return false;
        int n = Physics.RaycastNonAlloc(from, d / dist, hits, dist, blockMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = hits[i].collider;
            if (col == null) continue;
            if (col.transform.IsChildOf(lamp.transform)) continue;                  // 전등 자신
            if (col.GetComponentInParent<Health>() != null) continue;               // 캐릭터/마네킹
            if (col.GetComponentInParent<LightPassThrough>() != null) continue;     // 유리 등
            return true;
        }
        return false;
    }

    // ───────── 연출 (모든 화면) ─────────

    /// <summary>전등 번호 + 지점 + 대상으로 연출 (호스트 / 싱글)</summary>
    public void PlayLamp(int index, Vector3 spot, Transform target, float startRadius)
    {
        if (lamps == null || index < 0 || index >= lamps.Length || lamps[index] == null) return;
        lamps[index].Play(spot, target, startRadius, radius, radiusPerCatch, beam, crackleClips, OnLampContinue);
    }

    /// <summary>전등이 끄지 않고 이어서 비출 때: 적발 횟수 +1, 머문 시간 초기화 (호스트 / 싱글에서만 의미 있음)</summary>
    private void OnLampContinue(Transform target)
    {
        if (target == null || !target.TryGetComponent(out Health h)) return;
        catches.TryGetValue(h, out int count);
        catches[h] = count + 1;
        if (camps.TryGetValue(h, out var c)) { c.anchor = target.position; c.since = Time.time; }
    }

    /// <summary>참가자: 호스트 신호로 연출 (RoundNetworkSync가 호출). 대상은 네트워크 오브젝트 id로 찾음</summary>
    public void PlayLampFromHost(int index, Vector3 spot, ulong targetNetId, float startRadius)
    {
        Transform target = null;
        var nm = NetworkManager.Singleton;
        if (targetNetId != ulong.MaxValue && nm != null && nm.SpawnManager != null &&
            nm.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out var obj) && obj != null)
            target = obj.transform;
        PlayLamp(index, spot, target, startRadius);
    }

    // ───────── 디버그 ─────────

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        foreach (var l in GetComponentsInChildren<AntiCampLamp>(true))
        {
            Vector3 o = l.Origin;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(o, 0.25f);

            // 맡는 범위: 바닥에 Max Angle 원
            float floorY = Physics.Raycast(o + Vector3.down * 0.5f, Vector3.down, out var hit, 50f, blockMask, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
            float h = o.y - floorY;
            if (h > 0.1f)
            {
                float r = h * Mathf.Tan(maxAngle * Mathf.Deg2Rad);
                Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.25f);
                DrawCircle(new Vector3(o.x, floorY + 0.05f, o.z), r);
            }

            // 작동 중: 전등 → 대상 선 (예고 노랑 / 조명 빨강) + 반경 원
            if (Application.isPlaying && l.IsBusy)
            {
                Vector3 p = l.CurrentTarget != null ? l.CurrentTarget.position : l.CurrentSpot;
                Gizmos.color = l.IsLit ? Color.red : Color.yellow;
                Gizmos.DrawLine(o, p);
                DrawCircle(p + Vector3.up * 0.06f, l.CurrentRadius);
#if UNITY_EDITOR
                string who = l.CurrentTarget != null ? l.CurrentTarget.name : "?";
                Handles.color = Gizmos.color;
                Handles.Label(p + Vector3.up * 2.2f, $"{l.name} → {who}  r {l.CurrentRadius:0.0}m {(l.IsLit ? "LIT" : "WARN")}");
#endif
            }
        }

        if (!Application.isPlaying) return;
        foreach (var kv in camps)
        {
            if (kv.Key == null) continue;
            float k = Mathf.Clamp01((Time.time - kv.Value.since) / dwellTime);
            Gizmos.color = Color.Lerp(Color.green, Color.red, k);
            Gizmos.DrawWireSphere(kv.Value.anchor + Vector3.up * 0.05f, radius);
        }
    }

    private static void DrawCircle(Vector3 center, float r, int seg = 48)
    {
        Vector3 prev = center + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
