using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreatePlane")]
public class CreatePlaneButton : EditorToolbarButton
{
    public CreatePlaneButton()
    {
        icon = EditorGUIUtility.IconContent("d_Prefab Icon").image as Texture2D;
        tooltip = "Create Plane at Ground";

        clicked += () => CreatePrimitiveAtGround(PrimitiveType.Plane);
    }

    private void CreatePrimitiveAtGround(PrimitiveType type)
    {
        GameObject root = new GameObject($"{type}_Root");
        GameObject mesh = GameObject.CreatePrimitive(type);
        mesh.transform.SetParent(root.transform);

        // 바닥 정렬
        float yOffset = mesh.GetComponent<Renderer>().bounds.extents.y;
        mesh.transform.localPosition = new Vector3(0, yOffset, 0);

        // 부모 Transform 초기화
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
            string matPath = "Assets/Materials/Plane.Mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat == null)
            {
                Debug.Log($"Material not found at: {matPath}, creating URP Lit material.");

                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");

                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.85f, 0.85f, 0.85f); // 연한 회색
                mat.SetFloat("_Smoothness", 0.4f);
                mat.SetFloat("_Metallic", 0.0f);

                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                // 기존 것도 재설정 보장
                mat.color = new Color(0.85f, 0.85f, 0.85f);
                mat.SetFloat("_Smoothness", 0.4f);
                mat.SetFloat("_Metallic", 0.0f);

                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            renderer.sharedMaterial = mat;
        }

        Undo.RegisterCreatedObjectUndo(root, $"Create Grounded {type}");
        Selection.activeGameObject = root;
    }
}
