using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 설정의 마우스 감도를 1인칭 컨트롤러(StarterAssets FirstPersonController)의 RotationSpeed 에 적용.
/// Player_FPS 프리팹에 붙인다 (컨트롤러와 같은 오브젝트 또는 부모).
/// 처음 RotationSpeed 값을 기준(감도 50)으로 보고, 감도 값에 비례해 곱한다.
/// 게임 중 [ / ] 키로 감도를 0.1배씩 내리고 올림 (내 플레이어만, ESC 메뉴·사망·결과창 중에는 무시)
/// </summary>
public class MouseSensitivityApplier : MonoBehaviour
{
    [Tooltip("비우면 자신/자식에서 FirstPersonController 자동 검색")]
    [SerializeField] private MonoBehaviour controller;
    [SerializeField] private string fieldName = "RotationSpeed";
    [Tooltip("[ ] 키로 감도 조절")]
    [SerializeField] private bool hotkeys = true;

    private FieldInfo field;
    private float baseValue;
    private bool ready;
    private Health health;

    private void Awake()
    {
        health = GetComponentInParent<Health>();
        if (controller == null)
        {
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && mb.GetType().Name == "FirstPersonController") { controller = mb; break; }
            }
        }
        if (controller == null)
        {
            Debug.LogWarning("[MouseSensitivityApplier] FirstPersonController 를 찾지 못했습니다.", this);
            return;
        }

        field = controller.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(float))
        {
            Debug.LogWarning($"[MouseSensitivityApplier] {fieldName} 필드를 찾지 못했습니다.", this);
            return;
        }

        baseValue = (float)field.GetValue(controller);
        ready = true;
    }

    private void OnEnable()
    {
        GameSettings.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        GameSettings.Changed -= Apply;
    }

    private void Update()
    {
        if (!hotkeys) return;
        if (health == null || !health.IsLocal || health.IsDead) return;
        if (PauseMenuUI.IsOpen || MatchResultUI.IsShowing) return;

        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.rightBracketKey.wasPressedThisFrame) GameSettings.StepSensitivity(+1);
        else if (kb.leftBracketKey.wasPressedThisFrame) GameSettings.StepSensitivity(-1);
    }

    private void Apply()
    {
        if (!ready) return;
        field.SetValue(controller, baseValue * GameSettings.SensitivityMultiplier);
    }
}
