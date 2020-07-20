using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Jobs.LowLevel;
using F = System.Single;
public class VxOnJobSystem : MonoBehaviour
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct CompressedLitInfo
    {
        // voxel above it are lit
        public System.UInt16 litEndVoxelId;
        // voxel following it are shadow
        public System.UInt16 shadowStartVoxelId;
        public byte flags;
        // align
        public byte flags1;
        public bool IsAllIntersected
        {
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
        }
        public bool IsAllLit 
        { 
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
        }
    }

    public unsafe struct AccelerationJob : IJobParallelFor, Unity.Jobs.IJobParallelForBatch
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

        public AccelerationJob(long srcAddr, long dstAddr, int originWidth, int originHeight, int targetWidth, int targetHeight, int scaler, F depthPerVoxel = 1 / 2048.0f, int maxVoxel = 1024)
        {
            _srcAddr = srcAddr;
            _dstAddr = dstAddr;
            this.originWidth = originWidth;
            this.originHeight = originHeight;
            this.targetWidth = targetWidth;
            this.targetHeight = targetHeight;
            this.depthPerVoxel = 1 / 2048.0f;
            this.scaler = scaler;
            this.maxVoxel = maxVoxel;
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

                int dstX = index % targetWidth;
                int dstY = index / targetWidth;
                F depthMax = -0.1f;
                F depthMin = 1.1f;
                F avgDepth = 0;
                for (int v = 0; v < scaler; v++)
                    for (int u = 0; u < scaler; u++)
                    {
                        int srcY = scaler * dstY + v;
                        int srcX = scaler * dstX + u;
                        int srcIndex = srcY * originWidth + srcX;
                        Color32 encodedDepth = srcPtr[srcIndex];
                        F depth = DecodeFloatRGBA(new Vector4(encodedDepth.r, encodedDepth.g, encodedDepth.b, encodedDepth.a));
                        depthMax = Mathf.Max(depthMax, depth);
                        depthMin = Mathf.Min(depthMin, depth);
                        avgDepth += depth;
                    }

                avgDepth /= scaler * scaler;

                bool isAllShadow = true;
                bool isAllLit = true;
                bool isAllIntersected = true;

                // assume shadow voxel start
                F shadowPlaneFrontDepth = depthMin - depthMin % depthPerVoxel;
                F shadowPlaneBackDepth = shadowPlaneFrontDepth - depthPerVoxel;
                
                // search until all shadow
                int shadowStartDepth = Mathf.RoundToInt((1 - shadowPlaneFrontDepth) / depthPerVoxel);
                for (; shadowStartDepth < maxVoxel; shadowStartDepth++)
                {
                    for (int v = 0; v < 2; v++)
                    {
                        for (int u = 0; u < 2; u++)
                        {
                            int srcY = scaler * dstY + v;
                            int srcX = scaler * dstX + u;
                            int srcIndex = srcY * originWidth + srcX;
                            Color32 encodedDepth = srcPtr[srcIndex];
                            F depth = DecodeFloatRGBA(new Vector4(encodedDepth.r, encodedDepth.g, encodedDepth.b, encodedDepth.a));
                            isAllShadow &= depth > shadowPlaneFrontDepth;
                        }
                    }
                    shadowPlaneFrontDepth += depthPerVoxel;
                }
                if (isAllShadow)
                {
                    //ushort shadowedVoxelIdx = (ushort)Mathf.RoundToInt((1 - depth) / depthPerVoxel);
                    dstPtr[index].shadowStartVoxelId = (ushort)Mathf.Clamp(shadowStartDepth, 0, maxVoxel);
                    //int y = index / originWidth;
                }

                F litPlaneFrontDepth = depthMax - depthMax % depthPerVoxel;
                F litPlaneBackDepth = litPlaneFrontDepth - depthPerVoxel;
            }

        }

        public void Execute(int startIndex, int count)
        {
            
        }

        public static float DecodeFloatRGBA(Vector4 enc)
        {
            Vector4 kDecodeDot = new Vector4(1.0f, 1 / 255.0f, 1 / 65025.0f, 1 / 16581375.0f);
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

    [ContextMenu("Test")]
    public void Test()
    {
        long srcAddr = System.Runtime.InteropServices.Marshal.AllocHGlobal(1024).ToInt64();
        long dstAddr = System.Runtime.InteropServices.Marshal.AllocHGlobal(1024).ToInt64();
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
    }

    // Start is called before the first frame update
    void Start()
    { 
        new AccelerationJob().Run(1024);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

}
