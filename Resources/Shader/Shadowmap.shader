Shader "Unlit/Shadowmap"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _startPlane("_startPlane", Float) = 0
        _frontDepth("_frontDepth", Float) = 1
        _backDepth("_backDepth", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode" = "VxShadowmap"}
        LOD 100

        Pass
        {
            Name "FORWARD"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma multi_compile __ _ZCLIP
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
            uniform float _frontDepth;
            uniform float _backDepth;
            uniform float _VoxelDepth; // 1~32 1~64 1~128
            // float4x4 _LitViewMatrix;
            // uniform float4x4 _LitProjMatrixGPU;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float4 localPos = mul(UNITY_MATRIX_V, mul(UNITY_MATRIX_M, v.vertex));
                o.z = abs(localPos.z / localPos.w);
                // float4 clipPos = mul(_LitProjMatrixGPU, mul(_LitViewMatrix, mul(UNITY_MATRIX_M, v.vertex)));
                // solove different coord 
                #ifdef SHADER_API_D3D11
                    o.depth = o.vertex.z / o.vertex.w;
                #else
                    o.depth = o.vertex.z / o.vertex.w * 0.5 + 0.5; // clipPos.z / clipPos.w;
                #endif
                
                o.depth = lerp(_backDepth, _frontDepth, o.depth);
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
                // return 1 - i.depth;
                float depth = 0;
                float4 rgba = (float4)0;
                
                #ifdef SHADER_API_D3D11
                        depth = i.depth;
                #else
                        depth = 1- i.depth;
                #endif
                
                rgba = EncodeFloatRGBA(depth);

                return rgba;
                
            }
            ENDCG
        }

        
        /*
        Pass
        {
            Name "ShadowCaster"
            
            Blend One Zero
            ZWrite On

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
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float z : TEXCOORD2;
                float depth : TEXCOORD3;
            };

            uniform float _startPlane;
            uniform float _frontDepth;
            uniform float _backDepth;
            // float4x4 _LitViewMatrix;
            // uniform float4x4 _LitProjMatrixGPU;
                sampler2D _MainTex;
                float4 _MainTex_ST;
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float4 localPos = mul(UNITY_MATRIX_V, mul(UNITY_MATRIX_M, v.vertex));
                o.z = abs(localPos.z / localPos.w);
                o.uv = v.uv;
                // float4 clipPos = mul(_LitProjMatrixGPU, mul(_LitViewMatrix, mul(UNITY_MATRIX_M, v.vertex)));
                // solove different coord 
                #ifdef SHADER_API_D3D11
                    o.depth = o.vertex.z / o.vertex.w;
                #else
                    o.depth = o.vertex.z / o.vertex.w * 0.5 + 0.5; // clipPos.z / clipPos.w;
                #endif
                
                o.depth = lerp(_backDepth, _frontDepth, o.depth);
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
                // return 1 - i.depth;
                clip(tex2D(_MainTex, TRANSFORM_TEX(i.uv, _MainTex)).a  - 0.3333);
                #ifdef SHADER_API_D3D11
                    return EncodeFloatRGBA(i.depth);
                #else
                    return EncodeFloatRGBA(1- i.depth);
                #endif

                
            }
            ENDCG
        }
        */
        
    }
}
