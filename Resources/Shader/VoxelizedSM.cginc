#ifndef VOXELIZEDSM_INCLUDED
// Upgrade NOTE: excluded shader from DX11, OpenGL ES 2.0 because it uses unsized arrays
#pragma exclude_renderers d3d11 gles
#define VOXELIZEDSM_INCLUDED
#ifdef _DEBUG_SM_ON
#define __DEBUG_COLOR_
#endif

#if defined(_LIT_SCREEN_SPACE_MODE_ON)// && !defined(_LIT_SCREEN_SPACE_MODE)
#define _LIT_SCREEN_SPACE_MODE
#else //if !defined(_LIT_SCREEN_SPACE_MODE_ON)
#undef _LIT_SCREEN_SPACE_MODE
#endif
#if defined(_ENABLE_PREDICT_ON)// && !defined(_ENABLE_VOXEL_PREDICT)
#define _ENABLE_VOXEL_PREDICT
#else //if !defined(_ENABLE_PREDICT_ON)
#undef _ENABLE_VOXEL_PREDICT
#endif
 #define _LIT_SCREEN_SPACE_MODE

// high precision mode
//#define _SHADOWMAP_LITE1
// proto mode
//#define _SHADOWMAP_LITE
#define _ENABLE_LV4_VOXEL

// Input Macro

#if defined(_LIT_SCREEN_SPACE_MODE)
    #define DECLARE_POS(idx) float4 litSpaceClipPos : TEXCOORD##idx;
#else
    #define DECLARE_POS(idx) float4 litSpacePos : TEXCOORD##idx;
#endif

#ifdef _ENABLE_VOXEL_PREDICT
    #define DECLARE_PREDICT(idx, lv) float3 predictedLv##lv##VoxelIdx : TEXCOORD##idx;
#else
    #define DECLARE_PREDICT(idx, lv)
#endif

#define VOXELIZED_SM_COORDS(idx, idx1,idx2,idx3,idx4,idx5) float litDistance : TEXCOORD##idx; float depthWithBias : TEXCOORD##idx1;DECLARE_POS(idx2) DECLARE_PREDICT(idx3, 1) DECLARE_PREDICT(idx4, 2) DECLARE_PREDICT(idx5, 3)


uint table[] = {1,2,4,8,16,32,64,128};
// x: lv1 voxel size(litSpace) y:lv2 voxel Size
float4 _VoxelParams;
float4 _VoxelParamsLv2;
float4 _VoxelParamsLv3;
float4 _ProjSizeParams;

float4x4 _LitViewMatrix;
float4x4 _LitProjMatrix;
float4x4 _LitProjMatrixGPU;
// float4x4 _LitProjMatrixRT;
float4x4 _LitViewProjMatrix;

uniform float _DEBUG_FACT;
uniform float _DEBUG_FACT_10;

uniform float _ShadowAlpha;
uniform float _ShadowBias;
uniform float _ShadowBias1;
uniform float _level1TexSize;
uniform float _level2TexArrayDepth;
uniform float _level4TexArrayDepth;
float _ShadowDensity;
float _ShadowBalance;

sampler2D _Level1IndexMap;
// sampler2D _Level1IndexMapNoArray;
// sampler2D _Level2LitShadowInfo;
UNITY_DECLARE_TEX2DARRAY(_Level2LitShadowInfoArray);
UNITY_DECLARE_TEX2DARRAY(_Level4LitShadowInfoArray);
sampler2D _VoxelShadowmap;
sampler2D _Shadowmap;
sampler2D _VxShadow_Blur;
sampler2D _screenSpaceShadowRT;

// UNITY_DECLARE_TEX2DARRAY(_Level2LitShadowInfoArrayDebug);
// UNITY_DECLARE_TEX2DARRAY(_Level3LitShadowInfoArrayDebug);
// UNITY_DECLARE_TEX2DARRAY(_Level1LitShadowInfoArrayDebug);

typedef struct{
    float2 uv : TEXCOORD0;
    float4 vertex : SV_POSITION;
}VertexInfo;

typedef struct{
    float4 litSpacePos;
    float4 litSpaceClipPos;
    float4 litSpaceClipPosHigh;
    float4 litSpaceClipPosLow;
    float litDistance;
    float depthWithBias;
    float3 predictedLv1VoxelIdx;
    float3 predictedLv2VoxelIdx;
    float3 predictedLv3VoxelIdx;
} VoxelizedSM_Info;

