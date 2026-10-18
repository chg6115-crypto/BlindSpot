using System.Reflection;
using UnityEngine;

/// <summary>
/// 설정의 마우스 감도를 1인칭 컨트롤러(StarterAssets FirstPersonController)의 RotationSpeed 에 적용.
/// Player_FPS 프리팹에 붙인다 (컨트롤러와 같은 오브젝트 또는 부모).
/// 처음 RotationSpeed 값을 기준(감도 50)으로 보고, 감도 값에 비례해 곱한다.
/// </summary>
public class MouseSensitivityApplier : MonoBehaviour
{
    [Tooltip("비우면 자신/자식에서 FirstPersonController 자동 검색")]
    [SerializeField] private MonoBehaviour controller;
    [SerializeField] private string fieldName = "RotationSpeed";

    private FieldInfo field;
    private float baseValue;
    private bool ready;

    private void Awake()
    {
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

    private void Apply()
    {
        if (!ready) return;
        field.SetValue(controller, baseValue * GameSettings.SensitivityMultiplier);
    }
}
