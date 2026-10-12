// BlindSpot - 캐릭터 영역에 스텐실 표시만 남기는 셰이더 (화면에는 아무것도 안 그림)
// Render Objects 기능의 Override Material로 사용. SonarOutline이 이 영역을 제외한다.
Shader "BlindSpot/CharacterStencilMask"
{
    Properties
    {
        _Inflate ("Inflate (m) - 실루엣 경계까지 덮기", Range(0, 0.1)) = 0.03
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "StencilMask"

            ColorMask 0      // 색은 안 씀
            ZWrite Off
            ZTest LEqual     // 벽 뒤에 가려진 부분은 표시 안 함
            Cull Back

            Stencil
            {
                Ref 1
                WriteMask 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Inflate;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(ws + nws * _Inflate);
                return o;
            }

            half4 frag (Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}