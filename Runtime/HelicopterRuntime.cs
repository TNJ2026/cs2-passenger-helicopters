using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Colossal.Logging;
using Game;
using Game.Rendering;
[assembly: InternalsVisibleTo("CityWeaverMCP")]
namespace CityWeaver
{
    // Assembly name and component namespaces are retained for old imported assets.
    public static class HelicopterRuntime
    {
        public static readonly ILog log = LogManager.GetLogger("PassengerHelicopters").SetShowsErrorsInUI(false);
        public static bool Loaded { get; private set; }
        public static bool OwnsModelConfiguration(Newtonsoft.Json.Linq.JObject configuration)
        {
            if(!(configuration?["parts"] is Newtonsoft.Json.Linq.JArray parts)||parts.Count!=1||!(parts[0] is Newtonsoft.Json.Linq.JObject part))return false;
            return (string)part["render_prefab"]=="CityWeaver_4a7fc21e03ce4e449bc02ae964c6ffe2_0_CWMilMi26tcPart01 Mesh"&&
                ((string)part["rig_mode"]??"source")=="source"&&((string)part["color_mode"]??"source")=="route"&&
                part["bones"]==null&&part["animations"]==null&&part["lights"]==null&&configuration["effects"]==null;
        }
        private static bool s_PersistenceRegistered;
        public static void RegisterPersistence(UpdateSystem updateSystem)
        {
            if(s_PersistenceRegistered)return;
            updateSystem.UpdateBefore<VehicleModelPersistenceSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            s_PersistenceRegistered=true;
        }
        public static void Load(UpdateSystem updateSystem)
        {
            if (Loaded) return;
            RegisterPersistence(updateSystem);
            PassengerHelicopterIcons.Register();
            updateSystem.UpdateBefore<PassengerHelicopterSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<PassengerHelicopterStopMigrationSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<ImportedHeliportDualStopSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<PassengerHelicopterWaitingAreaSystem, ImportedHeliportDualStopSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<ImportedHeliportPedestrianRepairSystem, PassengerHelicopterWaitingAreaSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<PassengerHelicopterWaitingConnectionSystem, Game.Routes.WaypointConnectionSystem>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateAfter<HelipadAirwayConnectionSystem, PassengerHelicopterWaitingConnectionSystem>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateBefore<ImportedHeliportPadAlignmentSystem, ImportedHeliportDualStopSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<PassengerHelicopterRouteDisplaySystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<PassengerHelicopterMenuSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<PassengerHelicopterVehiclePickerSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<PassengerHelicopterVehicleModelSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<ImportedHelipadAlignmentSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<HelipadLotMigrationSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateBefore<HelipadLotMigrationSystem, ImportedHelipadAlignmentSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<HelipadPedestrianSystem, ImportedHelipadAlignmentSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateBefore<ImportedHelipadMenuSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAt<PassengerHelicopterRouteTooltipSystem>(SystemUpdatePhase.UITooltip);
            updateSystem.UpdateBefore<PassengerHelicopterRouteToolPolicySystem, Game.Tools.RouteToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateBefore<HeliportPlacementGradeSystem, Game.Tools.GenerateObjectsSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAfter<PassengerHelicopterCapacitySystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<PassengerHelicopterSpeedSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateBefore<PassengerHelicopterFleetSystem, Game.Simulation.TransportLineSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<PassengerHelicopterTimingSystem, Game.Simulation.TransportAircraftAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<PassengerHelicopterDepartureSystem, Game.Simulation.TransportAircraftAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<PassengerHelicopterHangarAssetSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<ImportedHeliportFootprintSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateBefore<ImportedHeliportFootprintSystem, ImportedHeliportPadAlignmentSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateBefore<ImportedHeliportFootprintSystem, PassengerHelicopterHangarAssetSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateBefore<PassengerHelicopterHangarSystem, PassengerHelicopterDepartureSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<PassengerHelicopterDepartureSpeedSystem, Game.Simulation.AircraftMoveSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<PassengerHelicopterMarkerSystem, Game.Notifications.IconCommandSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateBefore<PassengerHelicopterLineSafetySystem, Game.Simulation.TransportLineSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<PassengerHelicopterSpacingSystem, Game.Simulation.AircraftMoveSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<PassengerHelicopterAirportSystem, Game.Simulation.AircraftMoveSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<PassengerHelicopterLandingSystem, Game.Simulation.AircraftNavigationSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<PassengerHelicopterLandingSystem, PassengerHelicopterAirportSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<PassengerHelicopterLandingSystem, Game.Simulation.AircraftMoveSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<PassengerHelicopterHangarAnimationSystem, ProceduralSkeletonSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAfter<HelicopterModels.HelicopterNativeMotionBlurSystem, ProceduralSkeletonSystem>(SystemUpdatePhase.Rendering);
            // Native Rendering already orders ObjectInterpolate before the
            // skeleton upload. Each registration schedules an execution; two
            // relative registrations do not express a combined constraint.
            updateSystem.UpdateBefore<HelicopterModels.HelicopterRotorExposureSystem, ProceduralSkeletonSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateBefore<HelicopterModels.Mi26ModelSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<HelicopterModels.HelicopterModelReferencesSystem, Game.Prefabs.PrefabSystem>(SystemUpdatePhase.MainLoop);
            Loaded = true;
            log.Info("Passenger Helicopters standalone runtime enabled; no MCP bridge required.");
        }
        public static void Unload()
        {
            PassengerHelicopterIcons.Unregister();
            Loaded = false;
        }
    }
    public sealed class QueryException : Exception
    {
        public string Code { get; }
        public QueryException(string code, string message) : base(message) { Code = code; }
    }
    internal static class HelicopterAssetRules
    {
        internal static bool ValidBounds(float minX,float minY,float minZ,float maxX,float maxY,float maxZ)
        {
            var values=new[]{minX,minY,minZ,maxX,maxY,maxZ};
            return values.All(v=>!float.IsNaN(v)&&!float.IsInfinity(v)) && minX<=maxX && minY<=maxY && minZ<=maxZ &&
                ((double)maxX-minX+(double)maxY-minY+(double)maxZ-minZ)>0.00001;
        }
    }
}
