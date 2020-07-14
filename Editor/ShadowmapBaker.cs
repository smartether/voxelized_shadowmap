#define _ENABLE_LV3_MODE
//#define _GEN_SCALED_TEX
#define _ENABLE_BIG_TEX
//#define _LV3_OLD_MODE
#define _LZ4_COMPRESS_
 #define _MEMMAP_
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering; 
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public partial class ShadowmapBaker : UnityEditor.EditorWindow
{
    public enum RenderMode
    {
        None = 0,
        Shadowmap = 1,
        ShadowmapLite = 5,
        VoxelShadowmapDiff = 12, // diff depth between front depth and final depth for each voxel 
        //VoxelShadowmapSliced = 13, // shadowmap render with each depth
        //VoxelShadowmapDepthDiff = 14,
        //VoxelShadowmapStrip = 15,
        //VoxelShadowmapAnd = 16,  // sum pixel info on gpu
        //VoxelShadowmapMaxKernel = 17, // compute litOrShadowInfo for each voxel
    }

    [MenuItem("Tools/DebugViewCamera")]
    static void DebugViewCamera()
    {
        //Selection.activeGameObject = UnityEditor.SceneView.lastActiveSceneView.camera.gameObject;
        var litMat = Selection.activeObject as Material;
        var viewCamera = UnityEditor.SceneView.lastActiveSceneView.camera;
        Debug.Log(viewCamera.name);
        CommandBuffer cmd = new CommandBuffer();
        cmd.GetTemporaryRT(Shader.PropertyToID("_VxShadow"), viewCamera.pixelWidth, viewCamera.pixelHeight, 16, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        cmd.SetViewProjectionMatrices(viewCamera.worldToCameraMatrix, viewCamera.projectionMatrix);
        viewCamera.SetReplacementShader(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ShadowmapBaker/Resources/VxRender.shader"), "");
        viewCamera.Render();
    }

    static ShadowmapBaker _instance;
    [MenuItem("GLA/ShadowmapBakerEditorTool")]
    static void Create()
    {
        if (_instance != null)
        {
            (_instance as EditorWindow).Close();
            _instance = null;
        }
        _instance = CreateInstance<ShadowmapBaker>();
        _instance.Show();
    }


    // Rendering

    static void SetParam(UnityEngine.Object uobj, [System.Runtime.CompilerServices.CallerMemberName]string name = "")
    {
        string guid;
        long loc;
        if (uobj != null)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(uobj, out guid, out loc);
            EditorPrefs.SetString(string.Format("Sdmbk:{0}_{1}", name, UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name), guid);
        }
    }
    static T GetParam<T>(T uobj, [System.Runtime.CompilerServices.CallerMemberName]string name = "") where T : UnityEngine.Object
    {
        string guid;
        long loc;
        if (uobj != null)
            return uobj;
        var prefName = EditorPrefs.GetString(string.Format("Sdmbk:{0}_{1}", name, UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name), "");
        if (!string.IsNullOrEmpty(prefName))
        {
            var path = AssetDatabase.GUIDToAssetPath(prefName);
            uobj = AssetDatabase.LoadAssetAtPath<T>(path);
            //Debug.Log(uobj.name);
            return uobj;
        }
        return null;
    }

    public Transform shadowCamera;
    public RenderTexture _shadowMap;
    public RenderTexture shadowMap
    {
        get
        {
            _shadowMap = GetParam<RenderTexture>(_shadowMap);
            return _shadowMap;
        }
        set
        {
            _shadowMap = value;
            SetParam(_shadowMap);
        }
    }

    public Texture2D _shadowmapTex;
    public Texture2D shadowmapTex
    {
        get
        {
            _shadowmapTex = GetParam<Texture2D>(_shadowmapTex);
            return _shadowmapTex;
        }
        set
        {
            _shadowmapTex = value;
            SetParam(_shadowmapTex);
        }
    }
    public Texture2D _shadowmapLite;
    public Texture2D shadowmapLite
    {
        get
        {
            _shadowmapLite = GetParam<Texture2D>(_shadowmapLite);
            return _shadowmapLite;
        }
        set
        {
            _shadowmapLite = value;
            SetParam(_shadowmapLite);
        }
    }
    public Texture2D _shadowmapLite1;
    public Texture2D shadowmapLite1
    {
        get
        {
            _shadowmapLite1 = GetParam<Texture2D>(_shadowmapLite1);
            return _shadowmapLite1;
        }
        set
        {
            _shadowmapLite1 = value;
            SetParam(_shadowmapLite1);
        }
    }
    public static Material _litMaterial;
    public static Material litMaterial
    {
        get
        {
            _litMaterial = GetParam<Material>(_litMaterial);
            return _litMaterial;
        }
        set
        {
            _litMaterial = value;
            SetParam(_litMaterial);
        }
    }
    float startPlane = 0;
    bool zClip = false;
    bool bSetTopIntersectedVoxelLit = true;
    bool bExportLvLitShadowInfoTexArray4Dbg = true;
    bool bDrawmeshInstancingOrDrawRender = false;
    RenderMode renderMode;
    bool bShadowmapSplit;
    int layerMask = 0;

    // Voxel info
    float VoxelSize = 10;
    int RootVoxelWidthSize = 4;
    float OrthoProjSize = 300;
    float nearClip = 0;
    float farClip = 500;

    string slicedFilePath;
    int lastClickTime = 0;
    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        renderMode = (RenderMode)EditorGUILayout.EnumPopup(renderMode);

        bDrawmeshInstancingOrDrawRender = EditorGUILayout.Toggle("DrawmeshInstancingOrDrawRender", bDrawmeshInstancingOrDrawRender);
        bShadowmapSplit = EditorGUILayout.Toggle("bShadowmapSplit", bShadowmapSplit);
        zClip = EditorGUILayout.Toggle("_ZCLIP", zClip);
        startPlane = EditorGUILayout.Slider("startPlane", startPlane, -1, 1000);
        shadowCamera = EditorGUILayout.ObjectField("shadowCamera", shadowCamera, typeof(Transform)) as Transform;
        layerMask = EditorGUILayout.LayerField(layerMask);
        shadowMap = EditorGUILayout.ObjectField("shadowmap", shadowMap, typeof(RenderTexture)) as RenderTexture;
        litMaterial = EditorGUILayout.ObjectField("LitMaterial", litMaterial, typeof(Material)) as Material;

        RootVoxelWidthSize = Mathf.ClosestPowerOfTwo(EditorGUILayout.IntField("RootVoxelWidthSize", EditorPrefs.GetInt("Sdmbk:RootVoxelWidthSize_" + SceneManager.GetActiveScene().name, RootVoxelWidthSize)));
        EditorPrefs.SetInt("Sdmbk:RootVoxelWidthSize_" + SceneManager.GetActiveScene().name, RootVoxelWidthSize);
        OrthoProjSize = EditorGUILayout.FloatField("OrthoProjSize", EditorPrefs.GetFloat("Sdmbk:OrthoProjSize_" + SceneManager.GetActiveScene().name, OrthoProjSize));
        EditorPrefs.SetFloat("Sdmbk:OrthoProjSize_" + SceneManager.GetActiveScene().name, OrthoProjSize);
        nearClip = EditorGUILayout.FloatField("nearClip", EditorPrefs.GetFloat("Sdmbk:nearClip_" + SceneManager.GetActiveScene().name, nearClip));
        EditorPrefs.SetFloat("Sdmbk:nearClip_" + SceneManager.GetActiveScene().name, nearClip);
        farClip = EditorGUILayout.FloatField("farClip", EditorPrefs.GetFloat("Sdmbk:farClip_" + SceneManager.GetActiveScene().name, farClip));
        EditorPrefs.SetFloat("Sdmbk:farClip_" + SceneManager.GetActiveScene().name, farClip);
        VoxelSize = OrthoProjSize * 2.0f / (float)RootVoxelWidthSize;

        bExportLvLitShadowInfoTexArray4Dbg = EditorGUILayout.Toggle("ExportLvLitShadowInfoTexArray4Dbg", EditorPrefs.GetBool("Sdmbk:bExportLvLitShadowInfoTexArray4Dbg_" + SceneManager.GetActiveScene().name, bExportLvLitShadowInfoTexArray4Dbg));
        EditorPrefs.SetBool("Sdmbk:bExportLvLitShadowInfoTexArray4Dbg_" + SceneManager.GetActiveScene().name, bExportLvLitShadowInfoTexArray4Dbg);
        bSetTopIntersectedVoxelLit = EditorGUILayout.Toggle("SetTopIntersectedVoxelLit", EditorPrefs.GetBool("Sdmbk:bSetTopIntersectedVoxelLit_" + SceneManager.GetActiveScene().name, bSetTopIntersectedVoxelLit));
        EditorPrefs.SetBool("Sdmbk:bSetTopIntersectedVoxelLit_" + SceneManager.GetActiveScene().name, bSetTopIntersectedVoxelLit);
        if (GUILayout.Button("Precompute voxel depth") && lastClickTime != DateTime.Now.Second)
        {
            if (UnityEditor.EditorUtility.DisplayDialog("precompute", "will precompute?", "ok", "cancel"))
            {
                precomputeVoxelDepth();
            }
            lastClickTime = DateTime.Now.Second;
        }
        if (GUILayout.Button("Precompute voxel depth old") && lastClickTime != DateTime.Now.Second)
        {
            if (UnityEditor.EditorUtility.DisplayDialog("precompute", "will precompute?", "ok", "cancel"))
            {
                precomputeVoxelDepthOld();
            }
            lastClickTime = DateTime.Now.Second;
        }

        if (GUILayout.Button("Bake") && lastClickTime != DateTime.Now.Second)
        {
            if (UnityEditor.EditorUtility.DisplayDialog("bake", "will bake?", "ok", "cancel"))
            {
                bake();
            }
            lastClickTime = DateTime.Now.Second;
        }

        if (GUILayout.Button("SetupMatrix"))
        {
            SetupMatrix();
        }

        if (GUILayout.Button("GC"))
        {
            Resources.UnloadUnusedAssets();
            System.GC.Collect();

        }

        EditorGUILayout.EndVertical();
    }

    void SetupMatrix()
    {
        CommandBuffer cmd;
        float orthoHalfSize = OrthoProjSize;
        Matrix4x4 shadowmapProjMatrix = Matrix4x4.Ortho(-orthoHalfSize, orthoHalfSize, -orthoHalfSize, orthoHalfSize, nearClip, farClip);// shadowCamera.farClipPlane);
        Matrix4x4 shadowmapViewMatrix = Matrix4x4.TRS(shadowCamera.transform.position, shadowCamera.transform.rotation, new Vector3(1, 1, -1));
        //shadowmapViewMatrix = shadowmapViewMatrix.inverse;
        Debug.Log(shadowmapProjMatrix);
        Debug.Log(GL.GetGPUProjectionMatrix(shadowmapProjMatrix, false));
        Debug.Log(GL.GetGPUProjectionMatrix(shadowmapProjMatrix, true));
        Shader.SetGlobalMatrix("_LitViewMatrix", shadowmapViewMatrix.inverse);
        Shader.SetGlobalMatrix("_LitProjMatrix", shadowmapProjMatrix);
        Shader.SetGlobalMatrix("_LitProjMatrixGPU", GL.GetGPUProjectionMatrix(shadowmapProjMatrix, false));
        //Shader.SetGlobalMatrix("_LitProjMatrixRT", GL.GetGPUProjectionMatrix(shadowmapProjMatrix, false));
        Shader.SetGlobalMatrix("_LitViewProjMatrix", shadowmapProjMatrix * shadowmapViewMatrix.inverse); // shadowCamera.worldToCameraMatrix); // shadowmapProjMatrix * shadowmapViewMatrix);
    }


    void bake(Stream outstream = null)
    {
        float orthoHalfSize = OrthoProjSize;
        Matrix4x4 shadowmapProjMatrix = Matrix4x4.Ortho(-orthoHalfSize, orthoHalfSize, -orthoHalfSize, orthoHalfSize, nearClip, farClip);// shadowCamera.farClipPlane);
        Matrix4x4 shadowmapViewMatrix = Matrix4x4.TRS(shadowCamera.transform.position, shadowCamera.transform.rotation, new Vector3(1, 1, -1));
        //Debug.Log(shadowmapViewMatrix.inverse);
        shadowmapViewMatrix = shadowmapViewMatrix.inverse;
        //Debug.Log(shadowmapViewMatrix);
        //Debug.Log(shadowCamera.worldToCameraMatrix);
        //Debug.Log(shadowCamera.worldToCameraMatrix);
        var cmb = new CommandBuffer();

        Material renderMat = new Material(Shader.Find("Standard"));

        // setup shader
        if (renderMode == RenderMode.Shadowmap)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ShadowmapBaker/Resources/Shader/Shadowmap.shader");
            renderMat.shader = shader;
        }
        else if (renderMode == RenderMode.ShadowmapLite)
        {
            // level 3 voxel depth
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ShadowmapBaker/Resources/Shader/ShadowmapLiteMode.shader");
            renderMat.shader = shader;
            renderMat.SetFloat("_VoxelDepth", RootVoxelWidthSize * 4.0f);
        }
        else if (renderMode == RenderMode.VoxelShadowmapDiff)
            renderMat.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ShadowmapBaker/Resources/Shader/ShadowmapDiff.shader"); //Shader.Find("Unlit /ShadowmapDiff");
        cmb.DisableShaderKeyword("_ZCLIP");
        if (zClip)
        {
            cmb.EnableShaderKeyword("_ZCLIP");
        }
        else
        {
            cmb.DisableShaderKeyword("_ZCLIP");
        }
        renderMat.SetFloat("_startPlane", startPlane);


        // loopable
        cmb.SetViewProjectionMatrices(shadowmapViewMatrix, shadowmapProjMatrix);

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
            int requestCount = 0;
            int texIdx = 0;
            cmb.SetViewProjectionMatrices(shadowmapViewMatrix, GL.GetGPUProjectionMatrix(shadowmapProjMatrix, true));
            var _voxelShadowMapRT = Shader.PropertyToID("_voxelShadowMapRT");
            List<Texture2D> voxelTextures = new List<Texture2D>();
            var tmpRt = RenderTexture.GetTemporary(shadowMap.width, shadowMap.height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, 2);

            List<Task> copyTasks = new List<Task>();
            List<IDisposable> disposables = new List<IDisposable>();
            List<Stream> streams = new List<Stream>();
#if _MEMMAP_
            slicedFilePath = UnityEditor.EditorUtility.SaveFolderPanel("memory mapfile", Application.dataPath, "");

            var voxelInfoMemory  = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("VoxelInfoMapFile", 1024 * 1024 * 512, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
            //var voxelInfoMemory = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(slicedFilePath + "/memoryMapping.data", FileMode.Create, "VoxelInfoMapFile", 1024 * 1024 * 512, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
            var lockObj = new System.Threading.ReaderWriterLock();
            int memoryOffset = RootVoxelWidthSize * 8;
            uint totalCompressSize1 = 0;
#endif
            for (int i = 0, c = RootVoxelWidthSize /*Mathf.CeilToInt(farClip / VoxelSize)*/; i < c; i++)
            {
                UnityEditor.EditorUtility.DisplayProgressBar("", "render voxel slice", i / (float)RootVoxelWidthSize);
                tmpRt.DiscardContents(true, true);
                cmb.Clear();
                cmb.SetViewProjectionMatrices(shadowmapViewMatrix, GL.GetGPUProjectionMatrix(shadowmapProjMatrix, true));
                float _miniPlane = 1 - (i + 1) * VoxelSize / (farClip - nearClip); // (i + 1) * VoxelSize;// 
                float _maxPlane = 1 - i * VoxelSize / (farClip - nearClip); //  i * VoxelSize;//
                // Debug.Log(_maxPlane);
                renderMat.SetFloat("_miniPlane", _miniPlane);
                renderMat.SetFloat("_maxPlane", _maxPlane);
                renderMat.SetTexture("_shadowMap", shadowMap);
                renderMat.SetMatrix("_UNITY_MATRIX_P", shadowmapProjMatrix);
                //renderMat.SetMatrix("_UNITY_MATRIX_V", shadowmapViewMatrix);
                cmb.SetGlobalMatrix("_UNITY_MATRIX_P", shadowmapProjMatrix);
                //cmb.SetGlobalMatrix("_UNITY_MATRIX_V", shadowmapViewMatrix);
                cmb.SetGlobalFloat("_miniPlane", _miniPlane);
                cmb.Blit(shadowMap, tmpRt, renderMat);
                Graphics.ExecuteCommandBuffer(cmb);


#if UNITY_STANDALONE_WIN && false
                Texture2D readBackTex = null;
                requestCount++;
                cmb.RequestAsyncReadback(shadowMap, 0, TextureFormat.ARGB32, (req) =>
                {
                    readBackTex = new Texture2D(shadowMap.width, shadowMap.height, TextureFormat.ARGB32, false, true);
                    readBackTex.LoadRawTextureData(req.GetData<byte>());
                    readBackTex.Apply(false, false);
                    //AssetDatabase.CreateAsset(readBackTex, "Assets/ReadBackTex.asset");
                    tex = readBackTex;
                    if (voxelTextures.Count > 32)
                    {
                        voxelTextures.ForEach((tex1) =>
                        {
                            AssetDatabase.CreateAsset(tex1, string.Format(LitShadowMapPath + "voxel_lv_{0}.asset", texIdx));
                            texIdx++;
                            Resources.UnloadAsset(tex1);
                        });
                        voxelTextures.Clear();
                        Resources.UnloadUnusedAssets();
                        //AssetDatabase.AllowAutoRefresh();
                        AssetDatabase.Refresh();
                    }
                    requestCount--;
                });

#else

                var rawDataArray = new NativeArray<byte>();

                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    rawDataArray = new NativeArray<byte>(shadowMap.width * shadowMap.height, Allocator.Temp);
                    var aReq = AsyncGPUReadback.RequestIntoNativeArray<byte>(ref rawDataArray, tmpRt, 0, TextureFormat.Alpha8, (req) =>
                    {

                    });
                    aReq.WaitForCompletion();
                }
                else
                {
                    Texture2D tex = new Texture2D(shadowMap.width, shadowMap.height, TextureFormat.Alpha8, false, true);
                    var activeTmp = RenderTexture.active;
                    RenderTexture.active = tmpRt;
                    tex.ReadPixels(new Rect(0, 0, shadowMap.width, shadowMap.height), 0, 0, false);
                    tex.Apply(false, false);
                    RenderTexture.active = activeTmp;
                    rawDataArray = tex.GetRawTextureData<byte>();

                }


                //AssetDatabase.CreateAsset(tex, "Assets/tex.asset");

                if (copyTasks.Count > 256)
                {
                    Task.WaitAll(copyTasks.ToArray());
                    copyTasks.Clear();
                    streams.ForEach((stream) => stream.Dispose());
                    streams.Clear();
                    disposables.ForEach((dispose) => dispose.Dispose());
                    disposables.Clear();
                }
                /*
                if(texIdx % 128 == 0)
                {
                    Texture2D tex1 = new Texture2D(4096, 4096, TextureFormat.Alpha8, false, true);
                    tex1.LoadRawTextureData<byte>(rawDataArray);
                    tex1.Apply(false, false);
                    AssetDatabase.CreateAsset(tex1, "Assets/" + texIdx + ".asset");
                }

                texIdx++;
                */
                unsafe
                {
                    var copyDataArray = new NativeArray<byte>(shadowMap.width * shadowMap.height, Allocator.Persistent);
                    copyDataArray.CopyFrom(rawDataArray);
                    var ptr = (byte*)copyDataArray.GetUnsafeReadOnlyPtr();

#if !_LZ4_COMPRESS_
                    var memStream = new System.IO.UnmanagedMemoryStream((byte*)copyDataArray.GetUnsafeReadOnlyPtr(), tex.width * tex.height);
                    {
                        var fileStream = new System.IO.FileStream(string.Format(LitShadowMapPath + "voxel_lv_{0}.gzip", texIdx), System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite);
                        {
                            var gzipStream = new System.IO.Compression.DeflateStream(fileStream, System.IO.Compression.CompressionLevel.Optimal);
                            {
                                var copyTask = memStream.CopyToAsync(gzipStream, 1024 * 512);
                                copyTasks.Add(copyTask);
                                disposables.Add(copyDataArray);
                                streams.Add(gzipStream);
                                streams.Add(memStream);
                                //System.IO.File.WriteAllBytes(string.Format(LitShadowMapPath + "voxel_lv_{0}.png", texIdx), tex.EncodeToPNG());
                                texIdx++;
                            }

                        }

                    }
#else

                    int textureAreaSize = shadowMap.width * shadowMap.height;
                    int texIdxTmp = texIdx;
                    var lz4Task = Task.Run(() =>
                    {
#if !_MEMMAP_
                        using (var fileStream1 = new System.IO.FileStream(string.Format(LitShadowMapPath + "voxel_lv_{0}.lz4", texIdxTmp), System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite))

                    {
#endif
                        int boundSize = LZ4_compressBound(textureAreaSize);
                        byte* buffer = (byte*)AllocMem((ulong)boundSize);// new byte[textureAreaSize];

                        try
                        {
                            var lz4Stream = LZ4_createStream();
                            //ushort compressedblockSize = (ushort)LZ4_compress_default((byte*)ptr, buffer, textureAreaSize, textureAreaSize + boundSize); //
                            int compressedblockSize = LZ4_compress_fast((byte*)ptr, buffer, textureAreaSize, boundSize, 0);
                            //ushort compressedblockSize = (ushort)LZ4_compress_fast_continue(lz4Stream, (byte*)copyDataArray.GetUnsafeReadOnlyPtr(), buffer, textureAreaSize, textureAreaSize, 0);
                            uint compressedBlockSizeU = System.Convert.ToUInt32(compressedblockSize);
#if !_MEMMAP_
                            fileStream1.WriteByte(((byte*)&compressedBlockSizeU)[0]);
                            fileStream1.WriteByte(((byte*)&compressedBlockSizeU)[1]);
                            fileStream1.WriteByte(((byte*)&compressedBlockSizeU)[2]);
                            fileStream1.WriteByte(((byte*)&compressedBlockSizeU)[3]);
#endif
                            
                            using (var ummem = new System.IO.UnmanagedMemoryStream(buffer, compressedblockSize))
                            {
#if !_MEMMAP_
                                ummem.CopyTo(fileStream1, 4096);
#else
                                //while (lockObj.IsWriteLockHeld || !lockObj.TryEnterWriteLock(300));
                                //lockObj.EnterWriteLock();
                                
                                using (var headReadWrite = voxelInfoMemory.CreateViewStream(texIdxTmp * 8, 8, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite))
                                {
                                    uint memoryOffsetTmp = 0;

                                    //lock (lockObj)
                                    try
                                    {
                                        lockObj.AcquireWriterLock(5000);
                                        memoryOffsetTmp = (uint)memoryOffset;
                                        headReadWrite.WriteByte(((byte*)&memoryOffsetTmp)[0]);
                                        headReadWrite.WriteByte(((byte*)&memoryOffsetTmp)[1]);
                                        headReadWrite.WriteByte(((byte*)&memoryOffsetTmp)[2]);
                                        headReadWrite.WriteByte(((byte*)&memoryOffsetTmp)[3]);

                                        headReadWrite.WriteByte(((byte*)&compressedBlockSizeU)[0]);
                                        headReadWrite.WriteByte(((byte*)&compressedBlockSizeU)[1]);
                                        headReadWrite.WriteByte(((byte*)&compressedBlockSizeU)[2]);
                                        headReadWrite.WriteByte(((byte*)&compressedBlockSizeU)[3]); ;

                                        memoryOffset += compressedblockSize;
                                        totalCompressSize1 += compressedBlockSizeU;
                                    }
                                    finally
                                    {
                                        lockObj.ReleaseWriterLock();
                                    }
                                    using (var pageWrite = voxelInfoMemory.CreateViewStream(memoryOffsetTmp, compressedblockSize, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Write))
                                    {
                                        ummem.CopyTo(pageWrite, 4096);
                                    }
                                }

#endif
                                //lockObj.ExitWriteLock();
                            }
                            LZ4_freeStream(lz4Stream);
                        }
                        finally
                        {
                            FreeMem(buffer);
                        }
#if !_MEMMAP_
                }
#endif

                    });
                    copyTasks.Add(lz4Task);
                    disposables.Add(copyDataArray);
                    texIdx++;
#endif
                        }

                /*
                voxelTextures.Add(tex);
                if (voxelTextures.Count > 32)
                {
                    voxelTextures.ForEach((tex1) =>
                    {
                        System.IO.File.WriteAllBytes(string.Format(LitShadowMapPath + "voxel_lv_{0}.png", texIdx), tex1.EncodeToPNG());
                        //AssetDatabase.CreateAsset(tex1, string.Format(LitShadowMapPath + "voxel_lv_{0}.asset", texIdx));
                        texIdx++;
                        Resources.UnloadAsset(tex1);
                    });
                    voxelTextures.Clear();
                    Resources.UnloadUnusedAssets();
                    //AssetDatabase.AllowAutoRefresh();
                    AssetDatabase.Refresh();
                }
                */
#endif

            }


            if (copyTasks.Count > 0)
            {
                Task.WaitAll(copyTasks.ToArray());
                copyTasks.Clear();
                streams.ForEach((stream) => stream.Dispose());
                streams.Clear();
                disposables.ForEach((dispose) => dispose.Dispose());
                disposables.Clear();
            }

#if _MEMMAP_
            uint totalCompressedSize = 0;
            using (var headStream = voxelInfoMemory.CreateViewStream(0, 8 * RootVoxelWidthSize, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read))
            {
                unsafe
                {
                    for (int i = 0; i < RootVoxelWidthSize; i++)
                    {

                        uint sliceOffsetAddr = 0;
                        uint compressSizePerSlice = 0;
                        ((byte*)&sliceOffsetAddr)[0] = (byte)headStream.ReadByte();
                        ((byte*)&sliceOffsetAddr)[1] = (byte)headStream.ReadByte();
                        ((byte*)&sliceOffsetAddr)[2] = (byte)headStream.ReadByte();
                        ((byte*)&sliceOffsetAddr)[3] = (byte)headStream.ReadByte();
                        ((byte*)&compressSizePerSlice)[0] = (byte)headStream.ReadByte();
                        ((byte*)&compressSizePerSlice)[1] = (byte)headStream.ReadByte();
                        ((byte*)&compressSizePerSlice)[2] = (byte)headStream.ReadByte();
                        ((byte*)&compressSizePerSlice)[3] = (byte)headStream.ReadByte();

                        totalCompressedSize += compressSizePerSlice;
                    }

                }
            }

            //using (var fileStreamMemmap = new System.IO.FileStream(memMapFile + "/memoryMappingStripped.data", System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite))
            {
                using (var strippedMemMap = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(slicedFilePath + "/memoryMappingStripped.data", FileMode.Create, "strippedMemMap", totalCompressedSize + RootVoxelWidthSize * 8))
                {
                    using (var copyFrom = voxelInfoMemory.CreateViewStream(0, totalCompressedSize + RootVoxelWidthSize * 8, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read))
                    {
                        copyFrom.CopyTo(strippedMemMap.CreateViewStream(0, totalCompressedSize + RootVoxelWidthSize * 8));
                    }
                }
            }

            voxelInfoMemory.Dispose();
            System.IO.File.Delete(slicedFilePath + "/memoryMapping.data");
#endif
            //AssetDatabase.DisallowAutoRefresh();
            /*
            voxelTextures.ForEach((tex) =>
            {
                System.IO.File.WriteAllBytes(string.Format(LitShadowMapPath + "voxel_lv_{0}.png", texIdx), tex.EncodeToPNG());
                //AssetDatabase.CreateAsset(tex, string.Format(LitShadowMapPath + "voxel_lv_{0}.asset", texIdx));
                texIdx++;
                Resources.UnloadAsset(tex);
            });
            voxelTextures.Clear();
            */
            Resources.UnloadUnusedAssets();
            //AssetDatabase.AllowAutoRefresh();
            AssetDatabase.Refresh();
            cmb.Release();
        }
        else if (renderMode == RenderMode.None || renderMode == RenderMode.Shadowmap)
        {
            if (renderMode == RenderMode.Shadowmap)
            {
                cmb.SetRenderTarget(shadowMap);

                cmb.ClearRenderTarget(true, true, Color.black);
            }

            //cmb.DisableShaderKeyword("VOXEL_SHADOW");
            //cmb.EnableShaderKeyword("VOXEL_SHADOW");
            //cmb.EnableShaderKeyword("LIGHTMAP_ON");
            //cmb.EnableShaderKeyword("SHADOWS_DEPTH");
            // draw scene
            for (int i = 0, c = allRenderer.Length; i < c; i++)
            {
                if (allRenderer[i].gameObject.layer != layerMask)
                    continue;

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
                    var modelMat = allRenderer[i].sharedMaterial;
                    allRenderer[i].SetPropertyBlock(mpb);
                    if (modelMat != null)
                    {
                        var mainTex = modelMat.GetTexture("_MainTex");
                        if (mainTex != null)
                        {
                            renderMat.mainTexture = modelMat.mainTexture;// .SetTexture("_MainTex", mainTex);
                            renderMat.EnableKeyword("_Voxel_CLIP_ALPHA");
                        }
                        else
                        {
                            renderMat.DisableKeyword("_Voxel_CLIP_ALPHA");
                        }
                    }
                    cmb.DrawRenderer(allRenderer[i], renderMode != RenderMode.None ? renderMat : allRenderer[i].sharedMaterial);
                    /*
                    
                    var meshFilter = allRenderer[i].GetComponent<MeshFilter>();
                    var sharedMesh = meshFilter != null? meshFilter.sharedMesh: null;
                    if (meshFilter != null && sharedMesh != null && modelMat != null)
                    {
                        //"ShadowCaster"
                        var shadowCasterPass = modelMat.FindPass("SHADOWCASTER");
                        //Debug.Log(shadowCasterPass);
                        if (shadowCasterPass >= 0)
                        {
                            for (int meshIdx = 0, meshMax = sharedMesh.subMeshCount; meshIdx < meshMax; meshIdx++)
                            {
                                cmb.DrawRenderer(allRenderer[i], modelMat,  meshIdx, shadowCasterPass);
                            }
                        }
                    }
                    */

                }
            }


            Graphics.ExecuteCommandBuffer(cmb);
            cmb.Release();

            if (renderMode == RenderMode.Shadowmap)
            {
                var activeTmp = RenderTexture.active;
                RenderTexture.active = shadowMap;
                Texture2D tex = new Texture2D(shadowMap.width, shadowMap.height, TextureFormat.ARGB32, false, true);
                tex.ReadPixels(new Rect(0, 0, shadowMap.width, shadowMap.height), 0, 0);
                tex.Apply();
                RenderTexture.active = activeTmp;
                shadowmapTex = tex;

                AssetDatabase.CreateAsset(tex, "Assets/shadowmapAll.asset");
            }

        }
        else if (renderMode == RenderMode.ShadowmapLite)
        {
            var readBackRt = RenderTexture.GetTemporary(shadowMap.width, shadowMap.height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, 2);
            shadowmapLite = new Texture2D(shadowMap.width, shadowMap.height, TextureFormat.RGB24, false, true);
            shadowmapLite1 = new Texture2D(shadowMap.width, shadowMap.height, TextureFormat.RGB24, false, true);
            cmb.SetRenderTarget(readBackRt);
            cmb.ClearRenderTarget(true, true, Color.white);
            var saveAssetPath = EditorUtility.SaveFolderPanel("保存资源", Application.dataPath, "");
            for (int texId = 0; texId < 2; texId++)
            {
                bShadowmapSplit = texId == 0;
                if (bShadowmapSplit)
                {
                    cmb.EnableShaderKeyword("_ShadowmapSplit");
                    //renderMat.EnableKeyword("_ShadowmapSplit");
                }
                else
                {
                    cmb.DisableShaderKeyword("_ShadowmapSplit");
                    //renderMat.DisableKeyword("_ShadowmapSplit");
                }
                renderMat.EnableKeyword("_ShadowmapEncode");
                var fence = cmb.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing);
                for (int i = 0, c = allRenderer.Length; i < c; i++)
                {
                    if (allRenderer[i].gameObject.layer != layerMask)
                        continue;
                    allRenderer[i].GetPropertyBlock(mpb);
                    mpb.Clear();
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
                        var modelMat = allRenderer[i].sharedMaterial;
                        allRenderer[i].SetPropertyBlock(mpb);
                        if (modelMat != null)
                        {
                            var mainTex = modelMat.GetTexture("_MainTex");
                            if (mainTex != null)
                            {
                                renderMat.mainTexture = modelMat.mainTexture;// .SetTexture("_MainTex", mainTex);
                                renderMat.EnableKeyword("_Voxel_CLIP_ALPHA");
                            }
                            else
                            {
                                renderMat.DisableKeyword("_Voxel_CLIP_ALPHA");
                            }
                        }
                        cmb.DrawRenderer(allRenderer[i], renderMode != RenderMode.None ? renderMat : allRenderer[i].sharedMaterial);

                    }
                }

                //cmb.GetTemporaryRT(Shader.PropertyToID("shadowmapLite"), shadowMap.width, shadowMap.height, 1, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                //cmb.GetTemporaryRT(Shader.PropertyToID("shadowmapLite1"), shadowMap.width, shadowMap.height, 1, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                //cmb.Blit(shadowMap, bShadowmapSplit? new UnityEngine.Rendering.RenderTargetIdentifier("shadowmapLite") : new UnityEngine.Rendering.RenderTargetIdentifier("shadowmapLite1"));
                
                cmb.WaitOnAsyncGraphicsFence(fence, SynchronisationStageFlags.PixelProcessing);
                
                bool split = bShadowmapSplit;
                cmb.RequestAsyncReadback(readBackRt, 0, TextureFormat.ARGB32, new Action<AsyncGPUReadbackRequest>((req) =>
                {
                    if (req.done)
                    {
                        //var data = req.GetData<Byte>();
                        //var tex = bShadowmapSplit ? shadowmapLite : shadowmapLite1;
                        //tex.LoadRawTextureData(data);
#if UNITY_STANDALONE_WIN && false
                        Texture2D readBackTex = new Texture2D(readBackRt.width, readBackRt.height, TextureFormat.ARGB32, false, true);
                        readBackTex.LoadRawTextureData(req.GetData<byte>());
                        readBackTex.Apply(false, false);
                        //AssetDatabase.CreateAsset(readBackTex, "Assets/ReadBackTex.asset");

                        Texture2D tex = readBackTex;
#else
                        var activeTmp = RenderTexture.active;
                        RenderTexture.active = readBackRt;
                        Texture2D tex = new Texture2D(shadowMap.width, shadowMap.height, TextureFormat.RGB24, false, true);

                        tex.ReadPixels(new Rect(0, 0, shadowMap.width, shadowMap.height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = activeTmp;
#endif
                        
                        if (split)
                        {
                            Debug.Log("shadowmapLite");
                            shadowmapLite = tex;
                            // AssetDatabase.CreateAsset(shadowmapLite, "Assets/shadowmapLite.asset");
                            var projRelPath = UnityEditor.FileUtil.GetProjectRelativePath(saveAssetPath + "/shadowmapLite.tga");
                            System.IO.File.WriteAllBytes(saveAssetPath + "/shadowmapLite.tga", shadowmapLite.EncodeToTGA());
                            AssetDatabase.ImportAsset(projRelPath);
                            shadowmapLite = AssetDatabase.LoadAssetAtPath<Texture2D>(projRelPath);
                            var ti = TextureImporter.GetAtPath(projRelPath) as TextureImporter;
                            ti.mipmapEnabled = false;
                            ti.sRGBTexture = false;
                            var androidTi = ti.GetPlatformTextureSettings("android");
                            androidTi.overridden = true;
                            androidTi.maxTextureSize = 512;
                            androidTi.format = TextureImporterFormat.ASTC_RGB_4x4;
                            ti.SetPlatformTextureSettings(androidTi);

                            var iOSTi = ti.GetPlatformTextureSettings("ios");
                            iOSTi.overridden = true;
                            iOSTi.maxTextureSize = 512;
                            iOSTi.format = TextureImporterFormat.PVRTC_RGB4;
                            ti.SetPlatformTextureSettings(iOSTi);

                            ti.SaveAndReimport();

                            litMaterial.SetTexture("_VoxelShadowmap", shadowmapLite);
                        }
                        else
                        {
                            Debug.Log("shadowmapLite1");
                            shadowmapLite1 = tex;
                            // AssetDatabase.CreateAsset(shadowmapLite1, "Assets/shadowmapLite1.asset");
                            var projRelPath = UnityEditor.FileUtil.GetProjectRelativePath(saveAssetPath + "/shadowmapLite1.tga");
                            System.IO.File.WriteAllBytes(saveAssetPath + "/shadowmapLite1.tga", shadowmapLite1.EncodeToTGA());
                            AssetDatabase.ImportAsset(projRelPath);
                            shadowmapLite1 = AssetDatabase.LoadAssetAtPath<Texture2D>(projRelPath);
                            var ti = TextureImporter.GetAtPath(projRelPath) as TextureImporter;
                            ti.mipmapEnabled = false;
                            ti.sRGBTexture = false;
                            var androidTi = ti.GetPlatformTextureSettings("android");
                            androidTi.overridden = true;
                            androidTi.maxTextureSize = 256;
                            androidTi.format = TextureImporterFormat.ASTC_RGB_6x6;
                            ti.SetPlatformTextureSettings(androidTi);

                            var iOSTi = ti.GetPlatformTextureSettings("ios");
                            iOSTi.overridden = true;
                            iOSTi.maxTextureSize = 256;
                            iOSTi.format = TextureImporterFormat.PVRTC_RGB4;
                            ti.SetPlatformTextureSettings(iOSTi);

                            ti.SaveAndReimport();
                            litMaterial.SetTexture("_Shadowmap", shadowmapLite1);
                            
                        }
                    }
                }));
                cmb.WaitAllAsyncReadbackRequests();

            }

            Graphics.ExecuteCommandBuffer(cmb);
            cmb.Release();
        }

    }

    //struct BlockIndex
    //{
    //    public int U;
    //    public int V;
    //}

    //public enum LitOrShadow
    //{
    //    FullLit, 
    //    FullShadow,
    //    Intersected,
    //}

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


    /// <summary>
    /// force set top voxel full lit
    /// </summary>
    private unsafe void setTopVoxelLit(byte* texArray, long width, long height, long depth)
    {
        //bool[] pixelMask = new bool[width * height];
        //bool sliceChanged = false;

            for (long vIdx = 0, vMax = height; vIdx < vMax; vIdx++)
            {
                var task = Task.Run(() =>
                {

                    for (long uIdx = 0, uMax = width; uIdx < uMax; uIdx++)
                    {
                        for (long dIdx = 0, dMax = depth; dIdx < dMax; dIdx++)
                        {
                            var sliceFront = texArray + width * height * dIdx;
                            //var sliceBack = (dIdx < dMax - 1) ? texArray + width * height * (dIdx +1) : null;

                            var idx = vIdx * uMax + uIdx;
                            var litInfoFront = sliceFront[idx];
                            if (Mathf.Abs(litInfoFront - 128) < 20)
                            {
                                sliceFront[idx] = 255;
                                break;
                            }
                            //if (!pixelMask[idx] && Mathf.Abs(litInfoFront - 128) < 20) //&& (sliceBack == null || litInfoBack.a < 0.2f )
                            //{
                            //    if (bSetTopIntersectedVoxelLit)
                            //        pixelMask[idx] = true;
                            //    sliceFront[idx] = 255;
                            //}
                            //if (bSetTopIntersectedVoxelLit)
                            //    pixelMask[idx] |= Mathf.Abs(litInfoFront - 128) < 20;
                        }
                    }

                    IncreaseProgress();
                });
                mPendingTask.Add(task);
                if (mPendingTask.Count > height / 64 )
                {
                    WaitPendingTask((int)height, true, true, "SetTopVoxelLit", "SetTopVoxelLit");
                    mPendingTask.Clear();
                }
            }

            WaitPendingTask( (int)height, true, true, "SetTopVoxelLit", "SetTopVoxelLit");
            mPendingTask.Clear();
            //if (sliceChanged)
            //    texArray.SetPixels(sliceFront, dIdx);
        if (mPendingTask.Count > 0)
        {
            WaitPendingTask((int)height * (int)depth, true, true, "SetTopVoxelLit", "SetTopVoxelLit");
            mPendingTask.Clear();
        }
        //texArray.Apply(false, false);
    }

    /// <summary>
    /// force set top voxel full lit
    /// </summary>
    private void setTopVoxelLit(Texture2DArray texArray)
    {
        bool[] pixelMask = new bool[texArray.width * texArray.height];
        for (int dIdx = 0, dMax = texArray.depth; dIdx < dMax; dIdx++)
        {
            var sliceFront = texArray.GetPixels(dIdx, 0);
            var sliceBack = (dIdx < dMax - 1) ? texArray.GetPixels(dIdx + 1, 0) : null;
            bool sliceChanged = false;
            for (int vIdx = 0, vMax = texArray.height; vIdx < vMax; vIdx++)
            {
                for (int uIdx = 0, uMax = texArray.width; uIdx < uMax; uIdx++)
                {
                    var idx = vIdx * uMax + uIdx;
                    var litInfoFront = sliceFront[idx];
                    var litInfoBack = sliceBack != null ? sliceBack[idx] : Color.black;

                    if (!pixelMask[idx] && Mathf.Abs(litInfoFront.a - 0.5f) < 0.2) //&& (sliceBack == null || litInfoBack.a < 0.2f )
                    {
                        if (bSetTopIntersectedVoxelLit)
                            pixelMask[idx] = true;
                        sliceFront[idx] = new Color(1, 1, 1, 1);
                        sliceChanged = true;
                    }
                    if (bSetTopIntersectedVoxelLit)
                        pixelMask[idx] |= Mathf.Abs(litInfoFront.a - 0.5f) < 0.2;
                }
            }
            if (sliceChanged)
                texArray.SetPixels(sliceFront, dIdx);
        }
        texArray.Apply(false, false);
    }

    // compute voxel on cpu 
    // compute lv3 lit or shadow info first, then summary to lv2 and rootLv1
    void precomputeVoxelDepthOld()
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
        int lv4VoxelSize = lv3VoxelSize * 2;
        int lv5VoxelSize = lv4VoxelSize * 2;
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
        Texture2DArray litShadowInfoArrayLv3 = new Texture2DArray(resultTextureSize, resultTextureSize, resultTextureSize, TextureFormat.RGB24, false, true);
        Texture2DArray litShadowInfoArrayLv2 = new Texture2DArray(lv2VoxelSize, lv2VoxelSize, lv2VoxelSize, TextureFormat.RGB24, false, true);
        Texture2DArray litShadowInfoArrayRoot = new Texture2DArray(rootVoxelSize, rootVoxelSize, rootVoxelSize, TextureFormat.RGB24, false, true);

        List<Object> resourceToRelease = new List<Object>();

        object lockObj = new object();
        int threadCount = 0;
        Queue<Action> mainThreadTasks = new Queue<Action>();
        List<Task> plTasks = new List<Task>();

        var mainTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        // z-depth 
        for (int dVoxelIndex = 0, dVoxelMaxIndex = lv3VoxelSize; dVoxelIndex < dVoxelMaxIndex; dVoxelIndex++)
        {
            var voxelLitShadowInfo = AssetDatabase.LoadAssetAtPath<Texture2D>(string.Format(LitShadowMapPath + "voxel_lv_{0}.asset", dVoxelIndex));
            bool isAlpha8 = voxelLitShadowInfo.format == TextureFormat.Alpha8;
            resourceToRelease.Add(voxelLitShadowInfo);
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
            var voxelLitShadowInfoColorNA = voxelLitShadowInfo.GetRawTextureData<Color32>();
            var voxelLitShadowInfoNA = voxelLitShadowInfo.GetRawTextureData<byte>();
            float startTime = Time.realtimeSinceStartup;


            while (threadCount > 128)
            {
                System.Threading.Thread.Sleep(300);
                //if(Time.realtimeSinceStartup - startTime > 60)
                //{
                //    return;
                //}
            }

            int dVoxelIndexTmp = dVoxelIndex;
            System.Action task = () =>
            {
                litShadowInfoArrayLv3.SetPixels(blockPixels, dVoxelIndexTmp, 0);
            };


            // 
            int width = voxelLitShadowInfo.width;
            int height = voxelLitShadowInfo.height;
            //System.Threading.ThreadPool.UnsafeQueueUserWorkItem(
            var plTask = new Task(() =>
            {
                System.Threading.Thread.Sleep(300);
                int errorIndex = 0;
                unsafe
                {
                    Color32* voxelLitShadowInfoPtr = null;
                    byte* alpha8 = null;
                    if (isAlpha8)
                        alpha8 = (byte*)voxelLitShadowInfoNA.GetUnsafePtr<byte>();
                    else
                        voxelLitShadowInfoPtr = (Color32*)voxelLitShadowInfoColorNA.GetUnsafePtr<Color32>();

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

                                    var pixel = isAlpha8 ? alpha8[vPixel * width + uPixel] / 255 : voxelLitShadowInfoPtr[vPixel * width + uPixel].r;
                                    errorIndex = vPixel * width + uPixel;
                                    var isWhite = Mathf.Abs(pixel - 1) < 0.1f;
                                    var isBlack = Mathf.Abs(pixel - 0) < 0.1f;
                                    var isGray = Mathf.Abs(pixel - 0.5f) < 0.1f;
                                    isBlockLit &= isWhite;
                                    isBlockShadow &= isBlack;
                                }
                            }

                            bool isBlockIntersection = !isBlockLit && !isBlockShadow;
                            var blockResult = (isBlockLit ? 1 : 0) + (isBlockIntersection ? 0.5f : 0);
                            blockPixels[vBlockIndex * uBlockIdxMax + uBlockIndex] = Color.white * blockResult;
                        }
                    }
                }


                //litShadowInfoArrayLv3.SetPixels(blockPixels, dVoxelIndex, 0);
                //task.Start(mainTaskScheduler);
                lock (mainThreadTasks)
                {
                    mainThreadTasks.Enqueue(task);
                }
                lock (lockObj)
                {
                    threadCount--;
                }

            });

            plTasks.Add(plTask);


            while (mainThreadTasks.Count > 16)
            {
                lock (mainThreadTasks)
                {
                    while (mainThreadTasks.Count > 0)
                    {
                        var pendingTask = mainThreadTasks.Dequeue();
                        pendingTask();
                    }
                }
            }


            if (plTasks.Count > 32)
            {
                plTasks.ForEach((t) =>
                {
                    lock (lockObj)
                    {
                        threadCount++;
                    }
                    t.Start();
                });
                plTasks.Clear();
            }

            if (resourceToRelease.Count > 8)
            {
                resourceToRelease.ForEach((res) => Resources.UnloadAsset(res));
                Resources.UnloadUnusedAssets();
                System.GC.Collect();
            }
            resourceToRelease.Clear();

        }

        if (plTasks.Count > 0)
        {
            plTasks.ForEach((t) =>
            {
                threadCount++;
                t.Start();
            });
            plTasks.Clear();
        }

        while (threadCount != 0)
            {
                System.Threading.Thread.Sleep(300);
            }
            while (mainThreadTasks.Count > 0)
            {
                lock (mainThreadTasks)
                {
                    var pendingTask = mainThreadTasks.Dequeue();
                    pendingTask();
                }
            }

        litShadowInfoArrayLv3.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        DownSample(lv2VoxelSize, lv3VoxelSize, litShadowInfoArrayLv2, litShadowInfoArrayLv3);
        /*
        Debug.Log("$$ summary to lv2 start:" + Time.realtimeSinceStartup);
        // summary to lv2
        for (int dBlockIndex = 0, dBlockIdxMax = lv2VoxelSize; dBlockIndex < dBlockIdxMax; dBlockIndex++)
        {
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
        */
        litShadowInfoArrayLv2.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();


        DownSample(rootVoxelSize, lv2VoxelSize, litShadowInfoArrayRoot, litShadowInfoArrayLv2);

        /*

        Debug.Log("$$ summary to lv2 end:" + Time.realtimeSinceStartup);

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
        */
        litShadowInfoArrayRoot.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        setTopVoxelLit(litShadowInfoArrayLv3);
        //setTopVoxelLit(litShadowInfoArrayLv2);
        //setTopVoxelLit(litShadowInfoArrayRoot);

        if (bExportLvLitShadowInfoTexArray4Dbg)
            AssetDatabase.CreateAsset(litShadowInfoArrayRoot, "Assets/lightInfoArrayLv1.asset");

        if (bExportLvLitShadowInfoTexArray4Dbg)
            AssetDatabase.CreateAsset(litShadowInfoArrayLv2, "Assets/lightInfoArrayLv2.asset");

        if (bExportLvLitShadowInfoTexArray4Dbg)
            AssetDatabase.CreateAsset(litShadowInfoArrayLv3, "Assets/lightInfoArrayLv3.asset");

        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        // calculate intersected lv1 voxel count, to construct a lv2 info map
        int lv1IntersectedCount = 0;
        for (int dVoxelIndex = 0, dVoxelMax = rootVoxelSize; dVoxelIndex < dVoxelMax; dVoxelIndex++)
        {
            var litShadowInfoLv1 = litShadowInfoArrayRoot.GetPixels(dVoxelIndex);
            for (int pixelIdx = 0, pixelMax = litShadowInfoLv1.Length; pixelIdx < pixelMax; pixelIdx++)
            {
                Vector4 value = litShadowInfoLv1[pixelIdx];
                if (Mathf.Abs(value.x - 0.5f) < 0.1f)
                {
                    lv1IntersectedCount++;
                }
            }
        }

        int textureArraySize = Mathf.CeilToInt(lv1IntersectedCount / 32 + (lv1IntersectedCount % 32 > 0 ? 1 : 0)); //Mathf.NextPowerOfTwo

        lv1IntersectedCount = Mathf.IsPowerOfTwo(lv1IntersectedCount) ? lv1IntersectedCount : Mathf.NextPowerOfTwo(lv1IntersectedCount);

        // shipping Lit shadow info to realtime rendering texture
        // head start from level 1 info
        // format-----  

        int litShaowInfoIndexMapSizeOrig = Mathf.RoundToInt(Mathf.Sqrt(rootVoxelSize * rootVoxelSize * rootVoxelSize));
        int litShadowInfoIndexMapSize = Mathf.IsPowerOfTwo(litShaowInfoIndexMapSizeOrig) ? litShaowInfoIndexMapSizeOrig : Mathf.NextPowerOfTwo(litShaowInfoIndexMapSizeOrig);
        //int litShadowInfoMapLength = Mathf.RoundToInt(Mathf.Pow(rootVoxelSize, 3));
        // level1 index
        Texture2D litShadowInfoIndexMap = new Texture2D(litShadowInfoIndexMapSize, litShadowInfoIndexMapSize, TextureFormat.RGBA32, false, true);
        Texture2D litShadowInfoIndexMapNoTextureArray = new Texture2D(litShadowInfoIndexMapSize, litShadowInfoIndexMapSize, TextureFormat.RGBA32, false, true);

        // encode lit shadow info to texture2d
