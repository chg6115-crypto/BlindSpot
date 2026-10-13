using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreatePoint")]
public class CreatePointButton : EditorToolbarButton
{
    public CreatePointButton()
    {
        icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VARCO_URP/05.Editor/Icons/point.png");
        tooltip = "Create Point Light";

        clicked += () => CreatePointLight("Point Light");
    }

    private void CreatePointLight(string name)
    {
        GameObject lightGameObject = new GameObject(name);
        Light lightComp = lightGameObject.AddComponent<Light>();
        lightComp.type = LightType.Point;

        // 위치를 (0, 0, 0)으로 고정
        lightGameObject.transform.position = Vector3.zero;

        // 기본 광원 설정 (URP 기준)
        lightComp.intensity = 1f;
        lightComp.range = 10f;
        lightComp.shadows = LightShadows.Soft;

        // Filter and Temperature 모드 적용
        lightComp.useColorTemperature = true;
        lightComp.colorTemperature = 6500f; // 중립 색온도
        lightComp.color = Color.white;      // 필터 기준 색상

        Undo.RegisterCreatedObjectUndo(lightGameObject, "Create Point Light");
        Selection.activeGameObject = lightGameObject;
    }
}
