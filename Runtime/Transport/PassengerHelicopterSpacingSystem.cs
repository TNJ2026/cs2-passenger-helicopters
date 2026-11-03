using System.Collections.Generic;
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
    // Native navigation resets the speed each update. Run between navigation and movement,
    // at their cadence, so a temporary cruise limit never persists after traffic clears.
    public partial class PassengerHelicopterSpacingSystem : GameSystemBase
    {
        private EntityQuery m_Query;
        private struct Flight
        {
            internal Entity Entity;
            internal float3 Position, Velocity;
            internal float2 Direction;
            internal AircraftNavigation Navigation;
        }
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;
        public override int GetUpdateOffset(SystemUpdatePhase phase) => 10;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Query = GetEntityQuery(ComponentType.ReadOnly<Helicopter>(),
                ComponentType.ReadOnly<CurrentRoute>(), ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<Moving>(), ComponentType.ReadOnly<AircraftCurrentLane>(),
                ComponentType.ReadWrite<AircraftNavigation>(), ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Game.Tools.Temp>());
            RequireForUpdate(m_Query);
        }
        protected override void OnUpdate()
        {
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            if (!ps.TryGetPrefab(new PrefabID(nameof(TransportLinePrefab), PassengerHelicopterSystem.Line), out var prefab)) return;
            Entity linePrefab = ps.GetEntity(prefab);
            EntityManager.CompleteAllTrackedJobs();
            var flights = new List<Flight>();
            using (var entities = m_Query.ToEntityArray(Allocator.Temp))
            foreach (var entity in entities)
            {
                Entity route = EntityManager.GetComponentData<CurrentRoute>(entity).m_Route;
                if (!EntityManager.HasComponent<PrefabRef>(route) ||
                    EntityManager.GetComponentData<PrefabRef>(route).m_Prefab != linePrefab) continue;
                var flags = EntityManager.GetComponentData<AircraftCurrentLane>(entity).m_LaneFlags;
                if ((flags & AircraftLaneFlags.Flying) == 0 ||
                    (flags & (AircraftLaneFlags.Landing | AircraftLaneFlags.TakingOff | AircraftLaneFlags.SkipLane)) != 0) continue;
                var position = EntityManager.GetComponentData<Transform>(entity).m_Position;
                var navigation = EntityManager.GetComponentData<AircraftNavigation>(entity);
                var delta = navigation.m_TargetPosition.xz - position.xz;
                if (math.lengthsq(delta) < 1) continue;
                flights.Add(new Flight { Entity = entity, Position = position, Navigation = navigation,
                    Velocity = EntityManager.GetComponentData<Moving>(entity).m_Velocity,
                    Direction = math.normalize(delta) });
            }
            // Compare immutable snapshots so iteration order cannot create mutual braking.
            foreach (var follower in flights)
            {
                var navigation = follower.Navigation;
                foreach (var leader in flights)
                {
                    if (leader.Entity == follower.Entity) continue;
                    var offset = leader.Position - follower.Position;
                    float forward = math.dot(offset.xz, follower.Direction);
                    float lateral = offset.x * follower.Direction.y - offset.z * follower.Direction.x;
                    navigation.m_MaxSpeed = HelicopterSpacingRules.Limit(navigation.m_MaxSpeed,
                        forward, lateral, offset.y, math.dot(follower.Direction, leader.Direction),
                        math.max(0, math.dot(leader.Velocity.xz, follower.Direction)),
                        leader.Entity.Index < follower.Entity.Index);
                }
                if (navigation.m_MaxSpeed != follower.Navigation.m_MaxSpeed)
                    EntityManager.SetComponentData(follower.Entity, navigation);
            }
        }
    }
}
