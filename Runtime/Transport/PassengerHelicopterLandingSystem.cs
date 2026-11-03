using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Routes;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // Runs after native navigation and pad admission, before native movement.
    // Keep the native path, target and collision avoidance. Only shape the
    // landing speed and permit descent after horizontal capture.
    public partial class PassengerHelicopterLandingSystem : GameSystemBase
    {
        private EntityQuery m_Aircraft;
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;
        public override int GetUpdateOffset(SystemUpdatePhase phase) => 10;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Aircraft = GetEntityQuery(ComponentType.ReadOnly<Helicopter>(),
                ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(), ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<Moving>(),
                ComponentType.ReadOnly<AircraftCurrentLane>(), ComponentType.ReadWrite<AircraftNavigation>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
            RequireForUpdate(m_Aircraft);
        }

        protected override void OnUpdate()
        {
            var prefabs = World.GetExistingSystemManaged<PrefabSystem>();
            if (prefabs == null) return;
            EntityManager.CompleteAllTrackedJobs();
            using (var aircraft = m_Aircraft.ToEntityArray(Allocator.Temp)) foreach (var entity in aircraft)
            {
                var route = EntityManager.HasComponent<CurrentRoute>(entity)
                    ? EntityManager.GetComponentData<CurrentRoute>(entity).m_Route : Entity.Null;
                bool ownRoute = EntityManager.HasComponent<PrefabRef>(route) &&
                    PassengerHelicopterRoutePolicy.Applies(prefabs, EntityManager.GetComponentData<PrefabRef>(route).m_Prefab);
                if (route != Entity.Null && !ownRoute) continue;
                var lane = EntityManager.GetComponentData<AircraftCurrentLane>(entity);
                const AircraftLaneFlags required = AircraftLaneFlags.Flying | AircraftLaneFlags.Landing | AircraftLaneFlags.TransformTarget;
                if ((lane.m_LaneFlags & required) != required ||
                    (lane.m_LaneFlags & (AircraftLaneFlags.Airway | AircraftLaneFlags.TakingOff | AircraftLaneFlags.SkipLane)) != 0 ||
                    !EntityManager.HasComponent<Transform>(lane.m_Lane)) continue;
                var facility = PassengerHelicopterRoutePolicy.Airport(EntityManager, lane.m_Lane);
                if (!EntityManager.HasComponent<PrefabRef>(facility) ||
                    !prefabs.TryGetPrefab<BuildingPrefab>(EntityManager.GetComponentData<PrefabRef>(facility), out var building) ||
                    (building.name != PassengerHelicopterSystem.Airport && building.name != PassengerHelicopterSystem.Station &&
                     building.name != ImportedHelipadMenuSystem.Station && building.GetComponent<ImportedPassengerHeliport>() == null)) continue;
                bool returning = (EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(entity).m_State & PublicTransportFlags.Returning) != 0 &&
                    EntityManager.GetComponentData<Owner>(entity).m_Owner == facility;
                if (!HelicopterLandingRules.Managed(ownRoute, route != Entity.Null, returning)) continue;
                if (EntityManager.HasComponent<Blocker>(entity))
                {
                    var blocker = EntityManager.GetComponentData<Blocker>(entity);
                    if (blocker.m_Blocker != Entity.Null && blocker.m_Type != BlockerType.None) continue;
                }
                var type = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (!EntityManager.HasComponent<HelicopterData>(type)) continue;
                var data = EntityManager.GetComponentData<HelicopterData>(type);
                var position = EntityManager.GetComponentData<Transform>(entity).m_Position;
                var velocity = EntityManager.GetComponentData<Moving>(entity).m_Velocity;
                var navigation = EntityManager.GetComponentData<AircraftNavigation>(entity);
                var delta = navigation.m_TargetPosition - position;
                if (!HelicopterLandingRules.TryLimit(math.length(delta.xz), delta.y, math.length(velocity.xz),
                    data.m_FlyingAcceleration, navigation.m_MaxSpeed, out var limit, out var descend)) continue;
                navigation.m_MaxSpeed = limit; // never raise a native/admission speed limit
                if (descend) navigation.m_MinClimbAngle = math.min(navigation.m_MinClimbAngle, -math.PI / 2);
                EntityManager.SetComponentData(entity, navigation);
            }
        }
    }
}
