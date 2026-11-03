using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // Append to the owned twin-pad asset in place. Never UpdatePrefab: that can
    // replace saved boarding entities and invalidate ConnectedRoute references.
    public partial class ImportedHeliportDualStopSystem : GameSystemBase
    {
        private EntityQuery m_Prefabs, m_Buildings;
        private readonly HashSet<Entity> m_Configured = new HashSet<Entity>();
        private readonly HashSet<Entity> m_Migrated = new HashSet<Entity>();
        private readonly Dictionary<Entity, ObjectSubNetInfo[]> m_ExtraNets = new Dictionary<Entity, ObjectSubNetInfo[]>();
        private readonly HeliportNetCreationGate<Entity, ObjectSubNetInfo> m_PendingNets = new HeliportNetCreationGate<Entity, ObjectSubNetInfo>();
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = GetEntityQuery(ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<PrefabData>(),
                ComponentType.Exclude<Created>(), ComponentType.Exclude<Deleted>());
            m_Buildings = GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Created>());
        }
        protected override void OnUpdate()
        {
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            if (ps == null) return;
            using (var types = m_Prefabs.ToEntityArray(Allocator.Temp))
                foreach (var type in types)
                {
                    if (!World.GetOrCreateSystemManaged<ImportedHeliportFootprintSystem>().Ready(type) || m_Configured.Contains(type) || !ps.TryGetPrefab<BuildingPrefab>(type, out var p) || p == null ||
                        p.name != ImportedHelipadMenuSystem.Airport || p.isBuiltin || HelicopterAssetOwnership.RejectForeignSubscription(p) ||
                        !p.Has<ImportedPassengerHeliport>() || !EntityManager.HasBuffer<Game.Prefabs.SubObject>(type) ||
                        !EntityManager.HasBuffer<Game.Prefabs.SubNet>(type)) continue;
                    Configure(type, p, ps);
                }
            using (var buildings = m_Buildings.ToEntityArray(Allocator.Temp))
                foreach (var building in buildings)
                {
                    var type = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
                    if (!EntityManager.HasComponent<HelicopterAirportWidth>(building) || !m_Configured.Contains(type) || m_Migrated.Contains(building) ||
                        EntityManager.HasComponent<Updated>(building) ||
                        !EntityManager.HasBuffer<Game.Objects.SubObject>(building) ||
                        !EntityManager.HasBuffer<Game.Net.SubNet>(building)) continue;
                    // Native SubObjectSystem matches existing objects by prefab and
                    // nearest position. Original stop stays first and unchanged.
                    MigrateTaxiways(building, ps);
                    // Submit one missing edge per native tool pass, so the next
                    // edge can reuse its newly created junction rather than making
                    // two disconnected nodes at the same position.
                    bool ready=true;
                    foreach (var net in m_ExtraNets[type])
                        if (!EnsureNet(building, net, ps)) { ready=false; break; }
                    if (!ready) continue;
                    if (!EntityManager.HasComponent<Updated>(building)) EntityManager.AddComponent<Updated>(building);
                    RefreshStops(building);
                    m_Migrated.Add(building);
                    HelicopterRuntime.log.Info("Twin-pad airport independent taxiways migrated: " + building);
                }
        }

        private void Configure(Entity type, BuildingPrefab p, PrefabSystem ps)
        {
            ImportedHeliportPadAlignmentSystem.AlignPrefab(EntityManager, type, p);
            var objects = p.GetComponent<ObjectSubObjects>().m_SubObjects.ToList();
            var original = objects.FirstOrDefault(o => o.m_Object != null && o.m_Object.name == PassengerHelicopterSystem.Stop);
            var depot = objects.FirstOrDefault(o => o.m_Object != null && o.m_Object.name == "Integrated Helipad");
            var takeoff = objects.FirstOrDefault(o => o.m_Object != null && o.m_Object.name == "Helicopter Takeoff Location");
            if (original == null || depot == null || takeoff == null) return;
            var second = ImportedHeliportFootprintSystem.Point(ImportedHeliportRules.SecondPadX, .2f, ImportedHeliportRules.SecondPadZ);
            var parking = ImportedHeliportFootprintSystem.Point(ImportedHeliportRules.DepotX, .2f, ImportedHeliportRules.DepotZ);
            var exit = ImportedHeliportFootprintSystem.Point(-40, .2f, 4);
            ObjectSubObjectInfo At(ObjectPrefab prefab, float3 point) => new ObjectSubObjectInfo {
                m_Object = prefab, m_Position = point, m_Rotation = original.m_Rotation,
                m_ParentMesh = -1, m_Probability = 100 };
            var runtime = EntityManager.GetBuffer<Game.Prefabs.SubObject>(type);
            // Keep indices of all original objects; append the new stop and exit.
            for (int i = 0; i < runtime.Length; i++)
                if (runtime[i].m_Prefab == ps.GetEntity(depot.m_Object))
                { var item = runtime[i]; item.m_Position = parking; runtime[i] = item; }
            depot.m_Position = parking;
            void Append(ObjectSubObjectInfo item)
            {
                var prefab = ps.GetEntity(item.m_Object);
                if (objects.Any(o => o.m_Object == item.m_Object && math.distance(o.m_Position, item.m_Position) < .1f)) return;
                objects.Add(item);
                runtime.Add(new Game.Prefabs.SubObject { m_Prefab = prefab, m_Position = item.m_Position,
                    m_Rotation = item.m_Rotation, m_ParentIndex = -1, m_Probability = 100, m_Flags = SubObjectFlags.OnGround });
            }
            Append(At(original.m_Object, second));
            Append(At(takeoff.m_Object, exit));
            Append(At(takeoff.m_Object, TaxiPoint(4)));
            p.GetComponent<ObjectSubObjects>().m_SubObjects = objects.ToArray();
            var nets = p.GetComponent<ObjectSubNets>().m_SubNets.ToList();
            var foot = nets.First(n => n.m_NetPrefab.name == "Invisible Pedestrian Path").m_NetPrefab;
            var taxi = nets.First(n => n.m_NetPrefab.name == "Invisible Helicopter Taxiway").m_NetPrefab;
            ObjectSubNetInfo Path(NetPrefab prefab, float3 a, float3 b, int first, int last) => new ObjectSubNetInfo {
                m_NetPrefab = prefab, m_BezierCurve = new Bezier4x3(a, math.lerp(a,b,1f/3), math.lerp(a,b,2f/3), b),
                m_NodeIndex = new int2(first,last), m_ParentMesh = new int2(-1), m_Upgrades = Array.Empty<NetPieceRequirements>() };
            // Replace legacy taxi definitions, keeping pedestrian paths intact.
            // Existing world edges are migrated below without replacing stops.
            var taxiPaths=ImportedHeliportRules.TaxiEdges.Select(e=>Path(taxi,TaxiPoint(e.a),TaxiPoint(e.b),e.a,e.b)).ToArray();
            var extra = new[] {
                Path(foot, ImportedHeliportFootprintSystem.Point(20,.2f,48), ImportedHeliportFootprintSystem.Point(-32,.2f,48),11,14),
                Path(foot, ImportedHeliportFootprintSystem.Point(-32,.2f,48), second,14,15) }.Concat(taxiPaths).ToArray();
            var runtimeNets = EntityManager.GetBuffer<Game.Prefabs.SubNet>(type);
            nets.RemoveAll(n=>n.m_NetPrefab==taxi);
            for(int i=runtimeNets.Length-1;i>=0;i--)
                if(runtimeNets[i].m_Prefab==ps.GetEntity(taxi))runtimeNets.RemoveAt(i);
            foreach (var n in extra)
            {
                if (nets.Any(v => v.m_NetPrefab == n.m_NetPrefab && math.distance(v.m_BezierCurve.a,n.m_BezierCurve.a)<.1f &&
                    math.distance(v.m_BezierCurve.d,n.m_BezierCurve.d)<.1f)) continue;
                nets.Add(n);
                runtimeNets.Add(new Game.Prefabs.SubNet { m_Prefab=ps.GetEntity(n.m_NetPrefab), m_Curve=n.m_BezierCurve,
                    m_NodeIndex=n.m_NodeIndex, m_ParentMesh=n.m_ParentMesh });
            }
            p.GetComponent<ObjectSubNets>().m_SubNets = nets.ToArray();
            m_ExtraNets[type] = extra;
            m_Configured.Add(type);
        }

        private bool EnsureNet(Entity owner, ObjectSubNetInfo net, PrefabSystem ps)
        {
            var transform = EntityManager.GetComponentData<Game.Objects.Transform>(owner);
            var curve = Game.Objects.ObjectUtils.LocalToWorld(transform.m_Position, transform.m_Rotation, net.m_BezierCurve);
            var prefab = ps.GetEntity(net.m_NetPrefab);
            var subnets = EntityManager.GetBuffer<Game.Net.SubNet>(owner);
            foreach (var sub in subnets)
            {
                var e = sub.m_SubNet;
                if (!EntityManager.HasComponent<Game.Net.Curve>(e) || !EntityManager.HasComponent<PrefabRef>(e) ||
                    EntityManager.HasComponent<Deleted>(e) || EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != prefab) continue;
                var existing = EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
                if ((math.distance(existing.a,curve.a)<1 && math.distance(existing.d,curve.d)<1) ||
                    (math.distance(existing.d,curve.a)<1 && math.distance(existing.a,curve.d)<1))
                {
                    m_PendingNets.Observe(owner, net);
                    return true;
                }
            }
            // A native creation definition can be consumed before its permanent
            // edge appears in SubNet. Wait for that edge, not definition lifetime.
            // Retrying in this gap creates overlapping, disconnected junctions.
            if (!m_PendingNets.TrySubmit(owner, net)) return false;
            // Same permanent creation pipeline used by native BuildingConstructionSystem.
            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition { m_Prefab=prefab, m_Owner=owner,
                m_RandomSeed=owner.Index, m_Flags=CreationFlags.Permanent });
            var course = new NetCourse { m_Curve=curve, m_Length=MathUtils.Length(curve), m_FixedIndex=-1 };
            course.m_StartPosition = new CoursePos { m_Position=curve.a,
                m_Rotation=Game.Net.NetUtils.GetNodeRotation(MathUtils.StartTangent(curve),transform.m_Rotation),
                m_CourseDelta=0, m_Elevation=net.m_BezierCurve.a.y, m_ParentMesh=-1,
                m_Flags=CoursePosFlags.IsFirst | CoursePosFlags.DisableMerge };
            course.m_EndPosition = new CoursePos { m_Position=curve.d,
                m_Rotation=Game.Net.NetUtils.GetNodeRotation(MathUtils.EndTangent(curve),transform.m_Rotation),
                m_CourseDelta=1, m_Elevation=net.m_BezierCurve.d.y, m_ParentMesh=-1,
                m_Flags=CoursePosFlags.IsLast | CoursePosFlags.DisableMerge };
            // Explicitly reuse existing branch junctions in saved airports. New
            // junctions between queued courses are resolved by native generation.
            course.m_StartPosition.m_Entity = FindNode(owner, prefab, curve.a);
            course.m_EndPosition.m_Entity = FindNode(owner, prefab, curve.d);
            EntityManager.AddComponentData(definition, course);
            EntityManager.AddComponent<Updated>(definition);
            return false;
        }

        private static float3 TaxiPoint(int node)
        {
            var p=ImportedHeliportRules.TaxiNode(node);
            return ImportedHeliportFootprintSystem.Point(p.x,.2f,p.z);
        }

        private void MigrateTaxiways(Entity owner, PrefabSystem ps)
        {
            var root=EntityManager.GetComponentData<Game.Objects.Transform>(owner);
            float3 ToWorld(float3 p)=>root.m_Position+math.rotate(root.m_Rotation,p);
            // Node 1 used to be at the far end of the common pad row (now node 5).
            // Only match the two exact legacy edges. A new node 5 at the old
            // position MUST remain there when this city is loaded again.
            var nextJunction=ToWorld(TaxiPoint(1));
            using(var subs=EntityManager.GetBuffer<Game.Net.SubNet>(owner,true).ToNativeArray(Allocator.Temp))
            foreach(var sub in subs)
            {
                var e=sub.m_SubNet;
                if(EntityManager.HasComponent<Deleted>(e)||!EntityManager.HasComponent<Game.Net.Edge>(e)||
                    !EntityManager.HasComponent<Game.Net.Curve>(e)||!EntityManager.HasComponent<PrefabRef>(e))continue;
                if(!ps.TryGetPrefab<NetPrefab>(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab,out var prefab)||prefab.name!="Invisible Helicopter Taxiway")continue;
                var c=EntityManager.GetComponentData<Game.Net.Curve>(e);var edge=EntityManager.GetComponentData<Game.Net.Edge>(e);
                // The old rear connector (5 -> 2) is also part of the new layout.
                // Move it only when its junction is shared with the legacy long edge.
                var aLocal=math.rotate(math.inverse(root.m_Rotation),c.m_Bezier.a-root.m_Position);
                var dLocal=math.rotate(math.inverse(root.m_Rotation),c.m_Bezier.d-root.m_Position);
                int end=ImportedHeliportRules.LegacyTaxiJunctionEnd(aLocal.x/HeliportWidthRules.Factor,aLocal.z,dLocal.x/HeliportWidthRules.Factor,dLocal.z);
                if(end<0)continue;
                var junction=end==0?edge.m_Start:edge.m_End;
                if(!EntityManager.HasComponent<Game.Net.Node>(junction))continue;
                var node=EntityManager.GetComponentData<Game.Net.Node>(junction);
                node.m_Position=new float3(nextJunction.x,node.m_Position.y,nextJunction.z);
                EntityManager.SetComponentData(junction,node);EntityManager.AddComponent<Updated>(junction);
                foreach(var connected in subs)
                {
                    var ce=connected.m_SubNet;
                    if(EntityManager.HasComponent<Deleted>(ce)||!EntityManager.HasComponent<Game.Net.Edge>(ce)||!EntityManager.HasComponent<Game.Net.Curve>(ce))continue;
                    var link=EntityManager.GetComponentData<Game.Net.Edge>(ce);
                    if(link.m_Start!=junction&&link.m_End!=junction)continue;
                    var curve=EntityManager.GetComponentData<Game.Net.Curve>(ce);
                    var a=link.m_Start==junction?node.m_Position:curve.m_Bezier.a;
                    var d=link.m_End==junction?node.m_Position:curve.m_Bezier.d;
                    curve.m_Bezier=new Bezier4x3(a,math.lerp(a,d,1f/3),math.lerp(a,d,2f/3),d);
                    curve.m_Length=MathUtils.Length(curve.m_Bezier);
                    EntityManager.SetComponentData(ce,curve);EntityManager.AddComponent<Updated>(ce);
                }
                RefreshStops(owner);
                break;
            }
        }

        private void RefreshStops(Entity owner)
        {
            using(var children=EntityManager.GetBuffer<Game.Objects.SubObject>(owner,true).ToNativeArray(Allocator.Temp))
            foreach(var child in children)
            {
                var stop=child.m_SubObject;
                if(!EntityManager.HasComponent<Game.Routes.TransportStop>(stop))continue;
                EntityManager.AddComponent<Updated>(stop);
                if(!EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(stop))continue;
                using(var routes=EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(stop,true).ToNativeArray(Allocator.Temp))
                foreach(var route in routes)
                    if(EntityManager.Exists(route.m_Waypoint)&&!EntityManager.HasComponent<Deleted>(route.m_Waypoint))
                        EntityManager.AddComponent<Updated>(route.m_Waypoint);
            }
        }

        private Entity FindNode(Entity owner, Entity prefab, float3 position)
        {
            foreach (var sub in EntityManager.GetBuffer<Game.Net.SubNet>(owner))
            {
                var e = sub.m_SubNet;
                if (EntityManager.HasComponent<Game.Net.Node>(e) && EntityManager.HasComponent<PrefabRef>(e) &&
                    !EntityManager.HasComponent<Deleted>(e) && EntityManager.GetComponentData<PrefabRef>(e).m_Prefab == prefab &&
                    math.distance(EntityManager.GetComponentData<Game.Net.Node>(e).m_Position, position) < 1f) return e;
            }
            return Entity.Null;
        }
    }
}
