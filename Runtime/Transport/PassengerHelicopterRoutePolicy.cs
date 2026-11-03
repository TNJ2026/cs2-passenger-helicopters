using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Unity.Entities;

namespace CityWeaver
{
    internal static class PassengerHelicopterRoutePolicy
    {
        internal static bool Applies(PrefabSystem ps, Entity prefab) =>
            ps.TryGetPrefab<PrefabBase>(prefab, out var p) && p.name == PassengerHelicopterSystem.Line;

        internal static Entity Airport(EntityManager em, Entity stop)
        {
            for (int i = 0; i < 16 && em.Exists(stop); i++)
            {
                if (em.HasComponent<Game.Buildings.Building>(stop)) return stop;
                if (!em.HasComponent<Owner>(stop)) break;
                stop = em.GetComponentData<Owner>(stop).m_Owner;
            }
            return Entity.Null;
        }

        internal static bool IsOrigin(EntityManager em, PrefabSystem ps, Entity airport)
        {
            if (airport == Entity.Null || !em.HasComponent<PrefabRef>(airport) ||
                !ps.TryGetPrefab<PrefabBase>(em.GetComponentData<PrefabRef>(airport), out var p)) return false;
            return p.GetComponent<Game.Prefabs.TransportDepot>() != null &&
                (p.name == PassengerHelicopterSystem.Airport || p.GetComponent<ImportedPassengerHeliport>() != null);
        }

        internal static string Validate(EntityManager em, PrefabSystem ps, Entity prefab, IReadOnlyList<Entity> stops, bool closed)
        {
            if (!Applies(ps, prefab)) return null;
            var airports = new List<Entity>();
            foreach (var stop in stops)
            {
                var airport = Airport(em, stop);
                if (airport == Entity.Null) return "INCOMPATIBLE_TRANSPORT_STOP";
                airports.Add(airport);
            }
            return PassengerHelicopterRules.ValidateRoute(stops, airports,
                airports.Count > 0 && IsOrigin(em, ps, airports[0]), closed);
        }

        internal static List<Entity> Stops(EntityManager em, Entity route)
        {
            var result = new List<Entity>();
            var nodes = em.GetBuffer<RouteWaypoint>(route, true);
            foreach (var node in nodes)
                result.Add(em.HasComponent<Connected>(node.m_Waypoint)
                    ? em.GetComponentData<Connected>(node.m_Waypoint).m_Connected : Entity.Null);
            return result;
        }
    }
}