#if !_ENABLE_BIG_TEX
        Texture2D litShadowInfoMap = new Texture2D(32, lv1IntersectedCount, TextureFormat.RGBA32, false, true);
#endif
        Texture2DArray litShadowInfoMapArray = new Texture2DArray(32, 32, textureArraySize, TextureFormat.RGBA32, false, true);
#if !_ENABLE_BIG_TEX
        Texture2D litShadowInfoMapLv3 = new Texture2D(32, lv1IntersectedCount, TextureFormat.RGBA32, false, true);
#endif
        Texture2DArray litShadowInfoMapArrayLv3 = new Texture2DArray(32, 32, textureArraySize, TextureFormat.RGBA32, false, true);

        //var tmp = RenderTexture.GetTemporary(litShadowInfoMap.width, litShadowInfoMap.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, 2);
        //Graphics.Blit(Texture2D.blackTexture, tmp);
        //Graphics.CopyTexture(tmp, litShadowInfoMap);

        //_ENABLE_BIG_TEX

#if !_ENABLE_BIG_TEX
        var pixels = litShadowInfoMap.GetPixels(0);
        MultiCoreMemSetBlack(pixels);
        litShadowInfoMap.SetPixels(pixels);

        
        var pixelsLv3 = litShadowInfoMapLv3.GetPixels(0);
        MultiCoreMemSetBlack(pixelsLv3);
        litShadowInfoMapLv3.SetPixels(pixelsLv3);
        

        litShadowInfoMap.Apply();
