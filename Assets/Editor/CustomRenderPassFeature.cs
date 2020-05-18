using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions.Must;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal.Internal;

public class CustomRenderPassFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class CustomRenderPass : ScriptableRenderPass
    {
        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_RenderStateBlock;
        List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
        string m_ProfilerTag;
        ProfilingSampler m_ProfilingSampler;
        bool m_IsOpaque;
        public Material shadowMat;
        public RenderTexture shadowTarget;
        public Camera camera;

        public CustomRenderPass(string profilerTag, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
        {
            m_ProfilerTag = profilerTag;
            m_ProfilingSampler = new ProfilingSampler(profilerTag);
            m_ShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
            m_ShaderTagIdList.Add(new ShaderTagId("LightweightForward"));
            m_ShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
            renderPassEvent = evt;
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_IsOpaque = opaque;

            if (stencilState.enabled)
            {
                m_RenderStateBlock.stencilReference = stencilReference;
                m_RenderStateBlock.mask = RenderStateMask.Stencil;
                m_RenderStateBlock.stencilState = stencilState;
            }
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            //var cameras = GameObject.FindObjectsOfType<Camera>();
            //for(int i=0;i< cameras.Length; i++)
            //{
            //    if(cameras[i].name == "ShadowCamera")
            //    {
            //        context.SetupCameraProperties(cameras[i]);
            //    }
            //}

            //context.SetupCameraProperties(GameObject.Find("ShadowCamera").GetComponent<Camera>());
            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
            //cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
            //cmd.SetRenderTarget(shadowTarget);
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                Camera camera = renderingData.cameraData.camera;
                var sortFlags = (m_IsOpaque) ? renderingData.cameraData.defaultOpaqueSortFlags : SortingCriteria.CommonTransparent;
                var drawSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortFlags);
                Material shadowMat1 = new Material(Shader.Find("Unlit/Shadowmap"));
                drawSettings.overrideMaterial = shadowMat1;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings, ref m_RenderStateBlock);
                // Render objects that did not match any shader pass with error shader
                
                //RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, m_FilteringSettings, SortingCriteria.None);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

        }
    }

    public CustomRenderPass m_ScriptablePass;

    public string profilerTag;
    public LayerMask layermask;
    public override void Create()
    {
        m_ScriptablePass = new CustomRenderPass(profilerTag, true, RenderPassEvent.AfterRenderingOpaques, new RenderQueueRange(1000, 3000), layermask, StencilState.defaultValue, 0);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // renderer.ConfigureCameraTarget(shadowTarget, new RenderTargetIdentifier(BuiltinRenderTextureType.Depth));
        renderer.EnqueuePass(m_ScriptablePass);

        //m_ScriptablePass.shadowMesh.Clear();
        //var shadowCasters = GameObject.FindObjectsOfType<Shadowcaster >();
        //for (int i = 0; i < shadowCasters.Length; i++)
        //{
        //    m_ScriptablePass.shadowMesh.Add(shadowCasters[i].GetComponent<MeshFilter>().mesh);
        //}
        m_ScriptablePass.shadowMat = shadowMat;
        m_ScriptablePass.shadowTarget = shadowTarget;
    }

    public Material shadowMat;
    public RenderTexture shadowTarget;
    public Camera camera;

}


