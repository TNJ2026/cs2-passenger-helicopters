using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // RouteData serializes its width. Repair saved copies in place without rebuilding
    // the prefab or changing route waypoints, vehicles or flight connections.
    public partial class PassengerHelicopterRouteDisplaySystem : GameSystemBase
    {
        private EntityQuery m_Prefabs, m_Instances;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadWrite<RouteData>(), ComponentType.Exclude<Created>());
            m_Instances = GetEntityQuery(ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>(),
                ComponentType.Exclude<PrefabData>());
        }

        protected override void OnUpdate()
        {
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            if (ps == null || !ps.TryGetPrefab(new PrefabID(nameof(TransportLinePrefab), "Bus Line"), out var source) ||
                !(source is TransportLinePrefab bus)) return;
            var sourceEntity = ps.GetEntity(bus);
            if (!EntityManager.HasComponent<RouteData>(sourceEntity)) return;
            var native = EntityManager.GetComponentData<RouteData>(sourceEntity);
            if (native.m_Width <= 0 || native.m_SnapDistance <= 0) return;
            var changed = new HashSet<Entity>();
            using (var entities = m_Prefabs.ToEntityArray(Allocator.Temp))
            foreach (var entity in entities)
            {
                if (!ps.TryGetPrefab<TransportLinePrefab>(entity, out var prefab) || prefab == null ||
                    prefab.name != PassengerHelicopterSystem.Line || prefab.isBuiltin || prefab.isSubscribedMod) continue;
                prefab.m_Width = bus.m_Width;
                prefab.m_SegmentLength = bus.m_SegmentLength;
                var data = EntityManager.GetComponentData<RouteData>(entity);
                if (data.m_Width == native.m_Width && data.m_SnapDistance == native.m_SnapDistance &&
                    data.m_SegmentLength == native.m_SegmentLength) continue;
                data.m_Width = native.m_Width;
                data.m_SnapDistance = native.m_SnapDistance;
                data.m_SegmentLength = native.m_SegmentLength;
                EntityManager.SetComponentData(entity, data);
                changed.Add(entity);
            }
            if (changed.Count == 0) return;
            // Rebuild native segment curves and spatial bounds only when saved dimensions
            // actually changed. Preserve all entity identities and Connected references.
            using (var entities = m_Instances.ToEntityArray(Allocator.Temp))
            foreach (var entity in entities)
            {
                if (!changed.Contains(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab)) continue;
                if (EntityManager.HasComponent<Game.Routes.Route>(entity) ||
                    EntityManager.HasComponent<Game.Routes.Waypoint>(entity) ||
                    EntityManager.HasComponent<Game.Routes.Segment>(entity))
                    EntityManager.AddComponent<Updated>(entity);
            }
        }
    }
}