#endif
        var indexPixels = litShadowInfoIndexMap.GetPixels(0);
        var indexPixelsNoTexArrayPixels = litShadowInfoIndexMapNoTextureArray.GetPixels(0);



        MultiCoreMemSetBlack(indexPixels);
        litShadowInfoIndexMap.SetPixels(indexPixels);

        MultiCoreMemSetBlack(indexPixelsNoTexArrayPixels);
        litShadowInfoIndexMapNoTextureArray.SetPixels(indexPixelsNoTexArrayPixels);

        for (int texArrayDepth = 0, maxDepth = litShadowInfoMapArray.depth; texArrayDepth < maxDepth; texArrayDepth++)
        {
            var pixels1 = litShadowInfoMapArray.GetPixels(texArrayDepth, 0);
            MultiCoreMemSetBlack(pixels1);
            litShadowInfoMapArray.SetPixels(pixels1, texArrayDepth);
            var pixels2 = litShadowInfoMapArrayLv3.GetPixels(texArrayDepth, 0);
            MultiCoreMemSetBlack(pixels2);
            litShadowInfoMapArrayLv3.SetPixels(pixels2, texArrayDepth);
        }

        litShadowInfoMapArray.Apply();


        // bake lit shadow info to texture
        int queryIdx = 0;
        int texDepth = queryIdx / 32;
        Color[] colorBlock32x32 = new Color[32 * 32];

        bool[] axeAllLitFront = new bool[4] { true, true, true, true };
        bool[] axeAllShadowFront = new bool[4] { true, true, true, true };
        bool[] axeIntersectedFront = new bool[4] { true, true, true, true };
        bool[] axeAllLitBack = new bool[4] { true, true, true, true };
        bool[] axeAllShadowBack = new bool[4] { true, true, true, true };
        bool[] axeIntersectedBack = new bool[4] { true, true, true, true };

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
                                catch (System.IndexOutOfRangeException e)
                                {
                                    Debug.Log(vFinal);
                                    Debug.Log(litShadowInfoLv2_front.Length);
                                }
                                Color frontColor = lv2_front;
                                Color backColor = lv2_back;

                                if (((Mathf.Abs(frontColor.r - 1) < 0.1f && Mathf.Abs(backColor.r - 1) < 0.1f) ||
                                    (Mathf.Abs(frontColor.r - 0) < 0.1f && Mathf.Abs(backColor.r - 0) < 0.1f)))
                                {

                                    Color colorLv3 = new Color(frontColor.r, frontColor.r, frontColor.r, frontColor.r);
                                    for (int vPixelIndexLv3 = 0, vPixelMaxLv3 = 2; vPixelIndexLv3 < vPixelMaxLv3; vPixelIndexLv3++)
                                    {
                                        for (int uPixelIndexLv3 = 0, uPixelMaxLv3 = 2; uPixelIndexLv3 < uPixelMaxLv3; uPixelIndexLv3++)
                                        {
                                            int pixelIdx = 8 * vPixelIndex + uPixelIndex * 4 + 2 * vPixelIndexLv3 + uPixelIndexLv3;
#if !_ENABLE_BIG_TEX
                                            litShadowInfoMapLv3.SetPixel(pixelIdx, queryIdx, colorLv3, 0);
#else
                                            colorBlock32x32[queryIdx % 32 * 32 + pixelIdx] = colorLv3;
#endif


                                        }
                                    }
                                }
                                else
                                {
                                    //lv2FrontRGBA[vPixelIndex * uPixelMax + uPixelIndex] = lv2_front.r;
                                    //lv2BackRGBA[vPixelIndex * uPixelMax + uPixelIndex] = lv2_back.r;

                                    // TODO Lv3 info
                                    //float lv3TexV =

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
#if !_ENABLE_BIG_TEX
                                            litShadowInfoMapLv3.SetPixel(pixelIdx, queryIdx, colorLv3, 0);
#else
                                            colorBlock32x32[queryIdx % 32 * 32 + pixelIdx] = colorLv3;
#endif




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
                                        axeAllLitBack[1] ? 1 : 0 + (axeIntersectedBack[1] ? 0.5f : 0),
                                        axeAllLitBack[2] ? 1 : 0 + (axeIntersectedBack[2] ? 0.5f : 0),
                                        axeAllLitBack[3] ? 1 : 0 + (axeIntersectedBack[3] ? 0.5f : 0));
                                }

                                int seqFront = vPixelIndex * uPixelMax + uPixelIndex;
                                int seqBack = seqFront + 4;

                                // frontColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                                // backColor = frontColor;

                                // debug
                                //frontColor = new Color(1, 1, 1, 1);
                                //backColor = new Color(1, 1, 1, 1);

