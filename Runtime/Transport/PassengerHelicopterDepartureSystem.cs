using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // Replace only the initial same-airport dispatch path with a native ground
    // path. Native navigation, collision handling, animation and boarding remain
    // responsible for moving the real vehicle; no Transform interpolation/teleport.
    public partial class PassengerHelicopterDepartureSystem:GameSystemBase
    {
        private EntityQuery m_Vehicles;
        private readonly HashSet<Entity> m_Submitted=new HashSet<Entity>();
        private bool GroundOrigin(Entity owner,Entity vehicle,out AircraftCurrentLane result)
        {
            result=default;
            if(!EntityManager.HasBuffer<Game.Net.SubNet>(owner)||!EntityManager.HasComponent<Transform>(vehicle))return false;
            var position=EntityManager.GetComponentData<Transform>(vehicle).m_Position;
            float nearest=2f;Entity selected=Entity.Null;float curvePos=0;
            foreach(var net in EntityManager.GetBuffer<Game.Net.SubNet>(owner,true))
            {
                if(EntityManager.HasComponent<Deleted>(net.m_SubNet)||!EntityManager.HasBuffer<Game.Net.SubLane>(net.m_SubNet))continue;
                foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(net.m_SubNet,true))
                {
                    var candidate=sub.m_SubLane;
                    if(EntityManager.HasComponent<Deleted>(candidate)||!EntityManager.HasComponent<Game.Net.CarLane>(candidate)||
                        !EntityManager.HasComponent<Game.Net.Curve>(candidate)||!EntityManager.HasComponent<PrefabRef>(candidate))continue;
                    var prefab=EntityManager.GetComponentData<PrefabRef>(candidate).m_Prefab;
                    if(!EntityManager.HasComponent<CarLaneData>(prefab)||
                        (EntityManager.GetComponentData<CarLaneData>(prefab).m_RoadTypes&RoadTypes.Helicopter)==0)continue;
                    float distance=Colossal.Mathematics.MathUtils.Distance(EntityManager.GetComponentData<Game.Net.Curve>(candidate).m_Bezier,position,out float t);
                    if(distance>=nearest)continue;
                    nearest=distance;selected=candidate;curvePos=t;
                }
            }
            if(selected==Entity.Null)return false;
            result=new AircraftCurrentLane {m_Lane=selected,m_CurvePosition=new float3(curvePos)};
            return true;
        }
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Vehicles=GetEntityQuery(ComponentType.ReadOnly<Helicopter>(),ComponentType.ReadOnly<CurrentRoute>(),
                ComponentType.ReadOnly<Owner>(),ComponentType.ReadOnly<Odometer>(),ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(),
                ComponentType.ReadWrite<AircraftCurrentLane>(),ComponentType.ReadWrite<PathOwner>(),
                ComponentType.ReadWrite<Target>(),ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();if(ps==null)return;
            EntityManager.CompleteAllTrackedJobs();
            m_Submitted.RemoveWhere(e=>!EntityManager.Exists(e)||EntityManager.HasComponent<Deleted>(e));
            using(var vehicles=m_Vehicles.ToEntityArray(Allocator.Temp))foreach(var entity in vehicles)
            {
                if(m_Submitted.Contains(entity))continue;
                var owner=EntityManager.GetComponentData<Owner>(entity).m_Owner;
                if(!EntityManager.HasComponent<PrefabRef>(owner)||
                    !ps.TryGetPrefab<BuildingPrefab>(EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab,out var airport)||
                    airport.name!=ImportedHelipadMenuSystem.Airport||airport.isBuiltin||HelicopterAssetOwnership.RejectForeignSubscription(airport))continue;
                var route=EntityManager.GetComponentData<CurrentRoute>(entity).m_Route;
                if(!EntityManager.HasComponent<PrefabRef>(route)||!EntityManager.HasBuffer<RouteWaypoint>(route)||
                    !ps.TryGetPrefab<TransportLinePrefab>(EntityManager.GetComponentData<PrefabRef>(route).m_Prefab,out var line)||
                    line.name!=PassengerHelicopterSystem.Line)continue;
                var points=EntityManager.GetBuffer<RouteWaypoint>(route,true);if(points.Length==0)continue;
                var first=points[0].m_Waypoint;
                if(!EntityManager.HasComponent<Connected>(first))continue;
                var stop=EntityManager.GetComponentData<Connected>(first).m_Connected;
                var state=EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(entity).m_State;
                var lane=EntityManager.GetComponentData<AircraftCurrentLane>(entity);
                bool local=EntityManager.HasComponent<Owner>(stop)&&EntityManager.GetComponentData<Owner>(stop).m_Owner==owner;
                if(!HelicopterDepartureRules.Eligible(local,(state&PublicTransportFlags.EnRoute)!=0,
                    (state&PublicTransportFlags.Boarding)!=0,(state&PublicTransportFlags.Returning)!=0,
                    (state&PublicTransportFlags.RouteSource)!=0,(lane.m_LaneFlags&AircraftLaneFlags.Flying)!=0,
                    EntityManager.GetComponentData<Odometer>(entity).m_Distance))continue;
                // Prepare the ground path while the door opens. The movement
                // system holds speed at zero until full clearance, preventing
                // native dispatch from choosing an airborne origin meanwhile.
                var path=EntityManager.GetComponentData<PathOwner>(entity);
                if((path.m_State&(PathFlags.Pending|PathFlags.Scheduled))!=0)continue;
                var prefab=EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if(!EntityManager.HasComponent<AircraftData>(prefab))continue;
                // TransformTarget makes native target seeking choose the airborne
                // map even when Flying is disabled. Start on the colocated owned
                // taxi lane instead. Never move the rendered aircraft position.
                if(!GroundOrigin(owner,entity,out var ground))continue;
                lane=ground;
                var methods=VehicleUtils.GetPathMethods(EntityManager.GetComponentData<AircraftData>(prefab))&~PathMethod.Flying;
                var parameters=new PathfindParameters { m_MaxSpeed=HelicopterDepartureRules.TaxiSpeed,m_WalkSpeed=5.555556f,
                    m_Weights=new PathfindWeights(1,1,1,1),m_Methods=methods,
                    m_IgnoredRules=RuleFlags.ForbidCombustionEngines|RuleFlags.ForbidTransitTraffic|RuleFlags.ForbidPrivateTraffic|RuleFlags.ForbidSlowTraffic|RuleFlags.AvoidBicycles };
                var origin=new SetupQueueTarget {m_Type=SetupTargetType.CurrentLocation,m_Methods=methods,m_RoadTypes=RoadTypes.Helicopter};
                var destination=new SetupQueueTarget {m_Type=SetupTargetType.CurrentLocation,m_Entity=first,m_Methods=methods,m_RoadTypes=RoadTypes.Helicopter};
                // Retire the copied depot-request path before submitting a fresh native path.
                if(EntityManager.HasBuffer<PathElement>(entity))EntityManager.GetBuffer<PathElement>(entity).Clear();
                if(EntityManager.HasBuffer<AircraftNavigationLane>(entity))EntityManager.GetBuffer<AircraftNavigationLane>(entity).Clear();
                path.m_ElementIndex=0;path.m_State=0;
                EntityManager.SetComponentData(entity,new Target(first));
                var queue=World.GetOrCreateSystemManaged<PathfindSetupSystem>().GetQueue(this,64);
                VehicleUtils.SetupPathfind(ref lane,ref path,queue.AsParallelWriter(),new SetupQueueItem(entity,parameters,origin,destination));
                EntityManager.SetComponentData(entity,lane);EntityManager.SetComponentData(entity,path);
                m_Submitted.Add(entity);
                HelicopterRuntime.log.Info("Helicopter ground departure requested: "+entity+" -> "+first);
            }
        }
    }

    // Cap only the initial local dispatch movement; ordinary flights and return
    // trips retain native speeds. The state survives reload as native ECS data.
    public partial class PassengerHelicopterDepartureSpeedSystem:GameSystemBase
    {
        private EntityQuery m_Vehicles;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Vehicles=GetEntityQuery(ComponentType.ReadOnly<Helicopter>(),ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(),ComponentType.ReadOnly<Target>(),
                ComponentType.ReadOnly<AircraftCurrentLane>(),ComponentType.ReadWrite<AircraftNavigation>(),
                ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();if(ps==null)return;
            EntityManager.CompleteAllTrackedJobs();
            using(var entities=m_Vehicles.ToEntityArray(Allocator.Temp))foreach(var entity in entities)
            {
                var state=EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(entity).m_State;
                if((state&PublicTransportFlags.EnRoute)==0||(state&(PublicTransportFlags.RouteSource|PublicTransportFlags.Boarding|PublicTransportFlags.Returning))!=0)continue;
                if((EntityManager.GetComponentData<AircraftCurrentLane>(entity).m_LaneFlags&AircraftLaneFlags.Flying)!=0)continue;
                var owner=EntityManager.GetComponentData<Owner>(entity).m_Owner;
                if(!EntityManager.HasComponent<PrefabRef>(owner)||!ps.TryGetPrefab<BuildingPrefab>(EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab,out var airport)||
                    airport.name!=ImportedHelipadMenuSystem.Airport||airport.isBuiltin||HelicopterAssetOwnership.RejectForeignSubscription(airport))continue;
                if(World.GetExistingSystemManaged<PassengerHelicopterHangarSystem>().Hold(owner,entity))
                {
                    var waiting=EntityManager.GetComponentData<AircraftNavigation>(entity);waiting.m_MaxSpeed=0;
                    EntityManager.SetComponentData(entity,waiting);continue;
                }
                var target=EntityManager.GetComponentData<Target>(entity).m_Target;
                if(!EntityManager.HasComponent<Connected>(target))continue;
                var stop=EntityManager.GetComponentData<Connected>(target).m_Connected;
                if(!EntityManager.HasComponent<Owner>(stop)||EntityManager.GetComponentData<Owner>(stop).m_Owner!=owner)continue;
                var nav=EntityManager.GetComponentData<AircraftNavigation>(entity);
                nav.m_MaxSpeed=math.min(nav.m_MaxSpeed,HelicopterDepartureRules.TaxiSpeed);
                EntityManager.SetComponentData(entity,nav);
            }
        }
    }
}
