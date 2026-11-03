using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CityWeaver
{
    // Stable, mod-owned names are registered before save deserialization. Only game assets
    // are used as templates; the reference Heliport mod is never a dependency.
    public partial class PassengerHelicopterSystem : GameSystemBase
    {
        internal const string Airport = "CityWeaver Passenger Heliport";
        internal const string Station = "CityWeaver Helicopter Station";
        internal const string Stop = "CityWeaver Helicopter Boarding Stop";
        internal const string Vehicle = "CityWeaver Passenger Helicopter";
        internal const string Line = "CityWeaver Helicopter Line";
        internal const string Category = "CityWeaver Helicopter Transport";
        internal const string StopIcon = "CityWeaver Helicopter Stop Icon";
        internal const string Icon = PassengerHelicopterIcons.Helicopter;
        internal string State = "waiting_for_templates", Error;
        internal readonly List<PrefabBase> Registered = new List<PrefabBase>();
        private PrefabSystem m_Prefabs;
        private EntityQuery m_StopIcons;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_StopIcons = GetEntityQuery(ComponentType.ReadOnly<TransportStopMarkerData>(), ComponentType.ReadOnly<PrefabData>());
        }

        private T Find<T>(string name) where T : PrefabBase
        {
            if (!m_Prefabs.TryGetPrefab(new PrefabID(typeof(T).Name, name), out var p)) return null;
            return p as T;
        }

        protected override void OnUpdate()
        {
            var building = Find<BuildingPrefab>("FirefightingHelicoptersDepot01");
            var aircraft = Find<HelicopterPrefab>("HelicopterPassenger01");
            var airplaneLine = Find<TransportLinePrefab>("Passenger Airplane Line");
            var airGroup = Find<UIAssetCategoryPrefab>("TransportationAir");
            var sourceStop = Find<MarkerObjectPrefab>("Integrated Passenger Airplane Stop");
            var pedestrianPath = Find<PathwayPrefab>("Invisible Pedestrian Path");
            var takeoff = Find<MarkerObjectPrefab>("Helicopter Takeoff Location");
            var taxiway = Find<TaxiwayPrefab>("Invisible Helicopter Taxiway");
            var helipad = Find<MarkerObjectPrefab>("Integrated Helipad");
            var service = Find<ServicePrefab>("Transportation");
            var busLine = Find<TransportLinePrefab>("Bus Line");
            var config = Find<UITransportConfigurationPrefab>("UI Transport Configuration");
            if (new PrefabBase[] { building, aircraft, airplaneLine, airGroup, sourceStop,
                pedestrianPath, takeoff, taxiway, helipad, service, busLine, config }.Any(p => p == null)) return;
            var sourceIcon = FindStopIcon();
            if (sourceIcon == null) return;
            try
            {
                var group = Copy(airGroup, Category);
                var stopIcon = Copy(sourceIcon, StopIcon);
                try { stopIcon.m_Icon = PassengerHelicopterIcons.CreateStopMarker(sourceIcon.m_Icon); }
                catch (Exception ex) { HelicopterRuntime.log.Error(ex, "Helicopter stop marker texture unavailable; preserving native marker"); }
                stopIcon.AddOrGetComponent<TransportStopMarker>().m_TransportType = TransportType.Helicopter;
                stopIcon.m_EnabledByDefault = true;
                group.AddOrGetComponent<UIObject>().m_Icon = Icon;
                var vehicle = Copy(aircraft, Vehicle);
                // The line picker renders UIObject.m_Icon before trying a dynamic
                // thumbnail, which is unavailable for some saved private copies.
                vehicle.AddOrGetComponent<UIObject>().m_Icon = Icon;
                var privateMeshes = new List<PrefabBase>();
                for (int i = 0; i < vehicle.m_Meshes.Length; i++)
                {
                    var mesh = Copy(vehicle.m_Meshes[i].m_Mesh, Vehicle + " Mesh " + i);
                    vehicle.m_Meshes[i].m_Mesh = mesh;
                    privateMeshes.Add(mesh);
                }
                vehicle.m_SizeClass = Game.Vehicles.SizeClass.Small;
                var transport = vehicle.AddOrGetComponent<PublicTransport>();
                transport.m_TransportType = TransportType.Helicopter;
                transport.m_PassengerCapacity = PassengerHelicopterRules.PassengerCapacity;
                transport.m_Purposes = PublicTransportPurpose.TransportLine;
                transport.m_MaintenanceRange = 1000;

                var stop = Copy(sourceStop, Stop);
                ConfigureStop(stop.AddOrGetComponent<Game.Prefabs.TransportStop>());
                stop.m_Mesh = takeoff.m_Mesh;
                // A stop with a matching pedestrian SpawnLocation and no activity mask
                // makes native waypoints target the marker itself, while native spawn
                // connection deliberately leaves it disconnected. Let WaypointConnectionSystem
                // find the actual pedestrian lane beside this transport stop instead.
                stop.Remove<SpawnLocation>();

                var airport = MakeAirport(building, Airport, true, group, service, stop, pedestrianPath, takeoff, taxiway, helipad);
                var station = MakeAirport(building, Station, false, group, service, stop, pedestrianPath, takeoff, taxiway, helipad);
                var line = Copy(airplaneLine, Line);
                line.m_LocaleID = "CityWeaver.RouteName";
                // Airplane overlays and waypoint hit spheres span adjacent helipads.
                // Keep aircraft routing, but use the native local-transit display scale.
                line.m_Width = busLine.m_Width;
                line.m_SegmentLength = busLine.m_SegmentLength;
                line.m_TransportType = TransportType.Helicopter;
                line.m_AccessConnectionType = RouteConnectionType.Pedestrian;
                line.m_RouteConnectionType = RouteConnectionType.Road;
                line.m_AccessTrackType = line.m_RouteTrackType = TrackTypes.None;
                line.m_AccessRoadType = RoadTypes.None;
                line.m_RouteRoadType = RoadTypes.Helicopter;
                line.m_SizeClass = Game.Vehicles.SizeClass.Small;
                line.m_PassengerTransport = true;
                line.m_CargoTransport = false;
                line.m_DefaultVehicleInterval = 150;
                line.m_StopDuration = 30;
                line.m_VehicleNotification = busLine.m_VehicleNotification;
                line.m_Color = new Color(.2f, .65f, .95f, 1);
                SetUI(line, group, 30);

                var assets = privateMeshes.Concat(new PrefabBase[] { group, vehicle, stopIcon, stop, airport, station, line }).ToArray();
                ValidateDependencies(assets);
                foreach (var p in assets)
                {
                    if (!m_Prefabs.AddPrefab(p)) throw new InvalidOperationException("Cannot register " + p.name);
                    Registered.Add(p);
                }
                if (!(config.m_PassengerSummaryItems ?? Array.Empty<UITransportSummaryItem>()).Any(x => x.m_Type == TransportType.Helicopter))
                {
                    // No native Helicopter passenger-history statistic exists. Do not reuse
                    // airplane totals (which would duplicate unrelated passenger counts).
                    config.m_PassengerSummaryItems = (config.m_PassengerSummaryItems ?? Array.Empty<UITransportSummaryItem>())
                        .Concat(new[] { new UITransportSummaryItem { m_Type = TransportType.Helicopter,
                            m_Unlockable = line, m_Icon = Icon, m_ShowLines = true,
                            m_Statistic = Game.City.StatisticType.Invalid } }).ToArray();
                }
                // One overview tab per native transport type. Coexist with other helicopter
                // mods without mutating their lines, buildings, or vehicles.
                if (!(config.m_PassengerLineTypes ?? Array.Empty<UITransportItem>()).Any(x => x.m_Type == TransportType.Helicopter))
                {
                    config.m_PassengerLineTypes = (config.m_PassengerLineTypes ?? Array.Empty<UITransportItem>())
                        .Concat(new[] { new UITransportItem { m_Type = TransportType.Helicopter, m_Unlockable = line, m_Icon = Icon } }).ToArray();
                }
                State = "registered";
                HelicopterRuntime.log.Info("CityWeaver passenger helicopters registered: roadside airport, station, vehicle and line.");
            }
            catch (Exception ex)
            {
                State = "failed";
                Error = ex.Message;
                HelicopterRuntime.log.Error(ex, "Passenger helicopter registration failed");
            }
            Enabled = false;
        }

        private static T Copy<T>(T source, string name) where T : PrefabBase
        {
            if (!source.isBuiltin || source.isSubscribedMod)
                throw new InvalidOperationException("Expected an independent game template: " + source.name);
            var copy = (T)source.Clone(name);
            copy.Remove<ObsoleteIdentifiers>();
            copy.Remove<Unlockable>();
            copy.Remove<UnlockOnBuild>();
            return copy;
        }

        private NotificationIconPrefab FindStopIcon()
        {
            using (var entities = m_StopIcons.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    var data = EntityManager.GetComponentData<TransportStopMarkerData>(entity);
                    if (data.m_TransportType == TransportType.Airplane && data.m_StopTypeA &&
                        m_Prefabs.TryGetPrefab<NotificationIconPrefab>(entity, out var icon) && icon.isBuiltin && !icon.isSubscribedMod)
                        return icon;
                }
            return null;
        }

        internal static void ConfigureStop(Game.Prefabs.TransportStop stop)
        {
            stop.m_TransportType = TransportType.Helicopter;
            stop.m_AccessConnectionType = RouteConnectionType.Pedestrian;
            stop.m_RouteConnectionType = RouteConnectionType.Road;
            stop.m_AccessTrackType = stop.m_RouteTrackType = TrackTypes.None;
            stop.m_AccessRoadType = RoadTypes.None;
            stop.m_RouteRoadType = RoadTypes.Helicopter;
            stop.m_EnterDistance = stop.m_ExitDistance = stop.m_AccessDistance = 0;
            stop.m_BoardingTime = .1f;
            stop.m_ComfortFactor = .5f;
            stop.m_LoadingFactor = 0;
            stop.m_PassengerTransport = true;
            stop.m_CargoTransport = false;
        }

        private static void SetUI(PrefabBase p, UIGroupPrefab group, int priority)
        {
            var ui = p.AddOrGetComponent<UIObject>();
            ui.m_Group = group;
            ui.m_Icon = PassengerHelicopterIcons.ForPrefab(p.name) ?? Icon;
            ui.m_Priority = priority;
        }

        private static ObjectSubObjectInfo ObjectAt(ObjectPrefab p, float3 pos) => new ObjectSubObjectInfo
        { m_Object = p, m_Position = pos, m_Rotation = quaternion.identity, m_ParentMesh = -1, m_Probability = 100 };

        private static ObjectSubNetInfo Path(NetPrefab p, float3 a, float3 b, int first) => new ObjectSubNetInfo
        {
            m_NetPrefab = p,
            m_BezierCurve = new Bezier4x3(a, math.lerp(a, b, 1f / 3), math.lerp(a, b, 2f / 3), b),
            m_NodeIndex = new int2(first, first + 1), m_ParentMesh = new int2(-1),
            m_Upgrades = Array.Empty<NetPieceRequirements>()
        };

        private static BuildingPrefab MakeAirport(BuildingPrefab source, string name, bool depot,
            UIGroupPrefab group, ServicePrefab service, MarkerObjectPrefab stop, PathwayPrefab footpath,
            MarkerObjectPrefab takeoff, TaxiwayPrefab taxiway, MarkerObjectPrefab helipad)
        {
            var p = Copy(source, name);
            p.m_AccessType = BuildingAccessType.Front;
            p.Remove<FireStation>();
            p.Remove<Game.Prefabs.ServiceCoverage>();
            p.Remove<ServiceUpgrade>();
            p.Remove<Game.Prefabs.TransportStop>();
            p.Remove<Game.Prefabs.TransportDepot>();
            var station = p.AddOrGetComponent<Game.Prefabs.TransportStation>();
            station.m_ComfortFactor = .5f;
            station.m_CarRefuelTypes = station.m_TrainRefuelTypes = station.m_WatercraftRefuelTypes = 0;
            station.m_AircraftRefuelTypes = Game.Vehicles.EnergyTypes.Fuel;
            p.AddOrGetComponent<ServiceObject>().m_Service = service;
            p.AddOrGetComponent<PlaceableObject>().m_ConstructionCost = depot ? 250000u : 120000u;
            p.AddOrGetComponent<Workplace>().m_Workplaces = depot ? 12 : 6;
            if (p.TryGet<ServiceConsumption>(out var consumption)) consumption.m_Upkeep = depot ? 24000 : 8000;
            p.AddOrGetComponent<PassengerHelicopterRoadAccess>();
            SetUI(p, group, depot ? 10 : 20);
            if (depot)
            {
                var d = p.AddOrGetComponent<Game.Prefabs.TransportDepot>();
                d.m_TransportType = TransportType.Helicopter;
                d.m_SizeClass = Game.Vehicles.SizeClass.Small;
                d.m_EnergyTypes = Game.Vehicles.EnergyTypes.Fuel;
                d.m_VehicleCapacity = PassengerHelicopterRules.AirportVehicleCapacity;
                d.m_ProductionDuration = 0;
                d.m_MaintenanceDuration = 0.05f;
                d.m_DispatchCenter = false;
            }
            // Preserve the original lot, meshes, utilities and native pedestrian entrances.
            // A dedicated passenger pad occupies one of the existing helipad locations.
            var objects = (p.AddOrGetComponent<ObjectSubObjects>().m_SubObjects ?? Array.Empty<ObjectSubObjectInfo>()).ToList();
            // Integrated Helipad is the AIR spawn marker (five on the base-game depot).
            // Helicopter Takeoff Location instead joins ROAD taxiways to AIR paths;
            // the fire depot does not contain those road/air connection markers.
            var pads = objects.Where(x => x.m_Object == helipad).Select(x => RootPosition(p, x))
                .OrderByDescending(x => x.z).ToList();
            if (pads.Count < 2) throw new InvalidOperationException("Game helicopter depot needs two Integrated Helipad air spawn markers; found " + pads.Count);
            var pad = pads[0];
            var second = pads.Skip(1).OrderBy(x => math.distancesq(x, pad)).First();
            if (math.distance(pad, second) < 20) throw new InvalidOperationException("Boarding and depot pads must be separate.");
            objects.RemoveAll(x => x.m_Object == helipad);
            objects.Add(ObjectAt(stop, pad));
            if (depot) objects.Add(ObjectAt(helipad, second));
            var direction = math.normalize(second - pad);
            objects.Add(ObjectAt(takeoff, pad - direction * 8));
            objects.Add(ObjectAt(takeoff, second + direction * 8));
            p.AddOrGetComponent<ObjectSubObjects>().m_SubObjects = objects.ToArray();
            var nets = (p.AddOrGetComponent<ObjectSubNets>().m_SubNets ?? Array.Empty<ObjectSubNetInfo>()).ToList();
            int node = nets.SelectMany(x => new[] { x.m_NodeIndex.x, x.m_NodeIndex.y }).DefaultIfEmpty(0).Max() + 10;
            nets.Add(Path(taxiway, pad - direction * 8, second + direction * 8, node));
            var front = new float3(pad.x, 0, p.m_LotDepth * 4f);
            nets.Add(Path(footpath, front, pad, node + 10));
            p.AddOrGetComponent<ObjectSubNets>().m_SubNets = nets.ToArray();
            return p;
        }

        private static float3 RootPosition(BuildingPrefab p, ObjectSubObjectInfo obj)
        {
            if (obj.m_ParentMesh < 0) return obj.m_Position;
            if (obj.m_ParentMesh >= p.m_Meshes.Length) throw new InvalidOperationException("Invalid helipad parent mesh.");
            var mesh = p.m_Meshes[obj.m_ParentMesh];
            return mesh.m_Position + math.rotate(mesh.m_Rotation, obj.m_Position);
        }

        internal static void ValidateDependencies(IEnumerable<PrefabBase> assets)
        {
            var owned = new HashSet<PrefabBase>(assets);
            var seen = new HashSet<PrefabBase>();
            var pending = new Queue<PrefabBase>(owned);
            while (pending.Count > 0)
            {
                var p = pending.Dequeue();
                if (p == null || !seen.Add(p)) continue;
                if (!owned.Contains(p) && (!p.isBuiltin || p.isSubscribedMod))
                    throw new InvalidOperationException("External mod dependency rejected: " + p.name);
                var components = new List<ComponentBase>(); p.GetComponents(components);
                var dependencies = new List<PrefabBase>();
                foreach (var c in components) c.GetDependencies(dependencies);
                foreach (var dependency in dependencies) if (dependency != null) pending.Enqueue(dependency);
            }
        }
    }

}