VoxelizedSM_Info Voxelized_vertex(in VertexInfo v) {
                VoxelizedSM_Info voxelizedSM_info = (VoxelizedSM_Info)0;
                float4 viewPos = mul(_LitViewMatrix, mul(UNITY_MATRIX_M, v.vertex));        
                #ifndef _LIT_SCREEN_SPACE_MODE      
                float4 litSpacePos = viewPos;        
                litSpacePos.xyz = viewPos.xyz / viewPos.w;        
                litSpacePos.xy += _ProjSizeParams.xx;     
                litSpacePos.z = abs(litSpacePos.z);     
                voxelizedSM_info.litSpacePos = litSpacePos;
                #endif      
                voxelizedSM_info.litDistance = length(viewPos.xyz / viewPos.w);        
                
                float4 depthPos = mul(_LitProjMatrix, float4(0, 0 , _ShadowBias,0) + viewPos);      
                depthPos.xyz = depthPos.xyz / depthPos.w * 0.5 + 0.5;       
                #ifndef _SHADOWMAP_LITE     
                    voxelizedSM_info.depthWithBias = 1 - depthPos.z;       
                #endif      
                //float4 litSpacePos = mul(_LitProjMatrix, viewPos);        
                #ifdef _LIT_SCREEN_SPACE_MODE       
                float4 litSpaceClipPos = (float4)0;
                #ifdef _MODE_GPUMATRIX_ON       
                    litSpaceClipPos = mul(_LitProjMatrixGPU, float4(0, 0 , _ShadowBias,0) + viewPos);        
                    litSpaceClipPos.xy = saturate(litSpaceClipPos.xy / litSpaceClipPos.w * 0.5 + 0.5);      
                    litSpaceClipPos.z = 1 - litSpaceClipPos.z;      
                    litSpaceClipPos.w = 1;
                #else       
                    litSpaceClipPos = mul(_LitProjMatrix, float4(0, 0 , _ShadowBias,0) + viewPos);     
                    litSpaceClipPos.xyz = saturate(litSpaceClipPos.xyz / litSpaceClipPos.w * 0.5 + 0.5);       
                    litSpaceClipPos.z = 1 - litSpaceClipPos.z;      
                    litSpaceClipPos.w = 1;
                #endif      
                voxelizedSM_info.litSpaceClipPos = litSpaceClipPos;
                #ifdef _ENABLE_64BIT_POS
                    float2 rg = (float2)0;
                    rg = EncodeFloatRG(litSpaceClipPos.x);
                    voxelizedSM_info.litSpaceClipPosHigh.x = rg.x;
                    voxelizedSM_info.litSpaceClipPosLow.x = rg.y;
                    rg = EncodeFloatRG(litSpaceClipPos.y);
                    voxelizedSM_info.litSpaceClipPosHigh.y = rg.x;
                    voxelizedSM_info.litSpaceClipPosLow.y = rg.y;
                    rg = EncodeFloatRG(litSpaceClipPos.z);
                    voxelizedSM_info.litSpaceClipPosHigh.z = rg.x;
                    voxelizedSM_info.litSpaceClipPosLow.z = rg.y;
                    rg = EncodeFloatRG(litSpaceClipPos.w);
                    voxelizedSM_info.litSpaceClipPosHigh.w = rg.x;
                    voxelizedSM_info.litSpaceClipPosLow.w = rg.y;
                #endif
                #endif      
                    
    
                #ifdef _ENABLE_VOXEL_PREDICT        
    
                float3 voxelPos = 0;        
                float3 voxelPosLv2 = 0;     
                float3 voxelPosLv3 = 0;     
                float3 voxelUnionPosLv2 = 0;        
                float3 voxelUnionPosLv3 = 0;        
                    
                    
                #ifndef _LIT_SCREEN_SPACE_MODE  
                    float3 orthoPos = litSpacePos;        
                    voxelPos = orthoPos.xyz / _VoxelParams.x;       
                    voxelPos = floor(voxelPos);     
                    voxelPosLv2 = orthoPos.xyz / _VoxelParamsLv2.x;     
                    voxelUnionPosLv2 = floor(voxelPosLv2 % 2);      
                    voxelPosLv3 = orthoPos.xyz / _VoxelParamsLv3.x;     
                #endif      
                    
                ///////// screen space voxel pos    
                #ifdef _LIT_SCREEN_SPACE_MODE       
                    float3 litSpaceClipPosXYZ = litSpaceClipPos.xyz;     
                    // reverse z  depth == 1 near camera    
                    litSpaceClipPosXYZ.z = (1.0 - litSpaceClipPosXYZ.z);      
                    voxelPos = floor(litSpaceClipPosXYZ * _VoxelParams.z);     
                    voxelPosLv2 = floor(litSpaceClipPosXYZ * _VoxelParamsLv2.z);       
                    voxelPosLv3 = floor(litSpaceClipPosXYZ * _VoxelParamsLv3.z);     
                #endif          
    
                voxelizedSM_info.predictedLv1VoxelIdx = voxelPos;      
                voxelizedSM_info.predictedLv2VoxelIdx = voxelPosLv2;       
                voxelizedSM_info.predictedLv3VoxelIdx = voxelPosLv3;       
                #endif      
                return voxelizedSM_info;
}