#if !_ENABLE_BIG_TEX
                                litShadowInfoMap.SetPixel(seqFront, queryIdx, frontColor, 0);
                                litShadowInfoMap.SetPixel(seqBack, queryIdx, backColor, 0);
#endif


                            }
                        }

                        float texV = (queryIdx % 32) / 32.0f + 0.01f;  //(queryIdx % (lv1IntersectedCount * 0.5f))  / ((float)lv1IntersectedCount); //queryIdx / (float)lv1IntersectedCount;  //
                        Vector4 v = EncodeFloatRG(texV);

                        float arrayIndex = (queryIdx / 32) / (float)(textureArraySize - 1);
                        Vector2 arrayIndexRG = EncodeFloatRG(arrayIndex);
                        // R: lit or Shadow GB: v A : textureArray index  setup after lv2 info summarized
                        litShadowInfoIndexMap.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.r, texV, arrayIndexRG.x, arrayIndexRG.y));// //new Color(lv1.r, v.x, v.y, isHalf), 0);

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

#if _ENABLE_BIG_TEX
                        queryIdx++;
                        if (texDepth != queryIdx / 32)
                        {
                            litShadowInfoMapArrayLv3.SetPixels(colorBlock32x32, texDepth, 0);
                            texDepth = queryIdx / 32;
                            // colorBlock32x32 = litShadowInfoMapArrayLv3.GetPixels(texDepth, 0);

                        }
