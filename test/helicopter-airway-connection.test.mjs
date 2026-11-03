import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, writeFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('collapsed airport and helipad projections separate safely; pending or failed transport paths never certify a preview', async () => {
  const dir = await mkdtemp(path.join(tmpdir(), 'helicopter-airway-'));
  try {
    const files = [fileURLToPath(new URL('../Runtime/Transport/HelipadAirwayRules.cs', import.meta.url))];
    await writeFile(path.join(dir, 'Test.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${files.map(p => `<Compile Include="${p}"/>`).join('')}</ItemGroup></Project>`);
    await writeFile(path.join(dir, 'Program.cs'), String.raw`
using System; using CityWeaver;
class Program {
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static int Node(float t)=>(int)(t*32767f);
 static void Pair(float x,float y,float length) {
  Check(HelipadAirwayRules.TrySeparate(x,y,length,out var a,out var b),"collapsed pair must be repaired");
  Check(a>=0&&a<=1&&b>=0&&b<=1&&Node(a)!=Node(b),"native nodes must differ and stay on the same airway");
  Check((a<=b)==(x<=y),"projection order preserved");
  Check(Math.Abs(a-b)*length>=Math.Min(1.99f,length*.999f),"minimum physical separation");
  Check(!HelipadAirwayRules.TrySeparate(a,b,length,out var c,out var d)&&a==c&&b==d,"save/reload and repeat scans are idempotent");
 }
 static void Main() {
  const float x=.5140557885169983f,y=.5140683054924011f,length=494.3448486328125f;
  Check(Node(x)==16844&&Node(y)==16844,"Springfield reproduction must collapse to the observed native node");
  Pair(x,y,length);Pair(y,x,length);
  // Airport line 2: direct connectors on different ground lanes collapse,
  // while the side apron connector remains a valid independent depot route.
  var airport=new[]{.2332688271999359f,.23326754570007324f,.2897593080997467f};
  Check(Node(airport[0])==7643&&Node(airport[1])==7643,"airport reproduction node");
  Check(HelipadAirwayRules.TrySeparateAll(airport,length,out var fixedAirport),"airport direct connectors repaired");
  Check(Node(fixedAirport[0])!=Node(fixedAirport[1]),"different ground lanes must get different airway nodes");
  Check(fixedAirport[2]==airport[2],"side apron/depot connector must remain unchanged");
  Check(!HelipadAirwayRules.TrySeparateAll(fixedAirport,length,out _),"airport reload/repeat scan idempotent");
  foreach(var cluster in new[]{new[]{0f,0f,0f,.2f},new[]{1f,1f,1f,.8f},
    new[]{.5f,.5f,.5f,.501f,.501f,.9f},new[]{.50001f,.5f,.3f,.3f,.2f,.8f}}) {
   Check(HelipadAirwayRules.TrySeparateAll(cluster,length,out var fixedCluster),"multi-marker cluster repaired");
   var nodes=new System.Collections.Generic.HashSet<int>();
   foreach(var t in fixedCluster)Check(t>=0&&t<=1&&nodes.Add(Node(t)),"no new neighbor collisions");
   Check(!HelipadAirwayRules.TrySeparateAll(fixedCluster,length,out _),"multi-cluster idempotent");
  }
  Check(!HelipadAirwayRules.TrySeparateAll(new[]{.49997f,.5f,.5f,.50004f},length,out var dense)&&dense[0]==.49997f&&dense[3]==.50004f,
    "unsupported dense cluster cannot move an unrelated neighbor");
  Check(!HelipadAirwayRules.TrySeparateAll(null,length,out _),"null group rejected");
  Check(!HelipadAirwayRules.TrySeparate(.6041776538f,.5758626461f,length,out var a,out var b)&&
    a==.6041776538f&&b==.5758626461f,"working neighboring pad unchanged");
  foreach(float len in new[]{.01f,1f,2f,494.34485f,1000000f})
   foreach(float t in new[]{0f,.000001f,.25f,.5f,.999999f,1f}) Pair(t,t,len);
  foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f,1.01f})
   Check(!HelipadAirwayRules.TrySeparate(bad,.5f,length,out _,out _),"invalid projection rejected");
  foreach(float bad in new[]{float.NaN,float.PositiveInfinity,-1f,0f})
   Check(!HelipadAirwayRules.TrySeparate(.5f,.5f,bad,out _,out _),"invalid airway length rejected");
  Console.WriteLine("Helipad airway and asynchronous preview regression passed");
 }
}`);
    assert.match(execFileSync(process.env.CITYWEAVER_TEST_DOTNET || 'dotnet',
      ['run', '--project', path.join(dir, 'Test.csproj'), '-c', 'Release'],
      {encoding:'utf8', timeout:120000}), /Helipad airway and asynchronous preview regression passed/);
  } finally { await rm(dir, {recursive:true, force:true}); }
});
