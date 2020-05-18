Shader "Unlit/Shadowmap"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _startPlane("_startPlane", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode" = "VxShadowmap"}
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma multi_compile _ZCLIP
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                //float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float z : TEXCOORD2;
                float depth : TEXCOORD3;
            };

            uniform float _startPlane;
            // float4x4 _LitViewMatrix;
            // uniform float4x4 _LitProjMatrixGPU;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float4 localPos = mul(UNITY_MATRIX_V, mul(UNITY_MATRIX_M, v.vertex));
                 o.z = abs(localPos.z / localPos.w);
                 
                // float4 clipPos = mul(_LitProjMatrixGPU, mul(_LitViewMatrix, mul(UNITY_MATRIX_M, v.vertex)));
                o.depth = o.vertex.z / o.vertex.w; // clipPos.z / clipPos.w;
               
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // #ifdef _ZCLIP
                // float z = i.z;
                // if(z < _startPlane){
                //     i.depth = 0;
                // }
                // #endif
                return EncodeFloatRGBA(i.depth);
            }
            ENDCG
        }
    }
}
