import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('actual capacity migration tolerates native successful null casts and only changes owned helicopter assets',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-capacity-'));
 try{
  const sources=['PassengerHelicopterCapacitySystem','PassengerHelicopterRules','HelicopterAssetOwnership','BundledHelicopterAssets'].map(n=>fileURLToPath(new URL(`../Runtime/Transport/${n}.cs`,import.meta.url)));
  await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(s=>`<Compile Include="${s}"/>`).join('')}</ItemGroup></Project>`);
  // The adapter deliberately reproduces the game's true + null `as T` contract.
  // Compile and execute the real migration system against controlled ECS rows.
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.Linq;using System.Collections;using System.Collections.Generic;
using CityWeaver;using Game.Prefabs;using Unity.Entities;
namespace Unity.Collections { public enum Allocator {Temp} }
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
namespace Game.Common {public struct Created{} public struct Deleted{}}
namespace Game.Prefabs {
 public struct PrefabData{} public enum TransportType{Helicopter,Bus}
 public class PrefabBase {public string name;public bool isBuiltin,isSubscribedMod;public object Component;public PrefabAsset asset;public T GetComponent<T>() where T:class=>Component as T;}
 public class PrefabAsset {public Identifier id=new Identifier();} public class Identifier {public GuidValue guid;}
 public struct GuidValue {public string value;public static implicit operator GuidValue(string s)=>new GuidValue{value=s};public override string ToString()=>value??new string('0',32);}
 public class BuildingPrefab:PrefabBase{}
 public class BuildingUpgradePrefab:PrefabBase{}
 public class TransportDepot {public int m_VehicleCapacity;}
 public class PublicTransport {public int m_PassengerCapacity;}
 public struct TransportDepotData {public TransportType m_TransportType;public int m_VehicleCapacity;}
 public struct PublicTransportVehicleData {public TransportType m_TransportType;public int m_PassengerCapacity;}
 public class PrefabSystem {public Dictionary<int,PrefabBase> Prefabs=new Dictionary<int,PrefabBase>();public bool TryGetPrefab<T>(Entity e,out T value) where T:PrefabBase {bool found=Prefabs.TryGetValue(e.Id,out var p);value=p as T;return found;}}
}
namespace CityWeaver {
 public static class PassengerHelicopterSystem {public const string Airport="owned-airport",Vehicle="owned-helicopter";}
 public static class ImportedHelipadMenuSystem {public const string Airport="CityWeaver_850b7b8e4f8b4a679fa2623a7c3dd3e0";}
 public static class HelicopterRuntime {public static Logger log=new Logger();public class Logger{public void Info(string text){}}}
}
class Probe:PassengerHelicopterCapacitySystem{public void Start()=>OnCreate();public void Tick()=>OnUpdate();}
class Program {
 static void Check(bool v){if(!v)throw new Exception("Capacity migration regression");}
 static void Main(){
  var s=new Probe();s.Start();s.Tick(); // PrefabSystem absent during lifecycle transition.
  s.World.Prefabs=new PrefabSystem();
  void Add(int id,PrefabBase p,TransportType type=TransportType.Helicopter){s.World.Prefabs.Prefabs[id]=p;s.EntityManager.Data[id]=new Dictionary<Type,object>{{typeof(PrefabData),new PrefabData()},{typeof(TransportDepotData),new TransportDepotData{m_TransportType=type,m_VehicleCapacity=5}}};}
  var standard=new BuildingPrefab{name=PassengerHelicopterSystem.Airport,Component=new TransportDepot{m_VehicleCapacity=5}};
  var imported=new BuildingPrefab{name=ImportedHelipadMenuSystem.Airport,Component=new TransportDepot{m_VehicleCapacity=5}};
  Add(1,new BuildingUpgradePrefab{name="upgrade"}); // native true + null BuildingPrefab: former crash
  Add(2,null);Add(3,standard);Add(4,imported);
  Add(5,new BuildingPrefab{name=PassengerHelicopterSystem.Airport,isBuiltin=true});
  Add(6,new BuildingPrefab{name=PassengerHelicopterSystem.Airport,isSubscribedMod=true});
  Add(7,new BuildingPrefab{name="unrelated"});
  Add(8,new BuildingPrefab{name=PassengerHelicopterSystem.Airport},TransportType.Bus);
  Add(9,new BuildingPrefab{name=PassengerHelicopterSystem.Airport});s.EntityManager.Data[9][typeof(Game.Common.Created)]=new Game.Common.Created();
  Add(10,new BuildingPrefab{name=PassengerHelicopterSystem.Airport});s.EntityManager.Data[10][typeof(Game.Common.Deleted)]=new Game.Common.Deleted();
  var vehicle=new PrefabBase{name=PassengerHelicopterSystem.Vehicle,Component=new PublicTransport()};
  s.World.Prefabs.Prefabs[11]=vehicle;s.EntityManager.Data[11]=new Dictionary<Type,object>{{typeof(PrefabData),new PrefabData()},{typeof(PublicTransportVehicleData),new PublicTransportVehicleData{m_TransportType=TransportType.Helicopter,m_PassengerCapacity=15}}};
  s.World.Prefabs.Prefabs[12]=null;s.EntityManager.Data[12]=new Dictionary<Type,object>{{typeof(PrefabData),new PrefabData()},{typeof(PublicTransportVehicleData),new PublicTransportVehicleData()}};
  var sourceAsset=new PrefabAsset{id=new Identifier{guid="2f2dd49bae855f1a7db2f980737b0ac0"}};
  Add(13,new BuildingPrefab{name=ImportedHelipadMenuSystem.Airport,isSubscribedMod=true,asset=sourceAsset,Component=new TransportDepot{m_VehicleCapacity=5}});
  Add(14,new BuildingPrefab{name=ImportedHelipadMenuSystem.Airport,isSubscribedMod=true,asset=new PrefabAsset{id=new Identifier{guid="ffffffffffffffffffffffffffffffff"}}});
  Add(15,new BuildingPrefab{name=ImportedHelipadMenuSystem.Airport,isSubscribedMod=true,isBuiltin=true,asset=sourceAsset});
  s.Tick();
  for(int id=1;id<=10;id++)Check(s.EntityManager.GetComponentData<TransportDepotData>(new Entity(id)).m_VehicleCapacity==(id==3||id==4?6:5));
  for(int id=13;id<=15;id++)Check(s.EntityManager.GetComponentData<TransportDepotData>(new Entity(id)).m_VehicleCapacity==(id==13?6:5));
  Check(((TransportDepot)standard.Component).m_VehicleCapacity==6&&((TransportDepot)imported.Component).m_VehicleCapacity==6);
  Check(s.EntityManager.GetComponentData<PublicTransportVehicleData>(new Entity(11)).m_PassengerCapacity==60);
  int writes=s.EntityManager.Writes;s.Tick();Check(s.EntityManager.Writes==writes);
  Console.WriteLine("Capacity migration regression passed");
 }
}
`);
  assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Capacity migration regression passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
