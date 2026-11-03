import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, writeFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('actual construction-menu cleanup removes owned subscribed duplicates and preserves other categories and asset references', async () => {
 const dir = await mkdtemp(path.join(tmpdir(), 'helicopter-menu-'));
 try {
  const files = ['PassengerHelicopterMenuSystem.cs', 'BundledHelicopterAssets.cs', 'HelicopterAssetOwnership.cs']
   .map(name => fileURLToPath(new URL('../Runtime/Transport/' + name, import.meta.url)));
  await writeFile(path.join(dir, 'Test.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${files.map(p => `<Compile Include="${p}"/>`).join('')}</ItemGroup></Project>`);
  await writeFile(path.join(dir, 'Program.cs'), String.raw`
using System; using System.Collections.Generic; using System.Linq;
using Unity.Entities; using Game.Prefabs; using CityWeaver;
namespace Unity.Collections { public enum Allocator { Temp } }
namespace Unity.Entities {
 public record struct Entity(int Id) { public static Entity Null=>new(0); }
 public class ComponentType { public static ComponentType ReadOnly<T>()=>new();public static ComponentType Exclude<T>()=>new(); }
 public class Entities:List<Entity>,IDisposable { public void Dispose(){} }
 public class EntityQuery { public Entities Items=new();public Entities ToEntityArray(Unity.Collections.Allocator a)=>Items; }
 public class Buffer<T>:List<T> { public int Length=>Count; }
 public class EntityManager {
  public Dictionary<(Entity,Type),object> Data=new();
  public void Put<T>(Entity e,T v)=>Data[(e,typeof(T))]=v;
  public bool HasComponent<T>(Entity e)=>Data.ContainsKey((e,typeof(T)));
  public T GetComponentData<T>(Entity e)=>(T)Data[(e,typeof(T))];
  public bool HasBuffer<T>(Entity e)=>HasComponent<Buffer<T>>(e);
  public Buffer<T> GetBuffer<T>(Entity e)=>GetComponentData<Buffer<T>>(e);
 }
 public class World {
  public PrefabSystem Prefabs=new(); public PassengerHelicopterSystem Registration=new();
  public Game.UI.InGame.SelectedInfoUISystem Selected=new();
  public T GetExistingSystemManaged<T>() where T:class => new object[]{Prefabs,Registration,Selected}.OfType<T>().FirstOrDefault();
 }
}
namespace Game {
 public class GameSystemBase {
  public World World=new();public EntityManager EntityManager=new();public EntityQuery Categories=new(),Vehicles=new();private int queries;
  protected virtual void OnCreate(){}protected virtual void OnUpdate(){}
  protected EntityQuery GetEntityQuery(params ComponentType[] types)=>queries++==0?Categories:Vehicles;
  public void Init()=>OnCreate();public void Tick()=>OnUpdate();
 }
}
namespace Game.Common {public struct Created{}public struct Deleted{}}
namespace Game.UI.InGame {public class SelectedInfoUISystem {public void RequestUpdate(){}}}
namespace Game.Prefabs {
 public enum TransportType {Helicopter,Airplane}
 public struct HelicopterData{}public struct PrefabData{}
 public struct PublicTransportVehicleData {public TransportType m_TransportType;}
 public struct UIAssetCategoryData {public Entity m_Menu;}
 public struct UIGroupElement {public Entity m_Prefab;}
 public class UIObject {public string m_Icon;}
 public class PrefabAsset {public AssetId id=new();}public class AssetId {public string guid;}
 public class PrefabBase {
  public string name;public bool isBuiltin,isSubscribedMod;public PrefabAsset asset;
  public Dictionary<Type,object> Components=new();
  public T AddOrGetComponent<T>() where T:new(){if(!Components.TryGetValue(typeof(T),out var v))Components[typeof(T)]=v=new T();return (T)v;}
  public bool TryGet<T>(out T value){bool found=Components.TryGetValue(typeof(T),out var v);value=found?(T)v:default;return found;}
 }
 public class UIAssetCategoryPrefab:PrefabBase{}public class HelicopterPrefab:PrefabBase{}
 public class PrefabSystem {
  public Dictionary<Entity,PrefabBase> Items=new();
  public bool TryGetPrefab<T>(Entity e,out T p) where T:PrefabBase {bool found=Items.TryGetValue(e,out var v);p=v as T;return found;}
  public bool TryGetEntity(PrefabBase p,out Entity e){foreach(var pair in Items)if(ReferenceEquals(pair.Value,p)){e=pair.Key;return true;}e=Entity.Null;return false;}
 }
}
namespace CityWeaver {
 public class PassengerHelicopterSystem {public const string Category="CityWeaver Helicopter Transport",Vehicle="CityWeaver Passenger Helicopter";public string State="registered";public List<PrefabBase> Registered=new();}
 public static class PassengerHelicopterIcons {
  public const string Helicopter="helicopter-icon";public static string ForPrefab(string n)=>n=="CityWeaver_850b7b8e4f8b4a679fa2623a7c3dd3e0"?"airport-icon":null;
 }
}
class Program {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Main(){
  var s=new PassengerHelicopterMenuSystem();s.Init();var em=s.EntityManager;var ps=s.World.Prefabs;
  var menu=new Entity(1);var entries=new Buffer<UIGroupElement>();em.Put(menu,entries);
  UIAssetCategoryPrefab Add(int id,string name,bool subscribed=false,string cid=null,bool builtin=false){
   var e=new Entity(id);var p=new UIAssetCategoryPrefab{name=name,isSubscribedMod=subscribed,isBuiltin=builtin};
   if(cid!=null)p.asset=new PrefabAsset{id=new AssetId{guid=cid}};
   p.AddOrGetComponent<UIObject>().m_Icon="old-icon";ps.Items[e]=p;s.Categories.Items.Add(e);
   em.Put(e,new UIAssetCategoryData{m_Menu=menu});em.Put(e,new Buffer<UIGroupElement>());
   entries.Add(new UIGroupElement{m_Prefab=e});return p;
  }
  var builtin=Add(2,PassengerHelicopterSystem.Category,builtin:true);
  var foreign=Add(3,PassengerHelicopterSystem.Category,true,"ffffffffffffffffffffffffffffffff");
  var wrongName=Add(4,"Another helicopter mod",true,"a07c329a0d2f7ac631f9e46f12ce9065");
  var missingId=Add(5,PassengerHelicopterSystem.Category,true);
  var canonical=Add(6,PassengerHelicopterSystem.Category);s.World.Registration.Registered.Add(canonical);
  var subscribed=Add(7,PassengerHelicopterSystem.Category,true,"a07c329a0d2f7ac631f9e46f12ce9065");
  var oldLocal=Add(8,PassengerHelicopterSystem.Category);
  var airportId=new Entity(20);var airport=new PrefabBase{name="CityWeaver_850b7b8e4f8b4a679fa2623a7c3dd3e0",isSubscribedMod=true,asset=new PrefabAsset{id=new AssetId{guid="2f2dd49bae855f1a7db2f980737b0ac0"}}};
  airport.AddOrGetComponent<UIObject>().m_Icon="old-airport";ps.Items[airportId]=airport;
  var foreignChildId=new Entity(21);var foreignChild=new PrefabBase{name=airport.name,isSubscribedMod=true,asset=new PrefabAsset{id=new AssetId{guid="ffffffffffffffffffffffffffffffff"}}};
  foreignChild.AddOrGetComponent<UIObject>().m_Icon="foreign-icon";ps.Items[foreignChildId]=foreignChild;
  foreach(var id in new[]{6,7})em.GetBuffer<UIGroupElement>(new(id)).Add(new(){m_Prefab=airportId});
  em.GetBuffer<UIGroupElement>(new(6)).Add(new(){m_Prefab=foreignChildId});
  s.World.Registration.State="waiting_for_templates";s.Tick();Check(entries.Count==7,"cleanup ran before registration");
  s.World.Registration.State="registered";s.Tick();
  Check(entries.Select(x=>x.m_Prefab.Id).SequenceEqual(new[]{2,3,4,5,6}),"removed native/foreign/canonical category or retained own duplicate");
  Check(subscribed.AddOrGetComponent<UIObject>().m_Icon==PassengerHelicopterIcons.Helicopter,"subscribed category icon was skipped");
  Check(canonical.AddOrGetComponent<UIObject>().m_Icon==PassengerHelicopterIcons.Helicopter,"canonical icon missing");
  Check(foreign.AddOrGetComponent<UIObject>().m_Icon=="old-icon"&&builtin.AddOrGetComponent<UIObject>().m_Icon=="old-icon","foreign/native category presentation changed");
  Check(airport.AddOrGetComponent<UIObject>().m_Icon=="airport-icon","owned subscribed child icon skipped");
  Check(foreignChild.AddOrGetComponent<UIObject>().m_Icon=="foreign-icon","foreign child icon changed");
  Check(em.GetBuffer<UIGroupElement>(new(7)).Single().m_Prefab==airportId&&ps.Items[new(7)]==subscribed,"saved category/asset identity destroyed");
  s.Tick();Check(entries.Count==5,"cleanup not idempotent");
  // Native dependency loading can append a duplicate after initial registration.
  Add(9,PassengerHelicopterSystem.Category,true,"A07C329A0D2F7AC631F9E46F12CE9065");
  s.Tick();Check(entries.Count==5&&!entries.Any(x=>x.m_Prefab.Id==9),"late subscribed duplicate survived");
  Console.WriteLine("Construction menu subscription regression passed");
 }
}
`);
  assert.match(execFileSync('dotnet', ['run', '--project', path.join(dir, 'Test.csproj'), '-c', 'Release'], {encoding: 'utf8', timeout: 120000}), /Construction menu subscription regression passed/);
 } catch (error) {throw new Error(String(error.stdout || error.message), {cause:error});}
 finally {await rm(dir, {recursive:true, force:true});}
});
