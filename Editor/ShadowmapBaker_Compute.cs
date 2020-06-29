#define _ENABLE_LV3_MODE
//#define _GEN_SCALED_TEX
#define _ENABLE_BIG_TEX
//#define _LV3_OLD_MODE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public partial class ShadowmapBaker
{

    // compute voxel on cpu 
    // compute lv3 lit or shadow info first, then summary to lv2 and rootLv1
    void precomputeVoxelDepth()
    {

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
        Texture2DArray litShadowInfoArrayLv4 = new Texture2DArray(lv4VoxelSize, lv4VoxelSize, lv4VoxelSize, TextureFormat.Alpha8, false, true);
        Texture2DArray litShadowInfoArrayLv3 = new Texture2DArray(lv3VoxelSize, lv3VoxelSize, lv3VoxelSize, TextureFormat.Alpha8, false, true);
        Texture2DArray litShadowInfoArrayLv2 = new Texture2DArray(lv2VoxelSize, lv2VoxelSize, lv2VoxelSize, TextureFormat.Alpha8, false, true);
        Texture2DArray litShadowInfoArrayLv1 = new Texture2DArray(rootVoxelSize, rootVoxelSize, rootVoxelSize, TextureFormat.Alpha8, false, true);
        var litShadowInfoArrayLv4Na = new NativeArray<byte>(lv4VoxelSize * lv4VoxelSize * lv4VoxelSize, Allocator.Temp, NativeArrayOptions.ClearMemory);
        var litShadowInfoArrayLv3Na = new NativeArray<byte>(lv3VoxelSize * lv3VoxelSize * lv3VoxelSize, Allocator.Temp, NativeArrayOptions.ClearMemory);
        var litShadowInfoArrayLv2Na = new NativeArray<byte>(lv2VoxelSize * lv2VoxelSize * lv2VoxelSize, Allocator.Temp, NativeArrayOptions.ClearMemory);
        var litShadowInfoArrayLv1Na = new NativeArray<byte>(rootVoxelSize * rootVoxelSize * rootVoxelSize, Allocator.Temp, NativeArrayOptions.ClearMemory);

        List<Object> resourceToRelease = new List<Object>();

        object lockObj = new object();
        int threadCount = 0;
        List<Task> pendingTask = new List<Task>();
        List<Task> plTasks = new List<Task>();

        var mainTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        // z-depth 
        for (int dVoxelIndex = 0, dVoxelMaxIndex = lv4VoxelSize; dVoxelIndex < dVoxelMaxIndex; dVoxelIndex++)
        {
            var voxelLitShadowInfo = AssetDatabase.LoadAssetAtPath<Texture2D>(string.Format("Assets/litshadowmap/voxel_lv_{0}.asset", dVoxelIndex));
            bool isAlpha8 = voxelLitShadowInfo.format == TextureFormat.Alpha8;
            resourceToRelease.Add(voxelLitShadowInfo);
            if (voxelLitShadowInfo == null)
            {
                Debug.Log(string.Format("voxelLitShadowInfo {0} is not exist.", dVoxelIndex));
            }
            var tex = voxelLitShadowInfo;
            var texName = tex.name;
            var blockPixels = litShadowInfoArrayLv4Na.GetSubArray(voxelAreaLv4 * dVoxelIndex, voxelAreaLv4);;// litShadowInfoArrayLv4.GetPixels(dVoxelIndex, 0);
            var voxelLitShadowInfoColorNA = voxelLitShadowInfo.GetRawTextureData<Color32>();
            var voxelLitShadowInfoNA = voxelLitShadowInfo.GetRawTextureData<byte>();
            float startTime = Time.realtimeSinceStartup;

            if (pendingTask.Count > 64)
            {
                Task.WaitAll(pendingTask.ToArray());
                pendingTask.Clear();
            }

   
            int dVoxelIndexTmp = dVoxelIndex;
            System.Action task = () =>
            {
                //litShadowInfoArrayLv4.SetPixels(blockPixels, dVoxelIndexTmp, 0);
               // NativeArray<byte>.Copy(blockPixels, 0, litShadowInfoArrayLv4Na, litShadowInfoArrayLv4.width * litShadowInfoArrayLv4.height * dVoxelIndexTmp, blockPixels.Length);
            };

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

                    for (int vBlockIndex = 0, vBlockIdxMax = lv4VoxelSize; vBlockIndex < vBlockIdxMax; vBlockIndex++)
                    {
                        for (int uBlockIndex = 0, uBlockIdxMax = lv4VoxelSize; uBlockIndex < uBlockIdxMax; uBlockIndex++)
                        {
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
                                blockPixels[vBlockIndex * uBlockIdxMax + uBlockIndex] = 128;
                            }
                            blockPixels[vBlockIndex * uBlockIdxMax + uBlockIndex] = (byte)Mathf.RoundToInt((blockResult * 255));// Color.white * blockResult;
                        }
                    }
                }

            });

            plTasks.Add(plTask);



            if (plTasks.Count > 8)
            {
                plTasks.ForEach((t) =>
                {
                    t.Start();
                    pendingTask.Add(t);
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
                t.Start();
                pendingTask.Add(t);
            });
            plTasks.Clear();
        }
        if (pendingTask.Count > 0)
        {
            Task.WaitAll(pendingTask.ToArray());
            pendingTask.Clear();
        }


        for (int depth = 0, maxDepth = litShadowInfoArrayLv4.depth; depth < maxDepth; depth++) {
            var subArray = litShadowInfoArrayLv4Na.GetSubArray(depth * voxelAreaLv4, voxelAreaLv4);
            litShadowInfoArrayLv4.SetPixelData<byte>(subArray, 0, depth);
        }
        litShadowInfoArrayLv4.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();


        DownSample(lv3VoxelSize, lv4VoxelSize, litShadowInfoArrayLv3Na, litShadowInfoArrayLv4Na, 8);
        for (int depth = 0, maxDepth = litShadowInfoArrayLv3.depth; depth < maxDepth; depth++)
        {
            var subArray = litShadowInfoArrayLv3Na.GetSubArray(depth * voxelAreaLv3, voxelAreaLv3);
            litShadowInfoArrayLv3.SetPixelData<byte>(subArray, 0, depth);
        }
        litShadowInfoArrayLv3.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        DownSample(lv2VoxelSize, lv3VoxelSize, litShadowInfoArrayLv2Na, litShadowInfoArrayLv3Na);
        for (int depth = 0, maxDepth = litShadowInfoArrayLv2.depth; depth < maxDepth; depth++)
        {
            var subArray = litShadowInfoArrayLv2Na.GetSubArray(depth * voxelAreaLv2, voxelAreaLv2);
            litShadowInfoArrayLv2.SetPixelData<byte>(subArray, 0, depth);
        }
        litShadowInfoArrayLv2.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        DownSample(rootVoxelSize, lv2VoxelSize, litShadowInfoArrayLv1Na, litShadowInfoArrayLv2Na);
        for (int depth = 0, maxDepth = litShadowInfoArrayLv1.depth; depth < maxDepth; depth++)
        {
            var subArray = litShadowInfoArrayLv1Na.GetSubArray(depth * voxelAreaLv1, voxelAreaLv1);
            litShadowInfoArrayLv1.SetPixelData<byte>(subArray, 0, depth);
        }
        litShadowInfoArrayLv1.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        //setTopVoxelLit(litShadowInfoArrayLv3);
        if(bSetTopIntersectedVoxelLit)
            setTopVoxelLit(litShadowInfoArrayLv4);
        //setTopVoxelLit(litShadowInfoArrayRoot);

        if (bExportLvLitShadowInfoTexArray4Dbg)
        {
            AssetDatabase.CreateAsset(litShadowInfoArrayLv1, "Assets/lightInfoArrayLv1.asset");
            AssetDatabase.CreateAsset(litShadowInfoArrayLv2, "Assets/lightInfoArrayLv2.asset");
            AssetDatabase.CreateAsset(litShadowInfoArrayLv3, "Assets/lightInfoArrayLv3.asset");
            AssetDatabase.CreateAsset(litShadowInfoArrayLv4, "Assets/lightInfoArrayLv4.asset");
        }
        Resources.UnloadUnusedAssets();
        System.GC.Collect();


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
        // 2D mode
        int lv4TextureSize = Mathf.CeilToInt(Mathf.NextPowerOfTwo((int)Mathf.Sqrt(lv3IntersectedCount * 16)));  // 16 pixel per lv3 intersection
        // Texture2DArray mode
        int lv4TextureArraySize = Mathf.CeilToInt(lv3IntersectedCount / 64 + (lv1IntersectedCount % 64 > 0 ? 1 : 0));
        Debug.Log("$$ lv4TextureSize:" + lv4TextureSize);
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
        Texture2DArray litShadowInfoMapArray = new Texture2DArray(32, 32, lv23TextureArraySize, TextureFormat.RGBA32, false, true);
