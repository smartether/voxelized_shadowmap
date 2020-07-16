using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Jobs.LowLevel;
public class VxOnJobSystem : MonoBehaviour
{
    public unsafe struct AccelerationJob : IJobParallelFor, Unity.Jobs.IJobParallelForBatch
    {
        int originWidth;
        int originHeight;
        int targetWidth;
        int targetHeight;
        System.Int64 _srcAddr;
        System.Int64 _dstAddr;
        public AccelerationJob(long srcAddr, long dstAddr, int originWidth, int originHeight, int targetWidth, int targetHeight)
        {
            _srcAddr = srcAddr;
            _dstAddr = dstAddr;
            this.originWidth = originWidth;
            this.originHeight = originHeight;
            this.targetWidth = targetWidth;
            this.targetHeight = targetHeight;
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
                byte* srcPtr = (byte*)_srcPtr.ToPointer();
                byte* dstPtr = (byte*)_dstPtr.ToPointer();
                dstPtr[index] = (byte)(srcPtr[index] * srcPtr[index]);
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
        new AccelerationJob(srcAddr,dstAddr,0,0,0,0).Schedule(1024, 32).Complete();
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
