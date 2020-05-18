Shader "Unlit/VxRender64_64"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        _VoxelParams("VoxelParams", Vector) = (37.5, 0.0266, 16, 0.0625) // root voxel lv1 x: voxel world size (37.5) y: 1/x  z:  16*16*16 root voxel  w:voxel clipSpace size
        _VoxelParamsLv2("VoxelParamsLv2", Vector) = (18.75, 0.0533, 32, 0.03125) // root voxel lv1 x: voxel world size (37.5) y: 1/x  z:  16*16*16 root voxel  w:voxel clipSpace size
        _VoxelParamsLv3("VoxelParamsLv3", Vector) = (9.375, 0.1066, 64, 0.01562) // root voxel lv1 x: voxel world size (37.5) y: 1/x  z:  16*16*16 root voxel  w:voxel clipSpace size
        _ProjSizeParams("ProjSizeParams", Vector) = (300, 0.00333, 600, 0.00166)    // x: orthoSize y: 1/orthoSize z:2 * orthoSize w: 1 / (2 * orthoSize)
        _Level1IndexMap("Level1IndexMap", 2D) = "black" {}
        _Level1IndexMapNoArray("Level1IndexMapNoArray", 2D) = "black" {}
        _Level2LitShadowInfoArray("Level2LitShadowInfoArray", 2DArray) = "black"{}
        _Level2LitShadowInfo("Level2LitShadowInfo", 2D) = "black" {}
        _VoxelShadowmap("VoxelShadowmap", 2D) = "black" {}
        _Shadowmap("Shadowmap", 2D) = "black" {}

        _ShadowAlpha("ShadowAlpha", Range(0,1)) = 0.2
        
        // Debug
        _Level1LitShadowInfoArrayDebug("_Level1LitShadowInfoArrayDebug", 2DArray) = "black"{}
        _Level2LitShadowInfoArrayDebug("_Level2LitShadowInfoArrayDebug", 2DArray) = "black"{}
        _Level3LitShadowInfoArrayDebug("_Level3LitShadowInfoArrayDebug", 2DArray) = "black"{}

        _DEBUG_FACT("DEBUG_FACT", Float) = 1
        [Toggle]_MODE_GPUMATRIX("_MODE_GPUMATRIX", int) = 0
    }
    SubShader
    {
        Name "VOXELSHADOW"
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _MODE_GPUMATRIX_ON
            //#pragma fragmentoption ARB_precision_hint_fastest 
            #pragma fragmentoption ARB_precision_hint_nicest

            // #pragma multi_compile_instancing
            // make fog work
            // #pragma multi_compile_fog

            #include "./builtin/CGIncludes/UnityCG.cginc"
            #include "./builtin/CGIncludes/UnityInstancing.cginc"


            #define _MODE_NO_TEX_ARRAY
            #define _MODE_GPUMatrix_On

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float litDistance : TEXCOORD1;
                float4 litSpacePos : TEXCOORD2;
                float4 litSpaceClipPos : TEXCOORD3;
                float depthWithBias : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };


