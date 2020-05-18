#define _ENABLE_LV3_MODE

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngineInternal;
using UnityEngine.Assertions.Must;
using System.Linq;

public class ShadowmapBaker : UnityEditor.EditorWindow
{
    public enum RenderMode
    {
        None= 0,
        Shadowmap = 1,
        VoxelShadowmapDiff = 2, // diff depth between front depth and final depth for each voxel 
        VoxelShadowmapAnd = 3, 
        VoxelShadowmapMaxKernel = 4, // compute litOrShadowInfo for each voxel
    }

    static ShadowmapBaker _instance;
    [MenuItem("GLA/ShadowmapBakerEditorTool")]
    static void Create()
    {
        if(_instance != null)
        {
            _instance.Close();
            _instance = null;
        }
        _instance = CreateInstance<ShadowmapBaker>();
        _instance.Show();
    }


    // Rendering
    public Camera shadowCamera;
    public RenderTexture shadowMap;
    float startPlane = 0;
    bool zClip = false;
    bool bExportLvLitShadowInfoTexArray4Dbg = true;
    bool bDrawmeshInstancingOrDrawRender = false;
    RenderMode renderMode;

    // Voxel info
    float VoxelSize = 10;
    int RootVoxelWidthSize = 4;
    float OrthoProjSize = 300;
    float nearClip = 0;
    float farClip = 500;
    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        renderMode = (RenderMode) EditorGUILayout.EnumPopup(renderMode);
        
        bDrawmeshInstancingOrDrawRender = EditorGUILayout.Toggle("DrawmeshInstancingOrDrawRender", bDrawmeshInstancingOrDrawRender);
        zClip = EditorGUILayout.Toggle("_ZCLIP", zClip);
        startPlane = EditorGUILayout.Slider("startPlane", startPlane, -1, 1000);
        shadowCamera = EditorGUILayout.ObjectField("shadowCamera", shadowCamera, typeof(Camera)) as Camera;
        shadowMap = EditorGUILayout.ObjectField("shadowmap", shadowMap, typeof(RenderTexture)) as RenderTexture;

        RootVoxelWidthSize = Mathf.ClosestPowerOfTwo(EditorGUILayout.IntField("RootVoxelWidthSize", RootVoxelWidthSize));
        OrthoProjSize = EditorGUILayout.FloatField("OrthoProjSize", OrthoProjSize);
        nearClip = EditorGUILayout.FloatField("nearClip", nearClip);
        farClip = EditorGUILayout.FloatField("farClip", farClip);
        VoxelSize = OrthoProjSize * 2.0f / (float)RootVoxelWidthSize;

        bExportLvLitShadowInfoTexArray4Dbg = EditorGUILayout.Toggle("bExportLvLitShadowInfoTexArray4Dbg", bExportLvLitShadowInfoTexArray4Dbg);
        if (GUILayout.Button("Precompute voxel depth"))
        {
            precomputeVoxelDepth();
        }

        if (GUILayout.Button("Bake"))
        {
            bake();
        }

        if (GUILayout.Button("SetupMatrix"))
        {
            SetupMatrix();
        }