#if !_ENABLE_BIG_TEX
        Texture2D litShadowInfoMapLv3 = new Texture2D(32, lv1IntersectedCount, TextureFormat.RGBA32, false, true);
#endif
        Texture2DArray litShadowInfoMapArrayLv3 = new Texture2DArray(32, 32, lv23TextureArraySize, TextureFormat.RGBA32, false, true);
        NativeArray<Color32> litShadowInfoMapArrayLv3Na = new NativeArray<Color32>(32 * 32 * lv23TextureArraySize, Allocator.Temp);
        List<NativeArray<Color32>> litShadowInfoMapArrayLv3NaLstTotal = new List<NativeArray<Color32>>(lv23TextureArraySize);
        for (int i = 0; i < lv23TextureArraySize; i++)
        {
            litShadowInfoMapArrayLv3NaLstTotal.Add(litShadowInfoMapArrayLv3Na.GetSubArray(32 * 32 * i, 32 * 32));
        }
        var indexPixels = litShadowInfoIndexMap.GetPixels(0);
        var indexPixelsNoTexArrayPixels = litShadowInfoIndexMapNoTextureArray.GetPixels(0);

        // init after get a accurate size
        Texture2DArray litShadowInfoMapArrayLv4 = new Texture2DArray(64, 64, lv4TextureArraySize, TextureFormat.RGBA32, false, true);
        NativeArray<Color32> litShadowInfoMapArrayLv4Na = new NativeArray<Color32>(64 * 64 * lv4TextureArraySize, Allocator.Temp);
        List<NativeArray<Color32>> litShadowInfoMapArrayLv4NaLstTotal = new List<NativeArray<Color32>>(lv4TextureArraySize);
        for(int i = 0; i < lv4TextureArraySize; i++) {
            litShadowInfoMapArrayLv4NaLstTotal.Add(litShadowInfoMapArrayLv4Na.GetSubArray(64 * 64 * i, 64 * 64));
        }
        List<NativeArray<byte>> litShadowInfoArrayLv4NaLstTotal = new List<NativeArray<byte>>();
        for (int i = 0; i < lv4VoxelSize; i++)
        {
            litShadowInfoArrayLv4NaLstTotal.Add(litShadowInfoArrayLv4Na.GetSubArray(voxelAreaLv4 * i, voxelAreaLv4));
        }
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
        litShadowInfoMapArrayLv3.Apply(false, false);

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
                    List<NativeArray<byte>> litShadowInfoArrayLstLv4 = new List<NativeArray<byte>>(8);
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
                                                            colorLine[0] = Color.black;
                                                            colorLine[1] = Color.black;
                                                            if (Mathf.Abs(lv3Infos[axeId] - 0.5f) < 0.1)
                                                            {
                                                                litShadowInfoArrayLstLv4.Clear();
                                                                for (int lv4D = dVoxelIndexTmp * 2 * 2 * 8 + axeId * 8, lv4DepthMax = lv4D + 8; lv4D < lv4DepthMax; lv4D++)
                                                                {
                                                                    litShadowInfoArrayLstLv4.Add(litShadowInfoArrayLv4NaLstTotal[lv4D]); //(litShadowInfoArrayLv4Na.GetSubArray(voxelAreaLv4 * lv4D, voxelAreaLv4));
                                                                }
                                                                for (int vPixelIndexLv4 = 0, vPixelMaxLv4 = 8; vPixelIndexLv4 < vPixelMaxLv4; vPixelIndexLv4++)
                                                                {
                                                                    for (int uPixelIndexLv4 = 0, uPixelMaxLv4 = 8; uPixelIndexLv4 < uPixelMaxLv4; uPixelIndexLv4++)
                                                                    {
                                                                        
                                                                        for (int dPixelIndexLv4 = 0, dPixelMaxLv4 = 8; dPixelIndexLv4 < dPixelMaxLv4; dPixelIndexLv4++)
                                                                        {
                                                                            var litShadowInfoArrayLv4Sub = (byte*)litShadowInfoArrayLstLv4[dPixelIndexLv4].GetUnsafeReadOnlyPtr();
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
                                                                            isLit = isLit || isIntersected;
                                                                            //channel |= (byte)((isLit ? 1u : 0u) << dPixelIndexLv4);
                                                                            var color = colorLine[uPixelIndexLv4 / 4];
                                                                            color[uPixelIndexLv4 % 4] |= (byte)((isLit ? 1u : 0u) << dPixelIndexLv4);
                                                                            colorLine[uPixelIndexLv4 / 4] = color;
                                                                            
                                                                            //int litShadowInfoMapArrayLv4Idx = queryIdxLv4Tmp * 
                                                                            //litShadowInfoMapArrayLv4NaPtr[]

                                                                        }
                                                                        int V64x64 = queryIdxLv4Tmp % 64;
                                                                        int u64x64 = 16 * axeId + (vPixelIndexLv4 * 8 + uPixelIndexLv4) / 4;
                                                                        colorBlock64x64[V64x64 * 64 + u64x64] = colorLine[uPixelIndexLv4 / 4];
                                                                    }
                                                                }

                                                            }
                                                        }

                                                        //int lv4VPixel = queryIdxLv4Tmp % 64;
                                                        float fLv4v = (float)(queryIdxLv4Tmp % 64) / 64.0f + 0.01f;
                                                        int lv4Depth = queryIdxLv4Tmp / 64;
                                                        float fLv4Depth = (float)(queryIdxLv4Tmp / 64) / (float)(lv4TextureArraySize - 1);
                                                        Vector2 lv4vEncoded = EncodeFloatRG(fLv4v);
                                                        Vector2 lv4DepthEncoded = EncodeFloatRG(fLv4Depth);
                                                        colorBlock32x32[queryIdxLv1Tmp % 32 * 32 + pixelIdxLv4].r = (byte)(lv4vEncoded.x * 255);// new Color(lv4vEncoded.x, lv4vEncoded.y, lv4DepthEncoded.x, lv4DepthEncoded.y);
                                                        colorBlock32x32[queryIdxLv1Tmp % 32 * 32 + pixelIdxLv4].g = (byte)(lv4vEncoded.y * 255);
                                                        colorBlock32x32[queryIdxLv1Tmp % 32 * 32 + pixelIdxLv4].b = (byte)(lv4DepthEncoded.x * 255);
                                                        colorBlock32x32[queryIdxLv1Tmp % 32 * 32 + pixelIdxLv4].a = (byte)(lv4DepthEncoded.y * 255);

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

                                float texV = (queryIdxLv1Tmp % 32) / 32.0f + 0.01f;  //(queryIdx % (lv1IntersectedCount * 0.5f))  / ((float)lv1IntersectedCount); //queryIdx / (float)lv1IntersectedCount;  //
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

                });// End Task

                pendingTask.Add(taskToGenLitShadowTex);


                if (pendingTask.Count > 8)
                {
                    Task.WaitAll(pendingTask.ToArray());
                    pendingTask.Clear();
                }
                Resources.UnloadUnusedAssets();
                System.GC.Collect();
                //AssetDatabase.DeleteAsset("Assets/runtimeLitShadowInfo.asset");
                //AssetDatabase.CreateAsset(litShadowInfoMap, "Assets/runtimeLitShadowInfo.asset");
            }
        }

        Task.WaitAll(pendingTask.ToArray());
        pendingTask.Clear();

        var savePathAsset = EditorUtility.SaveFolderPanel("保存路径", Application.dataPath, "");
        var parentPath = FileUtil.GetProjectRelativePath(savePathAsset);

