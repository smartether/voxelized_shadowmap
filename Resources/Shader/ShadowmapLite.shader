Shader "Unlit/ShadowmapLite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                float2 offset1 = float2(0.01, 0);
                fixed4 col = tex2D(_MainTex, i.uv);
                fixed4 col1 = tex2D(_MainTex, i.uv + offset1.xy);
                fixed4 col2 = tex2D(_MainTex, i.uv - offset1.xy);
                fixed4 col3 = tex2D(_MainTex, i.uv + offset1.yx);
                fixed4 col4 = tex2D(_MainTex, i.uv - offset1.yx);
                float depth = DecodeFloatRGBA(col);
                float depth1 = DecodeFloatRGBA(col1);
                float depth2 = DecodeFloatRGBA(col2);
                float depth3 = DecodeFloatRGBA(col3);
                float depth4 = DecodeFloatRGBA(col4);
                if(4*depth - depth1 - depth2 - depth3 - depth4 > 0.05){
                    return 1;
                }
                else{
                    return 0;
                }

                return col;
            }
            ENDCG
        }
    }
}
