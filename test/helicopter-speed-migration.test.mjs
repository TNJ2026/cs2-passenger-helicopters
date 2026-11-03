import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('actual speed migration sets both passenger helicopters to 350 km/h and repairs serialized data without changing emergency aircraft',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-helicopter-speed-'));
 try{
  const sources=['PassengerHelicopterSpeedSystem','PassengerHelicopterRules'].map(n=>fileURLToPath(new URL(`../Runtime/Transport/${n}.cs`,import.meta.url)));
  await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(s=>`<Compile Include="${s}"/>`).join('')}</ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.Linq;using System.Collections;using System.Collections.Generic;
using CityWeaver;using Game.Prefabs;using Unity.Entities;
namespace Unity.Collections {public enum Allocator{Temp}}
namespace Unity.Entities {
 public struct Entity {public int Id;public Entity(int id){Id=id;}}
 public struct ComponentType {
  public Type Type;public bool Excluded;
  public static ComponentType ReadOnly<T>()=>new ComponentType{Type=typeof(T)};
  public static ComponentType ReadWrite<T>()=>ReadOnly<T>();
  public static ComponentType Exclude<T>()=>new ComponentType{Type=typeof(T),Excluded=true};
 }
 public sealed class EntityRows:IDisposable,IEnumerable<Entity>{readonly Entity[] rows;public EntityRows(Entity[] r){rows=r;}public void Dispose(){}public IEnumerator<Entity> GetEnumerator()=>((IEnumerable<Entity>)rows).GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();}
 public class EntityQuery {public EntityManager Manager;public ComponentType[] Types;public EntityRows ToEntityArray(Unity.Collections.Allocator a)=>new EntityRows(Manager.Data.Where(p=>Types.All(t=>t.Excluded?!p.Value.ContainsKey(t.Type):p.Value.ContainsKey(t.Type))).Select(p=>new Entity(p.Key)).ToArray());}
 public class EntityManager {
  public Dictionary<int,Dictionary<Type,object>> Data=new Dictionary<int,Dictionary<Type,object>>();public int Writes;
  public T GetComponentData<T>(Entity e)=>(T)Data[e.Id][typeof(T)];
  public void SetComponentData<T>(Entity e,T value){Writes++;Data[e.Id][typeof(T)]=value;}
 }
 public class World {public PrefabSystem Prefabs;public T GetExistingSystemManaged<T>() where T:class=>Prefabs as T;}
}
namespace Game {
 public class GameSystemBase {
  public World World=new World();public EntityManager EntityManager=new EntityManager();
  protected virtual void OnCreate(){}protected virtual void OnUpdate(){}
  protected EntityQuery GetEntityQuery(params ComponentType[] types)=>new EntityQuery{Manager=EntityManager,Types=types};
 }
}
namespace Game.Common {public struct Created{}public struct Deleted{}}
namespace Game.Prefabs {
 public struct PrefabData{}public enum TransportType{Helicopter,Bus}
 public class PrefabBase {public string name;public bool isBuiltin,isSubscribedMod;}
 public class HelicopterPrefab:PrefabBase {public float m_FlyingMaxSpeed=250,m_VelocitySwayFactor=.7f;}
 public struct HelicopterData {public float m_FlyingMaxSpeed,m_FlyingAcceleration,m_FlyingAngularAcceleration,m_AccelerationSwayFactor,m_VelocitySwayFactor;}
 public struct PublicTransportVehicleData {public TransportType m_TransportType;}
 public class PrefabSystem {public Dictionary<int,PrefabBase> Prefabs=new Dictionary<int,PrefabBase>();public bool TryGetPrefab<T>(Entity e,out T value) where T:PrefabBase {bool found=Prefabs.TryGetValue(e.Id,out var p);value=p as T;return found;}}
}
namespace CityWeaver {
 public static class PassengerHelicopterSystem{public const string Vehicle="CityWeaver Passenger Helicopter";}
 public static class HelicopterRuntime{public static Logger log=new Logger();public class Logger{public void Info(string s){}}}
}
class Probe:PassengerHelicopterSpeedSystem{public void Start()=>OnCreate();public void Tick()=>OnUpdate();}
class Program {
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static void Main(){
  var s=new Probe();s.Start();s.Tick();s.World.Prefabs=new PrefabSystem();
  HelicopterData Old()=>new HelicopterData{m_FlyingMaxSpeed=250f/3.6f,m_FlyingAcceleration=10,m_FlyingAngularAcceleration=.17453293f,m_AccelerationSwayFactor=.5f,m_VelocitySwayFactor=.7f/(250f/3.6f)};
  void Add(int id,PrefabBase prefab,TransportType transport=TransportType.Helicopter){s.World.Prefabs.Prefabs[id]=prefab;s.EntityManager.Data[id]=new Dictionary<Type,object>{{typeof(PrefabData),new PrefabData()},{typeof(HelicopterData),Old()},{typeof(PublicTransportVehicleData),new PublicTransportVehicleData{m_TransportType=transport}}};}
  Add(1,new HelicopterPrefab{name=PassengerHelicopterSystem.Vehicle});
  Add(2,new HelicopterPrefab{name="HelicopterPassenger01",isBuiltin=true});
  Add(3,new HelicopterPrefab{name="HelicopterFire01",isBuiltin=true});
  Add(4,new HelicopterPrefab{name="HelicopterMedical01",isBuiltin=true});
  Add(5,new HelicopterPrefab{name=PassengerHelicopterSystem.Vehicle,isSubscribedMod=true});
  Add(6,new PrefabBase{name="HelicopterPassenger01"}); // native true + null cast
  Add(7,null);Add(8,new HelicopterPrefab{name="HelicopterPassenger01"},TransportType.Bus);
  Add(9,new HelicopterPrefab{name="HelicopterPassenger01"});s.EntityManager.Data[9][typeof(Game.Common.Created)]=new Game.Common.Created();
  Add(10,new HelicopterPrefab{name="HelicopterPassenger01"});s.EntityManager.Data[10][typeof(Game.Common.Deleted)]=new Game.Common.Deleted();
  Add(11,new HelicopterPrefab{name="HelicopterPolice01",isBuiltin=true});
  s.Tick();
  for(int id=1;id<=11;id++){
   var data=s.EntityManager.GetComponentData<HelicopterData>(new Entity(id));bool passenger=id<=2;
   Check(Math.Abs(data.m_FlyingMaxSpeed-(passenger?350f:250f)/3.6f)<.0001f,"Only two passenger models may change speed");
   Check(data.m_FlyingAcceleration==10&&data.m_FlyingAngularAcceleration==Old().m_FlyingAngularAcceleration&&data.m_AccelerationSwayFactor==.5f,"Preserve acceleration, turn acceleration and sway");
   if(passenger){var p=(HelicopterPrefab)s.World.Prefabs.Prefabs[id];Check(p.m_FlyingMaxSpeed==350,"Managed prefabs must also initialize future copies at 350");Check(Math.Abs(data.m_VelocitySwayFactor*data.m_FlyingMaxSpeed-.7f)<.000001f,"Preserve visual sway amplitude");}
  }
  int writes=s.EntityManager.Writes;s.Tick();Check(s.EntityManager.Writes==writes,"No repeated writes when speed already matches");
  s.EntityManager.Data[2][typeof(HelicopterData)]=Old();s.Tick();
  Check(Math.Abs(s.EntityManager.GetComponentData<HelicopterData>(new Entity(2)).m_FlyingMaxSpeed*3.6f-350)<.001f,"Migrate a saved native prefab restored with its old speed");
  Check(s.EntityManager.Writes==writes+1,"Only the restored prefab needs another write");
  Console.WriteLine("Passenger helicopter speed migration passed");
 }
}
`);
  assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Passenger helicopter speed migration passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
