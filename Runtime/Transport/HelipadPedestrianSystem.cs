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
    // Preserve stop entities and route references. Replace only the station's
    // obsolete pedestrian approach using the native permanent-net pipeline.
    public partial class HelipadPedestrianSystem:GameSystemBase
    {
        private EntityQuery m_Buildings,m_Residents;
        private readonly Dictionary<Entity,ObjectSubNetInfo[]> m_Paths=new Dictionary<Entity,ObjectSubNetInfo[]>();
        private readonly HashSet<Entity> m_Done=new HashSet<Entity>();
        private readonly Dictionary<Entity,Entity> m_Pending=new Dictionary<Entity,Entity>();
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Buildings=GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Temp>());
            m_Residents=GetEntityQuery(ComponentType.ReadOnly<Game.Creatures.HumanCurrentLane>(),ComponentType.ReadWrite<Game.Pathfind.PathOwner>(),
                ComponentType.Exclude<Game.Creatures.CurrentVehicle>(),ComponentType.Exclude<Deleted>());
        }
        internal static float3 Waiting(float3 pad)
        {
            var p=HelipadPedestrianRules.Point(HelipadPedestrianRules.Source.Length-1,pad.x,pad.y,pad.z,HelipadLotRules.Width,HelipadLotRules.Depth);
            return new float3(p.x,p.y,p.z);
        }
        internal static ObjectSubNetInfo[] Paths(NetPrefab foot,float3 pad,float front)
        {
            // Keep the street entrance in line with the stair foot. Returning to
            // pad.x here makes descending passengers walk back across the lot.
            var points=new List<float3>{new float3(HelipadPedestrianRules.EntryX(pad.x,HelipadLotRules.Width),0,front)};
            var turn=HelipadPedestrianRules.EntryTurn(pad.x,front,HelipadLotRules.Width);
            points.Add(new float3(turn.x,turn.y,turn.z));
            for(int i=0;i<HelipadPedestrianRules.Source.Length;i++)
            {var p=HelipadPedestrianRules.Point(i,pad.x,pad.y,pad.z,HelipadLotRules.Width,HelipadLotRules.Depth);points.Add(new float3(p.x,p.y,p.z));}
            return Enumerable.Range(0,points.Count-1).Select(i=>new ObjectSubNetInfo{
                m_NetPrefab=foot,m_BezierCurve=Line(points[i],points[i+1]),m_NodeIndex=new int2(10+i,11+i),
                m_ParentMesh=new int2(ImportedHelipadRules.Parent(points[i].y),ImportedHelipadRules.Parent(points[i+1].y)),
                m_Upgrades=Array.Empty<NetPieceRequirements>()}).ToArray();
        }
        private static Bezier4x3 Line(float3 a,float3 d)=>new Bezier4x3(a,math.lerp(a,d,1f/3),math.lerp(a,d,2f/3),d);
        private static bool Same(Bezier4x3 a,Bezier4x3 b)=>
            (math.distance(a.a,b.a)<.08f&&math.distance(a.d,b.d)<.08f)||
            (math.distance(a.a,b.d)<.08f&&math.distance(a.d,b.a)<.08f);
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();if(ps==null)return;
            EntityManager.CompleteAllTrackedJobs();
            using(var buildings=m_Buildings.ToEntityArray(Allocator.Temp))foreach(var building in buildings)
            {
                if(!EntityManager.HasComponent<HelipadLotVersion>(building)||m_Done.Contains(building)||EntityManager.HasComponent<Updated>(building)||!EntityManager.HasBuffer<Game.Net.SubNet>(building))continue;
                var type=EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
                if(!ps.TryGetPrefab<BuildingPrefab>(type,out var p)||p==null||p.isBuiltin||p.isSubscribedMod||p.name!=ImportedHelipadMenuSystem.Station)continue;
                if(!m_Paths.TryGetValue(type,out var paths))
                {
                    var objects=p.GetComponent<ObjectSubObjects>()?.m_SubObjects;
                    var nets=p.GetComponent<ObjectSubNets>()?.m_SubNets;
                    if(objects==null||nets==null||!EntityManager.HasBuffer<Game.Prefabs.SubNet>(type))continue;
                    var stops=objects.Where(o=>o.m_Object!=null&&o.m_Object.name==PassengerHelicopterSystem.Stop).ToArray();
                    var foot=nets.FirstOrDefault(n=>n.m_NetPrefab!=null&&n.m_NetPrefab.name=="Invisible Pedestrian Path")?.m_NetPrefab;
                    if(stops.Length!=1||foot==null)continue;
                    paths=Paths(foot,stops[0].m_Position,p.m_LotDepth*4);
                    p.GetComponent<ObjectSubNets>().m_SubNets=nets.Where(n=>n.m_NetPrefab!=foot).Concat(paths).ToArray();
                    var runtime=EntityManager.GetBuffer<Game.Prefabs.SubNet>(type);var footEntity=ps.GetEntity(foot);
                    for(int i=runtime.Length-1;i>=0;i--)if(runtime[i].m_Prefab==footEntity)runtime.RemoveAt(i);
                    foreach(var n in paths)runtime.Add(new Game.Prefabs.SubNet{m_Prefab=footEntity,m_Curve=n.m_BezierCurve,m_NodeIndex=n.m_NodeIndex,m_ParentMesh=n.m_ParentMesh});
                    m_Paths.Add(type,paths);
                }
                if(m_Pending.TryGetValue(building,out var pending)&&EntityManager.Exists(pending)&&!EntityManager.HasComponent<Deleted>(pending))continue;
                m_Pending.Remove(building);
                var root=EntityManager.GetComponentData<Game.Objects.Transform>(building);
                var footType=ps.GetEntity(paths[0].m_NetPrefab);
                var curves=paths.Select(n=>Game.Objects.ObjectUtils.LocalToWorld(root.m_Position,root.m_Rotation,n.m_BezierCurve)).ToArray();
                var removed=new HashSet<Entity>();
                using(var nets=EntityManager.GetBuffer<Game.Net.SubNet>(building,true).ToNativeArray(Allocator.Temp))foreach(var sub in nets)
                {
                    var e=sub.m_SubNet;
                    if(EntityManager.HasComponent<Deleted>(e)||!EntityManager.HasComponent<PrefabRef>(e)||EntityManager.GetComponentData<PrefabRef>(e).m_Prefab!=footType)continue;
                    bool obsolete=false;
                    if(EntityManager.HasComponent<Game.Net.Curve>(e))obsolete=!curves.Any(c=>Same(c,EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier));
                    else if(EntityManager.HasComponent<Game.Net.Node>(e))
                    {var pos=EntityManager.GetComponentData<Game.Net.Node>(e).m_Position;obsolete=!curves.Any(c=>math.distance(c.a,pos)<.08f||math.distance(c.d,pos)<.08f);}
                    if(!obsolete)continue;
                    if(EntityManager.HasBuffer<Game.Net.SubLane>(e))foreach(var lane in EntityManager.GetBuffer<Game.Net.SubLane>(e,true))removed.Add(lane.m_SubLane);
                    EntityManager.AddComponent<Deleted>(e);
                }
                if(removed.Count>0){Refresh(building,removed);continue;}
                bool ready=true;
                for(int i=0;i<paths.Length;i++)if(!Ensure(building,footType,root,paths[i],curves[i])){ready=false;break;}
                if(!ready)continue;
                Refresh(building,new HashSet<Entity>());m_Done.Add(building);
                HelicopterRuntime.log.Info("Helipad pedestrian stairs and upper waiting platform ready: "+building);
            }
        }
        private Entity FindNode(Entity owner,Entity prefab,float3 position)
        {
            foreach(var sub in EntityManager.GetBuffer<Game.Net.SubNet>(owner,true))
            {var e=sub.m_SubNet;if(!EntityManager.HasComponent<Deleted>(e)&&EntityManager.HasComponent<Game.Net.Node>(e)&&
                EntityManager.HasComponent<PrefabRef>(e)&&EntityManager.GetComponentData<PrefabRef>(e).m_Prefab==prefab&&
                math.distance(EntityManager.GetComponentData<Game.Net.Node>(e).m_Position,position)<.08f)return e;}
            return Entity.Null;
        }
        private bool Ensure(Entity owner,Entity prefab,Game.Objects.Transform root,ObjectSubNetInfo net,Bezier4x3 curve)
        {
            foreach(var sub in EntityManager.GetBuffer<Game.Net.SubNet>(owner,true))
            {var e=sub.m_SubNet;if(!EntityManager.HasComponent<Deleted>(e)&&EntityManager.HasComponent<Game.Net.Curve>(e)&&
                EntityManager.HasComponent<PrefabRef>(e)&&EntityManager.GetComponentData<PrefabRef>(e).m_Prefab==prefab&&Same(EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier,curve))return true;}
            var definition=EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition,new CreationDefinition{m_Prefab=prefab,m_Owner=owner,m_RandomSeed=owner.Index,m_Flags=CreationFlags.Permanent});
            var course=new NetCourse{m_Curve=curve,m_Length=MathUtils.Length(curve),m_FixedIndex=-1};
            course.m_StartPosition=new CoursePos{m_Position=curve.a,m_Rotation=Game.Net.NetUtils.GetNodeRotation(MathUtils.StartTangent(curve),root.m_Rotation),
                m_CourseDelta=0,m_Elevation=net.m_BezierCurve.a.y,m_ParentMesh=net.m_ParentMesh.x,m_Entity=FindNode(owner,prefab,curve.a),m_Flags=CoursePosFlags.IsFirst|CoursePosFlags.DisableMerge};
            course.m_EndPosition=new CoursePos{m_Position=curve.d,m_Rotation=Game.Net.NetUtils.GetNodeRotation(MathUtils.EndTangent(curve),root.m_Rotation),
                m_CourseDelta=1,m_Elevation=net.m_BezierCurve.d.y,m_ParentMesh=net.m_ParentMesh.y,m_Entity=FindNode(owner,prefab,curve.d),m_Flags=CoursePosFlags.IsLast|CoursePosFlags.DisableMerge};
            EntityManager.AddComponentData(definition,course);EntityManager.AddComponent<Updated>(definition);m_Pending[owner]=definition;return false;
        }
        private void Refresh(Entity building,HashSet<Entity> lanes)
        {
            var queues=new HashSet<Entity>();
            if(EntityManager.HasBuffer<Game.Objects.SubObject>(building))
            using(var children=EntityManager.GetBuffer<Game.Objects.SubObject>(building,true).ToNativeArray(Allocator.Temp))foreach(var child in children)
            {
                var stop=child.m_SubObject;if(!EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(stop))continue;
                queues.Add(stop);EntityManager.AddComponent<Updated>(stop);
                using(var routes=EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(stop,true).ToNativeArray(Allocator.Temp))foreach(var r in routes)
                {queues.Add(r.m_Waypoint);EntityManager.AddComponent<Updated>(r.m_Waypoint);}
            }
            using(var people=m_Residents.ToEntityArray(Allocator.Temp))foreach(var human in people)
            {
                var lane=EntityManager.GetComponentData<Game.Creatures.HumanCurrentLane>(human);
                if(!lanes.Contains(lane.m_Lane)&&!queues.Contains(lane.m_QueueEntity))continue;
                var path=EntityManager.GetComponentData<Game.Pathfind.PathOwner>(human);path.m_State|=Game.Pathfind.PathFlags.Obsolete;EntityManager.SetComponentData(human,path);
            }
        }
    }
}
