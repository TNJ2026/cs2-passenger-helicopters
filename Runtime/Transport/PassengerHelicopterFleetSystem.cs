using System;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // Only CityWeaver routes receive our default model. Existing third-party routes and
    // explicit user fleet selections remain unchanged. Runs without MCP polling.
    public partial class PassengerHelicopterFleetSystem : Game.GameSystemBase
    {
        private EntityQuery m_Lines;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Lines = GetEntityQuery(ComponentType.ReadOnly<Game.Routes.TransportLine>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadWrite<Game.Routes.VehicleModel>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            if (!ps.TryGetPrefab(new PrefabID(nameof(HelicopterPrefab), PassengerHelicopterSystem.Vehicle), out var vehicle) ||
                !ps.TryGetEntity(vehicle, out var model) || EntityManager.HasComponent<Created>(model)) return;
            using (var entities = m_Lines.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    if (!ps.TryGetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(e), out var line) || line.name != PassengerHelicopterSystem.Line) continue;
                    var models = EntityManager.GetBuffer<Game.Routes.VehicleModel>(e);
                    if (models.Length == 0) models.Add(new Game.Routes.VehicleModel { m_PrimaryPrefab = model });
                }
        }
    }
}
