using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // In-place visual migration: preserve the airport entity, stops and networks.
    public partial class PassengerHelicopterHangarAssetSystem:GameSystemBase
    {
        internal readonly HashSet<Entity> Types=new HashSet<Entity>();
        private readonly HashSet<Entity> m_BoundBuildings=new HashSet<Entity>();
        private EntityQuery m_Types,m_Buildings,m_Renders;
        private RenderPrefab m_Solid,m_Glass;
        private static string Solid=>ImportedHeliportFootprintSystem.Renders[1];
        private static string Glass=>ImportedHeliportFootprintSystem.Renders[2];
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Types=GetEntityQuery(ComponentType.ReadOnly<BuildingData>(),ComponentType.ReadOnly<PrefabData>(),ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>());
            m_Buildings=GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            m_Renders=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<ProceduralBone>(),ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>());
        }
        protected override void OnUpdate()
        {
            if(!HelicopterModelPolicy.ImportedModelsEnabled){Enabled=false;return;}
            var ps=World.GetExistingSystemManaged<PrefabSystem>();if(ps==null)return;
            // Imported assets have a hash in PrefabID after reload. A name-only
            // PrefabID lookup cannot find them. Resolve the actual ECS prefab,
            // as the asset-inspection API does, then retain that exact object.
            if(m_Solid==null||m_Glass==null)
                using(var renders=m_Renders.ToEntityArray(Allocator.Temp))foreach(var e in renders)
                {
                    if(!ps.TryGetPrefab<RenderPrefab>(e,out var render)||render==null||render.isBuiltin||HelicopterAssetOwnership.RejectForeignSubscription(render))continue;
                    if(render.name==Solid)m_Solid=render;
                    if(render.name==Glass)m_Glass=render;
                }
            var sr=m_Solid;var gr=m_Glass;
            if(!Ready(ps,sr)||!Ready(ps,gr))return;
            EntityManager.CompleteAllTrackedJobs();
            using(var types=m_Types.ToEntityArray(Allocator.Temp))foreach(var type in types)
            {
                if(Types.Contains(type)||!ps.TryGetPrefab<BuildingPrefab>(type,out var p)||p==null||p.name!=ImportedHelipadMenuSystem.Airport||p.isBuiltin||HelicopterAssetOwnership.RejectForeignSubscription(p))continue;
                if((p.m_Meshes?.Length!=6&&p.m_Meshes?.Length!=7)||!EntityManager.HasBuffer<SubMesh>(type))continue;
                var meshes=EntityManager.GetBuffer<SubMesh>(type);if(meshes.Length!=p.m_Meshes.Length)continue;
                // Refuse an unexpected user-edited module, rather than overwrite it.
                if(!p.m_Meshes[1].m_Mesh.name.Contains("CWHeliportPart09 Mesh")&&p.m_Meshes[1].m_Mesh!=sr)continue;
                if(!p.m_Meshes[2].m_Mesh.name.Contains("CWHeliportPart17 Mesh")&&p.m_Meshes[2].m_Mesh!=gr)continue;
                p.m_Meshes[1].m_Mesh=sr;p.m_Meshes[2].m_Mesh=gr;
                var item=meshes[1];item.m_SubMesh=ps.GetEntity(sr);meshes[1]=item;
                item=meshes[2];item.m_SubMesh=ps.GetEntity(gr);meshes[2]=item;
                var data=EntityManager.GetComponentData<ObjectData>(type);
                using(var old=data.m_Archetype.GetComponentTypes())
                {
                    var components=new HashSet<ComponentType>(old.ToArray());
                    components.Add(ComponentType.ReadWrite<Skeleton>());components.Add(ComponentType.ReadWrite<Bone>());components.Add(ComponentType.ReadWrite<BoneHistory>());
                    data.m_Archetype=EntityManager.CreateArchetype(components.ToArray());
                }
                EntityManager.SetComponentData(type,data);Types.Add(type);
                HelicopterRuntime.log.Info("Helicopter hangar folding doors bound without replacing airport/stops: "+type);
            }
            using(var buildings=m_Buildings.ToEntityArray(Allocator.Temp))foreach(var building in buildings)
            {
                if(!Types.Contains(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab)||m_BoundBuildings.Contains(building))continue;
                if(!EntityManager.HasBuffer<Skeleton>(building))EntityManager.AddBuffer<Skeleton>(building);
                if(!EntityManager.HasBuffer<Bone>(building))EntityManager.AddBuffer<Bone>(building);
                if(!EntityManager.HasBuffer<BoneHistory>(building))EntityManager.AddBuffer<BoneHistory>(building);
                // Native pre-culling/InitializeBones allocate the GPU skeleton.
                if(!EntityManager.HasComponent<Updated>(building))EntityManager.AddComponent<Updated>(building);
                m_BoundBuildings.Add(building);
            }
            m_BoundBuildings.RemoveWhere(e=>!EntityManager.Exists(e)||EntityManager.HasComponent<Deleted>(e));
        }
        private bool Ready(PrefabSystem ps,RenderPrefab render)
        {
            if(render==null||render.GetComponent<ProceduralAnimationProperties>()?.m_Bones?.Length!=17||!ps.TryGetEntity(render,out var e))return false;
            return !EntityManager.HasComponent<Created>(e)&&EntityManager.HasBuffer<ProceduralBone>(e)&&EntityManager.GetBuffer<ProceduralBone>(e,true).Length==17;
        }
    }

    // Simulation-owned progress continues offscreen and stops while paused.
    public partial class PassengerHelicopterHangarSystem:GameSystemBase
    {
        private sealed class Door {internal float Progress;internal uint LastOccupied;internal bool Initialized;}
        private readonly Dictionary<Entity,Door> m_Doors=new Dictionary<Entity,Door>();
        private EntityQuery m_Airports,m_Vehicles;private uint m_LastFrame;private bool m_Ticked;
        internal float Progress(Entity owner)=>m_Doors.TryGetValue(owner,out var door)?door.Progress:0;
        internal Newtonsoft.Json.Linq.JArray Status()=>new Newtonsoft.Json.Linq.JArray(m_Doors.Select(p=>new Newtonsoft.Json.Linq.JObject{
            ["airport_index"]=p.Key.Index,["airport_version"]=p.Key.Version,["open_fraction"]=p.Value.Progress,["last_occupied_frame"]=p.Value.LastOccupied}));
        internal bool Managed(Entity owner)=>EntityManager.HasComponent<PrefabRef>(owner)&&
            World.GetExistingSystemManaged<PassengerHelicopterHangarAssetSystem>().Types.Contains(EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab);
        internal float3 Authored(Entity owner,float3 world)
        {
            var t=EntityManager.GetComponentData<Transform>(owner);var local=math.rotate(math.inverse(t.m_Rotation),world-t.m_Position);
            return new float3(local.z+6,local.y,HeliportWidthRules.AuthoredZ(local.x));
        }
        internal bool Hold(Entity owner,Entity vehicle)
        {
            if(!Managed(owner)||!EntityManager.HasComponent<Transform>(vehicle))return false;
            var p=Authored(owner,EntityManager.GetComponentData<Transform>(vehicle).m_Position);
            return HelicopterHangarDoorRules.Occupied(p.x,p.y,p.z)&&HelicopterHangarDoorRules.HoldDeparture(Progress(owner),p.z);
        }
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Airports=GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),ComponentType.ReadOnly<Transform>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            m_Vehicles=GetEntityQuery(ComponentType.ReadOnly<Helicopter>(),ComponentType.ReadOnly<Transform>(),ComponentType.ReadOnly<Owner>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            EntityManager.CompleteAllTrackedJobs();
            uint now=World.GetExistingSystemManaged<SimulationSystem>().frameIndex;
            float dt=m_Ticked?math.min(unchecked(now-m_LastFrame)/60f,1):0;m_Ticked=true;m_LastFrame=now;
            var occupied=new HashSet<Entity>();var movingOut=new HashSet<Entity>();
            using(var vehicles=m_Vehicles.ToEntityArray(Allocator.Temp))foreach(var vehicle in vehicles)
            {
                var owner=EntityManager.GetComponentData<Owner>(vehicle).m_Owner;if(!Managed(owner)||!EntityManager.HasComponent<Transform>(owner))continue;
                var p=Authored(owner,EntityManager.GetComponentData<Transform>(vehicle).m_Position);
                if(!HelicopterHangarDoorRules.Occupied(p.x,p.y,p.z))continue;
                occupied.Add(owner);if(p.z>ImportedHeliportRules.DepotZ+3)movingOut.Add(owner);
            }
            var active=new HashSet<Entity>();
            using(var airports=m_Airports.ToEntityArray(Allocator.Temp))foreach(var airport in airports)
            {
                if(!Managed(airport))continue;active.Add(airport);
                if(!m_Doors.TryGetValue(airport,out var door))m_Doors[airport]=door=new Door();
                if(!door.Initialized){door.Progress=movingOut.Contains(airport)?1:0;door.LastOccupied=now;door.Initialized=true;}
                if(occupied.Contains(airport))door.LastOccupied=now;
                bool open=HelicopterHangarDoorRules.ShouldOpen(door.Progress,occupied.Contains(airport),unchecked(now-door.LastOccupied));
                door.Progress=HelicopterHangarDoorRules.Step(door.Progress,open,dt);
            }
            foreach(var dead in m_Doors.Keys.Where(e=>!active.Contains(e)).ToArray())m_Doors.Remove(dead);
        }
    }

    public partial class PassengerHelicopterHangarAnimationSystem:GameSystemBase
    {
        private EntityQuery m_Airports;
        protected override void OnCreate()
        {
            base.OnCreate();m_Airports=GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadWrite<Skeleton>(),ComponentType.ReadWrite<Bone>(),ComponentType.Exclude<Deleted>());
        }
        protected override void OnUpdate()
        {
            var control=World.GetExistingSystemManaged<PassengerHelicopterHangarSystem>();var ps=World.GetExistingSystemManaged<PrefabSystem>();if(control==null||ps==null)return;
            EntityManager.CompleteAllTrackedJobs();
            using(var airports=m_Airports.ToEntityArray(Allocator.Temp))foreach(var airport in airports)
            {
                if(!control.Managed(airport))continue;
                var meshes=EntityManager.GetBuffer<SubMesh>(EntityManager.GetComponentData<PrefabRef>(airport).m_Prefab,true);
                var skeletons=EntityManager.GetBuffer<Skeleton>(airport);var bones=EntityManager.GetBuffer<Bone>(airport);
                for(int part=1;part<=2&&part<skeletons.Length;part++)
                {
                    var mesh=meshes[part].m_SubMesh;if(!ps.TryGetPrefab<RenderPrefab>(mesh,out var render)||!EntityManager.HasBuffer<ProceduralBone>(mesh))continue;
                    var rig=render.GetComponent<ProceduralAnimationProperties>();var procedural=EntityManager.GetBuffer<ProceduralBone>(mesh,true);
                    var skeleton=skeletons[part];if(skeleton.m_BufferAllocation.Empty)continue;
                    for(int i=0;i<procedural.Length;i++)
                    {
                        int bind=procedural[i].m_BindIndex,offset=skeleton.m_BoneOffset+i;
                        if(bind<0||bind>=rig.m_Bones.Length||offset>=bones.Length)continue;
                        string name=rig.m_Bones[bind].name;
                        if(!name.StartsWith("HangarDoor")||!int.TryParse(name.Substring(10),out int panel))continue;
                        var pose=HeliportWidthRules.DoorPose(panel,control.Progress(airport));var bone=bones[offset];
                        var position=new float3(pose.x,procedural[i].m_Position.y,pose.z);
                        // Preserve hinge-to-hinge length under the Z-axis stretch.
                        var rotation=quaternion.RotateY(pose.angle);
                        skeleton.m_CurrentUpdated|=!bone.m_Position.Equals(position)||!bone.m_Rotation.Equals(rotation);
                        bone.m_Position=position;bone.m_Rotation=rotation;bone.m_Scale=new float3(pose.length,1,1/pose.length);bones[offset]=bone;
                    }
                    skeletons[part]=skeleton;
                }
            }
        }
    }
}
