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



            if (plTasks.Count > 32)
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
        int lv1IntersectedCount = 0;
        for (int dVoxelIndex = 0, dVoxelMax = rootVoxelSize; dVoxelIndex < dVoxelMax; dVoxelIndex++)
        {
            var litShadowInfoLv1 = litShadowInfoArrayLv1.GetPixels(dVoxelIndex);
            for (int pixelIdx = 0, pixelMax = litShadowInfoLv1.Length; pixelIdx < pixelMax; pixelIdx++)
            {
                Vector4 value = litShadowInfoLv1[pixelIdx];
                if (Mathf.Abs(value.w - 0.5f) < 0.1f)
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

        //bool[] axeAllLitFront = new bool[4] { true, true, true, true };
        //bool[] axeAllShadowFront = new bool[4] { true, true, true, true };
        //bool[] axeIntersectedFront = new bool[4] { true, true, true, true };
        //bool[] axeAllLitBack = new bool[4] { true, true, true, true };
        //bool[] axeAllShadowBack = new bool[4] { true, true, true, true };
        //bool[] axeIntersectedBack = new bool[4] { true, true, true, true };
        unsafe
        {
            for (int dVoxelIndex = 0, dVoxelMax = rootVoxelSize; dVoxelIndex < dVoxelMax; dVoxelIndex++)
            {
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

                for (int vVoxelIndex = 0, vVoxelMax = rootVoxelSize; vVoxelIndex < vVoxelMax; vVoxelIndex++)
                {
                    for (int uVoxelIndex = 0, uVoxelMax = rootVoxelSize; uVoxelIndex < uVoxelMax; uVoxelIndex++)
                    {
                        var lv1 = litShadowInfoLv1[vVoxelIndex * uVoxelMax + uVoxelIndex];
                        var lvIndexMapIndex = dVoxelIndex * vVoxelMax * uVoxelMax + vVoxelIndex * uVoxelMax + uVoxelIndex;
                        var lvIndexMapY = lvIndexMapIndex / litShadowInfoIndexMapSize;
                        var lvIndexMapX = lvIndexMapIndex % litShadowInfoIndexMapSize;
                        // if lv1 voxel is intersected
                        if (Mathf.Abs(lv1.a - 0.5f) < 0.1f)
                        {
                            // litShadowInfoMap.SetPixel(0, queryIdx, new Color(lv1.r, 0,0,0 ), 0);
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

                                    if (((Mathf.Abs(frontColor.a - 1) < 0.1f && Mathf.Abs(backColor.a - 1) < 0.1f) ||
                                        (Mathf.Abs(frontColor.a - 0) < 0.1f && Mathf.Abs(backColor.a - 0) < 0.1f)))
                                    {

                                        Color colorLv3 = new Color(frontColor.a, frontColor.a, frontColor.a, frontColor.a);
                                        for (int vPixelIndexLv3 = 0, vPixelMaxLv3 = 2; vPixelIndexLv3 < vPixelMaxLv3; vPixelIndexLv3++)
                                        {
                                            for (int uPixelIndexLv3 = 0, uPixelMaxLv3 = 2; uPixelIndexLv3 < uPixelMaxLv3; uPixelIndexLv3++)
                                            {
                                                int pixelIdx = 8 * vPixelIndex + uPixelIndex * 4 + 2 * vPixelIndexLv3 + uPixelIndexLv3;
                                                colorBlock32x32[queryIdx % 32 * 32 + pixelIdx] = colorLv3;

                                            }
                                        }
                                    }
                                    else
                                    {
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
                                                
                                        
                                                Color colorLv3 = new Color(lv3_front.a, lv3_mid1.a, lv3_mid2.a, lv3_back.a);
                                                // pixel u
                                                int pixelIdx = 8 * vPixelIndex + uPixelIndex * 4 + 2 * vPixelIndexLv3 + uPixelIndexLv3;

                                                colorBlock32x32[queryIdx % 32 * 32 + pixelIdx] = colorLv3;
                                                // pixel u for lv4 query
                                                int pixelIdxLv4 = 16 + pixelIdx;

                                                for (int vPixelIndexLv4 = 0, vPixelMaxLv4 = 8; vPixelIndexLv4 < vPixelMaxLv4; vPixelMaxLv4++)
                                                {
                                                    for (int uPixelIndexLv4 = 0, uPixelMaxLv4 = 8; uPixelIndexLv4 < uPixelMaxLv4; uPixelIndex++)
                                                    {

                                                    }
                                                }
                                            }
                                        }

                                    }

                                    //int seqFront = vPixelIndex * uPixelMax + uPixelIndex;
                                    //int seqBack = seqFront + 4;


                                }
                            }

                            float texV = (queryIdx % 32) / 32.0f + 0.01f;  //(queryIdx % (lv1IntersectedCount * 0.5f))  / ((float)lv1IntersectedCount); //queryIdx / (float)lv1IntersectedCount;  //
                            Vector4 v = EncodeFloatRG(texV);

                            float arrayIndex = (queryIdx / 32) / (float)(textureArraySize - 1);
                            Vector2 arrayIndexRG = EncodeFloatRG(arrayIndex);
                            // R: lit or Shadow GB: v A : textureArray index  setup after lv2 info summarized
                            
                            litShadowInfoIndexMap.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.a, texV, arrayIndexRG.x, arrayIndexRG.y));// //new Color(lv1.a, v.x, v.y, isHalf), 0);

#if !_ENABLE_BIG_TEX
                        texV = (queryIdx % (lv1IntersectedCount / 2)) / (float)lv1IntersectedCount;
                        float isHalf = queryIdx > lv1IntersectedCount / 2 ? 1 : 0;
                        v = EncodeFloatRG(texV);
                        litShadowInfoIndexMapNoTextureArray.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.a, isHalf, v.x, v.y));
#endif

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
                            Vector4 v = EncodeFloatRG(lv1.a > 0.9 ? 20000 : 10000);
                            litShadowInfoIndexMap.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.a, 0, 0, 0), 0);
#if !_ENABLE_BIG_TEX
                        litShadowInfoIndexMapNoTextureArray.SetPixel(lvIndexMapX, lvIndexMapY, new Color(lv1.a, 0, 0, 0), 0);
#endif
                        }
                    }
                }


                Resources.UnloadUnusedAssets();
                System.GC.Collect();
                //AssetDatabase.DeleteAsset("Assets/runtimeLitShadowInfo.asset");
                //AssetDatabase.CreateAsset(litShadowInfoMap, "Assets/runtimeLitShadowInfo.asset");
            }
        }

        //if(EditorUtility.DisplayDialog("选择保存目录", "", ""))
        //{

        //}

        var savePathAsset = EditorUtility.SaveFolderPanel("保存路径", Application.dataPath, "");
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

        litShadowInfoMapArrayLv3.Apply(false, false);
        litShadowInfoMapArrayLv3.wrapMode = TextureWrapMode.Clamp;
        litShadowInfoMapArrayLv3.filterMode = FilterMode.Point;
        AssetDatabase.CreateAsset(litShadowInfoMapArrayLv3, parentPath + "/litShadowInfoMapArrayLv3.asset");

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
