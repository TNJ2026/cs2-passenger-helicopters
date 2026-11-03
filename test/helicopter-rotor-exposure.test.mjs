import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import fs from 'node:fs';
import crypto from 'node:crypto';

test('bundled angular shader was built and GPU-checked against the current extraction sources',()=>{
 const root=fileURLToPath(new URL('../',import.meta.url));
 const record=JSON.parse(fs.readFileSync(path.join(root,'assets/rendering/rotor-angular-build.json')));
 const bundle=fs.readFileSync(path.join(root,'assets/rendering/rotor-angular-windows.bundle'));
 const sha=b=>crypto.createHash('sha256').update(b).digest('hex');
 assert.equal(bundle.subarray(0,7).toString(),'UnityFS');assert.equal(sha(bundle),record.sha256);
 assert.equal(record.target,'StandaloneWindows64');assert.equal(record.native_skin_check,true);assert.equal(record.native_full_layout_verified,true);assert.equal(record.gpu_preflight,true);assert.equal(record.bundle_load_verified,true);
 for(const input of record.inputs)assert.equal(sha(fs.readFileSync(path.join(root,input.path))),input.sha256,'stale shader/mesh preflight: '+input.path);
});

test('angular shutter bounds, energy, phase transitions and fail-open freshness',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'helicopter-angular-'));
 try {
  const source=fileURLToPath(new URL('../Runtime/Models/HelicopterRotorExposureRules.cs',import.meta.url));
  await writeFile(path.join(dir,'Test.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using CityWeaver.HelicopterModels;
class Program {
 static void Check(bool x,string message){if(!x)throw new Exception(message);}
 static float T(float speed,bool flying=true,bool maneuver=false,bool boarding=false,bool paused=false)=>HelicopterRotorExposureRules.Target(62.83f,speed/3.6f,flying,maneuver,boarding,paused);
 static void Main(){
  Check(T(350)==1&&T(90)==0&&T(160)==1,"cruise/low speed exposure");
  Check(T(125)>.49f&&T(125)<.51f,"smooth transition midpoint");
  Check(T(350,false)==0&&T(350,true,true)==0&&T(350,true,false,true)==0&&T(350,true,false,false,true)==0,"ground, takeoff/landing, boarding and pause stay clear");
  Check(HelicopterRotorExposureRules.Paused(0,1)&&HelicopterRotorExposureRules.Paused(1,0)&&!HelicopterRotorExposureRules.Paused(1,1)&&!HelicopterRotorExposureRules.Paused(4,4)&&!HelicopterRotorExposureRules.Paused(4,.5f),"actual pause/smooth speed, not a stalled interpolation frame");
  // Native takeoff/landing lane flags can clear before the climb/descent ends.
  foreach(float vertical in new[]{-15f,-2f,2f,15f,float.NaN,float.PositiveInfinity}){
   Check(!HelicopterRotorExposureRules.CruiseEligible(350/3.6f,vertical,true,false,false,false),"vertical motion must remain clear without lane flags");
   Check(HelicopterRotorExposureRules.Target(62.83f,350/3.6f,true,false,false,false,vertical)==0,"no trail during climb/descent");
  }
  Check(HelicopterRotorExposureRules.Advance(1,0,1/144f,false,1)==0,"entering maneuver clears a full trail in one frame");
  Check(HelicopterRotorExposureRules.Advance(.5f,1,1/60f,true,.9f)==0,"no trail before stable cruise hold finishes");
  Check(HelicopterRotorExposureRules.CustomBlades(0,true)&&!HelicopterRotorExposureRules.CustomBlades(0,false)&&HelicopterRotorExposureRules.CustomBlades(1,false),"clear replacement bypasses native motion blur only when needed");
  Check(HelicopterRotorExposureRules.DrawSubmesh(0)==1&&HelicopterRotorExposureRules.DrawSubmesh(1)==0,"maneuvers draw only current blades, cruise draws angular samples");
  Check(HelicopterRotorExposureRules.StableCruise(.8f,false,1/60f)==0,"a new climb or maneuver resets cruise hold");
  foreach(float fps in new[]{20,30,60,144}){
   float hold=0,amount=0;int early=(int)(fps*.8f);
   for(int i=0;i<early;i++){hold=HelicopterRotorExposureRules.StableCruise(hold,true,1/fps);amount=HelicopterRotorExposureRules.Advance(amount,1,1/fps,true,hold);Check(amount==0,"takeoff-to-cruise hold cannot flash blur");}
   for(int i=0;i<(int)Math.Ceiling(fps);i++){hold=HelicopterRotorExposureRules.StableCruise(hold,true,1/fps);amount=HelicopterRotorExposureRules.Advance(amount,1,1/fps,true,hold);}
   Check(amount>.999f,"steady cruise restores the accepted full shutter effect");
   Check(HelicopterRotorExposureRules.Advance(amount,0,1/fps,false,hold)==0,"landing clears immediately at every tested frame rate");
  }
  Check(HelicopterRotorExposureRules.Target(float.NaN,100,true,false,false,false)==0,"invalid angular speed");
  Check(HelicopterRotorExposureRules.Target(62.83f,float.PositiveInfinity,true,false,false,false)==0,"invalid linear speed");
  for(int n=1;n<=64;n++){double sum=0;for(int i=0;i<n;i++)sum+=HelicopterRotorExposureRules.Weight(i,n);Check(Math.Abs(sum-1)<.000001,"shutter weights must conserve exposure energy");}
  double mainPitch=2*Math.PI/8,tailPitch=2*Math.PI/5;
  foreach(float spin in new[]{-1000f,-125.66f,-62.83f,0f,62.83f,125.66f,1000f}){
   float main=HelicopterRotorExposureRules.Sweep(spin,false),tail=HelicopterRotorExposureRules.Sweep(spin,true);
   Check(Math.Abs(main)<mainPitch&&Math.Abs(tail)<tailPitch,"no full-disk exposure");
   Check(Math.Sign(main)==Math.Sign(spin)&&Math.Sign(tail)==Math.Sign(spin),"exposure follows spin direction");
  }
  Check(HelicopterRotorExposureRules.Sweep(float.NaN,false)==0,"nonfinite sweep");
  foreach(float fps in new[]{20,30,60,144}){float blend=0;int frames=(int)Math.Ceiling(fps*.7);for(int i=0;i<frames;i++){float before=blend;blend=HelicopterRotorExposureRules.Blend(blend,1,1/fps);Check(blend>=before&&blend<=1,"fade cannot overshoot");}Check(blend>.999f,"fade is frame-rate independent above 20fps");for(int i=0;i<frames;i++)blend=HelicopterRotorExposureRules.Blend(blend,0,1/fps);Check(blend<.001f,"clear-blade restoration");}
  Check(HelicopterRotorExposureRules.Blend(.5f,1,0)==.5f,"zero-delta freeze");
  Check(HelicopterRotorExposureRules.Blend(0,1,10)<.084f,"hitch is bounded");
  Check(!HelicopterRotorExposureRules.Fresh(20,-1)&&!HelicopterRotorExposureRules.Fresh(20,17)&&HelicopterRotorExposureRules.Fresh(20,18),"missing/stale pass restores native blades");
  Console.WriteLine("Angular exposure rules passed");
 }
}`);
  assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Test.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Angular exposure rules passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
