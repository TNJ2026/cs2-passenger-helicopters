using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // Keep the street frontage in world space while shortening the lot at the rear.
    public partial class HelipadLotMigrationSystem:GameSystemBase
    {
        private EntityQuery m_Buildings;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Buildings=GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Game.Objects.Transform>(),ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>(),ComponentType.Exclude<HelipadLotVersion>());
        }
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();if(ps==null)return;
            EntityManager.CompleteAllTrackedJobs();
            using(var buildings=m_Buildings.ToEntityArray(Allocator.Temp))foreach(var b in buildings)
            {
                var type=EntityManager.GetComponentData<PrefabRef>(b).m_Prefab;
                if(!ps.TryGetPrefab<BuildingPrefab>(type,out var p)||p==null||p.name!=ImportedHelipadMenuSystem.Station||p.isBuiltin||p.isSubscribedMod||p.m_LotDepth!=4||p.m_LotWidth!=4)continue;
                if(p.m_Meshes?.Length!=4||!p.m_Meshes[0].m_Mesh.name.Contains("76f9b00133cb48b8a26664c92f8615ac"))continue;
                if(!EntityManager.HasBuffer<Game.Objects.SubObject>(b)||!EntityManager.HasBuffer<Game.Net.SubNet>(b))continue;
                var root=EntityManager.GetComponentData<Game.Objects.Transform>(b);var oldRoot=root;
                float3 Local(float3 v)=>math.rotate(math.inverse(oldRoot.m_Rotation),v-oldRoot.m_Position);
                int state=-1;
                foreach(var child in EntityManager.GetBuffer<Game.Objects.SubObject>(b,true)){
                    var e=child.m_SubObject;
                    if(EntityManager.HasComponent<Game.Routes.TransportStop>(e)&&EntityManager.HasComponent<Game.Objects.Transform>(e)){
                        state=HelipadLotRules.Layout(Local(EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position).z);break;
                    }
                }
                if(state<0)continue;
                if(state==0){
                    root.m_Position+=math.rotate(root.m_Rotation,new float3(0,0,4));
                    float3 MapLocal(float3 v)=>new float3(HelipadLotRules.MapX(v.x),v.y,HelipadLotRules.MapZ(v.z));
                    float3 Map(float3 v)=>root.m_Position+math.rotate(root.m_Rotation,MapLocal(Local(v)));
                    using(var children=EntityManager.GetBuffer<Game.Objects.SubObject>(b,true).ToNativeArray(Allocator.Temp))foreach(var child in children){
                        var e=child.m_SubObject;if(!EntityManager.HasComponent<Game.Objects.Transform>(e))continue;
                        var t=EntityManager.GetComponentData<Game.Objects.Transform>(e);t.m_Position=Map(t.m_Position);EntityManager.SetComponentData(e,t);
                        if(EntityManager.HasComponent<Game.Tools.LocalTransformCache>(e)){var cache=EntityManager.GetComponentData<Game.Tools.LocalTransformCache>(e);cache.m_Position=MapLocal(cache.m_Position);EntityManager.SetComponentData(e,cache);}
                        EntityManager.AddComponent<Updated>(e);
                        if(EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(e))using(var routes=EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(e,true).ToNativeArray(Allocator.Temp))foreach(var route in routes)
                            if(EntityManager.Exists(route.m_Waypoint))EntityManager.AddComponent<Updated>(route.m_Waypoint);
                    }
                    using(var nets=EntityManager.GetBuffer<Game.Net.SubNet>(b,true).ToNativeArray(Allocator.Temp))foreach(var sub in nets){
                        var e=sub.m_SubNet;if(EntityManager.HasComponent<Deleted>(e))continue;
                        if(EntityManager.HasComponent<Game.Net.Node>(e)){var n=EntityManager.GetComponentData<Game.Net.Node>(e);n.m_Position=Map(n.m_Position);EntityManager.SetComponentData(e,n);EntityManager.AddComponent<Updated>(e);}
                        if(EntityManager.HasComponent<Game.Net.Curve>(e)){var c=EntityManager.GetComponentData<Game.Net.Curve>(e);var v=c.m_Bezier;c.m_Bezier=new Bezier4x3(Map(v.a),Map(v.b),Map(v.c),Map(v.d));c.m_Length=MathUtils.Length(c.m_Bezier);EntityManager.SetComponentData(e,c);EntityManager.AddComponent<Updated>(e);}
                    }
                    EntityManager.SetComponentData(b,root);EntityManager.AddComponent<Updated>(b);EntityManager.AddComponent<BatchesUpdated>(b);
                }
                EntityManager.AddComponentData(b,new HelipadLotVersion{Version=1});
                HelicopterRuntime.log.Info("Standalone helipad 32x32 lot migration complete; street frontage preserved: "+b);
            }
        }
    }
}
