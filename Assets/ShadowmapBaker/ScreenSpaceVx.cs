// #define _ENABLE_RT_1

using AOT;
using MagicaCloth;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class ScreenSpaceVx : MonoBehaviour
{
    public VxShadowmap vxShadowmap;
    public Renderer[] objs;
    public Material litMat;
    public Material guassMat;
    public Camera cam;
    public bool CustomScreenTextureSize = false;
    public Vector2 screenShadowTextureSize;
    public bool filterWithLayer = false;
    public LayerMask layer;
    Camera viewCamera
    {
        get
        {
            if(cam == null)
            {
#if UNITY_EDITOR
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;

#else
                cam = Camera.main;
#endif
            }
            return cam;
        }
    }
    public RenderTexture shadowTexture;
    public RenderTexture shadowTextureBlur;
    
    // Start is called before the first frame update
    void Start()
    {
        //var viewCamera = cam = UnityEditor.SceneView.lastActiveSceneView.camera;
        //viewCamera.SetReplacementShader(UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/ShadowmapBaker/Resources/VxRender.shader"), "");
        //viewCamera.Render();

        vxShadowmap.LoadUniformData();
        SetupViewCamera();
    }

    private void OnDestroy()
    {
        ClearCommandBuff();
    }

    private void OnApplicationQuit()
    {
        ClearCommandBuff();
    }

    CommandBuffer matrixCmd;

#if UNITY_EDITOR
    //[UnityEditor.MenuItem("Tools/SelectViewCamera")]
    [ContextMenu("SelectViewCamera")]
    public void SelectViewCamera()
    {
        Selection.activeObject = viewCamera;

    }
#endif

    [ContextMenu("ClearCommandBuff")]
    public void ClearCommandBuff()
    {
        var commandBuffers = viewCamera.GetCommandBuffers(CameraEvent.BeforeForwardOpaque);
        for (int i = 0, c = commandBuffers.Length; i < c; i++)
        {
            if (commandBuffers[i].name == "VxShadowBlurFeature" || "VxShadowMatrix" == commandBuffers[i].name)
            {
                viewCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffers[i]);
            }
        }
    }

    //[UnityEditor.MenuItem("Tools/SetupViewCamera")]
    [ContextMenu("SetupViewCamera")]
    public void SetupViewCamera()
    {
        matrixCmd = new CommandBuffer();
        matrixCmd.name = "VxShadowMatrix";
        //EditorApplication.update += () =>
        //{
        //    matrixCmd.Clear();
        //    matrixCmd.SetViewProjectionMatrices(viewCamera.worldToCameraMatrix, viewCamera.projectionMatrix);
        //};

        var commandBuffers = viewCamera.GetCommandBuffers(CameraEvent.BeforeForwardOpaque);
        for(int i = 0, c = commandBuffers.Length; i < c; i++)
        { 
            if(commandBuffers[i].name == "VxShadowBlurFeature")
            { 
                viewCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffers[i]);
            }
        }
        var cmd = GetCommandBuff();
        //viewCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, matrixCmd);
        viewCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, cmd);
    }


    Matrix4x4 mat1;
    Matrix4x4 mat2;


    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    delegate void IssueCallback(int eventId);

    [MonoPInvokeCallback(typeof(IssueCallback))]
    private void SyncMatrix(int eventId)
    {
        //var viewCamera = UnityEditor.SceneView.lastActiveSceneView.camera;
        //mat1 = viewCamera.worldToCameraMatrix;
        //mat2 = viewCamera.projectionMatrix;
        //Debug.Log(eventId);
    }

    CommandBuffer GetCommandBuff()
    {
        Vector2 screenTextureSize = new Vector2(cam.scaledPixelWidth, cam.scaledPixelHeight);
        if (CustomScreenTextureSize)
        {
            screenTextureSize = new Vector2(Mathf.ClosestPowerOfTwo((int)screenShadowTextureSize.x), Mathf.ClosestPowerOfTwo((int)screenShadowTextureSize.y));
        }

        //var _VxShadow_BlurTex = RenderTexture.GetTemporary((int)screenTextureSize.x , (int)screenTextureSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, 2);
        //_VxShadow_BlurTex.wrapMode = TextureWrapMode.Clamp;
        //_VxShadow_BlurTex.name = "_VxShadow_Blur";
        CommandBuffer cmd = new CommandBuffer();
        //cmd.IssuePluginEvent(System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(new IssueCallback(SyncMatrix)), 0);
        cmd.name = "VxShadowBlurFeature";
        // cmd.SetViewProjectionMatrices(mat1, mat2);
#if UNITY_EDITOR && _ENABLE_RT_1
        cmd.GetTemporaryRT(Shader.PropertyToID("_ScreenSpaceShadow"), viewCamera.pixelWidth, viewCamera.pixelHeight, 16, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var _screenSpaceShadowRT = new RenderTargetIdentifier("_ScreenSpaceShadow");
#endif
        cmd.GetTemporaryRT(Shader.PropertyToID("_VxShadow_Blur"), Mathf.ClosestPowerOfTwo((int)screenShadowTextureSize.x), Mathf.ClosestPowerOfTwo((int)screenShadowTextureSize.y), 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        
        var _VxShadow_BlurRT = new RenderTargetIdentifier("_VxShadow_Blur");

        var _CameraTarget = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
#if UNITY_EDITOR && _ENABLE_RT_1
        cmd.SetRenderTarget(_screenSpaceShadowRT);
#else
        cmd.SetRenderTarget(_CameraTarget);
#endif

        // cmd.SetRenderTarget(_screenSpaceShadowRT);
        cmd.ClearRenderTarget(true, true, Color.white);
        //cmd.SetViewProjectionMatrices(viewCamera.worldToCameraMatrix, viewCamera.projectionMatrix);
        if (filterWithLayer) {
            objs = GameObject.FindObjectsOfType<Renderer>();
        }
        for (int i = 0, c = objs.Length; i < c; i++)
        {
            if (filterWithLayer && (((1 << objs[i].gameObject.layer) & layer.value) == 0 ))  //|| (objs[i].gameObject.layer & layer.value) == 0
                continue;
            cmd.DrawRenderer(objs[i], litMat);
        }
        
#if UNITY_EDITOR && _ENABLE_RT_1

        cmd.Blit(_screenSpaceShadowRT, shadowTexture);
        cmd.SetGlobalTexture("_ScreenSpaceShadow", _screenSpaceShadowRT);
        cmd.Blit(_screenSpaceShadowRT, _VxShadow_BlurRT, guassMat);
#else
        cmd.Blit(_CameraTarget, shadowTexture);
        cmd.SetGlobalTexture("_ScreenSpaceShadow", _CameraTarget);
        cmd.Blit(_CameraTarget, _VxShadow_BlurRT, guassMat);
#endif
        cmd.SetGlobalTexture("_VxShadow_Blur", _VxShadow_BlurRT);
        cmd.SetRenderTarget(_CameraTarget);
        cmd.ClearRenderTarget(true, true, Color.black);
        //var waitBlur = cmd.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing); //  cmd.CreateAsyncGraphicsFence(SynchronisationStage.PixelProcessing);//
        //cmd.WaitOnAsyncGraphicsFence(waitBlur);
        //cmd.WaitOnAsyncGraphicsFence(waitBlur, SynchronisationStageFlags.PixelProcessing);
        cmd.Blit(_VxShadow_BlurRT,  shadowTextureBlur);
        cmd.SetRenderTarget(_CameraTarget);
        //cmd.Blit(new RenderTargetIdentifier("_VxShadow_Blur"), new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive), guassMat);
        //cmd.ReleaseTemporaryRT(Shader.PropertyToID("_ScreenSpaceShadow"));

        return cmd;
    }

    private void OnWillRenderObject()
    {
        Debug.Log(name);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
