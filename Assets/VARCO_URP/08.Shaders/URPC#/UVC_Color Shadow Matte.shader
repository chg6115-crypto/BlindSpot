/*
 * Unity 6.4 / Universal Render Pipeline 17.4
 * URP conversion of "HDRP/Custom Unlit Color Shadow Matte".
 *
 * The material is an unlit white studio/skydome receiver by default. Its base
 * opacity is controlled by _alpha, while realtime light shadows and (optionally)
 * URP SSAO add opacity. Set Alpha to 0 for a transparent shadow-catcher.
 * The object does not cast shadows because it intentionally has no ShadowCaster pass.
 */

Shader "UVC Shader/UVC_Color Shadow Matte"
{
    Properties
    {
        _alpha("Alpha", Range(0, 1)) = 1
        _alphaClip("Alpha Clip Threshold", Range(0, 1)) = 0.5
        [HDR] _baseColor("Base Color", Color) = (1, 1, 1, 1)
        [HDR] _surfaceColor("Studio / Skydome Color", Color) = (1, 1, 1, 1)
        _shadowTint("Shadow Tint", Color) = (0, 0, 0, 0.42)
        _aoRadius("AO Radius", Range(0, 0.89)) = 0.75
        [Toggle] _aoRadiusDebug("AO Radius Debug", Float) = 0
        [Toggle(_USE_SSAO)] _UseSSAO("Use URP SSAO", Float) = 1
        [Toggle(_ADDITIONAL_LIGHT_SHADOWS_MATTE)] _AdditionalLightShadows("Additional Light Shadows", Float) = 1

        // Off is intentional: a skydome must be visible from its inside.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull (Off for Skydome)", Float) = 0
        [Toggle] _ZWrite("Z Write", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Z Test", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShadowMatteForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma shader_feature_local_fragment _USE_SSAO
            #pragma shader_feature_local_fragment _ADDITIONAL_LIGHT_SHADOWS_MATTE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _alpha;
                half _alphaClip;
                half4 _baseColor;
                half4 _surfaceColor;
                half4 _shadowTint;
                float _aoRadius;
                half _aoRadiusDebug;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS    : TEXCOORD1;
                half fogFactor    : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half LuminanceForMatte(half3 color)
            {
                return dot(color, half3(0.2126h, 0.7152h, 0.0722h));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));

                // A colored/intensity-scaled light only contributes a matte shadow
                // where that light actually reaches this receiver.
                half lightWeight = saturate(LuminanceForMatte(mainLight.color) * mainLight.distanceAttenuation);
                half shadowAmount = saturate((1.0h - mainLight.shadowAttenuation) * lightWeight);

                #if defined(_ADDITIONAL_LIGHTS) && defined(_ADDITIONAL_LIGHT_SHADOWS_MATTE)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                        half additionalWeight = saturate(LuminanceForMatte(additionalLight.color) * additionalLight.distanceAttenuation);
                        half additionalShadow = saturate((1.0h - additionalLight.shadowAttenuation) * additionalWeight);
                        shadowAmount = max(shadowAmount, additionalShadow);
                    LIGHT_LOOP_END
                #endif

                // Preserve the HDRP shader's world-radius SSAO fade convention.
                half aoRadiusIntensity = 1.0h;
                float radiusDenominator = max(0.001, 0.9 - _aoRadius);
                aoRadiusIntensity = 1.0h - saturate((length(input.positionWS / 16.0) - _aoRadius) / radiusDenominator);

                half aoAmount = 0.0h;
                #if defined(_USE_SSAO) && defined(_SCREEN_SPACE_OCCLUSION)
                    float2 normalizedScreenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(normalizedScreenUV);
                    aoAmount = saturate(1.0h - ao.indirectAmbientOcclusion) * aoRadiusIntensity;
                #endif

                half shadowAlpha = shadowAmount * _shadowTint.a;
                half localAlpha = saturate(max(shadowAlpha, aoAmount) + _alpha);

                half3 baseColor = _baseColor.rgb * _surfaceColor.rgb;
                half3 shadowColor = _shadowTint.rgb * baseColor;
                half3 matteColor = lerp(baseColor, shadowColor, shadowAmount * _shadowTint.a);
                // AO only darkens the otherwise unlit white studio surface.
                matteColor *= (1.0h - aoAmount * 0.65h);

                if (_aoRadiusDebug > 0.5h)
                    matteColor = lerp(baseColor, half3(0, 0, 1), 1.0h - aoRadiusIntensity);

                // As in the source shader, _alphaClip selects the color-composition
                // path; it does not discard the transparent receiver.
                if (_alpha < _alphaClip)
                    matteColor = lerp(shadowColor, matteColor, _alpha);

                matteColor = MixFog(matteColor, input.fogFactor);
                return half4(matteColor, localAlpha);
            }
            ENDHLSL
        }

        // Depth pass is used only when the material's Z Write property is enabled.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite [_ZWrite]
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 DepthFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack Off
}
