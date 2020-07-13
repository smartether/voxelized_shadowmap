#define _ENABLE_LV3_MODE
//#define _GEN_SCALED_TEX
#define _ENABLE_BIG_TEX
//#define _LV3_OLD_MODE
#define _ENABLE_CUDA
#define _LZ4_COMPRESS_
#define _ENABLE_STRIP_
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public unsafe partial class ShadowmapBaker
{
    const string DLL_NAME = "VoxelizedKernel.dll";
    // 64Bit size memory allocate
    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi, EntryPoint = "malloc")]
    public static extern void* malloc(ulong size);
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "AllocMem")]
    public static extern void* AllocMem(ulong size);
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "FreeMem")]
    public static extern void* FreeMem(void* ptr);
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetSubArray")]
    public static extern void* GetSubArray(void* ptr, long start, long length);

    // Cuda acclerate
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Init")]
    public static extern void Init(uint  targetBufferPoolSize, uint  originBufferPoolSize, uint  targetSize, uint scaler = 2, uint threadNum = 16);
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Close")]
    public static   extern new  void Close();
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Downsample")]
    public static extern int Downsample(void* targetTex, void* originTex, uint targetSize, uint scaler);

    const string DLL_LZ4 = "liblz4.dll";
    //LZ4 compression
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_createStream")]
    public static unsafe extern void* LZ4_createStream();
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_freeStream")]
    public static unsafe extern int LZ4_freeStream(void* streamPtr);
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_createStreamDecode")]
    public static unsafe extern void* LZ4_createStreamDecode();
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_freeStreamDecode")]
    public static unsafe extern int LZ4_freeStreamDecode(void* LZ4_stream);
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_compress_fast_continue")]
    public static unsafe extern int LZ4_compress_fast_continue(void* streamPtr, byte* src, byte* dst, int srcSize, int dstCapacity, int acceleration);
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_decompress_safe_continue")]
    public static unsafe extern int LZ4_decompress_safe_continue(void* LZ4_streamDecode, byte* src, byte* dst, int srcSize, int dstCapacity);
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_compress_default")]
    public static unsafe extern int LZ4_compress_default(byte* src, byte* dst, int srcSize, int dstCapacity);
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_compress_fast")]
    public static unsafe extern int LZ4_compress_fast(byte* src, byte* dst, int srcSize, int dstCapacity, int acceleration);
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_decompress_safe")]
    public static unsafe extern int LZ4_decompress_safe(byte* src, byte* dst, int compressedSize, int dstCapacity);
    [DllImport(DLL_LZ4, CallingConvention = CallingConvention.Cdecl, EntryPoint = "LZ4_compressBound")]
    public static unsafe extern int LZ4_compressBound(int inputSize);

    private void cancelPrecomputeVoxelDepth()
    {
        
    }

    const string LitShadowMapPath = "litshadowmapSSD/";


    // NativeArray不支持超过2G大小 并且 每个元素不可超过2MB
    byte* litShadowInfoArrayLv4Nalayout = null;
    object lockObj = new object();
    int taskProgress = 0;
    List<Task> pendingTask = new List<Task>();
    List<Task> plTasks = new List<Task>();
    CancellationTokenSource cancelAllTask = new CancellationTokenSource();
    TaskScheduler mainTaskScheduler = null;
    TaskScheduler countTaskScheduler = null;
    private void IncreaseProgress()
    {
        lock (lockObj)
        {
            taskProgress++;
        }
    }
    private void ResetProgress()
    {
        taskProgress = 0;
        UnityEditor.EditorUtility.ClearProgressBar();
    }
    private bool WaitPendingTask(int taskTotal, bool block = false, bool cancelable = true, string info= "Calculate", string detail= "Summary Lv4 Voxel to Lv3", List<Task> pendingTask = null, int timeOut = -1)
    {
        pendingTask = pendingTask == null ? this.pendingTask : pendingTask;
        var task = block ? Task.Run(() => { 
            if(timeOut > 0)
                Task.WaitAll(pendingTask.ToArray(), timeOut);
            else
                Task.WaitAll(pendingTask.ToArray());            
        }) : null;
        do
        {
            if (cancelable)
            {
                System.Threading.Thread.Sleep(300);
                if (EditorUtility.DisplayCancelableProgressBar(info, detail, (float)taskProgress / taskTotal))
                {
                    //pendingTask.ForEach((task1) =>
                    //{
                        
                    //});
                    cancelAllTask.Cancel(false);
                    FreeMem(litShadowInfoArrayLv4Nalayout);
                    EditorUtility.ClearProgressBar();
                    return true;
                }
            }
            else
            {
                EditorUtility.DisplayProgressBar(info, detail, (float)taskProgress / taskTotal);
            }
        } while (block && !task.IsCompleted);
        return false;
    }

    // compute voxel on cpu 
    // compute lv3 lit or shadow info first, then summary to lv2 and rootLv1
    unsafe void precomputeVoxelDepth()
    {
        mainTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        EditorUtility.DisplayProgressBar("Calculate", "Start alloc memory", 0.0f);
        int rootVoxelSize = RootVoxelWidthSize;
        int lv2VoxelSize = rootVoxelSize * 2;
        int lv3VoxelSize = lv2VoxelSize * 2;
        int lv4VoxelSize = lv3VoxelSize * 8;
        int rootPixelPerVoxel = 0;
        int lv2PixelPerVoxel = 0;
        int lv3PixelPerVoxel = 0;
        int lv4PixelPerVoxel = 0;

        //var allVoxelLitShadowInfo = AssetDatabase.LoadAllAssetsAtPath("Assets/shadowmap");
        var shadowMapWidth = shadowMap.width;
        rootPixelPerVoxel = shadowMapWidth / RootVoxelWidthSize;
        lv2PixelPerVoxel = rootPixelPerVoxel / 2;
        lv3PixelPerVoxel = lv2PixelPerVoxel / 2;
        lv4PixelPerVoxel = lv3PixelPerVoxel / 8;

        // lv3VoxelBlockInfo 32 * 32 * 32 if root is 8*8*8 .   lv3 4*4*4 voxel == lv1 1*1*1
        int resultTextureSize = lv3VoxelSize;
        // int resultMaxBlockCount = 256 / lv3VoxelSize;
        // Texture2D litShadowInfoMap = new Texture2D(resultTextureSize, resultTextureSize, TextureFormat.ARGB32, false, true);
        int voxelAreaLv4 = lv4VoxelSize * lv4VoxelSize;
        int voxelAreaLv3 = lv3VoxelSize * lv3VoxelSize;
        int voxelAreaLv2 = lv2VoxelSize * lv2VoxelSize;
        int voxelAreaLv1 = rootVoxelSize * rootVoxelSize;
        //Texture2DArray litShadowInfoArrayLv4 = new Texture2DArray(lv4VoxelSize, lv4VoxelSize, lv4VoxelSize, TextureFormat.Alpha8, false, true);
        Texture2DArray litShadowInfoArrayLv3 = new Texture2DArray(lv3VoxelSize, lv3VoxelSize, lv3VoxelSize, TextureFormat.Alpha8, false, true);
        Texture2DArray litShadowInfoArrayLv2 = new Texture2DArray(lv2VoxelSize, lv2VoxelSize, lv2VoxelSize, TextureFormat.Alpha8, false, true);
        Texture2DArray litShadowInfoArrayLv1 = new Texture2DArray(rootVoxelSize, rootVoxelSize, rootVoxelSize, TextureFormat.Alpha8, false, true);


        int lv4VoxelCount = 0;
        bool isLv4VoxelOverflow = false;
        NativeArray<byte> litShadowInfoArrayLv4Na = new NativeArray<byte>();
        try
        {
            checked
            {
                lv4VoxelCount = lv4VoxelSize * lv4VoxelSize * lv4VoxelSize;
            }
            litShadowInfoArrayLv4Na = new NativeArray<byte>(lv4VoxelCount, Allocator.Temp, NativeArrayOptions.ClearMemory);
        }
        catch (System.OverflowException e)
        {
            isLv4VoxelOverflow = true;
        }
        if (isLv4VoxelOverflow)
        {
            ulong lv4VoxelSize64 = (ulong)lv4VoxelSize;
            litShadowInfoArrayLv4Nalayout = (byte*)AllocMem(lv4VoxelSize64 * lv4VoxelSize64 * lv4VoxelSize64);
        }
        EditorUtility.DisplayProgressBar("Calculate", "Start alloc memory", 1.0f);


        var litShadowInfoArrayLv3Na = new NativeArray<byte>(lv3VoxelSize * lv3VoxelSize * lv3VoxelSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        var litShadowInfoArrayLv2Na = new NativeArray<byte>(lv2VoxelSize * lv2VoxelSize * lv2VoxelSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        var litShadowInfoArrayLv1Na = new NativeArray<byte>(rootVoxelSize * rootVoxelSize * rootVoxelSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        List<Object> resourceToRelease = new List<Object>();

        unsafe
        {
            //Texture2D alphaTex =new Texture2D(shadowMap.width, shadowMap.height, TextureFormat.Alpha8, false, true);
            // z-depth 
            int width = shadowMap.width;
            int height = shadowMap.height;
            Init(16, 16, (uint)lv4VoxelSize, (uint)shadowMap.width / (uint)lv4VoxelSize, 16);
            for (int dVoxelIndex = 0, dVoxelMaxIndex = lv4VoxelSize; dVoxelIndex < dVoxelMaxIndex; dVoxelIndex++)
            {
                byte* blockPixels = null;
                if (isLv4VoxelOverflow)
                    blockPixels = (byte*)litShadowInfoArrayLv4Nalayout + (long)voxelAreaLv4 * (long)dVoxelIndex;
                else
                    blockPixels = (byte*)litShadowInfoArrayLv4Na.GetSubArray(voxelAreaLv4 * dVoxelIndex, voxelAreaLv4).GetUnsafeReadOnlyPtr();// litShadowInfoArrayLv4.GetPixels(dVoxelIndex, 0);

                NativeArray<byte> voxelLitShadowInfoNA = new NativeArray<byte>(width * height, Allocator.Persistent);
                byte* ptr = (byte*)voxelLitShadowInfoNA.GetUnsafePtr();


                float startTime = Time.realtimeSinceStartup;

                if (pendingTask.Count > 32)
                {
                    if (WaitPendingTask(lv4VoxelSize, true))
                        return;
                    pendingTask.Clear();

                }
                //WaitPendingTask(lv4VoxelSize, false);


                int dVoxelIndexTmp = dVoxelIndex;
                //System.Threading.ThreadPool.UnsafeQueueUserWorkItem(
                var plTask = Task.Run(() =>
                {
#if !_LZ4_COMPRESS_
                    var fileStream = new System.IO.FileStream(string.Format(LitShadowMapPath + "voxel_lv_{0}.gzip", dVoxelIndexTmp), System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);
                    
                    var gzipStream = new System.IO.Compression.DeflateStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
                    var writeMem = new System.IO.UnmanagedMemoryStream(ptr, 4096, width * height, System.IO.FileAccess.Write);

                    var task = gzipStream.CopyToAsync(writeMem);
                    while (!task.IsCompleted)
                        System.Threading.Thread.Sleep(10);
                    fileStream.Close();
                    gzipStream.Close();
                    writeMem.Close();
#else


#if _READ_FROM_MULIP_FILE
                    using (var fileStream1 = new System.IO.FileStream(string.Format(LitShadowMapPath + "voxel_lv_{0}.lz4", dVoxelIndexTmp), System.IO.FileMode.Open, System.IO.FileAccess.Read))
                    {
                        int textureAreaSize = 4096 * 4096;

                        uint compressedSize = 0;

                        ((byte*)&compressedSize)[0] = (byte)fileStream1.ReadByte();
                        ((byte*)&compressedSize)[1] = (byte)fileStream1.ReadByte();
                        ((byte*)&compressedSize)[2] = (byte)fileStream1.ReadByte();
                        ((byte*)&compressedSize)[3] = (byte)fileStream1.ReadByte();
                        //fileStream1.Seek(4, System.IO.SeekOrigin.Begin);

                        var bufferPtr = (byte*)AllocMem((ulong)compressedSize);
                        var bufferUnmanged = new System.IO.UnmanagedMemoryStream((byte*)bufferPtr, compressedSize, compressedSize, System.IO.FileAccess.Write);

                        try
                        {
                            fileStream1.CopyTo(bufferUnmanged, (int)compressedSize);
                            //LZ4_decompress_safe_continue(lz4Stream, buffer, ptr, 4096 * 4096, 4096 * 4096);
                            int decodeSize = LZ4_decompress_safe((byte*)bufferPtr, ptr, (int)compressedSize, textureAreaSize);
                            //Debug.Log(decodeSize);
                        }
                        finally
                        {
                            FreeMem(bufferPtr);
                            bufferUnmanged.Dispose();
                        }
                    }
#else
                    var fileInfo = new System.IO.FileInfo("E:/Projects/Demo2019_3_2/memoryMappingStripped.data");

                    using (var voxelInfoMemory = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile("E:/Projects/Demo2019_3_2/memoryMappingStripped.data", System.IO.FileMode.Open, "VoxelInfoMapFile", fileInfo.Length, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read))
                    {
                        int textureAreaSize = width * height;

                        uint contentOffset = 0;
                        uint compressedSize = 0;
                        using (var readHead = voxelInfoMemory.CreateViewStream(dVoxelIndexTmp * 8, 8, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read))
                        {
                            ((byte*)&contentOffset)[0] = (byte)readHead.ReadByte();
                            ((byte*)&contentOffset)[1] = (byte)readHead.ReadByte();
                            ((byte*)&contentOffset)[2] = (byte)readHead.ReadByte();
                            ((byte*)&contentOffset)[3] = (byte)readHead.ReadByte();
                            ((byte*)&compressedSize)[0] = (byte)readHead.ReadByte();
                            ((byte*)&compressedSize)[1] = (byte)readHead.ReadByte();
                            ((byte*)&compressedSize)[2] = (byte)readHead.ReadByte();
                            ((byte*)&compressedSize)[3] = (byte)readHead.ReadByte();
                        }
                        using(var readContent = voxelInfoMemory.CreateViewStream(contentOffset, compressedSize, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read))
                        {
                            var bufferPtr = (byte*)AllocMem((ulong)compressedSize);
                            var bufferUnmanged = new System.IO.UnmanagedMemoryStream((byte*)bufferPtr, compressedSize, compressedSize, System.IO.FileAccess.Write);

                            try
                            {
                                readContent.CopyTo(bufferUnmanged, (int)compressedSize);
                                //LZ4_decompress_safe_continue(lz4Stream, buffer, ptr, 4096 * 4096, 4096 * 4096);
                                int decodeSize = LZ4_decompress_safe((byte*)bufferPtr, ptr, (int)compressedSize, textureAreaSize);
                                //Debug.Log(decodeSize);
                            }
                            finally
                            {
                                FreeMem(bufferPtr);
                                bufferUnmanged.Dispose();
                            }
                        }
                    }
#endif
#endif


                    Color32* voxelLitShadowInfoPtr = null;
                    byte* alpha8 = null;

                    try
                    {
                        alpha8 = (byte*)voxelLitShadowInfoNA.GetUnsafePtr<byte>();
#if _ENABLE_CUDA
                        int errorCode = Downsample(blockPixels, alpha8, (uint)lv4VoxelSize, (uint)(width / lv4VoxelSize));
                        if (errorCode != 0)
                            Debug.Log(errorCode);
                    }
                    finally
                    {
                        voxelLitShadowInfoNA.Dispose();
                    }
#else
                    long uvBlockIdxMax = System.Convert.ToInt64(lv4VoxelSize);
                    for (int vBlockIndex = 0, vBlockIdxMax = lv4VoxelSize; vBlockIndex < vBlockIdxMax; vBlockIndex++)
                    {
                        long vBlockIndexL = System.Convert.ToInt64(vBlockIndex);
                        for (int uBlockIndex = 0, uBlockIdxMax = lv4VoxelSize; uBlockIndex < uBlockIdxMax; uBlockIndex++)
                        {
                            long uBlockIndexL = System.Convert.ToInt64(uBlockIndex);
                            int uPixelBase = lv4PixelPerVoxel * uBlockIndex;
                            int vPixelBase = lv4PixelPerVoxel * vBlockIndex;

                            bool isBlockLit = true;
                            bool isBlockShadow = true;
                            for (int vPixelSub = 0, vPixelMax = lv4PixelPerVoxel; vPixelSub < lv4PixelPerVoxel; vPixelSub++)
                            {
                                for (int uPixelSub = 0, uPixelMax = lv4PixelPerVoxel; uPixelSub < lv4PixelPerVoxel; uPixelSub++)
                                {
                                    int vPixel = vPixelBase + vPixelSub;
                                    //vPixel = vBlockIndex * uBlockIdxMax * lv3PixelPerVoxel * lv3PixelPerVoxel;
                                    int uPixel = uPixelBase + uPixelSub;

                                    var pixel = isAlpha8 ? alpha8[vPixel * width + uPixel] / 255.0f : voxelLitShadowInfoPtr[vPixel * width + uPixel].r;
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
                            if (isBlockIntersection)
                            {
                                blockPixels[vBlockIndexL * uvBlockIdxMax + uBlockIndexL] = 128;
                            }
                            if ((blockPixels + vBlockIndexL * uvBlockIdxMax + uBlockIndexL) == null)
                            {
                                Debug.Log(vBlockIndexL + " " + uvBlockIdxMax + " " + uBlockIndexL);
                            }
                            blockPixels[vBlockIndexL * uvBlockIdxMax + uBlockIndexL] = (byte)Mathf.RoundToInt((blockResult * 255));// Color.white * blockResult;
                        }
                    }
#endif
                    IncreaseProgress();
                }, cancelAllTask.Token);

                pendingTask.Add(plTask);

            }

        }
        if (pendingTask.Count > 0)
        {
            if(WaitPendingTask(lv4VoxelSize, true))
            {
                return;
            }
 
            pendingTask.Clear();
        }
        Close();
        ResetProgress();
         

        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        

        
        if (isLv4VoxelOverflow)
        {
            DownSample(lv3VoxelSize, lv4VoxelSize, (byte*)litShadowInfoArrayLv3Na.GetUnsafePtr(), (byte*)litShadowInfoArrayLv4Nalayout, lv4VoxelSize / lv3VoxelSize);
        }
        else
            DownSample(lv3VoxelSize, lv4VoxelSize, (byte*)litShadowInfoArrayLv3Na.GetUnsafePtr(), (byte*)litShadowInfoArrayLv4Na.GetUnsafeReadOnlyPtr(), lv4VoxelSize / lv3VoxelSize);
        for (int depth = 0, maxDepth = litShadowInfoArrayLv3.depth; depth < maxDepth; depth++)
        {
            var subArray = litShadowInfoArrayLv3Na.GetSubArray(depth * voxelAreaLv3, voxelAreaLv3);
            litShadowInfoArrayLv3.SetPixelData<byte>(subArray, 0, depth);
        }
        litShadowInfoArrayLv3.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();


        DownSample(lv2VoxelSize, lv3VoxelSize, (byte*)litShadowInfoArrayLv2Na.GetUnsafePtr(), (byte*)litShadowInfoArrayLv3Na.GetUnsafeReadOnlyPtr());
        for (int depth = 0, maxDepth = litShadowInfoArrayLv2.depth; depth < maxDepth; depth++)
        {
            var subArray = litShadowInfoArrayLv2Na.GetSubArray(depth * voxelAreaLv2, voxelAreaLv2);
            litShadowInfoArrayLv2.SetPixelData<byte>(subArray, 0, depth);
        }
        litShadowInfoArrayLv2.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        DownSample(rootVoxelSize, lv2VoxelSize, (byte*)litShadowInfoArrayLv1Na.GetUnsafePtr(), (byte*)litShadowInfoArrayLv2Na.GetUnsafeReadOnlyPtr());
        //DownSample(rootVoxelSize, lv2VoxelSize, litShadowInfoArrayLv1, litShadowInfoArrayLv2);
        for (int depth = 0, maxDepth = litShadowInfoArrayLv1.depth; depth < maxDepth; depth++)
        {
            var subArray = litShadowInfoArrayLv1Na.GetSubArray(depth * voxelAreaLv1, voxelAreaLv1);
            litShadowInfoArrayLv1.SetPixelData<byte>(subArray, 0, depth);
        }
        litShadowInfoArrayLv1.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        /*
        int albumSie = 64;
        NativeArray<byte> lv4Frame = new NativeArray<byte>(albumSie * albumSie, Allocator.Temp);
        for (int frameIdx = 0; frameIdx < albumSie; frameIdx++)
        {
            using (var memcpyFrom = new System.IO.UnmanagedMemoryStream((byte*)litShadowInfoArrayLv1Na.GetUnsafePtr() + (long)albumSie * (long)albumSie * frameIdx, albumSie * albumSie, albumSie * albumSie * 2, System.IO.FileAccess.Read))
            using (var memcpy = new System.IO.UnmanagedMemoryStream((byte*)lv4Frame.GetUnsafePtr(), albumSie * albumSie, albumSie * albumSie * 2, System.IO.FileAccess.Write))
            {
                memcpyFrom.CopyTo(memcpy, albumSie * albumSie);
                //using (var fs = new System.IO.FileStream("lv4Frame.data", System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
                //{
                //    memcpyFrom.Seek(0, System.IO.SeekOrigin.Begin);
                //    memcpyFrom.CopyTo(fs);
                //}
                Texture2D texDebug = new Texture2D(albumSie, albumSie, TextureFormat.Alpha8, false, true);
                texDebug.SetPixelData<byte>(lv4Frame, 0);
                texDebug.Apply();
                AssetDatabase.CreateAsset(texDebug, string.Format("Assets/lv2Frame/lv4Frame{0}.asset", frameIdx));
            }

        }
        FreeMem(litShadowInfoArrayLv4Nalayout);
        return;
        */
        ResetProgress();
        //setTopVoxelLit(litShadowInfoArrayLv3);
        if (bSetTopIntersectedVoxelLit)
        {
            if (isLv4VoxelOverflow)
                setTopVoxelLit(litShadowInfoArrayLv4Nalayout, lv4VoxelSize, lv4VoxelSize, lv4VoxelSize);
            else
                setTopVoxelLit((byte*)litShadowInfoArrayLv4Na.GetUnsafePtr(), lv4VoxelSize, lv4VoxelSize, lv4VoxelSize);
        }
        //setTopVoxelLit(litShadowInfoArrayRoot);

        if (bExportLvLitShadowInfoTexArray4Dbg)
        {
            AssetDatabase.CreateAsset(litShadowInfoArrayLv1, "Assets/lightInfoArrayLv1.asset");
            AssetDatabase.CreateAsset(litShadowInfoArrayLv2, "Assets/lightInfoArrayLv2.asset");
            AssetDatabase.CreateAsset(litShadowInfoArrayLv3, "Assets/lightInfoArrayLv3.asset");
        }
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        ResetProgress();
        // calculate intersected lv1 voxel count, to construct a lv2 info map
        int[] lv1IntesectedPerlayer = new int[rootVoxelSize];
        int lv1IntersectedCount = SumInfo(rootVoxelSize, litShadowInfoArrayLv1Na, DownSampleOption.SumTargetIntersectedCount, lv1IntesectedPerlayer);
        int[] lv3IntesectedPerlayer = new int[rootVoxelSize];
        int lv3IntersectedCount = SumInfoLv3(lv3VoxelSize, litShadowInfoArrayLv3Na, DownSampleOption.SumTargetIntersectedCount, lv3IntesectedPerlayer);

        //int lv1IntersectedCount1 = SumInfo(rootVoxelSize, litShadowInfoArrayLv1, DownSampleOption.SumTargetIntersectedCount);
        //int lv4IntersectedCount = SumInfo(lv4VoxelSize, litShadowInfoArrayLv4Na, DownSampleOption.SumTargetIntersectedCount);
        //for (int dVoxelIndex = 0, dVoxelMax = rootVoxelSize; dVoxelIndex < dVoxelMax; dVoxelIndex++)
        //{
        //    var litShadowInfoLv1 = litShadowInfoArrayLv1.GetPixels(dVoxelIndex);
        //    for (int pixelIdx = 0, pixelMax = litShadowInfoLv1.Length; pixelIdx < pixelMax; pixelIdx++)
        //    {
        //        Vector4 value = litShadowInfoLv1[pixelIdx];
        //        if (Mathf.Abs(value.w - 0.5f) < 0.1f)
        //        {
        //            lv1IntersectedCount++;
        //        }
        //    }
        //}
        Debug.Log("$$ lv1IntersectedCount:" + lv1IntersectedCount);
        //Debug.Log("$$ lv1IntersectedCount1:" + lv1IntersectedCount1);
        int lv23TextureArraySize = Mathf.CeilToInt(lv1IntersectedCount / 32 + (lv1IntersectedCount % 32 > 0 ? 1 : 0)); //Mathf.NextPowerOfTwo
        Debug.Log("$$ lv23TextureArraySize:" + lv23TextureArraySize);
        // Texture2DArray mode
        int lv4TextureArraySize = Mathf.CeilToInt(lv3IntersectedCount / 64 + (lv3IntersectedCount % 64 > 0 ? 1 : 0));
        Debug.Log("$$ lv4TextureArraySize:" + lv4TextureArraySize);

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
        //Texture2DArray litShadowInfoMapArray = new Texture2DArray(32, 32, lv23TextureArraySize, TextureFormat.RGBA32, false, true);
#if !_ENABLE_BIG_TEX
        Texture2D litShadowInfoMapLv3 = new Texture2D(32, lv1IntersectedCount, TextureFormat.RGBA32, false, true);
#endif

        NativeArray<Color32> litShadowInfoMapArrayLv3Na = new NativeArray<Color32>(32 * 32 * lv23TextureArraySize, Allocator.Temp);
        List<NativeArray<Color32>> litShadowInfoMapArrayLv3NaLstTotal = new List<NativeArray<Color32>>(lv23TextureArraySize);
        for (int i = 0; i < lv23TextureArraySize; i++)
        {
            litShadowInfoMapArrayLv3NaLstTotal.Add(litShadowInfoMapArrayLv3Na.GetSubArray(32 * 32 * i, 32 * 32));
        }
        var indexPixels = litShadowInfoIndexMap.GetPixels(0);
        var indexPixelsNoTexArrayPixels = litShadowInfoIndexMapNoTextureArray.GetPixels(0);

        // init after get a accurate size
        NativeArray<Color32> litShadowInfoMapArrayLv4Na = new NativeArray<Color32>(64 * 64 * lv4TextureArraySize, Allocator.Temp);
        List<NativeArray<Color32>> litShadowInfoMapArrayLv4NaLstTotal = new List<NativeArray<Color32>>(lv4TextureArraySize);
        for(int i = 0; i < lv4TextureArraySize; i++) {
            litShadowInfoMapArrayLv4NaLstTotal.Add(litShadowInfoMapArrayLv4Na.GetSubArray(64 * 64 * i, 64 * 64));
        }
        List<System.IntPtr> litShadowInfoArrayLv4NaLstTotal = new List<System.IntPtr>();
        for (int i = 0; i < lv4VoxelSize; i++)
        {
            if (isLv4VoxelOverflow)
                litShadowInfoArrayLv4NaLstTotal.Add(new System.IntPtr(litShadowInfoArrayLv4Nalayout + (long)voxelAreaLv4 * (long)i));
            else
                litShadowInfoArrayLv4NaLstTotal.Add(new System.IntPtr(litShadowInfoArrayLv4Na.GetSubArray(voxelAreaLv4 * i, voxelAreaLv4).GetUnsafeReadOnlyPtr()));
        }
        MultiCoreMemSetBlack(indexPixels);
        litShadowInfoIndexMap.SetPixels(indexPixels);

        MultiCoreMemSetBlack(indexPixelsNoTexArrayPixels);
        litShadowInfoIndexMapNoTextureArray.SetPixels(indexPixelsNoTexArrayPixels);

        //for (int texArrayDepth = 0, maxDepth = litShadowInfoMapArray.depth; texArrayDepth < maxDepth; texArrayDepth++)
        //{
        //    var pixels1 = litShadowInfoMapArray.GetPixels(texArrayDepth, 0);
        //    MultiCoreMemSetBlack(pixels1);
        //    litShadowInfoMapArray.SetPixels(pixels1, texArrayDepth);

        //}

        //litShadowInfoMapArray.Apply();

        // bake lit shadow info to texture
        unsafe
        {
            var litShadowInfoIndexMapPtr = (Color32*)litShadowInfoIndexMap.GetRawTextureData<Color32>().GetUnsafePtr();
            for (int dVoxelIndex = 0, dVoxelMax = rootVoxelSize; dVoxelIndex < dVoxelMax; dVoxelIndex++)
            {
                int dVoxelIndexTmp = dVoxelIndex;
                var litShadowInfoLv1 = litShadowInfoArrayLv1.GetPixels(dVoxelIndex);

                //for (int dVoxelIndexLv2 = 0, dVoxelMaxLv2 = 2; dVoxelIndexLv2 < dVoxelMaxLv2; dVoxelIndexLv2++)
                //{
                var litShadowInfoLv2_front = litShadowInfoArrayLv2.GetPixels(2 * dVoxelIndex);
                var litShadowInfoLv2_back = litShadowInfoArrayLv2.GetPixels(2 * dVoxelIndex + 1);
                //}
                //var litShadowInfoLv3_front = litShadowInfoArrayLv3.GetPixels(4 * dVoxelIndex);
                Color* litShadowInfoLv3_front = null;
                fixed (Color* litShadowInfoLv3_front_fixed = litShadowInfoArrayLv3.GetPixels(4 * dVoxelIndex))
                    litShadowInfoLv3_front = litShadowInfoLv3_front_fixed;
                Color* litShadowInfoLv3_mid1 = null;
                fixed (Color* litShadowInfoLv3_mid1_fixed = litShadowInfoArrayLv3.GetPixels(4 * dVoxelIndex + 1))
                    litShadowInfoLv3_mid1 = litShadowInfoLv3_mid1_fixed;
                var litShadowInfoLv3_mid2 = litShadowInfoArrayLv3.GetPixels(4 * dVoxelIndex + 2);
                var litShadowInfoLv3_back = litShadowInfoArrayLv3.GetPixels(4 * dVoxelIndex + 3);

       
                int startIntersectCountPerlayerLv1 = 0;
                int startIntersectCountPerlayerLv3 = 0;
                //int endIntersectCountPerlayer = 0;
                for (int layerIdx = 0; layerIdx < dVoxelIndex; layerIdx++)
                {
                    startIntersectCountPerlayerLv1 += lv1IntesectedPerlayer[layerIdx];
                }
                for(int layerIdx = 0, layerMax = dVoxelIndex; layerIdx < layerMax; layerIdx++)
                {
                    startIntersectCountPerlayerLv3 += lv3IntesectedPerlayer[layerIdx];
                }
                //endIntersectCountPerlayer = startIntersectCountPerlayerLv1 + lv1IntesectedPerlayer[dVoxelIndex];
                var taskToGenLitShadowTex = Task.Run(() =>
                {
                    float[] lv3Infos = new float[4];
                    Color32[] colorLine = new Color32[2];
                    List<System.IntPtr> litShadowInfoArrayLstLv4 = new List<System.IntPtr>(8);
                    int queryIdxLv1Tmp = startIntersectCountPerlayerLv1;
                    int texDepthLv1Tmp = queryIdxLv1Tmp / 32;
                    int queryIdxLv4Tmp = startIntersectCountPerlayerLv3;
                    int texDepthLv4Tmp = queryIdxLv4Tmp / 64;
                    for (int vVoxelIndex = 0, vVoxelMax = rootVoxelSize; vVoxelIndex < vVoxelMax; vVoxelIndex++)
                    {
                        int vVoxelIndexTmp = vVoxelIndex;
                        for (int uVoxelIndex = 0, uVoxelMax = rootVoxelSize; uVoxelIndex < uVoxelMax; uVoxelIndex++)
                        {
                            int uVoxelIndexTmp = uVoxelIndex;
                            var lv1 = litShadowInfoLv1[vVoxelIndex * uVoxelMax + uVoxelIndex];
                            var lvIndexMapIndex = dVoxelIndexTmp * vVoxelMax * uVoxelMax + vVoxelIndex * uVoxelMax + uVoxelIndex;
                            var lvIndexMapY = lvIndexMapIndex / litShadowInfoIndexMapSize;
                            var lvIndexMapX = lvIndexMapIndex % litShadowInfoIndexMapSize;
                            // if lv1 voxel is intersected
                            if (Mathf.Abs(lv1.a - 0.5f) < 0.1f)
                            {
                                Color32* colorBlock32x32 = (Color32 *)litShadowInfoMapArrayLv3NaLstTotal[texDepthLv1Tmp].GetUnsafePtr(); //(Color32*)litShadowInfoMapArrayLv3Na.GetSubArray(32 * 32 * texDepthLv1Tmp, 32 * 32).GetUnsafePtr();
                                //int dependedTaskIdx = 0;

                                // litShadowInfoMap.SetPixel(0, queryIdx, new Color(lv1.r, 0,0,0 ), 0);
                                int lv2MemLocBase = (vVoxelIndexTmp * uVoxelMax + uVoxelIndexTmp) * 4;
                                //var lv2FrontRGBA = new Color();
                                //var lv2BackRGBA = new Color();

                                for (int vPixelIndex = 0, vPixelMax = 2; vPixelIndex < vPixelMax; vPixelIndex++)
                                {
                                    for (int uPixelIndex = 0, uPixelMax = 2; uPixelIndex < uPixelMax; uPixelIndex++)
                                    {
                                        int vFinal = (vVoxelIndexTmp * uVoxelMax * 4) + uVoxelIndexTmp * 2 + vPixelIndex * uVoxelMax * 2 + uPixelIndex;
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

                                        if (((Mathf.Abs(frontColor.a - 1) < 0.1f && Mathf.Abs(backColor.a - 1) < 0.1f) ||
                                            (Mathf.Abs(frontColor.a - 0) < 0.1f && Mathf.Abs(backColor.a - 0) < 0.1f)))
                                        {

                                            Color colorLv3 = new Color(frontColor.a, frontColor.a, frontColor.a, frontColor.a);
                                            for (int vPixelIndexLv3 = 0, vPixelMaxLv3 = 2; vPixelIndexLv3 < vPixelMaxLv3; vPixelIndexLv3++)
                                            {
                                                for (int uPixelIndexLv3 = 0, uPixelMaxLv3 = 2; uPixelIndexLv3 < uPixelMaxLv3; uPixelIndexLv3++)
                                                {
                                                    int pixelIdx = 8 * vPixelIndex + uPixelIndex * 4 + 2 * vPixelIndexLv3 + uPixelIndexLv3;
                                                    colorBlock32x32[queryIdxLv1Tmp % 32 * 32 + pixelIdx] = colorLv3;

                                                }
                                            }
                                        }
                                        else
                                        {
                                            Color32* colorBlock64x64 = null;
                                            for (int vPixelIndexLv3 = 0, vPixelMaxLv3 = 2; vPixelIndexLv3 < vPixelMaxLv3; vPixelIndexLv3++)
                                            {
                                                for (int uPixelIndexLv3 = 0, uPixelMaxLv3 = 2; uPixelIndexLv3 < uPixelMaxLv3; uPixelIndexLv3++)
                                                {
                                                    colorBlock64x64 = (Color32*)litShadowInfoMapArrayLv4NaLstTotal[texDepthLv4Tmp].GetUnsafePtr();
                                                    int finalLv3Y = vVoxelIndexTmp * 4 + vPixelIndex * 2 + vPixelIndexLv3;
                                                    int finalLv3X = uVoxelIndexTmp * 4 + uPixelIndex * 2 + uPixelIndexLv3;
                                                    //  4 * uPixelMax  * uVoxelMax * vPixelIndex   + 4 * uVoxelIndex 
                                                    int vFinalLv3 = 4 * uVoxelMax * finalLv3Y + finalLv3X; //(vVoxelIndex * uVoxelMax * 16) +  4 * (1 - vPixelIndex) + 8 * uVoxelMax * vPixelIndex + 4 * uVoxelIndex + uPixelIndex * 2 + uPixelMax * uPixelMaxLv3 * uVoxelMax * vPixelIndexLv3 + uPixelIndexLv3;
                                                    var lv3_front = litShadowInfoLv3_front[vFinalLv3];
                                                    var lv3_mid1 = litShadowInfoLv3_mid1[vFinalLv3];
                                                    var lv3_mid2 = litShadowInfoLv3_mid2[vFinalLv3];
                                                    var lv3_back = litShadowInfoLv3_back[vFinalLv3];

                                                    Color colorLv3 = new Color(lv3_front.a, lv3_mid1.a, lv3_mid2.a, lv3_back.a);
                                                    // pixel u
                                                    int pixelIdx = 8 * vPixelIndex + uPixelIndex * 4 + 2 * vPixelIndexLv3 + uPixelIndexLv3;

                                                    colorBlock32x32[queryIdxLv1Tmp % 32 * 32 + pixelIdx] = colorLv3;
                                                    // pixel u for lv4 query
                                                    int pixelIdxLv4 = 16 + pixelIdx;

                                                    lv3Infos[0] = lv3_front.a;
                                                    lv3Infos[1] = lv3_mid1.a;
                                                    lv3Infos[2] = lv3_mid2.a;
                                                    lv3Infos[3] = lv3_back.a;



                                                    bool hasInstected = false;
                                                    for (int axeId = 0, c = 4; axeId < c; axeId++)
                                                    {
                                                        hasInstected = hasInstected || Mathf.Abs(lv3Infos[axeId] - 0.5f) < 0.1;

                                                    }
                                                    if (hasInstected)
                                                    {
                                                        int lv4ShadowedCount = 0;
                                                        for (int axeId = 0, c = 4; axeId < c; axeId++)
                                                        {
                                                            if (Mathf.Abs(lv3Infos[axeId] - 0.5f) < 0.1)
                                                            {
                                                                litShadowInfoArrayLstLv4.Clear();
                                                                for (int lv4D = dVoxelIndexTmp * 2 * 2 * 8 + axeId * 8, lv4DepthMax = lv4D + 8; lv4D < lv4DepthMax; lv4D++)
                                                                {
                                                                    litShadowInfoArrayLstLv4.Add(litShadowInfoArrayLv4NaLstTotal[lv4D]); //(litShadowInfoArrayLv4Na.GetSubArray(voxelAreaLv4 * lv4D, voxelAreaLv4));
                                                                }
                                                                for (int vPixelIndexLv4 = 0, vPixelMaxLv4 = 8; vPixelIndexLv4 < vPixelMaxLv4; vPixelIndexLv4++)
                                                                {
                                                                    colorLine[0] = new Color32(0,0,0,0);
                                                                    colorLine[1] = new Color32(0,0,0,0);
                                                                    for (int uPixelIndexLv4 = 0, uPixelMaxLv4 = 8; uPixelIndexLv4 < uPixelMaxLv4; uPixelIndexLv4++)
                                                                    {
                                                                        
                                                                        for (int dPixelIndexLv4 = 0, dPixelMaxLv4 = 8; dPixelIndexLv4 < dPixelMaxLv4; dPixelIndexLv4++)
                                                                        {
                                                                            var litShadowInfoArrayLv4Sub = (byte*)litShadowInfoArrayLstLv4[dPixelIndexLv4].ToPointer();
                                                                            int lv4V = vVoxelIndex * 2 * 2 * 8 + 2 * 8 * vPixelIndex + 8 * vPixelIndexLv3 + vPixelIndexLv4;
                                                                            int lv4U = uVoxelIndex * 2 * 2 * 8 + 2 * 8 * uPixelIndex + 8 * uPixelIndexLv3 + uPixelIndexLv4;
                                                                            //bool isLit = litShadowInfoArrayLv4Sub[lv4VoxelSize * vVoxelIndex * 2 * 2 * 8;
                                                                            bool isLit = Mathf.Abs(litShadowInfoArrayLv4Sub[lv4V * lv4VoxelSize + lv4U] - 255) < 20;
                                                                            bool isShadow = Mathf.Abs(litShadowInfoArrayLv4Sub[lv4V * lv4VoxelSize + lv4U]) < 20;
                                                                            if (isShadow)
                                                                            {
                                                                                lv4ShadowedCount++;
                                                                            }
                                                                            bool isIntersected = !isLit && !isShadow;
                                                                            //isLit = isLit || isIntersected;
                                                                            isLit = isLit && !isIntersected;
                                                                            isShadow = isIntersected;
                                                                            //channel |= (byte)((isLit ? 1u : 0u) << dPixelIndexLv4);
                                                                            var color = colorLine[uPixelIndexLv4 / 4];
                                                                            color[uPixelIndexLv4 % 4] |= (byte)((isLit ? 1u : 0u) << dPixelIndexLv4);
                                                                            colorLine[uPixelIndexLv4 / 4] = color;
                                                                            
                                                                            //int litShadowInfoMapArrayLv4Idx = queryIdxLv4Tmp * 
                                                                            //litShadowInfoMapArrayLv4NaPtr[]

                                                                        }
                                                                    }

                                                                    int V64x64 = queryIdxLv4Tmp % 64;
                                                                    int u64x64 = 16 * axeId + vPixelIndexLv4 * 2;
                                                                    colorBlock64x64[V64x64 * 64 + u64x64] = colorLine[0];
                                                                    colorBlock64x64[V64x64 * 64 + u64x64 + 1] = colorLine[1];
                                                                }

                                                            }
                                                        }

                                                        //int lv4VPixel = queryIdxLv4Tmp % 64;
                                                        float fLv4v = (float)(queryIdxLv4Tmp % 64) / 63.0f;
                                                        //int lv4Depth = queryIdxLv4Tmp / 64;
                                                        float fLv4Depth = (float)(queryIdxLv4Tmp / 64) / (float)(lv4TextureArraySize - 1);
                                                        Vector2 lv4vEncoded = EncodeFloatRG(fLv4v);
                                                        Vector2 lv4DepthEncoded = EncodeFloatRG(fLv4Depth);
                                                        Color lv4UV = new Color(fLv4v, 0, lv4DepthEncoded.x, lv4DepthEncoded.y);
                                                        colorBlock32x32[queryIdxLv1Tmp % 32 * 32 + pixelIdxLv4] = lv4UV;

                                                        queryIdxLv4Tmp++;
                                                        if (texDepthLv4Tmp != Mathf.Min(lv4TextureArraySize - 1, queryIdxLv4Tmp / 64))
                                                        {
                                                            texDepthLv4Tmp = Mathf.Min(lv4TextureArraySize - 1, queryIdxLv4Tmp / 64);
                                                        }
                                                       
                                                        
                                                        //texDepthLv4Tmp = queryIdxLv4Tmp / 64;
                                                    }

                                                }
                                            }

                                        }

                                        //int seqFront = vPixelIndex * uPixelMax + uPixelIndex;
                                        //int seqBack = seqFront + 4;


                                    }
                                }

                                float texV = (queryIdxLv1Tmp % 32) / 31.0f;// 32.0f + 0.01f;  //(queryIdx % (lv1IntersectedCount * 0.5f))  / ((float)lv1IntersectedCount); //queryIdx / (float)lv1IntersectedCount;  //
                                Vector4 v = EncodeFloatRG(texV);

                                float arrayIndex = (queryIdxLv1Tmp / 32) / (float)(lv23TextureArraySize - 1);
                                Vector2 arrayIndexRG = EncodeFloatRG(arrayIndex);
                                // R: lit or Shadow GB: v A : textureArray index  setup after lv2 info summarized

                                //litShadowInfoIndexMap.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.a, texV, arrayIndexRG.x, arrayIndexRG.y));// //new Color(lv1.a, v.x, v.y, isHalf), 0);
                                litShadowInfoIndexMapPtr[lvIndexMapY * litShadowInfoIndexMapSize + lvIndexMapX] = new Color(lv1.a, texV, arrayIndexRG.x, arrayIndexRG.y);
#if !_ENABLE_BIG_TEX
                        texV = (queryIdx % (lv1IntersectedCount / 2)) / (float)lv1IntersectedCount;
                        float isHalf = queryIdx > lv1IntersectedCount / 2 ? 1 : 0;
                        v = EncodeFloatRG(texV);
                        litShadowInfoIndexMapNoTextureArray.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.a, isHalf, v.x, v.y));
#endif



#if _ENABLE_BIG_TEX
                                //queryIdxLv1++;
                                queryIdxLv1Tmp++;
                                if (texDepthLv1Tmp != queryIdxLv1Tmp / 32)
                                {
                                    // litShadowInfoMapArrayLv3.SetPixels(colorBlock32x32, texDepth, 0);
                                    // var subArray32x32 = litShadowInfoMapArrayLv3Na.GetSubArray(32 * 32 * texDepthLv1, 32 * 32);
                                    // subArray32x32.CopyFrom(colorBlock32x32);
                                    texDepthLv1Tmp = Mathf.Min(lv23TextureArraySize - 1, queryIdxLv1Tmp / 32);
                                    // colorBlock32x32 = litShadowInfoMapArrayLv3.GetPixels(texDepth, 0);

                                }
#endif
                            }
                            else
                            {
                                Vector4 v = EncodeFloatRG(lv1.a > 0.9 ? 20000 : 10000);
                                //litShadowInfoIndexMap.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.a, 0, 0, 0), 0);
                                litShadowInfoIndexMapPtr[lvIndexMapY * litShadowInfoIndexMapSize + lvIndexMapX] = new Color(lv1.a, 0, 0, 0);
#if !_ENABLE_BIG_TEX
                        litShadowInfoIndexMapNoTextureArray.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.a, 0, 0, 0), 0);
#endif
                            }

                        }   // End for uDir
                    }   // End for vDir

                    IncreaseProgress();
                }, cancelAllTask.Token);// End Task

                pendingTask.Add(taskToGenLitShadowTex);


                if (pendingTask.Count > 8)
                {
                    Task.WaitAll(pendingTask.ToArray());
                    if (WaitPendingTask(rootVoxelSize, true, true, "Calculating", "Generating VoxelInfo Texture"))
                        return;
                    pendingTask.Clear();

                    WaitPendingTask(rootVoxelSize, false, true, "Calculating", "Generating VoxelInfo Texture");
                    Resources.UnloadUnusedAssets();
                    System.GC.Collect();
                }
                
                //AssetDatabase.DeleteAsset("Assets/runtimeLitShadowInfo.asset");
                //AssetDatabase.CreateAsset(litShadowInfoMap, "Assets/runtimeLitShadowInfo.asset");
            }
        }

        if (WaitPendingTask(rootVoxelSize, true, true, "Calculating", "Generating VoxelInfo Texture"))
            return;
        ResetProgress();
        pendingTask.Clear();


#if _ENABLE_STRIP_
        // summary lv4 voxel info

        //List<System.IntPtr> texLines16 = new List<IntPtr>(16);
        List<System.IntPtr> texLines64 = new List<IntPtr>(64);
        for(int lv4InfoMapIdx = 0, lv4InfoMapMax = litShadowInfoMapArrayLv4NaLstTotal.Count;lv4InfoMapIdx < lv4InfoMapMax; lv4InfoMapIdx++)
        {
            var lv4InfoSubMap = litShadowInfoMapArrayLv4NaLstTotal[lv4InfoMapIdx];
            for (int curLineIdx = 0, curLineMax = 64; curLineIdx < curLineMax; curLineIdx++)
            {
                var curLine = (UInt32*)lv4InfoSubMap.GetUnsafePtr() + 64 * curLineIdx;
                bool isExist = false;
                int matchedLineIdx = 0;
                for (int lineIdx = 0, lineMax = texLines64.Count; lineIdx < lineMax; lineIdx++)
                {
                    var line = (UInt32*)texLines64[lineIdx].ToPointer();
                    UInt32 sum = 0;
                    for (int pixelIdx = 0, pixelMax = 64; pixelIdx < pixelMax; pixelIdx++)
                    {
                        sum += (line[pixelIdx] == curLine[pixelIdx]) ? 1u : 0u;
                    }
                    isExist = sum == 64;
                    if (isExist)
                    {
                        matchedLineIdx = lineIdx;
                        break;
                    }
                }
                if (!isExist)
                {
                    byte* dicInfo = (byte*)AllocMem((ulong)(4 * 64));
                    using (var read = new System.IO.UnmanagedMemoryStream((byte*)curLine, 4 * 64))
                    {
                        using(var write = new System.IO.UnmanagedMemoryStream((byte*)dicInfo, 4 * 64, 4 * 64, System.IO.FileAccess.Write))
                        {
                            read.CopyTo(write);
                        }
                    }
                    texLines64.Add(new IntPtr(dicInfo));
                    matchedLineIdx = texLines64.Count - 1;
                }
                //redirect uv
                curLine[0] = (uint)matchedLineIdx / 64;
                curLine[1] = (uint)matchedLineIdx % 64;
            }
        }

        Debug.Log("$$$ total uniqal line :" + texLines64.Count);
        //strip redundancy info & redirect uv
        int lv4TextureArraySizeFinal = Mathf.CeilToInt(Mathf.Min(2048, texLines64.Count / 64 + (texLines64.Count % 64 > 0 ? 1 : 0)));
        Debug.Log("$$$ lv4TextureArraySizeFinal: " + lv4TextureArraySizeFinal);
        Texture2DArray litShadowInfoMapArrayLv4 = new Texture2DArray(64, 64, lv4TextureArraySizeFinal, TextureFormat.RGBA32, false, true);
        var litShadowInfoMapArrayLv4NaFinal = new NativeArray<Color32>(64 * 64 * lv4TextureArraySizeFinal, Allocator.Temp);
        for(int copySubArrayIdx=0,copySubArrayMax=texLines64.Count;copySubArrayIdx < copySubArrayMax; copySubArrayIdx++)
        {
            using(var copyOpt = new System.IO.UnmanagedMemoryStream((byte*)texLines64[copySubArrayIdx].ToPointer(), 64 * 4, 64 * 4, System.IO.FileAccess.Read))
            {
                using(var copyTo = new System.IO.UnmanagedMemoryStream((byte*)litShadowInfoMapArrayLv4NaFinal.GetUnsafePtr() + 64 * 4 * copySubArrayIdx, 64 * 4, 64 * 4, System.IO.FileAccess.Write))
                {
                    copyOpt.CopyTo(copyTo);
                }
            }
        }

        for(int lv3InfoMapIdx=0,lv3InfoMapMax= litShadowInfoMapArrayLv3NaLstTotal.Count; lv3InfoMapIdx < lv3InfoMapMax; lv3InfoMapIdx++)
        {
            var lv3Info = (Color32*)litShadowInfoMapArrayLv3NaLstTotal[lv3InfoMapIdx].GetUnsafePtr();
            for(int subIdx = 0, subIdxMax = 32; subIdx < subIdxMax; subIdx++)
            {
                var uvInfo = lv3Info + subIdx * 32 + 16;
                for(int uvIdx=0,uvIdxMax=16;uvIdx< uvIdxMax; uvIdx++)
                {
                    Color uv = uvInfo[uvIdx];
                    float v = uv.r;
                    float depth = DecodeFloatRG(new Vector2(uv.b, uv.a));
                    int nV = Mathf.RoundToInt(v * 63);
                    int nDepth = Mathf.RoundToInt(depth * (float)(lv4TextureArraySize - 1));
                    uint* line = (uint*)litShadowInfoMapArrayLv4NaLstTotal[nDepth].GetUnsafePtr() + nV * 64;
                    uint nNewDepth = (uint)line[0];
                    uint nNewV = (uint)line[1];
                    float fNewDepth = nNewDepth / (float)(lv4TextureArraySizeFinal - 1);
                    float fNewV = (float)nNewV / 63.0f;
                    Vector2 encodedDepth = EncodeFloatRG(fNewDepth);
                    uvInfo[uvIdx] = new Color(fNewV, 0, encodedDepth.x, encodedDepth.y);
                }
            }
        }

        texLines64.ForEach((texLine) =>
        {
            FreeMem(texLine.ToPointer());
        });

        
        // strip lv2-3
        List<System.IntPtr> texLines32 = new List<IntPtr>(32);
        for (int lv3Idx = 0, lv3IdxMax = lv23TextureArraySize; lv3Idx < lv3IdxMax; lv3Idx++)
        {
            var lv3InfoSubMap = (uint*)litShadowInfoMapArrayLv3NaLstTotal[lv3Idx].GetUnsafePtr();
            for (int curLineIdx = 0, curLineMax = 32; curLineIdx < curLineMax; curLineIdx++)
            {
                var curLine = lv3InfoSubMap + 32 * curLineIdx;
                bool isExist = false;
                int matchedLineIdx = 0;
                for (int lineIdx = 0, lineMax = texLines32.Count; lineIdx < lineMax; lineIdx++)
                {
                    var line = (UInt32*)texLines32[lineIdx].ToPointer();
                    UInt32 sum = 0;
                    for (int pixelIdx = 0, pixelMax = 32; pixelIdx < pixelMax; pixelIdx++)
                    {
                        sum += (line[pixelIdx] == curLine[pixelIdx]) ? 1u : 0u;
                    }
                    isExist = sum == 32;
                    if (isExist)
                    {
                        matchedLineIdx = lineIdx;
                        break;
                    }
                }
                if (!isExist)
                {
                    byte* dicInfo = (byte*)AllocMem((ulong)(4 * 32));
                    using (var read = new System.IO.UnmanagedMemoryStream((byte*)curLine, 4 * 32))
                    {
                        using (var write = new System.IO.UnmanagedMemoryStream((byte*)dicInfo, 4 * 32, 4 * 32, System.IO.FileAccess.Write))
                        {
                            read.CopyTo(write);
                        }
                    }
                    texLines32.Add(new IntPtr(dicInfo));
                    matchedLineIdx = texLines32.Count - 1;
                }
                //redirect uv
                curLine[0] = (uint)matchedLineIdx / 32;
                curLine[1] = (uint)matchedLineIdx % 32;
            }
        }

        Debug.Log("$$$ lv23 stripped redundancy : " + texLines32.Count);
        //strip redundancy info & redirect uv
        int lv3TextureArraySizeFinal = Mathf.CeilToInt(Mathf.Min(2048, texLines32.Count / 32 + (texLines32.Count % 32 > 0 ? 1 : 0)));
        Debug.Log("$$$ lv3TextureArraySizeFinal: " + lv3TextureArraySizeFinal);
        var litShadowInfoMapArraylv3NaFinal = new NativeArray<Color32>(32 * 32 * lv3TextureArraySizeFinal, Allocator.Temp);
        for (int copySubArrayIdx = 0, copySubArrayMax = texLines32.Count; copySubArrayIdx < copySubArrayMax; copySubArrayIdx++)
        {
            using (var copyOpt = new System.IO.UnmanagedMemoryStream((byte*)texLines32[copySubArrayIdx].ToPointer(), 32 * 4, 32 * 4, System.IO.FileAccess.Read))
            {
                using (var copyTo = new System.IO.UnmanagedMemoryStream((byte*)litShadowInfoMapArraylv3NaFinal.GetUnsafePtr() + 32 * 4 * copySubArrayIdx, 32 * 4, 32 * 4, System.IO.FileAccess.Write))
                {
                    copyOpt.CopyTo(copyTo);
                }
            }
        }


        var lv1LitShadowInfoIndexMapPtr = (Color32*)litShadowInfoIndexMap.GetRawTextureData<Color32>().GetUnsafePtr();
        for (int lv1InfoMapIdx = 0, lv1InfoMapMax = litShadowInfoIndexMapSize * litShadowInfoIndexMapSize; lv1InfoMapIdx < lv1InfoMapMax; lv1InfoMapIdx++)
        {
            var lv1Info = lv1LitShadowInfoIndexMapPtr[lv1InfoMapIdx];
            Color uv = lv1Info;
            float litOrShadow = uv.r;
            float v = uv.g;
            float depth = DecodeFloatRG(new Vector2(uv.b, uv.a));
            int nV = Mathf.RoundToInt(v * 31.0f);
            int nDepth = Mathf.RoundToInt(depth * (float)(lv23TextureArraySize - 1));
            uint* line = (uint*)litShadowInfoMapArrayLv3NaLstTotal[nDepth].GetUnsafePtr() + nV * 32;
            uint nNewDepth = (uint)line[0];
            uint nNewV = (uint)line[1];
            float fNewDepth = nNewDepth / (float)(lv3TextureArraySizeFinal - 1);
            float fNewV = (float)nNewV / 31.0f;
            Vector2 encodedDepth = EncodeFloatRG(fNewDepth);
            lv1LitShadowInfoIndexMapPtr[lv1InfoMapIdx] = new Color(litOrShadow, fNewV, encodedDepth.x, encodedDepth.y);
        }

        texLines32.ForEach((texLine) =>
        {
            FreeMem(texLine.ToPointer());
        });
        
#else
        Texture2DArray litShadowInfoMapArrayLv4 = new Texture2DArray(64, 64, lv4TextureArraySize, TextureFormat.RGBA32, false, true);
#endif
#if _ENABLE_STRIP_
        Texture2DArray litShadowInfoMapArrayLv3 = new Texture2DArray(32, 32, lv3TextureArraySizeFinal, TextureFormat.RGBA32, false, true);
#else
        Texture2DArray litShadowInfoMapArrayLv3 = new Texture2DArray(32, 32, lv23TextureArraySize, TextureFormat.RGBA32, false, true);
#endif
        
        for (int texArrayDepth = 0, maxDepth = litShadowInfoMapArrayLv3.depth; texArrayDepth < maxDepth; texArrayDepth++)
        {
            var pixels2 = litShadowInfoMapArrayLv3.GetPixels(texArrayDepth, 0);
            MultiCoreMemSetBlack(pixels2);
            litShadowInfoMapArrayLv3.SetPixels(pixels2, texArrayDepth);
            litShadowInfoMapArrayLv3.Apply(false, false);
        }


#if _ENABLE_BIG_TEX
        //litShadowInfoMapArrayLv3.SetPixels(colorBlock32x32, Mathf.Min(texDepth, litShadowInfoMapArrayLv3.depth - 1), 0);
        {
            //var subArray = litShadowInfoMapArrayLv3Na.GetSubArray(32 * 32 * Mathf.Min(texDepthLv1, litShadowInfoMapArrayLv3.depth - 1), 32 * 32);
            //subArray.CopyFrom(colorBlock32x32);
            for (int copyDepth = 0, maxDepth = litShadowInfoMapArrayLv3.depth; copyDepth < maxDepth; copyDepth++)
            {
#if _ENABLE_STRIP_
                litShadowInfoMapArrayLv3.SetPixelData<Color32>(litShadowInfoMapArraylv3NaFinal.GetSubArray(32 * 32 * copyDepth, 32 * 32), 0, copyDepth);
#else
                litShadowInfoMapArrayLv3.SetPixelData<Color32>(litShadowInfoMapArrayLv3Na.GetSubArray(32 * 32 * copyDepth, 32 * 32), 0, copyDepth);
#endif
            }

            for (int copyDepth = 0, maxDepth = litShadowInfoMapArrayLv4.depth; copyDepth < maxDepth; copyDepth++)
            {
#if _ENABLE_STRIP_
                litShadowInfoMapArrayLv4.SetPixelData<Color32>(litShadowInfoMapArrayLv4NaFinal.GetSubArray(64 * 64 * copyDepth, 64 * 64), 0, copyDepth);
#else
                litShadowInfoMapArrayLv4.SetPixelData<Color32>(litShadowInfoMapArrayLv4Na.GetSubArray(64 * 64 * copyDepth, 64 * 64), 0, copyDepth);
#endif
            }
        }
#endif

                var savePathAsset = EditorUtility.SaveFolderPanel("保存路径", Application.dataPath, "");
        var parentPath = FileUtil.GetProjectRelativePath(savePathAsset);

        litShadowInfoArrayLv1Na.Dispose();
        litShadowInfoArrayLv2Na.Dispose();
        litShadowInfoArrayLv3Na.Dispose();


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

        FreeMem(litShadowInfoArrayLv4Nalayout);
        System.GC.Collect();

        litShadowInfoMapArrayLv3.Apply(false, false);
        litShadowInfoMapArrayLv3.wrapMode = TextureWrapMode.Clamp;
        litShadowInfoMapArrayLv3.filterMode = FilterMode.Point;
        AssetDatabase.CreateAsset(litShadowInfoMapArrayLv3, parentPath + "/litShadowInfoMapArrayLv3.asset");

        litShadowInfoMapArrayLv4.Apply(false, false);
        litShadowInfoMapArrayLv4.wrapMode = TextureWrapMode.Clamp;
        litShadowInfoMapArrayLv4.filterMode = FilterMode.Point;
        AssetDatabase.CreateAsset(litShadowInfoMapArrayLv4, parentPath + "/litShadowInfoMapArrayLv4.asset");

        //litShadowInfoMapArray.Apply(false, false);
        //AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        //if (litShadowInfoMapArray != null)
        //{
        //    litShadowInfoMapArray.wrapMode = TextureWrapMode.Clamp;
        //    litShadowInfoMapArray.filterMode = FilterMode.Point;
        //}
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // setup material property
        litMaterial.SetFloat("_ShadowAlpha", 0.549f);
        litMaterial.SetFloat("_ShadowBias", 0.0f);
        litMaterial.SetFloat("_ShadowBias1", 0.33f);
        litMaterial.SetFloat("_DEBUG_FACT", 0.00f);
        litMaterial.SetFloat("_level1TexSize", litShadowInfoIndexMapSize);
        litMaterial.SetFloat("_level2TexArrayDepth", lv3TextureArraySizeFinal - 1);
#if _ENABLE_STRIP_
        litMaterial.SetFloat("_level4TexArrayDepth", lv4TextureArraySizeFinal - 1);
#else
        litMaterial.SetFloat("_level4TexArrayDepth", lv4TextureArraySize - 1);
#endif

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
        litMaterial.SetTexture("_Level4LitShadowInfoArray", litShadowInfoMapArrayLv4);

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


    }

    [MenuItem("Tools/TestAlpha8")]
    public static void TestAlpha8()
    {
        Texture2DArray litShadowInfoArrayLv2 = new Texture2DArray(64, 64, 64, TextureFormat.Alpha8, false, true);
        var na = new NativeArray<byte>(64 * 64 * 64, Allocator.Temp);
        var subArray = na.GetSubArray(1024, 2048);
        int maxDepth = litShadowInfoArrayLv2.depth;
        var task = Task.Run(() =>
        {
            for (int i = 0, c = subArray.Length; i < c; i++)
            {
                subArray[i] = 128;
            }
            litShadowInfoArrayLv2.SetPixelData<byte>(na, 0, maxDepth - 1);
        });
        task.Wait();
        AssetDatabase.CreateAsset(litShadowInfoArrayLv2, "Assets/TestSubArray.asset");

    }





    public enum DownSampleOption
    {
        None,
        SumTargetIntersectedCount,
        SumTargetLitCount,
        SumTargetShadowedCount,
    }

    private int SumInfo(int size, Texture2DArray litShadowInfoArray, DownSampleOption downsampleOption = DownSampleOption.None, int[] sumPerLayer = null)
    {
        if (sumPerLayer != null)
        {
            UnityEngine.Assertions.Assert.AreEqual(sumPerLayer.Length, size, "$$ size of sumPerLayer should be equal to parm size");
        }
        int areaSize = size * size;
        List<Task<int>> pendingTask = new List<Task<int>>();
        for (int i = 0; i < size; i++)
        {
            int iTmp = i;
            var subArrayPtr = litShadowInfoArray.GetPixels(i);
            var task = Task.Run<int>(() =>
            {
                unsafe
                {
                    int sumSub = 0;
                    for (int idx = 0; idx < areaSize; idx++)
                    {
                        bool prebool = false;
                        prebool |= (DownSampleOption.SumTargetIntersectedCount == downsampleOption) && Mathf.Abs(subArrayPtr[idx].a - 0.5f) < 0.1;
                        prebool |= (DownSampleOption.SumTargetLitCount == downsampleOption) && Mathf.Abs(subArrayPtr[idx].a - 1) < 0.1;
                        prebool |= (DownSampleOption.SumTargetShadowedCount == downsampleOption) && subArrayPtr[idx].a < 0.1;
                        if (prebool)
                        {
                            sumSub++;
                        }
                    }
                    if(sumPerLayer != null)
                    {
                        sumPerLayer[iTmp] = sumSub;
                    }
                    return sumSub;
                }
            });

        }
        Task.WaitAll(pendingTask.ToArray());

        int Sum = 0;
        pendingTask.ForEach((t) =>
        {
            Sum += t.Result;
        });
        return Sum;
    }


    private int SumInfoLv3(int size, NativeArray<byte> litShadowInfoArray, DownSampleOption downsampleOption = DownSampleOption.None, int[] sumPerLayer = null)
    {
        if (sumPerLayer != null)
        {
            UnityEngine.Assertions.Assert.AreEqual(4 * sumPerLayer.Length, size, "$$ size of sumPerLayer should be equal to parm size");
        }
        int areaSize = size * size;
        List<Task<int>> pendingTask = new List<Task<int>>();
        for (int i = 0; i < size / 4; i++)
        {
            int iTmp = i;
            int maxLen = litShadowInfoArray.Length;
            var task = Task.Run<int>(() =>
            {
                unsafe
                {
                    int sumSub = 0;

                    List<NativeArray<byte>> litShadowInfoArrayLst = new List<NativeArray<byte>>(4);
                    litShadowInfoArrayLst.Clear();
                    for (int subLayer = iTmp * 4, subLayerMax = subLayer + 4; subLayer < subLayerMax; subLayer++)
                    {
                        var subArray = litShadowInfoArray.GetSubArray(areaSize * (subLayer), areaSize);
                        litShadowInfoArrayLst.Add(subArray);
                    }
                    for (int idx = 0; idx < areaSize; idx++)
                    {

                        bool prebool = false;
                        for (int subLayer = 0, subLayerMax = 4; subLayer < subLayerMax; subLayer++)
                        {
                            var subArrayPtr = (byte*)litShadowInfoArrayLst[subLayer].GetUnsafeReadOnlyPtr();
                            prebool |= (DownSampleOption.SumTargetIntersectedCount == downsampleOption) && Mathf.Abs(subArrayPtr[idx] - 128) < 20;
                            prebool |= (DownSampleOption.SumTargetLitCount == downsampleOption) && Mathf.Abs(subArrayPtr[idx] - 255) < 20;
                            prebool |= (DownSampleOption.SumTargetShadowedCount == downsampleOption) && Mathf.Abs(subArrayPtr[idx]) < 20;
                        }

                        if (prebool)
                        {
                            sumSub++;
                        }
                    }
                    if (sumPerLayer != null)
                    {
                        sumPerLayer[iTmp] = sumSub;
                    }
                    return sumSub;
                }
            });

            pendingTask.Add(task);
        }
        Task.WaitAll(pendingTask.ToArray());

        int Sum = 0;
        pendingTask.ForEach((t) =>
        {
            Sum += t.Result;
        });
        return Sum;
    }

    private int SumInfo(int size, NativeArray<byte> litShadowInfoArray, DownSampleOption downsampleOption = DownSampleOption.None, int[] sumPerLayer = null)
    {
        if (sumPerLayer != null)
        {
            UnityEngine.Assertions.Assert.AreEqual(sumPerLayer.Length, size, "$$ size of sumPerLayer should be equal to parm size");
        }
        int areaSize = size * size;
        List<Task<int>> pendingTask = new List<Task<int>>();
        for (int i = 0; i < size; i++)
        {
            int iTmp = i;
            var subArray = litShadowInfoArray.GetSubArray(areaSize * i, areaSize);

            var task = Task.Run<int>(() =>
            {
                unsafe
                {
                    var subArrayPtr = (byte*)subArray.GetUnsafeReadOnlyPtr();
                    int sumSub = 0;
                    for (int idx = 0; idx < areaSize; idx++)
                    {
                        bool prebool = false;
                        prebool |= (DownSampleOption.SumTargetIntersectedCount == downsampleOption) && Mathf.Abs(subArrayPtr[idx] - 128) < 20;
                        prebool |= (DownSampleOption.SumTargetLitCount == downsampleOption) && Mathf.Abs(subArrayPtr[idx] - 255) < 20;
                        prebool |= (DownSampleOption.SumTargetShadowedCount == downsampleOption) && Mathf.Abs(subArrayPtr[idx]) < 20;
                        if (prebool)
                        {
                            sumSub++;
                        }
                    }
                    if (sumPerLayer != null)
                    {
                        sumPerLayer[iTmp] = sumSub;
                    }
                    return sumSub;
                }
            });

            pendingTask.Add(task);
        }
        Task.WaitAll(pendingTask.ToArray());

        int Sum = 0;
        pendingTask.ForEach((t) =>
        {
            Sum += t.Result;
        });
        return Sum;
    }

    private unsafe int DownSample(int targetVoxelSize, int originVoxelSize, byte* targetLitShadowInfoArray,
         byte* originLitShadowInfoArray, int kernelSize = 2 /* 2 * 2 */)
    {
        ResetProgress();
        UnityEngine.Assertions.Assert.AreEqual(originVoxelSize / targetVoxelSize, kernelSize, "## Downsample error, kernelSize is not valid.");
        List<Task> pendingTask = new List<Task>();
        List<Task> pendingTask1 = new List<Task>();
        var mainThread = System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();
        long originAreaSize = (long)originVoxelSize * (long)originVoxelSize;
        long targetAreaSize = (long)targetVoxelSize * (long)targetVoxelSize;
        int taskProgress = 0;

        Init((uint)8, (uint)8 * (uint)kernelSize, (uint)targetVoxelSize, (uint)originVoxelSize / (uint)targetVoxelSize);
        // summary to root
        for (long dBlockIndex = 0, dBlockIdxMax = (long)targetVoxelSize; dBlockIndex < dBlockIdxMax; dBlockIndex++)
        {
            long dBlockIndexTmp = dBlockIndex;
            //var lv1BlockPixels = targetLitShadowInfoArray.GetSubArray(dBlockIndex * targetAreaSize, targetAreaSize);
            byte* lv1BlockPixels = (byte*)targetLitShadowInfoArray + dBlockIndex * targetAreaSize;
            //var lv2BlockPixelsFront = originLitShadowInfoArray.Slice(dBlockIndex * areaSize); //.GetPixels(kernelSize * dBlockIndex);
            //var lv2BlockPixelsBack = originLitShadowInfoArray.GetPixels(kernelSize * dBlockIndex + 1);
            List<System.IntPtr> subDepths = new List<System.IntPtr>();
            List<NativeArray<byte>> downsampledTemp = new List<NativeArray<byte>>(8);
            for (int subDepthIdx = 0; subDepthIdx < kernelSize; subDepthIdx++)
            {
                subDepths.Add(new IntPtr(originLitShadowInfoArray + ((long)kernelSize * dBlockIndex + (long)subDepthIdx) * originAreaSize));
                downsampledTemp.Add(new NativeArray<byte>(targetVoxelSize * targetVoxelSize, Allocator.Persistent));
                //subDepths.Add(originLitShadowInfoArray.Slice<byte>((kernelSize * dBlockIndex + subDepthIdx) * originAreaSize, originAreaSize));
            }

            var task = Task.Run(() =>
            {
#if _ENABLE_CUDA
                for(int subDepth=0,subDepthMax = subDepths.Count; subDepth < subDepthMax; subDepth++)
                {
                    void* subTex = subDepths[subDepth].ToPointer();
                    void* targetTex = downsampledTemp[subDepth].GetUnsafePtr();
                    Downsample(targetTex, subTex, (uint)targetVoxelSize, (uint)kernelSize);
                }

                for (int vBlockIndex = 0, vBlockIdxMax = targetVoxelSize; vBlockIndex < vBlockIdxMax; vBlockIndex++)
                {
                    for (int uBlockIndex = 0, uBlockIdxMax = targetVoxelSize; uBlockIndex < uBlockIdxMax; uBlockIndex++)
                    {
                        bool isAllDepthWhite = true;
                        bool isAllDepthBlack = true;
                        for (int subDepth = 0, subDepthMax = subDepths.Count; subDepth < subDepthMax; subDepth++)
                        {
                            var pData = (byte*)downsampledTemp[subDepth].GetUnsafeReadOnlyPtr();
                            isAllDepthWhite &= Mathf.Abs(pData[(long)vBlockIndex * (long)targetVoxelSize + (long)uBlockIndex] / 255.0f - 1) < 0.1f;
                            isAllDepthBlack &= Mathf.Abs(pData[(long)vBlockIndex * (long)targetVoxelSize + (long)uBlockIndex] / 255.0f) < 0.1f;
                        }

                        // isAllDepthWhite &= downsampledTemp.TrueForAll((b) =>
                        //{
                        //    var pData = (byte*)b.GetUnsafeReadOnlyPtr();
                        //    return Mathf.Abs(pData[(long)vBlockIndex * (long)targetVoxelSize + (long)uBlockIndex] / 255.0f - 1) < 0.1f;
                        //});
                        //isAllDepthBlack &= downsampledTemp.TrueForAll((b) =>
                        //{
                        //    var pData = (byte*)b.GetUnsafeReadOnlyPtr();
                        //    return Mathf.Abs(pData[(long)vBlockIndex * (long)targetVoxelSize + (long)uBlockIndex] / 255.0f) < 0.1f;
                        //});

                        bool isBlockIntersection = !isAllDepthWhite && !isAllDepthBlack;
                        var blockResult = (isAllDepthWhite ? 1 : 0) + (isBlockIntersection ? 0.5f : 0);
                        lv1BlockPixels[(long)vBlockIndex * (long)uBlockIdxMax + (long)uBlockIndex] = (byte)Mathf.RoundToInt(blockResult * 255);
                    }
                }

                downsampledTemp.ForEach((temp) =>
                {
                    temp.Dispose();
                });
                
#else

                for (int vBlockIndex = 0, vBlockIdxMax = targetVoxelSize; vBlockIndex < vBlockIdxMax; vBlockIndex++)
                {
                    for (int uBlockIndex = 0, uBlockIdxMax = targetVoxelSize; uBlockIndex < uBlockIdxMax; uBlockIndex++)
                    {
                        int uPixelBase = kernelSize * uBlockIndex;
                        int vPixelBase = kernelSize * vBlockIndex;
                        // voxel : 2*2*2 lv3
                        bool isAllDepthWhite = true;
                        bool isAllDepthBlack = true;
                        for (int vPixelSub = 0, vPixelMax = kernelSize; vPixelSub < vPixelMax; vPixelSub++)
                        {
                            for (int uPixelSub = 0, uPixelMax = kernelSize; uPixelSub < uPixelMax; uPixelSub++)
                            {
                                int vPixel = vPixelBase + vPixelSub;
                                int uPixel = uPixelBase + uPixelSub;

                                isAllDepthWhite &= subDepths.TrueForAll((b) =>
                                {
                                    var pData = (byte*)b.ToPointer();
                                    return Mathf.Abs(pData[(long)vPixel * (long)originVoxelSize + (long)uPixel] / 255.0f - 1) < 0.1f;
                                });
                                isAllDepthBlack &= subDepths.TrueForAll((b) =>
                                {
                                    var pData = (byte*)b.ToPointer();
                                    return Mathf.Abs(pData[(long)vPixel * (long)originVoxelSize + (long)uPixel] / 255.0f) < 0.1f;
                                });
                            }
                        }
                        bool isBlockIntersection = !isAllDepthWhite && !isAllDepthBlack;
                        var blockResult = (isAllDepthWhite ? 1 : 0) + (isBlockIntersection ? 0.5f : 0);
                        lv1BlockPixels[(long)vBlockIndex * (long)uBlockIdxMax + (long)uBlockIndex] = (byte)Mathf.RoundToInt(blockResult * 255);
                    }
                }
#endif
                IncreaseProgress();
            }, cancelAllTask.Token);
            //var task1 = new Task(() =>
            //{
            //    targetLitShadowInfoArray.(lv1BlockPixels, dBlockIndexTmp, 0);
            //});

            pendingTask.Add(task);
            if (pendingTask.Count > 4)
            {
                WaitPendingTask(targetVoxelSize, true, false, "Calculating", "DownSample from" + originVoxelSize + " to " + targetVoxelSize, pendingTask);
                pendingTask.Clear();
            }
            //pendingTask1.Add(task1);

        }

        WaitPendingTask(targetVoxelSize, true, false, "Calculating", "DownSample from" + originVoxelSize+ " to " + targetVoxelSize, pendingTask);
        Close();
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
        return 0;
    }


    private void DownSample(int targetVoxelSize, int originVoxelSize, Texture2DArray targetLitShadowInfoArray,
        Texture2DArray originLitShadowInfoArray, int kernelSize = 2 /* 2 * 2 */)
    {
        bool isAlpha8 = ((targetLitShadowInfoArray.format == TextureFormat.Alpha8) || (originLitShadowInfoArray.format == TextureFormat.Alpha8));
        UnityEngine.Assertions.Assert.AreEqual(originVoxelSize / targetVoxelSize, kernelSize, "## Downsample error, kernelSize is not valid.");
        List<Task> pendingTask = new List<Task>();
        List<Task> pendingTask1 = new List<Task>();
        var mainThread = System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();
        // summary to root
        for (int dBlockIndex = 0, dBlockIdxMax = targetVoxelSize; dBlockIndex < dBlockIdxMax; dBlockIndex++)
        {
            int dBlockIndexTmp = dBlockIndex;
            var lv1BlockPixels = targetLitShadowInfoArray.GetPixels(dBlockIndex);
            var lv2BlockPixelsFront = originLitShadowInfoArray.GetPixels(kernelSize * dBlockIndex);
            var lv2BlockPixelsBack = originLitShadowInfoArray.GetPixels(kernelSize * dBlockIndex + 1);

            var task = Task.Run(() =>
            {
                for (int vBlockIndex = 0, vBlockIdxMax = targetVoxelSize; vBlockIndex < vBlockIdxMax; vBlockIndex++)
                {
                    for (int uBlockIndex = 0, uBlockIdxMax = targetVoxelSize; uBlockIndex < uBlockIdxMax; uBlockIndex++)
                    {
                        int uPixelBase = kernelSize * uBlockIndex;
                        int vPixelBase = kernelSize * vBlockIndex;
                        // voxel : 2*2*2 lv3
                        bool isVoxelLited = true;
                        bool isVoxelShadowed = true;
                        for (int vPixelSub = 0, vPixelMax = kernelSize; vPixelSub < vPixelMax; vPixelSub++)
                        {
                            for (int uPixelSub = 0, uPixelMax = kernelSize; uPixelSub < uPixelMax; uPixelSub++)
                            {
                                int vPixel = vPixelBase + vPixelSub;
                                int uPixel = uPixelBase + uPixelSub;
                                var pixelFront = isAlpha8 ? lv2BlockPixelsFront[vPixel * originVoxelSize + uPixel].a : lv2BlockPixelsFront[vPixel * originVoxelSize + uPixel].r;
                                var pixelBack = isAlpha8 ? lv2BlockPixelsBack[vPixel * originVoxelSize + uPixel].a : lv2BlockPixelsBack[vPixel * originVoxelSize + uPixel].r;
                                var isWhite = Mathf.Abs(pixelFront - 1) < 0.1f;
                                var isWhiteBack = Mathf.Abs(pixelBack - 1) < 0.1f;
                                var isBlack = Mathf.Abs(pixelFront - 0) < 0.1f;
                                var isBlackBack = Mathf.Abs(pixelBack - 0) < 0.1f;
                                var isGray = Mathf.Abs(pixelFront - 0.5f) < 0.1f;
                                var isGrayBack = Mathf.Abs(pixelBack - 0.5f) < 0.1f;
                                isVoxelLited &= isWhite && isWhiteBack;
                                isVoxelShadowed &= isBlack && isBlackBack;
                            }
                        }
                        bool isBlockIntersection = !isVoxelLited && !isVoxelShadowed;
                        var blockResult = (isVoxelLited ? 1 : 0) + (isBlockIntersection ? 0.5f : 0);
                        lv1BlockPixels[vBlockIndex * uBlockIdxMax + uBlockIndex] = Color.white * blockResult;
                    }

                }
            });
            var task1 = new Task(() =>
            {
                targetLitShadowInfoArray.SetPixels(lv1BlockPixels, dBlockIndexTmp, 0);
            });

            pendingTask.Add(task);
            pendingTask1.Add(task1);

        }

        Task.WaitAll(pendingTask.ToArray());
        pendingTask1.ForEach((t) =>
        {
            t.RunSynchronously();
        });
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }

    public static void CompressLv4Tex()
    {
        var lv4TexArray = Selection.activeObject as Texture2DArray;
        var compressedTexArray = new Texture2DArray(lv4TexArray.width, lv4TexArray.height, lv4TexArray.depth, TextureFormat.ASTC_RGBA_4x4, false, true);
        Texture2D subTex = new Texture2D(lv4TexArray.width, lv4TexArray.height, TextureFormat.RGBA32, false, true);
        unsafe
        {
            for (int i = 0, c = lv4TexArray.depth; i < c; i++)
            {
                Graphics.CopyTexture(lv4TexArray, i, compressedTexArray, i);
                //var pixels = lv4TexArray.GetPixels(i, 0);
                //fixed(Color* pixelPtr = pixels)
                //{
                //    byte* pixelPtr1 = (byte*)pixelPtr;
                //    subTex.LoadRawTextureData(new IntPtr(pixelPtr), pixels.Length * 4);
                    
                //}
            }
        }
        AssetDatabase.CreateAsset(lv4TexArray, "Assets/compressedTexArray.asset");
    }

    [MenuItem("Tools/TestCuda1")]
    public static void TestCuda1()
    {
        var lv4TexArray = Selection.activeObject as Texture2D;
        Texture2D subTex = new Texture2D(lv4TexArray.width / 2, lv4TexArray.height / 2, TextureFormat.Alpha8, false, true);
        var targetTexNa = subTex.GetRawTextureData<byte>();
        byte* originTex = (byte*)lv4TexArray.GetRawTextureData<byte>().GetUnsafePtr();
        byte* targetTex = (byte*)targetTexNa.GetUnsafePtr();
        Init(8, 8, (uint)subTex.width, 2);
        uint width = (uint)subTex.width;

        object obj = new object();
        List<Task> tasks = new List<Task>();
        for (int i1 = 0; i1 < 128; i1++)
        {
            var task = Task.Run(() =>
            {
                for (int i = 0; i < 16; i++)
                {
                    Downsample(targetTex, originTex, (uint)width, 2);
                }
            });
            tasks.Add(task);
        }

        Task.WaitAll(tasks.ToArray());
        Close();
        subTex.SetPixelData<byte>(targetTexNa, 0);
        //subTex.LoadRawTextureData<byte>(targetTexNa);
        subTex.Apply(false, false);
        var colArray = subTex.GetRawTextureData();
        for (int i = 0; i < 32; i++) {
            Debug.Log(targetTex[i * 32]);
            Debug.Log(colArray[i * 32]);
        }
        
        AssetDatabase.CreateAsset(subTex, "Assets/subTex.asset");
    }

    [MenuItem("Tools/TestLoadAlpha8Bin")]
    public static void TestLoadAlpha8Bin()
    {
        var fileStream = new System.IO.FileStream("litshadowmapSSD/voxel_lv_975.gzip", System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);

        //fileStream.Seek(0, System.IO.SeekOrigin.End);
        NativeArray<byte> data = new NativeArray<byte>(16777216, Allocator.Temp);


        //var fileStream = new System.IO.FileStream(string.Format(LitShadowMapPath + "voxel_lv_{0}.gzip", texIdx), System.IO.FileMode.CreateNew, System.IO.FileAccess.ReadWrite);
        byte* ptr = (byte*)data.GetUnsafePtr();
        var gzipStream = new System.IO.Compression.DeflateStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
        var writeMem = new System.IO.UnmanagedMemoryStream(ptr, 1024,4096 * 4096, System.IO.FileAccess.Write);

        Debug.Log(fileStream.Length);
        var task = gzipStream.CopyToAsync(writeMem);
        while (!task.IsCompleted)
            System.Threading.Thread.Sleep(300);
        Texture2D tex = new Texture2D(4096, 4096, TextureFormat.Alpha8, false, true);
        tex.LoadRawTextureData<byte>(data);
        tex.Apply(false, false);
        AssetDatabase.CreateAsset(tex, "Assets/decompressedTex.asset");
    }

    [MenuItem("Tools/TestUnmanagedArray")]
    public unsafe static void TestUnmanagedArray()
    {
        int dataSize = 4096 * 4096;
        int boundSize = LZ4_compressBound(dataSize);
        byte* arrayPtr = (byte*)AllocMem((ulong)dataSize);
        byte* arrayCompressedPtr = (byte*)AllocMem((ulong)boundSize);
        byte* arrayDecompressedPtr = (byte*)AllocMem((ulong)dataSize);
        for (int i = 0; i < dataSize; i++)
        {
            arrayPtr[i] = 255;// (byte)UnityEngine.Random.Range(8, 255);
        }
        int compressedSize = LZ4_compress_fast(arrayPtr, arrayCompressedPtr, dataSize, boundSize, 0);
        int state = LZ4_decompress_safe(arrayCompressedPtr, arrayDecompressedPtr, compressedSize, dataSize);
        using(var mem = new System.IO.UnmanagedMemoryStream(arrayDecompressedPtr, dataSize, dataSize, System.IO.FileAccess.Read))
        {
            using(var f = new System.IO.FileStream("decompressed_array.bytes", System.IO.FileMode.Create, System.IO.FileAccess.Write))
            {
                mem.CopyTo(f);
            }
        }
        FreeMem(arrayPtr);
        FreeMem(arrayCompressedPtr);
        FreeMem(arrayDecompressedPtr);
    }

    [MenuItem("Tools/TestDecompressTex")]
    public unsafe static void TestDecompressTex()
    {
        for (int idx = 0; idx < 2048; idx++) {
            if (idx % 128 != 0) continue;
            int textureAreaSize = 4096 * 4096 + 1024 * 8;
            var texNa = new NativeArray<byte>(textureAreaSize, Allocator.Temp);
            var ptr = (byte*)texNa.GetUnsafePtr();
            var fileInfo = new System.IO.FileInfo(string.Format(LitShadowMapPath + "voxel_lv_{0}.lz4", idx));
            
            var fileStreamLz4 = new System.IO.FileStream(string.Format(LitShadowMapPath + "voxel_lv_{0}.lz4", idx), System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);

            uint compressedSize = (uint)fileInfo.Length;

            ((byte*)&compressedSize)[0] = (byte)fileStreamLz4.ReadByte();
            ((byte*)&compressedSize)[1] = (byte)fileStreamLz4.ReadByte();
            ((byte*)&compressedSize)[2] = (byte)fileStreamLz4.ReadByte();
            ((byte*)&compressedSize)[3] = (byte)fileStreamLz4.ReadByte();
            //fileStreamLz4.Seek(2, System.IO.SeekOrigin.Begin);

            var bufferNa = new NativeArray<byte>((int)compressedSize , Allocator.Temp);
            var bufferUnmanged = new System.IO.UnmanagedMemoryStream((byte*)bufferNa.GetUnsafePtr(), compressedSize , compressedSize, System.IO.FileAccess.Write);

            try
            {
                fileStreamLz4.CopyTo(bufferUnmanged, (int)compressedSize);
                //LZ4_decompress_safe_continue(lz4Stream, buffer, ptr, 4096 * 4096, 4096 * 4096);
                int decodeSize = LZ4_decompress_safe((byte*)bufferNa.GetUnsafePtr(), ptr, (int)compressedSize, textureAreaSize);
                Debug.Log(decodeSize);
            }
            finally
            {
                bufferNa.Dispose();
                bufferUnmanged.Dispose();
                fileStreamLz4.Close();
            }
            Texture2D tex = new Texture2D(4096, 4096, TextureFormat.Alpha8, false, true);
            tex.SetPixelData<byte>(texNa, 0);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, "Assets/decompressedTexs/tex_" + idx + ".asset");
        }
    }
    struct AccelerationJob : IJobParallelFor, Unity.Jobs.IJobParallelForBatch
    {
        public void Execute(int index)
        {
            throw new NotImplementedException();
        }

        public void Execute(int startIndex, int count)
        {
            throw new NotImplementedException();
        }
    }

}
