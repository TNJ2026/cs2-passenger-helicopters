using Game;
using Game.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Colossal.Mathematics;

namespace CityWeaver
{
    // Native waypoint lookup searches only 10 m from the aircraft stop. Our
    // edge waiting paths can be farther away. Bind only this airport's own
    // pedestrian lane at its configured endpoint, after native connections.
    public partial class PassengerHelicopterWaitingConnectionSystem:GameSystemBase
    {
        private EntityQuery m_Waypoints;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Waypoints=GetEntityQuery(ComponentType.ReadOnly<Game.Routes.Waypoint>(),
                ComponentType.ReadOnly<Game.Routes.Connected>(),ComponentType.ReadWrite<Game.Routes.AccessLane>(),
                ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            var waiting=World.GetExistingSystemManaged<PassengerHelicopterWaitingAreaSystem>();if(waiting==null)return;
            EntityManager.CompleteAllTrackedJobs();
            using(var waypoints=m_Waypoints.ToEntityArray(Allocator.Temp))foreach(var waypoint in waypoints)
            {
                var stop=EntityManager.GetComponentData<Game.Routes.Connected>(waypoint).m_Connected;
                if(EntityManager.HasComponent<Deleted>(stop)||!EntityManager.HasComponent<Owner>(stop)||
                    !EntityManager.HasComponent<Game.Objects.Transform>(stop))continue;
                var building=EntityManager.GetComponentData<Owner>(stop).m_Owner;
                if(EntityManager.HasComponent<Deleted>(building)||!EntityManager.HasBuffer<Game.Net.SubNet>(building)||
                    !waiting.TryWaitingPoint(building,EntityManager.GetComponentData<Game.Objects.Transform>(stop).m_Position,out var point))continue;
                Entity best=Entity.Null;float bestT=0,bestDistance=2f;
                var nets=EntityManager.GetBuffer<Game.Net.SubNet>(building,true);
                foreach(var net in nets)
                {
                    if(EntityManager.HasComponent<Deleted>(net.m_SubNet)||!EntityManager.HasBuffer<Game.Net.SubLane>(net.m_SubNet))continue;
                    foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(net.m_SubNet,true))
                    {
                        var lane=item.m_SubLane;
                        if(EntityManager.HasComponent<Deleted>(lane)||!EntityManager.HasComponent<Game.Net.PedestrianLane>(lane)||
                            !EntityManager.HasComponent<Game.Net.Curve>(lane))continue;
                        var curve=EntityManager.GetComponentData<Game.Net.Curve>(lane).m_Bezier;
                        var distance=MathUtils.Distance(curve,point,out float t);
                        if(distance>=bestDistance)continue;
                        bestDistance=distance;best=lane;bestT=t;
                    }
                }
                if(best==Entity.Null)continue; // never connect to another airport or an arbitrary nearby road
                var access=EntityManager.GetComponentData<Game.Routes.AccessLane>(waypoint);
                if(access.m_Lane==best&&math.abs(access.m_CurvePos-bestT)<.001f)continue;
                access.m_Lane=best;access.m_CurvePos=bestT;EntityManager.SetComponentData(waypoint,access);
                EntityManager.AddComponent<PathfindUpdated>(waypoint);
            }
        }
    }
}
