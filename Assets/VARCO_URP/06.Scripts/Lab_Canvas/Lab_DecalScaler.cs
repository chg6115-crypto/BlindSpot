using UnityEngine;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(DecalProjector))]
public sealed class DecalScaler : MonoBehaviour
{
    private const float MinimumSize = 0.0001f;

    [SerializeField, HideInInspector]
    private Vector3 baseProjectorSize = Vector3.one;

    [SerializeField, HideInInspector]
    private bool baseSizeInitialized;

    private DecalProjector projector;
    private Vector3 lastLocalScale;
    private Vector3 lastProjectorSize;
    private bool cacheInitialized;

    private void OnEnable()
    {
        Initialize();
        SynchronizeNow(false);
    }

    private void Reset()
    {
        projector = GetComponent<DecalProjector>();
        baseSizeInitialized = false;
        cacheInitialized = false;
        Initialize();
        SynchronizeNow(false);
    }

    private void OnValidate()
    {
        Initialize();
        SynchronizeNow(false);
    }

    private void Update()
    {
        if (!Initialize())
            return;

        Vector3 safeScale = GetSafeAbsoluteScale(transform.localScale);

        if (!cacheInitialized)
        {
            lastLocalScale = safeScale;
            lastProjectorSize = projector.size;
            cacheInitialized = true;
            return;
        }

        if (!Approximately(safeScale, lastLocalScale))
        {
            ApplyProjectorSize(Vector3.Scale(baseProjectorSize, safeScale), true);
            lastLocalScale = safeScale;
            lastProjectorSize = projector.size;
            return;
        }

        // Projector Size를 Inspector에서 직접 수정한 경우 새 기준 크기로 받아들인다.
        if (!Approximately(projector.size, lastProjectorSize))
        {
            baseProjectorSize = Divide(projector.size, safeScale);
            lastProjectorSize = projector.size;
        }
    }

    private bool Initialize()
    {
        if (projector == null)
            projector = GetComponent<DecalProjector>();

        if (projector == null)
            return false;

        // Unity 6.6의 계층 스케일 적용과 이 스크립트의 동기화가 중복되지 않게 한다.
        if (projector.scaleMode != DecalScaleMode.ScaleInvariant)
            projector.scaleMode = DecalScaleMode.ScaleInvariant;

        Vector3 safeScale = GetSafeAbsoluteScale(transform.localScale);
        if (!baseSizeInitialized)
        {
            baseProjectorSize = Divide(projector.size, safeScale);
            baseSizeInitialized = true;
        }

        return true;
    }

    private void SynchronizeNow(bool recordUndo)
    {
        if (!Initialize())
            return;

        Vector3 safeScale = GetSafeAbsoluteScale(transform.localScale);
        ApplyProjectorSize(Vector3.Scale(baseProjectorSize, safeScale), recordUndo);
        lastLocalScale = safeScale;
        lastProjectorSize = projector.size;
        cacheInitialized = true;
    }

    private void ApplyProjectorSize(Vector3 requestedSize, bool recordUndo)
    {
        Vector3 safeSize = GetSafeAbsoluteScale(requestedSize);
        if (Approximately(projector.size, safeSize))
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying && recordUndo)
        {
            Undo.RecordObject(projector, "Scale Decal Projector");
            projector.size = safeSize;
            EditorUtility.SetDirty(projector);
            PrefabUtility.RecordPrefabInstancePropertyModifications(projector);
            SceneView.RepaintAll();
            return;
        }
#endif

        projector.size = safeSize;
    }

    private static Vector3 GetSafeAbsoluteScale(Vector3 value)
    {
        return new Vector3(
            Mathf.Max(Mathf.Abs(value.x), MinimumSize),
            Mathf.Max(Mathf.Abs(value.y), MinimumSize),
            Mathf.Max(Mathf.Abs(value.z), MinimumSize));
    }

    private static Vector3 Divide(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            value.x / divisor.x,
            value.y / divisor.y,
            value.z / divisor.z);
    }

    private static bool Approximately(Vector3 a, Vector3 b)
    {
        return Mathf.Approximately(a.x, b.x) &&
               Mathf.Approximately(a.y, b.y) &&
               Mathf.Approximately(a.z, b.z);
    }
}
