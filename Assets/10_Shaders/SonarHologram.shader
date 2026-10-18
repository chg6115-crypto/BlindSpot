// BlindSpot - 소나 실루엣 홀로그램 (SonarReveal이 Graphics.RenderMesh로 그림)
// 마젠타 자체 발광 + 가로 스캔라인 + 가장자리 발광. 더하기 블렌딩이라 어둠 속에서도 보임.
// ZTest LEqual → 벽이나 물체 뒤에 있는 부분은 가려짐.
// _Alpha는 스크립트가 머티리얼 프로퍼티 블록으로 넘김 (유지 → 사라짐).
Shader "BlindSpot/SonarHologram"
{
    Properties
    {
        [Header(Color)]
        _Color ("Color", Color) = (1, 0.05, 0.85, 1)
        _Intensity ("Intensity (밝기)", Range(0, 10)) = 2.5
        _FillAlpha ("Fill (몸 전체 기본 밝기)", Range(0, 1)) = 0.2

        [Header(Rim)]
        _RimPower ("Rim Power (가장자리 날카로움)", Range(0.5, 8)) = 2.5
        _RimStrength ("Rim Strength (가장자리 밝기)", Range(0, 3)) = 1.2

        [Header(Scanlines)]
        _LineDensity ("Lines per Meter (1m당 줄 수)", Range(1, 100)) = 28
        _LineWidth ("Line Width (0~1)", Range(0.02, 0.9)) = 0.35
        _LineStrength ("Line Strength", Range(0, 2)) = 0.7
        _ScrollSpeed ("Scroll Speed (줄 흐르는 속도)", Range(-5, 5)) = 1.2
        _Flicker ("Flicker (깜빡임)", Range(0, 1)) = 0.15

        [Header(Shape)]
        _Inflate ("Inflate (m) - 몸보다 살짝 크게", Range(0, 0.05)) = 0.012

        _Alpha ("Alpha (스크립트가 제어)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SonarHologram"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One      // 더하기 (발광)
            ZWrite Off
            ZTest LEqual       // 벽 뒤는 안 보임
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity, _FillAlpha;
                float _RimPower, _RimStrength;
                float _LineDensity, _LineWidth, _LineStrength, _ScrollSpeed, _Flicker;
                float _Inflate;
                float _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz) + nws * _Inflate;
                o.positionWS = ws;
                o.normalWS = nws;
                o.positionCS = TransformWorldToHClip(ws);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1.0 - saturate(abs(dot(n, v))), _RimPower);

                // 가로 스캔라인 (월드 높이 기준, 위로 흐름)
                float y = i.positionWS.y * _LineDensity - _Time.y * _ScrollSpeed;
                float d = abs(frac(y) - 0.5) * 2.0;                 // 0 = 줄 가운데, 1 = 줄 사이
                float aa = max(fwidth(y) * 2.0, 0.001);
                float scan = 1.0 - smoothstep(_LineWidth, _LineWidth + aa, d);

                float flicker = 1.0 - _Flicker * (0.5 + 0.5 * sin(_Time.y * 57.0));

                float a = (_FillAlpha + rim * _RimStrength + scan * _LineStrength) * flicker * _Alpha;
                half3 col = _Color.rgb * _Intensity * a;
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
}
