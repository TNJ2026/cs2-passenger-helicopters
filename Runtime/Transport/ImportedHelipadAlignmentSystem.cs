using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // Repair saved prefab copies and their existing stop/network entities in place.
    // Do not UpdatePrefab: replacing a boarding stop breaks saved route references.
    public partial class ImportedHelipadAlignmentSystem : GameSystemBase
    {
        private EntityQuery m_Types, m_Buildings;
        private readonly Dictionary<Entity, Layout> m_Layouts = new Dictionary<Entity, Layout>();
        private readonly HashSet<Entity> m_Done = new HashSet<Entity>();
        private sealed class Layout
        {
            internal readonly List<(float3 oldPoint, float3 point)> Nodes = new List<(float3, float3)>();
            internal Game.Prefabs.SubObject[] Objects;
            internal float Shift;
        }
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Types = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<Game.Prefabs.SubObject>(), ComponentType.ReadOnly<Game.Prefabs.SubNet>(),
                ComponentType.Exclude<Created>(), ComponentType.Exclude<Deleted>());
            m_Buildings = GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.Exclude<Created>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
        }
        private Layout Configure(Entity type, BuildingPrefab p)
        {
            var managedObjects = p.GetComponent<ObjectSubObjects>()?.m_SubObjects;
            var managedNets = p.GetComponent<ObjectSubNets>()?.m_SubNets;
            if (managedObjects == null || managedNets == null ||
                EntityManager.GetBuffer<Game.Prefabs.SubObject>(type).Length != managedObjects.Length ||
                EntityManager.GetBuffer<Game.Prefabs.SubNet>(type).Length != managedNets.Length) return null;
            var meshes = EntityManager.GetBuffer<SubMesh>(type);
            float front = float.MinValue;
            float back = float.MaxValue;
            for (int i = 0; i < meshes.Length; i++)
            {
                var m = meshes[i];
                if (!EntityManager.HasComponent<MeshData>(m.m_SubMesh)) return null;
                if (math.lengthsq(m.m_Rotation.value - quaternion.identity.value) > .0001f) return null;
                front = math.max(front, EntityManager.GetComponentData<MeshData>(m.m_SubMesh).m_Bounds.max.z + m.m_Position.z);
                back = math.min(back, EntityManager.GetComponentData<MeshData>(m.m_SubMesh).m_Bounds.min.z + m.m_Position.z);
            }
            if (meshes.Length == 0 || p.m_Meshes.Length != meshes.Length) return null;
            float lotFront = EntityManager.GetComponentData<BuildingData>(type).m_LotSize.y * 4;
            var layout = new Layout { Shift = ImportedHelipadRules.FrontShift(front, (int)(lotFront / 4)) };
            for (int i = 0; i < meshes.Length; i++)
            {
                var m = meshes[i]; m.m_Position.z += layout.Shift; meshes[i] = m;
                p.m_Meshes[i].m_Position = m.m_Position;
            }
            var objects = EntityManager.GetBuffer<Game.Prefabs.SubObject>(type);
            layout.Objects = new Game.Prefabs.SubObject[objects.Length];
            for (int i = 0; i < objects.Length; i++)
            {
                var o = objects[i]; o.m_Position.z += layout.Shift;
                o.m_ParentIndex = 0; o.m_Flags &= ~SubObjectFlags.OnGround;
                objects[i] = o; layout.Objects[i] = o;
                managedObjects[i].m_Position = o.m_Position; managedObjects[i].m_ParentMesh = 0;
            }
            var nets = EntityManager.GetBuffer<Game.Prefabs.SubNet>(type);
            for (int i = 0; i < nets.Length; i++)
            {
                var n = nets[i]; var old = n.m_Curve;
                float3 Align(float3 point) { point.z = ImportedHelipadRules.PathZ(point.z, lotFront, layout.Shift); return point; }
                var a = Align(old.a); var d = Align(old.d);
                n.m_Curve = ShiftCurve(old, a, d);
                n.m_ParentMesh = new int2(ImportedHelipadRules.Parent(a.y), ImportedHelipadRules.Parent(d.y));
                nets[i] = n;
                managedNets[i].m_BezierCurve = n.m_Curve; managedNets[i].m_ParentMesh = n.m_ParentMesh;
                layout.Nodes.Add((old.a, a)); layout.Nodes.Add((old.d, d));
                var legacyA = a; legacyA.z = ImportedHelipadRules.LegacyPathZ(a.z, lotFront, front-back);
                var legacyD = d; legacyD.z = ImportedHelipadRules.LegacyPathZ(d.z, lotFront, front-back);
                layout.Nodes.Add((legacyA, a)); layout.Nodes.Add((legacyD, d));
            }
            return layout;
        }
        private static Bezier4x3 ShiftCurve(Bezier4x3 c, float3 a, float3 d) =>
            new Bezier4x3(a, c.b + math.lerp(a-c.a,d-c.d,1f/3), c.c + math.lerp(a-c.a,d-c.d,2f/3), d);
        protected override void OnUpdate()
        {
            if(!HelicopterModelPolicy.ImportedModelsEnabled){Enabled=false;return;}
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            using (var types = m_Types.ToEntityArray(Allocator.Temp))
                foreach (var type in types)
                    if (!m_Layouts.ContainsKey(type) && ps.TryGetPrefab<BuildingPrefab>(type, out var p) &&
                        p.name == ImportedHelipadMenuSystem.Station && !p.isBuiltin && !p.isSubscribedMod)
                    {
                        var layout = Configure(type, p);
                        if (layout != null) m_Layouts.Add(type, layout);
                    }
            using (var buildings = m_Buildings.ToEntityArray(Allocator.Temp))
                foreach (var building in buildings)
                {
                    if (!EntityManager.HasComponent<HelipadLotVersion>(building) || m_Done.Contains(building) || EntityManager.HasComponent<Updated>(building) ||
                        !m_Layouts.TryGetValue(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab, out var layout) ||
                        !EntityManager.HasBuffer<Game.Objects.SubObject>(building) || !EntityManager.HasBuffer<Game.Net.SubNet>(building)) continue;
                    var root = EntityManager.GetComponentData<Game.Objects.Transform>(building);
                    float3 Local(float3 point) => math.rotate(math.inverse(root.m_Rotation), point-root.m_Position);
                    float3 WorldPoint(float3 point) => root.m_Position + math.rotate(root.m_Rotation, point);
                    float3 AlignNode(float3 point)
                    {
                        var local = Local(point);
                        foreach (var pair in layout.Nodes)
                            if (math.distance(local.xz, pair.oldPoint.xz) < .15f || math.distance(local.xz, pair.point.xz) < .15f)
                                return WorldPoint(pair.point);
                        return point;
                    }
                    using (var children = EntityManager.GetBuffer<Game.Objects.SubObject>(building, true).ToNativeArray(Allocator.Temp))
                    foreach (var child in children)
                    {
                        var e = child.m_SubObject;
                        if (!EntityManager.HasComponent<Game.Objects.Transform>(e) || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                        var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                        var t = EntityManager.GetComponentData<Game.Objects.Transform>(e); var local = Local(t.m_Position);
                        int best = -1; float distance = float.MaxValue;
                        for (int i = 0; i < layout.Objects.Length; i++)
                        {
                            var o = layout.Objects[i]; if (o.m_Prefab != prefab) continue;
                            float delta = math.distance(local.xz, o.m_Position.xz);
                            if (delta < distance) { distance = delta; best = i; }
                        }
                        if (best < 0 || distance > 8) continue;
                        var target = layout.Objects[best]; var next = WorldPoint(target.m_Position);
                        if (math.distance(t.m_Position, next) < .01f && EntityManager.HasComponent<Game.Objects.Elevation>(e)) continue;
                        t.m_Position = next; EntityManager.SetComponentData(e, t);
                        var elevation = new Game.Objects.Elevation(target.m_Position.y, 0);
                        if (EntityManager.HasComponent<Game.Objects.Elevation>(e)) EntityManager.SetComponentData(e, elevation);
                        else EntityManager.AddComponentData(e, elevation);
                        if (EntityManager.HasComponent<Game.Tools.LocalTransformCache>(e))
                        {
                            var cache = EntityManager.GetComponentData<Game.Tools.LocalTransformCache>(e);
                            cache.m_Position = target.m_Position; cache.m_ParentMesh = 0;
                            EntityManager.SetComponentData(e, cache);
                        }
                        EntityManager.AddComponent<Updated>(e);
                        if (EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(e))
                            using (var routes = EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(e,true).ToNativeArray(Allocator.Temp))
                                foreach (var route in routes) EntityManager.AddComponent<Updated>(route.m_Waypoint);
                    }
                    using (var nets = EntityManager.GetBuffer<Game.Net.SubNet>(building,true).ToNativeArray(Allocator.Temp))
                    foreach (var sub in nets)
                    {
                        var e = sub.m_SubNet;
                        if (EntityManager.HasComponent<Deleted>(e)) continue;
                        if (EntityManager.HasComponent<Game.Net.Node>(e))
                        {
                            var n = EntityManager.GetComponentData<Game.Net.Node>(e); var next = AlignNode(n.m_Position);
                            if (math.distance(next,n.m_Position) < .01f) continue;
                            n.m_Position = next; EntityManager.SetComponentData(e,n);
                            SetElevation(e, new float2(Local(next).y)); EntityManager.AddComponent<Updated>(e);
                        }
                        else if (EntityManager.HasComponent<Game.Net.Curve>(e))
                        {
                            var c = EntityManager.GetComponentData<Game.Net.Curve>(e);
                            var a = AlignNode(c.m_Bezier.a); var d = AlignNode(c.m_Bezier.d);
                            if (math.distance(a,c.m_Bezier.a) < .01f && math.distance(d,c.m_Bezier.d) < .01f) continue;
                            c.m_Bezier = ShiftCurve(c.m_Bezier,a,d); c.m_Length = MathUtils.Length(c.m_Bezier);
                            EntityManager.SetComponentData(e,c); SetElevation(e,new float2(Local(a).y,Local(d).y));
                            EntityManager.AddComponent<Updated>(e);
                        }
                    }
                    // Mesh transforms changed inside the unchanged lot; refresh rendering only.
                    EntityManager.AddComponent<BatchesUpdated>(building);
                    m_Done.Add(building);
                    HelicopterRuntime.log.Info("Imported helipad frontage/roof migration completed: " + building);
                }
        }
        private void SetElevation(Entity e, float2 value)
        {
            var elevation = new Game.Net.Elevation(value);
            if (EntityManager.HasComponent<Game.Net.Elevation>(e)) EntityManager.SetComponentData(e,elevation);
            else EntityManager.AddComponentData(e,elevation);
        }
    }
}