        EditorGUILayout.EndVertical();
    }

    void SetupMatrix()
    {
        float orthoHalfSize = shadowCamera.orthographicSize = OrthoProjSize;
        Matrix4x4 shadowmapProjMatrix = Matrix4x4.Ortho(-orthoHalfSize, orthoHalfSize, -orthoHalfSize, orthoHalfSize, nearClip, farClip);// shadowCamera.farClipPlane);
        Matrix4x4 shadowmapViewMatrix = Matrix4x4.TRS(shadowCamera.transform.position, shadowCamera.transform.rotation, new Vector3(1, 1, -1));
        //shadowmapViewMatrix = shadowmapViewMatrix.inverse;
        Debug.Log(shadowmapProjMatrix);
        Debug.Log(GL.GetGPUProjectionMatrix(shadowmapProjMatrix, false));
        Debug.Log(GL.GetGPUProjectionMatrix(shadowmapProjMatrix, true));
        Shader.SetGlobalMatrix("_LitViewMatrix",  shadowmapViewMatrix);
        Shader.SetGlobalMatrix("_LitProjMatrix", shadowmapProjMatrix);
        Shader.SetGlobalMatrix("_LitProjMatrixGPU", GL.GetGPUProjectionMatrix(shadowmapProjMatrix, false));
        //Shader.SetGlobalMatrix("_LitProjMatrixRT", GL.GetGPUProjectionMatrix(shadowmapProjMatrix, false));
        Shader.SetGlobalMatrix("_LitViewProjMatrix", shadowmapProjMatrix * shadowmapViewMatrix); // shadowCamera.worldToCameraMatrix); // shadowmapProjMatrix * shadowmapViewMatrix);
    }


    void bake()
    {
        //var viewPos = shadowCamera.worldToCameraMatrix.MultiplyPoint(new Vector3(0.592f, 29, 0.576f));
        //Debug.Log(viewPos);
        // shadowCamera.projectionMatrix;// 
        float orthoHalfSize = shadowCamera.orthographicSize = OrthoProjSize;
        Matrix4x4 shadowmapProjMatrix = Matrix4x4.Ortho(-orthoHalfSize, orthoHalfSize, -orthoHalfSize, orthoHalfSize, nearClip, farClip);// shadowCamera.farClipPlane);
        Matrix4x4 shadowmapViewMatrix = Matrix4x4.TRS(shadowCamera.transform.position, shadowCamera.transform.rotation, new Vector3(1,1, -1));
        //Debug.Log(shadowmapViewMatrix);
        //Debug.Log(shadowmapViewMatrix.inverse);
        //shadowmapViewMatrix = shadowmapViewMatrix.inverse;
        Debug.Log(shadowmapViewMatrix);
        Debug.Log(shadowCamera.worldToCameraMatrix);
        //Debug.Log(shadowCamera.worldToCameraMatrix);
        var cmb = new CommandBuffer();

        Material renderMat = new Material(Shader.Find("Unlit/Shadowmap"));
        if (renderMode == RenderMode.Shadowmap)
            renderMat.shader = Shader.Find("Unlit/Shadowmap");
        else if (renderMode == RenderMode.VoxelShadowmapDiff)
            renderMat.shader = Shader.Find("Unlit/ShadowmapDiff");
        cmb.DisableShaderKeyword("_ZCLIP");
        if (zClip)
        {
            cmb.EnableShaderKeyword("_ZCLIP");
        }
        else {
            cmb.DisableShaderKeyword("_ZCLIP");
        }
        renderMat.SetFloat("_startPlane", startPlane);
       

        // loopable
        cmb.SetViewProjectionMatrices(shadowmapViewMatrix, shadowmapProjMatrix); //shadowCamera.worldToCameraMatrix
        
        var allRenderer = FindObjectsOfType<Renderer>();
        var mpb = new MaterialPropertyBlock();
        //var constBuff = new ComputeBuffer(4, 4, ComputeBufferType.Constant); // .Constant  
        //constBuff.SetData<byte>(new List<byte>()
        //{
        //    255,0,0,255,
        //    0,255,0,255,
        //    0,0,255,255,
        //    0,255,255,255
        //});
        //Shader.SetGlobalConstantBuffer(Shader.PropertyToID("VxInfo"), constBuff, 0, 16);
        //constBuff.Release();
        // draw voxel depth

        if (renderMode == RenderMode.VoxelShadowmapDiff)
        {
            var _voxelShadowMapRT = Shader.PropertyToID("_voxelShadowMapRT");
            List<Texture2D> voxelTextures = new List<Texture2D>();
            var tmpRt = RenderTexture.GetTemporary(shadowMap.width, shadowMap.height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, 2);
            for (int i = 0, c = Mathf.CeilToInt(farClip / VoxelSize); i < c; i++)
            {
                float _miniPlane = 1 - (i + 1) * VoxelSize / (farClip - nearClip); // (i + 1) * VoxelSize;// 
                float _maxPlane = 1 - i * VoxelSize / (farClip - nearClip); //  i * VoxelSize;//
                Debug.Log(_maxPlane);
                renderMat.SetFloat("_miniPlane", _miniPlane);
                renderMat.SetFloat("_maxPlane", _maxPlane);
                renderMat.SetTexture("_shadowMap", shadowMap);
                renderMat.SetMatrix("_UNITY_MATRIX_P", shadowmapProjMatrix);
                //renderMat.SetMatrix("_UNITY_MATRIX_V", shadowmapViewMatrix);
                cmb.SetGlobalMatrix("_UNITY_MATRIX_P", shadowmapProjMatrix);
                //cmb.SetGlobalMatrix("_UNITY_MATRIX_V", shadowmapViewMatrix);
                cmb.SetGlobalFloat("_miniPlane", _miniPlane);
                cmb.Blit(tmpRt, tmpRt, renderMat);
                Graphics.ExecuteCommandBuffer(cmb);
                RenderTexture.active = tmpRt;
                Texture2D tex = new Texture2D(shadowMap.width, shadowMap.height, TextureFormat.RGBA32, false, true);
                tex.ReadPixels(new Rect(0, 0, shadowMap.width, shadowMap.height), 0, 0, false);
                tex.Apply(false, false);
                voxelTextures.Add(tex);
            }
            int texIdx = 0;
            voxelTextures.ForEach((tex) =>
            {
                AssetDatabase.CreateAsset(tex, string.Format("Assets/shadowmap/voxel_lv_{0}.asset", texIdx));
                texIdx++;
            });            
            cmb.Release();
        }
        else if (renderMode == RenderMode.None || renderMode == RenderMode.Shadowmap)
        {
            if(renderMode == RenderMode.Shadowmap)
            {
                cmb.SetRenderTarget(shadowMap);
                
                cmb.ClearRenderTarget(true, true, Color.black);
            }
            // draw scene
            for (int i = 0, c = allRenderer.Length; i < c; i++)
            {
                allRenderer[i].GetPropertyBlock(mpb);
                mpb.Clear();
                //mpb.SetVectorArray("_litShadowInfo", new Vector4[] {
                //    new Vector4(1,0,0,0),
                //    new Vector4(0,1,0,0),
                //    new Vector4(0,0,1,0),
                //    new Vector4(0,0,0,0),
                //});

                // mpb.SetConstantBuffer("UnityInstancing_VxInfo_VxInfo", constBuff, 0, 16);  // UnityInstancing_VxInfo VxShadowMapArray VxInfo

                if (bDrawmeshInstancingOrDrawRender)
                {
                    var meshFiter = allRenderer[i].GetComponent<MeshFilter>();
                    cmb.DrawMeshInstanced(meshFiter.sharedMesh, 0,
                        renderMode != RenderMode.None ? renderMat : allRenderer[i].sharedMaterial,
                        0, new Matrix4x4[] { allRenderer[i].transform.localToWorldMatrix, Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity }, 4,
                        mpb);
                }
                else
                {
                    allRenderer[i].SetPropertyBlock(mpb);
                    cmb.DrawRenderer(allRenderer[i], renderMode != RenderMode.None ? renderMat : allRenderer[i].sharedMaterial);

                }
            }


            Graphics.ExecuteCommandBuffer(cmb);
            cmb.Release();
            if (renderMode == RenderMode.Shadowmap)
            {
                RenderTexture.active = shadowMap;
                Texture2D tex = new Texture2D(shadowMap.width, shadowMap.height, TextureFormat.ARGB32, false, true);
                tex.ReadPixels(new Rect(0, 0, shadowMap.width, shadowMap.height), 0, 0);
                tex.Apply();
                AssetDatabase.CreateAsset(tex, "Assets/shadowmapAll.asset");
            }
        }

    } 

    struct BlockIndex
    {
        public int U;
        public int V;
    }

    public enum LitOrShadow
    {
        FullLit, 
        FullShadow,
        Intersected,
    }

    private void MultiCoreMemSetBlack(Color[] pixels)
    {
        object objLock = new object();
        int allFinished = 0;
        for (int i = 0; i < 8; i++)
        {
            int id = i;
            var start = pixels.Length / 8 * i;
            var end = pixels.Length / 8 * (i + 1);
            System.Threading.ThreadPool.QueueUserWorkItem((a) =>
            {
                for (int idx = start, c = end; idx < c; idx++)
                {
                    pixels[idx] = Color.black;
                }
                lock (objLock)
                {
                    allFinished |= 1 << id;
                }
            });
        }

        while ((allFinished & 0xff) != 0xff)
        {
            System.Threading.Thread.Sleep(30);
        }

    }

    // compute voxel on cpu 
    // compute lv3 lit or shadow info first, then summary to lv2 and rootLv1
    void precomputeVoxelDepth()
    {
        //List<Color32> map1Colors = new List<Color32>();
        //List<Color32> map2Colors = new List<Color32>();
        //List<Color32> map3COlors = new List<Color32>();
        //Dictionary<int, Dictionary<int, LitOrShadow>> litshadowInfo = new Dictionary<int, Dictionary<int, LitOrShadow>>();

        //Dictionary<BlockIndex, LitOrShadow> litShadowInfo = new Dictionary<BlockIndex, LitOrShadow>();

        //List<Texture2D> lv1Blocks = new List<Texture2D>();

        int rootVoxelSize = RootVoxelWidthSize;
        int lv2VoxelSize = rootVoxelSize * 2;
        int lv3VoxelSize = lv2VoxelSize * 2;
        int rootPixelPerVoxel = 0;
        int lv2PixelPerVoxel = 0;
        int lv3PixelPerVoxel = 0;

        //var allVoxelLitShadowInfo = AssetDatabase.LoadAllAssetsAtPath("Assets/shadowmap");
        var shadowMapWidth = shadowMap.width;
        rootPixelPerVoxel = shadowMapWidth / RootVoxelWidthSize;
        lv2PixelPerVoxel = rootPixelPerVoxel / 2;
        lv3PixelPerVoxel = lv2PixelPerVoxel / 2;

        // lv3VoxelBlockInfo 32 * 32 * 32 if root is 8*8*8 .   lv3 4*4*4 voxel == lv1 1*1*1
        int resultTextureSize = lv3VoxelSize;
        // int resultMaxBlockCount = 256 / lv3VoxelSize;
        // Texture2D litShadowInfoMap = new Texture2D(resultTextureSize, resultTextureSize, TextureFormat.ARGB32, false, true);
        Texture2DArray litShadowInfoArrayLv3 = new Texture2DArray(resultTextureSize, resultTextureSize, resultTextureSize, TextureFormat.RGBA32, false, true);
        Texture2DArray litShadowInfoArrayLv2 = new Texture2DArray(lv2VoxelSize, lv2VoxelSize, lv2VoxelSize, TextureFormat.RGBA32, false, true);
        Texture2DArray litShadowInfoArrayRoot = new Texture2DArray(rootVoxelSize, rootVoxelSize, rootVoxelSize, TextureFormat.RGBA32, false, true);

        // z-depth 
        for (int dVoxelIndex = 0, dVoxelMaxIndex = lv3VoxelSize; dVoxelIndex < dVoxelMaxIndex; dVoxelIndex++)
        {
            var voxelLitShadowInfo = AssetDatabase.LoadAssetAtPath<Texture2D>(string.Format("Assets/shadowmap/voxel_lv_{0}.asset", dVoxelIndex));
            if (voxelLitShadowInfo == null)
            {
                Debug.Log(string.Format("voxelLitShadowInfo {0} is not exist.", dVoxelIndex));
            }
            var tex = voxelLitShadowInfo;
            var texName = tex.name;
            //var voxelLitShadowInfoPixels = voxelLitShadowInfo.GetPixels32(0);

            #region pixel first
            // iter each pixel on cpu
            //for (int vPixel = 0, vPixelMax = shadowMapWidth; vPixel < vPixelMax; vPixel++)
            //{
            //    for (int uPixel = 0, uPixelMax = shadowMapWidth; uPixel < uPixelMax; uPixel++)
            //    {
            //        int u = uPixel / lv3PixelPerVoxel;
            //        int v = vPixel / lv3PixelPerVoxel;
            //        BlockIndex blockIndex = new BlockIndex() { U = u, V = v };
            //        litShadowInfo[blockIndex] = LitOrShadow.FullLit;
            //    }
            //}
            #endregion

            var blockPixels = litShadowInfoArrayLv3.GetPixels(dVoxelIndex, 0);
            for (int vBlockIndex = 0, vBlockIdxMax = lv3VoxelSize; vBlockIndex < vBlockIdxMax; vBlockIndex++)
            {
                for (int uBlockIndex = 0, uBlockIdxMax = lv3VoxelSize; uBlockIndex < uBlockIdxMax; uBlockIndex++)
                {
                    int uPixelBase = lv3PixelPerVoxel * uBlockIndex;
                    int vPixelBase = lv3PixelPerVoxel * vBlockIndex;

                    bool isBlockLit = true;
                    bool isBlockShadow = true;
                    for (int vPixelSub = 0, vPixelMax = lv3PixelPerVoxel; vPixelSub < lv3PixelPerVoxel; vPixelSub++)
                    {
                        for (int uPixelSub = 0, uPixelMax = lv3PixelPerVoxel; uPixelSub < lv3PixelPerVoxel; uPixelSub++)
                        {
                            int vPixel = vPixelBase + vPixelSub;
                            //vPixel = vBlockIndex * uBlockIdxMax * lv3PixelPerVoxel * lv3PixelPerVoxel;
                            int uPixel = uPixelBase + uPixelSub;
                            var pixel = voxelLitShadowInfo.GetPixel(uPixel, vPixel, 0);
                            var isWhite = Mathf.Abs(pixel.r - 1) < 0.1f;
                            var isBlack = Mathf.Abs(pixel.r - 0) < 0.1f;
                            var isGray = Mathf.Abs(pixel.r - 0.5f) < 0.1f;
                            isBlockLit &= isWhite;
                            isBlockShadow &= isBlack;
                        }
                    }

                    bool isBlockIntersection = !isBlockLit && !isBlockShadow;
                    var blockResult = (isBlockLit ? 1 : 0) + (isBlockIntersection ? 0.5f : 0);
                    blockPixels[vBlockIndex * uBlockIdxMax + uBlockIndex] = Color.white * blockResult;
                }
            }
            litShadowInfoArrayLv3.SetPixels(blockPixels, dVoxelIndex, 0);
            
        }

        litShadowInfoArrayLv3.Apply(false, false);
        if (bExportLvLitShadowInfoTexArray4Dbg)
            AssetDatabase.CreateAsset(litShadowInfoArrayLv3, "Assets/lightInfoArrayLv3.asset");

        // summary to lv2
        for (int dBlockIndex = 0, dBlockIdxMax = lv2VoxelSize; dBlockIndex < dBlockIdxMax; dBlockIndex++) {
            var lv2BlockPixels = litShadowInfoArrayLv2.GetPixels(dBlockIndex);
            var lv3BlockPixelsFront = litShadowInfoArrayLv3.GetPixels(2 * dBlockIndex);
            var lv3BlockPixelsBack = litShadowInfoArrayLv3.GetPixels(2 * dBlockIndex + 1);
            for (int vBlockIndex = 0, vBlockIdxMax = lv2VoxelSize; vBlockIndex < vBlockIdxMax; vBlockIndex++)
            {
                for (int uBlockIndex = 0, uBlockIdxMax = lv2VoxelSize; uBlockIndex < uBlockIdxMax; uBlockIndex++)
                {
                    int uPixelBase = 2 * uBlockIndex;
                    int vPixelBase = 2 * vBlockIndex;
                    // voxel : 2*2*2 lv3
                    bool isVoxelLited = true;
                    bool isVoxelShadowed = true;
                    for (int vPixelSub = 0, vPixelMax = 2; vPixelSub < vPixelMax; vPixelSub++)
                    {
                        for (int uPixelSub = 0, uPixelMax = 2; uPixelSub < uPixelMax; uPixelSub++)
                        {
                            int vPixel = vPixelBase + vPixelSub;
                            int uPixel = uPixelBase + uPixelSub;
                            var pixelFront = lv3BlockPixelsFront[vPixel * lv3VoxelSize + uPixel];
                            var pixelBack = lv3BlockPixelsBack[vPixel * lv3VoxelSize + uPixel];
                            Vector3 colorVec = (Vector4)pixelFront;
                            Vector3 colorVecBack = (Vector4)pixelBack;
                            var isWhite = Mathf.Abs(pixelFront.r - 1) < 0.1f;
                            var isWhiteBack = Mathf.Abs(pixelBack.r - 1) < 0.1f;
                            var isBlack = Mathf.Abs(pixelFront.r - 0) < 0.1f;
                            var isBlackBack = Mathf.Abs(pixelBack.r - 0) < 0.1f;
                            var isGray = Mathf.Abs(pixelFront.r - 0.5f) < 0.1f;
                            var isGrayBack = Mathf.Abs(pixelBack.r - 0.5f) < 0.1f;
                            isVoxelLited &= isWhite && isWhiteBack;
                            isVoxelShadowed &= isBlack && isBlackBack;
                        }
                    }
                    bool isBlockIntersection = !isVoxelLited && !isVoxelShadowed;
                    var blockResult = (isVoxelLited ? 1 : 0) + (isBlockIntersection ? 0.5f : 0);

                    lv2BlockPixels[vBlockIndex * uBlockIdxMax + uBlockIndex] = Color.white * blockResult;
                }
            }
            litShadowInfoArrayLv2.SetPixels(lv2BlockPixels, dBlockIndex, 0);
        }

        litShadowInfoArrayLv2.Apply(false, false);
        if (bExportLvLitShadowInfoTexArray4Dbg)
            AssetDatabase.CreateAsset(litShadowInfoArrayLv2, "Assets/lightInfoArrayLv2.asset");

        // summary to root
        for (int dBlockIndex = 0, dBlockIdxMax = rootVoxelSize; dBlockIndex < dBlockIdxMax; dBlockIndex++)
        {
            var lv1BlockPixels = litShadowInfoArrayRoot.GetPixels(dBlockIndex);
            var lv2BlockPixelsFront = litShadowInfoArrayLv2.GetPixels(2 * dBlockIndex);
            var lv2BlockPixelsBack = litShadowInfoArrayLv2.GetPixels(2 * dBlockIndex + 1);
            for (int vBlockIndex = 0, vBlockIdxMax = rootVoxelSize; vBlockIndex < vBlockIdxMax; vBlockIndex++)
            {
                for (int uBlockIndex = 0, uBlockIdxMax = rootVoxelSize; uBlockIndex < uBlockIdxMax; uBlockIndex++)
                {
                    int uPixelBase = 2 * uBlockIndex;
                    int vPixelBase = 2 * vBlockIndex;
                    // voxel : 2*2*2 lv3
                    bool isVoxelLited = true;
                    bool isVoxelShadowed = true;
                    for (int vPixelSub = 0, vPixelMax = 2; vPixelSub < vPixelMax; vPixelSub++)
                    {
                        for (int uPixelSub = 0, uPixelMax = 2; uPixelSub < uPixelMax; uPixelSub++)
                        {
                            int vPixel = vPixelBase + vPixelSub;
                            int uPixel = uPixelBase + uPixelSub;
                            var pixelFront = lv2BlockPixelsFront[vPixel * lv2VoxelSize + uPixel];
                            var pixelBack = lv2BlockPixelsBack[vPixel * lv2VoxelSize + uPixel];
                            var isWhite = Mathf.Abs(pixelFront.r - 1) < 0.1f;
                            var isWhiteBack = Mathf.Abs(pixelBack.r - 1) < 0.1f;
                            var isBlack = Mathf.Abs(pixelFront.r - 0) < 0.1f;
                            var isBlackBack = Mathf.Abs(pixelBack.r - 0) < 0.1f;
                            var isGray = Mathf.Abs(pixelFront.r - 0.5f) < 0.1f;
                            var isGrayBack = Mathf.Abs(pixelBack.r - 0.5f) < 0.1f;
                            isVoxelLited &= isWhite && isWhiteBack;
                            isVoxelShadowed &= isBlack && isBlackBack;
                        }
                    }
                    bool isBlockIntersection = !isVoxelLited && !isVoxelShadowed;
                    var blockResult = (isVoxelLited ? 1 : 0) + (isBlockIntersection ? 0.5f : 0);
                    lv1BlockPixels[vBlockIndex * uBlockIdxMax + uBlockIndex] = Color.white * blockResult;
                }
                   
            }
            litShadowInfoArrayRoot.SetPixels(lv1BlockPixels, dBlockIndex, 0);
            
        }
        litShadowInfoArrayRoot.Apply(false, false);
        if (bExportLvLitShadowInfoTexArray4Dbg)
            AssetDatabase.CreateAsset(litShadowInfoArrayRoot, "Assets/lightInfoArrayLv1.asset");


        // calculate intersected lv1 voxel count, to construct a lv2 info map
        int lv1IntersectedCount = 0;
        for(int dVoxelIndex=0,dVoxelMax = rootVoxelSize; dVoxelIndex < dVoxelMax; dVoxelIndex++)
        {
            var litShadowInfoLv1 = litShadowInfoArrayRoot.GetPixels(dVoxelIndex);
            for(int pixelIdx = 0,pixelMax= litShadowInfoLv1.Length; pixelIdx < pixelMax; pixelIdx++)
            {
                Vector4 value = litShadowInfoLv1[pixelIdx];
                if(Mathf.Abs(value.x - 0.5f) < 0.1f)
                {
                    lv1IntersectedCount++;
                }
            }
        }

        int textureArraySize = Mathf.NextPowerOfTwo(lv1IntersectedCount / 32 + (lv1IntersectedCount % 32 > 0 ? 1 : 0));

        lv1IntersectedCount = Mathf.IsPowerOfTwo(lv1IntersectedCount) ? lv1IntersectedCount : Mathf.NextPowerOfTwo(lv1IntersectedCount);

        // shipping Lit shadow info to realtime rendering texture
        // head start from level 1 info
        // format-----  

        int litShaowInfoIndexMapSizeOrig = Mathf.RoundToInt(Mathf.Sqrt(rootVoxelSize * rootVoxelSize * rootVoxelSize));
        int litShadowInfoIndexMapSize = Mathf.IsPowerOfTwo(litShaowInfoIndexMapSizeOrig ) ? litShaowInfoIndexMapSizeOrig : Mathf.NextPowerOfTwo(litShaowInfoIndexMapSizeOrig);
        //int litShadowInfoMapLength = Mathf.RoundToInt(Mathf.Pow(rootVoxelSize, 3));
        // level1 index
        Texture2D litShadowInfoIndexMap = new Texture2D(litShadowInfoIndexMapSize, litShadowInfoIndexMapSize, TextureFormat.RGBA32, false, true);
        Texture2D litShadowInfoIndexMapNoTextureArray = new Texture2D(litShadowInfoIndexMapSize, litShadowInfoIndexMapSize, TextureFormat.RGBA32, false, true);

        // encode lit shadow info to texture2d
        Texture2D litShadowInfoMap = new Texture2D(32, lv1IntersectedCount, TextureFormat.RGBA32, false, true);
        Texture2DArray litShadowInfoMapArray = new Texture2DArray(32, 32, textureArraySize, TextureFormat.RGBA32, false, true);
        Texture2D litShadowInfoMapLv3 = new Texture2D(32, lv1IntersectedCount, TextureFormat.RGBA32, false, true);
        Texture2DArray litShaodwInfoMapArrayLv3 = new Texture2DArray(32, 32, textureArraySize, TextureFormat.RGBA32, false, true);
        
        //var tmp = RenderTexture.GetTemporary(litShadowInfoMap.width, litShadowInfoMap.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, 2);
        //Graphics.Blit(Texture2D.blackTexture, tmp);
        //Graphics.CopyTexture(tmp, litShadowInfoMap);

        var indexPixels = litShadowInfoIndexMap.GetPixels(0);
        var pixels = litShadowInfoMap.GetPixels(0);
        var indexPixelsNoTexArrayPixels = litShadowInfoIndexMapNoTextureArray.GetPixels(0);

        MultiCoreMemSetBlack(pixels);
        litShadowInfoMap.SetPixels(pixels);

        var pixelsLv3 = litShadowInfoMapLv3.GetPixels(0);
        MultiCoreMemSetBlack(pixelsLv3);
        litShadowInfoMapLv3.SetPixels(pixelsLv3);

        MultiCoreMemSetBlack(indexPixels);
        litShadowInfoIndexMap.SetPixels(indexPixels);

        MultiCoreMemSetBlack(indexPixelsNoTexArrayPixels);
        litShadowInfoIndexMapNoTextureArray.SetPixels(indexPixelsNoTexArrayPixels);

        for(int texArrayDepth=0,maxDepth = litShadowInfoMapArray.depth;texArrayDepth < maxDepth; texArrayDepth++)
        {
            var pixels1 = litShadowInfoMapArray.GetPixels(texArrayDepth, 0);
            MultiCoreMemSetBlack(pixels1);
            litShadowInfoMapArray.SetPixels(pixels1, texArrayDepth);
            var pixels2 = litShaodwInfoMapArrayLv3.GetPixels(texArrayDepth, 0);
            MultiCoreMemSetBlack(pixels2);
            litShaodwInfoMapArrayLv3.SetPixels(pixels2, texArrayDepth);
        }

        litShadowInfoMapArray.Apply();



        litShadowInfoMap.Apply();
        //AssetDatabase.CreateAsset(litShadowInfoMap, "Assets/black.asset");


        // bake lit shadow info to texture
        int queryIdx = 0;
        for (int dVoxelIndex = 0, dVoxelMax = rootVoxelSize; dVoxelIndex < dVoxelMax; dVoxelIndex++)
        {
            var litShadowInfoLv1 = litShadowInfoArrayRoot.GetPixels(dVoxelIndex);

            //for (int dVoxelIndexLv2 = 0, dVoxelMaxLv2 = 2; dVoxelIndexLv2 < dVoxelMaxLv2; dVoxelIndexLv2++)
            //{
                var litShadowInfoLv2_front = litShadowInfoArrayLv2.GetPixels(2 * dVoxelIndex);
                var litShadowInfoLv2_back = litShadowInfoArrayLv2.GetPixels(2 * dVoxelIndex + 1);
            //}
            var litShadowInfoLv3_front = litShadowInfoArrayLv3.GetPixels(4 * dVoxelIndex);
            var litShadowInfoLv3_mid1 = litShadowInfoArrayLv3.GetPixels(4 * dVoxelIndex + 1);
            var litShadowInfoLv3_mid2 = litShadowInfoArrayLv3.GetPixels(4 * dVoxelIndex + 2);
            var litShadowInfoLv3_back = litShadowInfoArrayLv3.GetPixels(4 * dVoxelIndex + 3);
            for (int vVoxelIndex = 0, vVoxelMax = rootVoxelSize; vVoxelIndex < vVoxelMax; vVoxelIndex++)
            {
                for (int uVoxelIndex = 0, uVoxelMax = rootVoxelSize; uVoxelIndex < uVoxelMax; uVoxelIndex++)
                {
                    var lv1 = litShadowInfoLv1[vVoxelIndex * uVoxelMax + uVoxelIndex];
                    var lvIndexMapIndex = dVoxelIndex * vVoxelMax * uVoxelMax + vVoxelIndex * uVoxelMax + uVoxelIndex;
                    var lvIndexMapY = lvIndexMapIndex / litShadowInfoIndexMapSize;
                    var lvIndexMapX = lvIndexMapIndex % litShadowInfoIndexMapSize;
                    // if lv1 voxel is intersected
                    if (Mathf.Abs(lv1.r - 0.5f) < 0.1f)
                    {
                        // litShadowInfoMap.SetPixel(0, queryIdx, new Color(lv1.r, 0,0,0 ), 0);
                        int seq = 0;
                        int lv2MemLocBase = (vVoxelIndex * uVoxelMax + uVoxelIndex) * 4;
                        //var lv2FrontRGBA = new Color();
                        //var lv2BackRGBA = new Color();

                        for (int vPixelIndex = 0, vPixelMax = 2; vPixelIndex < vPixelMax; vPixelIndex++)
                        {
                            for (int uPixelIndex = 0, uPixelMax = 2; uPixelIndex < uPixelMax; uPixelIndex++)
                            {
                                int vFinal = (vVoxelIndex * uVoxelMax * 4) + uVoxelIndex * 2 + vPixelIndex * uVoxelMax * 2 + uPixelIndex;
                                var lv2_front = Color.black;
                                var lv2_back = Color.black;
                                try
                                {
                                    lv2_front = litShadowInfoLv2_front[vFinal];
                                    lv2_back = litShadowInfoLv2_back[vFinal];
                                }
                                catch(System.IndexOutOfRangeException e)
                                {
                                    Debug.Log(vFinal);
                                    Debug.Log(litShadowInfoLv2_front.Length);
                                }
                                Color frontColor = lv2_front;
                                Color backColor = lv2_back;

                                if ( ((Mathf.Abs(frontColor.r - 1) < 0.1f && Mathf.Abs(backColor.r - 1) < 0.1f) ||
                                    (Mathf.Abs(frontColor.r - 0) < 0.1f && Mathf.Abs(backColor.r - 0) < 0.1f)))
                                {

                                    Color colorLv3 = new Color(frontColor.r, frontColor.r, frontColor.r, frontColor.r);
                                    for (int vPixelIndexLv3 = 0, vPixelMaxLv3 = 2; vPixelIndexLv3 < vPixelMaxLv3; vPixelIndexLv3++)
                                    {
                                        for (int uPixelIndexLv3 = 0, uPixelMaxLv3 = 2; uPixelIndexLv3 < uPixelMaxLv3; uPixelIndexLv3++)
                                        {
                                            int pixelIdx = 8 * vPixelIndex + uPixelIndex * 4 + 2 * vPixelIndexLv3 + uPixelIndexLv3;
                                            litShadowInfoMapLv3.SetPixel(pixelIdx, queryIdx, colorLv3, 0);
                                        }
                                    }
                                }
                                else
                                {
                                    //lv2FrontRGBA[vPixelIndex * uPixelMax + uPixelIndex] = lv2_front.r;
                                    //lv2BackRGBA[vPixelIndex * uPixelMax + uPixelIndex] = lv2_back.r;

                                    // TODO Lv3 info
                                    //float lv3TexV =
                                    bool[] axeAllLitFront = new bool[4] { true, true, true, true };
                                    bool[] axeAllShadowFront = new bool[4] { true, true, true, true };
                                    bool[] axeIntersectedFront = new bool[4] { true, true, true, true };
                                    bool[] axeAllLitBack = new bool[4] { true, true, true, true };
                                    bool[] axeAllShadowBack = new bool[4] { true, true, true, true };
                                    bool[] axeIntersectedBack = new bool[4] { true, true, true, true };

                                    for (int vPixelIndexLv3 = 0, vPixelMaxLv3 = 2; vPixelIndexLv3 < vPixelMaxLv3; vPixelIndexLv3++)
                                    {
                                        for (int uPixelIndexLv3 = 0, uPixelMaxLv3 = 2; uPixelIndexLv3 < uPixelMaxLv3; uPixelIndexLv3++)
                                        {
                                            int finalLv3Y = vVoxelIndex * 4 + vPixelIndex * 2 + vPixelIndexLv3;
                                            int finalLv3X = uVoxelIndex * 4 + uPixelIndex * 2 + uPixelIndexLv3;
                                            //  4 * uPixelMax  * uVoxelMax * vPixelIndex   + 4 * uVoxelIndex 
                                            int vFinalLv3 = 4 * uVoxelMax * finalLv3Y + finalLv3X; //(vVoxelIndex * uVoxelMax * 16) +  4 * (1 - vPixelIndex) + 8 * uVoxelMax * vPixelIndex + 4 * uVoxelIndex + uPixelIndex * 2 + uPixelMax * uPixelMaxLv3 * uVoxelMax * vPixelIndexLv3 + uPixelIndexLv3;
                                            var lv3_front = litShadowInfoLv3_front[vFinalLv3];
                                            var lv3_mid1 = litShadowInfoLv3_mid1[vFinalLv3];
                                            var lv3_mid2 = litShadowInfoLv3_mid2[vFinalLv3];
                                            var lv3_back = litShadowInfoLv3_back[vFinalLv3];
                                            bool allLitFront = axeAllLitFront[vPixelIndexLv3 * uPixelMaxLv3 + uPixelIndexLv3] = Mathf.Abs(lv3_front.r - 1) < 0.1f 
                                                && Mathf.Abs(lv3_mid1.r - 1) < 0.1f;
                                            bool allShadowFront = axeAllShadowFront[vPixelIndexLv3 * uPixelMaxLv3 + uPixelIndexLv3] = Mathf.Abs(lv3_front.r - 0) < 0.1f &&
                                                Mathf.Abs(lv3_mid1.r - 0) < 0.1f;
                                            axeIntersectedFront[vPixelIndexLv3 * uPixelMaxLv3 + uPixelIndexLv3] = !allLitFront && !allShadowFront;
                                            bool allLitBack = axeAllLitBack[vPixelIndexLv3 * uPixelMaxLv3 + uPixelIndexLv3] = Mathf.Abs(lv3_mid2.r - 1) < 0.1f &&
                                                Mathf.Abs(lv3_back.r - 1) < 0.1f;
                                            bool allShadowBack = axeAllShadowBack[vPixelIndexLv3 * uPixelMaxLv3 + uPixelIndexLv3] = Mathf.Abs(lv3_mid2.r - 0) < 0.1f &&
                                                Mathf.Abs(lv3_back.r - 0) < 0.1f;
                                            axeIntersectedBack[vPixelIndexLv3 * uPixelMaxLv3 + uPixelIndexLv3] = !allLitBack && !allShadowBack;

                                            Color colorLv3 = new Color(lv3_front.r, lv3_mid1.r, lv3_mid2.r, lv3_back.r);
                                            int pixelIdx = 8 * vPixelIndex + uPixelIndex * 4 + 2 * vPixelIndexLv3 + uPixelIndexLv3;
                                            litShadowInfoMapLv3.SetPixel(pixelIdx, queryIdx, colorLv3, 0);
                                            
                                        }
                                    }

                                    frontColor = new Color(axeAllLitFront[0] ? 1.0f : 0.0f + (axeAllShadowFront[0] ? 0.0f : 1.0f) + (axeIntersectedFront[0] ? 0.5f : 0),
                                        axeAllLitFront[1] ? 1 : 0 + (axeIntersectedFront[1] ? 0.5f : 0),
                                        axeAllLitFront[2] ? 1 : 0 + (axeIntersectedFront[2] ? 0.5f : 0),
                                        axeAllLitFront[3] ? 1 : 0 + (axeIntersectedFront[3] ? 0.5f : 0));

                                    //for(int i=0;i< 4; i++)
                                    //{
                                    //    frontColor[i] = axeIntersectedFront[i] ? 0.5f : (axeAllLitFront[i] ? 1.0f : 0.0f);
                                    //}
                                    backColor = new Color(axeAllLitBack[0] ? 1 : 0 + (axeAllShadowBack[0] ? 0 : 1) + (axeIntersectedBack[0] ? 0.5f : 0),
                                        axeAllLitBack[1] ? 1 : 0 +  (axeIntersectedBack[1] ? 0.5f : 0),
                                        axeAllLitBack[2] ? 1 : 0 +  (axeIntersectedBack[2] ? 0.5f : 0),
                                        axeAllLitBack[3] ? 1 : 0 +  (axeIntersectedBack[3] ? 0.5f : 0));
                                }

                                int seqFront = vPixelIndex * uPixelMax + uPixelIndex;
                                    int seqBack = seqFront + 4;

                                // frontColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                                // backColor = frontColor;

                                // debug
                                //frontColor = new Color(1, 1, 1, 1);
                                //backColor = new Color(1, 1, 1, 1);

                                litShadowInfoMap.SetPixel(seqFront, queryIdx, frontColor, 0);
                                litShadowInfoMap.SetPixel(seqBack, queryIdx, backColor, 0);


                            }
                        }

                        float texV = (queryIdx % 32) / 32.0f;  //(queryIdx % (lv1IntersectedCount * 0.5f))  / ((float)lv1IntersectedCount); //queryIdx / (float)lv1IntersectedCount;  //
                        Vector4 v = EncodeFloatRG(texV);
                        
                        float arrayIndex = (queryIdx / 32) / (float)textureArraySize;
                        // R: lit or Shadow GB: v A : textureArray index  setup after lv2 info summarized
                        litShadowInfoIndexMap.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.r, arrayIndex, v.x, v.y));// //new Color(lv1.r, v.x, v.y, isHalf), 0);

                        texV = (queryIdx % (lv1IntersectedCount / 2)) / (float)lv1IntersectedCount;
                        float isHalf = queryIdx > lv1IntersectedCount / 2 ? 1 : 0;
                        v = EncodeFloatRG(texV);
                        litShadowInfoIndexMapNoTextureArray.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.r, isHalf, v.x, v.y));

                        //for (int i = 0; i < 16; i++)
                        //{
                        //    litShadowInfoMap.SetPixel(seq, queryIdx, lv2FrontRGBA, 0);
                        //    seq++;
                        //    litShadowInfoMap.SetPixel(seq, queryIdx, lv2BackRGBA, 0);
                        //    seq++;
                        //}

                        // lv3
                        /*
                        for(int vPixelIndex = 0,vPixelMax = 4; vPixelIndex < vPixelMax; vPixelIndex++)
                        {
                            for (int uPixelIndex = 0, uPixelMax = 4; uPixelIndex < uPixelMax; uPixelIndex++)
                            {
                                var lv3_front = litShadowInfoLv3_front[vPixelIndex * uPixelMax + uPixelIndex];
                                var lv3_mid1 = litShadowInfoLv3_mid1[vPixelIndex * uPixelMax + uPixelIndex];
                                var lv3_mid2 = litShadowInfoLv3_mid2[vPixelIndex * uPixelMax + uPixelIndex];
                                var lv3_back = litShadowInfoLv3_back[vPixelIndex * uPixelMax + uPixelIndex];
                            }
                        }
                        */

                        queryIdx++;
                    }
                    else
                    {
                        Vector4 v = EncodeFloatRG(lv1.r > 0.9 ? 20000 : 10000);
                        litShadowInfoIndexMap.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.r, 0, 0, 0), 0);
                        litShadowInfoIndexMapNoTextureArray.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.r, 0, 0, 0), 0);
                    }
                }
            }


            //AssetDatabase.DeleteAsset("Assets/runtimeLitShadowInfo.asset");
            //AssetDatabase.CreateAsset(litShadowInfoMap, "Assets/runtimeLitShadowInfo.asset");
        }
        //AssetDatabase.DeleteAsset("Assets/litShadowInfoArray.asset");

        litShadowInfoIndexMap.Apply(false, false);
        litShadowInfoIndexMapNoTextureArray.Apply(false, false);
        litShadowInfoMap.Apply(false, false);
        litShadowInfoMapLv3.Apply(false, false);

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        int scaleLv2Fact = 32;
        // scaled lv2 textureArray
        Texture2DArray scaledTextureArray = new Texture2DArray(scaleLv2Fact * litShadowInfoMapArray.width,
            scaleLv2Fact * litShadowInfoMapArray.height,
            litShadowInfoMapArray.depth,
            TextureFormat.RGBA32, false,
            true);

        int blockMaxIdx = litShadowInfoMap.height / 32 + (litShadowInfoMap.height % 32 > 0 ? 1:0);
        for (int blockIdx = 0; blockIdx < blockMaxIdx; blockIdx++)
        {
            var colorBlock = litShadowInfoMap.GetPixels(0, 32 * blockIdx, 32, 32, 0);
            litShadowInfoMapArray.SetPixels(colorBlock, blockIdx, 0);
#if _ENABLE_LV3_MODE
            var colorBlockLv3 = litShadowInfoMapLv3.GetPixels(0, 32 * blockIdx, 32, 32, 0);
            litShaodwInfoMapArrayLv3.SetPixels(colorBlockLv3, blockIdx, 0);
#endif
            var colorBlockScaled = scaledTextureArray.GetPixels(blockIdx);
            int colIdx64 = 0;
            int colIdx32 = 0;
            try
            {
               
                for (int vIdx = 0, vMax = scaledTextureArray.width; vIdx < vMax; vIdx++)
                {
                    for (int uIdx = 0, uMax = scaledTextureArray.height; uIdx < uMax; uIdx++)
                    {
                        colIdx64 = vIdx * uMax + uIdx;
                        colIdx32 = uMax / scaleLv2Fact * (vIdx / scaleLv2Fact) + uIdx / scaleLv2Fact;

                        colorBlockScaled[colIdx64] = colorBlock[colIdx32];
                    }

                }
            }
            catch (System.IndexOutOfRangeException e)
            {
                Debug.Log(string.Format("colIdx64:{0} colIdx32:{1}", colIdx64, colIdx32));
            }
            scaledTextureArray.SetPixels(colorBlockScaled, blockIdx, 0);
        }
        scaledTextureArray.Apply(false, false);
        AssetDatabase.CreateAsset(scaledTextureArray, "Assets/litShadowInfoArrayScaled.asset");
        
        litShaodwInfoMapArrayLv3.Apply(false, false);
        AssetDatabase.CreateAsset(litShaodwInfoMapArrayLv3, "Assets/litShaodwInfoMapArrayLv3.asset");

        int scaleFact = 16;
        Texture2D scaledLv1Texture = new Texture2D(litShadowInfoIndexMap.width * scaleFact, litShadowInfoIndexMap.height * scaleFact, TextureFormat.RGBA32, false, true);
        var colorBlockOriginLv1 = litShadowInfoIndexMap.GetPixels(0);
        var colorBlockScaledLv1 = scaledLv1Texture.GetPixels(0);
        for (int vIdx=0,vIdxMax = scaledLv1Texture.height; vIdx < vIdxMax; vIdx++)
        {
            for(int uIdx = 0,uIdxMax = scaledLv1Texture.width; uIdx< uIdxMax; uIdx++)
            {
                int colIdxLarge = vIdx * uIdxMax + uIdx;
                int colIdxSmall = vIdx / scaleFact * uIdxMax / scaleFact + uIdx / scaleFact;
                colorBlockScaledLv1[colIdxLarge] = colorBlockOriginLv1[colIdxSmall];
            }
        }
        scaledLv1Texture.SetPixels(colorBlockScaledLv1, 0);
        scaledLv1Texture.Apply(true, true);
        AssetDatabase.CreateAsset(scaledLv1Texture, "Assets/litShadowInfoLv1_Scaled.asset");
        //litShadowInfoMapArray.SetPixels(arrayColors, arrayIdx + 1);

        litShadowInfoMapArray.Apply(false, false);
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        if (litShadowInfoMapArray != null)
        {
            AssetDatabase.CreateAsset(litShadowInfoMapArray, "Assets/litShadowInfoArray.asset");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = TextureImporter.GetAtPath("Assets/litShadowInfoArray.asset") as AssetImporter;
            //importer.filterMode = FilterMode.Point;
            //importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }


        System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv1.tga", litShadowInfoIndexMap.EncodeToTGA());
        System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv1.png", litShadowInfoIndexMap.EncodeToPNG());

        System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv1NoTexArray.tga", litShadowInfoIndexMapNoTextureArray.EncodeToTGA());

        System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv2.tga", litShadowInfoMap.EncodeToTGA());
        System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv2.png", litShadowInfoMap.EncodeToPNG());


        //var cmb = CommandBufferPool.Get();
        //Graphics.ExecuteCommandBuffer(cmb);
    }

    private Vector4 EncodeFloatRGB(float v)
    {
        Vector3 kEncodeMul = new Vector4(1.0f, 255.0f, 65025.0f);
        float kEncodeBit = 1.0f / 255.0f;
        Vector3 enc = kEncodeMul * v;
        enc = new Vector3(enc.x - Mathf.Floor(enc.x),
            enc.y - Mathf.Floor(enc.y),
            enc.z - Mathf.Floor(enc.z));
        enc -= new Vector3(enc.y, enc.z, enc.z) * kEncodeBit;
        return enc;
    }

    private Vector4 EncodeFloatRGBA(float v)
    {
        Vector4 kEncodeMul = new Vector4(1.0f, 255.0f, 65025.0f, 16581375.0f);
        float kEncodeBit = 1.0f / 255.0f;
        Vector4 enc = kEncodeMul * v;
        enc = new Vector4(enc.x - Mathf.Floor(enc.x),
            enc.y - Mathf.Floor(enc.y),
            enc.z - Mathf.Floor(enc.z),
            enc.w - Mathf.Floor(enc.w));
        enc -= new Vector4(enc.y, enc.z, enc.w, enc.w) * kEncodeBit;
        return enc;
    }
    private Vector2 EncodeFloatRG(float v)
    {
        Vector2 kEncodeMul = new Vector2(1.0f, 255.0f);
        float kEncodeBit = 1.0f / 255.0f;
        Vector2 enc = kEncodeMul * v;
        //Vector2 enc2 = new Vector2(enc.x % 1, enc.y % 1);
        Vector2 enc2 = new Vector2(enc.x - Mathf.Floor(enc.x), enc.y - Mathf.Floor(enc.y));
        enc = enc2;
        enc.x -= enc.y * kEncodeBit;
        return enc;
    }

    [MenuItem("Tools/TestCopyTex")]
    public static void Test()
    {
        Texture2DArray texArray = new Texture2DArray(Texture2D.blackTexture.width, Texture2D.blackTexture.height, 32, TextureFormat.RGBA32, false, true);
        texArray.SetPixels(Texture2D.blackTexture.GetPixels(), 0);
        texArray.Apply();
        AssetDatabase.CreateAsset(texArray, "Assets/texArrayTest.asset");

        return;
        Texture2D tex = Selection.activeObject as Texture2D;
        Texture2D texCopy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, true);
        for (int v = 0, vMax = tex.height; v < vMax; v++)
         
        {
            for (int u = 0, uMax = tex.width; u < uMax; u++)
            {
                var color = tex.GetPixel(u, v, 0);
                color *=  v / (float)tex.height;
                texCopy.SetPixel(u, v, color);
            }
        }
        texCopy.Apply();
        AssetDatabase.CreateAsset(texCopy, "Assets/texCopy.asset");
    }
}
