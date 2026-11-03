using System;
using System.Linq;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    public partial class ImportedHelipadMenuSystem : GameSystemBase
    {
        internal const string Airport = "CityWeaver_850b7b8e4f8b4a679fa2623a7c3dd3e0";
        internal const string Model = "CityWeaver_7e149f3fb9574d47adfac7977c83d900";
        internal const string Station = "CityWeaver Imported Helipad Station";
        private EntityQuery m_Models;
        private BuildingPrefab m_Station;
        private Entity m_Airport;
        private bool m_StationReadyLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Models = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadOnly<ObjectGeometryData>(), ComponentType.Exclude<Created>(), ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            if(!HelicopterModelPolicy.ImportedModelsEnabled){Enabled=false;return;}
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            var registry = World.GetExistingSystemManaged<PassengerHelicopterSystem>();
            if (ps == null || registry?.State != "registered") return;
            var group = registry.Registered.OfType<UIAssetCategoryPrefab>().FirstOrDefault();
            if (group == null || !ps.TryGetEntity(group, out var category) ||
                !EntityManager.HasBuffer<UIGroupElement>(category)) return;
            if (m_Station == null || !EntityManager.Exists(m_Airport))
                using (var models = m_Models.ToEntityArray(Allocator.Temp))
                    foreach (var e in models)
                    {
                        if (!ps.TryGetPrefab<PrefabBase>(e, out var p) || p == null || p.isBuiltin || HelicopterAssetOwnership.RejectForeignSubscription(p)) continue;
                        if (p.name == Airport && p is BuildingPrefab) m_Airport = e;
                        if (p.name == Model && p is StaticObjectPrefab model && m_Station == null && GeometryReady(e))
                        {
                            var template = registry.Registered.OfType<BuildingPrefab>().First(x => x.name == PassengerHelicopterSystem.Station);
                            var stationPrefab = CreateStation(template, model, ps, group);
                            if (!ps.AddPrefab(stationPrefab)) throw new InvalidOperationException("Cannot register imported helipad station");
                            m_Station = stationPrefab;
                            registry.Registered.Add(stationPrefab);
                            HelicopterRuntime.log.Info("Imported helipad station registered with independent building configuration; awaiting native geometry.");
                        }
                    }
            Bind(ps, category, group, m_Airport, PassengerHelicopterSystem.Airport, PassengerHelicopterIcons.Airport, 10);
            if (m_Station != null && ps.TryGetEntity(m_Station, out var station))
                Bind(ps, category, group, station, PassengerHelicopterSystem.Station, PassengerHelicopterIcons.Station, 20);
        }

        private void Bind(PrefabSystem ps, Entity category, UIAssetCategoryPrefab group, Entity target, string legacy, string icon, int priority)
        {
            if (!EntityManager.Exists(target) || EntityManager.HasComponent<Created>(target) ||
                !GeometryReady(target) || !EntityManager.HasComponent<ObjectData>(target) ||
                !EntityManager.GetComponentData<ObjectData>(target).m_Archetype.Valid ||
                !EntityManager.HasComponent<BuildingData>(target) || !EntityManager.HasComponent<UIObjectData>(target) ||
                !ps.TryGetPrefab<BuildingPrefab>(target, out var prefab) || prefab == null) return;
            var ui = prefab.AddOrGetComponent<UIObject>(); ui.m_Group = group; ui.m_Icon = icon; ui.m_Priority = priority;
            var data = EntityManager.GetComponentData<UIObjectData>(target); data.m_Group = category; data.m_Priority = priority;
            EntityManager.SetComponentData(target, data);
            var items = EntityManager.GetBuffer<UIGroupElement>(category);
            bool found = false;
            for (int i = items.Length - 1; i >= 0; i--)
            {
                var e = items[i].m_Prefab;
                if (e == target) { if (found) items.RemoveAt(i); found = true; }
                else if (ps.TryGetPrefab<PrefabBase>(e, out var old) && old != null && old.name == legacy) items.RemoveAt(i);
            }
            if (!found) items.Add(new UIGroupElement { m_Prefab = target });
            if (prefab == m_Station && !m_StationReadyLogged)
            {
                m_StationReadyLogged = true;
                HelicopterRuntime.log.Info("Imported helipad menu ready: " + prefab.name + ", meshes=" + prefab.m_Meshes.Length);
            }
        }

        private bool GeometryReady(Entity entity)
        {
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<ObjectGeometryData>(entity)) return false;
            var geometry = EntityManager.GetComponentData<ObjectGeometryData>(entity);
            return geometry.m_MinLod != 255 && HelicopterAssetRules.ValidBounds(
                geometry.m_Bounds.min.x, geometry.m_Bounds.min.y, geometry.m_Bounds.min.z,
                geometry.m_Bounds.max.x, geometry.m_Bounds.max.y, geometry.m_Bounds.max.z);
        }

        private static BuildingPrefab CreateStation(BuildingPrefab template, StaticObjectPrefab model, PrefabSystem ps, UIAssetCategoryPrefab group)
        {
            // A different model must not inherit the fire depot's model-specific
            // effects, activity locations, geometry settings or mesh references.
            var p = PrefabBase.Create<BuildingPrefab>(Station);
            void CopyService<T>() where T : ComponentBase
            {
                if (template.TryGet<T>(out var component)) p.AddComponentFrom(component);
            }
            CopyService<Game.Prefabs.TransportStation>();
            CopyService<CityServiceBuilding>();
            CopyService<ServiceObject>();
            CopyService<ServiceConsumption>();
            CopyService<Workplace>();
            p.m_AccessType = BuildingAccessType.Front;
            p.AddOrGetComponent<PassengerHelicopterRoadAccess>();
            // The imported model is already native Z-mirrored. Center its footprint;
            // never keep the old fire-depot meshes, utilities, areas or markers.
            if (model.m_Meshes == null || model.m_Meshes.Length == 0)
                throw new InvalidOperationException("Helipad model has no meshes");
            var min = new float3(float.MaxValue); var max = new float3(float.MinValue);
            foreach (var mesh in model.m_Meshes)
            {
                if (!(mesh.m_Mesh is RenderPrefab) || math.lengthsq(mesh.m_Rotation.value - quaternion.identity.value) > .0001f)
                    throw new InvalidOperationException("Helipad source requires native render meshes with identity rotation");
                var b = ((RenderPrefab)mesh.m_Mesh).bounds;
                min = math.min(min, b.min + mesh.m_Position); max = math.max(max, b.max + mesh.m_Position);
            }
            var offset = new float3(-(min.x+max.x)/2, -min.y, -(min.z+max.z)/2);
            p.m_LotWidth = HelipadLotRules.Cells(max.x-min.x); p.m_LotDepth = HelipadLotRules.Cells(max.z-min.z);
            offset.z += ImportedHelipadRules.FrontShift(max.z + offset.z, p.m_LotDepth);
            p.m_Meshes = model.m_Meshes.Select(m => new ObjectMeshInfo { m_Mesh=m.m_Mesh,
                m_Position=m.m_Position+offset, m_Rotation=m.m_Rotation, m_RequireState=m.m_RequireState }).ToArray();
            p.m_LotWidth = HelipadLotRules.Cells(max.x-min.x); p.m_LotDepth = HelipadLotRules.Cells(max.z-min.z);
            p.Remove<ObjectSubAreas>(); p.Remove<ObjectSubLanes>(); p.Remove<SpawnLocation>();
            p.Remove<Game.Prefabs.TransportDepot>();
            p.AddOrGetComponent<PlaceableObject>().m_ConstructionCost = 120000;
            var terra=p.AddOrGetComponent<BuildingTerraformOverride>();
            terra.m_LevelMinOffset=new float2(-1);terra.m_LevelMaxOffset=new float2(1);
            terra.m_SmoothMinOffset=new float2(-4);terra.m_SmoothMaxOffset=new float2(4);
            // UIObject.LateInitialize auto-inserts grouped prefabs. Bind only
            // after native geometry/archetype readiness has been verified.
            var ui=p.AddOrGetComponent<UIObject>();ui.m_Group=null;ui.m_Icon=PassengerHelicopterIcons.Station;ui.m_Priority=20;
            T Find<T>(string name) where T:PrefabBase => ps.TryGetPrefab(new PrefabID(typeof(T).Name,name),out var value) && value is T typed
                ?typed:throw new InvalidOperationException("Missing station dependency: "+name);
            // Measured center/top of the imported circular pad (OBJ Z is mirrored).
            var pad = new float3(1.331073f*HelipadLotRules.Width,5.01f,-2.510134f*HelipadLotRules.Depth)+offset;
            ObjectSubObjectInfo At(MarkerObjectPrefab marker,float3 pos)=>new ObjectSubObjectInfo{
                m_Object=marker,m_Position=pos,m_Rotation=quaternion.identity,m_ParentMesh=0,m_Probability=100};
            var takeoff=Find<MarkerObjectPrefab>("Helicopter Takeoff Location");
            p.AddOrGetComponent<ObjectSubObjects>().m_SubObjects=new[]{
                At(Find<MarkerObjectPrefab>(PassengerHelicopterSystem.Stop),pad),
                At(takeoff,pad+new float3(0,0,7)),At(takeoff,pad-new float3(0,0,7))};
            ObjectSubNetInfo Path(NetPrefab net,float3 a,float3 b,int first,int last)=>new ObjectSubNetInfo{
                m_NetPrefab=net,m_BezierCurve=new Bezier4x3(a,math.lerp(a,b,1f/3),math.lerp(a,b,2f/3),b),
                m_NodeIndex=new int2(first,last),m_ParentMesh=new int2(ImportedHelipadRules.Parent(a.y),ImportedHelipadRules.Parent(b.y)),m_Upgrades=Array.Empty<NetPieceRequirements>()};
            var foot=Find<PathwayPrefab>("Invisible Pedestrian Path");
            var front=new float3(pad.x,0,p.m_LotDepth*4);
            p.AddOrGetComponent<ObjectSubNets>().m_SubNets=new[]{
                Path(Find<TaxiwayPrefab>("Invisible Helicopter Taxiway"),pad+new float3(0,0,7),pad-new float3(0,0,7),0,1)}
                .Concat(HelipadPedestrianSystem.Paths(foot,pad,front.z)).ToArray();
            return p;
        }
    }
}
