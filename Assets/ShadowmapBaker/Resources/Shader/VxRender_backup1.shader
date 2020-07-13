Shader "Unlit/VxRender_backup1"
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
        _ShadowBias("shadowBias", Range(-500, 500)) = 20
        _ShadowBias1("shadowBias1", Range(0,1)) = 0.1
        
        _level1TexSize("level1TexSize", float) = 64
        _level2TexArrayDepth("level2TexArrayDepth", float) = 32
        // Debug
        // _Level1LitShadowInfoArrayDebug("_Level1LitShadowInfoArrayDebug", 2DArray) = "black"{}
        // _Level2LitShadowInfoArrayDebug("_Level2LitShadowInfoArrayDebug", 2DArray) = "black"{}
        // _Level3LitShadowInfoArrayDebug("_Level3LitShadowInfoArrayDebug", 2DArray) = "black"{}

        _DEBUG_FACT("DEBUG_FACT", Float) = 1
        [Toggle]_MODE_GPUMATRIX("_MODE_GPUMATRIX", int) = 0
        [Toggle]_ENABLE_PREDICT("_ENABLE_PREDICT", int) = 0
        [Toggle]_LIT_SCREEN_SPACE_MODE("_LIT_SCREEN_SPACE_MODE", int) = 0
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
            #pragma shader_feature _MODE_GPUMATRIX_ON 
            #pragma shader_feature _ENABLE_PREDICT_ON
            #pragma shader_feature _LIT_SCREEN_SPACE_MODE_ON
            #pragma fragmentoption ARB_precision_hint_fastest 
            //#pragma fragmentoption ARB_precision_hint_nicest

            // #pragma multi_compile_instancing
            // make fog work
            // #pragma multi_compile_fog

            #include "./builtin/CGIncludes/UnityCG.cginc"
            #include "./builtin/CGIncludes/UnityInstancing.cginc"


            #define _MODE_NO_TEX_ARRAY
            // #define _MODE_GPUMatrix_On

            #ifdef _LIT_SCREEN_SPACE_MODE_ON
            #define _LIT_SCREEN_SPACE_MODE
            #else
            #undef _LIT_SCREEN_SPACE_MODE
            #endif

           // #define _ENABLE_VOXEL_PREDICT
            #ifdef _ENABLE_PREDICT_ON
            #define _ENABLE_VOXEL_PREDICT
            #else
            #undef _ENABLE_VOXEL_PREDICT
            #endif

            #define _SHADOWMAP_LITE

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
                #ifndef _LIT_SCREEN_SPACE_MODE
                float4 litSpacePos : TEXCOORD2;
                #endif
                #ifdef _LIT_SCREEN_SPACE_MODE
                float4 litSpaceClipPos : TEXCOORD3;
                #endif
                float depthWithBias : TEXCOORD5;
                #ifdef _ENABLE_VOXEL_PREDICT
                float3 predictedLv1VoxelIdx : TEXCOORD6;
                float3 predictedLv2VoxelIdx : TEXCOORD7;
                float3 predictedLv3VoxelIdx : TEXCOORD8;
                #endif
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

            // UNITY_DECLARE_TEX2DARRAY(_Level2LitShadowInfoArrayDebug);
            // UNITY_DECLARE_TEX2DARRAY(_Level3LitShadowInfoArrayDebug);
            // UNITY_DECLARE_TEX2DARRAY(_Level1LitShadowInfoArrayDebug);

            float4x4 _LitViewMatrix;
            float4x4 _LitProjMatrix;
            float4x4 _LitProjMatrixGPU;
            // float4x4 _LitProjMatrixRT;
            float4x4 _LitViewProjMatrix;

            uniform float _DEBUG_FACT;

            uniform float _ShadowAlpha;
            uniform float _ShadowBias;
            uniform float _ShadowBias1;
            uniform float _level1TexSize;
            uniform float _level2TexArrayDepth;

            inline float DecodeFloatRGB( float3 enc )
            {
                float3 kDecodeDot = float3(1.0, 1/255.0, 1/65025.0);
                return dot( enc, kDecodeDot );
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                float4 viewPos = mul(_LitViewMatrix, mul(UNITY_MATRIX_M, v.vertex));
                #ifndef _LIT_SCREEN_SPACE_MODE
                o.litSpacePos = viewPos;
                o.litSpacePos.xyz = viewPos.xyz / viewPos.w;
                o.litSpacePos.xy += _ProjSizeParams.xx;
                o.litSpacePos.z = abs(o.litSpacePos.z);
                #endif
                o.litDistance = length(viewPos.xyz / viewPos.w);
                float4 depthPos = mul(_LitProjMatrix, float4(0, 0 , _ShadowBias,0) + viewPos);
                depthPos.xyz = depthPos.xyz / depthPos.w * 0.5 + 0.5;
                o.depthWithBias = 1 - depthPos.z; 
                //float4 litSpacePos = mul(_LitProjMatrix, viewPos);
                #ifdef _LIT_SCREEN_SPACE_MODE
                #ifdef _MODE_GPUMATRIX_ON
                    o.litSpaceClipPos = mul(_LitProjMatrixGPU, viewPos);
                    o.litSpaceClipPos.xy = o.litSpaceClipPos.xy / o.litSpaceClipPos.w * 0.5 + 0.5;
                    o.litSpaceClipPos.z = 1 - o.litSpaceClipPos.z;
                #else
                    o.litSpaceClipPos = mul(_LitProjMatrix, viewPos);
                    o.litSpaceClipPos.xyz = o.litSpaceClipPos.xyz / o.litSpaceClipPos.w * 0.5 + 0.5;
                    o.litSpaceClipPos.z = 1 - o.litSpaceClipPos.z;
                #endif
                #endif
                

                #ifdef _ENABLE_VOXEL_PREDICT

                float3 voxelPos = 0;
                float3 voxelPosLv2 = 0;
                float3 voxelPosLv3 = 0;
                float3 voxelUnionPosLv2 = 0;
                float3 voxelUnionPosLv3 = 0;
                
                
                #ifndef _LIT_SCREEN_SPACE_MODE
                    float3 orthoPos = o.litSpacePos;
                    voxelPos = orthoPos.xyz / _VoxelParams.x;
                    voxelPos = floor(voxelPos);
                    voxelPosLv2 = orthoPos.xyz / _VoxelParamsLv2.x;
                    voxelUnionPosLv2 = floor(voxelPosLv2 % 2);
                    voxelPosLv3 = orthoPos.xyz / _VoxelParamsLv3.x;
                #endif
                
                ///////// screen space voxel pos
                #ifdef _LIT_SCREEN_SPACE_MODE
                    float3 litSpaceClipPos = o.litSpaceClipPos.xyz;
                    // reverse z  depth == 1 near camera
                    litSpaceClipPos.z = (1.0 - litSpaceClipPos.z);
                    voxelPos = floor(litSpaceClipPos * _VoxelParams.z);
                    voxelPosLv2 = floor(litSpaceClipPos * _VoxelParamsLv2.z);
                    voxelPosLv3 = floor(litSpaceClipPos * _VoxelParamsLv3.z);
                #endif    

                o.predictedLv1VoxelIdx = voxelPos;
                o.predictedLv2VoxelIdx = voxelPosLv2;
                o.predictedLv3VoxelIdx = voxelPosLv3;
                #endif

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i)
                // litSpacePos center = (0,0) leftBottom=(-orthoSize,-orthoSize)
                // voxelPos leftBottom=(0,0)
                
                float3 voxelPos = 0;
                float3 voxelPosLv2 = 0;
                float3 voxelPosLv3 = 0;
                float3 voxelUnionPosLv2 = 0;
                float3 voxelUnionPosLv3 = 0;
                

                #ifndef _LIT_SCREEN_SPACE_MODE
                    float3 litSpacePos = i.litSpacePos;
                    float3 orthoPos = litSpacePos;
                    #ifdef _ENABLE_VOXEL_PREDICT
                        // voxelPos = i.predictedLv1VoxelIdx;
                        // voxelPosLv2 = i.predictedLv2VoxelIdx;
                        // voxelPosLv3 = i.predictedLv3VoxelIdx;                    
                        // float mLv1 = length(voxelPos % 1);
                        // float mLv2 = length(voxelPosLv2 % 1);
                        // float mLv3 = length(voxelPosLv3 % 1);
                        // float fullPredict = (mLv1 + mLv2 + mLv3);
                        // if(mLv1 > 0)
                        //     voxelPos = orthoPos.xyz / _VoxelParams.x;
                        // if(mLv2 > 0)
                        //     voxelPosLv2 = orthoPos.xyz / _VoxelParamsLv2.x;
                        // if(mLv3 > 0)
                        //     voxelPosLv3 = orthoPos.xyz / _VoxelParamsLv3.x;
                    #else
                        voxelPos = orthoPos.xyz / _VoxelParams.x;
                        voxelPosLv2 = orthoPos.xyz / _VoxelParamsLv2.x;
                        voxelPosLv3 = orthoPos.xyz / _VoxelParamsLv3.x;
                    #endif
                    voxelPos = floor(voxelPos);
                    voxelUnionPosLv2 = floor(voxelPosLv2 % 2);                    
                    voxelUnionPosLv3 = floor(voxelPosLv3 % 2);
                #endif
                
                float shadowAlpha = clamp(_ShadowAlpha * saturate(1 + cos(3.1415 * i.litDistance / _ProjSizeParams.z)), 0.3, 0.6);
                ///////// screen space voxel pos
                #ifdef _LIT_SCREEN_SPACE_MODE
                    float3 litSpaceClipPos = i.litSpaceClipPos.xyz;
                    // reverse z  depth == 1 near camera
                    
                    //#ifdef SHADER_API_D3D11
                        litSpaceClipPos.z = (1.0 - litSpaceClipPos.z);
                    //#endif

                    #ifdef _ENABLE_VOXEL_PREDICT
                    // voxelPos = i.predictedLv1VoxelIdx;
                    // voxelPosLv2 = i.predictedLv2VoxelIdx;
                    // voxelPosLv3 = i.predictedLv3VoxelIdx;
                    // float mLv1 = length(voxelPos % 1);
                    // float mLv2 = length(voxelPosLv2 % 1);
                    // float mLv3 = length(voxelPosLv3 % 1);
                    // if(mLv1 > 0){
                    //     voxelPos = floor(litSpaceClipPos * _VoxelParams.z);
                    // }
                    // if(mLv2 > 0){
                    //     voxelPosLv2 = floor(litSpaceClipPos * _VoxelParamsLv2.z);
                    // }
                    // if(mLv3 > 0){
                    //     voxelPosLv3 = floor(litSpaceClipPos * _VoxelParamsLv3.z);
                    // }
                    #else
                        voxelPos = floor(litSpaceClipPos * _VoxelParams.z);
                        voxelPosLv2 = floor(litSpaceClipPos * _VoxelParamsLv2.z);
                        voxelPosLv3 = floor(litSpaceClipPos * _VoxelParamsLv3.z);
                    #endif
                    
                voxelUnionPosLv2 = floor(voxelPosLv2 % 2.0);
                voxelUnionPosLv3 = floor(voxelPosLv3 % 2.0);
                #endif

                #ifndef _LIT_SCREEN_SPACE_MODE
                    float3 litSpaceClipPos = 0;
                #endif

                // TODO uniform param
                float level1TexSize = _level1TexSize; //64 ;// round(sqrt(_VoxelParams.z * _VoxelParams.z * _VoxelParams.z));
                float texPixelIndex = floor(_VoxelParams.z * _VoxelParams.z * voxelPos.z + voxelPos.y * _VoxelParams.z + voxelPos.x);
                float2 Level1IndexMapUV = saturate(float2(floor(texPixelIndex % level1TexSize) / level1TexSize, floor(texPixelIndex / level1TexSize) / (float)level1TexSize));
                
                //return float4(Level1IndexMapUV.x, 0, Level1IndexMapUV.y,1);
                float4 level1LitInfo = tex2Dlod(_Level1IndexMap, float4(Level1IndexMapUV, 0,0));
                float4 level1LitInfoNoArray = tex2Dlod(_Level1IndexMapNoArray, float4(Level1IndexMapUV, 0,0));
                float fact = 1.0 / 1024.0;
                float lv2MapWidth = 32.0;
                float lv2U_fact = 1.0 / lv2MapWidth;
                float v = DecodeFloatRG(level1LitInfo.zw);
                // float v1 = DecodeFloatRG(level1LitInfoNoArray.zw);
                // v = level1LitInfo.z;
                //return level1LitInfo.r;



                // stage 1
                float isLv1Zero = step(0.99, abs(level1LitInfo.r - 1));
                float isLv1One = step(0.99,  abs(level1LitInfo.r));
                float isLv1ZeroOrOne = step(0.99, isLv1Zero + isLv1One);
                // isZero = 1 - saturate(abs(level1LitInfo.r - 0) * 100);
                // isOne = 1 - saturate(abs(level1LitInfo.r - 1) * 100);
                // isZeroOrOne = (bool)(1 - saturate(abs((isZero + isOne) - 1) * 100));
                
                
                float4 colorIfOneOrZero = (float4)0;
                float4 colorIfLv23OneOrZero = (float4)0;
                float4 colorSM = (float4)0;

                float4 finalCol = (float4)0;

                // if(isLv1ZeroOrOne > 0.99){
                    // return fixed4(0,1,0,1);
                    colorIfOneOrZero = saturate(level1LitInfo.r   + (1 - level1LitInfo.r) * shadowAlpha);
                // }
                // else
                // {
                    float texArrayU = floor(voxelUnionPosLv2.z * 4 + voxelUnionPosLv2.y * 2 + voxelUnionPosLv2.x) / lv2MapWidth;
                    texArrayU = floor(voxelUnionPosLv2.y * 8 + voxelUnionPosLv2.x * 4 + voxelUnionPosLv3.y * 2 + voxelUnionPosLv3.x) / lv2MapWidth;

                    float4 arrayValue = UNITY_SAMPLE_TEX2DARRAY_LOD(_Level2LitShadowInfoArray, float3(texArrayU,
                    v + _DEBUG_FACT,
                    floor(level1LitInfo.g * _level2TexArrayDepth) ), //floor(DecodeFloatRG(level1LitInfo.zw) * _level2TexArrayDepth) //
                    0);

                    int idx = floor(voxelUnionPosLv3.y * 2 + voxelUnionPosLv3.x) ;
                    idx = floor(voxelUnionPosLv2.z * 2 + voxelUnionPosLv3.z);
                    float litOrShadowed = arrayValue[idx];

                    //stage 2
                    float isLv23Zero = step(0.99, abs(litOrShadowed.r - 1));
                    float isLv23One = step(0.99,  abs(litOrShadowed.r));
                    float isLv23ZeroOrOne = step(0.99, isLv23Zero + isLv23One);

                    // if(isLv23ZeroOrOne > 0.99){
                        // return fixed4(0,0,1,1);
                        colorIfLv23OneOrZero = saturate(litOrShadowed + (1 - litOrShadowed) * shadowAlpha);
                    // }
                    // else if(abs(litOrShadowed - 0.5) < 0.1){
                        // return 1;
                        // return fixed4(1,0,0,1);  

                        float4 colVoxel = tex2D(_VoxelShadowmap, float4(litSpaceClipPos.xy,0,0).xy);
                        float4 col = tex2D(_Shadowmap, float4(litSpaceClipPos.xy,0,0).xy);
                        
                        #ifdef _SHADOWMAP_LITE
                            float voxelDepth = ceil(DecodeFloatRG(colVoxel.rg) * _VoxelParamsLv3.z);
                            float voxelScopeDepth = DecodeFloatRG(col.rg);// DecodeFloatRG(col.ba); // 
                          
                            float modelVoxelScopeDepth = saturate((litSpaceClipPos.z * (_VoxelParamsLv3.z)) % 1.0 - _ShadowBias1); //  i.depthWithBias % 1; //
                            //return litSpaceClipPos.z;
                           //return DecodeFloatRG(col.rg);

                            float isLv3EqualVoxelDepth = step(0.95, 1 - abs(voxelDepth - voxelPosLv3.z));
                            float isLv3GreaterThanVoxelDepth = step(voxelDepth, voxelPosLv3.z);
                            colorSM = saturate(1 - isLv3EqualVoxelDepth) * isLv3GreaterThanVoxelDepth * shadowAlpha +
                                saturate(1 - isLv3GreaterThanVoxelDepth) * 1 +  
                                isLv3EqualVoxelDepth * saturate(saturate((voxelScopeDepth - modelVoxelScopeDepth) * 100) + shadowAlpha);
                            // if(voxelPosLv3.z > voxelDepth){
                            //     // return fixed4(1,0,0,1);
                            //     colorSM = shadowAlpha;
                            // }
                            // else if(voxelPosLv3.z < voxelDepth)
                            // {
                            //     // return fixed4(0,0,1,1);
                            //     colorSM = 1;
                            // }
                            // else{
                            //     colorSM = saturate(saturate((voxelScopeDepth - modelVoxelScopeDepth) * 100) + shadowAlpha);
                            //     // return modelVoxelScopeDepth  < voxelScopeDepth + 0.1 ? 1:shadowAlpha;
                            // }
                        #else
                            
                            float decodedDepth = DecodeFloatRGBA(col);
                            float depth = i.depthWithBias;
                            finalCol = saturate(saturate((i.depthWithBias - decodedDepth) * 50) + shadowAlpha);
                        #endif
                    // }
                    // else
                        // colorIfLv23OneOrZero = fixed4(1, 0, 0, 1); // unreachable
                

