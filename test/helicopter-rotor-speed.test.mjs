import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('rotor power follows scheduled ground turns, preserves flight RPM and continuous simulation-time phase',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'helicopter-rotor-speed-'));
 try{
  const source=fileURLToPath(new URL('../Runtime/Models/HelicopterRotorSpeedRules.cs',import.meta.url));
  await writeFile(path.join(dir,'Test.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using R=CityWeaver.HelicopterModels.HelicopterRotorSpeedRules;
class Program{
 static void Check(bool x,string m){if(!x)throw new Exception(m);}
 static void Near(double a,double b,double epsilon,string m){Check(Math.Abs(a-b)<epsilon,m+": "+a+" vs "+b);}
 static float Target(float remaining,bool flying=false,bool maneuver=false,bool boarding=true,float speed=0,float vertical=0)=>R.Target(flying,maneuver,boarding,speed,vertical,remaining);
 static void Main(){
  Check(Target(30)==R.IdleFactor,"stationary scheduled boarding may idle");
  Check(Target(4)==1&&Target(0)==1&&Target(-5)==1&&Target(float.NaN)==1,"expired, imminent and unknown departures stay ready");
  Check(Target(30,true)==1&&Target(30,false,true)==1&&Target(30,false,false,false)==1,"takeoff, approach, landing and non-boarding ground movement stay full");
  Check(Target(30,speed:.6f)==1&&Target(30,vertical:.3f)==1,"no idle while moving or descending without lane flags");
  Check(Target(30,speed:float.NaN)==1&&Target(30,vertical:float.PositiveInfinity)==1,"invalid motion fails full");
  Check(float.IsNaN(R.DepartureSeconds(100,0)),"zero frame means unknown schedule");
  Near(R.DepartureSeconds(100,340),4,.00001,"native departure frames use 60 Hz");
  Near(R.DepartureSeconds(uint.MaxValue-119,120),4,.00001,"departure survives uint wrap");
  Check(R.DepartureSeconds(200,100)<0,"past departure is never a fresh boarding wait");
  foreach(float fps in new[]{20,30,60,144})foreach(float simSpeed in new[]{1,2,4}){
   float factor=1,main=.7f,tail=-.3f;double elapsed=0,totalAngle=0;
   float seconds=R.StepSeconds(60*simSpeed/fps,false);
   // A 30-second scheduled stop includes a slow idle-down and a full spool-up
   // before departure. No transport state or departure time is written.
   while(elapsed<30){
    float remaining=(float)(30-elapsed),before=factor;
    factor=R.Advance(factor,Target(remaining),seconds,false);
    Check(factor>=R.IdleFactor-.00001&&factor<=1,"bounded power envelope");
    if(elapsed>=7&&remaining>4.5)Near(factor,R.IdleFactor,.0001,"turnaround reaches idle");
    if(remaining<.5)Near(factor,1,.0001,"scheduled spool-up finishes before departure");
    totalAngle+=62.8318558*seconds*(before+factor)*.5;
    main=R.Phase(main,62.8318558f,seconds,before,factor);
    tail=R.Phase(tail,125.6637116f,seconds,before,factor);
    elapsed+=seconds;
   }
   // Compare orientation on the unit circle (wrapped angles are intentional).
   Near(Math.Sin(main),Math.Sin(.7+totalAngle),.002,"main phase preserves integrated rotation");
   Near(Math.Cos(tail),Math.Cos(-.3+2*totalAngle),.004,"main/tail drive ratio preserved");
   float stopped=R.Phase(main,62.83f,R.StepSeconds(12,true),factor,factor);
   Check(stopped==main&&R.Advance(factor,R.IdleFactor,0,false)==factor,"pause freezes phase and power");
   Check(R.Phase(main,62.83f,R.StepSeconds(0,false),factor,factor)==main,"stalled interpolation does not invent rotation");
   Check(R.Advance(R.IdleFactor,1,seconds,true)==1,"unexpected early airborne departure is immediately full; never delay flight");
  }
  float angle=1.234f;
  // The game can schedule the same managed system twice in one render frame.
  // The envelope and both angles must consume that frame's delta only once.
  foreach(float fps in new[]{20,30,60,144}){
   var gate=new CityWeaver.HelicopterModels.HelicopterRotorFrameGate();float power=1;int accepted=0;
   for(int frame=0;frame<(int)(fps*6);frame++)for(int duplicate=0;duplicate<2;duplicate++)if(gate.Enter(frame)){
    accepted++;power=R.Advance(power,R.IdleFactor,1/fps,false);
    if(frame<(int)(fps*3))Check(power>.81f,"duplicate scheduling must not halve spool-down time");
   }
   Check(accepted==(int)(fps*6),"one update per render frame");Near(power,R.IdleFactor,.0001,"six-second idle transition");
   gate.Reset();Check(gate.Enter((int)(fps*6)-1),"reload can recompute a frame after clearing owned state");
  }
  Check(R.Phase(angle,float.NaN,1,1,1)==angle&&R.Phase(angle,60,1,float.NaN,1)==angle,"non-finite inputs cannot poison visual phase");
  Check(R.StepSeconds(float.NaN,false)==0&&R.StepSeconds(-1,false)==0&&R.StepSeconds(600,false)==.5f,"bad/hitch interpolation bounded");
  Near(R.Phase(angle,-3,.1f,1,1),angle-.3,.00001,"reverse spin direction preserved");
  Near(R.Phase(angle,3,0,.65f,1),angle,.00001,"changing speed never snaps a stationary angle");
  Console.WriteLine("Rotor power and phase scenarios passed");
 }
}`);
  assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Test.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Rotor power and phase scenarios passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
