using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class TextureAssetCompress : MonoBehaviour
{
    [MenuItem("Tools/CompressTexture")]
    public static void CompressTextureETC2()
    {
        var tex = (Selection.activeObject as Texture2D);
        System.IO.File.WriteAllBytes(UnityEditor.EditorUtility.SaveFilePanel("save", "", "tex", "png") , tex.EncodeToPNG());
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
