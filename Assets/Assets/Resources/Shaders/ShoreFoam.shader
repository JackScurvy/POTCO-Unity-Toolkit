Shader "POTCO/ShoreFoam"
{
    Properties
    {
        _MainTex ("Foam Texture", 2D) = "white" {}
        _AlphaTex ("Alpha Mask (Optional)", 2D) = "white" {}
        _FoamU ("Foam U Offset", Float) = 0
        _FoamV ("Foam V Offset", Float) = 0
        _Alpha ("Alpha Multiplier", Range(0, 1)) = 1
        _Color ("Tint Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _AlphaTex;
            float _FoamU;
            float _FoamV;
            float _Alpha;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                // Apply Foam offsets
                o.uv.x += _FoamU; 
                o.uv.y += _FoamV;
                
                o.color = v.color;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample texture
                fixed4 col = tex2D(_MainTex, i.uv);
                
                // Sample alpha mask
                fixed4 alphaSample = tex2D(_AlphaTex, i.uv);
                
                // Apply tint and vertex colors
                col *= _Color * i.color;
                
                // Combine alphas: MainTex alpha * AlphaTex red channel * Global Alpha * Vertex Alpha
                col.a *= alphaSample.r * _Alpha;

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
