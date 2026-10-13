using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreateArea")]
public class CreateAreaButton : EditorToolbarButton
{
    public CreateAreaButton()
    {
        icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VARCO_URP/05.Editor/Icons/area.png");
        tooltip = "Create Area Light (Baked Only)";

        clicked += () => CreateSimpleAreaLight("Area Light");
    }

    private void CreateSimpleAreaLight(string name)
    {
        GameObject lightGO = new GameObject(name);
        Light light = lightGO.AddComponent<Light>();

        // Area Light 설정 (URP)
        light.type = LightType.Rectangle;
        light.lightmapBakeType = LightmapBakeType.Baked; // URP는 실시간 Area Light 미지원

        // 위치 및 회전 고정
        lightGO.transform.position = new Vector3(0f, 2f, 0f);
        lightGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // 기본 설정
        light.color = Color.white;
        light.intensity = 2f;
        light.range = 5f;
        light.areaSize = new Vector2(1f, 1f);

        // Filter and Temperature 모드 적용
        light.useColorTemperature = true;
        light.colorTemperature = 6500f;

        Undo.RegisterCreatedObjectUndo(lightGO, "Create Area Light");
        Selection.activeGameObject = lightGO;

        Debug.Log("<color=yellow>Area Light created at (0,2,0), rotated (90,0,0).</color> Note: Only works with Baked GI and Lightmap Static objects.");
    }
}
