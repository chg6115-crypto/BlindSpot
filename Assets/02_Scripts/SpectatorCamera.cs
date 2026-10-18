using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 관전 카메라. PlayerNetworkSetup이 내 플레이어에만 자동으로 붙인다 (프리팹에 직접 넣지 않아도 됨).
///  - 내가 죽으면 살아 있는 다른 플레이어의 머리(PlayerCameraRoot)로 카메라 전환 → 그 사람 시점
///  - 좌클릭 / 스페이스: 다음 플레이어
///  - 보고 있던 사람이 죽으면 자동으로 다음 사람
///  - 부활(다음 라운드)하면 내 시점으로 복귀
/// </summary>
[DisallowMultipleComponent]
public class SpectatorCamera : MonoBehaviour
{
    [SerializeField] private int fontSize = 22;

    private PlayerNetworkSetup self;
    private Health health;
    private CinemachineCamera vcam;
    private PlayerNetworkSetup target;
    private GUIStyle style;
    private readonly List<PlayerNetworkSetup> candidates = new List<PlayerNetworkSetup>();

    public bool IsSpectating => target != null;

    private void Awake()
    {
        self = GetComponent<PlayerNetworkSetup>();
        health = GetComponent<Health>();
    }

    private void OnDisable()
    {
        SwitchTo(null);
    }

    private void Update()
    {
        bool dead = health != null && health.IsDead;
        if (!dead)
        {
            if (target != null) SwitchTo(null);   // 부활 → 내 시점
            return;
        }

        // 보고 있는 사람이 없거나 죽었으면 다음 사람
        if (target == null || !IsAlive(target))
            SwitchTo(FindNext(target));

        var mouse = Mouse.current;
        var kb = Keyboard.current;
        bool next = (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
                    (kb != null && kb.spaceKey.wasPressedThisFrame);
        if (next && !PauseMenuUI.IsOpen) SwitchTo(FindNext(target));   // ESC 메뉴 클릭은 무시
    }

    private static bool IsAlive(PlayerNetworkSetup p)
    {
        if (p == null || !p.isActiveAndEnabled) return false;
        var h = p.GetComponent<Health>();
        return h != null && !h.IsDead;
    }

    private PlayerNetworkSetup FindNext(PlayerNetworkSetup current)
    {
        candidates.Clear();
        foreach (var p in PlayerNetworkSetup.All)
            if (p != null && p != self && !p.IsMine && IsAlive(p)) candidates.Add(p);
        if (candidates.Count == 0) return null;

        candidates.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        int i = current != null ? candidates.IndexOf(current) : -1;
        return candidates[(i + 1) % candidates.Count];
    }

    private void SwitchTo(PlayerNetworkSetup next)
    {
        if (next == target) return;

        if (target != null) target.SetSpectated(false);
        target = next;

        if (vcam == null) vcam = FindAnyObjectByType<CinemachineCamera>();
        if (vcam != null)
        {
            Transform follow = target != null ? target.CameraRoot : (self != null ? self.CameraRoot : null);
            if (follow != null) vcam.Follow = follow;
        }

        if (target != null) target.SetSpectated(true);
    }

    // ───────── HUD ─────────

    private void OnGUI()
    {
        if (health == null || !health.IsDead) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 1f, 0.9f) }
            };
        }
        style.fontSize = fontSize;

        string text;
        if (target != null)
        {
            var combat = target.GetComponent<PlayerNetworkCombat>();
            string name = combat != null ? combat.DisplayName : target.name;
            text = $"SPECTATING  {name.ToUpperInvariant()}\n<size={Mathf.RoundToInt(fontSize * 0.7f)}>CLICK / SPACE : NEXT PLAYER</size>";
        }
        else
        {
            text = "NO PLAYERS TO SPECTATE";
        }

        style.richText = true;
        GUI.Label(new Rect(0, Screen.height - 140, Screen.width, 80), text, style);
    }
}
