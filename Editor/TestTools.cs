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

    [MenuItem("Tools/LoadTextureFromByte")]
    public static void CreateTextureFromByte()
    {
        TextAsset byteData = Selection.activeObject as TextAsset;
        Texture2D tex = new Texture2D(4096, 4096, TextureFormat.Alpha8, false, true);
        tex.SetPixelData<byte>(byteData.bytes, 0);
        tex.Apply(false, false);
        AssetDatabase.CreateAsset(tex, "Assets/tex.asset");
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
        for (int i = 0; i < 32; i++)
        {
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
        var writeMem = new System.IO.UnmanagedMemoryStream(ptr, 1024, 4096 * 4096, System.IO.FileAccess.Write);

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
        using (var mem = new System.IO.UnmanagedMemoryStream(arrayDecompressedPtr, dataSize, dataSize, System.IO.FileAccess.Read))
        {
            using (var f = new System.IO.FileStream("decompressed_array.bytes", System.IO.FileMode.Create, System.IO.FileAccess.Write))
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
        for (int idx = 0; idx < 2048; idx++)
        {
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

            var bufferNa = new NativeArray<byte>((int)compressedSize, Allocator.Temp);
            var bufferUnmanged = new System.IO.UnmanagedMemoryStream((byte*)bufferNa.GetUnsafePtr(), compressedSize, compressedSize, System.IO.FileAccess.Write);

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

    [MenuItem("Tools/TestDot")]
    public static void TestDot()
    {
        for(int i = 0; i < 64; i++)
        {
            var r = Vector4.Normalize(Matrix4x4.Rotate(Quaternion.Euler(6 * i, 2 * i, 15 * i)).MultiplyVector(Vector3.up));
            var l = Vector4.Normalize(Matrix4x4.Rotate(Quaternion.Euler(1 * i, 3 * i, 5 * i)).MultiplyVector(Vector3.up));
            r.w = i * 5;
            l.w = i * 8;
            var dotValue = Vector4.Dot(r, l);
            var lenL = Mathf.Sqrt(l.x * l.x + l.y * l.y + l.z * l.z + l.w * l.w);
            var lenR = Mathf.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + l.w * l.w);
            var cosRL = (l.x * r.x + l.y * r.y + l.z * r.z + l.w * r.w) / (lenR * lenL);
            var dotValue1 = lenL * lenR * cosRL;
            Debug.Log(dotValue);
            Debug.Log(dotValue1);
        }
    }
}
