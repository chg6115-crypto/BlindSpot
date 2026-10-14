using System;
using UnityEngine;

/// <summary>
/// 체력. Player 프리팹 루트에 붙임 (마네킹 Variant도 자동으로 같이 가짐).
///  - 마네킹(Mannequin 태그): 체력 없음. 맞으면 데칼만 발광 (쓰러지지 않음)
///  - 플레이어: 맞으면 체력 감소 (데칼 발광은 기본 꺼짐), 0이 되면 사망 → RoundManager에 킬 보고
///  - 로컬 플레이어(Player 태그)는 ScoreManager의 LocalPlayerId를 사용
///  - 디버그 봇은 SetupAsBot()으로 플레이어 등록
/// 공격하는 쪽은 TakeDamage(피해량, 공격자 GameObject)만 호출하면 됨 (근접, 총 공통).
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

    [Header("임시 HUD (로컬 플레이어만)")]
    [SerializeField] private bool showHud = true;

    public float Current { get; private set; }
    public float Max => maxHealth;
    public int PlayerId { get; private set; } = -1;
    public bool IsLocal { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsMannequin => CompareTag(mannequinTag);

    /// <summary>맞았을 때 (대상, 피해량, 공격자) - 마네킹 포함</summary>
    public event Action<Health, float, GameObject> OnDamaged;
    /// <summary>죽었을 때 (대상, 공격자) - 플레이어만</summary>
    public event Action<Health, GameObject> OnDied;

    private DecalBlink decal;
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
    }

    private void Start()
    {
        if (CompareTag("Player") && ScoreManager.Instance != null)
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

        // 마네킹: 체력 없음, 데칼만 발광
        if (IsMannequin)
        {
            if (flashMannequinDecalOnHit) FlashDecal();
            OnDamaged?.Invoke(this, amount, attacker);
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

    /// <summary>체력 회복 + 몸 다시 표시 (라운드 시작 시)</summary>
    public void Revive()
    {
        IsDead = false;
        Current = maxHealth;
        SetBodyActive(true);
    }

    private void HandleRoundStart(int round)
    {
        if (PlayerId >= 0) Revive();
    }

    // ───────── 연출 ─────────

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