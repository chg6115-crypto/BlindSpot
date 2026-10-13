// BlindSpot - 소리 펄스 외곽선 (Full Screen Pass Renderer Feature 용)
// SonarDepthFeature가 그린 "캐릭터 없는" 깊이/노멀로 외곽선을 찾는다 → 캐릭터 자리에도 뒤 배경 선이 이어짐.
// 소리 범위(SoundPulseManager) 안에서만 표시. 바닥(위를 향한 면)은 월드 격자로 표시.
// 깊이 외곽선은 "꺾임(2차 차이)"으로 판정 → 비스듬히 본 평평한 벽이 통째로 빛나지 않음.
Shader "BlindSpot/SonarOutline"
{
    Properties
    {
        [Header(Color)]
        _LineColor ("Line Color", Color) = (0.75, 0.88, 1, 1)
        _Intensity ("Overall Intensity", Range(0, 2)) = 0.5

        [Header(Edge Detection)]
        _Thickness ("Line Thickness (px)", Range(0.5, 3)) = 1
        _DepthSensitivity ("Depth Sensitivity", Range(1, 100)) = 25
        _DepthThreshold ("Depth Threshold", Range(0, 1)) = 0.5
        _NormalSensitivity ("Normal Sensitivity", Range(0.1, 5)) = 1
        _NormalThreshold ("Normal Threshold (노멀맵 잡음 제거)", Range(0, 1.5)) = 0.8

        [Header(Floor Grid)]
        [Toggle] _FloorGrid ("Floor Grid (바닥은 격자로)", Float) = 1
        _FloorNormalY ("Floor Detect (위를 향한 정도)", Range(0.5, 1)) = 0.85
        _GridSize ("Grid Size (m)", Range(0.25, 10)) = 2
        _GridWidth ("Grid Line Width (px)", Range(0.5, 4)) = 1.2
        _GridIntensity ("Grid Intensity", Range(0, 1)) = 0.5

        [Header(Pulse)]
        _EdgeSoftness ("Pulse Edge Softness (m)", Range(0.1, 5)) = 1.5
        _InnerFade ("Fade Toward Edge", Range(0, 1)) = 0.6
        _RingWidth ("Leading Ring Width (m)", Range(0.05, 3)) = 0.6
        _RingBoost ("Leading Ring Boost", Range(0, 3)) = 0.6

        [Header(Debug)]
        [IntRange] _DebugView ("Debug View (0 끔, 1 외곽선 전체, 2 소리 범위, 3 소나 깊이)", Range(0, 3)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "SonarOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define MAX_PULSES 16

            // SonarDepthFeature 결과 (캐릭터 제외)
            TEXTURE2D_X_FLOAT(_SonarDepthTexture);
            TEXTURE2D_X(_SonarNormalsTexture);

            // SoundPulseManager 전역 값
            float4 _SonarPulses[MAX_PULSES];     // xyz 위치, w 현재 반경
            float4 _SonarPulseData[MAX_PULSES];  // x 밝기, y 링 강조
            int    _SonarPulseCount;

            // 머티리얼 값
            half4 _LineColor;
            float _Intensity;
            float _Thickness, _DepthSensitivity, _DepthThreshold, _NormalSensitivity, _NormalThreshold;
            float _FloorGrid, _FloorNormalY, _GridSize, _GridWidth, _GridIntensity;
            float _EdgeSoftness, _InnerFade, _RingWidth, _RingBoost;
            float _DebugView;

            float RawDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_SonarDepthTexture, sampler_PointClamp, uv, 0).r;
            }

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(RawDepth(uv), _ZBufferParams);
            }

            float3 Normal(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_SonarNormalsTexture, sampler_PointClamp, uv, 0).xyz;
            }

            // floorMask: 1이면 바닥 → 노멀 외곽선 무시,
            // 깊이 외곽선도 "주변에 바닥이 아닌 면(벽/물체)이 있을 때만" 사용 → 바닥 요철 점 제거
            float EdgeAt(float2 uv, float floorMask)
            {
                float2 o = _ScreenSize.zw * _Thickness;
                float2 uvA = uv + o * float2(-1, -1);
                float2 uvB = uv + o * float2( 1,  1);
                float2 uvC = uv + o * float2( 1, -1);
                float2 uvD = uv + o * float2(-1,  1);

                float3 nA = Normal(uvA), nB = Normal(uvB), nC = Normal(uvC), nD = Normal(uvD);

                // 깊이: "꺾임(2차 차이)"으로 판정 → 비스듬히 본 평평한 벽은 0, 실제 모서리/경계만 큼
                // (1/깊이는 평면 위에서 화면 방향으로 일정하게 변하므로 평면에서는 2차 차이가 0)
                float d0 = EyeDepth(uv);
                float dA = EyeDepth(uvA), dB = EyeDepth(uvB), dC = EyeDepth(uvC), dD = EyeDepth(uvD);
                float i0 = 1.0 / max(d0, 0.001);
                float iA = 1.0 / max(dA, 0.001), iB = 1.0 / max(dB, 0.001);
                float iC = 1.0 / max(dC, 0.001), iD = 1.0 / max(dD, 0.001);
                float depthDiff = (abs(iA + iB - 2.0 * i0) + abs(iC + iD - 2.0 * i0)) / i0;
                float depthEdge = smoothstep(_DepthThreshold, _DepthThreshold + 0.5, depthDiff * _DepthSensitivity);

                // 주변 4점이 모두 바닥이면 → 바닥끼리의 요철이므로 깊이 선 무시
                float minNY = min(min(nA.y, nB.y), min(nC.y, nD.y));
                float allFloor = smoothstep(_FloorNormalY - 0.05, _FloorNormalY, minNY);
                depthEdge *= 1 - floorMask * allFloor;

                // 노멀 (면이 꺾이는 곳). 바닥에서는 무시
                float normalDiff = sqrt(dot(nA - nB, nA - nB) + dot(nC - nD, nC - nD));
                float normalEdge = smoothstep(_NormalThreshold, _NormalThreshold + 0.3, normalDiff * _NormalSensitivity);
                normalEdge *= 1 - floorMask;

                return max(depthEdge, normalEdge);
            }

            // 바닥 격자: 월드 XZ 기준, 화면 픽셀 굵기를 거리와 무관하게 일정하게
            float GridAt(float3 wp, float eyeDepth)
            {
                float pixelWorld = eyeDepth * 2.0 / (_ScreenParams.y * unity_CameraProjection._m11);  // 이 거리에서 1px의 월드 크기
                float halfW = pixelWorld * _GridWidth * 0.5;

                float2 dist = abs(frac(wp.xz / _GridSize + 0.5) - 0.5) * _GridSize;   // 가장 가까운 격자선까지 거리 (m)
                float d = min(dist.x, dist.y);
                return 1 - smoothstep(halfW, halfW + pixelWorld, d);
            }

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                int dbg = (int)round(_DebugView);

                float raw = RawDepth(uv);

                // [디버그 3] 소나용 깊이 → 캐릭터가 없는 회색 화면이면 정상
                if (dbg == 3) return half4(Linear01Depth(raw, _ZBufferParams).xxx * 4, 1);

                // 바닥 판정 (위를 향한 면)
                float3 n0 = Normal(uv);
                float floorMask = _FloorGrid * smoothstep(_FloorNormalY - 0.05, _FloorNormalY, n0.y);

                // [디버그 1] 소리 범위와 상관없이 외곽선 전체 표시
                if (dbg == 1) return half4(scene.rgb + EdgeAt(uv, floorMask).xxx, 1);

                // [디버그 2] 소리 신호를 아예 못 받으면 화면 전체가 어두운 빨강
                if (_SonarPulseCount <= 0) return dbg == 2 ? half4(0.25, 0, 0, 1) : scene;

                // 깊이 → 월드 좌표
                #if UNITY_REVERSED_Z
                    if (raw <= 0.000001) return scene;          // 아무것도 없음
                #else
                    if (raw >= 0.999999) return scene;
                    raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 wp = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);

                // 소리 범위 마스크 (여러 개 중 가장 밝은 값)
                float mask = 0;
                [loop]
                for (int i = 0; i < MAX_PULSES; i++)
                {
                    if (i >= _SonarPulseCount) break;
                    float r = _SonarPulses[i].w;
                    if (r <= 0.001) continue;

                    float d = distance(wp, _SonarPulses[i].xyz);
                    float inside = 1 - smoothstep(r - _EdgeSoftness, r, d);
                    inside *= lerp(1, 1 - _InnerFade, saturate(d / r));        // 바깥쪽일수록 흐리게

                    float ring = exp(-abs(d - r) / _RingWidth) * _RingBoost * _SonarPulseData[i].y;

                    mask = max(mask, saturate(inside + ring) * _SonarPulseData[i].x);
                }

                // [디버그 2] 소리 범위를 초록색으로 표시
                if (dbg == 2) return half4(scene.rgb + half3(0, mask * 0.5, 0), 1);

                if (mask <= 0.001) return scene;

                float edge = EdgeAt(uv, floorMask);
                float grid = GridAt(wp, EyeDepth(uv)) * floorMask * _GridIntensity;
                float lines = max(edge, grid);

                scene.rgb += _LineColor.rgb * (_Intensity * lines * mask);
                return scene;
            }
            ENDHLSL
        }
    }
}