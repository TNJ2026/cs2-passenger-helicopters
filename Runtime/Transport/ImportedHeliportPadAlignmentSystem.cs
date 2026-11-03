using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // Move the old operational pad row to the measured artwork centers. Exact old
    // row matching makes this idempotent; the depot and frontage are not moved.
    public partial class ImportedHeliportPadAlignmentSystem : GameSystemBase
    {
        private EntityQuery m_Buildings;
        internal static float3 Align(float3 p)
        {
            var source = ImportedHeliportRules.AlignLegacyPad(p.z + 6, HeliportWidthRules.AuthoredZ(p.x));
            var local = ImportedHeliportRules.Position(source.x, source.z);
            return new float3(local.x*HeliportWidthRules.Factor, p.y, local.z);
        }
        private static Bezier4x3 Shift(Bezier4x3 c, float3 a, float3 d)
        {
            var da = a-c.a; var dd = d-c.d;
            return new Bezier4x3(a,c.b+math.lerp(da,dd,1f/3),c.c+math.lerp(da,dd,2f/3),d);
        }
        internal static void AlignPrefab(EntityManager em, Entity type, BuildingPrefab p)
        {
            foreach (var o in p.GetComponent<ObjectSubObjects>().m_SubObjects) o.m_Position = Align(o.m_Position);
            var objects = em.GetBuffer<Game.Prefabs.SubObject>(type);
            for (int i=0;i<objects.Length;i++) { var o=objects[i]; o.m_Position=Align(o.m_Position); objects[i]=o; }
            foreach (var n in p.GetComponent<ObjectSubNets>().m_SubNets)
                n.m_BezierCurve=Shift(n.m_BezierCurve,Align(n.m_BezierCurve.a),Align(n.m_BezierCurve.d));
            var nets=em.GetBuffer<Game.Prefabs.SubNet>(type);
            for (int i=0;i<nets.Length;i++) { var n=nets[i]; n.m_Curve=Shift(n.m_Curve,Align(n.m_Curve.a),Align(n.m_Curve.d)); nets[i]=n; }
        }
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Buildings=GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>(),ComponentType.Exclude<Created>());
        }
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();
            if (ps==null) return;
            using (var buildings=m_Buildings.ToEntityArray(Allocator.Temp))
            foreach (var building in buildings)
            {
                var type=EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
                if(!World.GetOrCreateSystemManaged<ImportedHeliportFootprintSystem>().Ready(type)||!EntityManager.HasComponent<HelicopterAirportWidth>(building))continue;
                if (!ps.TryGetPrefab<BuildingPrefab>(type,out var prefab) || prefab==null ||
                    prefab.name!=ImportedHelipadMenuSystem.Airport || prefab.isBuiltin || HelicopterAssetOwnership.RejectForeignSubscription(prefab) ||
                    !prefab.Has<ImportedPassengerHeliport>()) continue;
                var root=EntityManager.GetComponentData<Game.Objects.Transform>(building);
                float3 WorldPoint(float3 p) {
                    var local=math.rotate(math.inverse(root.m_Rotation),p-root.m_Position);
                    var aligned=Align(local);
                    return math.distance(local,aligned)<.001f ? p : root.m_Position+math.rotate(root.m_Rotation,aligned);
                }
                if (EntityManager.HasBuffer<Game.Objects.SubObject>(building))
                {
                    using (var children=EntityManager.GetBuffer<Game.Objects.SubObject>(building,true).ToNativeArray(Allocator.Temp))
                    foreach (var child in children)
                    {
                        var e=child.m_SubObject;
                        if (!EntityManager.HasComponent<Game.Objects.Transform>(e) || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                        var objectType=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                        if (!ps.TryGetPrefab<MarkerObjectPrefab>(objectType,out var marker) || marker==null) continue;
                        var t=EntityManager.GetComponentData<Game.Objects.Transform>(e); var next=WorldPoint(t.m_Position);
                        if (math.distance(t.m_Position,next)<.01f) continue;
                        t.m_Position=next; EntityManager.SetComponentData(e,t); EntityManager.AddComponent<Updated>(e);
                        if (EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(e))
                        {
                            using (var routes=EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(e,true).ToNativeArray(Allocator.Temp))
                            foreach (var route in routes) EntityManager.AddComponent<Updated>(route.m_Waypoint);
                        }
                    }
                }
                if (!EntityManager.HasBuffer<Game.Net.SubNet>(building)) continue;
                using (var nets=EntityManager.GetBuffer<Game.Net.SubNet>(building,true).ToNativeArray(Allocator.Temp))
                foreach (var sub in nets)
                {
                    var e=sub.m_SubNet;
                    if (EntityManager.HasComponent<Deleted>(e)) continue;
                    if (EntityManager.HasComponent<Game.Net.Node>(e))
                    {
                        var n=EntityManager.GetComponentData<Game.Net.Node>(e); var next=WorldPoint(n.m_Position);
                        if (math.distance(n.m_Position,next)<.01f) continue;
                        n.m_Position=next; EntityManager.SetComponentData(e,n); EntityManager.AddComponent<Updated>(e);
                    }
                    else if (EntityManager.HasComponent<Game.Net.Curve>(e))
                    {
                        var c=EntityManager.GetComponentData<Game.Net.Curve>(e); var a=WorldPoint(c.m_Bezier.a); var d=WorldPoint(c.m_Bezier.d);
                        if (math.distance(c.m_Bezier.a,a)<.01f && math.distance(c.m_Bezier.d,d)<.01f) continue;
                        c.m_Bezier=Shift(c.m_Bezier,a,d); c.m_Length=MathUtils.Length(c.m_Bezier);
                        EntityManager.SetComponentData(e,c); EntityManager.AddComponent<Updated>(e);
                    }
                }
            }
        }
    }
}
