Shader "POTCO/GuiMeshUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlendTex ("Blend Texture", 2D) = "white" {}
        _AlphaTex ("Alpha Mask", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10
        _UseAlphaTex ("Use Alpha Mask", Float) = 0
        _AlphaChannel ("Alpha Mask Channel", Float) = 0
        _SwapUVChannels ("Swap UV Channels", Float) = 0
        _MainTexWrap ("Main Tex Wrap", Vector) = (0,0,0,0)
        _BlendTexWrap ("Blend Tex Wrap", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }

        Cull [_Cull]
        Lighting Off
        ZWrite [_ZWrite]
        ZTest Always
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BlendTex;
            sampler2D _AlphaTex;
            fixed4 _Color;
            float _UseAlphaTex;
            float _AlphaChannel;
            float _SwapUVChannels;
            float4 _MainTexWrap;
            float4 _BlendTexWrap;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uvMain : TEXCOORD0;
                float2 uvBlend : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uvMain = input.texcoord;
                output.uvBlend = input.texcoord1;
                output.color = input.color * _Color;
                return output;
            }

            fixed SelectAlphaChannel(fixed4 alpha)
            {
                if (_AlphaChannel > 2.5) return alpha.a;
                if (_AlphaChannel > 1.5) return alpha.b;
                if (_AlphaChannel > 0.5) return alpha.g;
                return alpha.r;
            }

            float2 ApplyWrapMode(float2 uv, float2 wrapMode)
            {
                float2 result = uv;
                if (wrapMode.x > 0.5) result.x = saturate(result.x);
                if (wrapMode.y > 0.5) result.y = saturate(result.y);
                return result;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 mainUv = _SwapUVChannels > 0.5 ? input.uvBlend : input.uvMain;
                float2 blendUv = _SwapUVChannels > 0.5 ? input.uvMain : input.uvBlend;
                mainUv = ApplyWrapMode(mainUv, _MainTexWrap.xy);
                blendUv = ApplyWrapMode(blendUv, _BlendTexWrap.xy);

                fixed4 color = tex2D(_MainTex, mainUv);
                fixed4 blend = tex2D(_BlendTex, blendUv);

                if (blend.r < 0.99 || blend.g < 0.99 || blend.b < 0.99)
                    color *= blend;

                color *= input.color;

                if (_UseAlphaTex > 0.5)
                {
                    fixed4 alpha = tex2D(_AlphaTex, float2(mainUv.x, 1.0 - mainUv.y));
                    color.a *= SelectAlphaChannel(alpha);
                }

                return color;
            }
            ENDCG
        }
    }
}
