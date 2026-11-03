using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using PublicTransport=Game.Vehicles.PublicTransport;
using TransportStop=Game.Routes.TransportStop;

namespace CityWeaver
{
    // Before native aircraft boarding: a future departure on a non-boarding
    // vehicle would underflow arrivalFrame - departureFrame in RouteUtils.
    public partial class PassengerHelicopterTimingSystem:GameSystemBase
    {
        private EntityQuery m_Lines,m_Vehicles;
        public override int GetUpdateInterval(SystemUpdatePhase phase)=>16;
        public override int GetUpdateOffset(SystemUpdatePhase phase)=>10;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Lines=GetEntityQuery(ComponentType.ReadOnly<TransportLine>(),ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<RouteWaypoint>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            m_Vehicles=GetEntityQuery(ComponentType.ReadOnly<Helicopter>(),ComponentType.ReadOnly<CurrentRoute>(),
                ComponentType.ReadWrite<PublicTransport>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();
            var managed=new Dictionary<Entity,TransportLineData>();var repaired=new HashSet<Entity>();
            EntityManager.CompleteAllTrackedJobs();
            uint now=World.GetExistingSystemManaged<Game.Simulation.SimulationSystem>().frameIndex;
            using(var lines=m_Lines.ToEntityArray(Allocator.Temp))foreach(var line in lines)
            {
                var type=EntityManager.GetComponentData<PrefabRef>(line).m_Prefab;
                if(!ps.TryGetPrefab<TransportLinePrefab>(type,out var prefab)||prefab.name!=PassengerHelicopterSystem.Line||
                    !EntityManager.HasComponent<TransportLineData>(type))continue;
                var data=EntityManager.GetComponentData<TransportLineData>(type);managed[line]=data;
                // Use the full planned circuit as a conservative bound, so valid
                // long routes and ordinary congestion retain their measured times.
                float expected=0;
                if(EntityManager.HasBuffer<RouteSegment>(line))foreach(var segment in EntityManager.GetBuffer<RouteSegment>(line,true))
                    if(EntityManager.HasComponent<Game.Pathfind.PathInformation>(segment.m_Segment))
                        expected+=EntityManager.GetComponentData<Game.Pathfind.PathInformation>(segment.m_Segment).m_Duration;
                using(var stops=EntityManager.GetBuffer<RouteWaypoint>(line,true).ToNativeArray(Allocator.Temp))foreach(var stop in stops)
                {
                    if(!EntityManager.HasComponent<VehicleTiming>(stop.m_Waypoint))continue;
                    var timing=EntityManager.GetComponentData<VehicleTiming>(stop.m_Waypoint);
                    if(!HelicopterTimingRules.InvalidAverage(timing.m_AverageTravelTime,expected))continue;
                    HelicopterRuntime.log.Info("Reset invalid helicopter travel time on "+line+": "+timing.m_AverageTravelTime);
                    // Zero is native 'no observation'; native line calculation
                    // falls back to planned path duration and learns fresh samples.
                    timing.m_AverageTravelTime=0;EntityManager.SetComponentData(stop.m_Waypoint,timing);repaired.Add(line);
                }
            }
            using(var vehicles=m_Vehicles.ToEntityArray(Allocator.Temp))foreach(var vehicle in vehicles)
            {
                var line=EntityManager.GetComponentData<CurrentRoute>(vehicle).m_Route;
                if(!managed.TryGetValue(line,out var data))continue;
                var transport=EntityManager.GetComponentData<PublicTransport>(vehicle);
                bool boarding=(transport.m_State&PublicTransportFlags.Boarding)!=0;
                uint old=transport.m_DepartureFrame;
                if(HelicopterTimingRules.InvalidDeparture(old,now,boarding))transport.m_DepartureFrame=0;
                else if(boarding&&repaired.Contains(line))
                {
                    var waypoint=EntityManager.HasComponent<Target>(vehicle)?EntityManager.GetComponentData<Target>(vehicle).m_Target:Entity.Null;
                    float dwell=data.m_StopDuration;
                    if(EntityManager.HasComponent<Connected>(waypoint))
                    {
                        var stop=EntityManager.GetComponentData<Connected>(waypoint).m_Connected;
                        if(EntityManager.HasComponent<TransportStop>(stop))dwell=RouteUtils.GetStopDuration(data,EntityManager.GetComponentData<TransportStop>(stop));
                    }
                    transport.m_DepartureFrame=HelicopterTimingRules.Shorten(old,now,dwell);
                    if(transport.m_DepartureFrame!=old&&EntityManager.HasComponent<VehicleTiming>(waypoint))
                    {
                        var timing=EntityManager.GetComponentData<VehicleTiming>(waypoint);
                        if(timing.m_LastDepartureFrame==old){timing.m_LastDepartureFrame=transport.m_DepartureFrame;EntityManager.SetComponentData(waypoint,timing);}
                    }
                }
                if(transport.m_DepartureFrame!=old)EntityManager.SetComponentData(vehicle,transport);
            }
        }
    }
}
