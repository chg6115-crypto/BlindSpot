using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreateSphere")]
public class CreateSphereButton : EditorToolbarButton
{
    public CreateSphereButton()
    {
        icon = EditorGUIUtility.IconContent("d_SphereCollider Icon").image as Texture2D;
        tooltip = "Create Sphere at Ground";

        clicked += () => CreatePrimitiveAtGround(PrimitiveType.Sphere);
    }

    private void CreatePrimitiveAtGround(PrimitiveType type)
    {
        GameObject root = new GameObject($"{type}_Root");
        GameObject mesh = GameObject.CreatePrimitive(type);
        mesh.transform.SetParent(root.transform);

        // 지면 정렬
        float yOffset = mesh.GetComponent<Renderer>().bounds.extents.y;
        mesh.transform.localPosition = new Vector3(0, yOffset, 0);

        // 선택된 트랜스폼 하위에 배치
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

        // URP용 Lit 머티리얼 적용
        Renderer renderer = mesh.GetComponent<Renderer>();
        if (renderer != null)
        {
            string matPath = "Assets/Materials/Sphere.Mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat == null)
            {
                Debug.Log($"Material not found at: {matPath}, creating URP Lit material.");

                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");

                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.9f, 0.75f, 0.2f); // 황금빛
                mat.SetFloat("_Smoothness", 0.85f);
                mat.SetFloat("_Metallic", 0.4f);

                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                mat.color = new Color(0.9f, 0.75f, 0.2f);
                mat.SetFloat("_Smoothness", 0.85f);
                mat.SetFloat("_Metallic", 0.4f);

                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            renderer.sharedMaterial = mat;
        }

        Undo.RegisterCreatedObjectUndo(root, $"Create Grounded {type}");
        Selection.activeGameObject = root;
    }
}
