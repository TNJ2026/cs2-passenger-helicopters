using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // Imported assets can deserialize private copies of our category. Keep only
    // the category registered by this mod in the construction toolbar; retain
    // the asset definitions and their saved references for compatibility.
    public partial class PassengerHelicopterMenuSystem : GameSystemBase
    {
        private EntityQuery m_Categories, m_Vehicles;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Categories = GetEntityQuery(ComponentType.ReadOnly<UIAssetCategoryData>(),
                ComponentType.ReadOnly<PrefabData>(), ComponentType.Exclude<Created>(),
                ComponentType.Exclude<Deleted>());
            m_Vehicles = GetEntityQuery(ComponentType.ReadOnly<HelicopterData>(),
                ComponentType.ReadOnly<PublicTransportVehicleData>(), ComponentType.ReadOnly<PrefabData>(),
                ComponentType.Exclude<Created>(), ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            var registration = World.GetExistingSystemManaged<PassengerHelicopterSystem>();
            if (ps == null || registration == null || registration.State != "registered") return;
            bool changed = false;
            using (var vehicles = m_Vehicles.ToEntityArray(Allocator.Temp))
                foreach (var entity in vehicles)
                {
                    if (!ps.TryGetPrefab<HelicopterPrefab>(entity, out var vehicle) || vehicle == null ||
                        vehicle.name != PassengerHelicopterSystem.Vehicle || vehicle.isBuiltin || HelicopterAssetOwnership.RejectForeignSubscription(vehicle) ||
                        EntityManager.GetComponentData<PublicTransportVehicleData>(entity).m_TransportType != TransportType.Helicopter) continue;
                    // Patch every deserialized private copy, not just the model
                    // originally registered by this session. No UpdatePrefab:
                    // ImageSystem reads the managed icon directly.
                    var ui = vehicle.AddOrGetComponent<UIObject>();
                    if (ui.m_Icon == PassengerHelicopterIcons.Helicopter) continue;
                    ui.m_Icon = PassengerHelicopterIcons.Helicopter;
                    changed = true;
                }
            if (changed) World.GetExistingSystemManaged<Game.UI.InGame.SelectedInfoUISystem>()?.RequestUpdate();
            // Use object identity from registration, not the name registry, which
            // may resolve a separately serialized copy with the very same name.
            Entity canonical = Entity.Null;
            foreach (var asset in registration.Registered)
                if (asset is UIAssetCategoryPrefab && asset.name == PassengerHelicopterSystem.Category &&
                    ps.TryGetEntity(asset, out var candidate)) canonical = candidate;
            if (canonical == Entity.Null || !EntityManager.HasComponent<UIAssetCategoryData>(canonical) ||
                EntityManager.HasComponent<Created>(canonical)) return;

            using (var categories = m_Categories.ToEntityArray(Allocator.Temp))
                foreach (var category in categories)
                {
                    if (!ps.TryGetPrefab<UIAssetCategoryPrefab>(category, out var prefab) || prefab == null ||
                        prefab.name != PassengerHelicopterSystem.Category || prefab.isBuiltin || HelicopterAssetOwnership.RejectForeignSubscription(prefab)) continue;
                    // Saved imported assets carry private UIObject instances too.
                    // Change presentation only; UpdatePrefab would rebuild assets.
                    if (prefab.TryGet<UIObject>(out var categoryUI)) categoryUI.m_Icon = PassengerHelicopterIcons.Helicopter;
                    if (EntityManager.HasBuffer<UIGroupElement>(category))
                    {
                        var children = EntityManager.GetBuffer<UIGroupElement>(category);
                        for (int j = 0; j < children.Length; j++)
                            if (ps.TryGetPrefab<PrefabBase>(children[j].m_Prefab, out var child) && child != null &&
                                !child.isBuiltin && !HelicopterAssetOwnership.RejectForeignSubscription(child) && child.TryGet<UIObject>(out var ui))
                            {
                                var icon = PassengerHelicopterIcons.ForPrefab(child.name);
                                if (icon != null) ui.m_Icon = icon;
                            }
                    }
                    if (category == canonical) continue;
                    var menu = EntityManager.GetComponentData<UIAssetCategoryData>(category).m_Menu;
                    if (!EntityManager.HasBuffer<UIGroupElement>(menu)) continue;
                    var entries = EntityManager.GetBuffer<UIGroupElement>(menu);
                    for (int i = entries.Length - 1; i >= 0; i--)
                        if (entries[i].m_Prefab == category) entries.RemoveAt(i);
                }
            // Keep checking while paused too: native asset loading can append
            // another category after initial registration or after loading a save.
        }
    }
}
