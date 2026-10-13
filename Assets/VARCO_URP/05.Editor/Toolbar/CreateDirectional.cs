using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.Rendering;

[EditorToolbarElement("PrimTools/CreateDirectional")]
public class CreateDirectionalButton : EditorToolbarButton
{
    public CreateDirectionalButton()
    {
        icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VARCO_URP/05.Editor/Icons/dir.png");
        tooltip = "Create Directional Light";

        clicked += () => CreateDirectionalLight("Directional Light");
    }

    private void CreateDirectionalLight(string name)
    {
        GameObject lightGameObject = new GameObject(name);
        Light lightComp = lightGameObject.AddComponent<Light>();
        lightComp.type = LightType.Directional;

        // 기본 설정: URP용 Mixed Light + Soft Shadow
        lightComp.intensity = 1f;

        // Filter and Temperature 모드 설정
        lightComp.useColorTemperature = true;       // Emission Light Appearance: Filter and Temperature
        lightComp.colorTemperature = 6500f;         // 중립 색온도
        lightComp.color = Color.white;              // 필터 기준 색상

        lightComp.shadows = LightShadows.Soft;
        lightComp.shadowBias = 0.05f;
        lightComp.shadowNormalBias = 0.4f;
        lightComp.renderMode = LightRenderMode.Auto;

        // URP용 Mixed Lighting 모드
        lightComp.lightmapBakeType = LightmapBakeType.Mixed;

        // 위치 및 회전 고정
        lightGameObject.transform.position = new Vector3(0f, 3f, 0f);
        lightGameObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        Undo.RegisterCreatedObjectUndo(lightGameObject, "Create Directional Light");
        Selection.activeGameObject = lightGameObject;
    }
}