#endif

                    }
                    else
                    {
                        Vector4 v = EncodeFloatRG(lv1.r > 0.9 ? 20000 : 10000);
                        litShadowInfoIndexMap.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.r, 0, 0, 0), 0);
                        litShadowInfoIndexMapNoTextureArray.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.r, 0, 0, 0), 0);
                    }
                }
            }


            Resources.UnloadUnusedAssets();
            System.GC.Collect();
            //AssetDatabase.DeleteAsset("Assets/runtimeLitShadowInfo.asset");
            //AssetDatabase.CreateAsset(litShadowInfoMap, "Assets/runtimeLitShadowInfo.asset");
        }

        //if(EditorUtility.DisplayDialog("选择保存目录", "", ""))
        //{

        //}

        var savePathAsset = EditorUtility.SaveFolderPanel("保存路径",Application.dataPath, "");
        var parentPath = FileUtil.GetProjectRelativePath(savePathAsset);

#if _ENABLE_BIG_TEX
        litShadowInfoMapArrayLv3.SetPixels(colorBlock32x32, Mathf.Min(texDepth, litShadowInfoMapArrayLv3.depth - 1), 0);
        litShadowInfoMapArrayLv3.Apply(false, false);
#endif
        //AssetDatabase.DeleteAsset("Assets/litShadowInfoArray.asset");

        litShadowInfoIndexMap.Apply(false, false);
        litShadowInfoIndexMap.filterMode = FilterMode.Point;
        AssetDatabase.CreateAsset(litShadowInfoIndexMap, parentPath + "/litShadowInfoLv1.asset");
        litShadowInfoIndexMapNoTextureArray.Apply(false, false);
