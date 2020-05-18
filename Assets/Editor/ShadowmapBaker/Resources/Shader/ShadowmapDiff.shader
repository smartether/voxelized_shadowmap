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
                float frontDepth : TEXCOORD1;
                float backDepth : TEXCOORD2;
            };

            // front of voxelbox whith is near camera
            uniform float _miniPlane;
            // back of voxelbox 
            uniform float _maxPlane;
            // final global shadowmap
            uniform sampler2D _shadowMap;
            // uniform float4x4 _UNITY_MATRIX_P;
            // uniform float4x4 _UNITY_MATRIX_V;
            // uniform float4x4 _UNITY_MATRIX_M;
            // uniform float4x4 _viewMatrix;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = o.vertex.xy / o.vertex.w * 0.5 + 0.5; //v.uv;
                o.uv.y = 1 - o.uv.y;
                //float4 clipPosMax = mul(_UNITY_MATRIX_P, float4(0,0, -_maxPlane,1));
                //float4 clipPosMini = mul(_UNITY_MATRIX_P, float4(0,0, -_miniPlane,1));
                o.frontDepth =  _maxPlane; //clipPosMax.z / clipPosMax.w * 0.5 + 0.5; //
                o.backDepth =  _miniPlane; //clipPosMini.z / clipPosMini.w * 0.5 + 0.5; // 
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //return float4(i.depth, 0,0,1);
                
                float4 depthRGBA =  tex2D(_shadowMap, i.uv);
                // return depthRGBA;
                float depth = DecodeFloatRGBA(depthRGBA) ;
                // return i.depth - depth;
                // return depth;
                float diff = 0; //saturate((depth - i.depth) * 1000); //EncodeFloatRGBA(depth - i.depth); 
                if(i.frontDepth < depth){
                    diff = 0;
                }
                else if(i.backDepth > depth)
                {
                    diff = 1;
                }
                else if(depth >= i.backDepth && depth <= i.frontDepth)
                {
                    diff = 0.5;
                }
                // 0 if pixel is shadowed, 1 if pixel is lit
                return diff.xxxx;
            }
            ENDCG
        }
    }
}
