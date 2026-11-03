import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
test('ground departure applies only to a fresh local dispatch and hangar exit preserves apron nodes',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-departure-'));
 try{
 const sources=['HelicopterDepartureRules','ImportedHeliportRules'].map(n=>fileURLToPath(new URL(`../Runtime/Transport/${n}.cs`,import.meta.url)));
 await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(s=>`<Compile Include="${s}"/>`).join('')}</ItemGroup></Project>`);
 await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.Linq;using CityWeaver;
class Program{
 static void Check(bool b){if(!b)throw new Exception("Departure invariant failed");}
 static void Main(){
  Check(HelicopterDepartureRules.Eligible(true,true,false,false,false,false,0));
  for(int mask=0;mask<64;mask++){
   bool local=(mask&1)!=0,enRoute=(mask&2)!=0,boarding=(mask&4)!=0,returning=(mask&8)!=0,source=(mask&16)!=0,flying=(mask&32)!=0;
   Check(HelicopterDepartureRules.Eligible(local,enRoute,boarding,returning,source,flying,0)==(mask==3));
  }
  foreach(var distance in new[]{-1f,3f,100f,float.NaN,float.PositiveInfinity})Check(!HelicopterDepartureRules.Eligible(true,true,false,false,false,false,distance));
  Check(HelicopterDepartureRules.TaxiSpeed==3);
  Check(ImportedHeliportRules.TaxiNode(3)==(-32f,4f));
  Check(ImportedHeliportRules.TaxiNode(6)==(-32f,-8f));
  Check(ImportedHeliportRules.TaxiNode(8)==(ImportedHeliportRules.DepotX,ImportedHeliportRules.DepotZ));
  Check(ImportedHeliportRules.TaxiEdges.Contains((7,8)));
  Check(ImportedHeliportRules.TaxiEdges.Count(e=>e.a==8||e.b==8)==1);
  // Measured tall-hangar door opening, not the annex to its left.
  Check(Math.Abs(ImportedHeliportRules.DepotX-(-20.312f+11.688f)/2)<.001);
  Check(ImportedHeliportRules.TaxiNode(7).x==ImportedHeliportRules.DepotX);
  Check(ImportedHeliportRules.TaxiNode(7).z>10);
  Check(ImportedHeliportRules.DepotZ<0);
  Console.WriteLine("Departure rules passed");
 }
}`);
 assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Departure rules passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
