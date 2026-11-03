using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using CityWeaver.HelicopterModels;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

public static class RotorShaderChecks
{
    public static void CheckNativeSkin()
    {
        var mesh=new Mesh{name="Native rigid skin fixture"};
        mesh.SetVertexBufferParams(9,new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,1),
            new VertexAttributeDescriptor(VertexAttribute.BlendIndices,VertexAttributeFormat.UInt32,1,2));
        var positions=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,0,1),new Vector3(10,0,0),new Vector3(12,0,0),new Vector3(10,0,2),new Vector3(20,0,0),new Vector3(21,0,0),new Vector3(20,0,1)};
        mesh.SetVertexBufferData(positions,0,0,9,0);mesh.SetVertexBufferData(new Vector2[9],0,0,9,1);mesh.SetVertexBufferData(new uint[]{0,0,0,1,1,1,2,2,2},0,0,9,2);
        mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,1,2,3,4,5},0);mesh.SetTriangles(new[]{6,7,8},1);
        var binds=new[]{Matrix4x4.identity,Matrix4x4.Translate(new Vector3(-10,0,0)),Matrix4x4.Translate(new Vector3(-20,0,0))};
        try
        {
            using(var main=HelicopterRotorExposureMesh.Build(new[]{mesh},binds,1))
            using(var tail=HelicopterRotorExposureMesh.Build(new[]{mesh},binds,2))
            {
                if(main.Slices.Count!=1||tail.Slices.Count!=1||main.Slices[0].Surface!=0||tail.Slices[0].Surface!=1)throw new Exception("Body/rotor material separation failed");
                if(main.Vertices!=3*(HelicopterRotorExposureRules.Samples+1)||main.Triangles!=HelicopterRotorExposureRules.Samples+1||Math.Abs(main.Slices[0].Radius-2)>.0001f||Math.Abs(tail.Slices[0].Radius-1)>.0001f)throw new Exception("Native index stream or authoritative bind-pose conversion failed");
                if(main.Slices[0].Mesh.subMeshCount!=2||main.Slices[0].Mesh.GetIndexCount(0)!=3*(HelicopterRotorExposureRules.Samples+1)||main.Slices[0].Mesh.GetIndexCount(1)!=3||tail.Slices[0].Mesh.GetIndexCount(1)!=3)throw new Exception("Clear pass must contain exactly the current real triangle, preserving all shutter samples");
            }
            mesh.SetTriangles(new[]{0,3,4},0);bool rejected=false;
            try{using(var bad=HelicopterRotorExposureMesh.Build(new[]{mesh},binds,1)){} }catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("A body/rotor crossing triangle was accepted");
            Debug.Log("ROTOR_NATIVE_SKIN_CHECK_OK");
        }
        finally{UnityEngine.Object.DestroyImmediate(mesh);}
    }
    // GPU preflight of the same shader and mesh builder, using original blade
    // OBJ groups. This is an engineering preview, never substituted for game QA.
    public static void Preview()
    {
        CheckNativeSkin();
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
        CheckCornerWelding();
        var originals=RotorNativeImportFixture.Load(root);var binds=RotorNativeImportFixture.Binds();
        var exposure=HelicopterRotorExposureMesh.Build(originals,binds,1);
        var tail=HelicopterRotorExposureMesh.Build(originals,binds,2);
        if(exposure.Slices.Count!=1||tail.Slices.Count!=1||exposure.Slices[0].Surface!=0||tail.Slices[0].Surface!=0||
            exposure.Triangles!=4242*(HelicopterRotorExposureRules.Samples+1)||tail.Triangles!=2274*(HelicopterRotorExposureRules.Samples+1))throw new Exception("Native full-material rotor topology/material mismatch");
        if(exposure.Slices[0].Mesh.GetIndexCount(1)!=4242*3||tail.Slices[0].Mesh.GetIndexCount(1)!=2274*3)throw new Exception("Native clear-only pass contains extra shutter geometry");
        Debug.Log("ROTOR_NATIVE_WELD_OK main vertices="+exposure.Vertices+", main triangles="+exposure.Triangles+", tail vertices="+tail.Vertices+", tail triangles="+tail.Triangles);
        var bundle=AssetBundle.LoadFromFile(Path.Combine(root,"artifacts/rotor-shader-build/rotor-angular-windows"));
        var shader=bundle!=null?bundle.LoadAsset<Shader>("Assets/RotorAngular.shader"):null;
        if(shader==null||!shader.isSupported)throw new Exception("Built Windows shader bundle cannot be loaded/rendered");
        var material=new Material(shader);var image=new Texture2D(1,1);
        image.LoadImage(File.ReadAllBytes(Path.Combine(root,"assets_game_import/mi26-rig-v6-small/CWMilMi26tcPart01_BaseColorMap.png")));
        material.SetTexture("_BaseColorMap",image);material.SetTexture("_ControlMask",Texture2D.blackTexture);
        var target=new RenderTexture(1024,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();
        var cmd=new CommandBuffer{name="Rotor shader GPU preflight"};cmd.SetRenderTarget(target);cmd.ClearRenderTarget(true,true,new Color(.2f,.22f,.25f,1));
        var camera=new GameObject("Offscreen preflight camera").AddComponent<Camera>();camera.transform.SetPositionAndRotation(new Vector3(0,35,-6),Quaternion.LookRotation(new Vector3(0,-35,6)));camera.orthographic=true;camera.orthographicSize=19;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=100;
        var clip=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;
        for(int i=0;i<2;i++)
        {
            cmd.SetViewport(new Rect(i*512,0,512,512));var block=new MaterialPropertyBlock();block.SetMatrix("_RotorLocalToClip",clip);block.SetColor("_RotorTint",Color.white);block.SetFloat("_RotorAmount",i);block.SetFloat("_RotorSweep",HelicopterRotorExposureRules.Sweep(62.8319f,false));block.SetFloat("_RotorRadius",exposure.Slices[0].Radius);
            foreach(var slice in exposure.Slices)cmd.DrawMesh(slice.Mesh,Matrix4x4.identity,material,HelicopterRotorExposureRules.DrawSubmesh(i),0,block);
        }
        Graphics.ExecuteCommandBuffer(cmd);var previous=RenderTexture.active;RenderTexture.active=target;var output=new Texture2D(1024,512,TextureFormat.RGB24,false);output.ReadPixels(new Rect(0,0,1024,512),0,0);output.Apply();RenderTexture.active=previous;
        string destination=Path.Combine(root,"artifacts/rotor-angular-gpu-preflight.png");File.WriteAllBytes(destination,output.EncodeToPNG());
        int changed=0;var pixels=output.GetPixels32();for(int y=0;y<512;y++)for(int x=0;x<512;x++){int a=y*1024+x,b=a+512;if(Math.Abs(pixels[a].r-pixels[b].r)+Math.Abs(pixels[a].g-pixels[b].g)+Math.Abs(pixels[a].b-pixels[b].b)>12)changed++;}
        if(changed<1000)throw new Exception("GPU angular effect did not alter at least 1000 pixels: "+changed);
        Debug.Log("ROTOR_GPU_PREFLIGHT_OK changed pixels="+changed+", real blade triangles=4242, samples="+HelicopterRotorExposureRules.Samples+", output="+destination);
        cmd.Dispose();exposure.Dispose();tail.Dispose();target.Release();UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(target);foreach(var original in originals)UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(output);bundle.Unload(true);
    }
    private static void CheckCornerWelding()
    {
        var mesh=new Mesh();mesh.vertices=new[]{new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(0,0,2),new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(0,0,2)};
        var uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.zero,Vector2.right,Vector2.up};mesh.uv=uv;mesh.triangles=new[]{0,1,2,3,4,5};
        var weights=new BoneWeight[6];for(int i=0;i<6;i++)weights[i]=new BoneWeight{boneIndex0=1,weight0=1};mesh.boneWeights=weights;
        try
        {
            var binds=new[]{Matrix4x4.identity,Matrix4x4.identity,Matrix4x4.identity};int copies=HelicopterRotorExposureRules.Samples+1;
            using(var result=HelicopterRotorExposureMesh.Build(new[]{mesh},binds,1))if(result.Vertices!=3*copies||result.Triangles!=2*copies)throw new Exception("Unrolled corners were not welded");
            uv[3]=new Vector2(.25f,0);mesh.uv=uv;
            using(var result=HelicopterRotorExposureMesh.Build(new[]{mesh},binds,1))if(result.Vertices!=4*copies)throw new Exception("Welding destroyed a UV seam");
        }
        finally{UnityEngine.Object.DestroyImmediate(mesh);}
    }
}
