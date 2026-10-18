using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 액티브 소나 스킬. 키를 누르면 주변으로 소나 펄스를 한 번 퍼뜨리고 쿨타임 동안 재사용 불가.
///  - 라운드 GO 후 Start Cooldown(5초) 동안 사용 불가, 사용 후 Cooldown(10초)
///  - RoundManager가 있으면 플레이 중(Playing)에만 사용 가능 (카운트다운 중 사용 불가)
///  - 펄스 모양(퍼짐/유지/사라짐 시간)은 이 Inspector에서 조정
///  - 로컬 플레이어(Player 태그)에서만 동작 → 같은 프리팹을 쓰는 마네킹은 자동으로 꺼짐
/// Player 루트에 붙임.
/// </summary>
public class SonarSkill : MonoBehaviour
{
    [Header("입력")]
    [SerializeField] private Key key = Key.Q;
    [Tooltip("게임패드 Y(북쪽) 버튼으로도 사용")]
    [SerializeField] private bool useGamepad = true;

    [Header("소나")]
    [SerializeField, Min(1f)] private float radius = 15f;
    [Tooltip("사용 후 쿨타임 (초)")]
    [SerializeField, Min(0f)] private float cooldown = 10f;
    [Tooltip("라운드 시작(GO) 후 첫 사용까지 쿨타임 (초)")]
    [SerializeField, Min(0f)] private float startCooldown = 5f;
    [Tooltip("펄스 중심 = 발 위치 + 이 값")]
    [SerializeField] private Vector3 offset = Vector3.zero;

    [Header("펄스 모양 (초)")]
    [Tooltip("반경이 0 → 최대까지 퍼지는 시간")]
    [SerializeField, Min(0.01f)] private float expandTime = 1f;
    [Tooltip("다 퍼진 뒤 최대 밝기로 유지되는 시간")]
    [SerializeField, Min(0f)] private float holdTime = 1f;
    [Tooltip("서서히 사라지는 시간")]
    [SerializeField, Min(0.01f)] private float fadeTime = 2f;

    [Header("동작 대상")]
    [Tooltip("이 태그일 때만 동작 (마네킹 제외용)")]
    [SerializeField] private string localPlayerTag = "Player";

    [Header("임시 HUD")]
    [SerializeField] private bool showHud = true;
    [SerializeField] private int hudFontSize = 18;

    /// <summary>스킬을 사용했을 때 (UI, 사운드 연결용)</summary>
    public event Action OnUsed;

    public float Cooldown => cooldown;
    public float Radius => radius;
    public float ExpandTime => expandTime;
    /// <summary>마지막으로 사용한 펄스의 중심 (SonarReveal이 사용)</summary>
    public Vector3 LastOrigin { get; private set; }
    public float CooldownRemaining => Mathf.Max(0f, readyTime - Time.time);
    /// <summary>라운드 진행 중이 아니라서(카운트다운·라운드 종료 등) 막혀 있는지</summary>
    public bool IsLockedByRound => round != null && round.CurrentState != RoundManager.State.Playing;
    public bool IsReady => !IsLockedByRound && CooldownRemaining <= 0f;

    private float readyTime;
    private RoundManager round;
    private GUIStyle hudStyle;

    private void Awake()
    {
        // 마네킹(같은 프리팹 Variant)에서는 꺼짐
        if (!CompareTag(localPlayerTag)) enabled = false;
    }

    private void Start()
    {
        round = RoundManager.Instance;
        if (round != null)
        {
            round.OnPlayStart += HandlePlayStart;
            // 이미 플레이 중에 생성됐으면 지금부터 시작 쿨타임
            if (round.CurrentState == RoundManager.State.Playing) ApplyStartCooldown();
        }
        else
        {
            // 라운드 없는 테스트 씬: 시작하자마자 시작 쿨타임
            ApplyStartCooldown();
        }
    }

    private void OnDestroy()
    {
        if (round != null) round.OnPlayStart -= HandlePlayStart;
    }

    private void HandlePlayStart(int roundNumber) => ApplyStartCooldown();

    private void ApplyStartCooldown() => readyTime = Time.time + startCooldown;

    private void Update()
    {
        if (PauseMenuUI.IsOpen) return;   // ESC 메뉴 중
        bool pressed = false;

        var kb = Keyboard.current;
        if (kb != null && kb[key].wasPressedThisFrame) pressed = true;

        if (useGamepad)
        {
            var gp = Gamepad.current;
            if (gp != null && gp.buttonNorth.wasPressedThisFrame) pressed = true;
        }

        if (pressed) TryUse();
    }

    /// <summary>스킬 사용 시도. 쿨타임 중이면 false</summary>
    public bool TryUse()
    {
        if (!IsReady) return false;

        LastOrigin = transform.position + offset;
        SoundPulseManager.Emit(LastOrigin, radius, gameObject, expandTime, holdTime, fadeTime);
        readyTime = Time.time + cooldown;

        OnUsed?.Invoke();
        return true;
    }

    // ───────── 임시 HUD (나중에 UI로 교체) ─────────

    private void OnGUI()
    {
        if (!showHud) return;

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hudFontSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        string text;
        if (IsLockedByRound)
        {
            hudStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            text = round.CurrentState == RoundManager.State.Intro ? $"SONAR  {startCooldown:0.0}s" : "SONAR  --";
        }
        else if (IsReady)
        {
            hudStyle.normal.textColor = new Color(0.75f, 0.88f, 1f);
            text = $"SONAR [{key}]  READY";
        }
        else
        {
            hudStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            text = $"SONAR  {CooldownRemaining:0.0}s";
        }

        GUI.Label(new Rect(0, Screen.height - 60, Screen.width, 30), text, hudStyle);
    }
}