Shader "POTCO/Reference Sky Layer"
{
    Properties
    {
        _BaseTex ("Base Texture", 2D) = "white" {}
        _BlendTex ("Blend Texture", 2D) = "white" {}
        _OverlayTex ("Overlay Texture", 2D) = "white" {}
        _AlphaTex ("Alpha Mask", 2D) = "white" {}
        _BaseBlendColor ("Base Blend Color", Color) = (0, 0, 0, 0)
        _OverlayBlendColor ("Overlay Blend Color", Color) = (0, 0, 0, 0)
        _Color ("Color Scale", Color) = (1, 1, 1, 1)
        _UvScrollA ("Base UV Scroll", Vector) = (0, 0, 0, 0)
        _UvScrollB ("Blend UV Scroll", Vector) = (0, 0, 0, 0)
        _UseAlphaTex ("Use Alpha Mask", Float) = 0
        _AlphaChannel ("Alpha Mask Channel", Float) = 0
        _MoonPhase ("Moon Phase", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseTex;
            sampler2D _BlendTex;
            sampler2D _OverlayTex;
            sampler2D _AlphaTex;
            float4 _BaseTex_ST;
            float4 _BlendTex_ST;
            float4 _OverlayTex_ST;
            fixed4 _BaseBlendColor;
            fixed4 _OverlayBlendColor;
            fixed4 _Color;
            float4 _UvScrollA;
            float4 _UvScrollB;
            float _UseAlphaTex;
            float _AlphaChannel;
            float _MoonPhase;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uvBase : TEXCOORD0;
                float2 uvBlend : TEXCOORD1;
                float2 uvOverlay : TEXCOORD2;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvBase = TRANSFORM_TEX(v.uv, _BaseTex) + _UvScrollA.xy;
                o.uvBlend = TRANSFORM_TEX(v.uv, _BlendTex) + _UvScrollB.xy;
                o.uvOverlay = TRANSFORM_TEX(v.uv, _OverlayTex);
                o.color = v.color;
                return o;
            }

            fixed4 InterpolateByColor(fixed4 previous, fixed4 incoming, fixed4 weight)
            {
                fixed4 result;
                result.rgb = lerp(previous.rgb, incoming.rgb, saturate(weight.rgb));
                result.a = lerp(previous.a, incoming.a, saturate(weight.a));
                return result;
            }

            fixed ReadAlphaChannel(fixed4 alpha)
            {
                if (_AlphaChannel > 2.5) return alpha.a;
                if (_AlphaChannel > 1.5) return alpha.b;
                if (_AlphaChannel > 0.5) return alpha.g;
                return alpha.r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseColor = tex2D(_BaseTex, i.uvBase);
                fixed4 blendColor = tex2D(_BlendTex, i.uvBlend);
                fixed4 overlayColor = tex2D(_OverlayTex, i.uvOverlay);

                fixed4 result = InterpolateByColor(baseColor, blendColor, _BaseBlendColor);
                result = InterpolateByColor(result, overlayColor, _OverlayBlendColor);
                result *= _Color * i.color;

                if (_UseAlphaTex > 0.5)
                {
                    fixed4 alphaSample = tex2D(_AlphaTex, float2(i.uvBase.x, 1.0 - i.uvBase.y));
                    result.a *= ReadAlphaChannel(alphaSample);
                }

                return result;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Transparent"
}
