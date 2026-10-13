using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 액티브 소나 스킬. 키를 누르면 주변으로 소나 펄스를 한 번 퍼뜨리고 쿨타임 동안 재사용 불가.
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
    [SerializeField, Min(0f)] private float cooldown = 8f;
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
    public float CooldownRemaining => Mathf.Max(0f, readyTime - Time.time);
    public bool IsReady => CooldownRemaining <= 0f;

    private float readyTime;
    private GUIStyle hudStyle;

    private void Awake()
    {
        // 마네킹(같은 프리팹 Variant)에서는 꺼짐
        if (!CompareTag(localPlayerTag)) enabled = false;
    }

    private void Update()
    {
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

        SoundPulseManager.Emit(transform.position + offset, radius, gameObject, expandTime, holdTime, fadeTime);
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
        if (IsReady)
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