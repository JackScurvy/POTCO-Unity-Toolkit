Shader "POTCO/WakeShader"
{
    Properties
    {
        _Color ("Tint Color", Color) = (1,1,1,1)
        _MainTex ("Wake Texture", 2D) = "white" {}
        _AlphaTex ("Alpha Mask (Optional)", 2D) = "white" {}
        _WakeU ("Wake U Offset", Float) = 0
        _Alpha ("Alpha Multiplier", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+60" "IgnoreProjector"="True" "RenderType"="Transparent" }
        LOD 100
        ZWrite Off
        ZTest [_ZTest]
        Offset [_OffsetFactor], [_OffsetUnits]
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

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _AlphaTex;
            float _WakeU;
            float _Alpha;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv.x += _WakeU;
                o.color = v.color; // Pass vertex color
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample main texture (RGB)
                fixed4 col = tex2D(_MainTex, i.uv);
                
                // Sample alpha texture (R channel as alpha)
                fixed4 alphaSample = tex2D(_AlphaTex, i.uv);
                col.a = alphaSample.r; 

                // Apply Tint Color
                col *= _Color;

                // Multiply by other factors
                col.a *= i.color.a * _Alpha;
                col.rgb *= i.color.rgb;

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
