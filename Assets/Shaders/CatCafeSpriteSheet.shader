Shader "UI/CatCafeSpriteSheet"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Sheet", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Columns ("Columns", Float) = 1
        _Rows ("Rows", Float) = 1
        _FrameRate ("Frame Rate", Float) = 1
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float _Columns;
            float _Rows;
            float _FrameRate;
            float4 _ClipRect;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float columns = max(1.0, floor(_Columns + 0.5));
                float rows = max(1.0, floor(_Rows + 0.5));
                float frameCount = columns * rows;
                float frame = floor(fmod(_Time.y * max(_FrameRate, 0.001), frameCount));
                float column = fmod(frame, columns);
                float rowFromTop = floor(frame / columns);
                float2 frameSize = float2(1.0 / columns, 1.0 / rows);
                float2 frameOrigin = float2(column * frameSize.x,
                    (rows - 1.0 - rowFromTop) * frameSize.y);
                float2 uv = frameOrigin + input.texcoord * frameSize;
                float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
                float2 frameMin = frameOrigin + halfTexel;
                float2 frameMax = frameOrigin + frameSize - halfTexel;
                uv = clamp(uv, frameMin, frameMax);

                fixed4 color = tex2D(_MainTex, uv) * input.color;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
