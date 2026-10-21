using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

[ExecuteAlways]
public class UVC_Reflections : MonoBehaviour
{
    [System.Serializable]
    public enum ResolutionMulltiplier
    {
        Full,
        Half,
        Third,
        Quarter
    }

    [System.Serializable]
    public class PlanarReflectionSettings
    {
        public ResolutionMulltiplier m_ResolutionMultiplier = ResolutionMulltiplier.Third;
        public float m_ClipPlaneOffset = 0.07f;
        public LayerMask m_ReflectLayers = -1;
        public bool m_shadows;
    }

    [SerializeField]
    public PlanarReflectionSettings m_settings = new PlanarReflectionSettings();

    public GameObject target;

    [FormerlySerializedAs("camOffset")]
    public float m_planeOffset;

    private static Camera m_ReflectionCamera;

    private RenderTexture m_ReflectionTexture = null;

    private readonly int planarReflectionTextureID =
        Shader.PropertyToID("_PlanarReflectionTexture");

    private Vector2Int m_OldReflectionTextureSize =
        new Vector2Int(-1, -1);

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += ExecuteBeforeCameraRender;
    }

    private void OnDisable()
    {
        Cleanup();
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        RenderPipelineManager.beginCameraRendering -= ExecuteBeforeCameraRender;

        if (m_ReflectionCamera != null)
        {
            m_ReflectionCamera.targetTexture = null;
            SafeDestroy(m_ReflectionCamera.gameObject);
            m_ReflectionCamera = null;
        }

        if (m_ReflectionTexture != null)
        {
            RenderTexture.ReleaseTemporary(m_ReflectionTexture);
            m_ReflectionTexture = null;
            m_OldReflectionTextureSize = new Vector2Int(-1, -1);
        }
    }

    private void SafeDestroy(Object obj)
    {
        if (obj == null)
            return;

        if (UnityEngine.Application.isEditor)
        {
            DestroyImmediate(obj);
        }
        else
        {
            Destroy(obj);
        }
    }

    private void UpdateCamera(Camera src, Camera dest)
    {
        if (dest == null)
            return;

        dest.CopyFrom(src);
        dest.cameraType = CameraType.Game;
        dest.useOcclusionCulling = false;
    }

    private void UpdateReflectionCamera(Camera realCamera)
    {
        if (m_ReflectionCamera == null)
        {
            m_ReflectionCamera = CreateMirrorObjects(realCamera);
        }

        Vector3 pos = Vector3.zero;
        Vector3 normal = Vector3.up;

        if (target != null)
        {
            pos = target.transform.position + Vector3.up * m_planeOffset;
            normal = target.transform.up;
        }

        UpdateCamera(realCamera, m_ReflectionCamera);

        float d =
            -Vector3.Dot(normal, pos) -
            m_settings.m_ClipPlaneOffset;

        Vector4 reflectionPlane =
            new Vector4(
                normal.x,
                normal.y,
                normal.z,
                d
            );

        Matrix4x4 reflection = Matrix4x4.identity;

        reflection *=
            Matrix4x4.Scale(
                new Vector3(1, -1, 1)
            );

        CalculateReflectionMatrix(
            ref reflection,
            reflectionPlane
        );

        Vector3 oldpos =
            realCamera.transform.position -
            new Vector3(
                0,
                pos.y * 2,
                0
            );

        Vector3 newpos =
            ReflectPosition(oldpos);

        m_ReflectionCamera.transform.forward =
            Vector3.Scale(
                realCamera.transform.forward,
                new Vector3(1, -1, 1)
            );

        m_ReflectionCamera.worldToCameraMatrix =
            realCamera.worldToCameraMatrix *
            reflection;

        Vector4 clipPlane =
            CameraSpacePlane(
                m_ReflectionCamera,
                pos - Vector3.up * 0.1f,
                normal,
                1.0f
            );

        Matrix4x4 projection =
            realCamera.CalculateObliqueMatrix(
                clipPlane
            );

        m_ReflectionCamera.projectionMatrix =
            projection;

        m_ReflectionCamera.cullingMask =
            m_settings.m_ReflectLayers;

        m_ReflectionCamera.transform.position =
            newpos;
    }

    private static void CalculateReflectionMatrix(
        ref Matrix4x4 reflectionMat,
        Vector4 plane
    )
    {
        reflectionMat.m00 =
            1F - 2F * plane[0] * plane[0];

        reflectionMat.m01 =
            -2F * plane[0] * plane[1];

        reflectionMat.m02 =
            -2F * plane[0] * plane[2];

        reflectionMat.m03 =
            -2F * plane[3] * plane[0];

        reflectionMat.m10 =
            -2F * plane[1] * plane[0];

        reflectionMat.m11 =
            1F - 2F * plane[1] * plane[1];

        reflectionMat.m12 =
            -2F * plane[1] * plane[2];

        reflectionMat.m13 =
            -2F * plane[3] * plane[1];

        reflectionMat.m20 =
            -2F * plane[2] * plane[0];

        reflectionMat.m21 =
            -2F * plane[2] * plane[1];

        reflectionMat.m22 =
            1F - 2F * plane[2] * plane[2];

        reflectionMat.m23 =
            -2F * plane[3] * plane[2];

        reflectionMat.m30 = 0F;
        reflectionMat.m31 = 0F;
        reflectionMat.m32 = 0F;
        reflectionMat.m33 = 1F;
    }

    private static Vector3 ReflectPosition(Vector3 pos)
    {
        return new Vector3(
            pos.x,
            -pos.y,
            pos.z
        );
    }

    private float GetScaleValue()
    {
        switch (m_settings.m_ResolutionMultiplier)
        {
            case ResolutionMulltiplier.Full:
                return 1f;

            case ResolutionMulltiplier.Half:
                return 0.5f;

            case ResolutionMulltiplier.Third:
                return 0.33f;

            case ResolutionMulltiplier.Quarter:
                return 0.25f;
        }

        return 0.5f;
    }

    private static bool Int2Compare(
        Vector2Int a,
        Vector2Int b
    )
    {
        return
            a.x == b.x &&
            a.y == b.y;
    }

    private Vector4 CameraSpacePlane(
        Camera cam,
        Vector3 pos,
        Vector3 normal,
        float sideSign
    )
    {
        Vector3 offsetPos =
            pos +
            normal *
            m_settings.m_ClipPlaneOffset;

        Matrix4x4 m =
            cam.worldToCameraMatrix;

        Vector3 cpos =
            m.MultiplyPoint(offsetPos);

        Vector3 cnormal =
            m.MultiplyVector(normal).normalized *
            sideSign;

        return new Vector4(
            cnormal.x,
            cnormal.y,
            cnormal.z,
            -Vector3.Dot(cpos, cnormal)
        );
    }

    private Camera CreateMirrorObjects(
        Camera currentCamera
    )
    {
        GameObject go =
            new GameObject(
                $"Planar Refl Camera id{GetEntityId()} for {currentCamera.GetEntityId()}",
                typeof(Camera)
            );

        var additionalData =
            go.AddComponent<UniversalAdditionalCameraData>();

        additionalData.renderShadows =
            m_settings.m_shadows;

        additionalData.requiresColorOption =
            CameraOverrideOption.Off;

        additionalData.requiresDepthOption =
            CameraOverrideOption.Off;

        var reflectionCamera =
            go.GetComponent<Camera>();

        reflectionCamera.transform.SetPositionAndRotation(
            transform.position,
            transform.rotation
        );

        reflectionCamera.allowMSAA =
            currentCamera.allowMSAA;

        reflectionCamera.depth =
            -10;

        reflectionCamera.enabled =
            false;

        reflectionCamera.allowHDR =
            currentCamera.allowHDR;

        go.hideFlags =
            HideFlags.HideAndDontSave;

        return reflectionCamera;
    }

    private Vector2Int ReflectionResolution(
        Camera cam,
        float scale
    )
    {
        var x =
            (int)(
                cam.pixelWidth *
                scale *
                GetScaleValue()
            );

        var y =
            (int)(
                cam.pixelHeight *
                scale *
                GetScaleValue()
            );

        x = Mathf.Max(1, x);
        y = Mathf.Max(1, y);

        return new Vector2Int(x, y);
    }

    public void ExecuteBeforeCameraRender(
        ScriptableRenderContext context,
        Camera camera
    )
    {
        if (!enabled)
            return;

        if (
            m_ReflectionCamera != null &&
            camera == m_ReflectionCamera
        )
        {
            return;
        }

        bool prevInvertCulling =
            GL.invertCulling;

        bool prevFog =
            RenderSettings.fog;

        int prevMaxLod =
            QualitySettings.maximumLODLevel;

        float prevLodBias =
            QualitySettings.lodBias;

        try
        {
            GL.invertCulling =
                true;

            RenderSettings.fog =
                false;

            QualitySettings.maximumLODLevel =
                1;

            QualitySettings.lodBias =
                prevLodBias * 0.5f;

            UpdateReflectionCamera(camera);

            m_ReflectionCamera.cameraType =
                camera.cameraType;

            float renderScale =
                UniversalRenderPipeline.asset != null
                    ? UniversalRenderPipeline.asset.renderScale
                    : 1f;

            var res =
                ReflectionResolution(
                    camera,
                    renderScale
                );

            if (
                m_ReflectionTexture == null ||
                !Int2Compare(
                    res,
                    m_OldReflectionTextureSize
                )
            )
            {
                if (m_ReflectionTexture != null)
                {
                    RenderTexture.ReleaseTemporary(
                        m_ReflectionTexture
                    );
                }

                var format =
                    SystemInfo.SupportsRenderTextureFormat(
                        RenderTextureFormat.RGB111110Float
                    )
                        ? RenderTextureFormat.RGB111110Float
                        : RenderTextureFormat.DefaultHDR;

                m_ReflectionTexture =
                    RenderTexture.GetTemporary(
                        res.x,
                        res.y,
                        16,
                        GraphicsFormatUtility.GetGraphicsFormat(
                            format,
                            true
                        )
                    );

                m_ReflectionTexture.useMipMap =
                    true;

                m_ReflectionTexture.autoGenerateMips =
                    true;

                m_OldReflectionTextureSize =
                    res;
            }

            m_ReflectionCamera.targetTexture =
                m_ReflectionTexture;

            var request =
                new UniversalRenderPipeline.SingleCameraRequest
                {
                    destination =
                        m_ReflectionTexture
                };

            if (
                RenderPipeline.SupportsRenderRequest(
                    m_ReflectionCamera,
                    request
                )
            )
            {
                RenderPipeline.SubmitRenderRequest(
                    m_ReflectionCamera,
                    request
                );
            }

            Shader.SetGlobalTexture(
                planarReflectionTextureID,
                m_ReflectionTexture
            );
        }
        finally
        {
            GL.invertCulling =
                prevInvertCulling;

            RenderSettings.fog =
                prevFog;

            QualitySettings.maximumLODLevel =
                prevMaxLod;

            QualitySettings.lodBias =
                prevLodBias;
        }
    }
}