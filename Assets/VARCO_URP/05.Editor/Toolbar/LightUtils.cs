using UnityEditor;
using UnityEngine;

public static class LightUtils
{
    public static void CreateLight(LightType type, string name)
    {
        if (type == LightType.Rectangle)
        {
            CreateEmissiveAreaLight(name);
            return;
        }

        GameObject lightGO = new GameObject(name);
        Light light = lightGO.AddComponent<Light>();
        light.type = type;
        light.lightmapBakeType = LightmapBakeType.Mixed;

        switch (type)
        {
            case LightType.Directional:
                light.color = Color.white;
                light.intensity = 1.0f;
                light.shadows = LightShadows.Soft;
                break;
            case LightType.Point:
                light.color = Color.white;
                light.intensity = 1.0f;
                light.range = 10f;
                light.shadows = LightShadows.Soft;
                break;
            case LightType.Spot:
                light.color = Color.white;
                light.intensity = 1.0f;
                light.range = 15f;
                light.spotAngle = 30f;
                light.shadows = LightShadows.Soft;
                break;
            default:
                Debug.LogWarning($"Light type '{type}' may not be supported in Built-in RP.");
                break;
        }

        PlaceObjectInScene(lightGO, $"Create {type} Light");
    }

    private static void CreateEmissiveAreaLight(string name)
    {
        GameObject root = new GameObject(name + "_EmissiveArea");

        // Emissive Plane
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "EmissivePlane";
        plane.transform.SetParent(root.transform);
        plane.transform.localScale = new Vector3(0.2f, 1f, 0.2f); // Smaller than default plane
        plane.transform.localRotation = Quaternion.Euler(90, 0, 0); // Face downward

        // Emissive Material
        var matPath = "Assets/Materials/EmissiveWhite.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");

            mat = new Material(Shader.Find("Standard"));
            mat.color = Color.white;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.white * 2f);

            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
        }

        plane.GetComponent<Renderer>().sharedMaterial = mat;

        // Spot Light to simulate Area Light
        GameObject lightGO = new GameObject("SpotLight");
        lightGO.transform.SetParent(root.transform);
        lightGO.transform.localPosition = new Vector3(0, 0.5f, 0);
        lightGO.transform.localRotation = Quaternion.Euler(90, 0, 0);

        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Spot;
        light.spotAngle = 90f;
        light.range = 5f;
        light.intensity = 2f;
        light.color = Color.white;
        light.shadows = LightShadows.Soft;
        light.lightmapBakeType = LightmapBakeType.Mixed;

        PlaceObjectInScene(root, "Create Emissive Area Light");
    }

    private static void PlaceObjectInScene(GameObject go, string undoLabel)
    {
        if (Selection.activeTransform != null)
        {
            go.transform.SetParent(Selection.activeTransform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }
        else if (SceneView.lastActiveSceneView != null)
        {
            var cam = SceneView.lastActiveSceneView.camera.transform;
            go.transform.position = cam.position + cam.forward * 5f;
            go.transform.rotation = Quaternion.LookRotation(-cam.forward);
        }
        else
        {
            go.transform.position = Vector3.zero;
        }

        Undo.RegisterCreatedObjectUndo(go, undoLabel);
        Selection.activeGameObject = go;
    }
}
