Shader "UI/CatCafePieceAlphaGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Pawn RGBA Atlas", 2D) = "white" {}
        _PawnAtlas ("Pawn RGBA Alpha Atlas", 2D) = "black" {}
        _AtlasUvRect ("Atlas UV Rect", Vector) = (0, 0, 1, 1)
        _AtlasContentSize ("Atlas Content Size", Float) = 128
        _GlowRadius ("Glow Radius", Range(0.5, 32)) = 15
        _GlowSoftness ("Glow Softness", Range(0.1, 16)) = 8
        _GlowCoreWidth ("Glow Inner Fade", Range(0.1, 8)) = 3.5
        _GlowStrength ("Glow Strength", Range(0, 4)) = 1
        _GlowEmission ("Glow Emission", Range(0, 12)) = 8
        _GlowSize ("Geometry Padding", Range(0, 32)) = 18
        _GlowBaseSize ("Base Size", Vector) = (104, 104, 0, 0)
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]

        Pass
        {
            Name "PawnGlow"
            Blend SrcAlpha One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragGlow
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 pawnUv01 : TEXCOORD0;
            };

            // Keep the glow sampling source separate from CanvasRenderer's
            // per-renderer _MainTex binding. The runtime material binds the
            // generated RGBA atlas explicitly to _PawnAtlas.
            sampler2D _MainTex;
            sampler2D _PawnAtlas;
            float4 _PawnAtlas_TexelSize;
            float4 _AtlasUvRect;
            float _AtlasContentSize;
            float _GlowRadius;
            float _GlowSoftness;
            float _GlowCoreWidth;
            float _GlowStrength;
            float _GlowEmission;
            float _GlowSize;
            float4 _GlowBaseSize;

            v2f vert(appdata_t input)
            {
                v2f output;
                float2 baseSize =
                    max(_GlowBaseSize.xy, float2(1.0, 1.0));

                float2 atlasRectSize = max(
                    _AtlasUvRect.zw,
                    _PawnAtlas_TexelSize.xy);
                float2 sourceUv01 = saturate(
                    (input.texcoord - _AtlasUvRect.xy) /
                    atlasRectSize);
                float2 edgeDirection =
                    sourceUv01 * 2.0 - 1.0;
                float2 paddingUv =
                    _GlowSize / baseSize;

                // Expand from the sprite UV center, not from local vertex
                // coordinates. This remains centered for every pivot,
                // rotation and Canvas transform.
                input.vertex.xy +=
                    edgeDirection * _GlowSize;

                output.vertex = UnityObjectToClipPos(input.vertex);
                output.pawnUv01 =
                    sourceUv01 +
                    edgeDirection * paddingUv;
                output.color = input.color;
                return output;
            }

            float SampleAlpha(float2 uv01)
            {
                float inBounds =
                    step(0.0, uv01.x) *
                    step(0.0, uv01.y) *
                    step(uv01.x, 1.0) *
                    step(uv01.y, 1.0);

                float2 halfTexel =
                    _PawnAtlas_TexelSize.xy * 0.5;
                float2 atlasMin =
                    _AtlasUvRect.xy + halfTexel;
                float2 atlasMax =
                    _AtlasUvRect.xy +
                    _AtlasUvRect.zw - halfTexel;
                float2 atlasUv = lerp(
                    atlasMin,
                    atlasMax,
                    saturate(uv01));

                return tex2D(_PawnAtlas, atlasUv).a *
                    inBounds;
            }

            float SampleRingEightAxis(
                float2 uv01,
                float2 radius)
            {
                const float diagonal = 0.70710678;
                float2 diagonalRadius =
                    radius * diagonal;
                float alpha = 0.0;

                alpha += SampleAlpha(
                    uv01 + float2(radius.x, 0.0));
                alpha += SampleAlpha(
                    uv01 - float2(radius.x, 0.0));
                alpha += SampleAlpha(
                    uv01 + float2(0.0, radius.y));
                alpha += SampleAlpha(
                    uv01 - float2(0.0, radius.y));

                alpha += SampleAlpha(
                    uv01 + diagonalRadius);
                alpha += SampleAlpha(
                    uv01 - diagonalRadius);
                alpha += SampleAlpha(
                    uv01 + float2(
                        diagonalRadius.x,
                        -diagonalRadius.y));
                alpha += SampleAlpha(
                    uv01 + float2(
                        -diagonalRadius.x,
                        diagonalRadius.y));

                return alpha * 0.125;
            }

            float SampleRingEightOffset(
                float2 uv01,
                float2 radius)
            {
                const float shallowX = 0.92387953;
                const float shallowY = 0.38268343;
                float2 shallowRadius = float2(
                    radius.x * shallowX,
                    radius.y * shallowY);
                float2 steepRadius = float2(
                    radius.x * shallowY,
                    radius.y * shallowX);
                float alpha = 0.0;

                alpha += SampleAlpha(
                    uv01 + shallowRadius);
                alpha += SampleAlpha(
                    uv01 - shallowRadius);
                alpha += SampleAlpha(
                    uv01 + float2(
                        shallowRadius.x,
                        -shallowRadius.y));
                alpha += SampleAlpha(
                    uv01 + float2(
                        -shallowRadius.x,
                        shallowRadius.y));

                alpha += SampleAlpha(
                    uv01 + steepRadius);
                alpha += SampleAlpha(
                    uv01 - steepRadius);
                alpha += SampleAlpha(
                    uv01 + float2(
                        steepRadius.x,
                        -steepRadius.y));
                alpha += SampleAlpha(
                    uv01 + float2(
                        -steepRadius.x,
                        steepRadius.y));
                return alpha * 0.125;
            }

            float4 fragGlow(v2f input) : SV_Target
            {
                float2 baseSize =
                    max(_GlowBaseSize.xy, float2(1.0, 1.0));
                float atlasPixelUi = max(
                    max(baseSize.x, baseSize.y) /
                    max(_AtlasContentSize, 1.0),
                    0.001);
                float radiusUi =
                    max(_GlowRadius, atlasPixelUi);
                float softnessUi = min(
                    max(_GlowSoftness, atlasPixelUi),
                    radiusUi);
                float centerAlpha =
                    SampleAlpha(input.pawnUv01);
                float shellRadius1 = max(
                    softnessUi * 0.14,
                    atlasPixelUi);
                float shellRadius2 = max(
                    softnessUi * 0.28,
                    shellRadius1);
                float shellRadius3 = max(
                    softnessUi * 0.42,
                    shellRadius2);
                float shellRadius4 = max(
                    softnessUi * 0.60,
                    shellRadius3);
                float shellRadius5 = max(
                    softnessUi * 0.78,
                    shellRadius4);
                float shellRadius6 = max(
                    softnessUi,
                    shellRadius5);
                float shellRadius7 = lerp(
                    shellRadius6,
                    radiusUi,
                    0.55);

                // Alternating the angular samples between shells fills the
                // whole disk and prevents visible radial spokes.
                float shell1 = SampleRingEightAxis(
                    input.pawnUv01,
                    shellRadius1 / baseSize);
                float shell2 = SampleRingEightOffset(
                    input.pawnUv01,
                    shellRadius2 / baseSize);
                float shell3 = SampleRingEightAxis(
                    input.pawnUv01,
                    shellRadius3 / baseSize);
                float shell4 = SampleRingEightOffset(
                    input.pawnUv01,
                    shellRadius4 / baseSize);
                float shell5 = SampleRingEightAxis(
                    input.pawnUv01,
                    shellRadius5 / baseSize);
                float shell6 = SampleRingEightOffset(
                    input.pawnUv01,
                    shellRadius6 / baseSize);
                float shell7 = SampleRingEightAxis(
                    input.pawnUv01,
                    shellRadius7 / baseSize);
                float shell8 = SampleRingEightOffset(
                    input.pawnUv01,
                    radiusUi / baseSize);

                // Inner shells form the bright core, middle shells carry the
                // readable soft body, and the last two shells form the tail.
                float glowField = saturate(
                    (shell1 * 0.20 +
                     shell2 * 0.18 +
                     shell3 * 0.16 +
                     shell4 * 0.14 +
                     shell5 * 0.12 +
                     shell6 * 0.09 +
                     shell7 * 0.07 +
                     shell8 * 0.04) * 2.35);
                float softnessRatio = saturate(
                    softnessUi / radiusUi);
                float falloffPower = lerp(
                    0.92,
                    0.72,
                    softnessRatio);
                glowField = pow(glowField, falloffPower);

                float coreThreshold =
                    saturate(_GlowCoreWidth / 8.0);
                float outsidePawn =
                    1.0 - smoothstep(
                        0.0,
                        max(coreThreshold, 0.001),
                        centerAlpha);
                float glowAlpha = saturate(
                    glowField *
                    outsidePawn *
                    _GlowStrength *
                    input.color.a);

                float3 glowRgb =
                    input.color.rgb * _GlowEmission;
                return float4(glowRgb, glowAlpha);
            }
            ENDCG
        }
    }
}
