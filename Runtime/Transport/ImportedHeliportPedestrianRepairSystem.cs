using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // Repair saved duplicate courses in place; stop, building and road identities
    // remain intact. Native lane/pathfinding systems rebuild the repaired graph.
    public partial class ImportedHeliportPedestrianRepairSystem : GameSystemBase
    {
        private EntityQuery m_Buildings, m_Humans;
        private readonly HashSet<Entity> m_Deferred = new HashSet<Entity>();
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Buildings=GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<HelicopterAirportWidth>(),
                ComponentType.ReadOnly<Game.Net.SubNet>(), ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Created>(), ComponentType.Exclude<Updated>(), ComponentType.Exclude<Game.Tools.Temp>());
            m_Humans=GetEntityQuery(ComponentType.ReadOnly<Game.Creatures.HumanCurrentLane>(), ComponentType.Exclude<Deleted>());
        }
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();
            if(ps==null)return;
            EntityManager.CompleteAllTrackedJobs();
            using(var buildings=m_Buildings.ToEntityArray(Allocator.Temp))foreach(var building in buildings)
            {
                if(!ps.TryGetPrefab<BuildingPrefab>(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab,out var prefab)||
                    prefab==null||prefab.name!=ImportedHelipadMenuSystem.Airport||prefab.isBuiltin||HelicopterAssetOwnership.RejectForeignSubscription(prefab)||
                    !prefab.Has<ImportedPassengerHeliport>())continue;
                Repair(building,ps);
            }
        }
        private void Repair(Entity building, PrefabSystem ps)
        {
            var entities=new Dictionary<int,Entity>();
            var nodes=new List<HeliportPedestrianTopology.Node>();
            var edges=new List<HeliportPedestrianTopology.Edge>();
            HeliportPedestrianTopology.Point Point(Unity.Mathematics.float3 p)=>new HeliportPedestrianTopology.Point(p.x,p.y,p.z);
            using(var subs=EntityManager.GetBuffer<Game.Net.SubNet>(building,true).ToNativeArray(Allocator.Temp))
            foreach(var sub in subs)
            {
                var entity=sub.m_SubNet;
                if(!EntityManager.Exists(entity)||EntityManager.HasComponent<Deleted>(entity)||
                    !EntityManager.HasComponent<Owner>(entity)||EntityManager.GetComponentData<Owner>(entity).m_Owner!=building||
                    !EntityManager.HasComponent<PrefabRef>(entity)||
                    !ps.TryGetPrefab<NetPrefab>(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab,out var prefab)||
                    prefab==null||prefab.name!="Invisible Pedestrian Path")continue;
                entities[entity.Index]=entity;
                if(EntityManager.HasComponent<Game.Net.Node>(entity))
                    nodes.Add(new HeliportPedestrianTopology.Node{Id=entity.Index,
                        Position=Point(EntityManager.GetComponentData<Game.Net.Node>(entity).m_Position),External=HasExternalEdge(entity,building)});
                else if(EntityManager.HasComponent<Game.Net.Edge>(entity)&&EntityManager.HasComponent<Game.Net.Curve>(entity))
                {
                    var edge=EntityManager.GetComponentData<Game.Net.Edge>(entity);
                    var c=EntityManager.GetComponentData<Game.Net.Curve>(entity).m_Bezier;
                    edges.Add(new HeliportPedestrianTopology.Edge{Id=entity.Index,Start=edge.m_Start.Index,End=edge.m_End.Index,
                        Curve=new[]{Point(c.a),Point(c.b),Point(c.c),Point(c.d)}});
                }
            }
            var nodeIds=new HashSet<int>(nodes.Select(n=>n.Id));
            if(edges.Any(e=>!nodeIds.Contains(e.Start)||!nodeIds.Contains(e.End)))return;
            var plan=HeliportPedestrianTopology.Analyze(nodes,edges);
            if(!plan.Changed)return;
            // Do not remove a lane underneath an actual pedestrian. Native path
            // invalidation handles queued paths after this occupied-lane guard.
            var lanes=new HashSet<Entity>();
            foreach(var entity in entities.Values)
                if(EntityManager.HasBuffer<Game.Net.SubLane>(entity))
                    foreach(var lane in EntityManager.GetBuffer<Game.Net.SubLane>(entity,true))lanes.Add(lane.m_SubLane);
            using(var humans=m_Humans.ToEntityArray(Allocator.Temp))foreach(var human in humans)
                if(lanes.Contains(EntityManager.GetComponentData<Game.Creatures.HumanCurrentLane>(human).m_Lane))
                {
                    if(m_Deferred.Add(building))HelicopterRuntime.log.Info("Airport pedestrian repair waiting for occupied lanes: "+building);
                    return;
                }
            m_Deferred.Remove(building);
            foreach(var edge in edges)
            {
                var entity=entities[edge.Id];
                if(plan.RemovedEdges.Contains(edge.Id))EntityManager.AddComponent<Deleted>(entity);
                else if(plan.Nodes.ContainsKey(edge.Start)&&plan.Nodes.ContainsKey(edge.End))
                {
                    var link=EntityManager.GetComponentData<Game.Net.Edge>(entity);
                    link.m_Start=entities[plan.Nodes[edge.Start]];link.m_End=entities[plan.Nodes[edge.End]];
                    EntityManager.SetComponentData(entity,link);
                    EntityManager.AddComponent<Updated>(entity);
                    EntityManager.AddComponent<PathfindUpdated>(entity);
                }
            }
            foreach(var node in nodes)
            {
                var entity=entities[node.Id];
                var adjacent=new HashSet<Entity>();
                if(plan.Nodes[node.Id]==node.Id)
                {
                    // Keep genuine external connections, discard stale copied
                    // adjacency entries whose Edge never references this node.
                    if(EntityManager.HasBuffer<Game.Net.ConnectedEdge>(entity))
                        foreach(var item in EntityManager.GetBuffer<Game.Net.ConnectedEdge>(entity,true))
                            if(LiveEdgeTouches(item.m_Edge,entity)&&!entities.ContainsKey(item.m_Edge.Index))adjacent.Add(item.m_Edge);
                    foreach(var edge in edges)
                        if(!plan.RemovedEdges.Contains(edge.Id)&&plan.Nodes.ContainsKey(edge.Start)&&plan.Nodes.ContainsKey(edge.End)&&
                            (plan.Nodes[edge.Start]==node.Id||plan.Nodes[edge.End]==node.Id))adjacent.Add(entities[edge.Id]);
                }
                var connected=EntityManager.HasBuffer<Game.Net.ConnectedEdge>(entity)?
                    EntityManager.GetBuffer<Game.Net.ConnectedEdge>(entity):EntityManager.AddBuffer<Game.Net.ConnectedEdge>(entity);
                connected.Clear();
                foreach(var edge in adjacent)connected.Add(new Game.Net.ConnectedEdge(edge));
                if(plan.Nodes[node.Id]!=node.Id)EntityManager.AddComponent<Deleted>(entity);
                else {EntityManager.AddComponent<Updated>(entity);EntityManager.AddComponent<PathfindUpdated>(entity);}
            }
            // Remove only obsolete pedestrian members. Taxiways and utilities
            // are not included in the planner and are left as they were.
            var members=EntityManager.GetBuffer<Game.Net.SubNet>(building);
            for(int i=members.Length-1;i>=0;i--)
            {
                int id=members[i].m_SubNet.Index;
                if(plan.RemovedEdges.Contains(id)||(plan.Nodes.TryGetValue(id,out var canonical)&&canonical!=id))members.RemoveAt(i);
            }
            EntityManager.AddComponent<Updated>(building);
            if(EntityManager.HasBuffer<Game.Objects.SubObject>(building))
                using(var children=EntityManager.GetBuffer<Game.Objects.SubObject>(building,true).ToNativeArray(Allocator.Temp))
                    foreach(var child in children)
                    {
                        var stop=child.m_SubObject;
                        if(!EntityManager.HasComponent<Game.Routes.TransportStop>(stop))continue;
                        EntityManager.AddComponent<Updated>(stop);
                        if(EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(stop))
                            using(var routes=EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(stop,true).ToNativeArray(Allocator.Temp))
                            foreach(var route in routes)
                                if(EntityManager.Exists(route.m_Waypoint)&&!EntityManager.HasComponent<Deleted>(route.m_Waypoint))
                                    EntityManager.AddComponent<Updated>(route.m_Waypoint);
                    }
            HelicopterRuntime.log.Info("Airport pedestrian topology repaired in place: "+building+", duplicate edges="+plan.RemovedEdges.Count+
                ", duplicate nodes="+plan.Nodes.Count(n=>n.Key!=n.Value));
        }
        private bool LiveEdgeTouches(Entity edge,Entity node)
        {
            if(!EntityManager.HasComponent<Game.Net.Edge>(edge)||EntityManager.HasComponent<Deleted>(edge))return false;
            var data=EntityManager.GetComponentData<Game.Net.Edge>(edge);
            if(data.m_Start==node||data.m_End==node)return true;
            // Building entrances attach to the middle of a road edge through
            // ConnectedNode, rather than replacing either road endpoint.
            if(EntityManager.HasBuffer<Game.Net.ConnectedNode>(edge))
                foreach(var attached in EntityManager.GetBuffer<Game.Net.ConnectedNode>(edge,true))
                    if(attached.m_Node==node)return true;
            return false;
        }
        private bool HasExternalEdge(Entity node,Entity building)
        {
            if(!EntityManager.HasBuffer<Game.Net.ConnectedEdge>(node))return false;
            foreach(var item in EntityManager.GetBuffer<Game.Net.ConnectedEdge>(node,true))
                if(LiveEdgeTouches(item.m_Edge,node)&&(!EntityManager.HasComponent<Owner>(item.m_Edge)||
                    EntityManager.GetComponentData<Owner>(item.m_Edge).m_Owner!=building))return true;
            return false;
        }
    }
}