#ifdef _ENABLE_VOXEL_PREDICT
    #define TRANS_PREDICT o.predictedLv1VoxelIdx = voxelizedSM_info.predictedLv1VoxelIdx; \
                    o.predictedLv2VoxelIdx = voxelizedSM_info.predictedLv2VoxelIdx; \
                    o.predictedLv3VoxelIdx = voxelizedSM_info.predictedLv3VoxelIdx; 
#else
    #define TRANS_PREDICT 
#endif

#ifdef _LIT_SCREEN_SPACE_MODE  
#define TRANS_POS o.litSpaceClipPos = voxelizedSM_info.litSpaceClipPos;  
#else  
#define TRANS_POS o.litSpacePos = voxelizedSM_info.litSpacePos;                  
#endif 

#define TRANS_VOXELIZED(v1, o1, texcoord)    \
                VertexInfo vertexInfo;  \
                vertexInfo.uv = v1.##texcoord;   \
                vertexInfo.vertex = v1.vertex;   \
                VoxelizedSM_Info voxelizedSM_info = Voxelized_vertex(vertexInfo);  \
                o1.litDistance = voxelizedSM_info.litDistance;   \
                o1.depthWithBias = voxelizedSM_info.depthWithBias;  \
                TRANS_PREDICT   \
                TRANS_POS 




