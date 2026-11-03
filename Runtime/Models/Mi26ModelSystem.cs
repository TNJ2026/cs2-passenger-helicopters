using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver.HelicopterModels
{
    // Reapply the bundled default to real managed prefabs on each world load.
    // Keep source GUIDs, authored rotor rig, native color bindings and route IDs.
    public partial class Mi26ModelSystem : GameSystemBase
    {
        private EntityQuery m_Renders,m_Vehicles;
        private readonly HashSet<VehiclePrefab> m_Applied=new HashSet<VehiclePrefab>();
        private readonly HashSet<VehiclePrefab> m_Failed=new HashSet<VehiclePrefab>();
        private readonly Dictionary<VehiclePrefab,Tuple<HelicopterModelReferencesSystem.Pending,DateTime>> m_Pending=new Dictionary<VehiclePrefab,Tuple<HelicopterModelReferencesSystem.Pending,DateTime>>();
        private readonly Dictionary<VehiclePrefab,Tuple<ObjectMeshInfo[],ObjectMeshInfo[]>> m_Originals=new Dictionary<VehiclePrefab,Tuple<ObjectMeshInfo[],ObjectMeshInfo[]>>();
        private GameManager.EventGamePreload m_Preload;
        private JObject m_Part;
        public string State { get; private set; }="waiting_for_assets";
        public string Error { get; private set; }
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Renders=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<MeshData>(),ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>());
            m_Vehicles=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<HelicopterData>(),ComponentType.ReadOnly<PublicTransportVehicleData>(),ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>());
            m_Part=(JObject)HelicopterModelDefaults.Load().Get(PassengerHelicopterSystem.Vehicle)["parts"][0];
            m_Preload=(purpose,mode)=>ResetForLoad();
            GameManager.instance.onGamePreload+=m_Preload;
        }
        protected override void OnDestroy()
        {
            if(GameManager.instance!=null&&m_Preload!=null)GameManager.instance.onGamePreload-=m_Preload;
            base.OnDestroy();
        }
        private void ResetForLoad()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();
            var references=World.GetOrCreateSystemManaged<HelicopterModelReferencesSystem>();
            references.ResetForLoad();
            World.GetOrCreateSystemManaged<HelicopterNativeMotionBlurSystem>().ResetForLoad();
            World.GetOrCreateSystemManaged<HelicopterRotorExposureSystem>().ResetForLoad();
            foreach(var record in m_Originals)
            {
                // Only restore the replacement this system owns. An explicit
                // authoring override is restored by its own optional adapter.
                if(record.Key==null||record.Key.m_Meshes!=record.Value.Item2||ps==null)continue;
                try
                {
                    var before=ps.GetEntity(record.Key);record.Key.m_Meshes=record.Value.Item1;
                    references.Track(record.Key,before);ps.UpdatePrefab(record.Key);
                }
                catch(Exception e){HelicopterRuntime.log.Error(e,"Mi-26 preload restoration failed");}
            }
            m_Originals.Clear();m_Applied.Clear();m_Failed.Clear();m_Pending.Clear();
            State="waiting_for_assets";Error=null;
        }
        protected override void OnUpdate()
        {
            var gm=GameManager.instance;
            if(!HelicopterModelPolicy.ImportedModelsEnabled||gm==null||gm.isGameLoading||gm.gameMode!=GameMode.Game)return;
            var ps=World.GetExistingSystemManaged<PrefabSystem>();if(ps==null)return;
            var references=World.GetOrCreateSystemManaged<HelicopterModelReferencesSystem>();
            foreach(var entry in m_Pending.ToArray())
            {
                var type=ps.GetEntity(entry.Key);
                if(type!=Entity.Null&&EntityManager.Exists(type)&&!EntityManager.HasComponent<Created>(type)&&
                    EntityManager.HasBuffer<SubMesh>(type)&&references.Verified(entry.Value.Item1,type))
                {
                    var meshes=EntityManager.GetBuffer<SubMesh>(type,true);
                    if(meshes.Length==1&&entry.Key.m_Meshes?.Length==1&&meshes[0].m_SubMesh==ps.GetEntity(entry.Key.m_Meshes[0].m_Mesh))
                    {m_Pending.Remove(entry.Key);State=m_Pending.Count==0?"verified":"applying";continue;}
                }
                if(DateTime.UtcNow>entry.Value.Item2)
                {m_Pending.Remove(entry.Key);State="verification_failed";Error="Native Mi-26 model replacement was not verified; automatic reapply is disabled.";HelicopterRuntime.log.Error(Error);}
            }
            RenderPrefab source=null;
            using(var renders=m_Renders.ToEntityArray(Allocator.Temp))foreach(var entity in renders)
            {
                if(ps.TryGetPrefab<RenderPrefab>(entity,out var candidate)&&candidate!=null&&candidate.name==(string)m_Part["render_prefab"]&&candidate.geometryAsset!=null&&!candidate.isImpostor)
                    source=candidate;
            }
            if(source==null)return;
            EntityManager.CompleteAllTrackedJobs();
            using(var vehicles=m_Vehicles.ToEntityArray(Allocator.Temp))foreach(var entity in vehicles)
            {
                if(!ps.TryGetPrefab<HelicopterPrefab>(entity,out var vehicle)||vehicle==null||vehicle.isSubscribedMod||
                    !HelicopterModelPolicy.IsHelicopter(vehicle.name)||m_Applied.Contains(vehicle)||m_Failed.Contains(vehicle))continue;
                if(vehicle.m_Meshes==null||vehicle.m_Meshes.Length==0)continue;
                var saved=World.GetOrCreateSystemManaged<VehicleModelPersistenceSystem>().Data;
                if(saved.DefaultDisabled(vehicle.name))continue;
                var configuration=saved.Get(vehicle.name)??new JObject{["parts"]=new JArray(m_Part.DeepClone())};
                // Preserve advanced overrides authored through the optional MCP
                // editor. Its generic restoration service owns those settings.
                if(!HelicopterRuntime.OwnsModelConfiguration(configuration))continue;
                var modelPart=(JObject)configuration["parts"][0];
                // Do not mutate an in-progress native import or placement preview.
                var tool=World.GetExistingSystemManaged<Game.Tools.ToolSystem>();
                if(tool!=null&&!(tool.activeTool is Game.Tools.DefaultToolSystem))return;
                try
                {
                    var render=HelicopterModelLoader.Prepare(source,(JObject)modelPart["route_color"]);
                    if(!ps.AddPrefab(render))throw new InvalidOperationException("Could not register the independent Mi-26 render.");
                    var original=vehicle.m_Meshes;var part=original[0];
                    var position=modelPart["position"] as JArray;
                    var rotation=modelPart["rotation_degrees"] as JArray;
                    var primary=new ObjectMeshInfo{m_Mesh=render,
                        m_Position=position==null?part.m_Position:new Unity.Mathematics.float3((float)position[0],(float)position[1],(float)position[2]),
                        m_Rotation=rotation==null?part.m_Rotation:(Unity.Mathematics.quaternion)UnityEngine.Quaternion.Euler((float)rotation[0],(float)rotation[1],(float)rotation[2]),
                        m_RequireState=part.m_RequireState};
                    vehicle.m_Meshes=new[]{primary};
                    World.GetOrCreateSystemManaged<HelicopterNativeMotionBlurSystem>().Track(render);
                    World.GetOrCreateSystemManaged<HelicopterRotorExposureSystem>().Track(vehicle,render);
                    m_Originals[vehicle]=Tuple.Create(original,vehicle.m_Meshes);
                    var pending=references.Track(vehicle,ps.GetEntity(vehicle));
                    m_Pending[vehicle]=Tuple.Create(pending,DateTime.UtcNow.AddSeconds(30));
                    ps.UpdatePrefab(vehicle);m_Applied.Add(vehicle);
                    saved.Put(vehicle.name,configuration);
                    State="applying";Error=null;
                    HelicopterRuntime.log.Info("Independent Mil Mi-26TC model and native route colors applied: "+vehicle.name);
                }
                catch(Exception e)
                {
                    // A dispatch might already have changed native prefab state. Never
                    // clone and apply again blindly after an uncertain result.
                    m_Failed.Add(vehicle);State="failed";Error=e.Message;
                    HelicopterRuntime.log.Error(e,"Independent Mi-26 model initialization failed");
                }
                return; // One native prefab update at a time.
            }
        }
    }
}
