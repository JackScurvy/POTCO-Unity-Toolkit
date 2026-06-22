Shader "EggImporter/SailWithLogo"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlendTex ("Sail Color Texture", 2D) = "white" {}
        _LogoTex ("Logo Texture", 2D) = "white" {}
        _LogoAlpha ("Logo Alpha Mask", 2D) = "white" {}
        _AlphaTex ("Alpha Mask (optional)", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        LOD 200
        Cull [_Cull]

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BlendTex;
            sampler2D _AlphaTex;
            fixed4 _Color;
            float _Cutoff;

            struct v2fShadow
            {
                V2F_SHADOW_CASTER;
                float2 uvMain : TEXCOORD1;
                float2 uvBlend : TEXCOORD2;
                fixed4 color : COLOR;
            };

            v2fShadow vertShadow(appdata_full v)
            {
                v2fShadow o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                o.uvMain = v.texcoord.xy;
                o.uvBlend = v.texcoord1.xy;
                o.color = v.color;
                return o;
            }

            float4 fragShadow(v2fShadow i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uvMain);
                fixed4 blendColor = tex2D(_BlendTex, i.uvBlend);

                if (blendColor.r < 0.99 || blendColor.g < 0.99 || blendColor.b < 0.99)
                {
                    texColor *= blendColor;
                }

                fixed alpha = (texColor * i.color * _Color).a;
                fixed4 alphaTexColor = tex2D(_AlphaTex, float2(i.uvMain.x, 1.0 - i.uvMain.y));

                if (alphaTexColor.r < 0.99 || alphaTexColor.g < 0.99 || alphaTexColor.b < 0.99)
                {
                    alpha *= alphaTexColor.r * 0.5;
                }

                clip(alpha - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }

        CGPROGRAM
        #pragma surface surf BrightLambert vertex:vert alphatest:_Cutoff
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _BlendTex;
        sampler2D _LogoTex;
        sampler2D _LogoAlpha;
        sampler2D _AlphaTex;
        fixed4 _Color;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv2_BlendTex;
            fixed4 color : COLOR;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.uv_MainTex = v.texcoord.xy;
            o.uv2_BlendTex = v.texcoord1.xy;
            o.color = v.color;
        }

        // Custom lighting model with ambient lighting so models aren't pitch black
        half4 LightingBrightLambert(SurfaceOutput s, half3 lightDir, half atten)
        {
            half NdotL = dot(s.Normal, lightDir);
            half diff = NdotL * 0.3 + 0.3; // Wrap lighting for brightness
            half4 c;
            // Add ambient lighting (5% minimum brightness) plus directional lighting
            half3 ambient = s.Albedo * 0.05; // 5% ambient light
            half3 directional = s.Albedo * _LightColor0.rgb * (diff * atten);
            c.rgb = ambient + directional;
            c.a = s.Alpha;
            return c;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // Sample main texture
            fixed4 texColor = tex2D(_MainTex, IN.uv_MainTex);

            // Sample sail color texture with UV2
            fixed4 blendColor = tex2D(_BlendTex, IN.uv2_BlendTex);

            // If blend texture is assigned (not white), multiply it with base texture
            if (blendColor.r < 0.99 || blendColor.g < 0.99 || blendColor.b < 0.99)
            {
                texColor *= blendColor;
            }

            // Sample logo texture
            fixed4 logoColor = tex2D(_LogoTex, IN.uv2_BlendTex);

            // Sample logo alpha mask with flipped Y coordinate (white = visible, black = transparent)
            fixed4 logoAlphaColor = tex2D(_LogoAlpha, float2(IN.uv2_BlendTex.x, 1.0 - IN.uv2_BlendTex.y));

            // Use red channel for alpha (white = 1.0 = show logo, black = 0.0 = show base texture)
            fixed logoAlpha = logoAlphaColor.r;

            // Apply logo where alpha mask is white
            texColor.rgb = lerp(texColor.rgb, logoColor.rgb, logoAlpha);

            // Apply vertex colors and material color
            fixed4 finalColor = texColor * IN.color * _Color;

            o.Albedo = finalColor.rgb;

            // Check if alpha texture is assigned (not white)
            fixed4 alphaTexColor = tex2D(_AlphaTex, float2(IN.uv_MainTex.x, 1.0 - IN.uv_MainTex.y));

            // If alpha texture is essentially white (1,1,1), use regular alpha
            // Otherwise use the alpha mask
            if (alphaTexColor.r < 0.99 || alphaTexColor.g < 0.99 || alphaTexColor.b < 0.99)
            {
                // Alpha mask is assigned - use it with binary cutoff
                fixed aMask = alphaTexColor.r * 0.5;
                o.Alpha = aMask * finalColor.a;
            }
            else
            {
                // No alpha mask - use regular alpha
                o.Alpha = finalColor.a;
            }
        }
        ENDCG
    }

    Fallback "Legacy Shaders/VertexLit"
}
