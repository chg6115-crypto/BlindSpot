using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;

[Overlay(typeof(SceneView), "PrimTools", true)]
public class PrimToolbarOverlay : ToolbarOverlay
{
    public PrimToolbarOverlay() : base(
        "PrimTools/CreateGrounded",
        "PrimTools/CreateSphere",
        "PrimTools/CreateCapsule",
        "PrimTools/CreateCylinder",
        "PrimTools/CreatePlane",
        "PrimTools/CreateEmpty",
        "PrimTools/ResetTransform",      // 해당 기능 스크립트 구현 필요
        "PrimTools/CreateDirectional",
        "PrimTools/CreatePoint",
        "PrimTools/CreateSpot",
        "PrimTools/CreateArea"
    )
    {
    }
}
