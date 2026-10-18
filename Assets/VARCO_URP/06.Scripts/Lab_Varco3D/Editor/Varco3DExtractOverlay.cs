using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

[Overlay(typeof(SceneView), "VE Extract Tool")]
public class Varco3DExtractOverlay : Overlay
{
    private Label statusLabel;

    public override VisualElement CreatePanelContent()
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;
        root.style.paddingTop = 6;
        root.style.paddingBottom = 6;
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;
        root.style.minWidth = 170;

        statusLabel = new Label(GetSelectionLabel());
        statusLabel.style.marginBottom = 6;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        root.Add(statusLabel);

        var extractButton = new Button(RunExtract)
        {
            text = "Run Extract"
        };
        extractButton.style.height = 28;
        root.Add(extractButton);

        Selection.selectionChanged -= RefreshSelectionLabel;
        Selection.selectionChanged += RefreshSelectionLabel;

        return root;
    }

    private void RunExtract()
    {
        Debug.Log("[Varco3D] Overlay Run Extract pressed.");
        if (statusLabel != null)
            statusLabel.text = "Running...";

        Varco3DExtractTool.RunFromCurrentSelection();
        RefreshSelectionLabel();
    }

    private void RefreshSelectionLabel()
    {
        if (statusLabel != null)
            statusLabel.text = GetSelectionLabel();
    }

    private static string GetSelectionLabel()
    {
        Object active = Selection.activeObject;
        return active == null ? "Selection: None" : $"Selection: {active.name}";
    }
}
