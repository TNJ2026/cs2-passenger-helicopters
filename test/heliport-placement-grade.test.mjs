import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('production placement grade lifts a new airport preview as a unit, preserves frontage and ignores existing/foreign buildings',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'heliport-grade-'));
 try{
  const sources=['HeliportPlacementGradeRules.cs','HeliportPlacementGradeSystem.cs'].map(n=>fileURLToPath(new URL('../Runtime/Transport/'+n,import.meta.url)));
  await writeFile(path.join(dir,'Test.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(s=>`<Compile Include="${s}"/>`).join('')}</ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.Collections.Generic;using System.Linq;
using Unity.Entities;using Unity.Mathematics;using Game.Tools;using CityWeaver;
namespace Unity.Collections {public enum Allocator{Temp}}
namespace Unity.Mathematics {
 public struct float3 {public float x,y,z;public float3(float a,float b,float c){x=a;y=b;z=c;}public static float3 operator +(float3 a,float3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);public static float3 operator -(float3 a,float3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);}
 public struct quaternion {public float angle;}
 public static class math {public static quaternion inverse(quaternion q)=>new(){angle=-q.angle}; public static float3 rotate(quaternion q,float3 p)=>new((float)(Math.Cos(q.angle)*p.x+Math.Sin(q.angle)*p.z),p.y,(float)(-Math.Sin(q.angle)*p.x+Math.Cos(q.angle)*p.z));public static float lerp(float a,float b,float t)=>a+(b-a)*t;}
}
namespace Colossal.Mathematics {public struct Bezier4x3 {public float3 a,b,c,d;}public static class MathUtils {public static float Length(Bezier4x3 c){var d=c.d-c.a;return (float)Math.Sqrt(d.x*d.x+d.y*d.y+d.z*d.z);}}}
namespace Unity.Entities {
 public interface IComponentData{} public record struct Entity(int Id){public static Entity Null=>default;}
 public class ComponentType {public Type Type;public bool Excluded;public static ComponentType ReadOnly<T>()=>new(){Type=typeof(T)};public static ComponentType Exclude<T>()=>new(){Type=typeof(T),Excluded=true};}
 public class EntityArray:List<Entity>,IDisposable{public void Dispose(){}}
 public class EntityQuery {public EntityManager Manager; public ComponentType[] Filters;public EntityArray ToEntityArray(Unity.Collections.Allocator a){var r=new EntityArray();r.AddRange(Manager.Live.Where(e=>Filters.All(f=>Manager.Has(e,f.Type)!=f.Excluded)));return r;}}
 public class EntityManager {public HashSet<Entity> Live=new();public Dictionary<(Entity,Type),object> Data=new();public void Put<T>(Entity e,T value){Live.Add(e);Data[(e,typeof(T))]=value;}public bool Has(Entity e,Type t)=>Data.ContainsKey((e,t));public bool HasComponent<T>(Entity e)=>Has(e,typeof(T));public T GetComponentData<T>(Entity e)=>(T)Data[(e,typeof(T))];public void SetComponentData<T>(Entity e,T value)=>Put(e,value);public void AddComponent<T>(Entity e)=>Put(e,Activator.CreateInstance<T>());public void CompleteAllTrackedJobs(){} }
 public class World {public Dictionary<Type,object> Systems=new();public T GetExistingSystemManaged<T>() where T:class=>Systems.TryGetValue(typeof(T),out var s)?(T)s:null;public void Add<T>(T s)=>Systems[typeof(T)]=s;}
}
namespace Game {public enum GameMode{Game,Editor}public class GameSystemBase{public World World=new();public EntityManager EntityManager=new();protected virtual void OnCreate(){}protected virtual void OnUpdate(){}protected EntityQuery GetEntityQuery(params ComponentType[] f)=>new(){Manager=EntityManager,Filters=f};public void Init()=>OnCreate();public void Tick()=>OnUpdate();}}
namespace Game.SceneFlow {public class GameManager {public static GameManager instance=new();public Game.GameMode gameMode=Game.GameMode.Game;}}
namespace Game.Common {public struct Updated{}public struct Deleted{}}
namespace Game.Prefabs {public class PrefabBase{public string name;public bool isBuiltin,Foreign;}public class BuildingPrefab:PrefabBase{public int m_LotWidth=13,m_LotDepth=14;}public class PrefabSystem {public Entity Entity=new(99);public bool TryGetEntity(PrefabBase p,out Entity e){e=Entity;return true;}}}
namespace Game.Simulation {public struct TerrainHeightData {public bool isCreated;}public class TerrainSystem{public bool Available=true;public TerrainHeightData GetHeightData(bool wait)=>new(){isCreated=Available};}public static class TerrainUtils{public static Func<float3,float> Sample;public static List<float3> Positions=new();public static float SampleHeight(ref TerrainHeightData h,float3 p){Positions.Add(p);return Sample(p);}}}
namespace Game.Tools {
 public class ToolSystem {public object activeTool;}
 public class ObjectToolSystem {public enum Mode{Create,Move,Upgrade,Stamp}public Mode actualMode;public Game.Prefabs.PrefabBase Prefab;public Game.Prefabs.PrefabBase GetPrefab()=>Prefab;}
 [Flags] public enum CreationFlags{Delete=4}
 public struct CreationDefinition{public Entity m_Prefab,m_Original,m_Owner;public CreationFlags m_Flags;}
 public struct OwnerDefinition {public Entity m_Prefab;public float3 m_Position;public quaternion m_Rotation;}
 public struct ObjectDefinition{public float3 m_Position,m_LocalPosition;public quaternion m_Rotation;public float m_Elevation;}
 public struct CoursePos{public float3 m_Position;}
 public struct NetCourse{public CoursePos m_StartPosition,m_EndPosition;public Colossal.Mathematics.Bezier4x3 m_Curve;public float m_Length;}
}
namespace CityWeaver {public static class ImportedHelipadMenuSystem{public const string Airport="Airport";}public static class HelicopterAssetOwnership{public static bool RejectForeignSubscription(Game.Prefabs.PrefabBase p)=>p.Foreign;}}
class Program {
 static void Check(bool value,string name){if(!value)throw new Exception(name);}static void Near(float a,float b,string n)=>Check(Math.Abs(a-b)<.001f,n+": "+a+" != "+b);
 static HeliportPlacementGradeSystem Make(float angle=0){
  var s=new HeliportPlacementGradeSystem();s.Init();var p=new Game.Prefabs.BuildingPrefab{name="Airport"};s.World.Add(new ToolSystem{activeTool=new ObjectToolSystem{Prefab=p}});s.World.Add(new Game.Prefabs.PrefabSystem());s.World.Add(new Game.Simulation.TerrainSystem());
  var em=s.EntityManager;var root=new Entity(1);var pos=new float3(100,228.809128f,200);var rot=new quaternion{angle=angle};
  var owner=new OwnerDefinition{m_Prefab=new(99),m_Position=pos,m_Rotation=rot};
  void Def(int id,int prefab){em.Put(new(id),new CreationDefinition{m_Prefab=new(prefab)});em.Put(new(id),new Game.Common.Updated());}
  Def(1,99);em.Put(root,new ObjectDefinition{m_Position=pos,m_LocalPosition=pos,m_Rotation=rot});
  Def(2,200);em.Put(new Entity(2),owner);em.Put(new Entity(2),new ObjectDefinition{m_Position=pos+math.rotate(rot,new(10,.2f,20)),m_LocalPosition=new(10,.2f,20),m_Elevation=.2f});
  Def(3,300);em.Put(new Entity(3),owner);var a=pos+math.rotate(rot,new(-48,0,56));var d=pos+math.rotate(rot,new(-48,0,14));
  em.Put(new Entity(3),new NetCourse{m_StartPosition=new(){m_Position=a},m_EndPosition=new(){m_Position=d},m_Curve=new(){a=a,b=a+(d-a)*(1f/3),c=a+(d-a)*(2f/3),d=d}});
  Def(4,400);em.Put(new Entity(4),new ObjectDefinition{m_Position=new(0,7,0)});
  // Attached external road definitions do not have this airport's OwnerDefinition.
  Def(5,500);em.Put(new Entity(5),new NetCourse{m_Curve=new(){a=new(0,8,0),d=new(10,8,0)}});
  Game.Simulation.TerrainUtils.Positions.Clear();Game.Simulation.TerrainUtils.Sample=_=>231.003525f;return s;
 }
 static void Main(){
  float[] live={230.85022f,231.003525f,231.101578f,230.369125f,231.003525f,231.003525f,230.308228f,230.941025f,231.003525f};
  Near(HeliportPlacementGradeRules.Target(228.809128f,live),231.003525f,"measured city plateau");
  Near(HeliportPlacementGradeRules.Target(235,live),235,"never lower");
  Near(HeliportPlacementGradeRules.Target(220,live),220,"large raise rejected");
  Near(HeliportPlacementGradeRules.Target(230,Enumerable.Range(0,9).Select(i=>230+i*.6f).ToArray()),230,"slope fallback");
  Near(HeliportPlacementGradeRules.Target(230,new[]{230f,230,230,230,230,230,234,234,234}),230,"minority high samples");
  live[0]=float.NaN;Near(HeliportPlacementGradeRules.Target(228,live),228,"invalid terrain");
  foreach(float angle in new[]{0f,1.2f,-1.583f}){
   var s=Make(angle);var em=s.EntityManager;s.Tick();var root=em.GetComponentData<ObjectDefinition>(new(1));Near(root.m_Position.y,231.003525f,"root raised");Near(root.m_LocalPosition.y,root.m_Position.y,"root absolute local position");
   var child=em.GetComponentData<ObjectDefinition>(new(2));Near(child.m_Position.y,231.203525f,"stop raised");Near(child.m_LocalPosition.y,.2f,"stop local position unchanged");Near(child.m_Elevation,.2f,"relative elevation unchanged");
   Near(em.GetComponentData<OwnerDefinition>(new(3)).m_Position.y,root.m_Position.y,"net owner key updated");var net=em.GetComponentData<NetCourse>(new(3));Near(net.m_StartPosition.m_Position.y,228.809128f,"road entrance anchored");Near(net.m_EndPosition.m_Position.y,231.003525f,"interior node raised");Near(net.m_Curve.b.y,228.809128f+(231.003525f-228.809128f)/3,"ramp control point");
   Near(em.GetComponentData<ObjectDefinition>(new(4)).m_Position.y,7,"unrelated object");Near(em.GetComponentData<NetCourse>(new(5)).m_Curve.a.y,8,"external road");
   Check(Game.Simulation.TerrainUtils.Positions.Count==9,"bounded samples");foreach(var p in Game.Simulation.TerrainUtils.Positions){var local=math.rotate(new quaternion{angle=-angle},p-new float3(100,228.809128f,200));Check(Math.Abs(local.x)>71.9f||local.z< -75.9f,"sample outside preview apron");}
   s.Tick();Near(em.GetComponentData<ObjectDefinition>(new(1)).m_Position.y,231.003525f,"idempotent");Check(Game.Simulation.TerrainUtils.Positions.Count==9,"no resampling processed definition");
  }
  for(int mode=0;mode<9;mode++){
   var s=Make();var tool=(ObjectToolSystem)s.World.GetExistingSystemManaged<ToolSystem>().activeTool;
   if(mode==0)tool.actualMode=ObjectToolSystem.Mode.Move;
   if(mode==1)((Game.Prefabs.BuildingPrefab)tool.Prefab).Foreign=true;
   if(mode==2)tool.Prefab.name="Other";
   if(mode==3){var c=s.EntityManager.GetComponentData<CreationDefinition>(new(1));c.m_Original=new(80);s.EntityManager.Put(new Entity(1),c);}
   if(mode==4)Game.SceneFlow.GameManager.instance.gameMode=Game.GameMode.Editor;
   if(mode==5)s.World.GetExistingSystemManaged<Game.Simulation.TerrainSystem>().Available=false;
   if(mode==6){s.EntityManager.Put(new Entity(6),new CreationDefinition());s.EntityManager.Put(new Entity(6),new Game.Common.Updated());s.EntityManager.Put(new Entity(6),s.EntityManager.GetComponentData<OwnerDefinition>(new(2)));}
   if(mode==7){var c=s.EntityManager.GetComponentData<CreationDefinition>(new(2));c.m_Original=new(80);s.EntityManager.Put(new Entity(2),c);}
   if(mode==8){var obj=s.EntityManager.GetComponentData<ObjectDefinition>(new(2));s.EntityManager.Put(new Entity(6),new CreationDefinition());s.EntityManager.Put(new Entity(6),new Game.Common.Updated());s.EntityManager.Put(new Entity(6),new ObjectDefinition());s.EntityManager.Put(new Entity(6),new OwnerDefinition{m_Prefab=new(200),m_Position=obj.m_Position,m_Rotation=obj.m_Rotation});}
   s.Tick();Near(s.EntityManager.GetComponentData<ObjectDefinition>(new(1)).m_Position.y,228.809128f,"fallback "+mode);Near(s.EntityManager.GetComponentData<ObjectDefinition>(new(2)).m_Position.y,229.009128f,"atomic fallback "+mode);Game.SceneFlow.GameManager.instance.gameMode=Game.GameMode.Game;
  }
  Console.WriteLine("Production grade placement passed");
 }
}
`.replace('public static float3 operator +(float3 a,float3 b)', 'public static float3 operator *(float3 a,float b)=>new(a.x*b,a.y*b,a.z*b);public static float3 operator +(float3 a,float3 b)'));
  let output;try{output=execFileSync('dotnet',['run','--project',path.join(dir,'Test.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000});}catch(e){throw new Error(String(e.stdout??'')+String(e.stderr??''));}
  assert.match(output,/Production grade placement passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