#if !_ENABLE_BIG_TEX
        litShadowInfoMap.Apply(false, false);
        litShadowInfoMapLv3.Apply(false, false);
#endif

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        System.GC.Collect();
#if _GEN_SCALED_TEX
#endif
#if !_ENABLE_BIG_TEX
        int scaleLv2Fact = 32;
        // scaled lv2 textureArray
#if _GEN_SCALED_TEX
        Texture2DArray scaledTextureArray = new Texture2DArray(scaleLv2Fact * litShadowInfoMapArray.width,
            scaleLv2Fact * litShadowInfoMapArray.height,
            litShadowInfoMapArray.depth,
            TextureFormat.RGBA32, false,
            true);
#endif

        int blockMaxIdx = lv1IntersectedCount / 32 + (lv1IntersectedCount % 32) > 0 ? 1 : 0;// litShadowInfoMap.height / 32 + (litShadowInfoMap.height % 32 > 0 ? 1:0);
        blockMaxIdx = Mathf.Min(blockMaxIdx, textureArraySize);
        for (int blockIdx = 0; blockIdx < blockMaxIdx; blockIdx++)
        {
            var colorBlock = litShadowInfoMap.GetPixels(0, 32 * blockIdx, 32, 32, 0);
            litShadowInfoMapArray.SetPixels(colorBlock, blockIdx, 0);
#if _ENABLE_LV3_MODE
            var colorBlockLv3 = litShadowInfoMapLv3.GetPixels(0, 32 * blockIdx, 32, 32, 0);
            litShadowInfoMapArrayLv3.SetPixels(colorBlockLv3, blockIdx, 0);
#endif
#if _GEN_SCALED_TEX
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
#endif
        }

