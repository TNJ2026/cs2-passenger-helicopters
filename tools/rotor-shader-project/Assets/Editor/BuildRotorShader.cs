using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildRotorShader
{
    public static void BuildAndPreview(){Build();RotorShaderChecks.Preview();}
    public static void Build()
    {
        RotorShaderChecks.CheckNativeSkin();
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RotorAngular.shader");
        if(shader==null)throw new Exception("Rotor shader asset was not imported");
        var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity==ShaderCompilerMessageSeverity.Error).ToArray();
        if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message)));
        string output=Environment.GetEnvironmentVariable("HELICOPTER_SHADER_OUTPUT");
        if(string.IsNullOrWhiteSpace(output))throw new Exception("HELICOPTER_SHADER_OUTPUT missing");
        Directory.CreateDirectory(output);
        var result=BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName="rotor-angular-windows",assetNames=new[]{"Assets/RotorAngular.shader"}}},BuildAssetBundleOptions.ForceRebuildAssetBundle|BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64);
        if(result==null)throw new Exception("Rotor shader bundle build failed");
        Debug.Log("ROTOR_SHADER_BUILD_OK "+Path.Combine(output,"rotor-angular-windows"));
    }
}
