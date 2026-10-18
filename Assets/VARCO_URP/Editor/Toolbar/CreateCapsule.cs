using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreateCapsule")]
public class CreateCapsuleButton : EditorToolbarButton
{
    public CreateCapsuleButton()
    {
        icon = EditorGUIUtility.IconContent("d_CapsuleCollider Icon").image as Texture2D;
        tooltip = "Create Capsule at Ground";

        clicked += () => CreatePrimitiveAtGround(PrimitiveType.Capsule);
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
            string matPath = "Assets/Materials/Capsule.Mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat == null)
            {
                Debug.Log($"Material not found at: {matPath}, creating URP Lit material.");

                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");

                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.5f, 0.9f, 0.6f); // 연한 연두색
                mat.SetFloat("_Smoothness", 0.7f);
                mat.SetFloat("_Metallic", 0.3f);

                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                mat.color = new Color(0.5f, 0.9f, 0.6f);
                mat.SetFloat("_Smoothness", 0.7f);
                mat.SetFloat("_Metallic", 0.3f);

                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            renderer.sharedMaterial = mat;
        }

        Undo.RegisterCreatedObjectUndo(root, $"Create Grounded {type}");
        Selection.activeGameObject = root;
    }
}
