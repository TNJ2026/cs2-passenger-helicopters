import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('both heliport pads have direct independent takeoffs, depot access and idempotent legacy migration',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cityweaver-taxiway-'));
 try{
 const source=fileURLToPath(new URL('../Runtime/Transport/ImportedHeliportRules.cs',import.meta.url));
 await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
 await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.Collections.Generic;using System.Linq;using CityWeaver;
class Program{
 static void Check(bool b){if(!b)throw new Exception("Taxiway invariant failed");}
 static double Distance((float x,float z) a,(float x,float z) b)=>Math.Sqrt(Math.Pow(a.x-b.x,2)+Math.Pow(a.z-b.z,2));
 static double SegmentDistance((float x,float z) p,(float x,float z) a,(float x,float z) b){var dx=b.x-a.x;var dz=b.z-a.z;var t=Math.Clamp(((p.x-a.x)*dx+(p.z-a.z)*dz)/(dx*dx+dz*dz),0,1);return Distance(p,(a.x+t*dx,a.z+t*dz));}
 static void Main(){
  var points=Enumerable.Range(0,9).Select(ImportedHeliportRules.TaxiLocal).ToList();
  var first=ImportedHeliportRules.AlignLegacyPad(-3,26);first=ImportedHeliportRules.Position(first.x,first.z);
  var second=ImportedHeliportRules.AlignLegacyPad(-32,26);second=ImportedHeliportRules.Position(second.x,second.z);
  points.Add(first);points.Add(second);var edges=ImportedHeliportRules.TaxiEdges.ToList();
  Check(edges.Count==8);Check(Distance(points[0],points[1])==16);Check(Distance(points[4],points[5])==16);
  Check(SegmentDistance(first,points[0],points[1])<.001);Check(SegmentDistance(second,points[4],points[5])<.001);
  // Neither boarding branch crosses the other pad. The apron connector also clears it.
  Check(SegmentDistance(first,points[4],points[5])>20);Check(SegmentDistance(second,points[0],points[1])>20);
  Check(SegmentDistance(second,points[1],points[2])>12);
  edges.Remove((0,1));edges.Remove((4,5));edges.AddRange(new[]{(0,9),(9,1),(4,10),(10,5)});
  double[] Distances(int start,int blocked){var ds=Enumerable.Repeat(double.PositiveInfinity,11).ToArray();ds[start]=0;for(int pass=0;pass<11;pass++)foreach(var(a,b) in edges){if(a==blocked||b==blocked)continue;var length=Distance(points[a],points[b]);ds[a]=Math.Min(ds[a],ds[b]+length);ds[b]=Math.Min(ds[b],ds[a]+length);}return ds;}
  // Both reach a takeoff in 8 m and can reach the depot without traversing the other stop.
  var takeoffs=new[]{0,2,4,5};
  foreach(var pair in new[]{(pad:9,other:10),(pad:10,other:9)}){
   var full=Distances(pair.pad,-1);var isolated=Distances(pair.pad,pair.other);
   Check(Math.Abs(takeoffs.Min(n=>full[n])-8)<.001);
   Check(Math.Abs(takeoffs.Min(n=>isolated[n])-8)<.001);
   Check(!double.IsInfinity(isolated[8]));Check(Math.Abs(full[8]-isolated[8])<.001);
  }
  int Legacy(int a,int d)=>ImportedHeliportRules.LegacyTaxiJunctionEnd(points[a].x,points[a].z,points[d].x,points[d].z);
  Check(Legacy(0,5)==1);Check(Legacy(5,0)==0);
  // Critical reload guard: old position 5 now belongs to the independent pad.
  // Matching that position alone would wrongly move it on every city reload.
  foreach(var(a,b) in ImportedHeliportRules.TaxiEdges){Check(Legacy(a,b)==-1);Check(Legacy(b,a)==-1);}
  Check(ImportedHeliportRules.LegacyTaxiJunctionEnd(float.NaN,0,points[5].x,points[5].z)==-1);
  Console.WriteLine("Independent taxiways passed");
 }
}
`);
 assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Independent taxiways passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
