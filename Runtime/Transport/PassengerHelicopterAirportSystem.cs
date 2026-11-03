using System.Collections.Generic;
using System.Linq;
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
    // Admission supplements native boarding. It never moves an aircraft, edits its path,
    // claims BoardingVehicle, or delays an outgoing/ground aircraft.
    public partial class PassengerHelicopterAirportSystem : GameSystemBase
    {
        private EntityQuery m_Aircraft, m_Stops;
        private readonly Dictionary<Entity, HelicopterAirportQueue<Entity>> m_Queues = new Dictionary<Entity, HelicopterAirportQueue<Entity>>();
        private readonly Dictionary<Entity, (Entity vehicle, float distance, uint frame)> m_Progress = new Dictionary<Entity, (Entity, float, uint)>();
        private readonly Dictionary<Entity,Entity> m_OccupiedPads = new Dictionary<Entity,Entity>();
        internal int HeldApproaches, ActiveAirports;
        private struct Flight
        {
            internal Entity Entity, Stop;
            internal float3 Position;
            internal float Speed, Acceleration;
            internal bool Landing;
        }
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;
        public override int GetUpdateOffset(SystemUpdatePhase phase) => 10;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Aircraft = GetEntityQuery(ComponentType.ReadOnly<Helicopter>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<Moving>(),
                ComponentType.ReadOnly<AircraftCurrentLane>(), ComponentType.ReadWrite<AircraftNavigation>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
            m_Stops = GetEntityQuery(ComponentType.ReadOnly<BoardingVehicle>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            HeldApproaches = ActiveAirports = 0;
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            if (!ps.TryGetPrefab(new PrefabID(nameof(MarkerObjectPrefab), PassengerHelicopterSystem.Stop), out var stopPrefab) ||
                !ps.TryGetPrefab(new PrefabID(nameof(HelicopterPrefab), PassengerHelicopterSystem.Vehicle), out var vehiclePrefab) ||
                !ps.TryGetPrefab(new PrefabID(nameof(TransportLinePrefab), PassengerHelicopterSystem.Line), out var linePrefab)) return;
            var stopType = ps.GetEntity(stopPrefab); var vehicleType = ps.GetEntity(vehiclePrefab);
            var lineType = ps.GetEntity(linePrefab);
            EntityManager.CompleteAllTrackedJobs();
            var pads = new Dictionary<Entity, float3>();
            using (var stops = m_Stops.ToEntityArray(Allocator.Temp))
                foreach (var stop in stops)
                    if (EntityManager.GetComponentData<PrefabRef>(stop).m_Prefab == stopType ||
                        (ps.TryGetPrefab<MarkerObjectPrefab>(EntityManager.GetComponentData<PrefabRef>(stop).m_Prefab, out var ownedStop) &&
                         ownedStop != null && ownedStop.name == PassengerHelicopterSystem.Stop && !ownedStop.isBuiltin && !ownedStop.isSubscribedMod))
                        pads.Add(stop, EntityManager.GetComponentData<Transform>(stop).m_Position);
            var flights = new List<Flight>();
            using (var entities = m_Aircraft.ToEntityArray(Allocator.Temp))
            foreach (var entity in entities)
            {
                // A saved line may select a stock/custom helicopter model. Scope admission
                // by our line as well as our model; third-party routes remain untouched.
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                var route = EntityManager.HasComponent<CurrentRoute>(entity) ? EntityManager.GetComponentData<CurrentRoute>(entity).m_Route : Entity.Null;
                bool ownRoute = EntityManager.HasComponent<PrefabRef>(route) && EntityManager.GetComponentData<PrefabRef>(route).m_Prefab == lineType;
                var target = EntityManager.HasComponent<Target>(entity) ? EntityManager.GetComponentData<Target>(entity).m_Target : Entity.Null;
                var stop = EntityManager.HasComponent<Connected>(target) ? EntityManager.GetComponentData<Connected>(target).m_Connected : Entity.Null;
                var flags = EntityManager.GetComponentData<AircraftCurrentLane>(entity).m_LaneFlags;
                var data = EntityManager.HasComponent<HelicopterData>(prefab) ? EntityManager.GetComponentData<HelicopterData>(prefab) : default;
                flights.Add(new Flight { Entity = entity, Stop = stop,
                    Position = EntityManager.GetComponentData<Transform>(entity).m_Position,
                    Speed = math.length(EntityManager.GetComponentData<Moving>(entity).m_Velocity),
                    Acceleration = data.m_FlyingAcceleration,
                    Landing = HelicopterAirportQueue<Entity>.Managed(prefab == vehicleType, ownRoute, route != Entity.Null) && (flags & (AircraftLaneFlags.Flying | AircraftLaneFlags.Landing)) ==
                        (AircraftLaneFlags.Flying | AircraftLaneFlags.Landing) && (flags & AircraftLaneFlags.SkipLane) == 0 });
            }
            uint frame = World.GetExistingSystemManaged<Game.Simulation.SimulationSystem>().frameIndex;
            var liveAircraft=new HashSet<Entity>(flights.Select(f=>f.Entity));
            foreach(var stale in m_OccupiedPads.Keys.Where(e=>!liveAircraft.Contains(e)).ToArray())m_OccupiedPads.Remove(stale);
            var boardingPads=new Dictionary<Entity,Entity>();
            foreach(var pad in pads)
            {
                var boarding=EntityManager.GetComponentData<BoardingVehicle>(pad.Key).m_Vehicle;
                if(boarding!=Entity.Null)boardingPads[boarding]=pad.Key;
            }
            foreach(var flight in flights)
            {
                var distances=pads.OrderBy(p=>p.Key.Index).Select(p=>(p.Key,math.distance(flight.Position,p.Value))).ToArray();
                var assigned=HelicopterPadOccupancy<Entity>.Select(distances,
                    boardingPads.TryGetValue(flight.Entity,out var boarding)?(Entity?)boarding:null,
                    flight.Landing&&pads.ContainsKey(flight.Stop)?(Entity?)flight.Stop:null,
                    m_OccupiedPads.TryGetValue(flight.Entity,out var prior)?(Entity?)prior:null);
                if(assigned.HasValue)m_OccupiedPads[flight.Entity]=assigned.Value;
                else m_OccupiedPads.Remove(flight.Entity);
            }
            foreach (var stale in m_Queues.Keys.Where(x => !pads.ContainsKey(x)).ToArray())
            { m_Queues.Remove(stale); m_Progress.Remove(stale); }
            foreach (var pad in pads)
            {
                if (!m_Queues.TryGetValue(pad.Key, out var queue))
                    m_Queues.Add(pad.Key, queue = new HelicopterAirportQueue<Entity>());
                var arrivals = flights.Where(f => f.Landing && f.Stop == pad.Key)
                    .OrderBy(f => math.distancesq(f.Position, pad.Value)).ThenBy(f => f.Entity.Index).ToList();
                // Already inside: let native boarding/departure drain the volume, including
                // overlaps loaded from an older save. Do not trap an occupant at the gate.
                var occupants = flights.Where(f => m_OccupiedPads.TryGetValue(f.Entity,out var assigned)&&assigned==pad.Key&&
                    (math.distance(f.Position,pad.Value)<HelicopterPadOccupancy<Entity>.Radius||
                     (boardingPads.TryGetValue(f.Entity,out var boardingPad)&&boardingPad==pad.Key))).Select(f => f.Entity).ToList();
                var admitted = queue.Select(arrivals.Select(f => f.Entity).ToArray(), occupants);
                // A stalled approach outside the occupied volume must not monopolize the
                // queue forever. Yield its place only; never release a physical occupant.
                if (admitted.HasValue)
                {
                    var selected = arrivals.First(f => f.Entity == admitted.Value);
                    float distance = math.distance(selected.Position, pad.Value);
                    if (!m_Progress.TryGetValue(pad.Key, out var progress) || progress.vehicle != selected.Entity ||
                        distance < progress.distance - 5 || frame < progress.frame)
                        m_Progress[pad.Key] = (selected.Entity, distance, frame);
                    else if (distance >= 80 && frame - progress.frame > 3600 && arrivals.Count > 1)
                    {
                        queue.Defer(selected.Entity);
                        admitted = queue.Select(arrivals.Select(f => f.Entity).ToArray(), occupants);
                        m_Progress.Remove(pad.Key);
                    }
                }
                else m_Progress.Remove(pad.Key);
                if (arrivals.Count != 0 || occupants.Count != 0) ActiveAirports++;
                foreach (var flight in arrivals)
                {
                    float distance = math.distance(flight.Position, pad.Value);
                    if (distance < 80 || admitted == flight.Entity) continue;
                    var navigation = EntityManager.GetComponentData<AircraftNavigation>(flight.Entity);
                    float limit = HelicopterAirportQueue<Entity>.HoldSpeed(navigation.m_MaxSpeed, distance, flight.Speed, flight.Acceleration);
                    if (limit < navigation.m_MaxSpeed)
                    {
                        navigation.m_MaxSpeed = limit;
                        EntityManager.SetComponentData(flight.Entity, navigation);
                        HeldApproaches++;
                    }
                }
            }
        }
    }
}
