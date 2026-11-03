using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    internal static class TransportStopHealth
    {
        internal static bool Valid(EntityManager em, Entity stop) => stop != Entity.Null &&
            em.Exists(stop) && em.HasComponent<Game.Routes.TransportStop>(stop) &&
            !em.HasComponent<Deleted>(stop) && !em.HasComponent<Game.Tools.Temp>(stop);

        internal static int Missing(EntityManager em, Entity line)
        {
            var waypoints = em.GetBuffer<RouteWaypoint>(line, true);
            int missing = 0;
            foreach (var w in waypoints)
            {
                var stop = em.HasComponent<Connected>(w.m_Waypoint)
                    ? em.GetComponentData<Connected>(w.m_Waypoint).m_Connected : Entity.Null;
                if (!Valid(em, stop)) missing++;
            }
            return missing;
        }
    }

    // Use the same native out-of-service policy as the tested MCP/manual recovery.
    // Never resume automatically: repairing stops does not revoke a user's stop policy.
    public partial class PassengerHelicopterLineSafetySystem : GameSystemBase
    {
        private EntityQuery m_Lines;
        private readonly HelicopterLineSafetyRules<Entity> m_Rules = new HelicopterLineSafetyRules<Entity>();
        internal int InvalidLines, StopRequests;
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 256;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Lines = GetEntityQuery(ComponentType.ReadOnly<TransportLine>(), ComponentType.ReadOnly<Route>(),
                ComponentType.ReadOnly<RouteWaypoint>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>(), ComponentType.Exclude<Created>());
        }
        protected override void OnUpdate()
        {
            InvalidLines = StopRequests = 0;
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            if (!ps.TryGetPrefab(new PrefabID(nameof(TransportLinePrefab), PassengerHelicopterSystem.Line), out var prefab) ||
                !ps.TryGetEntity(prefab, out var ownPrefab)) return;
            EntityManager.CompleteAllTrackedJobs();
            uint frame = World.GetExistingSystemManaged<Game.Simulation.SimulationSystem>().frameIndex;
            var live = new HashSet<Entity>();
            using (var lines = m_Lines.ToEntityArray(Allocator.Temp))
            foreach (var line in lines)
            {
                if (EntityManager.GetComponentData<PrefabRef>(line).m_Prefab != ownPrefab) continue;
                live.Add(line);
                bool invalid = EntityManager.GetBuffer<RouteWaypoint>(line, true).Length < 2 || TransportStopHealth.Missing(EntityManager, line) != 0;
                if (invalid) InvalidLines++;
                bool active = !RouteUtils.CheckOption(EntityManager.GetComponentData<Route>(line), RouteOption.Inactive);
                if (!m_Rules.ShouldStop(line, frame, true, active, invalid)) continue;
                World.GetOrCreateSystemManaged<Game.UI.InGame.TransportationOverviewUISystem>().SetLineState(line, false);
                StopRequests++;
            }
            m_Rules.Retain(live);
        }
    }
}