/*
    UNITY_INSTANCING_BUFFER_START(VxShadowMap)
        UNITY_DEFINE_INSTANCED_PROP(float4, _litShadowInfo)
    UNITY_INSTANCING_BUFFER_END(VxShadowMap)
*/
     UNITY_CONST_BUFFER(VxInfo)

    

            // #define UNITY_ACCESS_INSTANCED_PROP_IDX(arr, var)   arr##Array[unity_InstanceID].var

            sampler2D _MainTex;
            float4 _MainTex_ST;

            // x: lv1 voxel size(litSpace) y:lv2 voxel Size
            float4 _VoxelParams;
            float4 _VoxelParamsLv2;
            float4 _VoxelParamsLv3;
            float4 _ProjSizeParams;

            sampler2D _Level1IndexMap;
            sampler2D _Level1IndexMapNoArray;
            sampler2D _Level2LitShadowInfo;
            UNITY_DECLARE_TEX2DARRAY(_Level2LitShadowInfoArray);
            sampler2D _VoxelShadowmap;
            sampler2D _Shadowmap;

            UNITY_DECLARE_TEX2DARRAY(_Level2LitShadowInfoArrayDebug);
            UNITY_DECLARE_TEX2DARRAY(_Level3LitShadowInfoArrayDebug);
            UNITY_DECLARE_TEX2DARRAY(_Level1LitShadowInfoArrayDebug);

            float4x4 _LitViewMatrix;
            float4x4 _LitProjMatrix;
            float4x4 _LitProjMatrixGPU;
            // float4x4 _LitProjMatrixRT;
            float4x4 _LitViewProjMatrix;

            uniform float _DEBUG_FACT;

            uniform float _ShadowAlpha;

            inline float DecodeFloatRGB( float3 enc )
            {
                float3 kDecodeDot = float3(1.0, 1/255.0, 1/65025.0);
                return dot( enc, kDecodeDot );
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                    o.litSpacePos = mul(_LitViewMatrix, mul(UNITY_MATRIX_M, v.vertex));
                    o.litSpacePos.xyz = o.litSpacePos.xyz / o.litSpacePos.w;
                    o.litDistance = length( o.litSpacePos.xyz);
                    o.litSpacePos.xy += _ProjSizeParams.xx;
                    o.litSpacePos.z = abs(o.litSpacePos.z);
                    float4 viewPos = mul(_LitViewMatrix, mul(UNITY_MATRIX_M, v.vertex));
                    float4 depthPos = mul(_LitProjMatrixGPU, float4(0,0 , 10,0) + viewPos);
                    //float4 litSpacePos = mul(_LitProjMatrix, viewPos);
                    #ifdef _MODE_GPUMATRIX_ON
                        o.litSpaceClipPos = mul(_LitProjMatrixGPU, viewPos);
                        o.litSpaceClipPos.xy = o.litSpaceClipPos.xy / o.litSpaceClipPos.w * 0.5 + 0.5;
                    #else
                        o.litSpaceClipPos = mul(_LitProjMatrix, viewPos);
                        o.litSpaceClipPos.xyz = o.litSpaceClipPos.xyz / o.litSpaceClipPos.w * 0.5 + 0.5;
                    #endif
                    
                    
                    // o.litSpaceClipPos.z = depthPos.z;
                    o.depthWithBias = depthPos.z; 
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                
                UNITY_SETUP_INSTANCE_ID(i)
                //return  float4(i.litSpaceClipPos.xy * 0.5 + 0.5 ,0,1);
                 float4 col = tex2Dlod(_Shadowmap, float4(i.litSpaceClipPos.xy,0,0));
                float decodedDepth = DecodeFloatRGBA(col);
                // return i.litSpaceClipPos.z;
                // return decodedDepth;
                float depth = i.depthWithBias;// (i.litSpaceClipPos.z * 0.5) + 0.5;
                // //return depth;
                //return saturate((depth - decodedDepth) * 100) + 0.2;

                // litSpacePos center = (0,0) leftBottom=(-orthoSize,-orthoSize)
                float3 litSpacePos = i.litSpacePos;
                // voxelPos leftBottom=(0,0)
                float3 orthoPos = litSpacePos;

                float3 voxelPos = 0;
                voxelPos = orthoPos.xyz * _VoxelParams.y;
                voxelPos = floor(voxelPos);

                
                float3 voxelPosLv2 = 0;
                voxelPosLv2 = orthoPos.xyz * _VoxelParamsLv2.y;
                //voxelPosLv2 = round(voxelPosLv2 / 2);
                float3 voxelUnionPosLv2 = 0;
                voxelUnionPosLv2 = floor(voxelPosLv2 % 2.0);

                float3 voxelPosLv3 = 0;
                voxelPosLv3 = orthoPos.xyz * _VoxelParamsLv3.y;
                //voxelPosLv3 = floor(voxelPosLv3 / 2);
                float3 voxelUnionPosLv3 = 0;
                voxelUnionPosLv3 = floor(voxelPosLv3 % 2.0 );
                
                ///////// screen space voxel pos
                float3 litSpaceVoxelPos = i.litSpaceClipPos.xyz;
                // reverse z  depth == 1 near camera
                litSpaceVoxelPos.z = (1.0 - litSpaceVoxelPos.z);
                voxelPos = floor(litSpaceVoxelPos / _VoxelParams.w);
                
                voxelPosLv2 = floor(litSpaceVoxelPos / _VoxelParamsLv2.w);
                voxelUnionPosLv2 = floor(voxelPosLv2 % 2.0);
                //return float4(voxelUnionPosLv2, 1);
                voxelPosLv3 = floor(litSpaceVoxelPos / _VoxelParamsLv3.w);
                voxelUnionPosLv3 = floor(voxelPosLv3 % 2.0);
                //return float4(voxelUnionPosLv3, 1);

                // // return float4(voxelPos.x / 16, voxelPos.y / 16, voxelPos.z / 16, 1);
                // if(voxelPosLv3.z > 60){
                //     return 1;
                // }
                // else{
                //     return 0;
                // }
                float Level2IndexD = voxelPos.z;

                // TODO uniform param
                float level1TexSize = 64.0;// round(sqrt(_VoxelParams.z * _VoxelParams.z * _VoxelParams.z));
                float texPixelIndex = floor(_VoxelParams.z * _VoxelParams.z * voxelPos.z + voxelPos.y * _VoxelParams.z + voxelPos.x);
                float2 Level1IndexMapUV = saturate(float2(floor(texPixelIndex % level1TexSize) / level1TexSize, floor(texPixelIndex / level1TexSize) / (float)level1TexSize));
               
                
                //return float4(Level1IndexMapUV.x, 0, Level1IndexMapUV.y,1);
                float4 level1LitInfo = tex2Dlod(_Level1IndexMap, float4(Level1IndexMapUV, 0,0));
                float4 level1LitInfoNoArray = tex2Dlod(_Level1IndexMapNoArray, float4(Level1IndexMapUV, 0,0));
                float fact = 1.0 / 1024.0;
                float lv2MapWidth = 32.0;
                float lv2U_fact = 1.0 / lv2MapWidth;
                float v = DecodeFloatRG(level1LitInfo.zw);
                float v1 = DecodeFloatRG(level1LitInfoNoArray.zw);


                float lv1 = UNITY_SAMPLE_TEX2DARRAY_LOD(_Level1LitShadowInfoArrayDebug, float3(voxelPos.xy    / _VoxelParams.z, floor(voxelPos.z)), 0);
                float lv2 = UNITY_SAMPLE_TEX2DARRAY_LOD(_Level2LitShadowInfoArrayDebug, float3(voxelPosLv2.xy / _VoxelParamsLv2.z, floor(voxelPosLv2.z)), 0);
                float lv3 = UNITY_SAMPLE_TEX2DARRAY_LOD(_Level3LitShadowInfoArrayDebug, float3(voxelPosLv3.xy / _VoxelParamsLv3.z, floor(voxelPosLv3.z)), 0);
                
                // #define _DEBUG;

                float shadowAlpha = _ShadowAlpha * saturate(1 + cos(3.1415 * i.litDistance / _ProjSizeParams.z));
                if(abs(lv1 - 0.5) > 0.2){
                    #ifdef _DEBUG
                    return fixed4(0,1,0,1);
                    #endif
                    return lv1 + (1 - lv1) * shadowAlpha;
                }
                if(abs(lv2 - 0.5) > 0.2)
                {
                    #ifdef _DEBUG
                    return fixed4(0,0,1,1);
                    #endif
                    return lv2 + (1 - lv2) * shadowAlpha;
                }
                if(abs(lv3 - 0.5) < 0.4){
                    #ifdef _DEBUG
                    return fixed4(1,0,0,1);
                    #endif
                    return saturate(saturate((i.depthWithBias - decodedDepth) * 50) + shadowAlpha);
                }
                #ifdef _DEBUG
                    return fixed4(0,1,1,1);
                #endif
                return lv3 + (1 - lv3) * shadowAlpha;


                if(abs(level1LitInfo.r - 0) < 0.1 || abs(level1LitInfo.r - 1) < 0.1 ){
                     return level1LitInfo.r * 0.8;
                }
                else if(abs(level1LitInfo.r - 0.5) < 0.1)
                {
                    //return saturate((i.depthWithBias - decodedDepth) * 100) * 0.6 ;
                    //return saturate((i.depthWithBias - decodedDepth) * 100) * 0.6 ;
                    // return (32 - floor(level1LitInfo.g * 32.0))/ 32.0;
                    // return UNITY_SAMPLE_TEX2DARRAY_LOD(_Level2LitShadowInfoArray, float3(0,0, 1), 0) ;
                    //float texV = v ; //DecodeFloatRG(level1LitInfo.gb);
                    float texArrayU = lv2U_fact * floor(voxelUnionPosLv2.z * 4.0 + voxelUnionPosLv2.y * 2.0 + voxelUnionPosLv2.x);


                    float4 arrayValue = UNITY_SAMPLE_TEX2DARRAY_LOD(_Level2LitShadowInfoArray, float3(texArrayU,
                    v,
                    floor(level1LitInfo.g * 32.0)),
                    0);

                    float4 arrayValue1 = tex2Dlod(_Level2LitShadowInfo, float4(texArrayU, (level1LitInfoNoArray.g > 0 ?  0.5 : 0) + v1, 0,0));
                    

                    int idx = floor(voxelUnionPosLv3.y * 2.0 + voxelUnionPosLv3.x);
                    float litOrShadowed = arrayValue[idx];
                    float litOrShadowed1 = arrayValue1[idx];
                   
                    if(abs(litOrShadowed - 0.5) < 0.1){
                        return saturate((i.depthWithBias - decodedDepth) * 100) * 0.6 ;
                    }
                    else
                        return litOrShadowed;


/*
                    //float isHalf = level1LitInfo.a;
                    //texV = texV * 0.5 + isHalf * 0.5;
                    //return floor(voxelUnionPosLv2.z * 4 + voxelUnionPosLv2.y * 2 + voxelUnionPosLv2.x) / 8;
                    
                    float4 lv2Info = tex2Dlod(_Level2LitShadowInfo, float4(lv2U_fact * floor(voxelUnionPosLv2.z * 4 + voxelUnionPosLv2.y * 2 + voxelUnionPosLv2.x),  texV, 0, 0));
                    int i = round(clamp(voxelUnionPosLv3.y * 2 + voxelUnionPosLv3.x, 0, 4));
                    
                    float litOrShadowed = lv2Info[(int)(floor(voxelUnionPosLv3.y * 2 + voxelUnionPosLv3.x) )];
                    */
                    return litOrShadowed ;
                    return fixed4(1,  abs(litOrShadowed - 0.5) < 0.1 , litOrShadowed, 1);
                    
                }
                else{
                    return 1;
                }


                // return level1LitInfo.xxxx;
                // return fixed4(voxelPos.x * _VoxelParams.w, 0 ,voxelPos.y * _VoxelParams.w,1);
                // return fixed4(abs(orthoPos * _ProjSizeParams.ww).x, 0, 0,1);
                // return fixed(1).xxxx;


                //float4 litInfo = UNITY_CONST_BUFFER_PROP(VxInfo, 1); //UNITY_ACCESS_INSTANCED_PROPIDX(VxShadowMap, _litShadowInfo,   i.screenPosX > 0 ? 1 : 0);  // UNITY_CONST_BUFFER_PROP(VxInfo, 1);
                //return litInfo;
            }
            ENDCG
        }
    }
}
