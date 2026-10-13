using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreateGrounded")]
public class CreateGroundedButton : EditorToolbarButton
{
    public CreateGroundedButton()
    {
        icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VARCO_URP/05.Editor/Icons/create.png");
        tooltip = "Create Grounded Cube";

        clicked += () =>
        {
            GameObject root = new GameObject("Cube_Root");
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform);

            // 바닥에 정렬
            float yOffset = cube.GetComponent<Renderer>().bounds.extents.y;
            cube.transform.localPosition = new Vector3(0, yOffset, 0);

            // 선택된 오브젝트 하위 또는 월드 기준 위치 설정
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

            // 머티리얼 적용
            Renderer renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                string matPath = "Assets/Materials/Cube.Mat"; // ✅ 짧은 이름 사용
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                // 머티리얼이 없으면 생성
                if (mat == null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                        AssetDatabase.CreateFolder("Assets", "Materials");

                    mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    mat.color = Color.white;
                    mat.SetFloat("_Metallic", 0f);
                    mat.SetFloat("_Smoothness", 0.5f);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    mat.EnableKeyword("_EMISSION");

                    AssetDatabase.CreateAsset(mat, matPath);
                    AssetDatabase.SaveAssets();
                }
                else
                {
                    // 기존 머티리얼 강제 초기화
                    mat.color = Color.white;
                    mat.SetFloat("_Metallic", 0f);
                    mat.SetFloat("_Smoothness", 0.5f);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    mat.EnableKeyword("_EMISSION");

                    EditorUtility.SetDirty(mat);
                    AssetDatabase.SaveAssets();
                }

                renderer.sharedMaterial = mat;
            }

            Undo.RegisterCreatedObjectUndo(root, "Create Grounded Cube");
            Selection.activeGameObject = root;
        };
    }
}
