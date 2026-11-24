Shader "Custom/VegetationInstanced"
{
    Properties
    {
        _BaseMap ("Albedo (RGB)", 2D) = "white" {}
        _OpacityMap ("Opacity (R)", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.45
        _UseOpacityMap ("Use Opacity Map (0=solid mesh, 1=use mask)", Float) = 0
        _Brightness ("Brightness", Range(0,4)) = 1.6
        _WindDirection ("Wind Direction (XZ)", Vector) = (1,0,0,0)
        _WindStrength ("Wind Strength (m)", Float) = 0.06
        _WindSpeed ("Wind Speed (rad/s)", Float) = 2.2
        _WindFrequency ("Wind Spatial Frequency", Float) = 0.15
        _WindHeight ("Model Height (m)", Float) = 0.47
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        LOD 100
        Cull Off
        ZWrite On

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_OpacityMap); SAMPLER(sampler_OpacityMap);
            float4 _BaseColor;
            float _Cutoff;
            float _UseOpacityMap;
            float _Brightness;
            float4 _WindDirection;
            float _WindStrength;
            float _WindSpeed;
            float _WindFrequency;
            float _WindHeight;

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                // UNITY_MATRIX_M (not unity_ObjectToWorld) is required for
                // Graphics.DrawMeshInstanced: Unity maps it to the per-instance
                // matrix array, while unity_ObjectToWorld holds the batch matrix.
                float3 worldPos = mul(UNITY_MATRIX_M, float4(input.positionOS.xyz, 1)).xyz;
                float height01 = saturate(input.positionOS.y / max(0.001, _WindHeight));
                float bend = height01 * height01;
                float2 windDir = _WindDirection.xz;
                windDir /= max(0.0001, length(windDir));
                float instanceOffset = 0.0;
                #ifdef UNITY_INSTANCING_ENABLED
                    instanceOffset = (float)(unity_InstanceID % 97) * 0.7;
                #endif
                float phase = _Time.y * _WindSpeed
                    + dot(worldPos.xz, windDir) * _WindFrequency
                    + instanceOffset;
                float sway = sin(phase) * _WindStrength;
                float flutter = sin(phase * 2.7 + 1.3) * _WindStrength * 0.35;
                worldPos.xz += windDir * (sway + flutter) * bend;
                worldPos.y += cos(phase * 1.9) * _WindStrength * 0.2 * bend;

                o.positionCS = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.uv = input.uv;
                return o;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float mask = SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, input.uv).r;
                float opacity = lerp(1.0, mask, saturate(_UseOpacityMap));
                clip(tex.a * opacity * _BaseColor.a - _Cutoff);
                float3 color = saturate(tex.rgb * _BaseColor.rgb * _Brightness);
                return float4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
