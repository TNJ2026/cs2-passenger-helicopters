import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm,readFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('imported heliport fits its lot, faces the road and keeps boarding separate from depot',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cityweaver-heliport-layout-'));
 try{
  const source=fileURLToPath(new URL('../Runtime/Transport/ImportedHeliportRules.cs',import.meta.url));
  const measured=JSON.parse(await readFile(new URL('../assets_game_import/heliport-matched-v10/validation.json',import.meta.url),'utf8'));
  await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),`using System;using CityWeaver;
class Program {
 static void Check(bool b){if(!b)throw new Exception("Layout invariant failed");}
 static void Main(){
  // Read back from native MeshData after OBJ import: Z is mirrored.
  Check(ImportedHeliportRules.Footprint(6,-50,-52,62,50));
  Check(ImportedHeliportRules.Footprint(6,-50,-62,62,50));
  Check(!ImportedHeliportRules.Footprint(6,-50,-60,62,50));
  Check(!ImportedHeliportRules.Footprint(6,-50,-72,62,50));
  Check(ImportedHeliportRules.LotWidth(-52)==14);
  Check(ImportedHeliportRules.LotWidth(-62)==16);
  foreach(var x in new[]{-50f,62f})foreach(var z in new[]{-50f,62f}){
   var p=ImportedHeliportRules.Position(x,z);Check(Math.Abs(p.x)<ImportedHeliportRules.LotWidth(-62)*4 && Math.Abs(p.z)<=14*4);
  }
  Check(!ImportedHeliportRules.Footprint(6,-50,-50,62,52));
  Check(!ImportedHeliportRules.Footprint(5,-50,-50,62,52));
  Check(!ImportedHeliportRules.Footprint(6,-62,-52,62,52));
  Check(!ImportedHeliportRules.Footprint(6,float.NaN,-50,62,52));
  Check(!ImportedHeliportRules.Footprint(6,-50,-50,float.PositiveInfinity,52));
  foreach(var x in new[]{-50f,62f})foreach(var z in new[]{-50f,52f}){
   var p=ImportedHeliportRules.Position(x,z);Check(Math.Abs(p.x)<14*4 && Math.Abs(p.z)<=14*4);
  }
  var entry=ImportedHeliportRules.Position(62,48);Check(entry.z==14*4 && Math.Abs(entry.x)<14*4);
  var stop=ImportedHeliportRules.Position(-3,26);var depot=ImportedHeliportRules.Position(-32,26);
  Check(stop.x==25 && stop.z==-9 && depot.x==25 && depot.z==-38);
  // Transform a native vertex with the actual -90 degree mesh rotation plus offset.
  // OBJ (x,z) becomes native (x,-z); local (-nativeZ-1,nativeX-6).
  foreach(var x in new[]{-50f,-3f,62f})foreach(var z in new[]{-50f,26f,52f}){
   var p=ImportedHeliportRules.Position(x,z);var nativeZ=-z;
   Check(p.x==-nativeZ-1 && p.z==x-6);
  }
  Check(Math.Abs(stop.z-depot.z)>=20 && stop.x==depot.x);
  // Dual-pad upgrade preserves pad one, converts the original depot pad to
  // boarding and separates depot spawning from BOTH boarding positions.
  var second=ImportedHeliportRules.Position(ImportedHeliportRules.SecondPadX,ImportedHeliportRules.SecondPadZ);
  var parking=ImportedHeliportRules.Position(ImportedHeliportRules.DepotX,ImportedHeliportRules.DepotZ);
  Check(second==depot && second!=stop && parking!=second && parking!=stop);
  foreach(var point in new[]{stop,second}){
   var dx=parking.x-point.x;var dz=parking.z-point.z;Check(dx*dx+dz*dz>=20*20);
  }
  Check(Math.Abs(parking.x)<14*4 && Math.Abs(parking.z)<14*4);
  // Compare operational targets against independently measured ring mesh bounds.
  var firstAligned=ImportedHeliportRules.AlignLegacyPad(-3,26);
  var secondAligned=ImportedHeliportRules.AlignLegacyPad(-32,26);
  Check(Math.Abs(firstAligned.x-(${measured.first_center[0]}))<.001 && Math.Abs(firstAligned.z-(${measured.first_center[2]}))<.001);
  Check(Math.Abs(secondAligned.x-(${measured.second_center[0]}))<.001 && Math.Abs(secondAligned.z-(${measured.second_center[2]}))<.001);
  Check(ImportedHeliportRules.AlignLegacyPad(firstAligned.x,firstAligned.z)==firstAligned);
  Check(ImportedHeliportRules.AlignLegacyPad(secondAligned.x,secondAligned.z)==secondAligned);
  Check(ImportedHeliportRules.AlignLegacyPad(62,48)==(62f,48f));
  Check(ImportedHeliportRules.AlignLegacyPad(-32,4)==(-32f,4f));
  // Rotating the source road-facing side (+X) must produce native front (+Z).
  var front=ImportedHeliportRules.Position(62,1);var back=ImportedHeliportRules.Position(-50,1);
  Check(front.z>back.z && front.x==back.x);
  Console.WriteLine("Layout passed");
 }}
`);
  assert.match(execFileSync(process.env.CITYWEAVER_TEST_DOTNET||'dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Layout passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
