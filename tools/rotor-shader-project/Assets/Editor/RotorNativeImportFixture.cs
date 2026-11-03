using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Reproduce the published native importer: six material meshes, unrolled
// triangle corners, Float16 UVs, interleaved Position/UInt32 rigid bone indices.
// Counts are checked against the actual shipped GeometryAsset headers.
public static class RotorNativeImportFixture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SkinVertex {internal Vector3 Position;internal uint Bone;}
    public static Matrix4x4[] Binds()=>new[]{Matrix4x4.identity,
        Matrix4x4.TRS(new Vector3(.0166864f,5.91186f,.98524666f),Quaternion.Euler(3.889433f,0,.000009155f),Vector3.one).inverse,
        Matrix4x4.TRS(new Vector3(1.1801733f,6.3594667f,-13.2777333f),Quaternion.Euler(0,0,-90),Vector3.one).inverse};
    public static Mesh[] Load(string root)
    {
        var meshes=new List<Mesh>();
        try
        {
            for(int part=1;part<=6;part++)
            {
                var positions=new List<Vector3>();var uv=new List<Vector2>();var corners=new List<SkinVertex>();var packedUv=new List<uint>();uint bone=0;
                string file=Path.Combine(root,"assets_game_import/mi26-rig-v6-small/CWMilMi26tcPart"+part.ToString("D2")+".obj");
                foreach(var line in File.ReadLines(file))
                {
                    var p=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);if(p.Length==0)continue;
                    if(p[0]=="v")positions.Add(new Vector3(F(p[1]),F(p[2]),-F(p[3])));
                    else if(p[0]=="vt")uv.Add(new Vector2(F(p[1]),F(p[2])));
                    else if(p[0]=="g")bone=p.Length==2&&p[1]=="rotor_top"?1u:p.Length==2&&p[1]=="rotor_bck"?2u:0u;
                    else if(p[0]=="f")for(int i=2;i<p.Length-1;i++)foreach(int corner in new[]{1,i+1,i})
                    {
                        var ids=p[corner].Split('/');var t=uv[I(ids[1],uv.Count)];
                        corners.Add(new SkinVertex{Position=positions[I(ids[0],positions.Count)],Bone=bone});
                        packedUv.Add((uint)Mathf.FloatToHalf(t.x)|((uint)Mathf.FloatToHalf(t.y)<<16));
                    }
                }
                var mesh=new Mesh{name="Native unrolled Mi26 material "+(part-1),indexFormat=IndexFormat.UInt32};meshes.Add(mesh);
                mesh.SetVertexBufferParams(corners.Count,new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
                    new VertexAttributeDescriptor(VertexAttribute.BlendIndices,VertexAttributeFormat.UInt32,1,0),new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float16,2,1));
                mesh.SetVertexBufferData(corners.ToArray(),0,0,corners.Count,0);mesh.SetVertexBufferData(packedUv.ToArray(),0,0,packedUv.Count,1);
                int[] indices=new int[corners.Count];for(int i=0;i<indices.Length;i++)indices[i]=i;mesh.triangles=indices;mesh.RecalculateBounds();
            }
            string geometry=Path.Combine(root,"assets/native/ImportedData/CityWeaver_4a7fc21e03ce4e449bc02ae964c6ffe2/CWMilMi26tcPart01_9409d9f859de09b961b3ccac52bfc92d.Geometry");
            using(var reader=new BinaryReader(File.OpenRead(geometry)))
            {
                if(reader.ReadUInt16()!=1||reader.ReadInt32()!=6)throw new Exception("Shipped geometry fixture header changed");reader.ReadUInt32();
                for(int i=0;i<6;i++)
                {
                    reader.BaseStream.Position+=76;int vertices=reader.ReadInt32(),indices=reader.ReadInt32(),submeshes=reader.ReadInt32();
                    if(vertices!=meshes[i].vertexCount||indices!=meshes[i].GetIndexCount(0)||submeshes!=1)throw new Exception("Regenerated native material layout does not match shipped GeometryAsset "+i);
                }
            }
            Debug.Log("ROTOR_NATIVE_FULL_LAYOUT_OK materials=6, vertices="+string.Join(",",meshes.ConvertAll(m=>m.vertexCount.ToString()).ToArray()));
            return meshes.ToArray();
        }
        catch{foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);throw;}
    }
    private static float F(string value)=>float.Parse(value,CultureInfo.InvariantCulture);
    private static int I(string value,int length){int i=int.Parse(value,CultureInfo.InvariantCulture);return i>0?i-1:length+i;}
}
