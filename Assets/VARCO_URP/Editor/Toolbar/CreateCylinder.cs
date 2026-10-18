using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreateCylinder")]
public class CreateCylinderButton : EditorToolbarButton
{
    public CreateCylinderButton()
    {
        icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VARCO_URP/05.Editor/Icons/Cylinder.png");
        tooltip = "Create Cylinder at Ground";

        clicked += () => CreatePrimitiveAtGround(PrimitiveType.Cylinder);
    }

    private void CreatePrimitiveAtGround(PrimitiveType type)
    {
        GameObject root = new GameObject($"{type}_Root");
        GameObject mesh = GameObject.CreatePrimitive(type);
        mesh.transform.SetParent(root.transform);

        float yOffset = mesh.GetComponent<Renderer>().bounds.extents.y;
        mesh.transform.localPosition = new Vector3(0, yOffset, 0);

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

        // URP Lit 머티리얼 적용
        Renderer renderer = mesh.GetComponent<Renderer>();
        if (renderer != null)
        {
            string matPath = "Assets/Materials/Cylinder.Mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat == null)
            {
                Debug.Log($"Material not found at: {matPath}, creating URP Lit material.");

                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");

                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.7f, 0.6f, 1f); // 연한 보라색
                mat.SetFloat("_Smoothness", 0.6f);
                mat.SetFloat("_Metallic", 0.5f);

                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                mat.color = new Color(0.7f, 0.6f, 1f);
                mat.SetFloat("_Smoothness", 0.6f);
                mat.SetFloat("_Metallic", 0.5f);

                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            renderer.sharedMaterial = mat;
        }

        Undo.RegisterCreatedObjectUndo(root, $"Create Grounded {type}");
        Selection.activeGameObject = root;
    }
}
