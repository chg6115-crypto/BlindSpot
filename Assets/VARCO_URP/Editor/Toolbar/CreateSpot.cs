using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreateSpot")]
public class CreateSpotButton : EditorToolbarButton
{
    public CreateSpotButton()
    {
        icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VARCO_URP/05.Editor/Icons/spot.png");
        tooltip = "Create Spot Light";

        clicked += () => CreateSpotLight("Spot Light");
    }

    private void CreateSpotLight(string name)
    {
        GameObject lightGameObject = new GameObject(name);
        Light lightComp = lightGameObject.AddComponent<Light>();
        lightComp.type = LightType.Spot;

        // 위치: (0, 2, 0), 회전: (90, 0, 0)
        lightGameObject.transform.position = new Vector3(0f, 2f, 0f);
        lightGameObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // 기본 광원 설정 (URP 기준)
        lightComp.intensity = 1f;
        lightComp.range = 15f;
        lightComp.spotAngle = 30f;
        lightComp.shadows = LightShadows.Soft;

        // Filter and Temperature 모드 적용
        lightComp.useColorTemperature = true;
        lightComp.colorTemperature = 6500f; // 중립 색온도
        lightComp.color = Color.white;      // 필터 기준 색상

        Undo.RegisterCreatedObjectUndo(lightGameObject, "Create Spot Light");
        Selection.activeGameObject = lightGameObject;
    }
}
