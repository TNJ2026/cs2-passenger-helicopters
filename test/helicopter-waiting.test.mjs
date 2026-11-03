import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('waiting setback follows each approach and rejects invalid or short geometry',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cityweaver-waiting-'));
 try{
 const sources=['HelicopterWaitingRules','ImportedHeliportRules'].map(n=>fileURLToPath(new URL(`../Runtime/Transport/${n}.cs`,import.meta.url)));
 await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(s=>`<Compile Include="${s}"/>`).join('')}</ItemGroup></Project>`);
 await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using CityWeaver;
class Program{
 static void Check(bool b){if(!b)throw new Exception("Waiting geometry invariant failed");}
 static double D((float x,float z) a,(float x,float z) b)=>Math.Sqrt(Math.Pow(a.x-b.x,2)+Math.Pow(a.z-b.z,2));
 static (float x,float z) P(float x,float z){var p=ImportedHeliportRules.AlignLegacyPad(x,z);return ImportedHeliportRules.Position(p.x,p.z);}
 static void Main(){
 foreach(var pair in new[]{(pad:P(-3,26),entry:P(20,26)),(pad:P(-32,26),entry:P(-32,48))}){
  var p=pair.pad;var e=pair.entry;var w=HelicopterWaitingRules.Edge(p.x,p.z,e.x,e.z,14);
  Check(Math.Abs(D(p,w)-14)<.001);Check(Math.Abs(D(p,w)+D(w,e)-D(p,e))<.001);
  // Mirroring/rotating the asset keeps the waiting point on its pedestrian approach.
  var rotated=HelicopterWaitingRules.Edge(-p.z,p.x,-e.z,e.x,14);
  Check(D(rotated,(-w.z,w.x))<.001);
  // A width-only bake must commute with waiting-point placement: Z stays unchanged.
  float width=104f/102f;
  var scaled=HelicopterWaitingRules.WidthEdge(p.x*width,p.z,e.x*width,e.z,14,width);
  Check(D(scaled,(w.x*width,w.z))<.001);
 }
 var roof=HelicopterWaitingRules.Edge(1.331073f,-2.510134f,1.331073f,9.489866f,10);
 Check(D(roof,(1.331073f,7.489866f))<.001);
 foreach(float r in new[]{float.NaN,float.PositiveInfinity,-1f,0f,20f}){
  bool rejected=false;try{HelicopterWaitingRules.Edge(0,0,20,0,r);}catch(ArgumentException){rejected=true;}Check(rejected);
 }
 bool invalid=false;try{HelicopterWaitingRules.Edge(float.NaN,0,20,0,14);}catch(ArgumentException){invalid=true;}Check(invalid);
 Console.WriteLine("Waiting geometry passed");
 }
}
`);
 assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Waiting geometry passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
