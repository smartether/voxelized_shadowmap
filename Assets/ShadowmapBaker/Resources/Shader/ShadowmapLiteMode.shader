Shader "Unlit/ShadowmapLiteMode"
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

            //Blend One Zero
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma multi_compile __ _ZCLIP
            #pragma multi_compile __ _ShadowmapSplit
            #pragma multi_compile __ _ShadowmapEncode
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

            // high precision mode
            // #define _SHADOWMAP_LITE1

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
                /*
                rgba.rg = EncodeFloatRG(saturate(floor((1.0- depth) * _VoxelDepth) / _VoxelDepth));
                rgba.b = (saturate(((1.0 - depth) * _VoxelDepth) % 1.0)); // near:1 far:0
                */
                #ifdef _SHADOWMAP_LITE1
                float _VoxelDepthLv3 = _VoxelDepth;
                _VoxelDepth = _VoxelDepth / 4.0;
                #endif

                float voxelDepth = saturate(floor((1.0- depth) * _VoxelDepth)  / _VoxelDepth);
                float voxelDepthLv2 = saturate(floor(((1.0- depth) * _VoxelDepth * 2) % 2.0) / 2.0);
                float voxelDepthLv3 = saturate(floor(((1.0- depth) * _VoxelDepth * 2 * 2) % 2.0) / 2.0);

                #ifndef _SHADOWMAP_LITE1
                    float voxelScopeDepth = saturate(((1.0 - depth) * _VoxelDepth) % 1.0);
                #else
                    float voxelScopeDepth = saturate(((1.0 - depth) * _VoxelDepthLv3) % 1.0);
                #endif

                #ifdef _ShadowmapSplit
                #   ifdef _ShadowmapEncode
                       rgba.rg = EncodeFloatRG(voxelDepth);
                    //    rgba.b = voxelDepth;
                       rgba.b = voxelDepthLv2;
                #   else
                       rgba.rgb = (voxelDepth);
                #   endif
                #else
                #   ifdef _ShadowmapEncode
                       rgba.rg = EncodeFloatRG(voxelScopeDepth); // near:1 far:0
                       rgba.b = voxelDepthLv3;
                #   else
                       rgba.rgb = (voxelScopeDepth); // near:1 far:0
                #   endif
                #endif

                rgba.a = 0;
                //rgba.a = saturate(((1.0 - depth) * _VoxelDepth) % 1.0);
                return rgba;
                
            }
            ENDCG
        }

        
        
    }
}
