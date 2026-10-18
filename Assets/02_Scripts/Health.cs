using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 체력. Player 프리팹 루트에 붙임 (마네킹 Variant도 자동으로 같이 가짐).
///  - 마네킹(Mannequin 태그): 체력 없음. 한 대 맞으면 데칼 발광 + 쓰러진 뒤 사라짐 (킬 기록 없음)
///  - 플레이어: 맞으면 체력 감소 (데칼 발광은 기본 꺼짐), 0이 되면 사망 → RoundManager에 킬 보고
///  - 로컬 플레이어(Player 태그)는 ScoreManager의 LocalPlayerId를 사용
///  - 디버그 봇은 SetupAsBot()으로 플레이어 등록
/// 공격하는 쪽은 TakeDamage(피해량, 공격자 GameObject)만 호출하면 됨 (근접, 총 공통).
///
/// [네트워크 5단계] 같은 오브젝트에 PlayerNetworkCombat이 있고 스폰된 상태면 (= 네트워크 플레이어)
///  - TakeDamage → 호스트에 전달, 체력은 호스트가 바꾼 값을 받아서 표시
///  - 사망 / 부활 / 킬 기록도 PlayerNetworkCombat이 호출 (NetDie / NetRevive)
///  - 마네킹: 맞힌 사람 화면에서 쓰러지고 MannequinHitLocally로 알림 → 다른 화면에서도 같은 마네킹이 쓰러짐
///  - 디버그 봇은 각자 컴퓨터에서만 처리
/// </summary>
public class Health : MonoBehaviour
{
    [Header("체력")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("맞았을 때 데칼 발광")]
    [Tooltip("플레이어가 맞으면 데칼 발광 (기본 꺼짐)")]
    [SerializeField] private bool flashPlayerDecalOnHit = false;
    [Tooltip("마네킹이 맞으면 데칼 발광")]
    [SerializeField] private bool flashMannequinDecalOnHit = true;
    [SerializeField, Min(1)] private int flashCount = 2;
    [SerializeField, Min(0.01f)] private float flashOnTime = 0.06f;
    [SerializeField, Min(0.01f)] private float flashOffTime = 0.08f;

    [Header("마네킹 판정")]
    [SerializeField] private string mannequinTag = "Mannequin";

    [Header("마네킹 쓰러짐")]
    [Tooltip("마네킹이 한 대 맞으면 쓰러짐 (끄면 데칼만 발광하고 계속 서 있음)")]
    [SerializeField] private bool mannequinFallsOnHit = true;
    [Tooltip("쓰러지는 데 걸리는 시간 (초)")]
    [SerializeField, Min(0.05f)] private float fallDuration = 0.35f;
    [Tooltip("쓰러진 뒤 사라지기까지 시간 (초). 0이면 사라지지 않고 누워 있음")]
    [SerializeField, Min(0f)] private float hideDelay = 1.5f;

    [Header("임시 HUD (로컬 플레이어만)")]
    [SerializeField] private bool showHud = true;

