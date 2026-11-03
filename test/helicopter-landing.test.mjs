import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, writeFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('landing controller brakes horizontally at 350 km/h, captures before descent, and respects native holds', async () => {
  const dir = await mkdtemp(path.join(tmpdir(), 'helicopter-landing-'));
  try {
    const sources = ['HelicopterLandingRules','PassengerHelicopterRules'].map(n =>
      fileURLToPath(new URL(`../Runtime/Transport/${n}.cs`, import.meta.url)));
    await writeFile(path.join(dir, 'Test.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(p => `<Compile Include="${p}"/>`).join('')}</ItemGroup></Project>`);
    await writeFile(path.join(dir, 'Program.cs'), String.raw`
using System; using CityWeaver;
class Program {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static double Clamp(double x,double min,double max)=>Math.Max(min,Math.Min(max,x));
 // Independent acceleration-limited flight model using native movement cadence,
 // height restriction and braking envelope. It detects overshoot and hover stalls;
 // passing this model does not replace in-game collision/boarding verification.
 static void Flight(double direction,double height,double acceleration,double initialSpeed) {
  double dt=4.0/15,cruise=350/3.6;
  double start=Math.Max(750,initialSpeed*initialSpeed/(2*acceleration)+initialSpeed*.8+50);
  double x=-direction*start,y=height,vx=direction*initialSpeed,vy=0,worst=0;
  for(int tick=0;tick<1600;tick++) {
   double dx=-x,dy=-y,h=Math.Abs(dx),d=Math.Sqrt(dx*dx+dy*dy),v=Math.Sqrt(vx*vx+vy*vy);
   if(d<1&&v<.1){Check(worst<3,"overshoot exceeds 3 m: "+worst);return;}
   double ratio=Clamp((vx*dx+vy*dy+1)/(v*d+1),0,1),blend=1-Math.Sqrt(1-ratio*ratio);
   double brakeTime=acceleration*(dt+.5);
   double native=Math.Sqrt(brakeTime*brakeTime+2*acceleration*(h+(d-h)*blend))-brakeTime;
   native=Clamp(native,Math.Max(0,v-acceleration*dt),Math.Min(cruise,v+acceleration*dt));
   Check(HelicopterLandingRules.TryLimit((float)h,(float)dy,(float)Math.Abs(vx),(float)acceleration,
     (float)native,out var limit,out var descend),"valid flight inputs");
   Check(limit<=native+.00001,"native/admission limit cannot be raised");
   double climb=Math.Min(Math.Atan((height-y)*4/100),Math.Asin(Clamp(h*2/100-1,0,1)));
   if(descend)climb=Math.Min(climb,-Math.PI/2);
   double nx=d>0?dx/d:0,ny=d>0?dy/d:0;
   if(climb>Math.Asin(Clamp(ny,0,1))){nx=Math.Sign(dx)*Math.Cos(climb);ny=Math.Sin(climb);}
   double ax=nx*limit-vx,ay=ny*limit-vy,a=Math.Sqrt(ax*ax+ay*ay),step=acceleration*dt;
   if(a>step){ax*=step/a;ay*=step/a;}
   vx+=ax;vy+=ay;x+=vx*dt;y+=vy*dt;worst=Math.Max(worst,x*direction);
  }
  throw new Exception("controller stalled before completing the landing");
 }
 static void Main() {
  Check(PassengerHelicopterRules.FlyingMaxSpeedKph==350,"cruise setting changed");
  Check(HelicopterLandingRules.Managed(true,true,false),"own route included");
  Check(HelicopterLandingRules.Managed(false,false,true),"native return-to-depot removes CurrentRoute and must remain covered");
  Check(!HelicopterLandingRules.Managed(false,true,true),"unrelated route excluded");
  Check(!HelicopterLandingRules.Managed(false,false,false),"unrelated off-route aircraft excluded");
  Check(HelicopterLandingRules.TryLimit(9.75f,-91.51f,39.95f,10,39.4f,out var limit,out var descend)&&
    limit<10&&!descend,"observed 10 m / 92 m / 144 km/h approach must brake, not dive");
  Check(HelicopterLandingRules.TryLimit(1,-80,1,10,40,out limit,out descend)&&descend&&limit<=8,"captured aircraft may descend");
  Check(HelicopterLandingRules.TryLimit(1,-80,20,10,40,out limit,out descend)&&!descend&&limit<3,"fast aircraft passing above pad cannot dive");
  Check(HelicopterLandingRules.TryLimit(1,-80,0,10,0,out limit,out descend)&&limit==0,"pad admission hold preserved");
  Check(HelicopterLandingRules.TryLimit(2000,-100,97.22222f,10,97.22222f,out limit,out descend)&&limit==97.22222f,"far approach retains 350 km/h limit");
  foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-1})
   Check(!HelicopterLandingRules.TryLimit(invalid,-100,20,10,40,out _,out _),"bad geometry rejected");
  foreach(float invalid in new[]{0f,-1f,float.NaN})
   Check(!HelicopterLandingRules.TryLimit(100,-100,20,invalid,40,out _,out _),"bad acceleration rejected");
  foreach(double dir in new[]{-1.0,1.0})foreach(double height in new[]{5.0,95.0,100.0,200.0})
   foreach(double a in new[]{5.0,10.0,20.0})foreach(double speed in new[]{0.0,350/3.6})Flight(dir,height,a,speed);
  Console.WriteLine("Landing controller flight regression passed; cruise remains 350 km/h");
 }
}`);
    assert.match(execFileSync(process.env.CITYWEAVER_TEST_DOTNET || 'dotnet',
      ['run', '--project', path.join(dir, 'Test.csproj'), '-c', 'Release'],
      {encoding:'utf8', timeout:120000}), /Landing controller flight regression passed/);
  } finally { await rm(dir, {recursive:true, force:true}); }
});