#if _ENABLE_BIG_TEX
        //litShadowInfoMapArrayLv3.SetPixels(colorBlock32x32, Mathf.Min(texDepth, litShadowInfoMapArrayLv3.depth - 1), 0);
        {
            //var subArray = litShadowInfoMapArrayLv3Na.GetSubArray(32 * 32 * Mathf.Min(texDepthLv1, litShadowInfoMapArrayLv3.depth - 1), 32 * 32);
            //subArray.CopyFrom(colorBlock32x32);
            for (int copyDepth = 0, maxDepth = litShadowInfoMapArrayLv3.depth; copyDepth < maxDepth; copyDepth++)
            {
                litShadowInfoMapArrayLv3.SetPixelData<Color32>(litShadowInfoMapArrayLv3Na.GetSubArray(32 * 32 * copyDepth, 32 * 32), 0, copyDepth);
            }
            
            for (int copyDepth = 0, maxDepth = litShadowInfoMapArrayLv4.depth; copyDepth < maxDepth; copyDepth++)
            {
                litShadowInfoMapArrayLv4.SetPixelData<Color32>(litShadowInfoMapArrayLv4Na.GetSubArray(64 * 64 * copyDepth, 64 * 64), 0, copyDepth);
            }
        }
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

        litShadowInfoMapArrayLv3.Apply(false, false);
        litShadowInfoMapArrayLv3.wrapMode = TextureWrapMode.Clamp;
        litShadowInfoMapArrayLv3.filterMode = FilterMode.Point;
        AssetDatabase.CreateAsset(litShadowInfoMapArrayLv3, parentPath + "/litShadowInfoMapArrayLv3.asset");

        litShadowInfoMapArrayLv4.Apply(false, false);
        litShadowInfoMapArrayLv4.wrapMode = TextureWrapMode.Clamp;
        litShadowInfoMapArrayLv4.filterMode = FilterMode.Point;
        AssetDatabase.CreateAsset(litShadowInfoMapArrayLv4, parentPath + "/litShadowInfoMapArrayLv4.asset");

        litShadowInfoMapArray.Apply(false, false);
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        if (litShadowInfoMapArray != null)
        {
            litShadowInfoMapArray.wrapMode = TextureWrapMode.Clamp;
            litShadowInfoMapArray.filterMode = FilterMode.Point;
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // setup material property
        litMaterial.SetFloat("_ShadowAlpha", 0.549f);
        litMaterial.SetFloat("_ShadowBias", 8.0f);
        litMaterial.SetFloat("_ShadowBias1", 0.33f);
        litMaterial.SetFloat("_DEBUG_FACT", 0.00f);
        litMaterial.SetFloat("_level1TexSize", litShadowInfoIndexMapSize);
        litMaterial.SetFloat("_level2TexArrayDepth", lv23TextureArraySize - 1);
        litMaterial.SetFloat("_level4TexArrayDepth", lv4TextureArraySize - 1);

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

    private int DownSample(int targetVoxelSize, int originVoxelSize, NativeArray<byte> targetLitShadowInfoArray,
         NativeArray<byte> originLitShadowInfoArray, int kernelSize = 2 /* 2 * 2 */)
    {
        UnityEngine.Assertions.Assert.AreEqual(originVoxelSize / targetVoxelSize, kernelSize, "## Downsample error, kernelSize is not valid.");
        List<Task> pendingTask = new List<Task>();
        List<Task> pendingTask1 = new List<Task>();
        var mainThread = System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();
        int originAreaSize = originVoxelSize * originVoxelSize;
        int targetAreaSize = targetVoxelSize * targetVoxelSize;
        // summary to root
        for (int dBlockIndex = 0, dBlockIdxMax = targetVoxelSize; dBlockIndex < dBlockIdxMax; dBlockIndex++)
        {
            int dBlockIndexTmp = dBlockIndex;
            var lv1BlockPixels = targetLitShadowInfoArray.GetSubArray(dBlockIndex * targetAreaSize, targetAreaSize); // targetLitShadowInfoArray.Slice(dBlockIndex * targetAreaSize, targetAreaSize);
            //var lv2BlockPixelsFront = originLitShadowInfoArray.Slice(dBlockIndex * areaSize); //.GetPixels(kernelSize * dBlockIndex);
            //var lv2BlockPixelsBack = originLitShadowInfoArray.GetPixels(kernelSize * dBlockIndex + 1);
            List<NativeSlice<byte>> subDepths = new List<NativeSlice<byte>>();
            for (int subDepthIdx = 0; subDepthIdx < kernelSize; subDepthIdx++)
            {
                subDepths.Add(originLitShadowInfoArray.Slice<byte>((kernelSize * dBlockIndex + subDepthIdx) * originAreaSize, originAreaSize));
            }

            var task = Task.Run(() =>
            {
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
                                    return Mathf.Abs(b[vPixel * originVoxelSize + uPixel] / 255.0f - 1) < 0.1f;
                                });
                                isAllDepthBlack &= subDepths.TrueForAll((b) =>
                                {
                                    return Mathf.Abs(b[vPixel * originVoxelSize + uPixel] / 255.0f) < 0.1f;
                                });
                            }
                        }
                        bool isBlockIntersection = !isAllDepthWhite && !isAllDepthBlack;
                        var blockResult = (isAllDepthWhite ? 1 : 0) + (isBlockIntersection ? 0.5f : 0);
                        lv1BlockPixels[vBlockIndex * uBlockIdxMax + uBlockIndex] = (byte)Mathf.RoundToInt(blockResult * 255);
                    }
                }
            });
            //var task1 = new Task(() =>
            //{
            //    targetLitShadowInfoArray.(lv1BlockPixels, dBlockIndexTmp, 0);
            //});

            pendingTask.Add(task);
            if (pendingTask.Count > 64)
            {
                Task.WaitAll(pendingTask.ToArray());
                pendingTask.Clear();
            }
            //pendingTask1.Add(task1);

        }

        Task.WaitAll(pendingTask.ToArray());
        //pendingTask1.ForEach((t) =>
        //{
        //    t.RunSynchronously();
        //});
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