// return shadow color
fixed4 VoxelizedFrag(in VoxelizedSM_Info i){
                float3 voxelPos = 0;
                float3 voxelPosLv2 = 0;
                float3 voxelPosLv3 = 0;
                float voxelIdLv3 = 0;
                float3 voxelIdLv4 = 0;
                float3 voxelUnionPosLv2 = 0;
                float3 voxelUnionPosLv3 = 0;
                
                uint3 uVoxelPos = 0;
                uint3 uVoxelPosLv2 = 0;
                uint3 uVoxelPosLv3 = 0;


                float shadowAlpha = clamp(_ShadowAlpha * saturate(1 + cos(3.1415 * i.litDistance / _ProjSizeParams.z)), 0.3, 0.6);
                #ifndef _LIT_SCREEN_SPACE_MODE
                    float3 litSpacePos = i.litSpacePos;
                    float3 orthoPos = litSpacePos;
                    #ifdef _ENABLE_VOXEL_PREDICT
                        voxelPos = i.predictedLv1VoxelIdx;
                        voxelPosLv2 = i.predictedLv2VoxelIdx;
                        voxelPosLv3 = i.predictedLv3VoxelIdx;                    
                        float mLv1 = length(voxelPos % 1);
                        float mLv2 = length(voxelPosLv2 % 1);
                        float mLv3 = length(voxelPosLv3 % 1);
                        float fullPredict = (mLv1 + mLv2 + mLv3);
                        if(mLv1 > 0)
                            voxelPos = orthoPos.xyz / _VoxelParams.x;
                        if(mLv2 > 0)
                            voxelPosLv2 = orthoPos.xyz / _VoxelParamsLv2.x;
                        if(mLv3 > 0)
                            voxelPosLv3 = orthoPos.xyz / _VoxelParamsLv3.x;
                    #else
                        voxelPos = orthoPos.xyz / _VoxelParams.x;
                        voxelPosLv2 = orthoPos.xyz / _VoxelParamsLv2.x;
                        voxelPosLv3 = orthoPos.xyz / _VoxelParamsLv3.x;
                        voxelIdLv4 = (voxelPosLv3 % 1.0) * 8.0;
                    #endif
                    voxelPos = floor(voxelPos);
                    voxelUnionPosLv2 = floor(voxelPosLv2 % 2);                    
                    voxelUnionPosLv3 = floor(voxelPosLv3 % 2);
                #endif
                
                ///////// screen space voxel pos
                float3 litSpaceClipPos = 0;
                #ifdef _LIT_SCREEN_SPACE_MODE
                    litSpaceClipPos = i.litSpaceClipPos.xyz;
                    // reverse z  depth == 1 near camera
                    
                    //#ifdef SHADER_API_D3D11
                        litSpaceClipPos.z = (1.0 - litSpaceClipPos.z);
                    //#endif
                    #ifdef _ENABLE_VOXEL_PREDICT
                        voxelPos = i.predictedLv1VoxelIdx;
                        voxelPosLv2 = i.predictedLv2VoxelIdx;
                        voxelPosLv3 = i.predictedLv3VoxelIdx;
                        float mLv1 = length(voxelPos % 1);
                        float mLv2 = length(voxelPosLv2 % 1);
                        float mLv3 = length(voxelPosLv3 % 1);
                        if(mLv1 > 0){
                            voxelPos = floor(litSpaceClipPos * _VoxelParams.z);
                        }
                        if(mLv2 > 0){
                            voxelPosLv2 = floor(litSpaceClipPos * _VoxelParamsLv2.z);
                        }
                        if(mLv3 > 0){
                            voxelPosLv3 = floor(litSpaceClipPos * _VoxelParamsLv3.z);
                        }
                    #else
                        float3 fVoxelPos = litSpaceClipPos * _VoxelParams.z;
                        voxelPos = floor(litSpaceClipPos * _VoxelParams.z);
                        voxelPosLv2 = floor(litSpaceClipPos * _VoxelParamsLv2.z);
                        voxelPosLv3 = floor(litSpaceClipPos * _VoxelParamsLv3.z);
                        voxelIdLv3 = floor((voxelPos * 1.0) * 4.0);
                        voxelIdLv4 = floor((litSpaceClipPos * _VoxelParamsLv3.z * 8.0) % 8.0);
                        
                        voxelIdLv3 = floor((litSpaceClipPos.z / _VoxelParamsLv3.w) % 4);
                        voxelIdLv4 = floor((litSpaceClipPos / _VoxelParamsLv3.w % 1) * 8);
                        
                        uVoxelPosLv3 = (uint)floor(litSpaceClipPos * _VoxelParamsLv3.z);
                        uVoxelPosLv2 = (uint)floor(litSpaceClipPos * _VoxelParamsLv2.z);
                        uVoxelPos = (uint)floor(litSpaceClipPos * _VoxelParams.z);
                    #endif
                voxelUnionPosLv2 = floor(voxelPosLv2 % 1.999); // 2.0); //
                voxelUnionPosLv3 = floor(voxelPosLv3 % 1.999); // 2.0); //
                #endif
                
                // TODO uniform param
                float level1TexSize = _level1TexSize; //64 ;// round(sqrt(_VoxelParams.z * _VoxelParams.z * _VoxelParams.z));
                float texPixelIndex = floor(_VoxelParams.z * _VoxelParams.z * voxelPos.z + voxelPos.y * _VoxelParams.z + voxelPos.x);
                float2 Level1IndexMapUV = saturate(float2(floor(texPixelIndex % level1TexSize) / level1TexSize, floor(texPixelIndex / level1TexSize) / (float)level1TexSize));
                
                //return float4(Level1IndexMapUV.x, 0, Level1IndexMapUV.y,1);
                float4 level1LitInfo = tex2Dlod(_Level1IndexMap, float4(Level1IndexMapUV, 0,0));
                // float4 level1LitInfoNoArray = tex2Dlod(_Level1IndexMapNoArray, float4(Level1IndexMapUV, 0,0));
                float fact = 1.0 / 1024.0;
                float lv2MapWidth = 32.0;
                float lv2U_fact = 1.0 / lv2MapWidth;
                float v = level1LitInfo.y;// DecodeFloatRG(level1LitInfo.zw);
                float texDepth = DecodeFloatRG(level1LitInfo.zw);
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
                
                #ifdef __DEBUG_COLOR_
                  if(isLv1One > 0.5)
                      return fixed4(0,0,1,1);
                 if(isLv1Zero > 0.5)
                      return fixed4(0,0,1,1);
                // if(isLv1ZeroOrOne < 0.5)
                //     return fixed4(1,0,0,1);
                #endif

                float4 colorIfOneOrZero = (float4)0;
                float4 colorIfLv23OneOrZero = (float4)0;
                float4 colorSM = (float4)0;
                float4 color4 = (float)0;

                float4 finalCol = (float4)0;
                
                colorIfOneOrZero = saturate(level1LitInfo.r   + (1 - level1LitInfo.r) * shadowAlpha);
                    
                float texArrayU = floor(voxelUnionPosLv2.z * 4 + voxelUnionPosLv2.y * 2 + voxelUnionPosLv2.x) / lv2MapWidth;
                texArrayU = floor(voxelUnionPosLv2.y * 8 + voxelUnionPosLv2.x * 4 + voxelUnionPosLv3.y * 2 + voxelUnionPosLv3.x) / lv2MapWidth;

                float4 arrayValue = UNITY_SAMPLE_TEX2DARRAY_LOD(_Level2LitShadowInfoArray, (1 - isLv1ZeroOrOne) * float3(texArrayU,
                v,
                round(texDepth * _level2TexArrayDepth)), //floor(DecodeFloatRG(level1LitInfo.zw) * _level2TexArrayDepth) //
                0);

                float4 lv4uv = UNITY_SAMPLE_TEX2DARRAY_LOD(_Level2LitShadowInfoArray, (1 - isLv1ZeroOrOne) * float3(texArrayU  + 16.0 / lv2MapWidth ,
                v,
                round(texDepth * _level2TexArrayDepth)), 0);

                int idx = floor(voxelUnionPosLv3.y * 2 + voxelUnionPosLv3.x) ;
                idx = floor(voxelUnionPosLv2.z * 2 + voxelUnionPosLv3.z);
                float litOrShadowed = arrayValue[idx];

                //stage 2
                float isLv23Zero = step(0.99, abs(litOrShadowed.r - 1));
                float isLv23One = step(0.99,  abs(litOrShadowed.r));
                float isLv23ZeroOrOne = step(0.99, isLv23Zero + isLv23One);

                colorIfLv23OneOrZero = saturate(litOrShadowed + (1 - litOrShadowed) * shadowAlpha);
                
                #ifdef __DEBUG_COLOR_
                 if(isLv23One > 0.5)
                     return fixed4(0,0.5,1,1);
                 if(isLv23Zero > 0.5)
                     return fixed4(0,0.5,1,1);
                // if(isLv23ZeroOrOne < 0.5)
                //     return fixed4(1,0.5,0,1);
                #endif

                // #ifdef _DEBUG_SM_ON
                // if(isLv1ZeroOrOne < 0.5 && isLv23ZeroOrOne < 0.5)
                // return fixed4(1,0,0,1);
                // #endif
                #ifdef _ENABLE_LV4_VOXEL
                float lv4V = lv4uv.r + _DEBUG_FACT_10;// + _DEBUG_FACT + _DEBUG_FACT_10;// DecodeFloatRG(lv4uv.rg);
                float flv4Depth = DecodeFloatRG(lv4uv.ba);
                float lv4Depth = round(_level4TexArrayDepth * flv4Depth);
                float lv4UPixelOffset = voxelIdLv3;//floor(voxelPosLv3.z % 4.0);
                float3 lv4Pos = voxelIdLv4; // floor(voxelPosLv3 / (1.0 / 8.0)); // floor(voxelIdLv4 % 7.999);
                float lv4UPixel = floor(lv4Pos.y * 2.0) + floor(lv4Pos.x / 4.0) + floor(16 * lv4UPixelOffset);// + round(100 * _DEBUG_FACT_10);
                //return voxelIdLv4.z / 8;
                float lv4U = lv4UPixel / 63.0;
                float4 lv4Color = UNITY_SAMPLE_TEX2DARRAY_LOD(_Level4LitShadowInfoArray, (1 - isLv23ZeroOrOne) * float3(lv4U, lv4V, lv4Depth), 0);
                uint table1[] = {1,2,4,8,16,32,64,128};
                uint flag = (uint)table1[(uint)round(lv4Pos.z)]; //(uint)(1u << (int)floor(lv4Pos.z)); // 
                //return flag / 128.0;
                // return (uint)round((1- lv4Color[floor(lv4Pos.x % 4.0)]) * 255.0) & flag;
               
                uint lvC = (uint)floor(lv4Color[floor(lv4Pos.x % 4.0)] * 255.0) & flag; //lv4Pos.x % 3.99
                color4 = lvC > 0 ? 1 : shadowAlpha.xxxx;
                color4 = max(color4, step(0.999, litSpaceClipPos.z));
                //color4 = lv4Depth / 64.0;
                //color4 = lvC > 0 ? fixed4(1,0,0,1) : fixed4(0,0,1,1);//shadowAlpha.xxxx;
                
                //color4 = lv4Color.r;
                // color4 = 1;
                //return lv4Color[floor(lv4Pos.x % 4)];

                //color4 = max(lvC, max(approxC, max(approxD, max(approxE, approxF)))) > 0 ? 1 : shadowAlpha;
                finalCol = isLv1ZeroOrOne * colorIfOneOrZero + 
                    saturate(1 - isLv1ZeroOrOne) * isLv23ZeroOrOne * colorIfLv23OneOrZero + 
                    saturate(1 - isLv1ZeroOrOne) * saturate(1 - isLv23ZeroOrOne) * color4;
                #else

                float4 colVoxel = tex2D(_VoxelShadowmap, (1 - isLv23ZeroOrOne) * float4(litSpaceClipPos.xy,0,0).xy);
                float4 col = tex2D(_Shadowmap, (1 - isLv23ZeroOrOne) * float4(litSpaceClipPos.xy,0,0).xy);

                    #ifdef _SHADOWMAP_LITE1
                        float voxelDepth = round((1.0 * DecodeFloatRG(colVoxel.rg) + 0.0 * colVoxel.b) / _VoxelParams.w); 
                        float voxelDepthLv2 = round(colVoxel.b *  2.0);
                        float voxelDepthLv3 = round(col.b * 2.0);
                        float voxelScopeDepth = 1.0 * DecodeFloatRG(col.rg) + 0.0 * col.b;// DecodeFloatRG(col.ba); // 
                        float isLv1Equal = step(0.9999, 1 - abs(voxelDepth - voxelPos.z));
                        float isLv1Greater = step(voxelDepth, voxelPos.z);
                        float isLv2Equal = step(0.9999, 1 - abs(voxelDepthLv2 - voxelUnionPosLv2.z));
                        float isLv2Greater = step(voxelDepthLv2, voxelUnionPosLv2.z);
                        float isLv3Equal = step(0.9999, 1 - abs(voxelDepthLv3 - voxelUnionPosLv3.z));
                        float isLv3Greater = step(voxelDepthLv3, voxelUnionPosLv3.z);
                        
                        float modelVoxelScopeDepth = saturate((litSpaceClipPos.z * (_VoxelParamsLv3.z)) % 1 - _ShadowBias1); //  i.depthWithBias % 1; //

                        // if(isLv1Equal > 0.5) return fixed4(0,0,1,1);
                        // if(isLv2Equal > 0.5 && isLv2Equal > 0.5) return fixed4(0,1,0,1);
                        // if(isLv2Equal > 0.5 && isLv2Equal > 0.5 && isLv3Equal > 0.5) return fixed4(1,0,0,1);
                        
                        //float isLv3EqualVoxelDepth = step(0.95, 1 - abs(voxelDepth - voxelPosLv3.z));
                        float lv1_2_Equal = isLv1Equal * isLv2Equal;
                        float lv1_E_2NE = isLv1Equal * (1 - isLv2Equal);
                        float lv1_2_3_Equal = isLv1Equal * isLv2Equal * isLv3Equal;
                        float lv1_2_E_3_NE =  isLv1Equal * isLv2Equal * (1 - isLv3Equal);
                        float isLv3GreaterThanVoxelDepth = step(voxelDepth, voxelPosLv3.z);
                        colorSM = saturate(1 - isLv1Equal) * isLv1Greater * shadowAlpha +
                            (1 - isLv1Equal) * saturate(1 - isLv1Greater) * 1 +
                            lv1_E_2NE * isLv2Greater * shadowAlpha +
                            lv1_E_2NE * saturate(1 - isLv2Greater) * 1 +
                            lv1_2_E_3_NE * isLv3Greater * shadowAlpha +
                            lv1_2_E_3_NE * saturate(1 - isLv3Greater) * 1 +
                            //lv1_2_3_Equal;
                            lv1_2_3_Equal * saturate(saturate((voxelScopeDepth - modelVoxelScopeDepth) * 100) + shadowAlpha);

                        #ifdef __DEBUG_COLOR_
                        if(isLv1ZeroOrOne < 0.5 && isLv23ZeroOrOne < 0.5 && lv1_2_E_3_NE > 0.5 && isLv3Greater < 0.5 )
                            return fixed4(0,0,1,1);
                        #endif
                        #ifdef __DEBUG_COLOR_
                        if(isLv1ZeroOrOne < 0.5 && isLv23ZeroOrOne < 0.5 && lv1_2_3_Equal > 0.5 && saturate((voxelScopeDepth - modelVoxelScopeDepth) * 100) > 0.5 )
                            return fixed4(1,1,0,1);
                        #endif
                    #elif defined(_SHADOWMAP_LITE)
                        float voxelDepth = round((1.0 * DecodeFloatRG(colVoxel.rg) + 0.0 * colVoxel.b) / _VoxelParamsLv3.w ); 
                        float voxelScopeDepth = 1.0 * DecodeFloatRG(col.rg) + 0.0 * col.b;// DecodeFloatRG(col.ba); // 
                          
                        float modelVoxelScopeDepth = saturate((litSpaceClipPos.z / (_VoxelParamsLv3.w)) % 1.0 - _ShadowBias1); //  i.depthWithBias % 1; //

                        float isLv3EqualVoxelDepth = step(0.95, 1 - abs(voxelDepth - voxelPosLv3.z));
                        float isLv3GreaterThanVoxelDepth = step(voxelDepth, voxelPosLv3.z);
                        colorSM = saturate(1 - isLv3EqualVoxelDepth) * isLv3GreaterThanVoxelDepth * shadowAlpha +
                            saturate(1 - isLv3GreaterThanVoxelDepth) * 1 +  
                            isLv3EqualVoxelDepth * saturate(saturate((voxelScopeDepth - modelVoxelScopeDepth) * 100) + shadowAlpha);
                        

                        #ifdef __DEBUG_COLOR_
                        if(isLv3EqualVoxelDepth < 0.5)
                            return fixed4(0,1,1,1);
                        // if(isLv1ZeroOrOne < 0.5 && isLv23ZeroOrOne < 0.5 && isLv3EqualVoxelDepth > 0.5 && isLv3GreaterThanVoxelDepth > 0.5 )
                        //     return fixed4(1,0,0,1);
                         #endif

                    #else
                            
                        float decodedDepth = DecodeFloatRGBA(col);
                        // float depth = i.depthWithBias;
                        colorSM = saturate(saturate(((1 - i.depthWithBias) - decodedDepth) * 100) + shadowAlpha);
                    #endif

                // if(saturate(1 - isLv1ZeroOrOne) * saturate(1 - isLv23ZeroOrOne) > 0.5)
                //     return fixed4(1,1,0,1);
                finalCol = isLv1ZeroOrOne * colorIfOneOrZero + 
                    saturate(1 - isLv1ZeroOrOne) * isLv23ZeroOrOne * colorIfLv23OneOrZero + 
                    saturate(1 - isLv1ZeroOrOne) * saturate(1 - isLv23ZeroOrOne) * colorSM;
                #endif
                return finalCol;
}

#ifdef _LIT_SCREEN_SPACE_MODE
    #define TRANS_POS_FRAG voxelizedSM_info.litSpaceClipPos = i.litSpaceClipPos;
#else
    #define TRANS_POS_FRAG voxelizedSM_info.litSpacePos = i.litSpacePos;
#endif

#ifdef _ENABLE_VOXEL_PREDICT
#define TRANS_PREDIC_FRAG voxelizedSM_info.litDistance = i.litDistance;          \
                voxelizedSM_info.predictedLv1VoxelIdx= i.predictedLv1VoxelIdx;                  \
                voxelizedSM_info.predictedLv2VoxelIdx= i.predictedLv2VoxelIdx;                  \
                voxelizedSM_info.predictedLv3VoxelIdx= i.predictedLv3VoxelIdx; 
#else
#define TRANS_PREDIC_FRAG
#endif


#define TRANS_FRAG(i, col) \
                VoxelizedSM_Info voxelizedSM_info = (VoxelizedSM_Info)0; \
                TRANS_POS_FRAG  \
                TRANS_PREDIC_FRAG   \
                col = VoxelizedFrag(voxelizedSM_info);


#endif