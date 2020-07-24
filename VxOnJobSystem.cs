#define _LITINFO_NO_CACHE_
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine.UIElements;
using F = System.Double;
public class VxOnJobSystem : MonoBehaviour
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct CompressedLitInfo
    {
        public static int currentCompressedLitInfoMaxVoxelId = 4096;
        // voxel above it are lit
        public System.UInt16 litEndVoxelId;
        // voxel following it are shadow
        public System.UInt16 shadowStartVoxelId;
#if !_LITINFO_NO_CACHE_
        public byte flags;
        // align
        public byte flags1;
#endif
        public bool IsSpecial
        {
            get { return IsAllIntersected || IsAllLit || IsAllShadow; }
        } 
        // all voxel on axsi is intersected
        public bool IsAllIntersected
        {
#if _LITINFO_NO_CACHE_
            get
            {
                return shadowStartVoxelId >= currentCompressedLitInfoMaxVoxelId - 2 && litEndVoxelId <= 2;
            }
#else
            get
            {
                return (flags & (1 << 7 ))!= 0;
            }
            set
            {
                unchecked
                {
                    if (value)
                        flags |= (byte)((byte)1 << 7);
                    else
                        flags &= (byte)(~((byte)1 << 7));
                }
            }
#endif
        }
        // all voxel on axsi is lit
        public bool IsAllLit
        {
#if _LITINFO_NO_CACHE_
            get
            {
                return litEndVoxelId >= currentCompressedLitInfoMaxVoxelId - 2;
            }
#else
            get
            {
                return (flags & (1 << 6)) != 0;
            }
            set
            {
                unchecked
                {
                    if (value)
                        flags |= (byte)((byte)1 << 6);
                    else
                        flags &= (byte)(~((byte)1 << 6));
                }
            }
#endif
        }
        // all voxel on axsi is shadow 
        public bool IsAllShadow
        {
#if _LITINFO_NO_CACHE_
            get
            {
                return shadowStartVoxelId <= 2;
            }
#else
            get
            {
                return (flags & (1 << 5)) != 0;
            }
            set
            {
                unchecked
                {
                    if (value)
                        flags |= (byte)((byte)1 << 5);
                    else
                        flags &= (byte)(~((byte)1 << 5));
                }
            }
#endif
            
        }

        public override string ToString()
        {
            return string.Format("$$ litend: {0}  shadowStart: {1}", litEndVoxelId, shadowStartVoxelId);
        }
    }

    public unsafe struct AccelerationJob : IJobParallelFor, Unity.Jobs.IJobParallelForBatch, Unity.Jobs.IJobFor
    {
        int originWidth;
        int originHeight;
        int targetWidth;
        int targetHeight;
        F depthPerVoxel;
        int scaler;
        System.Int64 _srcAddr;
        System.Int64 _dstAddr;
        int maxVoxel;

        int progress;

        public AccelerationJob(long srcAddr, long dstAddr, int originWidth, int originHeight, int targetWidth, int targetHeight, int scaler, F depthPerVoxel = 1 / 2048.0f, int maxVoxel = 1024)
        {
            _srcAddr = srcAddr;
            _dstAddr = dstAddr;
            this.originWidth = originWidth;
            this.originHeight = originHeight;
            this.targetWidth = targetWidth;
            this.targetHeight = targetHeight;
            this.depthPerVoxel = depthPerVoxel; 
            this.scaler = scaler;
            this.maxVoxel = maxVoxel;
            progress = 0;
        }

        private void SetPixel(int x, int y, Color32 color)
        {

        }

        private void SetByte(int x, int y, byte byteValue)
        {

        }

        private void Set2Bit(int x, int y, bool highBit, bool lowBit)
        {

        }

        public void Execute(int index)
        {
            unsafe
            {
                System.IntPtr _srcPtr = new System.IntPtr(_srcAddr);
                System.IntPtr _dstPtr = new System.IntPtr(_dstAddr);
                Color32* srcPtr = (Color32*)_srcPtr.ToPointer();
                CompressedLitInfo* dstPtr = (CompressedLitInfo*)_dstPtr.ToPointer();
                Color32* dstColorPtr = (Color32*) _dstPtr.ToPointer();

                int srcBaseX = (index % targetWidth ) * scaler;
                int srcBaseY = (index / targetWidth) * scaler;
                F depthMax = -0.1f;
                F depthMin = 1.1f;
                F avgDepth = 0;
                for (int v = 0; v < scaler; v++)
                    for (int u = 0; u < scaler; u++)
                    {
                        int srcY = srcBaseY + v;
                        int srcX = srcBaseX + u;
                        int srcIndex = srcY * originWidth + srcX;
                        Color32 encodedDepth = srcPtr[srcIndex];
                        Color fColor = encodedDepth;
                        F depth = DecodeFloatRGBA(new Vector4(fColor.r, fColor.g, fColor.b, fColor.a));
                        depthMax = System.Math.Max(depthMax, depth);
                        depthMin = System.Math.Min(depthMin, depth);
                        avgDepth += depth;
                    }

                avgDepth /= scaler * scaler;

                bool isAllShadow = true;
                bool isAllLit = true;
                bool isAllIntersected = true;

                // find out shadow voxel start
                F shadowPlaneFrontDepth = depthMin - (depthMin % depthPerVoxel);
                F shadowPlaneBackDepth = shadowPlaneFrontDepth - depthPerVoxel;
                
                // search until all shadow
                int shadowStartDepth = (int)System.Math.Round(System.Math.Min(maxVoxel,(1 - shadowPlaneFrontDepth) / depthPerVoxel));
                for (; shadowStartDepth < maxVoxel; shadowStartDepth++)
                {
                    isAllShadow = true;
                    for (int v = 0; v < scaler; v++)
                    {
                        for (int u = 0; u < scaler; u++)
                        {
                            int srcY = srcBaseY + v;
                            int srcX = srcBaseX + u;
                            int srcIndex = srcY * originWidth + srcX;
                            Color32 encodedDepth = srcPtr[srcIndex];
                            Color fColor = encodedDepth;
                            F depth = DecodeFloatRGBA(new Vector4(fColor.r, fColor.g, fColor.b, fColor.a));
                            isAllShadow &= depth > shadowPlaneFrontDepth;
                        }
                    }
                    if (isAllShadow)
                        break;
                    shadowPlaneFrontDepth -= depthPerVoxel;
                }
                dstPtr[index].shadowStartVoxelId = (ushort)Mathf.Clamp(shadowStartDepth, 0, maxVoxel);
                Color32* colorPtr = (Color32*) dstPtr + index;
                // colorPtr->r = (byte)(dstPtr[index].litEndVoxelId / (float) CompressedLitInfo.currentCompressedLitInfoMaxVoxelId * 255);


                F litPlaneFrontDepth = depthMax - (depthMax % depthPerVoxel);
                F litPlaneBackDepth = litPlaneFrontDepth - depthPerVoxel;
                int litEndDepth = (int) System.Math.Round(System.Math.Min(maxVoxel,(1 - litPlaneBackDepth) / depthPerVoxel));
                for(;litEndDepth > -1; litEndDepth--)
                { 
                    isAllLit = true;
                    for (int v = 0; v < scaler; v++)
                    {
                        for (int u = 0; u < scaler; u++)
                        {
                            int srcY = srcBaseY + v;
                            int srcX = srcBaseX + u;
                            int srcIndex = srcY * originWidth + srcX;
                            Color32 encodedDepth = srcPtr[srcIndex];
                            Color fColor = encodedDepth;
                            F depth = DecodeFloatRGBA(new Vector4(fColor.r, fColor.g, fColor.b, fColor.a));
                            isAllLit &= depth < litPlaneBackDepth;
                        }
                    }
                    if (isAllLit)
                        break;
                    litPlaneBackDepth += depthPerVoxel;
                }
                dstPtr[index].litEndVoxelId = (ushort)Mathf.Clamp(litEndDepth, 0, maxVoxel);
                // colorPtr->b = (byte)(dstPtr[index].shadowStartVoxelId / (float) CompressedLitInfo.currentCompressedLitInfoMaxVoxelId * 255);

                if (dstPtr[index].litEndVoxelId >= dstPtr[index].shadowStartVoxelId)
                {
                    Debug.LogFormat("$$ litEnd:{0} shadowStart:{1} index:{2}", 
                        dstPtr[index].litEndVoxelId,
                        dstPtr[index].shadowStartVoxelId,
                        index);
                }
            }

        }

        public void Execute(int startIndex, int count)
        {
            for (int index = startIndex, maxIndex = startIndex + count; index < maxIndex; index++)
            {
                Execute(index);
            }
        }

        static Vector4 kDecodeDot = new Vector4(1.0f, 1 / 255.0f, 1 / 65025.0f, 1 / 16581375.0f);
        public static float DecodeFloatRGBA(Vector4 enc)
        {
            return Vector4.Dot(enc, kDecodeDot);
        }

        public static float Dot(Vector4 l, Vector4 r)
        {
            var lenL = Mathf.Sqrt(l.x * l.x + l.y * l.y + l.z * l.z + l.w * l.w);
            var lenR = Mathf.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + l.w * l.w);
            var cosRL = (l.x * r.x + l.y * r.y + l.z * r.z + l.w * r.w) / (lenR * lenL);
            var dotValue1 = lenL * lenR * cosRL;
            return dotValue1;
        }

    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/RunVxOnJobSystem")]
    public static unsafe void Test()
    {
        Texture2D shadowmap = UnityEditor.Selection.activeObject as Texture2D;
        var shadowmapData = shadowmap.GetRawTextureData<Color32>();
        NativeArray<Color32> targetData = new NativeArray<Color32>(4096 * 4096 * sizeof(CompressedLitInfo), Allocator.Temp);
        long srcAddr = new System.IntPtr(shadowmapData.GetUnsafePtr()).ToInt64();
        long dstAddr = new System.IntPtr(targetData.GetUnsafePtr()).ToInt64();

        AccelerationJob job = new AccelerationJob(srcAddr, dstAddr,
            8192, 8192,
            4096, 4096,
            2, 1 / 4095f,
            4095);
        //job.Schedule(4096 * 4096, 64);
        JobHandle jobHandle = new JobHandle();
        // var jobState = job.ScheduleParallel(4096 * 4096, 64, jobHandle);
        var jobState = job.Schedule(4096 * 4096, 64);
        while (!jobState.IsCompleted)
        {
            System.Threading.Thread.Sleep(30);
        }
        Texture2D texLitInfo = new Texture2D(4096, 4096, TextureFormat.RGBA32, false, true);
        texLitInfo.SetPixelData<Color32>( targetData, 0);
        texLitInfo.Apply(false, false);
        UnityEditor.AssetDatabase.CreateAsset(texLitInfo, "Assets/texLitInfo.asset");
        /*
        unsafe
        {
            byte* src = (byte*)new System.IntPtr(srcAddr).ToPointer();
            for (int i = 0; i < 1024; i++)
            {
                src[i] = 12;
            }
        }
        //new AccelerationJob(srcAddr,dstAddr,0,0,0,0).Schedule(1024, 32).Complete();
        unsafe
        {
            byte* dst = (byte*)new System.IntPtr(dstAddr).ToPointer();
            for (int i = 0; i < 1024; i++)
            {
                Debug.Log(dst[i]);
            }
        }
        */
    }
#endif
    
    [MenuItem("Tools/PrintLitInfo")]
    public static void PrintLitInfo()
    {
        Texture2D tex = Selection.activeObject as Texture2D;
        var data = tex.GetRawTextureData<CompressedLitInfo>();
        for (int i = 0; i < data.Length; i++)
        {
            var info = data[i];
            if(info.litEndVoxelId < 2000)
                Debug.Log(info);
        }
    }
    

    // Update is called once per frame
    void Update()
    {
        
    }

}
