using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // Repair this mod's airport/standalone pad permanent takeoff connections,
    // after native connection lookup and before the native path graph is updated.
    public partial class HelipadAirwayConnectionSystem : GameSystemBase
    {
        private EntityQuery m_Markers, m_Routes;
        private readonly HashSet<Entity> m_Logged = new HashSet<Entity>();
        public int RepairsApplied { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Markers = GetEntityQuery(ComponentType.ReadOnly<Game.Routes.TakeoffLocation>(),
                ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<AccessLane>(),
                ComponentType.ReadWrite<RouteLane>(), ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Game.Tools.Temp>());
            m_Routes = GetEntityQuery(ComponentType.ReadOnly<RouteWaypoint>(),
                ComponentType.ReadOnly<RouteSegment>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            if (m_Markers.IsEmptyIgnoreFilter) return;
            EntityManager.CompleteAllTrackedJobs();
            var prefabs = World.GetExistingSystemManaged<PrefabSystem>();
            var groups = new Dictionary<(Entity building, Entity air), List<Entity>>();
            using (var markers = m_Markers.ToEntityArray(Allocator.Temp)) foreach (var marker in markers)
            {
                var building = PassengerHelicopterRoutePolicy.Airport(EntityManager, marker);
                if (building == Entity.Null || !EntityManager.HasComponent<PrefabRef>(building) ||
                    !prefabs.TryGetPrefab<BuildingPrefab>(EntityManager.GetComponentData<PrefabRef>(building), out var prefab) ||
                    (prefab.name != ImportedHelipadMenuSystem.Station && prefab.name != PassengerHelicopterSystem.Airport &&
                     prefab.name != PassengerHelicopterSystem.Station && prefab.GetComponent<ImportedPassengerHeliport>() == null)) continue;
                var access = EntityManager.GetComponentData<AccessLane>(marker);
                var route = EntityManager.GetComponentData<RouteLane>(marker);
                if (access.m_Lane == Entity.Null || route.m_EndLane == Entity.Null || route.m_StartLane != route.m_EndLane ||
                    !EntityManager.HasComponent<Game.Net.CarLane>(access.m_Lane) ||
                    EntityManager.HasComponent<Deleted>(access.m_Lane) || EntityManager.HasComponent<Deleted>(route.m_EndLane) ||
                    !EntityManager.HasComponent<Game.Net.Curve>(route.m_EndLane)) continue;
                if (PassengerHelicopterRoutePolicy.Airport(EntityManager, access.m_Lane) != building ||
                    !EntityManager.HasComponent<Game.Net.ConnectionLane>(route.m_EndLane)) continue;
                var airway = EntityManager.GetComponentData<Game.Net.ConnectionLane>(route.m_EndLane);
                if ((airway.m_Flags & Game.Net.ConnectionLaneFlags.Airway) == 0 ||
                    (airway.m_RoadTypes & Game.Net.RoadTypes.Helicopter) == 0) continue;
                var key = (building, route.m_EndLane);
                if (!groups.TryGetValue(key, out var pair)) groups[key] = pair = new List<Entity>();
                pair.Add(marker);
            }
            var repaired = new HashSet<Entity>();
            foreach (var group in groups)
            {
                if (group.Value.Count < 2 || group.Value.Count > 64) continue;
                var markers = group.Value;
                var lanes = new RouteLane[markers.Count];
                var positions = new float[markers.Count];
                for (int i = 0; i < markers.Count; i++)
                { lanes[i] = EntityManager.GetComponentData<RouteLane>(markers[i]); positions[i] = lanes[i].m_EndCurvePos; }
                float length = EntityManager.GetComponentData<Game.Net.Curve>(group.Key.air).m_Length;
                if (!HelipadAirwayRules.TrySeparateAll(positions, length, out var adjusted)) continue;
                for (int i = 0; i < markers.Count; i++)
                {
                    if (positions[i] == adjusted[i]) continue;
                    var lane = lanes[i];
                    lane.m_StartCurvePos = lane.m_EndCurvePos = adjusted[i];
                    EntityManager.SetComponentData(markers[i], lane);
                    EntityManager.AddComponent<PathfindUpdated>(markers[i]);
                }
                repaired.Add(group.Key.building);
                RepairsApplied++;
                if (m_Logged.Add(group.Key.building))
                    HelicopterRuntime.log.Info("Passenger helicopter airway connections separated: " + group.Key.building + " positions=" + string.Join(",", adjusted));
            }
            if (repaired.Count > 0) Replan(repaired, prefabs);
        }

        private void Replan(HashSet<Entity> buildings, PrefabSystem prefabs)
        {
            // Invalidate only this mod's routes serving a repaired pad, including
            // a currently open preview. Let native RoutePathSystem rebuild them.
            using (var routes = m_Routes.ToEntityArray(Allocator.Temp)) foreach (var route in routes)
            {
                if (!prefabs.TryGetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(route), out var prefab) ||
                    prefab.name != PassengerHelicopterSystem.Line) continue;
                bool affected = false;
                var points = EntityManager.GetBuffer<RouteWaypoint>(route, true);
                foreach (var point in points)
                    if (EntityManager.HasComponent<Connected>(point.m_Waypoint) &&
                        buildings.Contains(PassengerHelicopterRoutePolicy.Airport(EntityManager,
                            EntityManager.GetComponentData<Connected>(point.m_Waypoint).m_Connected))) { affected = true; break; }
                if (!affected) continue;
                using (var segments = EntityManager.GetBuffer<RouteSegment>(route, true).ToNativeArray(Allocator.Temp))
                foreach (var item in segments)
                {
                    var segment = item.m_Segment;
                    if (EntityManager.HasComponent<Deleted>(segment) || !EntityManager.HasComponent<PathTargets>(segment)) continue;
                    EntityManager.SetComponentData(segment, new PathTargets());
                    EntityManager.AddComponent<Updated>(segment);
                }
            }
        }
    }
}
