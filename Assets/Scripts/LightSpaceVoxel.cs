using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Jobs;

public class LightSpaceVoxel : MonoBehaviour
{
    public enum VoxelLevel
    {
        Off = 0,
        RootLevel1 = 1,
        Level2 = 2,
        Level3 = 3
    }

    float[] levelVoxelSizeUnit;
    float[] levelVoxelPositionOffset;

    public VoxelLevel voxelLevel;

    Vector3 veclightPos;
    Vector3 lightDir;
    Vector3 lightRightDir;
    Vector3 lightUpDir;

    public int rootVoxelWidthSize = 8;
    public float OrthoProjSize = 20;
    public float nearClip = 0.3f;
    public float farClip = 500;
    private void Update()
    {
        ComputeBuffer computebuffer = new ComputeBuffer(1024, 4, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        
        MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();
        
    }


    private void voxelPosCalcul(float voxelSize, Vector3 lightPos, float voxelSizeUnit, float voxelPositionOffset, int xDir = 1, int yDir = 1,List<Vector3> cubePosList = null)
    {
        for (int y = 0; y < voxelSize / 2; y++)
        {
            for (int x = 0; x < voxelSize / 2; x++)
            {
                var voxelPos = lightPos + (xDir* lightRightDir * (x * voxelSizeUnit + voxelPositionOffset)) +
                    (yDir* lightUpDir * (y * voxelSizeUnit + voxelPositionOffset));
                if(cubePosList != null)
                {
                    cubePosList.Add(voxelPos);
                }
            }
        }
    }

    private void SpareateSpace2Voxel(VoxelLevel vxlevel, List<Vector3> cubePos)
    {
        float voxelSizeUnit = levelVoxelSizeUnit[(int)vxlevel - 1];
        float voxelPostitionOffset = levelVoxelPositionOffset[(int)vxlevel - 1];

        int sizeFact = Mathf.RoundToInt(Mathf.Pow(2, (int)voxelLevel - 1));
        for (int i = 0; i < Mathf.CeilToInt(farClip / voxelSizeUnit); i++)
        {
            voxelPosCalcul(rootVoxelWidthSize * sizeFact, veclightPos + (lightDir * (i * voxelSizeUnit + voxelPostitionOffset)), voxelSizeUnit, voxelPostitionOffset, 1, 1, cubePos);
            voxelPosCalcul(rootVoxelWidthSize * sizeFact, veclightPos + (lightDir * (i * voxelSizeUnit + voxelPostitionOffset)), voxelSizeUnit, voxelPostitionOffset, 1, -1, cubePos);
            voxelPosCalcul(rootVoxelWidthSize * sizeFact, veclightPos + (lightDir * (i * voxelSizeUnit + voxelPostitionOffset)), voxelSizeUnit, voxelPostitionOffset, -1, -1, cubePos);
            voxelPosCalcul(rootVoxelWidthSize * sizeFact, veclightPos + (lightDir * (i * voxelSizeUnit + voxelPostitionOffset)), voxelSizeUnit, voxelPostitionOffset, -1, 1, cubePos);
        }

    }

    private void OnDrawGizmos()
    {
        veclightPos = transform.position;
        lightDir = transform.forward;
        lightRightDir = transform.right;
        lightUpDir = transform.up;


        float level1VoxelSizeUnit = OrthoProjSize * 2.0f / (float)(rootVoxelWidthSize);
        float level2VoxelSizeUnit = level1VoxelSizeUnit * 0.500f;
        float level3VoxelSizeUnit = level2VoxelSizeUnit * 0.500f;
        levelVoxelSizeUnit = new float[] { level1VoxelSizeUnit, level2VoxelSizeUnit, level3VoxelSizeUnit };
        
        float level1VoxelPositionOffset = level1VoxelSizeUnit * 0.500f;
        float level2VoxelPositionOffset = level2VoxelSizeUnit * 0.500f;
        float level3VoxelPositionOffset = level3VoxelSizeUnit * 0.500f;
        levelVoxelPositionOffset = new float[] { level1VoxelPositionOffset, level2VoxelPositionOffset, level3VoxelPositionOffset };

        var list = new List<Vector3>();
        SpareateSpace2Voxel(voxelLevel, list);

        list.ForEach((vec) =>
        {
            Gizmos.matrix = Matrix4x4.TRS(vec + lightDir * nearClip, transform.rotation, transform.lossyScale);// Matrix4x4.Rotate(transform.rotation);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one * levelVoxelSizeUnit[(int)voxelLevel - 1]);
            
        });
        //Gizmos.DrawWireCube()
    }

}
