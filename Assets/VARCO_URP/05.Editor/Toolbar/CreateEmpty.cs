using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/CreateEmpty")]
public class CreateEmptyButton : EditorToolbarButton
{
    public CreateEmptyButton()
    {
        icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VARCO_URP/05.Editor/Icons/empty.png");
        tooltip = "Create Empty GameObject";

        clicked += () =>
        {
            string baseName = "EmptyObject";
            string finalName = baseName;
            int index = 1;

            while (GameObject.Find(finalName) != null)
                finalName = $"{baseName}_{index++}";

            GameObject empty = new GameObject(finalName);

            // SceneView 카메라 앞쪽에 기본 위치 지정
            Vector3 spawnPosition = Vector3.zero;
            Quaternion spawnRotation = Quaternion.identity;
            if (SceneView.lastActiveSceneView != null)
            {
                var cam = SceneView.lastActiveSceneView.camera.transform;
                spawnPosition = cam.position + cam.forward * 5f;
                spawnRotation = Quaternion.LookRotation(-cam.forward);
            }

            // 선택된 오브젝트가 있다면 그 하위에 생성
            if (Selection.activeTransform != null)
            {
                empty.transform.SetParent(Selection.activeTransform);
                empty.transform.localPosition = Vector3.zero;
                empty.transform.localRotation = Quaternion.identity;
                empty.transform.localScale = Vector3.one;
            }
            else
            {
                empty.transform.position = spawnPosition;
                empty.transform.rotation = spawnRotation;
            }

            // 태그 및 레이어 초기화
            empty.tag = "Untagged";
            empty.layer = LayerMask.NameToLayer("Default");

            // Undo 및 선택
            Undo.RegisterCreatedObjectUndo(empty, "Create Empty GameObject");
            Selection.activeGameObject = empty;
        };
    }
}
