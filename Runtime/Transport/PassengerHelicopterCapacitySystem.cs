using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // PublicTransportVehicleData is serialized: update saved prefab copies too.
    // Model replacement preserves this gameplay component and its prefab identity.
    public partial class PassengerHelicopterCapacitySystem : GameSystemBase
    {
        private EntityQuery m_Prefabs,m_Depots;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadWrite<PublicTransportVehicleData>(), ComponentType.Exclude<Created>());
            m_Depots = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadWrite<TransportDepotData>(), ComponentType.Exclude<Created>(), ComponentType.Exclude<Deleted>());
        }
        protected override void OnUpdate()
        {
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            if(ps==null)return;
            // Imported airport assets and saved prefab copies retain serialized
            // depot data. Update in place so existing facilities gain the slot
            // without replacing their stops, routes or owned vehicles.
            using(var depots=m_Depots.ToEntityArray(Allocator.Temp))
                foreach(var entity in depots)
                {
                    // Native TryGetPrefab returns true even when its `as T`
                    // cast returns null (e.g. a depot upgrade, not a building).
                    if(!ps.TryGetPrefab<BuildingPrefab>(entity,out var p)||p==null||p.isBuiltin||HelicopterAssetOwnership.RejectForeignSubscription(p)||
                        (p.name!=PassengerHelicopterSystem.Airport&&p.name!=ImportedHelipadMenuSystem.Airport))continue;
                    var data=EntityManager.GetComponentData<TransportDepotData>(entity);
                    if(data.m_TransportType!=TransportType.Helicopter)continue;
                    var managed=p.GetComponent<Game.Prefabs.TransportDepot>();
                    if(managed!=null)managed.m_VehicleCapacity=PassengerHelicopterRules.AirportVehicleCapacity;
                    if(data.m_VehicleCapacity==PassengerHelicopterRules.AirportVehicleCapacity)continue;
                    data.m_VehicleCapacity=PassengerHelicopterRules.AirportVehicleCapacity;
                    EntityManager.SetComponentData(entity,data);
                    HelicopterRuntime.log.Info("Passenger helicopter airport capacity updated to 6: "+entity);
                }
            using (var entities = m_Prefabs.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    if (!ps.TryGetPrefab<PrefabBase>(entity, out var p) || p==null ||
                        (p.name != PassengerHelicopterSystem.Vehicle && p.name != "HelicopterPassenger01")) continue;
                    var data = EntityManager.GetComponentData<PublicTransportVehicleData>(entity);
                    if (data.m_TransportType != TransportType.Helicopter) continue;
                    var managed = p.GetComponent<Game.Prefabs.PublicTransport>();
                    if (managed != null) managed.m_PassengerCapacity = PassengerHelicopterRules.PassengerCapacity;
                    if (data.m_PassengerCapacity == PassengerHelicopterRules.PassengerCapacity) continue;
                    data.m_PassengerCapacity = PassengerHelicopterRules.PassengerCapacity;
                    EntityManager.SetComponentData(entity, data);
                }
        }
    }
}
