import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('production runtime schedules the rotor exactly once after interpolation and before skeleton upload',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'helicopter-schedule-'));
 try{
  const source=fileURLToPath(new URL('../Runtime/HelicopterRuntime.cs',import.meta.url));
  const text=await readFile(source,'utf8');
  // Compile the actual production registration method. Stub only its external
  // dependencies; model UpdateSystem's append semantics, verified in the live
  // 1.1.7 schedule (rotor indices635 and652), rather than deduplicating types.
  const native=new Set(['ObjectInterpolateSystem','ProceduralSkeletonSystem']);
  const names=new Set([...text.matchAll(/updateSystem\.Update\w+<([^>]+)>/g)].flatMap(m=>m[1].split(',').map(s=>s.trim())).map(n=>n.startsWith('Game.')?n:native.has(n)?'Game.Rendering.'+n:'CityWeaver.'+n));
  for(const name of native)names.add('Game.Rendering.'+name);
  names.add('Game.Tools.GenerateNodesSystem');
  const stubs=[...names].map(n=>{const p=n.lastIndexOf('.');return 'namespace '+n.slice(0,p)+' { public class '+n.slice(p+1)+' {} }';}).join('\n');
  const project=runtime=>`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="${runtime}"/></ItemGroup></Project>`;
  await writeFile(path.join(dir,'Test.csproj'),project(source));
  await writeFile(path.join(dir,'Program.cs'),stubs+String.raw`
namespace Colossal.Logging{
 public interface ILog{ILog SetShowsErrorsInUI(bool value);void Info(string message);}
 public class Sink:ILog{public ILog SetShowsErrorsInUI(bool value)=>this;public void Info(string message){}}
 public static class LogManager{public static ILog GetLogger(string name)=>new Sink();}
}
namespace Newtonsoft.Json.Linq{
 public class JObject{public object this[string name]=>null;}
 public class JArray{public int Count=>0;public object this[int index]=>null;}
}
namespace CityWeaver{public static class PassengerHelicopterIcons{public static void Register(){}public static void Unregister(){}}}
namespace Game{
 public enum SystemUpdatePhase{MainLoop,Modification1,Modification5,UITooltip,ToolUpdate,GameSimulation,ModificationEnd,Rendering}
 public class UpdateSystem{
  public readonly System.Collections.Generic.List<System.Type> Rendering=new System.Collections.Generic.List<System.Type>();
  public readonly System.Collections.Generic.List<System.Type> Modification1=new System.Collections.Generic.List<System.Type>();
  private void Register(System.Type system,System.Type relative,int direction,SystemUpdatePhase phase){
   var list=phase==SystemUpdatePhase.Rendering?Rendering:phase==SystemUpdatePhase.Modification1?Modification1:null;
   if(list==null)return;
   if(relative==null){list.Add(system);return;}
   int i=list.IndexOf(relative);if(i<0)throw new System.Exception("Missing native anchor: "+relative);
   list.Insert(i+(direction>0?1:0),system);
  }
  public void UpdateAt<T>(SystemUpdatePhase p)=>Register(typeof(T),null,0,p);
  public void UpdateBefore<T>(SystemUpdatePhase p)=>Register(typeof(T),null,-1,p);
  public void UpdateAfter<T>(SystemUpdatePhase p)=>Register(typeof(T),null,1,p);
  public void UpdateBefore<T,U>(SystemUpdatePhase p)=>Register(typeof(T),typeof(U),-1,p);
  public void UpdateAfter<T,U>(SystemUpdatePhase p)=>Register(typeof(T),typeof(U),1,p);
 }
}
class Program{
 static void Main(){
  var update=new Game.UpdateSystem();
  update.UpdateAt<Game.Rendering.ObjectInterpolateSystem>(Game.SystemUpdatePhase.Rendering);
  update.UpdateAt<Game.Rendering.ProceduralSkeletonSystem>(Game.SystemUpdatePhase.Rendering);
  update.UpdateAt<Game.Tools.GenerateObjectsSystem>(Game.SystemUpdatePhase.Modification1);
  update.UpdateAt<Game.Tools.GenerateNodesSystem>(Game.SystemUpdatePhase.Modification1);
  CityWeaver.HelicopterRuntime.Load(update);CityWeaver.HelicopterRuntime.Load(update);
  int gradeCount=0;foreach(var type in update.Modification1)if(type==typeof(CityWeaver.HeliportPlacementGradeSystem))gradeCount++;
  if(gradeCount!=1||update.Modification1[0]!=typeof(CityWeaver.HeliportPlacementGradeSystem))throw new System.Exception("Grade must run once before native object and network generation");
  var schedule=update.Rendering;
  int count=0;foreach(var type in schedule)if(type==typeof(CityWeaver.HelicopterModels.HelicopterRotorExposureSystem))count++;
  if(count!=1)throw new System.Exception("Rotor must execute once, found "+count);
  int rotor=schedule.IndexOf(typeof(CityWeaver.HelicopterModels.HelicopterRotorExposureSystem));
  if(rotor<=schedule.IndexOf(typeof(Game.Rendering.ObjectInterpolateSystem))||rotor>=schedule.IndexOf(typeof(Game.Rendering.ProceduralSkeletonSystem)))throw new System.Exception("Rotor pose must be finalized between interpolation and skeleton upload");
  System.Console.WriteLine("Production render schedule passed");
 }
}`);
  let result;try{result=execFileSync('dotnet',['run','--project',path.join(dir,'Test.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000});}catch(e){throw new Error(String(e.stdout??'')+String(e.stderr??''));}
  assert.match(result,/Production render schedule passed/);
  // Reintroducing the exact two-registration bug must fail this integration
  // scenario, even though all pure RPM envelope tests would still pass.
  const anchor='updateSystem.UpdateBefore<HelicopterModels.HelicopterRotorExposureSystem, ProceduralSkeletonSystem>';
  assert(text.includes(anchor));
  const mutant=text.replace(anchor,'updateSystem.UpdateAfter<HelicopterModels.HelicopterRotorExposureSystem, ObjectInterpolateSystem>(SystemUpdatePhase.Rendering);\n            '+anchor);
  await writeFile(path.join(dir,'DuplicateRuntime.cs'),mutant);
  await writeFile(path.join(dir,'Test.csproj'),project('DuplicateRuntime.cs'));
  let rejection='';try{execFileSync('dotnet',['run','--project',path.join(dir,'Test.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000,stdio:['ignore','pipe','pipe']});}catch(e){rejection=String(e.stdout??'')+String(e.stderr??'');}
  assert.match(rejection,/Rotor must execute once, found 2/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
