import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('helipad walking route follows surveyed stairs and terminates above the deck',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cityweaver-stairs-'));
 try{
 const source=fileURLToPath(new URL('../Runtime/Transport/HelipadPedestrianRules.cs',import.meta.url));
 await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
 await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.Linq;using CityWeaver;
class Program{static void Main(){
 // The road is outward (+Z). Native connection validation rejects dot < cos45.
 foreach(float padX in new[]{-12f,0f,13f})foreach(float front in new[]{16f,20f,24f}){
  var t=HelipadPedestrianRules.EntryTurn(padX,front);
  double dx=HelipadPedestrianRules.EntryX(padX)-t.x,dz=front-t.z,dot=dz/Math.Sqrt(dx*dx+dz*dz);
  if(dot<.70710677||t.y!=0||dz<1)throw new Exception("road frontage connection angle");
  var foot=HelipadPedestrianRules.Point(0,padX,5.01f,-2.510134f);
  if(Math.Abs(t.x-foot.x)>.0001||Math.Abs(HelipadPedestrianRules.EntryX(padX)-foot.x)>.0001)
   throw new Exception("stair exit doubles back toward the old central entrance");
  if(t.z<=foot.z||front<=t.z)throw new Exception("exit must continue toward the street");
 }
 var pts=Enumerable.Range(0,HelipadPedestrianRules.Source.Length).Select(i=>HelipadPedestrianRules.Point(i,1.331073f,5.01f,-2.510134f)).ToArray();
 for(int i=0;i<pts.Length;i++){
  var shifted=HelipadPedestrianRules.Point(i,101.331073f,15.01f,197.489866f);
  if(Math.Abs(shifted.x-pts[i].x-100)>.0001||Math.Abs(shifted.y-pts[i].y-10)>.0001||Math.Abs(shifted.z-pts[i].z-200)>.0001)throw new Exception("translation");
 }
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(pts.Select(p=>new[]{p.x,p.y,-p.z})));
}}
`);
 const points=JSON.parse(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}).trim().split(/\r?\n/).at(-1));
 // Read actual imported source faces, not a second copy of the implementation.
 const triangles=[];
 for(let part=1;part<=13;part++){
  const file=new URL(`../assets_game_import/pbr-v2-uv/CWHelipadPBRSection${part<=8?1:2}/CWHelipadPart${String(part).padStart(2,'0')}.obj`,import.meta.url);
  const lines=(await readFile(file,'utf8')).split(/\r?\n/);
  const vertices=lines.filter(l=>l.startsWith('v ')).map(l=>l.trim().split(/\s+/).slice(1).map(Number));
  for(const line of lines.filter(l=>l.startsWith('f ')))triangles.push(line.trim().split(/\s+/).slice(1).map(v=>vertices[Number(v.split('/')[0])-1]));
 }
 function surface(x,z){let height=-Infinity;
  for(const [a,b,c] of triangles){
   const d=(b[2]-c[2])*(a[0]-c[0])+(c[0]-b[0])*(a[2]-c[2]);if(Math.abs(d)<1e-6)continue;
   const u=((b[2]-c[2])*(x-c[0])+(c[0]-b[0])*(z-c[2]))/d;
   const v=((c[2]-a[2])*(x-c[0])+(a[0]-c[0])*(z-c[2]))/d;
   if(u>=0&&v>=0&&u+v<=1)height=Math.max(height,u*a[1]+v*b[1]+(1-u-v)*c[1]);
  }return height;
 }
 for(let i=0;i<points.length-1;i++)for(let t=0;t<=1;t+=.1){
  const p=points[i].map((v,j)=>v+(points[i+1][j]-v)*t),y=surface(p[0],p[2]);
  assert.ok(Number.isFinite(y),`missing walking surface ${p}`);
  assert.ok(p[1]>=y-.16&&p[1]<=y+.22,`walk surface clearance ${p[1]-y} at ${p}`);
 }
 const last=points.at(-1);assert.ok(Math.hypot(last[0]-1.331073,last[2]-2.510134)>8);
 assert.ok(last[1]>surface(last[0],last[2]));
 }finally{await rm(dir,{recursive:true,force:true});}
});
