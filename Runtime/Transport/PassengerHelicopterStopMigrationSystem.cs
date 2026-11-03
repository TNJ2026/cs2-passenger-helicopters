using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // Repair saved CityWeaver stops once. Native waypoint connection resolves their
    // access to a real lane; do not synthesize lane links or hide notification icons.
    public partial class PassengerHelicopterStopMigrationSystem : GameSystemBase
    {
        private EntityQuery m_Stops;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Stops = GetEntityQuery(ComponentType.ReadOnly<Game.Routes.TransportStop>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            if (ps == null) return;
            using (var entities = m_Stops.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    // Imported airports serialize their referenced marker as a separate
                    // prefab entity. The name registry alone resolves only the freshly
                    // registered copy, not necessarily the one used by this saved stop.
                    var stopPrefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    if (EntityManager.HasComponent<Created>(stopPrefab) ||
                        !ps.TryGetPrefab<MarkerObjectPrefab>(stopPrefab, out var prefab) ||
                        // Native TryGetPrefab returns true even when its 'as T' cast
                        // produces null for another transport stop prefab type.
                        prefab == null ||
                        prefab.name != PassengerHelicopterSystem.Stop || prefab.isBuiltin ||
                        HelicopterAssetOwnership.RejectForeignSubscription(prefab)) continue;
                    // A repaired save no longer has the instance component, but its
                    // separately serialized prefab may still restore the old data.
                    // Check both sides before deciding this stop has been migrated.
                    if (!EntityManager.HasComponent<Game.Objects.SpawnLocation>(entity) &&
                        !prefab.Has<Game.Prefabs.SpawnLocation>() &&
                        !EntityManager.HasComponent<SpawnLocationData>(stopPrefab)) continue;
                    if (prefab.Has<Game.Prefabs.SpawnLocation>() || EntityManager.HasComponent<SpawnLocationData>(stopPrefab))
                    {
                        prefab.Remove<Game.Prefabs.SpawnLocation>();
                        // Do not queue PrefabSystem.UpdatePrefab here: it recreates owned
                        // stop instances and breaks saved ConnectedRoute references.
                        // Existing instances are migrated in place below; any new instance
                        // carrying the old cached archetype is caught by the same query.
                        // Also remove the serialized runtime data immediately so native
                        // waypoint reconnection cannot choose the obsolete marker shortcut.
                        EntityManager.RemoveComponent<SpawnLocationData>(stopPrefab);
                    }
                    var refresh = new List<Entity> { entity };
                    if (EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(entity))
                    {
                        var routes = EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(entity);
                        for (int i = 0; i < routes.Length; i++) refresh.Add(routes[i].m_Waypoint);
                    }
                    if (EntityManager.HasComponent<Owner>(entity))
                    {
                        var owner = EntityManager.GetComponentData<Owner>(entity).m_Owner;
                        refresh.Add(owner);
                        if (EntityManager.HasBuffer<Game.Buildings.SpawnLocationElement>(owner))
                        {
                            var locations = EntityManager.GetBuffer<Game.Buildings.SpawnLocationElement>(owner);
                            for (int i = locations.Length - 1; i >= 0; i--)
                                if (locations[i].m_SpawnLocation == entity) locations.RemoveAt(i);
                        }
                    }
                    EntityManager.RemoveComponent<Game.Objects.SpawnLocation>(entity);
                    foreach (var target in refresh)
                        if (EntityManager.Exists(target) && !EntityManager.HasComponent<Deleted>(target) &&
                            !EntityManager.HasComponent<Updated>(target)) EntityManager.AddComponent<Updated>(target);
                }
        }
    }
}
