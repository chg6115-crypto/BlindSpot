using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 소나 외곽선 전용 깊이/노멀 텍스처를 그리는 Renderer Feature.
///  - Layer Mask에서 Character를 빼면 캐릭터가 없는 것처럼 배경만 그려짐
///  - 결과: _SonarDepthTexture, _SonarNormalsTexture (SonarOutline 셰이더가 사용)
///  - URP 기본 깊이/노멀 텍스처는 건드리지 않음 → 플레이어 데칼, SSAO 영향 없음
/// PC_Renderer → Add Renderer Feature → Sonar Depth Feature
/// </summary>
public class SonarDepthFeature : ScriptableRendererFeature
{
    [Tooltip("소나 외곽선 계산에 포함할 레이어. Character를 빼야 캐릭터를 무시하고 그 뒤 배경 선이 그려짐")]
    public LayerMask layerMask = ~0;

    private SonarDepthPass pass;

    public override void Create()
    {
        pass = new SonarDepthPass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var type = renderingData.cameraData.cameraType;
        if (type == CameraType.Preview || type == CameraType.Reflection) return;

        pass.layerMask = layerMask;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Release();
    }

    private class SonarDepthPass : ScriptableRenderPass
    {
        public LayerMask layerMask;

        private RTHandle depthHandle;
        private RTHandle normalsHandle;

        private static readonly List<ShaderTagId> Tags = new List<ShaderTagId>
        {
            new ShaderTagId("DepthNormals"),
            new ShaderTagId("DepthNormalsOnly"),
        };

        private static readonly int DepthId = Shader.PropertyToID("_SonarDepthTexture");
        private static readonly int NormalsId = Shader.PropertyToID("_SonarNormalsTexture");
        private static readonly GlobalKeyword OctNormalsKeyword = GlobalKeyword.Create("_GBUFFER_NORMALS_OCT");
        private static readonly GlobalKeyword RenderingLayersKeyword = GlobalKeyword.Create("_WRITE_RENDERING_LAYERS");

        private class PassData
        {
            public RendererListHandle list;
        }

        public void Release()
        {
            depthHandle?.Release();
            normalsHandle?.Release();
            depthHandle = null;
            normalsHandle = null;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();

            var desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.bindMS = false;

            // 노멀: 월드 노멀을 그대로 저장 (-1~1)
            var nDesc = desc;
            nDesc.depthBufferBits = 0;
            nDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateHandleIfNeeded(ref normalsHandle, nDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SonarNormalsTexture");

            // 깊이
            var dDesc = desc;
            dDesc.graphicsFormat = GraphicsFormat.None;
            dDesc.depthStencilFormat = GraphicsFormat.D32_SFloat;
            RenderingUtils.ReAllocateHandleIfNeeded(ref depthHandle, dDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SonarDepthTexture");

            TextureHandle normals = renderGraph.ImportTexture(normalsHandle);
            TextureHandle depth = renderGraph.ImportTexture(depthHandle);

            var drawSettings = RenderingUtils.CreateDrawingSettings(Tags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            var filter = new FilteringSettings(RenderQueueRange.opaque, layerMask);
            var listParams = new RendererListParams(renderingData.cullResults, drawSettings, filter);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sonar DepthNormals", out var data))
            {
                data.list = renderGraph.CreateRendererList(listParams);
                builder.UseRendererList(data.list);
                builder.SetRenderAttachment(normals, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetGlobalTextureAfterPass(normals, NormalsId);
                builder.SetGlobalTextureAfterPass(depth, DepthId);

                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.ClearRenderTarget(true, true, Color.clear);
                    ctx.cmd.SetKeyword(OctNormalsKeyword, false);       // 노멀을 압축하지 않고 그대로 출력
                    ctx.cmd.SetKeyword(RenderingLayersKeyword, false);  // 두 번째 출력(렌더링 레이어) 끔
                    ctx.cmd.DrawRendererList(d.list);
                });
            }
        }
    }
}