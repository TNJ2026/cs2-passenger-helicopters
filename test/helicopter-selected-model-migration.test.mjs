import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('actual helicopter selection migration replaces hidden legacy choices, deduplicates, preserves other routes and survives reload',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-helicopter-selection-'));
 try{
  const source=fileURLToPath(new URL('../Runtime/Transport/PassengerHelicopterVehicleModelSystem.cs',import.meta.url));
  await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.Linq;using System.Collections;using System.Collections.Generic;using CityWeaver;using Unity.Entities;using Game.Prefabs;using Game.Routes;
namespace Unity.Collections{public enum Allocator{Temp}}
namespace Unity.Entities{
 public struct Entity:IEquatable<Entity>{public int Id;public Entity(int id){Id=id;}public static Entity Null=>default;public bool Equals(Entity b)=>Id==b.Id;public override bool Equals(object b)=>b is Entity e&&Equals(e);public override int GetHashCode()=>Id;public static bool operator==(Entity a,Entity b)=>a.Equals(b);public static bool operator!=(Entity a,Entity b)=>!a.Equals(b);}
 public struct ComponentType{public Type Type;public bool Excluded;public static ComponentType ReadOnly<T>()=>new ComponentType{Type=typeof(T)};public static ComponentType ReadWrite<T>()=>ReadOnly<T>();public static ComponentType Exclude<T>()=>new ComponentType{Type=typeof(T),Excluded=true};}
 public class EntityRows:IDisposable,IEnumerable<Entity>{readonly Entity[] rows;public EntityRows(Entity[] r){rows=r;}public void Dispose(){}public IEnumerator<Entity> GetEnumerator()=>((IEnumerable<Entity>)rows).GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();}
 public class EntityQuery{public EntityManager Manager;public ComponentType[] Types;public EntityRows ToEntityArray(Unity.Collections.Allocator a)=>new EntityRows(Manager.Data.Where(p=>Types.All(t=>t.Excluded?!p.Value.ContainsKey(t.Type):p.Value.ContainsKey(t.Type))).Select(p=>new Entity(p.Key)).ToArray());}
 public class Buffer<T>{public EntityManager Manager;public List<T> Items;public int Length=>Items.Count;public T this[int i]{get=>Items[i];set{Manager.Writes++;Items[i]=value;}}public void Add(T item){Manager.Writes++;Items.Add(item);}public void RemoveAt(int i){Manager.Writes++;Items.RemoveAt(i);}}
 public class EntityManager{
  public Dictionary<int,Dictionary<Type,object>> Data=new Dictionary<int,Dictionary<Type,object>>();public int Writes;
  public bool HasComponent<T>(Entity e)=>Data.TryGetValue(e.Id,out var row)&&row.ContainsKey(typeof(T));
  public T GetComponentData<T>(Entity e)=>(T)Data[e.Id][typeof(T)];
  public Buffer<T> GetBuffer<T>(Entity e)=>new Buffer<T>{Manager=this,Items=(List<T>)Data[e.Id][typeof(T)]};
  public void CompleteAllTrackedJobs(){}
 }
 public class World{public PrefabSystem Prefabs;public Game.UI.InGame.SelectedInfoUISystem Info=new Game.UI.InGame.SelectedInfoUISystem();public T GetExistingSystemManaged<T>()where T:class=>(typeof(T)==typeof(PrefabSystem)?(object)Prefabs:Info)as T;}
}
namespace Game{public class GameSystemBase{public World World=new World();public EntityManager EntityManager=new EntityManager();protected virtual void OnCreate(){}protected virtual void OnUpdate(){}protected EntityQuery GetEntityQuery(params ComponentType[] types)=>new EntityQuery{Manager=EntityManager,Types=types};}}
namespace Game.Common{public struct Created{}public struct Deleted{}}
namespace Game.Tools{public struct Temp{}}
namespace Game.Routes{public struct TransportLine{}public struct VehicleModel{public Entity m_PrimaryPrefab,m_SecondaryPrefab;}}
namespace Game.UI.InGame{public class SelectedInfoUISystem{public int Requests;public void RequestUpdate(){Requests++;}}}
namespace Game.Prefabs{
 public struct PrefabID{public PrefabID(string type,string name){}}
 public struct PrefabRef{public Entity m_Prefab;}
 public class PrefabBase{public string name;public bool isBuiltin,isSubscribedMod;}
 public class HelicopterPrefab:PrefabBase{}
 public class TransportLinePrefab:PrefabBase{}
 public enum TransportType{Helicopter,Bus}public struct PublicTransportVehicleData{public TransportType m_TransportType;}
 public class PrefabSystem{
  public Dictionary<int,PrefabBase> Prefabs=new Dictionary<int,PrefabBase>();public PrefabBase Canonical;
  public bool TryGetPrefab(PrefabID id,out PrefabBase prefab){prefab=Canonical;return prefab!=null;}
  public bool TryGetPrefab<T>(Entity entity,out T prefab)where T:PrefabBase{bool found=Prefabs.TryGetValue(entity.Id,out var p);prefab=p as T;return found;}
  public bool TryGetEntity(PrefabBase p,out Entity entity){foreach(var row in Prefabs)if(ReferenceEquals(row.Value,p)){entity=new Entity(row.Key);return true;}entity=default;return false;}
 }
}
namespace CityWeaver{public static class PassengerHelicopterSystem{public const string Line="owned-line",Vehicle="owned-helicopter";}public static class HelicopterRuntime{public static Logger log=new Logger();public class Logger{public void Info(string s){}}}}
class Probe:PassengerHelicopterVehicleModelSystem{public void Start()=>OnCreate();public void Tick()=>OnUpdate();}
class Program{
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static void Main(){
  var s=new Probe();s.Start();s.Tick();var ps=s.World.Prefabs=new PrefabSystem();
  ps.Canonical=ps.Prefabs[20]=new HelicopterPrefab{name=PassengerHelicopterSystem.Vehicle};
  ps.Prefabs[21]=new HelicopterPrefab{name="HelicopterPassenger01",isBuiltin=true};
  ps.Prefabs[22]=new HelicopterPrefab{name="other-helicopter"};
  ps.Prefabs[23]=new HelicopterPrefab{name="HelicopterPassenger01",isSubscribedMod=true};
  ps.Prefabs[24]=new PrefabBase{name="wrong-cast"};
  ps.Prefabs[1]=new TransportLinePrefab{name=PassengerHelicopterSystem.Line};
  ps.Prefabs[2]=new TransportLinePrefab{name="Bus Line",isBuiltin=true};
  ps.Prefabs[3]=new TransportLinePrefab{name=PassengerHelicopterSystem.Line,isSubscribedMod=true};
  s.EntityManager.Data[20]=new Dictionary<Type,object>{{typeof(PublicTransportVehicleData),new PublicTransportVehicleData{m_TransportType=TransportType.Helicopter}},{typeof(Game.Common.Created),new Game.Common.Created()}};
  VehicleModel Model(int p,int secondary=0)=>new VehicleModel{m_PrimaryPrefab=new Entity(p),m_SecondaryPrefab=new Entity(secondary)};
  void Line(int id,int type,params VehicleModel[] models){s.EntityManager.Data[id]=new Dictionary<Type,object>{{typeof(TransportLine),new TransportLine()},{typeof(PrefabRef),new PrefabRef{m_Prefab=new Entity(type)}},{typeof(VehicleModel),models.ToList()}};}
  Buffer<VehicleModel> Models(int id)=>s.EntityManager.GetBuffer<VehicleModel>(new Entity(id));
  Line(100,1,Model(21));Line(101,1,Model(21),Model(20)); // reported line 2 state
  Line(102,1);Line(103,1,Model(22));Line(104,2,Model(21));Line(105,3,Model(21));
  Line(106,1,Model(21));s.EntityManager.Data[106][typeof(Game.Common.Deleted)]=new Game.Common.Deleted();
  Line(107,1,Model(21));s.EntityManager.Data[107][typeof(Game.Tools.Temp)]=new Game.Tools.Temp();
  Line(108,1,Model(0));Line(109,1,Model(21,22));Line(110,1,Model(23));Line(111,1,Model(24));
  s.Tick();Check(s.EntityManager.Writes==0,"Wait for the default prefab to finish native initialization");
  s.EntityManager.Data[20].Remove(typeof(Game.Common.Created));s.Tick();
  foreach(int id in new[]{100,101,102,108})Check(Models(id).Length==1&&Models(id)[0].m_PrimaryPrefab.Id==20,"Replace old/automatic choices and deduplicate the imported model");
  Check(Models(103)[0].m_PrimaryPrefab.Id==22,"Preserve another explicitly selected model");
  foreach(int id in new[]{104,105,106,107})Check(Models(id)[0].m_PrimaryPrefab.Id==21,"Preserve other routes, subscribed lines and removed/temporary entities");
  Check(Models(109)[0].m_PrimaryPrefab.Id==20&&Models(109)[0].m_SecondaryPrefab.Id==22,"Preserve secondary model data");
  Check(Models(110)[0].m_PrimaryPrefab.Id==23&&Models(111)[0].m_PrimaryPrefab.Id==24,"Do not miscast or replace subscribed helicopter definitions");
  int writes=s.EntityManager.Writes;s.Tick();Check(s.EntityManager.Writes==writes,"Migration must be idempotent");
  Models(100).Items.Clear();Models(100).Items.Add(Model(21));s.Tick();Check(Models(100)[0].m_PrimaryPrefab.Id==20,"Repair a restored legacy selection after reload");
  Check(s.World.Info.Requests>=5,"Refresh the selected-line UI after policy changes");
  Console.WriteLine("Helicopter selected-model migration passed");
 }
}
`);
  let output;
  try{output=execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000});}
  catch(error){throw new Error(`${error.stdout??''}\n${error.stderr??''}`,{cause:error});}
  assert.match(output,/Helicopter selected-model migration passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
