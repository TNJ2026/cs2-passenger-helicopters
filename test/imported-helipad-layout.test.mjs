import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('standalone helipad meets road frontage and anchors elevated stops to the building',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-helipad-'));
 try {
  const source=fileURLToPath(new URL('../Runtime/Transport/ImportedHelipadRules.cs',import.meta.url));
  await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),`using System; using CityWeaver;
class Program {
 static void Check(bool x) { if(!x) throw new Exception("Helipad alignment invariant failed"); }
 static void Main() {
  // Independent native MeshData bounds read from the live imported asset.
  float min=-16.7364425659f,max=15.3806915283f;
  int depth=(int)Math.Ceiling((max-min)/8);
  float centered=-(min+max)/2;
  float shift=ImportedHelipadRules.FrontShift(max+centered,depth);
  Check(depth==5 && Math.Abs(shift-3.94143295f)<.001f);
  Check(Math.Abs(max+centered+shift-20)<.001f);
  Check(min+centered+shift>=-20);
  Check(ImportedHelipadRules.FrontShift(max+centered+shift,depth)==0);
  float roof=5.010139942f+0.11802578f;
  Check(roof>5.12f && roof<5.14f && ImportedHelipadRules.Parent(roof)==0);
  Check(ImportedHelipadRules.Parent(0)==-1);
  Check(ImportedHelipadRules.PathZ(20,20,shift)==20); // roadside endpoint stays fixed
  Check(ImportedHelipadRules.LegacyPathZ(20,20,max-min)==20);
  float pad=-2.510134f+centered;
  Check(Math.Abs(ImportedHelipadRules.PathZ(pad,20,shift)-(pad+shift))<.001f);
  Check(Math.Abs(ImportedHelipadRules.LegacyPathZ(pad+shift,20,max-min)-pad)<.001f);
  // A newly registered aligned prefab has zero shift, but old saved taxiway
  // endpoints still need the original centered-position mapping after reload.
  foreach(float z in new[]{pad-7,pad,pad+7,pad+12}) {
   float aligned=z+shift;
   Check(ImportedHelipadRules.FrontShift(20,5)==0);
   Check(Math.Abs(ImportedHelipadRules.LegacyPathZ(aligned,20,max-min)-z)<.001f);
  }
  Check(Math.Abs((pad+7+shift)-(pad-7+shift)-14)<.001f);
  foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,40f,-40f}) {
   bool rejected=false;try{ImportedHelipadRules.FrontShift(invalid,5);}catch(InvalidOperationException){rejected=true;} Check(rejected);
  }
  Console.WriteLine("Helipad rules passed");
 }
}`);
  assert.match(execFileSync(process.env.CITYWEAVER_TEST_DOTNET||'dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Helipad rules passed/);
 } finally {await rm(dir,{recursive:true,force:true});}
});