/*
                    //float isHalf = level1LitInfo.a;
                    //texV = texV * 0.5 + isHalf * 0.5;
                    //return floor(voxelUnionPosLv2.z * 4 + voxelUnionPosLv2.y * 2 + voxelUnionPosLv2.x) / 8;
                    
                    float4 lv2Info = tex2Dlod(_Level2LitShadowInfo, float4(lv2U_fact * floor(voxelUnionPosLv2.z * 4 + voxelUnionPosLv2.y * 2 + voxelUnionPosLv2.x),  texV, 0, 0));
                    int i = round(clamp(voxelUnionPosLv3.y * 2 + voxelUnionPosLv3.x, 0, 4));
                    
                    float litOrShadowed = lv2Info[(int)(floor(voxelUnionPosLv3.y * 2 + voxelUnionPosLv3.x) )];
                    */
                    
                // }
                // else{
                //     return fixed4(0,0,0,1);
                // }
                // return  !isLv1ZeroOrOne && !isLv23ZeroOrOne * saturate(colorSM - 0.3);
                finalCol = isLv1ZeroOrOne * colorIfOneOrZero + 
                    saturate(1 - isLv1ZeroOrOne) * isLv23ZeroOrOne * colorIfLv23OneOrZero + 
                    saturate(1 - isLv1ZeroOrOne) * saturate(1 - isLv23ZeroOrOne) * colorSM;

                return finalCol;
            }
            ENDCG
        }
    }
}