    public float Current { get; private set; }
    public float Max => maxHealth;
    public int PlayerId { get; private set; } = -1;
    public bool IsLocal { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsMannequin => CompareTag(mannequinTag);
    /// <summary>네트워크 플레이어인지 (체력을 호스트가 관리)</summary>
    public bool IsNetworked => net != null && net.IsSpawned;

    /// <summary>맞았을 때 (대상, 피해량, 공격자) - 마네킹 포함</summary>
    public event Action<Health, float, GameObject> OnDamaged;
    /// <summary>죽었을 때 (대상, 공격자) - 플레이어만</summary>
    public event Action<Health, GameObject> OnDied;
    /// <summary>내 컴퓨터에서 마네킹을 맞혔을 때 (마네킹, 공격자) - 다른 사람 화면에도 쓰러뜨리기용</summary>
    public static event Action<Health, GameObject> MannequinHitLocally;

    private DecalBlink decal;
    private PlayerNetworkCombat net;
    private Renderer[] renderers;
    private Collider[] colliders;
    private GUIStyle hudStyle;

    // ───────── 초기화 ─────────

    private void Awake()
    {
        Current = maxHealth;
        decal = GetComponentInChildren<DecalBlink>();
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        net = GetComponent<PlayerNetworkCombat>();
    }

    private void Start()
    {
        // 네트워크 플레이어는 PlayerNetworkCombat이 AssignNetworkPlayer()로 설정함
        if (!IsNetworked && CompareTag("Player") && ScoreManager.Instance != null)
        {
            PlayerId = ScoreManager.Instance.LocalPlayerId;
            IsLocal = true;
        }

        if (RoundManager.Instance != null)
            RoundManager.Instance.OnRoundStart += HandleRoundStart;
    }

    private void OnDestroy()
    {
        if (RoundManager.Instance != null)
            RoundManager.Instance.OnRoundStart -= HandleRoundStart;
    }

    /// <summary>디버그 봇: 마네킹 프리팹을 "진짜 플레이어"로 등록</summary>
    public void SetupAsBot(string botName)
    {
        tag = "Untagged";   // 마네킹 아님
        var sm = ScoreManager.Instance;
        if (sm == null) return;

        PlayerId = sm.Register(botName);
        if (RoundManager.Instance != null) RoundManager.Instance.RegisterParticipant(PlayerId);
    }

    // ───────── 피해 ─────────

    /// <summary>피해 받기. attacker = 공격한 오브젝트 (킬 기록용, 없으면 null)</summary>
    public void TakeDamage(float amount, GameObject attacker)
    {
        if (IsDead || amount <= 0f) return;

        // 마네킹: 체력 없음, 데칼 발광 + 한 대에 쓰러짐
        if (IsMannequin)
        {
            HitMannequin(amount, attacker);
            MannequinHitLocally?.Invoke(this, attacker);   // 멀티: 다른 사람 화면에도 알림
            return;
        }

        // 네트워크 플레이어: 호스트에 보내고 끝 (체력 변화는 호스트에서 돌아옴)
        if (IsNetworked)
        {
            net.RequestDamage(amount, attacker);
            return;
        }

        // 플레이어: 라운드 진행 중에만 피해
        var rm = RoundManager.Instance;
        if (rm != null && rm.CurrentState != RoundManager.State.Playing) return;

        Current = Mathf.Max(0f, Current - amount);
        if (flashPlayerDecalOnHit) FlashDecal();
        OnDamaged?.Invoke(this, amount, attacker);

        if (Current <= 0f) Die(attacker);
    }

    private void Die(GameObject attacker)
    {
        IsDead = true;

        int killerId = -1;
        if (attacker != null)
        {
            var killerHealth = attacker.GetComponentInParent<Health>();
            if (killerHealth != null) killerId = killerHealth.PlayerId;
        }

        if (PlayerId >= 0)
        {
            if (RoundManager.Instance != null) RoundManager.Instance.ReportKill(killerId, PlayerId);
            else if (ScoreManager.Instance != null) ScoreManager.Instance.AddKill(killerId, PlayerId);
        }

        OnDied?.Invoke(this, attacker);

        // 로컬 플레이어는 몸을 남겨둠 (RoundManager가 조작 정지), 봇은 숨김
        if (!IsLocal) SetBodyActive(false);
    }

    /// <summary>다른 플레이어가 이 마네킹을 맞혔다는 알림 (PlayerNetworkCombat이 호출). 다시 알리지 않음</summary>
    public void RemoteMannequinHit(GameObject attacker)
    {
        if (IsDead || !IsMannequin) return;
        HitMannequin(0f, attacker);
    }

    private void HitMannequin(float amount, GameObject attacker)
    {
        if (flashMannequinDecalOnHit) FlashDecal();
        OnDamaged?.Invoke(this, amount, attacker);

        if (mannequinFallsOnHit)
        {
            IsDead = true;   // 중복 피격 방지
            StartCoroutine(MannequinFall(attacker));
        }
    }

    /// <summary>체력 회복 + 몸 다시 표시 (라운드 시작 시)</summary>
    public void Revive()
    {
        IsDead = false;
        Current = maxHealth;
        SetBodyActive(true);
    }

    private void HandleRoundStart(int round)
    {
        // 네트워크 플레이어는 호스트가 체력을 되돌리면 NetRevive로 부활
        if (PlayerId >= 0 && !IsNetworked) Revive();
    }

    // ───────── 네트워크 (PlayerNetworkCombat이 호출) ─────────

    /// <summary>점수판 id / 내 플레이어 여부 설정</summary>
    public void AssignNetworkPlayer(int id, bool isLocal)
    {
        PlayerId = id;
        IsLocal = isLocal;
    }

    /// <summary>호스트가 정한 체력 반영. 줄었으면 피격 연출 (공격자는 모름)</summary>
    public void SetNetworkHealth(float value, bool notify)
    {
        float old = Current;
        Current = Mathf.Clamp(value, 0f, maxHealth);
        if (notify && Current < old)
        {
            if (flashPlayerDecalOnHit) FlashDecal();
            OnDamaged?.Invoke(this, old - Current, null);
        }
    }

    /// <summary>사망 처리 (킬 기록은 PlayerNetworkCombat이 따로 보고)</summary>
    public void NetDie(GameObject attacker)
    {
        if (IsDead) return;
        IsDead = true;
        Current = 0f;
        OnDied?.Invoke(this, attacker);

        // 내 몸은 남겨둠 (RoundManager가 조작 정지), 다른 플레이어는 숨김
        if (!IsLocal) SetBodyActive(false);
    }

    /// <summary>부활 (호스트가 체력을 되돌렸을 때)</summary>
    public void NetRevive() => Revive();

    // ───────── 연출 ─────────

    /// <summary>마네킹: 공격자 반대 방향으로 뒤로 넘어진 뒤 사라짐 (다음 라운드에 스포너가 새로 배치)</summary>
    private IEnumerator MannequinFall(GameObject attacker)
    {
        // 바로 충돌 끄기 → 시체에 총알/트랩이 걸리지 않음
        foreach (var c in colliders) if (c != null) c.enabled = false;

        // 애니메이션 정지 (쓰러진 자세 그대로 유지)
        var anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.enabled = false;

        // 넘어질 방향: 공격자 → 마네킹 방향 (없으면 마네킹 뒤쪽)
        Vector3 away = -transform.forward;
        if (attacker != null)
        {
            away = transform.position - attacker.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        }
        away.Normalize();
        Vector3 axis = Vector3.Cross(Vector3.up, away);   // 이 축으로 90도 돌면 away 쪽으로 넘어짐

        Quaternion start = transform.rotation;
        Quaternion end = Quaternion.AngleAxis(90f, axis) * start;

        float t = 0f;
        while (t < fallDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fallDuration);
            k = k * k;   // 처음엔 천천히, 점점 빠르게
            transform.rotation = Quaternion.Slerp(start, end, k);
            yield return null;
        }
        transform.rotation = end;

        if (hideDelay > 0f)
        {
            yield return new WaitForSeconds(hideDelay);
            SetBodyActive(false);
        }
    }

    private void FlashDecal()
    {
        if (decal != null) decal.ForceFlash(flashCount, flashOnTime, flashOffTime);
    }

    private void SetBodyActive(bool on)
    {
        foreach (var r in renderers) if (r != null) r.enabled = on;
        foreach (var c in colliders) if (c != null) c.enabled = on;
        if (decal != null) decal.enabled = on;
    }

    // ───────── 임시 HUD ─────────

    private void OnGUI()
    {
        if (!showHud || !IsLocal) return;

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
        }
        hudStyle.normal.textColor = Current > maxHealth * 0.3f ? new Color(0.85f, 0.9f, 1f) : new Color(1f, 0.4f, 0.4f);
        GUI.Label(new Rect(24, Screen.height - 60, 300, 30), $"HP {Mathf.CeilToInt(Current)}", hudStyle);
    }
}