#if _GEN_SCALED_TEX
        scaledTextureArray.Apply(false, false);
        AssetDatabase.CreateAsset(scaledTextureArray, "Assets/litShadowInfoArrayScaled.asset");
#endif
     
#endif

        litShadowInfoMapArrayLv3.Apply(false, false);
        litShadowInfoMapArrayLv3.wrapMode = TextureWrapMode.Clamp;
        litShadowInfoMapArrayLv3.filterMode = FilterMode.Point;
        AssetDatabase.CreateAsset(litShadowInfoMapArrayLv3, parentPath + "/litShadowInfoMapArrayLv3.asset");

        /*
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
        */

        litShadowInfoMapArray.Apply(false, false);
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        if (litShadowInfoMapArray != null)
        {
            litShadowInfoMapArray.wrapMode = TextureWrapMode.Clamp;
            litShadowInfoMapArray.filterMode = FilterMode.Point;
            //AssetDatabase.CreateAsset(litShadowInfoMapArray, "Assets/litShadowInfoArray.asset");
            //var importer = TextureImporter.GetAtPath("Assets/litShadowInfoArray.asset") as AssetImporter;
            //importer.filterMode = FilterMode.Point;
            //importer.wrapMode = TextureWrapMode.Clamp;
            //importer.SaveAndReimport();
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // setup material property
        litMaterial.SetFloat("_ShadowAlpha", 0.549f);
        litMaterial.SetFloat("_ShadowBias", 8.0f);
        litMaterial.SetFloat("_ShadowBias1", 0.33f);
        litMaterial.SetFloat("_DEBUG_FACT", 0.00f);
        litMaterial.SetFloat("_level1TexSize", litShadowInfoIndexMapSize);
        litMaterial.SetFloat("_level2TexArrayDepth", textureArraySize - 1);

        // lv1VoxelWidth world space
        float lv1VoxelWidth = OrthoProjSize * 2.0f / rootVoxelSize;
        float lv1VoxelWidthInverse = 1 / lv1VoxelWidth;
        float lv1VoxelSizeInverse = 1 / (float)rootVoxelSize;
        // lv2VoxelWidth world space ...
        float lv2VoxelWidth = OrthoProjSize * 2.0f / lv2VoxelSize;
        float lv2VoxelWidthInverse = 1 / lv2VoxelWidth;
        float lv2VoxelSizeInverse = 1 / (float)lv2VoxelSize;
        // lv3VoxelWidth world space ...
        float lv3VoxelWidth = OrthoProjSize * 2.0f / lv3VoxelSize;
        float lv3VoxelWidthInverse = 1 / lv3VoxelWidth;
        float lv3VoxelSizeInverse = 1 / (float)lv3VoxelSize;

        Vector4 _VoxelParams = new Vector4(lv1VoxelWidth, lv1VoxelWidthInverse, rootVoxelSize, lv1VoxelSizeInverse);
        Vector4 _VoxelParamsLv2 = new Vector4(lv2VoxelWidth, lv2VoxelWidthInverse, lv2VoxelSize, lv2VoxelSizeInverse);
        Vector4 _VoxelParamsLv3 = new Vector4(lv3VoxelWidth, lv3VoxelWidthInverse, lv3VoxelSize, lv3VoxelSizeInverse);
        Vector4 _ProjSizeParams = new Vector4(OrthoProjSize, 1 / OrthoProjSize, OrthoProjSize * 2, 1 / (OrthoProjSize * 2));
        litMaterial.SetVector("_VoxelParams", _VoxelParams);
        litMaterial.SetVector("_VoxelParamsLv2", _VoxelParamsLv2);
        litMaterial.SetVector("_VoxelParamsLv3", _VoxelParamsLv3);
        litMaterial.SetVector("_ProjSizeParams", _ProjSizeParams);

        // litShadowInfoLv1
        litMaterial.SetTexture("_Level1IndexMap", litShadowInfoIndexMap);
        litMaterial.SetTexture("_Level2LitShadowInfoArray", litShadowInfoMapArrayLv3);

        //litMaterial.SetTexture("_Shadowmap", shadowmapTex);
        litMaterial.SetTexture("_VoxelShadowmap", shadowmapLite);
        litMaterial.SetTexture("_Shadowmap", shadowmapLite1);

        var vxShadowmapUniformData = new VxShadowmapUniformData()
        {
            SceneName = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name,
            SectionIndex = 0
        };

        vxShadowmapUniformData.FillUniformInfoFromMaterial(litMaterial);
        //AssetDatabase.CreateAsset(vxShadowmapUniformData, parentPath + "/vxShadowmapUniformData.asset");
        

        GameObject go = new GameObject();
        go.name = "vxShadowmapUniformDataLoader";
        var vxShadowmapPrefab = go.AddComponent<VxShadowmap>();
        vxShadowmapPrefab.vxShadowmapUniformData = vxShadowmapUniformData;
        if (vxShadowmapUniformData != null)
        {
            //AssetDatabase.AddObjectToAsset(vxShadowmapUniformData, vxShadowmapPrefab.gameObject);
        }
        PrefabUtility.SaveAsPrefabAsset(vxShadowmapPrefab.gameObject, parentPath + "/vxShadowmap.prefab");
        DestroyImmediate(go);
        
        
        //AssetDatabase.CreateAsset(vxShadowmapPrefab, parentPath + "/vxShadowmap.prefab");

        // System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv1.tga", litShadowInfoIndexMap.EncodeToTGA());
        // System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv1.png", litShadowInfoIndexMap.EncodeToPNG());

        // System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv1NoTexArray.tga", litShadowInfoIndexMapNoTextureArray.EncodeToTGA());

#if !_ENABLE_BIG_TEX
        System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv2.tga", litShadowInfoMap.EncodeToTGA());
        System.IO.File.WriteAllBytes(Application.dataPath + "/litShadowInfoLv2.png", litShadowInfoMap.EncodeToPNG());

#endif


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

    private float DecodeFloatRG(Vector2 enc)
    {
        Vector2 kDecodeDot = new Vector2(1.0f, 1.0f / 255.0f);
        return Vector2.Dot(enc, kDecodeDot);
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
                color *= v / (float)tex.height;
                texCopy.SetPixel(u, v, color);
            }
        }
        texCopy.Apply();
        AssetDatabase.CreateAsset(texCopy, "Assets/texCopy.asset");

    }


    static bool isCameraShaderOverrided = false;
    [MenuItem("Tools/SwitchCameraShader")]
    public static void SwitchCameraShader()
    {
        List<string> propertyFloatOverride = new List<string>()
        {
            "_ShadowAlpha",
            "_ShadowBias",
            "_level1TexSize",
            "_level2TexArrayDepth",
            "_DEBUG_FACT",
        };
        List<string> propertyVectorOverride = new List<string>()
        {
            "_VoxelParams",
            "_VoxelParamsLv2",
            "_VoxelParamsLv3",
            "_ProjSizeParams",
        };
        List<string> propertyMatrixOverride = new List<string>()
        {
            "_LitViewMatrix",
            "_LitProjMatrix",
        };

        List<string> propertyTextureOverride = new List<string>()
        {
            "_Level1IndexMap",
            "_Level2LitShadowInfoArray",
            "_Shadowmap",
        };


        Material mat = ShadowmapBaker.litMaterial;// AssetDatabase.LoadAssetAtPath<Material>("Assets/litNormal.mat");
        propertyFloatOverride.ForEach((fProp) =>
        {
            var f = mat.GetFloat(fProp);
            Shader.SetGlobalFloat(fProp, f);
        });
        propertyVectorOverride.ForEach((vProp) =>
        {
            var v = mat.GetVector(vProp);
            Shader.SetGlobalVector(vProp, v);
        });
        propertyTextureOverride.ForEach((tProp) =>
        {
            var t = mat.GetTexture(tProp);
            Shader.SetGlobalTexture(tProp, t);
        });

        //Shader.SetGlobalVector(propert)

        var vxShader = Shader.Find("Unlit/VxRender");
        //vxShader.
        //var cameras = UnityEditor.SceneView.GetAllSceneCameras();
        //var camera = UnityEditor.SceneView.GetAllSceneCameras()[0];
        if (!isCameraShaderOverrided)
        {
            var views = UnityEditor.SceneView.sceneViews;
            for (int i = 0, c = views.Count; i < c; i++)
            {
                (views[i] as SceneView).SetSceneViewShaderReplace(vxShader, "");
            }
        }
        else
        {
            var views = UnityEditor.SceneView.sceneViews;
            for (int i = 0, c = views.Count; i < c; i++)
            {
                (views[i] as SceneView).SetSceneViewShaderReplace(null, "");
            }
        }

        isCameraShaderOverrided = !isCameraShaderOverrided;

    }

    [MenuItem("Tools/SetMultiLayer")]
    public static void SetMultiLayer()
    {
        Debug.Log(System.Threading.Thread.CurrentThread.ManagedThreadId);
        var taskSche = TaskScheduler.Current;
        var sche = TaskScheduler.FromCurrentSynchronizationContext();
        var task = new Task(() =>
        {
            unsafe
            {
                Debug.Log(System.Threading.Thread.CurrentThread.ManagedThreadId);
            }
        });
        task.Start();
    }
}
