Shader "UI/CatCafe Start Glow Breathing"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BreathSpeed ("Breath Speed", Range(0.05, 2.0)) = 0.55
        _BreathStrength ("Breath Strength", Range(0.0, 0.35)) = 0.10
        _DistortionAmount ("Distortion Amount", Range(0.0, 0.01)) = 0.0015
        _DistortionSpeed ("Distortion Speed", Range(0.05, 2.0)) = 0.35
        _DistortionFrequency ("Distortion Frequency", Range(1.0, 20.0)) = 7.0
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
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _BreathSpeed;
            float _BreathStrength;
            float _DistortionAmount;
            float _DistortionSpeed;
            float _DistortionFrequency;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float time = _Time.y;
                float2 uv = input.texcoord;
                float2 centeredUv = uv - 0.5;
                float phase = time * _DistortionSpeed;

                // Two incommensurate waves keep the movement soft instead of looking tiled.
                float waveX = sin(centeredUv.y * _DistortionFrequency + phase);
                float waveY = cos(centeredUv.x * (_DistortionFrequency * 0.83) - phase * 1.17);
                float2 offset = float2(waveX, waveY) * _DistortionAmount;
                fixed4 sample = tex2D(_MainTex, saturate(uv + offset));

                float breath = 1.0 + sin(time * _BreathSpeed) * _BreathStrength;
                sample.rgb *= breath;
                sample.a *= breath;
                sample *= input.color;
                return sample;
            }
            ENDCG
        }
    }
}
