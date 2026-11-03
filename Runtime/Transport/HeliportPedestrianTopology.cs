using System;
using System.Collections.Generic;
using System.Linq;

namespace CityWeaver
{
    // Only receives the imported airport's owned Invisible Pedestrian Path.
    // Entity indices are lookup keys within this one synchronous ECS snapshot.
    internal static class HeliportPedestrianTopology
    {
        internal struct Point
        {
            public float X, Y, Z;
            public Point(float x, float y, float z) { X=x; Y=y; Z=z; }
            public bool Near(Point b) => Math.Abs(X-b.X)<.05f && Math.Abs(Y-b.Y)<.1f && Math.Abs(Z-b.Z)<.05f;
        }
        internal sealed class Node
        {
            public int Id;
            public Point Position;
            public bool External;
        }
        internal sealed class Edge
        {
            public int Id, Start, End;
            public Point[] Curve;
        }
        internal sealed class Plan
        {
            public readonly Dictionary<int,int> Nodes = new Dictionary<int,int>();
            public readonly HashSet<int> RemovedEdges = new HashSet<int>();
            public bool Changed => RemovedEdges.Count>0 || Nodes.Any(n=>n.Key!=n.Value);
        }
        public static Plan Analyze(IReadOnlyList<Node> nodes, IReadOnlyList<Edge> edges)
        {
            var plan=new Plan();
            var degree=nodes.ToDictionary(n=>n.Id,n=>edges.Count(e=>e.Start==n.Id||e.End==n.Id));
            var canonical=new List<Node>();
            foreach(var node in nodes.OrderByDescending(n=>n.External).ThenByDescending(n=>degree[n.Id]).ThenBy(n=>n.Id))
            {
                // Never collapse separate external road attachments.
                var match=canonical.FirstOrDefault(n=>!(n.External&&node.External)&&n.Position.Near(node.Position));
                plan.Nodes[node.Id]=match==null?node.Id:match.Id;
                if(match==null)canonical.Add(node);
            }
            var kept=new List<Edge>();
            // Prefer an edge already using the retained junctions, then its oldest ID.
            foreach(var edge in edges.Where(e=>plan.Nodes.ContainsKey(e.Start)&&plan.Nodes.ContainsKey(e.End))
                .OrderBy(e=>(plan.Nodes[e.Start]==e.Start?0:1)+(plan.Nodes[e.End]==e.End?0:1)).ThenBy(e=>e.Id))
            {
                bool Same(Edge other)
                {
                    bool forward=plan.Nodes[edge.Start]==plan.Nodes[other.Start]&&plan.Nodes[edge.End]==plan.Nodes[other.End];
                    bool reverse=plan.Nodes[edge.Start]==plan.Nodes[other.End]&&plan.Nodes[edge.End]==plan.Nodes[other.Start];
                    return (forward&&Enumerable.Range(0,4).All(i=>edge.Curve[i].Near(other.Curve[i]))) ||
                        (reverse&&Enumerable.Range(0,4).All(i=>edge.Curve[i].Near(other.Curve[3-i])));
                }
                if(kept.Any(Same))plan.RemovedEdges.Add(edge.Id);
                else kept.Add(edge);
            }
            return plan;
        }
    }
}
