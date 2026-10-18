using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreatePrimitiveDropdown")]
public class CreatePrimitiveDropdown : EditorToolbarDropdown
{
    private static PrimitiveType[] types = {
        PrimitiveType.Cube,
        PrimitiveType.Sphere,
        PrimitiveType.Capsule,
        PrimitiveType.Cylinder,
        PrimitiveType.Plane
    };

    private static string[] typeNames = { "Cube", "Sphere", "Capsule", "Cylinder", "Plane" };

    private PrimitiveType selectedType = PrimitiveType.Cube;

    public CreatePrimitiveDropdown()
    {
        icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VARCO_URP/05.Editor/Icons/create.png");
        tooltip = "Create Grounded Primitive";

        clicked += ShowDropdownMenu;
    }

    private void ShowDropdownMenu()
    {
        GenericMenu menu = new GenericMenu();
        for (int i = 0; i < types.Length; i++)
        {
            int index = i;
            menu.AddItem(new GUIContent(typeNames[i]), selectedType == types[i], () =>
            {
                selectedType = types[index];
                CreateGroundedPrimitive(selectedType);
            });
        }
        menu.ShowAsContext();
    }

    private void CreateGroundedPrimitive(PrimitiveType type)
    {
        GameObject root = new GameObject($"{type}_Root");
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.transform.SetParent(root.transform);

        float yOffset = obj.GetComponent<Renderer>().bounds.extents.y;
        obj.transform.localPosition = new Vector3(0, yOffset, 0);

        if (Selection.activeTransform != null)
        {
            root.transform.SetParent(Selection.activeTransform);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
        }
        else
        {
            root.transform.position = Vector3.zero;
        }

        // Built-in RP용 머티리얼 자동 생성 및 적용
        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            string matPath = $"Assets/Materials/{type}Standard.mat";
            Material preset = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (preset == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");

                preset = new Material(Shader.Find("Standard"));
                preset.color = new Color(0.8f, 0.9f, 1.0f); // 연한 파랑
                preset.SetFloat("_Glossiness", 0.5f);
                preset.SetFloat("_Metallic", 0.1f);

                AssetDatabase.CreateAsset(preset, matPath);
                AssetDatabase.SaveAssets();
            }

            renderer.sharedMaterial = preset;
        }

        Undo.RegisterCreatedObjectUndo(root, $"Create Grounded {type}");
        Selection.activeGameObject = root;
    }
}
