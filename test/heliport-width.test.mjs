import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile,mkdtemp,writeFile,rm,readdir} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('width bake preserves faces/UVs and produces 104 x 112m referenced mesh bounds',async()=>{
 const entries=JSON.parse(await readFile(new URL('../assets_game_import/heliport-width104/preparation.json',import.meta.url),'utf8'));
 let minX=Infinity,maxX=-Infinity,minZ=Infinity,maxZ=-Infinity;
 for(const entry of entries){
  for(const name of (await readdir(entry.dest)).filter(n=>n.endsWith('.obj'))){
   const old=(await readFile(path.join(entry.source,name),'utf8')).split(/\r?\n/);
   const next=(await readFile(path.join(entry.dest,name),'utf8')).split(/\r?\n/);
   assert.deepEqual(next.filter(l=>/^(vt |f |g |usemtl )/.test(l)),old.filter(l=>/^(vt |f |g |usemtl )/.test(l)));
   const verts=next.filter(l=>l.startsWith('v ')).map(l=>l.split(/\s+/).slice(1).map(Number));
   const before=old.filter(l=>l.startsWith('v ')).map(l=>l.split(/\s+/).slice(1).map(Number));
   for(let i=0;i<verts.length;i++){assert.equal(verts[i][0],before[i][0]);assert.equal(verts[i][1],before[i][1]);assert.ok(Math.abs(verts[i][2]-before[i][2]*104/102)<1e-6);}
   for(const face of next.filter(l=>l.startsWith('f ')))for(const token of face.split(/\s+/).slice(1)){
    const [x,y,z]=verts[Number(token.split('/')[0])-1];const localX=z-104/102,localZ=x-6;
    minX=Math.min(minX,localX);maxX=Math.max(maxX,localX);minZ=Math.min(minZ,localZ);maxZ=Math.max(maxZ,localZ);
   }
  }
 }
 assert.ok(Math.abs(minX+52)<1e-5&&Math.abs(maxX-52)<1e-5);assert.equal(minZ,-56);assert.equal(maxZ,56);
});
test('width migration recognizes old/new layouts and folding door hinges track stretched geometry',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-airport-width-'));
 try{
  const sources=['HeliportWidthRules','ImportedHeliportRules','HelicopterHangarDoorRules'].map(n=>fileURLToPath(new URL(`../Runtime/Transport/${n}.cs`,import.meta.url)));
  await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(s=>`<Compile Include="${s}"/>`).join('')}</ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),`using System;using CityWeaver;
class Program{static void Check(bool b){if(!b)throw new Exception("Width invariant failed");}static void Main(){
 foreach(float x in new[]{25f,30.39467335f}){Check(HeliportWidthRules.LayoutState(x)==0);Check(HeliportWidthRules.LayoutState(x*HeliportWidthRules.Factor)==1);Check(Math.Abs(HeliportWidthRules.AuthoredZ((x)*HeliportWidthRules.Factor)-(x+1))<.0001f);}
 Check(HeliportWidthRules.LayoutState(float.NaN)==-1);Check(HeliportWidthRules.LayoutState(99)==-1);
 for(int panel=1;panel<=16;panel++)for(int frame=0;frame<=100;frame++){
  var old=HelicopterHangarDoorRules.Pose(panel,frame/100f);var next=HeliportWidthRules.DoorPose(panel,frame/100f);
  Check(Math.Abs(next.x-old.x)<.0001);Check(Math.Abs(next.z-old.z*HeliportWidthRules.Factor)<.0001);
  Check(Math.Abs(2*Math.Cos(next.angle)*next.length-2*Math.Cos(old.angle))<.0001);
  Check(Math.Abs(2*Math.Sin(next.angle)*next.length-2*Math.Sin(old.angle)*HeliportWidthRules.Factor)<.0001);
 }
 Console.WriteLine("Width passed");}}
`);
  assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Width passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
