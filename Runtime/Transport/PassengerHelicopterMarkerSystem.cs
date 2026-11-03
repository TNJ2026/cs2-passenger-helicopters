using Game;
using Game.Common;
using Game.Notifications;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // Native MarkerCreateSystem has no Helicopter stop archetype branch. Supply
    // native, raycastable icons instead of masquerading as an airplane stop.
    public partial class PassengerHelicopterMarkerSystem : GameSystemBase
    {
        private EntityQuery m_Stops;
        private EntityQuery m_BuildingMarkers;
        private PrefabSystem m_Prefabs;
        private ToolSystem m_Tools;
        private IconCommandSystem m_Icons;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Tools = World.GetOrCreateSystemManaged<ToolSystem>();
            m_Icons = World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_BuildingMarkers = GetEntityQuery(ComponentType.ReadOnly<BuildingMarkerData>(), ComponentType.ReadOnly<NotificationIconData>());
            m_Stops = GetEntityQuery(new EntityQueryDesc {
                All = new[] { ComponentType.ReadOnly<Game.Routes.TransportStop>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            if (!m_Prefabs.TryGetPrefab(new PrefabID(nameof(NotificationIconPrefab), PassengerHelicopterSystem.StopIcon), out var icon) || icon == null) return;
            var iconType = m_Prefabs.GetEntity(icon);
            var tool = m_Tools.activeTool;
            bool visible = tool != null && (tool.requireStops == TransportType.Helicopter || tool.requireStopIcons);
            // Registered immediately before IconCommandSystem, after native marker
            // creation. Later command-buffer ordering wins over native removal.
            var commands = m_Icons.CreateCommandBuffer();
            using (var markers = m_BuildingMarkers.ToEntityArray(Allocator.Temp))
            using (var entities = m_Stops.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    // Imported airports serialize private copies of the boarding
                    // marker. Compare the owned definition, not its entity against
                    // the newly registered marker used by runtime-built stations.
                    var stopType = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    if (!m_Prefabs.TryGetPrefab<MarkerObjectPrefab>(stopType, out var stop) || stop == null ||
                        stop.name != PassengerHelicopterSystem.Stop || stop.isBuiltin || HelicopterAssetOwnership.RejectForeignSubscription(stop) ||
                        !EntityManager.HasComponent<TransportStopData>(stopType) ||
                        EntityManager.GetComponentData<TransportStopData>(stopType).m_TransportType != TransportType.Helicopter) continue;
                    // Native route previews hide the original and create a Temp
                    // replacement. Keep the original icon (hidden) and provide
                    // the replacement with native Temp linkage for stable picking.
                    bool isTemp = EntityManager.HasComponent<Temp>(entity);
                    bool isHidden = EntityManager.HasComponent<Hidden>(entity);
                    if (visible)
                    {
                        // Hide only the owner's generic station badge while its
                        // individual boarding markers are visible. Native marker
                        // creation restores it when leaving the route tool.
                        // Do not remove operational warnings or other transport icons.
                        if (EntityManager.HasComponent<Owner>(entity))
                        {
                            var station = EntityManager.GetComponentData<Owner>(entity).m_Owner;
                            if (EntityManager.HasComponent<Game.Buildings.TransportStation>(station))
                                foreach (var marker in markers)
                                    if (EntityManager.GetComponentData<BuildingMarkerData>(marker).m_BuildingType == BuildingType.TransportStation)
                                        commands.Remove(station, marker);
                        }
                        // Roof-pad stops may be ground-snapped by native placement.
                        // Lift only their clickable icon above the owning model;
                        // preserve the stop entity and its transport connections.
                        if (EntityManager.HasComponent<Owner>(entity) && EntityManager.HasComponent<Game.Objects.Transform>(entity))
                        {
                            var owner = EntityManager.GetComponentData<Owner>(entity).m_Owner;
                            if (EntityManager.HasComponent<PrefabRef>(owner) && EntityManager.HasComponent<Game.Objects.Transform>(owner))
                            {
                                var ownerType = EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab;
                                if (m_Prefabs.TryGetPrefab<BuildingPrefab>(ownerType, out var building) && building != null &&
                                    building.name == ImportedHelipadMenuSystem.Station && !building.isBuiltin && !building.isSubscribedMod &&
                                    EntityManager.HasComponent<ObjectGeometryData>(ownerType))
                                {
                                    var location = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
                                    var roof = EntityManager.GetComponentData<Game.Objects.Transform>(owner).m_Position.y +
                                        EntityManager.GetComponentData<ObjectGeometryData>(ownerType).m_Bounds.max.y + 1f;
                                    location.y = Unity.Mathematics.math.max(location.y, roof);
                                    commands.Add(entity, iconType, location, IconPriority.Info, IconClusterLayer.Marker,
                                        IconFlags.Unique, isTemp: isTemp, isHidden: isHidden, disallowCluster: true);
                                    continue;
                                }
                            }
                        }
                        commands.Add(entity, iconType, IconPriority.Info, IconClusterLayer.Marker,
                            IconFlags.Unique, isTemp: isTemp, isHidden: isHidden, disallowCluster: true);
                    }
                    else
                        commands.Remove(entity, iconType);
                }
        }
    }
}
