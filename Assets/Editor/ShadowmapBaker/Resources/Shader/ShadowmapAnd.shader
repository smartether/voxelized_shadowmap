Shader "Unlit/ShadowmapDiff"
{
    Properties
    {
        _startPlane("_startPlane", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode" = "VxShadowmap"}
        LOD 100
        
        Blend One Zero

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

            uniform float _voxelSize;
            // final global shadowmap
            uniform sampler2D _litShadowMap;
            uniform float4x4 _UNITY_MATRIX_P;
            uniform float4x4 _UNITY_MATRIX_V;
            uniform float4x4 _UNITY_MATRIX_M;
            // uniform float4x4 _viewMatrix;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //return float4(i.depth, 0,0,1);
                
                float4 isLitOrShadowed =  tex2D(_litShadowMap, i.uv);

                return fixed(1).xxxx;
            }
            ENDCG
        }
    }
}
