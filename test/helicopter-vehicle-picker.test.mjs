import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('native helicopter picker hides only the redundant available option and preserves fleet selections, other routes and native bindings',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-helicopter-picker-'));
 try{
  const source=fileURLToPath(new URL('../Runtime/Transport/PassengerHelicopterVehiclePickerSystem.cs',import.meta.url));
  await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.Linq;using System.Collections;using System.Collections.Generic;
using CityWeaver;using Unity.Entities;using Unity.Collections;using Game.Prefabs;using Game.UI.InGame;
namespace Unity.Entities{
 public struct Entity{public int Id;public Entity(int id){Id=id;}}
 public class EntityManager{
  public Dictionary<int,Dictionary<Type,object>> Data=new Dictionary<int,Dictionary<Type,object>>();
  public bool HasComponent<T>(Entity e)=>Data.ContainsKey(e.Id)&&Data[e.Id].ContainsKey(typeof(T));
  public T GetComponentData<T>(Entity e)=>(T)Data[e.Id][typeof(T)];
 }
 public class World{public Dictionary<Type,object> Systems=new Dictionary<Type,object>();public T GetExistingSystemManaged<T>() where T:class=>Systems.TryGetValue(typeof(T),out var s)?s as T:null;}
}
namespace Unity.Collections{
 public struct NativeList<T>:IEnumerable<T>{public List<T> Items;public int Length=>Items.Count;public T this[int i]=>Items[i];public void RemoveAt(int i)=>Items.RemoveAt(i);public IEnumerator<T> GetEnumerator()=>Items.GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();}
}
namespace Colossal.UI.Binding{public interface IJsonWriter{}public interface IJsonWritable{void Write(IJsonWriter writer);}}
namespace Game{public class GameSystemBase{public World World=new World();public EntityManager EntityManager=new EntityManager();protected virtual void OnUpdate(){}}}
namespace Game.Common{}
namespace Game.Routes{public struct TransportLine{}}
namespace Game.Prefabs{
 public struct PrefabRef{public Entity m_Prefab;}
 public class PrefabBase{public string name;public bool isBuiltin,isSubscribedMod;}
 public class HelicopterPrefab:PrefabBase{}
 public class TransportLinePrefab:PrefabBase{}
 public class PrefabSystem{public Dictionary<int,PrefabBase> Prefabs=new Dictionary<int,PrefabBase>();public bool TryGetPrefab<T>(Entity e,out T p)where T:PrefabBase{bool found=Prefabs.TryGetValue(e.Id,out var obj);p=obj as T;return found;}}
}
namespace Game.UI.InGame{
 using Colossal.UI.Binding;
 public interface ISectionSource:IJsonWritable{void RequestUpdate();void PerformUpdate();}
 public abstract class InfoSectionBase{protected List<string> tooltipKeys{get;set;}=new List<string>();}
 public class SelectVehiclesSection:InfoSectionBase,ISectionSource{
  private NativeList<Entity> availablePrimaryVehicles{get;set;}=new NativeList<Entity>{Items=new List<Entity>()};
  public List<Entity> Catalogue=new List<Entity>();public List<Entity> Selected=new List<Entity>();public int Writes,Requests,SerializedCount;public bool NativeVisible;
  public int[] AvailableIds=>availablePrimaryVehicles.Items.Select(e=>e.Id).ToArray();public string[] Tooltips=>tooltipKeys.ToArray();
  public void RequestUpdate(){Requests++;}
  public void PerformUpdate(){tooltipKeys.Clear();availablePrimaryVehicles.Items.Clear();availablePrimaryVehicles.Items.AddRange(Catalogue);NativeVisible=availablePrimaryVehicles.Length>1;}
  public void Write(IJsonWriter w){Writes++;SerializedCount=availablePrimaryVehicles.Length;}
 }
 public class SelectedInfoUISystem{
  private List<ISectionSource> m_MiddleSections=new List<ISectionSource>();
  public Entity selectedEntity;public int Requests;public ISectionSource Section=>m_MiddleSections[0];
  public void Init(ISectionSource source){m_MiddleSections.Add(source);}public void RequestUpdate(){Requests++;}
 }
}
namespace CityWeaver{
 public static class PassengerHelicopterSystem{public const string Vehicle="owned",Line="owned-line";}
 public static class HelicopterRuntime{public static Logger log=new Logger();public class Logger{public void Info(string s){}}}
}
class Probe:PassengerHelicopterVehiclePickerSystem{public void Tick()=>OnUpdate();}
class Writer:Colossal.UI.Binding.IJsonWriter{}
class Program{
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static void Main(){
  var system=new Probe();system.Tick(); // UI absent during startup.
  var ps=new PrefabSystem();var info=new SelectedInfoUISystem();var native=new SelectVehiclesSection();info.Init(native);
  system.World.Systems[typeof(PrefabSystem)]=ps;system.World.Systems[typeof(SelectedInfoUISystem)]=info;
  ps.Prefabs[1]=new TransportLinePrefab{name=PassengerHelicopterSystem.Line};
  ps.Prefabs[2]=new TransportLinePrefab{name="Bus Line",isBuiltin=true};
  ps.Prefabs[10]=new HelicopterPrefab{name=PassengerHelicopterSystem.Vehicle};
  ps.Prefabs[11]=new HelicopterPrefab{name="HelicopterPassenger01",isBuiltin=true};
  ps.Prefabs[12]=new HelicopterPrefab{name="third-party"};
  ps.Prefabs[13]=new HelicopterPrefab{name="HelicopterPassenger01",isSubscribedMod=true};
  ps.Prefabs[14]=new PrefabBase{name="wrong-cast"}; // native true + null cast contract
  void Line(int id,int prefab){system.EntityManager.Data[id]=new Dictionary<Type,object>{{typeof(Game.Routes.TransportLine),new Game.Routes.TransportLine()},{typeof(PrefabRef),new PrefabRef{m_Prefab=new Entity(prefab)}}};}
  Line(100,1);Line(200,2);info.selectedEntity=new Entity(100);
  native.Catalogue.AddRange(new[]{10,11,12,13,14}.Select(x=>new Entity(x)));native.Selected.Add(new Entity(11));
  system.Tick();var wrapped=info.Section;Check(!ReferenceEquals(wrapped,native),"Native section wrapped once");
  wrapped.PerformUpdate();Check(native.AvailableIds.SequenceEqual(new[]{10,12,13,14}),"Remove only the redundant native passenger helicopter option");
  Check(native.Selected.Single().Id==11,"An existing line's selected model and in-service fleet remain unchanged");
  Check(native.Tooltips.SequenceEqual(new[]{"CityWeaverHelicopter"}),"Dedicated helicopter help key");
  wrapped.Write(new Writer());Check(native.Writes==1&&native.SerializedCount==4,"Serialize through the original native binding");
  wrapped.RequestUpdate();Check(native.Requests==1,"Native selection refresh remains connected");
  system.Tick();Check(ReferenceEquals(wrapped,info.Section),"Never wrap repeatedly");
  info.selectedEntity=new Entity(200);wrapped.PerformUpdate();Check(native.AvailableIds.Length==5&&native.Tooltips.Length==0,"Other line types remain untouched");
  info.selectedEntity=new Entity(100);native.Catalogue.RemoveAll(x=>x.Id==10);wrapped.PerformUpdate();Check(native.AvailableIds.Contains(11),"Keep native fallback when default model is unavailable");
  native.Catalogue.Clear();native.Catalogue.AddRange(new[]{new Entity(10),new Entity(11)});wrapped.PerformUpdate();
  Check(native.AvailableIds.SequenceEqual(new[]{10})&&native.NativeVisible,"Single remaining model retains the picker and label");
  Console.WriteLine("Helicopter vehicle picker passed");
 }
}
`);
  assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Helicopter vehicle picker passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
