using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // Move ONLY pedestrian path endpoints. Aircraft stops, taxiways, ownership
    // and ConnectedRoute identities stay intact. Native BoardingVehicle gates
    // the subsequent pedestrian walk to the stationary aircraft.
    public partial class PassengerHelicopterWaitingAreaSystem:GameSystemBase
    {
        private EntityQuery m_Buildings,m_Residents;
        private readonly Dictionary<Entity,List<(float3 oldPoint,float3 nextPoint)>> m_Layouts=new Dictionary<Entity,List<(float3,float3)>>();
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Buildings=GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>(),ComponentType.Exclude<Created>());
            m_Residents=GetEntityQuery(ComponentType.ReadOnly<Game.Creatures.HumanCurrentLane>(),ComponentType.ReadWrite<Game.Pathfind.PathOwner>(),
                ComponentType.Exclude<Game.Creatures.CurrentVehicle>(),ComponentType.Exclude<Deleted>());
        }
        private static bool Near(float3 a,float3 b)=>math.distance(a.xz,b.xz)<.1f&&math.abs(a.y-b.y)<.5f;
        internal bool TryWaitingPoint(Entity building,float3 stop,out float3 point)
        {
            point=default;
            if(!EntityManager.HasComponent<PrefabRef>(building)||!EntityManager.HasComponent<Game.Objects.Transform>(building))return false;
            var type=EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
            var root=EntityManager.GetComponentData<Game.Objects.Transform>(building);
            var local=math.rotate(math.inverse(root.m_Rotation),stop-root.m_Position);
            var ps=World.GetExistingSystemManaged<PrefabSystem>();
            if(ps!=null&&ps.TryGetPrefab<BuildingPrefab>(type,out var prefab)&&prefab!=null&&prefab.name==ImportedHelipadMenuSystem.Station)
            {point=root.m_Position+math.rotate(root.m_Rotation,HelipadPedestrianSystem.Waiting(local));return true;}
            if(!m_Layouts.TryGetValue(type,out var layout))return false;
            foreach(var item in layout)if(math.distance(local.xz,item.oldPoint.xz)<.2f)
            {point=root.m_Position+math.rotate(root.m_Rotation,item.nextPoint);point.y=stop.y;return true;}
            return false;
        }
        private List<(float3 oldPoint,float3 nextPoint)> Configure(Entity type,BuildingPrefab prefab)
        {
            if(m_Layouts.TryGetValue(type,out var existing))return existing;
            var objects=prefab.GetComponent<ObjectSubObjects>()?.m_SubObjects;
            var nets=prefab.GetComponent<ObjectSubNets>()?.m_SubNets;
            if(objects==null||nets==null||!EntityManager.HasBuffer<Game.Prefabs.SubNet>(type))return null;
            var pads=objects.Where(o=>o.m_Object!=null&&o.m_Object.name==PassengerHelicopterSystem.Stop).ToArray();
            bool airport=prefab.name==ImportedHelipadMenuSystem.Airport;
            if(pads.Length!=(airport?2:1))return null; // wait for the dual-stop migration
            float radius=airport?14f:10f;
            float width=airport?HeliportWidthRules.Factor:1;
            float Distance(float3 a,float3 b)=>math.length(new float2((a.x-b.x)/width,a.z-b.z));
            var changes=new List<(float3 oldPoint,float3 nextPoint)>();
            foreach(var pad in pads)
            foreach(var net in nets.Where(n=>n.m_NetPrefab.name=="Invisible Pedestrian Path"))
            {
                var c=net.m_BezierCurve;
                foreach(bool start in new[]{true,false})
                {
                    var end=start?c.a:c.d;var other=start?c.d:c.a;
                    if(Near(end,pad.m_Position))
                    {
                        var edge=HelicopterWaitingRules.WidthEdge(end.x,end.z,other.x,other.z,radius,width);
                        changes.Add((end,new float3(edge.x,end.y,edge.z)));
                    }
                    else if(math.abs(Distance(end,pad.m_Position)-radius)<.1f&&math.abs(end.y-pad.m_Position.y)<.5f&&
                            Distance(other,pad.m_Position)>radius+.25f)
                        changes.Add((pad.m_Position,end)); // already configured prefab + older city instance
                }
            }
            if(changes.Count!=pads.Length)return null;
            float3 Map(float3 point){foreach(var c in changes)if(Near(point,c.oldPoint))return c.nextPoint;return point;}
            foreach(var net in nets.Where(n=>n.m_NetPrefab.name=="Invisible Pedestrian Path"))
            {
                var c=net.m_BezierCurve;var a=Map(c.a);var d=Map(c.d);
                if(!Near(a,c.a)||!Near(d,c.d))net.m_BezierCurve=new Bezier4x3(a,math.lerp(a,d,1f/3),math.lerp(a,d,2f/3),d);
            }
            var ps=World.GetExistingSystemManaged<PrefabSystem>();
            var runtime=EntityManager.GetBuffer<Game.Prefabs.SubNet>(type);
            for(int i=0;i<runtime.Length;i++)
            {
                var n=runtime[i];
                if(!ps.TryGetPrefab<NetPrefab>(n.m_Prefab,out var net)||net.name!="Invisible Pedestrian Path")continue;
                var a=Map(n.m_Curve.a);var d=Map(n.m_Curve.d);
                if(Near(a,n.m_Curve.a)&&Near(d,n.m_Curve.d))continue;
                n.m_Curve=new Bezier4x3(a,math.lerp(a,d,1f/3),math.lerp(a,d,2f/3),d);runtime[i]=n;
            }
            m_Layouts[type]=changes;return changes;
        }
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();if(ps==null)return;
            EntityManager.CompleteAllTrackedJobs();
            var replan=new HashSet<Entity>();
            using(var buildings=m_Buildings.ToEntityArray(Allocator.Temp))foreach(var building in buildings)
            {
                var type=EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
                if(!World.GetOrCreateSystemManaged<ImportedHeliportFootprintSystem>().Ready(type)||!EntityManager.HasComponent<HelicopterAirportWidth>(building))continue;
                if(!ps.TryGetPrefab<BuildingPrefab>(type,out var prefab)||prefab==null||prefab.isBuiltin||HelicopterAssetOwnership.RejectForeignSubscription(prefab)||
                    prefab.name!=ImportedHelipadMenuSystem.Airport||
                    !EntityManager.HasBuffer<Game.Net.SubNet>(building))continue;
                var changes=Configure(type,prefab);if(changes==null)continue;
                var root=EntityManager.GetComponentData<Game.Objects.Transform>(building);
                float3 Map(float3 point)
                {
                    var local=math.rotate(math.inverse(root.m_Rotation),point-root.m_Position);
                    foreach(var c in changes)if(math.distance(local.xz,c.oldPoint.xz)<.1f)
                    {var next=root.m_Position+math.rotate(root.m_Rotation,c.nextPoint);next.y=point.y;return next;}
                    return point;
                }
                bool changed=false;
                using(var subnets=EntityManager.GetBuffer<Game.Net.SubNet>(building,true).ToNativeArray(Allocator.Temp))foreach(var sub in subnets)
                {
                    var entity=sub.m_SubNet;
                    if(EntityManager.HasComponent<Deleted>(entity)||!EntityManager.HasComponent<PrefabRef>(entity)||
                        !ps.TryGetPrefab<NetPrefab>(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab,out var net)||net.name!="Invisible Pedestrian Path")continue;
                    if(EntityManager.HasComponent<Game.Net.Node>(entity))
                    {
                        var node=EntityManager.GetComponentData<Game.Net.Node>(entity);var next=Map(node.m_Position);
                        if(math.distance(next,node.m_Position)<.01f)continue;
                        node.m_Position=next;EntityManager.SetComponentData(entity,node);
                    }
                    else if(EntityManager.HasComponent<Game.Net.Curve>(entity))
                    {
                        var curve=EntityManager.GetComponentData<Game.Net.Curve>(entity);var a=Map(curve.m_Bezier.a);var d=Map(curve.m_Bezier.d);
                        if(math.distance(a,curve.m_Bezier.a)<.01f&&math.distance(d,curve.m_Bezier.d)<.01f)continue;
                        curve.m_Bezier=new Bezier4x3(a,math.lerp(a,d,1f/3),math.lerp(a,d,2f/3),d);curve.m_Length=MathUtils.Length(curve.m_Bezier);
                        EntityManager.SetComponentData(entity,curve);
                    }
                    else continue;
                    EntityManager.AddComponent<Updated>(entity);changed=true;
                }
                if(!changed||!EntityManager.HasBuffer<Game.Objects.SubObject>(building))continue;
                using(var children=EntityManager.GetBuffer<Game.Objects.SubObject>(building,true).ToNativeArray(Allocator.Temp))foreach(var child in children)
                {
                    if(!EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(child.m_SubObject))continue;
                    replan.Add(child.m_SubObject);EntityManager.AddComponent<Updated>(child.m_SubObject);
                    using(var routes=EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(child.m_SubObject,true).ToNativeArray(Allocator.Temp))foreach(var route in routes)
                    {replan.Add(route.m_Waypoint);EntityManager.AddComponent<Updated>(route.m_Waypoint);}
                }
                HelicopterRuntime.log.Info("Helicopter waiting paths moved to pad edge: "+building);
            }
            if(replan.Count==0)return;
            using(var residents=m_Residents.ToEntityArray(Allocator.Temp))foreach(var resident in residents)
            {
                var lane=EntityManager.GetComponentData<Game.Creatures.HumanCurrentLane>(resident);
                if(!replan.Contains(lane.m_QueueEntity))continue;
                var path=EntityManager.GetComponentData<Game.Pathfind.PathOwner>(resident);path.m_State|=Game.Pathfind.PathFlags.Obsolete;
                EntityManager.SetComponentData(resident,path); // native reroute, never teleport passengers
            }
        }
    }
}
