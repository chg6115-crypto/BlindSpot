using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/ResetTransform")]
public class ResetTransformButton : EditorToolbarButton
{
    public ResetTransformButton()
    {
        icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VARCO_URP/05.Editor/Icons/reset.png");
        tooltip = "Reset Transform of Selected Object(s)";

        clicked += () =>
        {
            if (Selection.transforms == null || Selection.transforms.Length == 0)
            {
                Debug.LogWarning("No object selected.");
                return;
            }

            foreach (Transform t in Selection.transforms)
            {
                Undo.RecordObject(t, "Reset Transform");
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
            }

            Debug.Log($"Reset transform for {Selection.transforms.Length} object(s).");
        };
    }
}

