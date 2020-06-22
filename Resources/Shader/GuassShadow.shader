Shader "Unlit/GuassShadow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        
        _ShadowMapTexture_TexelSize("_ShadowMapTexture_TexelSize", Vector) = (0.002,0.002,500,500)   
        _ReceiverPlaneDepthBias("receiverPlaneDepthBias", float) = 0
        _Pandding("_Pandding", Vector) = (0,0,1,1)
        //_ScreenSpaceShadow("_ScreenSpaceShadow", 2D) = "white" {}
        [Toggle]_SCALE3OR5("_Scale3Or5", int) = 0
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

            #pragma shader_feature _SCALE3OR5_ON

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float _ReceiverPlaneDepthBias;
            float4 _Pandding;

            UNITY_DECLARE_SHADOWMAP(_ScreenSpaceShadow);
            // sampler2D _ScreenSpaceShadow;
            float4 _ShadowMapTexture_TexelSize;
            #define SHADOWMAPSAMPLER_AND_TEXELSIZE_DEFINED
    


/**
* Combines the different components of a shadow coordinate and returns the final coordinate.
* See UnityGetReceiverPlaneDepthBias
*/
float3 UnityCombineShadowcoordComponents(float2 baseUV, float2 deltaUV, float depth, float3 receiverPlaneDepthBias)
{
    float3 uv = float3(baseUV + deltaUV, depth + receiverPlaneDepthBias.z);
    uv.z += dot(deltaUV, receiverPlaneDepthBias.xy);
    return uv;
}

/**
* PCF gaussian shadowmap filtering based on a 3x3 kernel (9 taps no PCF hardware support)
*/
half UnitySampleShadowmap_PCF3x3NoHardwareSupport(float4 coord, float3 receiverPlaneDepthBias)
{
        half shadow = 1;

    #ifdef SHADOWMAPSAMPLER_AND_TEXELSIZE_DEFINED
        // when we don't have hardware PCF sampling, then the above 5x5 optimized PCF really does not work.
        // Fallback to a simple 3x3 sampling with averaged results.
        float2 base_uv = coord.xy;
        float2 ts = _ShadowMapTexture_TexelSize.xy;
        shadow = 0;
        shadow += UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(-ts.x, -ts.y), coord.z, receiverPlaneDepthBias));
        shadow += UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(0, -ts.y), coord.z, receiverPlaneDepthBias));
        shadow += UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(ts.x, -ts.y), coord.z, receiverPlaneDepthBias));
        shadow += UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(-ts.x, 0), coord.z, receiverPlaneDepthBias));
        shadow += UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(0, 0), coord.z, receiverPlaneDepthBias));
        shadow += UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(ts.x, 0), coord.z, receiverPlaneDepthBias));
        shadow += UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(-ts.x, ts.y), coord.z, receiverPlaneDepthBias));
        shadow += UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(0, ts.y), coord.z, receiverPlaneDepthBias));
        shadow += UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(ts.x, ts.y), coord.z, receiverPlaneDepthBias));
        shadow /= 9.0;
    #endif

        return shadow;
}

/**
* PCF gaussian shadowmap filtering based on a 5x5 kernel (optimized with 9 taps)
*
* Algorithm: http://the-witness.net/news/2013/09/shadow-mapping-summary-part-1/
* Implementation example: http://mynameismjp.wordpress.com/2013/09/10/shadow-maps/
*/
half UnitySampleShadowmap_PCF5x5Gaussian(float4 coord, float3 receiverPlaneDepthBias)
{
        half shadow = 1;

    #ifdef SHADOWMAPSAMPLER_AND_TEXELSIZE_DEFINED

        #ifndef SHADOWS_NATIVE
            // when we don't have hardware PCF sampling, fallback to a simple 3x3 sampling with averaged results.
            return UnitySampleShadowmap_PCF3x3NoHardwareSupport(coord, receiverPlaneDepthBias);
        #endif

        const float2 offset = float2(0.5, 0.5);
        float2 uv = (coord.xy * _ShadowMapTexture_TexelSize.zw) + offset;
        float2 base_uv = (floor(uv) - offset) * _ShadowMapTexture_TexelSize.xy;
        float2 st = frac(uv);

        float3 uw = float3(4 - 3 * st.x, 7, 1 + 3 * st.x);
        float3 u = float3((3 - 2 * st.x) / uw.x - 2, (3 + st.x) / uw.y, st.x / uw.z + 2);
        u *= _ShadowMapTexture_TexelSize.x;

        float3 vw = float3(4 - 3 * st.y, 7, 1 + 3 * st.y);
        float3 v = float3((3 - 2 * st.y) / vw.x - 2, (3 + st.y) / vw.y, st.y / vw.z + 2);
        v *= _ShadowMapTexture_TexelSize.y;

        half sum = 0.0f;

        half3 accum = uw * vw.x;
        sum += accum.x * UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(u.x, v.x), coord.z, receiverPlaneDepthBias));
        sum += accum.y * UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(u.y, v.x), coord.z, receiverPlaneDepthBias));
        sum += accum.z * UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(u.z, v.x), coord.z, receiverPlaneDepthBias));

        accum = uw * vw.y;
        sum += accum.x *  UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(u.x, v.y), coord.z, receiverPlaneDepthBias));
        sum += accum.y *  UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(u.y, v.y), coord.z, receiverPlaneDepthBias));
        sum += accum.z *  UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(u.z, v.y), coord.z, receiverPlaneDepthBias));

        accum = uw * vw.z;
        sum += accum.x * UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(u.x, v.z), coord.z, receiverPlaneDepthBias));
        sum += accum.y * UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(u.y, v.z), coord.z, receiverPlaneDepthBias));
        sum += accum.z * UNITY_SAMPLE_SHADOW(_ScreenSpaceShadow, UnityCombineShadowcoordComponents(base_uv, float2(u.z, v.z), coord.z, receiverPlaneDepthBias));
        shadow = sum / 144.0f;

    #endif

        return shadow;
}


            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                 o.uv = v.uv;// TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = (fixed4)0;
                // float clipFact = abs(i.uv.x - 0.5) > (0.5 - _Pandding.x / _Pandding.z) || abs(i.uv.y - 0.5) > (0.5 - _Pandding.y / _Pandding.w)?-1:1;
                // if(clipFact < 0){
                //     return 1;
                // }
                #ifndef _SCALE3OR5_ON
                    //float4 uv = -0.025 + 1.10 * half4(i.uv, 0,1);
                    //if((0.5 - abs(uv.x - 0.5) < 0.02) || (0.5 - abs(uv.y - 0.5) < 0.02))
                    //    return 1;
                    col = UnitySampleShadowmap_PCF3x3NoHardwareSupport( half4(i.uv, 0,1), _ReceiverPlaneDepthBias);
                #else
                    col = UnitySampleShadowmap_PCF5x5Gaussian(half4(i.uv, 0,1), _ReceiverPlaneDepthBias);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
