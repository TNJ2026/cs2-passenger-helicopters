using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityWeaver.HelicopterModels
{
    internal sealed class HelicopterRotorExposureMesh:IDisposable
    {
        private struct VertexKey:IEquatable<VertexKey>
        {
            internal Vector3 Position;internal Vector2 UV;
            public bool Equals(VertexKey other)=>Position.Equals(other.Position)&&UV.Equals(other.UV);
            public override bool Equals(object other)=>other is VertexKey value&&Equals(value);
            public override int GetHashCode(){unchecked{return Position.GetHashCode()*397^UV.GetHashCode();}}
        }
        internal sealed class Slice {internal Mesh Mesh;internal int Surface;internal float Radius;}
        internal readonly List<Slice> Slices=new List<Slice>();
        internal int Vertices,Triangles;
        internal static HelicopterRotorExposureMesh Build(Mesh[] sources,Matrix4x4[] binds,int bone)
        {
            var result=new HelicopterRotorExposureMesh();int surface=0;
            try
            {
                foreach(var source in sources)
                {
                    var vertices=source.vertices;var uv=source.uv;var bones=RigidBones(source);
                    if(vertices.Length>2000000||bones.Length!=vertices.Length||uv.Length!=vertices.Length||bone>=binds.Length)
                        throw new InvalidOperationException("Mi-26 rotor requires readable rigid skin, UVs and bind poses");
                    for(int sub=0;sub<source.subMeshCount;sub++,surface++)
                    {
                        var input=source.GetTriangles(sub);var selected=new List<int>();
                        for(int i=0;i<input.Length;i+=3)
                        {
                            bool a=bones[input[i]]==bone,b=bones[input[i+1]]==bone,c=bones[input[i+2]]==bone;
                            if(a&&b&&c){selected.Add(input[i]);selected.Add(input[i+1]);selected.Add(input[i+2]);}
                            else if(a||b||c)throw new InvalidOperationException("Rotor triangles cross a rigid bone boundary");
                        }
                        if(selected.Count==0)continue;
                        // Native import unrolls every triangle corner. Weld only
                        // identical position+UV inside this bone/material; normals
                        // are unused by the shutter shader, UV seams stay separate.
                        var map=new Dictionary<VertexKey,int>();var local=new List<Vector3>();var tex=new List<Vector2>();var indices=new List<int>();float radius=0;
                        foreach(int index in selected)
                        {
                            var p=binds[bone].MultiplyPoint3x4(vertices[index]);var t=uv[index];
                            if(!HelicopterRotorExposureRules.Finite(p.x)||!HelicopterRotorExposureRules.Finite(p.y)||!HelicopterRotorExposureRules.Finite(p.z)||!HelicopterRotorExposureRules.Finite(t.x)||!HelicopterRotorExposureRules.Finite(t.y))throw new InvalidOperationException("Nonfinite rotor vertex/UV");
                            var key=new VertexKey{Position=p,UV=t};
                            if(!map.TryGetValue(key,out int mapped))
                            {
                                mapped=local.Count;map.Add(key,mapped);
                                local.Add(p);tex.Add(t);radius=Math.Max(radius,new Vector2(p.x,p.z).magnitude);
                            }
                            indices.Add(mapped);
                        }
                        int copies=HelicopterRotorExposureRules.Samples+1;
                        if((long)local.Count*copies+result.Vertices>250000||(long)indices.Count*copies/3+result.Triangles>250000)
                            throw new InvalidOperationException("Mi-26 angular samples exceed geometry budget: bone="+bone+", material="+surface+", corners="+selected.Count+", unique vertices="+local.Count+", copies="+copies+", prior vertices="+result.Vertices+", prior triangles="+result.Triangles);
                        var positions=new Vector3[local.Count*copies];var uvs=new Vector2[positions.Length];var shutters=new List<Vector3>(positions.Length);var triangles=new int[indices.Count*copies];
                        // Draw older shutter positions first; current blade last.
                        for(int copy=0;copy<copies;copy++)
                        {
                            bool lead=copy==copies-1;int sample=HelicopterRotorExposureRules.Samples-1-copy;
                            var shutter=lead?Vector3.zero:new Vector3((sample+.5f)/HelicopterRotorExposureRules.Samples,1,HelicopterRotorExposureRules.Weight(sample,HelicopterRotorExposureRules.Samples));
                            for(int v=0;v<local.Count;v++){int dst=copy*local.Count+v;positions[dst]=local[v];uvs[dst]=tex[v];shutters.Add(shutter);}
                            for(int t=0;t<indices.Count;t++)triangles[copy*indices.Count+t]=copy*local.Count+indices[t];
                        }
                        var mesh=new Mesh{name="Mi26 real-blade angular samples",indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};
                        mesh.vertices=positions;mesh.uv=uvs;mesh.SetUVs(1,shutters);mesh.subMeshCount=2;mesh.SetTriangles(triangles,0);
                        // The final copy is the current unblurred blade. Select
                        // only these indices to bypass native motion blur during
                        // maneuvers without drawing all 48 zero-opacity samples.
                        var clear=new int[indices.Count];Array.Copy(triangles,triangles.Length-clear.Length,clear,0,clear.Length);mesh.SetTriangles(clear,1);
                        mesh.bounds=new Bounds(Vector3.zero,new Vector3(radius*2+1,Math.Max(2,source.bounds.size.magnitude),radius*2+1));
                        mesh.UploadMeshData(true);
                        result.Slices.Add(new Slice{Mesh=mesh,Surface=surface,Radius=radius});result.Vertices+=positions.Length;result.Triangles+=triangles.Length/3;
                    }
                }
                if(result.Slices.Count==0)throw new InvalidOperationException("No real rotor triangles found for bone "+bone);
                return result;
            }
            catch {result.Dispose();throw;}
        }
        private static int[] RigidBones(Mesh mesh)
        {
            var result=new int[mesh.vertexCount];
            // Native GeometryAsset drops Unity bindposes/weights. Its rigid skin
            // stores one UInt32 bone index, with the bind table on the prefab.
            var attribute=VertexAttribute.BlendIndices;
            if(mesh.HasVertexAttribute(attribute)&&mesh.GetVertexAttributeFormat(attribute)==VertexAttributeFormat.UInt32&&
                mesh.GetVertexAttributeDimension(attribute)==1&&!mesh.HasVertexAttribute(VertexAttribute.BlendWeight))
            {
                int stream=mesh.GetVertexAttributeStream(attribute),offset=mesh.GetVertexAttributeOffset(attribute),stride=mesh.GetVertexBufferStride(stream);
                using(var data=Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    var bytes=data[0].GetVertexData<byte>(stream);
                    for(int i=0;i<result.Length;i++)
                    {
                        int p=i*stride+offset;if(p<0||p+3>=bytes.Length)throw new InvalidOperationException("Truncated rigid skin stream");
                        uint index=(uint)(bytes[p]|bytes[p+1]<<8|bytes[p+2]<<16|bytes[p+3]<<24);
                        if(index>2)throw new InvalidOperationException("Unexpected Mi-26 rigid bone index");result[i]=(int)index;
                    }
                }
                return result;
            }
            var weights=mesh.boneWeights;
            if(weights.Length!=result.Length)throw new InvalidOperationException("Rigid skin stream missing");
            for(int i=0;i<weights.Length;i++)
            {
                var w=weights[i];if(w.weight0<.999f||w.weight1>.001f||w.weight2>.001f||w.weight3>.001f||w.boneIndex0>2)throw new InvalidOperationException("Non-rigid rotor skin");
                result[i]=w.boneIndex0;
            }
            return result;
        }
        public void Dispose(){foreach(var s in Slices)if(s.Mesh!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(s.Mesh);else UnityEngine.Object.DestroyImmediate(s.Mesh);}Slices.Clear();}
    }
}
