import test from 'node:test';import assert from 'node:assert/strict';
import {readFile,readdir,mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';import path from 'node:path';import {fileURLToPath} from 'node:url';import {execFileSync} from 'node:child_process';
test('helipad referenced geometry fits 32x32m while retaining UVs and faces',async()=>{
 const report=JSON.parse(await readFile('assets_game_import/helipad-lot32/preparation.json','utf8'));
 const lo=[Infinity,Infinity,Infinity],hi=[-Infinity,-Infinity,-Infinity];
 for(const entry of report)for(const name of (await readdir(entry.dest)).filter(f=>f.endsWith('.obj'))){
  const original=(await readFile(path.join(entry.src,name),'utf8')).split(/\r?\n/),next=(await readFile(path.join(entry.dest,name),'utf8')).split(/\r?\n/);
  assert.deepEqual(next.filter(l=>/^(f |vt |g |usemtl )/.test(l)),original.filter(l=>/^(f |vt |g |usemtl )/.test(l)));
  const vertices=next.filter(l=>l.startsWith('v ')).map(l=>l.split(/\s+/).slice(1).map(Number));
  for(const f of next.filter(l=>l.startsWith('f ')))for(const s of f.split(/\s+/).slice(1)){
   const v=vertices[Number(s.split('/')[0])-1];for(let i=0;i<3;i++){lo[i]=Math.min(lo[i],v[i]);hi[i]=Math.max(hi[i],v[i]);}
  }
 }
 assert.ok(Math.abs(hi[0]-lo[0]-32)<1e-4);assert.ok(Math.abs(hi[2]-lo[2]-32)<1e-4);
 assert.ok(Math.abs(hi[1]-lo[1]-6.60169744)<1e-4);
});
test('lot migration preserves road frontage and stair path scale; new saves are recognized',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-helipad-lot-'));
 try{
  const sources=['HelipadLotRules','HelipadPedestrianRules'].map(n=>fileURLToPath(new URL(`../Runtime/Transport/${n}.cs`,import.meta.url)));
  await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(s=>`<Compile Include="${s}"/>`).join('')}</ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),`using System;using CityWeaver;
class Program{static void C(bool b){if(!b)throw new Exception("Lot invariant failed");}static void Main(){
 C(HelipadLotRules.Cells(32.00001f)==4);C(HelipadLotRules.Cells(32.01f)==5);
 C(HelipadLotRules.MapZ(20)+4==20);C(Math.Abs(HelipadLotRules.MapX(14.4773669f)-16)<.0001);
 C(Math.Abs(HelipadLotRules.MapZ(-12.1171341f)+16)<.0001);
 float x=-.25106172f,y=5.12802578f,z=2.10917447f;
 C(HelipadLotRules.Layout(z)==0);C(HelipadLotRules.Layout(HelipadLotRules.MapZ(z))==1);C(HelipadLotRules.Layout(99)==-1);
 for(int i=0;i<HelipadPedestrianRules.Source.Length;i++){
  var old=HelipadPedestrianRules.Point(i,x,y,z);
  var next=HelipadPedestrianRules.Point(i,HelipadLotRules.MapX(x),y,HelipadLotRules.MapZ(z),HelipadLotRules.Width,HelipadLotRules.Depth);
  C(Math.Abs(next.x-HelipadLotRules.MapX(old.x))<.0001);C(Math.Abs(next.z-HelipadLotRules.MapZ(old.z))<.0001);C(next.y==old.y);
 }
 var foot=HelipadPedestrianRules.Point(0,HelipadLotRules.MapX(x),y,HelipadLotRules.MapZ(z),HelipadLotRules.Width,HelipadLotRules.Depth);
 var turn=HelipadPedestrianRules.EntryTurn(HelipadLotRules.MapX(x),16,HelipadLotRules.Width);
 C(Math.Abs(foot.x-turn.x)<.0001);C(foot.z<turn.z&&turn.z<16);C(Math.Abs(turn.x)<16);
 Console.WriteLine("Lot passed");}}
`);
  assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Lot passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
