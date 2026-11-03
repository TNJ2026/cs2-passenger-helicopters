using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // Runs immediately before RouteToolSystem, against the same snapped points
    // that its Apply method consumes. A private Error entity participates in the
    // native GetAllowApply gate, and is removed as soon as the preview is valid.
    public partial class PassengerHelicopterRouteToolPolicySystem : GameSystemBase
    {
        private static readonly FieldInfo Points = typeof(RouteToolSystem).GetField("m_ControlPoints", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo State = typeof(RouteToolSystem).GetField("m_State", BindingFlags.Instance | BindingFlags.NonPublic);
        private Entity m_Error;
        private EntityQuery m_Previews;
        public string LastError { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Previews = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<RouteWaypoint>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            var tools = World.GetExistingSystemManaged<ToolSystem>();
            var tool = tools?.activeTool as RouteToolSystem;
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            string error = null;
            if (tool?.GetPrefab()?.name != PassengerHelicopterSystem.Line && m_Error == Entity.Null)
            {
                LastError = null;
                return;
            }
            EntityManager.CompleteAllTrackedJobs();
            if (tool?.GetPrefab()?.name == PassengerHelicopterSystem.Line)
            {
                if (Points == null || State == null) error = "HELICOPTER_TOOL_API_UNAVAILABLE";
                else
                {
                    string state = State.GetValue(tool).ToString();
                    var points = (NativeList<ControlPoint>)Points.GetValue(tool);
                    bool extendsRoute = state == "Create" && points.IsCreated && points.Length > 0 &&
                        EntityManager.HasBuffer<RouteWaypoint>(points[0].m_OriginalEntity);
                    if ((state == "Default" || state == "Create") && !extendsRoute && points.IsCreated && points.Length > 0)
                    {
                        var stops = new List<Entity>();
                        for (int i = 0; i < points.Length; i++)
                        {
                            var point = points[i];
                            var stop = point.m_OriginalEntity;
                            // Clicking an existing route enters Modify, not a new route.
                            if (state == "Default" && EntityManager.HasComponent<Route>(stop)) break;
                            if (EntityManager.HasBuffer<RouteWaypoint>(stop) && point.m_ElementIndex.x >= 0)
                            {
                                var nodes = EntityManager.GetBuffer<RouteWaypoint>(stop, true);
                                if (point.m_ElementIndex.x < nodes.Length) stop = nodes[point.m_ElementIndex.x].m_Waypoint;
                            }
                            if (EntityManager.HasComponent<Connected>(stop)) stop = EntityManager.GetComponentData<Connected>(stop).m_Connected;
                            stops.Add(stop);
                        }
                        if (stops.Count > 0) error = PassengerHelicopterRoutePolicy.Validate(EntityManager, ps,
                            ps.GetEntity(tool.GetPrefab()), stops, false);
                    }
                    if (state == "Modify" || state == "Remove" || extendsRoute)
                        using (var previews = m_Previews.ToEntityArray(Allocator.Temp))
                            foreach (var preview in previews)
                            {
                                var temp = EntityManager.GetComponentData<Temp>(preview);
                                if ((temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0) continue;
                                var prefab = EntityManager.GetComponentData<PrefabRef>(preview).m_Prefab;
                                error = PassengerHelicopterRoutePolicy.Validate(EntityManager, ps, prefab,
                                    PassengerHelicopterRoutePolicy.Stops(EntityManager, preview),
                                    EntityManager.HasComponent<Route>(preview) &&
                                    (EntityManager.GetComponentData<Route>(preview).m_Flags & RouteFlags.Complete) != 0);
                                if (error != null) break;
                            }
                }
            }
            if (error != LastError && error != null) HelicopterRuntime.log.Info("Helicopter route blocked: " + error);
            LastError = error;
            if (error != null)
            {
                if (!EntityManager.Exists(m_Error)) m_Error = EntityManager.CreateEntity(typeof(Game.Tools.Error));
                else if (!EntityManager.HasComponent<Game.Tools.Error>(m_Error)) EntityManager.AddComponent<Game.Tools.Error>(m_Error);
            }
            else if (EntityManager.Exists(m_Error))
            {
                EntityManager.DestroyEntity(m_Error);
                m_Error = Entity.Null;
            }
        }
    }
}
