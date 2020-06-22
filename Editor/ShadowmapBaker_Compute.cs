using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class ShadowmapBaker
{

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
        int lv4VoxelSize = lv3VoxelSize * 2;
        int lv5VoxelSize = lv4VoxelSize * 2;
        int rootPixelPerVoxel = 0;
        int lv2PixelPerVoxel = 0;
        int lv3PixelPerVoxel = 0;
        int lv4PixelPerVoxel = 0;

        //var allVoxelLitShadowInfo = AssetDatabase.LoadAllAssetsAtPath("Assets/shadowmap");
        var shadowMapWidth = shadowMap.width;
        rootPixelPerVoxel = shadowMapWidth / RootVoxelWidthSize;
        lv2PixelPerVoxel = rootPixelPerVoxel / 2;
        lv3PixelPerVoxel = lv2PixelPerVoxel / 2;
        lv4PixelPerVoxel = lv3PixelPerVoxel / 2;

        // lv3VoxelBlockInfo 32 * 32 * 32 if root is 8*8*8 .   lv3 4*4*4 voxel == lv1 1*1*1
        int resultTextureSize = lv3VoxelSize;
        // int resultMaxBlockCount = 256 / lv3VoxelSize;
        // Texture2D litShadowInfoMap = new Texture2D(resultTextureSize, resultTextureSize, TextureFormat.ARGB32, false, true);
        Texture2DArray litShadowInfoArrayLv4 = new Texture2DArray(lv4VoxelSize, lv4VoxelSize, lv4VoxelSize, TextureFormat.RGBA32, false, true);
        Texture2DArray litShadowInfoArrayLv3 = new Texture2DArray(lv3VoxelSize, lv3VoxelSize, lv3VoxelSize, TextureFormat.RGBA32, false, true);
        Texture2DArray litShadowInfoArrayLv2 = new Texture2DArray(lv2VoxelSize, lv2VoxelSize, lv2VoxelSize, TextureFormat.RGBA32, false, true);
        Texture2DArray litShadowInfoArrayLv1 = new Texture2DArray(rootVoxelSize, rootVoxelSize, rootVoxelSize, TextureFormat.RGBA32, false, true);

        List<Object> resourceToRelease = new List<Object>();

        object lockObj = new object();
        int threadCount = 0;
        Queue<Action> mainThreadTasks = new Queue<Action>();
        List<Task> plTasks = new List<Task>();

        var mainTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        // z-depth 
        for (int dVoxelIndex = 0, dVoxelMaxIndex = lv3VoxelSize; dVoxelIndex < dVoxelMaxIndex; dVoxelIndex++)
        {
            var voxelLitShadowInfo = AssetDatabase.LoadAssetAtPath<Texture2D>(string.Format("Assets/litshadowmap/voxel_lv_{0}.asset", dVoxelIndex));
            resourceToRelease.Add(voxelLitShadowInfo);
            if (voxelLitShadowInfo == null)
            {
                Debug.Log(string.Format("voxelLitShadowInfo {0} is not exist.", dVoxelIndex));
            }
            var tex = voxelLitShadowInfo;
            var texName = tex.name;
            var blockPixels = litShadowInfoArrayLv4.GetPixels(dVoxelIndex, 0);
            var voxelLitShadowInfoNA = voxelLitShadowInfo.GetRawTextureData<Color32>();
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
                litShadowInfoArrayLv4.SetPixels(blockPixels, dVoxelIndexTmp, 0);
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
                    Color32* voxelLitShadowInfoPtr = (Color32*)voxelLitShadowInfoNA.GetUnsafePtr<Color32>(); // .GetUnsafePtr<Color>();
                    for (int vBlockIndex = 0, vBlockIdxMax = lv3VoxelSize; vBlockIndex < vBlockIdxMax; vBlockIndex++)
                    {
                        for (int uBlockIndex = 0, uBlockIdxMax = lv3VoxelSize; uBlockIndex < uBlockIdxMax; uBlockIndex++)
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
                                    int uPixel = uPixelBase + uPixelSub;

                                    var pixel = voxelLitShadowInfoPtr[vPixel * width + uPixel];// voxelLitShadowInfo.GetPixel(uPixel, vPixel, 0);
                                    errorIndex = vPixel * width + uPixel;
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

        litShadowInfoArrayLv4.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();


        DownSample(lv3VoxelSize, lv4VoxelSize, litShadowInfoArrayLv3, litShadowInfoArrayLv4);
        litShadowInfoArrayLv3.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        DownSample(lv2VoxelSize, lv3VoxelSize, litShadowInfoArrayLv2, litShadowInfoArrayLv3);
        litShadowInfoArrayLv2.Apply(false, false);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        DownSample(rootVoxelSize, lv2VoxelSize, litShadowInfoArrayLv1, litShadowInfoArrayLv2);
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
            var litShadowInfoLv1 = litShadowInfoArrayLv1.GetPixels(dVoxelIndex);

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

                                            colorBlock32x32[queryIdx % 32 * 32 + pixelIdx] = colorLv3;


                                        }
                                    }

                                    frontColor = new Color(axeAllLitFront[0] ? 1.0f : 0.0f + (axeAllShadowFront[0] ? 0.0f : 1.0f) + (axeIntersectedFront[0] ? 0.5f : 0),
                                        axeAllLitFront[1] ? 1 : 0 + (axeIntersectedFront[1] ? 0.5f : 0),
                                        axeAllLitFront[2] ? 1 : 0 + (axeIntersectedFront[2] ? 0.5f : 0),
                                        axeAllLitFront[3] ? 1 : 0 + (axeIntersectedFront[3] ? 0.5f : 0));
                                    backColor = new Color(axeAllLitBack[0] ? 1 : 0 + (axeAllShadowBack[0] ? 0 : 1) + (axeIntersectedBack[0] ? 0.5f : 0),
                                        axeAllLitBack[1] ? 1 : 0 + (axeIntersectedBack[1] ? 0.5f : 0),
                                        axeAllLitBack[2] ? 1 : 0 + (axeIntersectedBack[2] ? 0.5f : 0),
                                        axeAllLitBack[3] ? 1 : 0 + (axeIntersectedBack[3] ? 0.5f : 0));
                                }

                                int seqFront = vPixelIndex * uPixelMax + uPixelIndex;
                                int seqBack = seqFront + 4;


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

}
