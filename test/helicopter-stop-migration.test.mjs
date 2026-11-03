import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('actual stop migration tolerates mixed prefab types and preserves saved stop/waypoint identities', async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cityweaver-stop-migration-'));
 try {
  const source=fileURLToPath(new URL('../Runtime/Transport/PassengerHelicopterStopMigrationSystem.cs',import.meta.url));
  const ownership=['BundledHelicopterAssets.cs','HelicopterAssetOwnership.cs'].map(n=>fileURLToPath(new URL('../Runtime/Transport/'+n,import.meta.url)));
  await writeFile(path.join(dir,'Test.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${[source,...ownership].map(p=>`<Compile Include="${p}"/>`).join('')}</ItemGroup></Project>`);
  // Compile the production OnUpdate against a minimal ECS fake. Prefab lookup
  // deliberately reproduces the native true + null result for a failed cast.
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System; using System.Collections.Generic; using System.Linq;
using Unity.Entities; using Game.Prefabs; using CityWeaver;
namespace Unity.Collections { public enum Allocator { Temp } }
namespace Unity.Entities {
 public record struct Entity(int Id);
 public class ComponentType { public static ComponentType ReadOnly<T>()=>new(); public static ComponentType Exclude<T>()=>new(); }
 public class Entities : List<Entity>, IDisposable { public void Dispose(){} }
 public class EntityQuery { public Entities Items=new(); public Entities ToEntityArray(Unity.Collections.Allocator a)=>Items; }
 public class World { public PrefabSystem Prefabs=new(); public T GetExistingSystemManaged<T>() where T:class=>Prefabs as T; }
 public class Buffer<T> : List<T> { public int Length=>Count; }
 public class EntityManager {
  public Dictionary<(Entity,Type),object> Data=new();
  public HashSet<Entity> Live=new(); public int Removals;
  public void Put<T>(Entity e,T value){Live.Add(e);Data[(e,typeof(T))]=value;}
  public bool Exists(Entity e)=>Live.Contains(e);
  public bool HasComponent<T>(Entity e)=>Data.ContainsKey((e,typeof(T)));
  public T GetComponentData<T>(Entity e)=>(T)Data[(e,typeof(T))];
  public void AddComponent<T>(Entity e)=>Put(e,Activator.CreateInstance<T>());
  public void RemoveComponent<T>(Entity e){if(Data.Remove((e,typeof(T))))Removals++;}
  public bool HasBuffer<T>(Entity e)=>HasComponent<Buffer<T>>(e);
  public Buffer<T> GetBuffer<T>(Entity e)=>GetComponentData<Buffer<T>>(e);
 }
}
namespace Game { public class GameSystemBase {
 public World World=new(); public EntityManager EntityManager=new(); public EntityQuery Query=new();
 protected virtual void OnCreate(){} protected virtual void OnUpdate(){}
 protected EntityQuery GetEntityQuery(params ComponentType[] c)=>Query;
 public void Init()=>OnCreate(); public void Tick()=>OnUpdate();
} }
namespace Game.Common { public struct Deleted{} public struct Created{} public struct Updated{} public struct Owner { public Entity m_Owner; } }
namespace Game.Tools { public struct Temp{} }
namespace Game.Objects { public struct SpawnLocation{} }
namespace Game.Routes { public struct TransportStop{} public struct ConnectedRoute { public Entity m_Waypoint; } }
namespace Game.Buildings { public struct SpawnLocationElement { public Entity m_SpawnLocation; } }
namespace Game.Prefabs {
 public struct PrefabRef { public Entity m_Prefab; } public struct SpawnLocationData{} public class SpawnLocation{}
 public class PrefabBase {
  public string name; public bool isBuiltin,isSubscribedMod; public PrefabAsset asset; public HashSet<Type> Components=new();
  public bool Has<T>()=>Components.Contains(typeof(T)); public void Remove<T>()=>Components.Remove(typeof(T));
 }
 public class MarkerObjectPrefab:PrefabBase{}
 public class PrefabAsset { public AssetId id=new(); }
 public class AssetId { public string guid; }
 public class PrefabSystem {
  public Dictionary<Entity,PrefabBase> Items=new();
  public bool TryGetPrefab<T>(Entity e,out T value) where T:PrefabBase {
   bool found=Items.TryGetValue(e,out var item);value=item as T;return found;
  }
 }
}
namespace CityWeaver { public static class PassengerHelicopterSystem { public const string Stop="CityWeaver Helicopter Boarding Stop"; } }
class Program {
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 static void Main(){
  var s=new PassengerHelicopterStopMigrationSystem();s.Init();var em=s.EntityManager;
  Entity Add(int id,PrefabBase prefab,bool instance,bool runtime){
   var e=new Entity(id);var p=new Entity(id+100);s.Query.Items.Add(e);
   em.Put(e,new PrefabRef{m_Prefab=p});
   if(prefab!=null)s.World.Prefabs.Items[p]=prefab;
   if(instance)em.Put(e,new Game.Objects.SpawnLocation());
   if(runtime)em.Put(p,new SpawnLocationData());return e;
  }
  var ordinary=Add(1,new PrefabBase(),true,true);
  var missing=Add(2,null,true,true);
  var unrelated=Add(3,new MarkerObjectPrefab{name="Other"},true,true);
  var builtin=Add(4,new MarkerObjectPrefab{name=PassengerHelicopterSystem.Stop,isBuiltin=true},true,true);
  var subscribed=Add(5,new MarkerObjectPrefab{name=PassengerHelicopterSystem.Stop,isSubscribedMod=true},true,true);
  var pending=Add(6,new MarkerObjectPrefab{name=PassengerHelicopterSystem.Stop},true,true);
  em.Put(new Entity(106),new Game.Common.Created());
  // Already repaired instance, but stale serialized prefab restored on reload.
  var repaired=Add(7,new MarkerObjectPrefab{name=PassengerHelicopterSystem.Stop},false,true);
  var legacyPrefab=new MarkerObjectPrefab{name=PassengerHelicopterSystem.Stop};
  legacyPrefab.Components.Add(typeof(SpawnLocation));
  var legacy=Add(8,legacyPrefab,true,true);
  var owner=new Entity(200);var waypoint=new Entity(201);var other=new Entity(202);
  em.Live.Add(waypoint);
  em.Put(legacy,new Game.Common.Owner{m_Owner=owner});
  em.Put(legacy,new Buffer<Game.Routes.ConnectedRoute>{new(){m_Waypoint=waypoint}});
  em.Put(owner,new Buffer<Game.Buildings.SpawnLocationElement>{new(){m_SpawnLocation=legacy},new(){m_SpawnLocation=other}});
  MarkerObjectPrefab Owned(string name=PassengerHelicopterSystem.Stop,string cid="e2592fa67f8e2465d7d32de27b2ab067")=>new(){
   name=name,isSubscribedMod=true,asset=new PrefabAsset{id=new AssetId{guid=cid}}
  };
  // The downloaded airport references an old serialized marker, not the fresh registry copy.
  // Both pads share one prefab: cleaning it for the first pad must not skip the second instance.
  var subscribedPrefab=Owned(); subscribedPrefab.Components.Add(typeof(SpawnLocation));
  var ownedFirst=Add(9,subscribedPrefab,true,true);
  var ownedSecond=Add(10,subscribedPrefab,true,false);
  em.Put(ownedSecond,new PrefabRef{m_Prefab=new Entity(109)});
  var subscribedOwner=new Entity(203);var subscribedWaypoint=new Entity(204);
  em.Live.Add(subscribedWaypoint);
  foreach(var e in new[]{ownedFirst,ownedSecond})em.Put(e,new Game.Common.Owner{m_Owner=subscribedOwner});
  em.Put(ownedFirst,new Buffer<Game.Routes.ConnectedRoute>{new(){m_Waypoint=subscribedWaypoint}});
  em.Put(subscribedOwner,new Buffer<Game.Buildings.SpawnLocationElement>{new(){m_SpawnLocation=ownedFirst},new(){m_SpawnLocation=ownedSecond},new(){m_SpawnLocation=other}});
  var subscribedReload=Add(11,Owned(),false,true);
  var foreignCid=Add(12,Owned(cid:"ffffffffffffffffffffffffffffffff"),true,true);
  var foreignName=Add(13,Owned(name:"Other subscribed stop"),true,true);
  var cleanOwned=Add(14,Owned(),false,false);
  s.Tick();
  foreach(var e in new[]{ordinary,missing,unrelated,builtin,subscribed,pending,foreignCid,foreignName}){
   Check(em.HasComponent<Game.Objects.SpawnLocation>(e),"unrelated/pending stop changed");
   Check(!em.HasComponent<Game.Common.Updated>(e),"unrelated stop refreshed");
  }
  Check(!em.HasComponent<SpawnLocationData>(new Entity(107)),"prefab-only legacy data survived");
  Check(!em.HasComponent<SpawnLocationData>(new Entity(108))&&!legacyPrefab.Has<SpawnLocation>(),"legacy prefab not cleaned");
  Check(!em.HasComponent<Game.Objects.SpawnLocation>(legacy),"legacy instance not cleaned");
  foreach(var e in new[]{repaired,legacy,owner,waypoint})Check(em.HasComponent<Game.Common.Updated>(e),"refresh missing");
  Check(em.GetBuffer<Game.Routes.ConnectedRoute>(legacy)[0].m_Waypoint==waypoint,"route reference changed");
  Check(em.GetComponentData<PrefabRef>(legacy).m_Prefab==new Entity(108),"prefab identity changed");
  Check(em.GetBuffer<Game.Buildings.SpawnLocationElement>(owner).Single().m_SpawnLocation==other,"owner buffer incorrectly edited");
  Check(!subscribedPrefab.Has<SpawnLocation>()&&!em.HasComponent<SpawnLocationData>(new Entity(109)),"owned subscribed prefab kept legacy spawn data");
  foreach(var e in new[]{ownedFirst,ownedSecond,subscribedReload}){
   Check(!em.HasComponent<Game.Objects.SpawnLocation>(e),"owned subscribed instance kept obsolete spawn point");
   Check(em.HasComponent<Game.Common.Updated>(e),"owned subscribed stop not refreshed");
  }
  Check(!em.HasComponent<SpawnLocationData>(new Entity(111)),"subscribed reload retained stale runtime prefab data");
  Check(em.GetComponentData<PrefabRef>(ownedSecond).m_Prefab==new Entity(109),"shared subscribed prefab identity changed");
  Check(em.GetBuffer<Game.Routes.ConnectedRoute>(ownedFirst)[0].m_Waypoint==subscribedWaypoint,"subscribed waypoint identity changed");
  Check(em.GetBuffer<Game.Buildings.SpawnLocationElement>(subscribedOwner).Single().m_SpawnLocation==other,"subscribed owner spawn list corrupted");
  Check(!em.HasComponent<Game.Common.Updated>(cleanOwned),"clean subscribed stop refreshed repeatedly");
  int count=em.Removals;s.Tick();Check(em.Removals==count,"migration not idempotent");
  s.World.Prefabs=null;s.Tick();
  Console.WriteLine("Stop migration regression passed");
 }
}
`);
  assert.match(execFileSync(process.env.CITYWEAVER_TEST_DOTNET||'dotnet',['run','--project',path.join(dir,'Test.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Stop migration regression passed/);
 } catch(error) { throw new Error(String(error.stdout || error.message), {cause:error}); } finally {await rm(dir,{recursive:true,force:true});}
});
