import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
test('folding leaves stay connected; dispatch waits for full opening and clearance delays closure',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-hangar-'));
 try{
 const sources=['HelicopterHangarDoorRules','ImportedHeliportRules'].map(n=>fileURLToPath(new URL(`../Runtime/Transport/${n}.cs`,import.meta.url)));
 await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(s=>`<Compile Include="${s}"/>`).join('')}</ItemGroup></Project>`);
 await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using CityWeaver;
class Program{
 static void Check(bool b){if(!b)throw new Exception("Hangar door invariant failed");}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.0001;
 static void Main(){
  float p=0;
  for(int frame=0;frame<240;frame++){
   Check(HelicopterHangarDoorRules.ShouldOpen(p,true,1000));
   if(frame<239)Check(HelicopterHangarDoorRules.HoldDeparture(p,-16));
   p=HelicopterHangarDoorRules.Step(p,true,1f/60);
  }
  Check(p>.999f&&!HelicopterHangarDoorRules.HoldDeparture(p,-16));
  Check(HelicopterHangarDoorRules.Step(.5f,true,0)==.5f);
  Check(HelicopterHangarDoorRules.ShouldOpen(p,false,179));
  Check(!HelicopterHangarDoorRules.ShouldOpen(p,false,180));
  Check(HelicopterHangarDoorRules.ShouldOpen(.5f,true,10000)); // another vehicle reopens a closing door
  Check(!HelicopterHangarDoorRules.ShouldOpen(0,false,0));
  Check(HelicopterHangarDoorRules.Step(p,false,8)==0);
  Check(HelicopterHangarDoorRules.Occupied(-4.312f,.2f,-16));
  Check(HelicopterHangarDoorRules.Occupied(-32,.2f,4));
  Check(!HelicopterHangarDoorRules.Occupied(-4.0815f,.2f,31.39f));
  Check(!HelicopterHangarDoorRules.Occupied(-33.0815f,.2f,31.39f));
  Check(!HelicopterHangarDoorRules.Occupied(-4,.2f,30));
  Check(!HelicopterHangarDoorRules.Occupied(-4,100,0));
  foreach(float progress in new[]{0f,.1f,.5f,.9f,1f}){
   for(int i=1;i<=16;i++){
    var a=HelicopterHangarDoorRules.Pose(i,progress);bool left=i<=8;
    if(progress==0)Check(Near(a.x,left?-20.312f+2*(i-1):11.688f-2*(16-i))&&Near(a.z,.27f)&&a.angle==0);
    if(i!=8&&i!=9){var b=HelicopterHangarDoorRules.Pose(left?i+1:i-1,progress);float sign=left?1:-1;
     Check(Near(a.x+sign*2*(float)Math.Cos(a.angle),b.x));
     Check(Near(a.z-sign*2*(float)Math.Sin(a.angle),b.z));
    }
   }
  }
  var l=HelicopterHangarDoorRules.Pose(8,1);var r=HelicopterHangarDoorRules.Pose(9,1);
  Check(r.x-l.x>29); // clear rotor passage between folded stacks
  bool failed=false;try{HelicopterHangarDoorRules.Pose(17,1);}catch(ArgumentOutOfRangeException){failed=true;}Check(failed);
  Console.WriteLine("Hangar rules passed");
 }
}`);
 assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Hangar rules passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
