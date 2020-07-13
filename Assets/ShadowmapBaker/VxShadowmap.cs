using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Contexts;
using UnityEditor;
using UnityEngine;

[System.Serializable]
public class VxShadowmapUniformData
{
    public string SceneName;
    public int SectionIndex;
    public float _ShadowAlpha;
    public float _ShadowBias;
    public float _ShadowBias1;
    public float _DEBUG_FACT;
    public float _level1TexSize;
    public float _level2TexArrayDepth;
    public float _level4TexArrayDepth;
    public float _ShadowDensity;
    public float _ShadowBalance;

    public Vector4 _VoxelParams;
    public Vector4 _VoxelParamsLv2;
    public Vector4 _VoxelParamsLv3;
    public Vector4 _ProjSizeParams;

    public Matrix4x4 _LitViewMatrix;
    public Matrix4x4 _LitProjMatrix;


    [SerializeReference]
    public Texture _Level1IndexMap;
    [SerializeReference]
    public Texture _Level2LitShadowInfoArray;
    [SerializeReference]
    public Texture _Level4LitShadowInfoArray;
    [SerializeReference]
    public Texture _VoxelShadowmap;
    [SerializeReference]
    public Texture _Shadowmap;

    [SerializeField]
    private string _json;

#if UNITY_EDITOR
    public void Serializable()
    {
        _json = EditorJsonUtility.ToJson(this, true);
    }
#endif
    public void Deserializable()
    {
        JsonUtility.FromJsonOverwrite(_json, this);
    }

    public void LoadUniformData()
    {
        var fields = GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        for (int i = 0, c = fields.Length; i < c; i++)
        {
            var field = fields[i];
            var fieldType = field.FieldType;
            if (typeof(float) == fieldType)
            {
                var fValue = (float)field.GetValue(this);
                Shader.SetGlobalFloat(field.Name, fValue);
            }
            else if (typeof(Vector4) == fieldType)
            {
                var fValue = (Vector4)field.GetValue(this);
                Shader.SetGlobalVector(field.Name, fValue);
            }
            else if (typeof(Texture) == fieldType)
            {
                var fValue = (Texture)field.GetValue(this);
                Shader.SetGlobalTexture(field.Name, fValue);
            }
            else if(typeof(Matrix4x4) == fieldType)
            {
                var fValue = (Matrix4x4)field.GetValue(this);
                Shader.SetGlobalMatrix(field.Name, fValue);
            }
        }
    }

    public void FillUniformInfoFromMaterial(Material mat)
    {
        var fields = GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        for (int i = 0, c = fields.Length; i < c; i++)
        {

            var field = fields[i];
            string fieldName = field.Name;
            var fieldType = field.FieldType;
            if (typeof(float) == fieldType)
            {
                var fValue = mat.GetFloat(fieldName);
                field.SetValue(this, fValue);
            }
            else if (typeof(Vector4) == fieldType)
            {
                var fValue = mat.GetVector(fieldName);
                field.SetValue(this, fValue);
            }
            else if (typeof(Texture) == fieldType)
            {
                var fValue = mat.GetTexture(fieldName);
                field.SetValue(this, fValue);
            }
            else if(typeof(Matrix4x4) == fieldType)
            {
                if (mat.HasProperty(fieldName))
                {
                    var fValue = mat.GetMatrix(fieldName);
                    field.SetValue(this, fValue);
                }
                else
                {
                    var fValue = Shader.GetGlobalMatrix(fieldName);
                    field.SetValue(this, fValue);
                }
            }
        }
    }
}
public class VxShadowmap : MonoBehaviour
{
    public VxShadowmapUniformData[] vxShadowmapUniformDatas;
    public VxShadowmapUniformData vxShadowmapUniformData;

    [ContextMenu("LoadUniformData")]
    public void LoadUniformData()
    {
        vxShadowmapUniformData.LoadUniformData();
    }

#if UNITY_EDITOR
    [ContextMenu("Test")]
    public void Test()
    {
        var vxShadowmapUniformData = new VxShadowmapUniformData()
        {
            SceneName = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name,
            SectionIndex = 0
        };

        //vxShadowmapUniformData.FillUniformInfoFromMaterial(litMaterial);
        //AssetDatabase.CreateAsset(vxShadowmapUniformData, parentPath + "/vxShadowmapUniformData.asset");


        GameObject go = gameObject;
        var vxShadowmapPrefab = go.GetComponent<VxShadowmap>();

        
        if (vxShadowmapUniformData != null)
        {
            //AssetDatabase.AddObjectToAsset(vxShadowmapUniformData, vxShadowmapPrefab.gameObject);
        }

        vxShadowmapPrefab.vxShadowmapUniformData = vxShadowmapUniformData;
        PrefabUtility.SaveAsPrefabAsset(vxShadowmapPrefab.gameObject, "Assets/vxShadowmap.prefab");
    }
#endif

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
