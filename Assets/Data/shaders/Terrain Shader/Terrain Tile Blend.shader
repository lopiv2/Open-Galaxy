Shader "Custom/URP/Terrain Tile Blend"
{
    Properties
    {
        [Header(Current Tile)]
        _GroundMap ("Ground", 2D) = "white" {}
        _CliffMap ("Shared Cliff", 2D) = "gray" {}
        _CliffMapB ("Shared Cliff B", 2D) = "gray" {}

        [Header(Surface Detail)]
        _GroundNormalMap ("Ground Normal", 2D) = "bump" {}
        _GroundOcclusionMap ("Ground Occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _BumpScale ("Normal Strength", Range(0, 2)) = 1

        [Header(Closeup Detail)]
        _DetailMap ("Detail Albedo", 2D) = "white" {}
        _DetailNormalMap ("Detail Normal", 2D) = "bump" {}
        _DetailHeightMap ("Detail Height", 2D) = "black" {}
        _DetailOcclusionMap ("Detail AO", 2D) = "white" {}
        _DetailEnabled ("Detail Enabled", Float) = 0
        _DetailTiling ("Detail Tiling", Float) = 0.05
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.6
        _DetailNormalStrength ("Detail Normal Strength", Range(0, 2)) = 0.8
        _DetailHeightScale ("Detail Height Scale", Range(0, 0.2)) = 0.015
        _DetailAOStrength ("Detail AO Strength", Range(0, 1)) = 0.8
        _DetailFadeDistance ("Detail Fade Distance", Float) = 350

        [Header(Terrain Overlays)]
        _OverlayEnabled ("Overlay Enabled", Float) = 0

        _Overlay0Map ("Overlay 0 Albedo", 2D) = "white" {}
        _Overlay0NormalMap ("Overlay 0 Normal", 2D) = "bump" {}
        _Overlay0AOMap ("Overlay 0 AO", 2D) = "white" {}
        _Overlay0A ("0 Tiling/Strength/Fade/Mode", Vector) = (0.05, 0, 600, 0)
        _Overlay0B ("0 Width/Feather/Wander/Freq", Vector) = (3, 4, 0, 0.002)
        _Overlay0C ("0 Noise/Threshold/Patch Feather/Seed", Vector) = (0.002, 0.5, 0.12, 0)
        _Overlay0D ("0 Dir/Offset/Phase", Vector) = (1, 0, 0, 0)
        _Overlay0E ("0 Normal/AO/Segments/Unused", Vector) = (0.5, 0.5, 0, 0)

        _Overlay1Map ("Overlay 1 Albedo", 2D) = "white" {}
        _Overlay1NormalMap ("Overlay 1 Normal", 2D) = "bump" {}
        _Overlay1AOMap ("Overlay 1 AO", 2D) = "white" {}
        _Overlay1A ("1 Tiling/Strength/Fade/Mode", Vector) = (0.05, 0, 600, 0)
        _Overlay1B ("1 Width/Feather/Wander/Freq", Vector) = (3, 4, 0, 0.002)
        _Overlay1C ("1 Noise/Threshold/Patch Feather/Seed", Vector) = (0.002, 0.5, 0.12, 0)
        _Overlay1D ("1 Dir/Offset/Phase", Vector) = (1, 0, 0, 0)
        _Overlay1E ("1 Normal/AO/Segments/Unused", Vector) = (0.5, 0.5, 0, 0)

        _Overlay2Map ("Overlay 2 Albedo", 2D) = "white" {}
        _Overlay2NormalMap ("Overlay 2 Normal", 2D) = "bump" {}
        _Overlay2AOMap ("Overlay 2 AO", 2D) = "white" {}
        _Overlay2A ("2 Tiling/Strength/Fade/Mode", Vector) = (0.05, 0, 600, 0)
        _Overlay2B ("2 Width/Feather/Wander/Freq", Vector) = (3, 4, 0, 0.002)
        _Overlay2C ("2 Noise/Threshold/Patch Feather/Seed", Vector) = (0.002, 0.5, 0.12, 0)
        _Overlay2D ("2 Dir/Offset/Phase", Vector) = (1, 0, 0, 0)
        _Overlay2E ("2 Normal/AO/Segments/Unused", Vector) = (0.5, 0.5, 0, 0)

        [Header(Neighbor Ground Textures)]
        _RightGroundMap ("Right Ground", 2D) = "white" {}
        _FrontGroundMap ("Front Ground", 2D) = "white" {}

        [Header(Procedural Border Blending)]
        _BorderBlend ("Border Blend Width", Range(0.001, 0.5)) = 0.12
        _BlendRoughness ("Blend Roughness", Range(0, 1)) = 0.35
        _BlendNoiseTiling ("Blend Noise Tiling", Float) = 3

        [Header(Cliff)]
        _CliffStart ("Cliff Slope Start", Range(0, 1)) = 0.65
        _CliffSoftness ("Cliff Slope Softness", Range(0.001, 1)) = 0.2
        _CliffBlendAmount ("Cliff Map Blend", Range(0, 1)) = 0

        [Header(Texture Settings)]
        _TextureTiling ("Texture Tiling", Float) = 1
        _Brightness ("Brightness", Range(0, 2)) = 1

        _MiterSoftness ("Miter Softness", Range(0.0, 0.2)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GroundMap);
            SAMPLER(sampler_GroundMap);

            TEXTURE2D(_CliffMap);
            SAMPLER(sampler_CliffMap);

            TEXTURE2D(_CliffMapB);
            SAMPLER(sampler_CliffMapB);

            TEXTURE2D(_RightGroundMap);
            SAMPLER(sampler_RightGroundMap);

            TEXTURE2D(_FrontGroundMap);
            SAMPLER(sampler_FrontGroundMap);

            TEXTURE2D(_GroundNormalMap);
            SAMPLER(sampler_GroundNormalMap);

            TEXTURE2D(_GroundOcclusionMap);
            SAMPLER(sampler_GroundOcclusionMap);

            TEXTURE2D(_DetailMap);
            SAMPLER(sampler_DetailMap);

            TEXTURE2D(_DetailNormalMap);
            SAMPLER(sampler_DetailNormalMap);

            TEXTURE2D(_DetailHeightMap);
            SAMPLER(sampler_DetailHeightMap);

            TEXTURE2D(_DetailOcclusionMap);
            SAMPLER(sampler_DetailOcclusionMap);

            TEXTURE2D(_Overlay0Map);
            TEXTURE2D(_Overlay0NormalMap);
            TEXTURE2D(_Overlay0AOMap);

            TEXTURE2D(_Overlay1Map);
            TEXTURE2D(_Overlay1NormalMap);
            TEXTURE2D(_Overlay1AOMap);

            TEXTURE2D(_Overlay2Map);
            TEXTURE2D(_Overlay2NormalMap);
            TEXTURE2D(_Overlay2AOMap);

            CBUFFER_START(UnityPerMaterial)

                half _BorderBlend;
                half _BlendRoughness;
                half _BlendNoiseTiling;
                half _MiterSoftness;

                half _CliffStart;
                half _CliffSoftness;
                half _CliffBlendAmount;

                half _TextureTiling;
                half _Brightness;

                half _OcclusionStrength;
                half _BumpScale;

                half _DetailEnabled;
                half _DetailTiling;
                half _DetailStrength;
                half _DetailNormalStrength;
                half _DetailHeightScale;
                half _DetailAOStrength;
                half _DetailFadeDistance;

                half _OverlayEnabled;

                float4 _Overlay0A;
                float4 _Overlay0B;
                float4 _Overlay0C;
                float4 _Overlay0D;
                float4 _Overlay0E;
                float4 _Overlay0Segments[16];

                float4 _Overlay1A;
                float4 _Overlay1B;
                float4 _Overlay1C;
                float4 _Overlay1D;
                float4 _Overlay1E;
                float4 _Overlay1Segments[16];

                float4 _Overlay2A;
                float4 _Overlay2B;
                float4 _Overlay2C;
                float4 _Overlay2D;
                float4 _Overlay2E;
                float4 _Overlay2Segments[16];

            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 cliffMask  : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS =
                    NormalizeNormalPerVertex(normalInputs.normalWS);

                output.uv = input.uv * _TextureTiling;
                output.cliffMask = input.color;

                return output;
            }

            half3 SampleGround(
                TEXTURE2D_PARAM(groundTexture, groundSampler),
                float2 uv)
            {
                return SAMPLE_TEXTURE2D(
                    groundTexture,
                    groundSampler,
                    uv
                ).rgb;
            }

            half3 SampleCurrentGround(float2 uv)
            {
                return SampleGround(
                    TEXTURE2D_ARGS(_GroundMap, sampler_GroundMap),
                    uv
                );
            }

            half3 SampleSharedCliff(float2 uv)
            {
                return SAMPLE_TEXTURE2D(
                    _CliffMap,
                    sampler_CliffMap,
                    uv
                ).rgb;
            }

            half3 SampleSharedCliffB(float2 uv)
            {
                return SAMPLE_TEXTURE2D(
                    _CliffMapB,
                    sampler_CliffMapB,
                    uv
                ).rgb;
            }

            // Procedural hash function.
            float Hash21(float2 position)
            {
                position = frac(
                    position * float2(123.34, 456.21)
                );

                position += dot(
                    position,
                    position + 45.32
                );

                return frac(position.x * position.y);
            }

            // Smooth procedural value noise.
            float ValueNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 local = frac(position);

                local = local * local * (3.0 - 2.0 * local);

                float bottomLeft =
                    Hash21(cell);

                float bottomRight =
                    Hash21(cell + float2(1.0, 0.0));

                float topLeft =
                    Hash21(cell + float2(0.0, 1.0));

                float topRight =
                    Hash21(cell + float2(1.0, 1.0));

                float bottom = lerp(
                    bottomLeft,
                    bottomRight,
                    local.x
                );

                float top = lerp(
                    topLeft,
                    topRight,
                    local.x
                );

                return lerp(bottom, top, local.y);
            }

            float GetBlendNoise(float3 positionWS)
            {
                float2 noisePosition =
                    positionWS.xz *
                    _BlendNoiseTiling *
                    0.01;

                // Convert noise from 0..1 to -1..1.
                return ValueNoise(noisePosition) * 2.0 - 1.0;
            }

            // Only blend to right (+X) and front (+Y) neighbors to avoid mutual blending seams.
            half3 BlendNeighborGround(
    float2 tileUV,
    float3 positionWS,
    half3 currentGround)
{
    half noise = (half)GetBlendNoise(positionWS);

    half edgeOffset =
        noise *
        _BlendRoughness *
        _BorderBlend;

    // Strip gradients: 0 on the inner edge of the strip, 1 at the tile edge.
    half rightWeight = smoothstep(
        1.0 - _BorderBlend,
        1.0,
        tileUV.x + edgeOffset
    );

    half frontWeight = smoothstep(
        1.0 - _BorderBlend,
        1.0,
        tileUV.y + edgeOffset
    );

    // Mitered ends: the strips only exist in the triangle above the
    // diagonal u + v = 1. This gives the 45-degree cuts at the
    // top-left (front strip) and bottom-right (right strip) corners.
    half miter = smoothstep(
        0.0,
        max(_MiterSoftness, 0.0001),
        tileUV.x + tileUV.y + edgeOffset - 1.0
    );

    rightWeight *= miter;
    frontWeight *= miter;

    half3 rightGround = SampleGround(
        TEXTURE2D_ARGS(_RightGroundMap, sampler_RightGroundMap),
        tileUV
    );

    half3 frontGround = SampleGround(
        TEXTURE2D_ARGS(_FrontGroundMap, sampler_FrontGroundMap),
        tileUV
    );

    half3 color = currentGround;
    color = lerp(color, rightGround, rightWeight);
    color = lerp(color, frontGround, frontWeight);

    return color;
}

            // Normal mapping without a tangent stream: builds the TBN frame
            // from screen-space derivatives (standard technique for
            // generated meshes). Missing map = neutral via "bump" default.
            half3 PerturbGroundNormal(
                half3 positionWS,
                half3 normalWS,
                float2 uv)
            {
                half3 mapN = UnpackNormal(
                    SAMPLE_TEXTURE2D(
                        _GroundNormalMap,
                        sampler_GroundNormalMap,
                        uv
                    )
                );

                mapN.xy *= _BumpScale;

                half3 q0 = ddx(positionWS);
                half3 q1 = ddy(positionWS);
                half2 st0 = ddx(uv);
                half2 st1 = ddy(uv);

                half3 N = normalize(normalWS);
                half3 T = normalize(q0 * st1.y - q1 * st0.y);
                half3 B = -normalize(cross(N, T));
                half3x3 TBN = half3x3(T, B, N);

                return normalize(mul(mapN, TBN));
            }

            half GroundOcclusion(float2 uv)
            {
                half ao = SAMPLE_TEXTURE2D(
                    _GroundOcclusionMap,
                    sampler_GroundOcclusionMap,
                    uv
                ).g;

                return lerp(1.0h, ao, _OcclusionStrength);
            }

            // Close-up normal detail, blended over the (already perturbed)
            // ground normal. TBN is rebuilt from derivatives so no tangent
            // stream is needed on the generated mesh.
            half3 ApplyDetailNormal(
                half3 baseNormalWS,
                float3 positionWS,
                float2 detailUV)
            {
                half3 mapN = UnpackNormal(
                    SAMPLE_TEXTURE2D(
                        _DetailNormalMap,
                        sampler_DetailNormalMap,
                        detailUV
                    )
                );

                half3 q0 = ddx(positionWS);
                half3 q1 = ddy(positionWS);
                half2 st0 = ddx(detailUV);
                half2 st1 = ddy(detailUV);

                half3 N = normalize(baseNormalWS);
                half3 T = normalize(q0 * st1.y - q1 * st0.y);
                half3 B = -normalize(cross(N, T));
                half3x3 TBN = half3x3(T, B, N);

                half3 detailNormal = normalize(mul(mapN, TBN));
                return normalize(lerp(N, detailNormal,
                    saturate(_DetailNormalStrength)));
            }

            // Distance from a world position to a path segment. Used for POI paths.
            float OverlaySegmentDistance(float2 p, float2 a, float2 b)
            {
                float2 segment = b - a;
                float lengthSquared = max(dot(segment, segment), 0.0001);
                float t = saturate(dot(p - a, segment) / lengthSquared);
                return length(p - (a + segment * t));
            }

            // Procedural mask for one overlay slot.
            // mode 0 = noise patches, 1 = baked cliff foot, 2 = free trail,
            // 3 = polyline connecting POIs.
            float OverlayMask(
                float3 positionWS,
                float4 cliffMask,
                float4 pA,
                float4 pB,
                float4 pC,
                float4 pD,
                float4 pathSegments[16],
                float segmentCount)
            {
                int mode = (int)(pA.w + 0.5);
                float2 worldXZ = positionWS.xz;
                float seedOffset = pC.w;
                float noiseScale = max(0.00001, pC.x);

                if (mode == 1)
                {
                    float breakup = ValueNoise(worldXZ * noiseScale + seedOffset);
                    return saturate(cliffMask.r * lerp(0.35, 1.0,
                        smoothstep(0.2, 0.8, breakup)));
                }

                if (mode == 0)
                {
                    float field = ValueNoise(worldXZ * noiseScale + seedOffset) * 0.65
                        + ValueNoise(worldXZ * noiseScale * 2.7 + seedOffset + 13.7) * 0.35;
                    return smoothstep(pC.y,
                        pC.y + max(0.005, pC.z), field);
                }

                float directionLength = max(0.0001, length(pD.xy));
                float2 direction = pD.xy / directionLength;
                float2 perpendicular = float2(-direction.y, direction.x);
                float along = dot(worldXZ, direction);

                float noise = ValueNoise(float2(along * max(0.00001, pB.w), seedOffset));
                float wander = (sin(along * pB.w + pD.w)
                    + (noise - 0.5) * 2.0) * pB.z;
                float2 warped = worldXZ + perpendicular * wander;

                float distanceToPath = 1000000.0;

                if (segmentCount > 0.5)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        if ((float)i >= segmentCount)
                            break;

                        float4 segment = pathSegments[i];
                        distanceToPath = min(distanceToPath,
                            OverlaySegmentDistance(warped, segment.xy, segment.zw));
                    }
                }
                else if (mode == 2)
                {
                    distanceToPath = abs(dot(warped, perpendicular) - pD.z);
                }

                float width = max(0.05, pB.x);
                // Feather is the soft edge around the configured width. Capping
                // it keeps a large value from swallowing width changes.
                float feather = clamp(pB.y, 0.05, width);
                float edgeNoise = (ValueNoise(warped * noiseScale * 3.0 + seedOffset)
                    - 0.5) * feather * 0.15;

                return 1.0 - smoothstep(width, width + feather,
                    distanceToPath + edgeNoise);
            }

            half3 ApplyOverlayNormal(
                TEXTURE2D_PARAM(normalTexture, normalSampler),
                half3 baseNormalWS,
                float3 positionWS,
                float2 uv,
                half strength)
            {
                if (strength <= 0.001h)
                    return baseNormalWS;

                half3 mapN = UnpackNormal(
                    SAMPLE_TEXTURE2D(normalTexture, normalSampler, uv)
                );

                mapN.xy *= strength;

                half3 q0 = ddx(positionWS);
                half3 q1 = ddy(positionWS);
                half2 st0 = ddx(uv);
                half2 st1 = ddy(uv);

                half3 N = normalize(baseNormalWS);
                half3 T = normalize(q0 * st1.y - q1 * st0.y);
                half3 B = -normalize(cross(N, T));
                half3x3 TBN = half3x3(T, B, N);

                return normalize(lerp(N, normalize(mul(mapN, TBN)),
                    saturate(strength)));
            }

            void ApplyOverlayLayer(
                TEXTURE2D_PARAM(albedoTex, albedoSampler),
                TEXTURE2D_PARAM(normalTex, normalSampler),
                TEXTURE2D_PARAM(aoTex, aoSampler),
                inout half3 surfaceColor,
                inout half3 surfaceNormal,
                float mask,
                float tiling,
                float fadeDistance,
                float strength,
                float normalStrength,
                float aoStrength,
                float3 positionWS,
                half wallProtection)
            {
                if (mask <= 0.001 || strength <= 0.001)
                    return;

                float cameraDistance = distance(positionWS, _WorldSpaceCameraPos);
                half fade = fadeDistance <= 0.0 ? 1.0h : 1.0h - smoothstep(
                    fadeDistance * 0.6, fadeDistance, cameraDistance);
                half amount = saturate(mask * strength * fade * wallProtection);

                if (amount <= 0.001)
                    return;

                float2 uv = positionWS.xz * tiling;

                half3 overlayColor = SAMPLE_TEXTURE2D(
                    albedoTex, albedoSampler, uv).rgb;
                surfaceColor = lerp(surfaceColor, overlayColor, amount);

                half overlayAO = SAMPLE_TEXTURE2D(
                    aoTex, aoSampler, uv).g;
                surfaceColor *= lerp(1.0h, overlayAO,
                    saturate(aoStrength * amount));

                surfaceNormal = ApplyOverlayNormal(
                    TEXTURE2D_ARGS(normalTex, normalSampler),
                    surfaceNormal,
                    positionWS,
                    uv,
                    normalStrength * amount);
            }

            // Cliff-foot masks are baked from the terrain shape and already
            // exclude the wall, so they must not be weakened by slope checks.
            half OverlayWallProtection(float mode, half cliffAmount)
            {
                return abs(mode - 1.0f) < 0.5f
                    ? 1.0h
                    : saturate(1.0h - cliffAmount * 0.9h);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 groundNormalWS = PerturbGroundNormal(
                    input.positionWS,
                    normalize(input.normalWS),
                    input.uv
                );

                // Horizontal surfaces have normal.y near 1.
                // Vertical surfaces have normal.y near 0. Cliff detection
                // uses the ground normal only: detail bumps must not make
                // flat dunes look like cliff walls.
                half cliffAmount = saturate(
                    (_CliffStart - groundNormalWS.y) /
                    _CliffSoftness
                );

                // Coordinates within the current tile.
                float2 tileUV = frac(input.uv);

                // Blend only the ground textures.
                half3 groundColor =
                    SampleCurrentGround(input.uv);

                groundColor = BlendNeighborGround(
                    tileUV,
                    input.positionWS,
                    groundColor
                );

                // Baked crevice shadowing from the AO map (ground only).
                groundColor *= GroundOcclusion(input.uv);

                // Close-up detail: world-space tiled maps, so walking over a
                // tile never reveals UV stretching or per-tile repetition.
                half detailAmount = saturate(
                    _DetailEnabled
                    * _DetailStrength
                    * (1.0h - smoothstep(
                        _DetailFadeDistance * 0.6,
                        _DetailFadeDistance,
                        distance(input.positionWS, _WorldSpaceCameraPos)))
                );

                float2 detailUV = input.positionWS.xz * _DetailTiling;

                // Height map drives a mild parallax offset: close grains
                // shift slightly against the more distant dune surface.
                half detailHeight = SAMPLE_TEXTURE2D(
                    _DetailHeightMap,
                    sampler_DetailHeightMap,
                    detailUV
                ).r;

                float3 viewDirWS = normalize(
                    _WorldSpaceCameraPos - input.positionWS
                );

                float2 parallax = viewDirWS.xz
                    / max(0.35, abs(viewDirWS.y))
                    * (detailHeight - 0.5)
                    * _DetailHeightScale
                    * 5.0;

                float2 detailUVFinal = detailUV + parallax;

                half3 detailAlbedo = SAMPLE_TEXTURE2D(
                    _DetailMap,
                    sampler_DetailMap,
                    detailUVFinal
                ).rgb;

                half detailAO = SAMPLE_TEXTURE2D(
                    _DetailOcclusionMap,
                    sampler_DetailOcclusionMap,
                    detailUVFinal
                ).g;

                half3 detailedGround = groundColor
                    * saturate(detailAlbedo * 2.0h);

                detailedGround *= lerp(1.0h, detailAO,
                    _DetailAOStrength);

                groundColor = lerp(groundColor, detailedGround,
                    detailAmount);

                half3 litNormal = ApplyDetailNormal(
                    groundNormalWS,
                    input.positionWS,
                    detailUVFinal
                );

                litNormal = normalize(lerp(groundNormalWS, litNormal,
                    detailAmount));

                // Per-planet overlays: cliff-foot sand, paths, city patches.
                // Masks are deterministic from the planet seed / baked cliff
                // data and need no splatmap assets.
                if (_OverlayEnabled > 0.5)
                {
                    float overlayMask0 = OverlayMask(input.positionWS,
                        input.cliffMask, _Overlay0A, _Overlay0B, _Overlay0C,
                        _Overlay0D, _Overlay0Segments, _Overlay0E.z);
                    ApplyOverlayLayer(
                        TEXTURE2D_ARGS(_Overlay0Map, sampler_DetailMap),
                        TEXTURE2D_ARGS(_Overlay0NormalMap, sampler_DetailMap),
                        TEXTURE2D_ARGS(_Overlay0AOMap, sampler_DetailMap),
                        groundColor, litNormal, overlayMask0,
                        _Overlay0A.x, _Overlay0A.z, _Overlay0A.y,
                        _Overlay0E.x, _Overlay0E.y,
                        input.positionWS,
                        OverlayWallProtection(_Overlay0A.w, cliffAmount));

                    float overlayMask1 = OverlayMask(input.positionWS,
                        input.cliffMask, _Overlay1A, _Overlay1B, _Overlay1C,
                        _Overlay1D, _Overlay1Segments, _Overlay1E.z);
                    ApplyOverlayLayer(
                        TEXTURE2D_ARGS(_Overlay1Map, sampler_DetailMap),
                        TEXTURE2D_ARGS(_Overlay1NormalMap, sampler_DetailMap),
                        TEXTURE2D_ARGS(_Overlay1AOMap, sampler_DetailMap),
                        groundColor, litNormal, overlayMask1,
                        _Overlay1A.x, _Overlay1A.z, _Overlay1A.y,
                        _Overlay1E.x, _Overlay1E.y,
                        input.positionWS,
                        OverlayWallProtection(_Overlay1A.w, cliffAmount));

                    float overlayMask2 = OverlayMask(input.positionWS,
                        input.cliffMask, _Overlay2A, _Overlay2B, _Overlay2C,
                        _Overlay2D, _Overlay2Segments, _Overlay2E.z);
                    ApplyOverlayLayer(
                        TEXTURE2D_ARGS(_Overlay2Map, sampler_DetailMap),
                        TEXTURE2D_ARGS(_Overlay2NormalMap, sampler_DetailMap),
                        TEXTURE2D_ARGS(_Overlay2AOMap, sampler_DetailMap),
                        groundColor, litNormal, overlayMask2,
                        _Overlay2A.x, _Overlay2A.z, _Overlay2A.y,
                        _Overlay2E.x, _Overlay2E.y,
                        input.positionWS,
                        OverlayWallProtection(_Overlay2A.w, cliffAmount));
                }

                // Cliff maps: two textures blended with world-space noise so
                // walls do not switch abruptly at tile borders.
                half cliffPatch = smoothstep(
                    0.35,
                    0.65,
                    ValueNoise(input.positionWS.xz * 0.0012)
                );

                half cliffBlend = cliffPatch * _CliffBlendAmount;

                half3 cliffColor = lerp(
                    SampleSharedCliff(input.uv),
                    SampleSharedCliffB(input.uv),
                    cliffBlend
                );

                // The baked foot mask keeps cliff textures on the wall and
                // lets the coast sand show at the lower edge.
                half cliffFootProtection = 1.0h
                    - saturate(input.cliffMask.r);

                half3 color = lerp(
                    groundColor,
                    cliffColor,
                    cliffAmount * cliffFootProtection
                );

                Light mainLight = GetMainLight();

                half diffuse = saturate(
                    dot(litNormal, mainLight.direction)
                );

                half lighting = 0.35 + diffuse * 0.65;

                color *= mainLight.color;
                color *= lighting;
                color *= _Brightness;

                return half4(color, 1.0);
            }

            ENDHLSL
        }
    }
}