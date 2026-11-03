import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('missing-stop protection debounces edits, scopes routes, survives clock reset; pets do not consume passenger count',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cityweaver-line-safety-'));
 try {
  const source=fileURLToPath(new URL('../Runtime/Transport/HelicopterLineSafetyRules.cs',import.meta.url));
  await writeFile(path.join(dir,'Test.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),`using System; using System.Collections.Generic; using CityWeaver;
class Program {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Main(){
  var r=new HelicopterLineSafetyRules<int>();
  Check(!r.ShouldStop(1,100,true,true,true),"first missing observation");
  Check(!r.ShouldStop(1,611,true,true,true),"grace boundary");
  Check(r.ShouldStop(1,612,true,true,true),"persistent missing stop");
  Check(!r.ShouldStop(1,613,true,true,true),"no event flood");
  Check(!r.ShouldStop(1,700,true,false,true),"native stop acknowledged");
  Check(!r.ShouldStop(1,9000,true,false,false),"never auto-resume");
  Check(!r.ShouldStop(2,100,true,true,true),"start edit gap");
  Check(!r.ShouldStop(2,200,true,true,false),"valid edit resets");
  Check(!r.ShouldStop(2,1000,true,true,true),"new gap must wait again");
  Check(r.ShouldStop(2,1512,true,true,true),"second persistent gap");
  Check(!r.ShouldStop(3,0,false,true,true),"other route ignored");
  Check(!r.ShouldStop(3,10000,false,true,true),"other route still ignored");
  Check(!r.ShouldStop(4,5000,true,true,true),"before clock reset");
  Check(!r.ShouldStop(4,20,true,true,true),"clock reset waits");
  Check(!r.ShouldStop(4,531,true,true,true),"clock reset grace");
  Check(r.ShouldStop(4,532,true,true,true),"clock reset protection resumes");
  Check(!r.ShouldStop(5,100,true,true,true),"deleted line tracked");
  r.Retain(new HashSet<int>{1,2,4});
  Check(!r.ShouldStop(5,1000,true,true,true),"deleted entries removed");
  var counts=new PassengerManifestCounts();
  Check(counts.Total==0,"empty vehicle");
  for(int i=0;i<15;i++)counts.Add(false);
  counts.Add(true);
  Check(counts.Passengers==15 && counts.Pets==1 && counts.Total==16,"observed 15 people plus pet");
  var pets=new PassengerManifestCounts();pets.Add(true);pets.Add(true);
  Check(pets.Passengers==0 && pets.Pets==2 && pets.Total==2,"pet-only manifest");
  Console.WriteLine("Line safety and manifest rules passed");
 }
}`);
  assert.match(execFileSync(process.env.CITYWEAVER_TEST_DOTNET||'dotnet',['run','--project',path.join(dir,'Test.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Line safety and manifest rules passed/);
 } finally {await rm(dir,{recursive:true,force:true});}
});
