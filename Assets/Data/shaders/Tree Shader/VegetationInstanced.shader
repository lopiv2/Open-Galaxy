Shader "Custom/VegetationInstanced"
{
    Properties
    {
        _BaseMap ("Albedo (RGB)", 2D) = "white" {}
        _OpacityMap ("Opacity (R)", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.45
        _UseOpacityMap ("Use Opacity Map (0=solid mesh, 1=billboard)", Float) = 0
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
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
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

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<float4x4> _TransformBuffer;
            #endif

            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    float4x4 data = _TransformBuffer[unity_InstanceID];
                    unity_ObjectToWorld = data;
                    float3x3 w2oRotation = transpose((float3x3)data);
                    unity_WorldToObject._m00_m01_m02 = w2oRotation._m00_m01_m02;
                    unity_WorldToObject._m10_m11_m12 = w2oRotation._m10_m11_m12;
                    unity_WorldToObject._m20_m21_m22 = w2oRotation._m20_m21_m22;
                    unity_WorldToObject._m03_m13_m23 = -mul(w2oRotation, data._m03_m13_m23);
                    unity_WorldToObject._m30_m31_m32_m33 = float4(0,0,0,1);
                #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                setup();

                float3 worldPos = mul(unity_ObjectToWorld, float4(input.positionOS.xyz, 1)).xyz;
                float height01 = saturate(input.positionOS.y / max(0.001, _WindHeight));
                float bend = height01 * height01;
                float2 windDir = _WindDirection.xz;
                windDir /= max(0.0001, length(windDir));
                float instanceOffset = 0.0;
                #if defined(UNITY_VERTEX_INPUT_INSTANCE_ID)
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
