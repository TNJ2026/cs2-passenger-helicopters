using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // Rebind all six modules together; migrate local coordinates without replacing stops.
    public partial class ImportedHeliportFootprintSystem : GameSystemBase
    {
        internal static readonly string[] Renders={
            "CityWeaver_bb6e274887ec4c5ea8bef7d725e3313d_0_CWHeliportPart01 Mesh",
            "CityWeaver_ca89b95a71b64787a5ead4ea15f5ecc1_0_CWHangarAnimatedPart09 Mesh",
            "CityWeaver_7ecb90d4a10f4ae1b921aade4455da6e_0_CWHangarAnimatedPart17 Mesh",
            "CityWeaver_22b0ac34577b451caa08a343bc489fd7_0_CWHeliportSiteAsphalt Mesh",
            "CityWeaver_7ed34b9a7a2543adb99b382a79255ecd_0_CWHeliportTerminalPart01 Mesh",
            "CityWeaver_5867035097c349bc821bf786d28376fb_0_CWHeliportTerminalPart09 Mesh"};
        private EntityQuery m_Types,m_Buildings,m_Renders;
        private readonly HashSet<Entity> m_Done=new HashSet<Entity>(),m_Refreshed=new HashSet<Entity>();
        private readonly RenderPrefab[] m_Render=new RenderPrefab[6];
        internal bool Ready(Entity type)=>m_Done.Contains(type);
        internal static float3 Scale(float3 p){p.x*=HeliportWidthRules.Factor;return p;}
        internal static float3 Point(float x,float y,float z)=>Scale(ImportedHeliportLayout.Point(x,y,z));
        private static Bezier4x3 Scale(Bezier4x3 c)=>new Bezier4x3(Scale(c.a),Scale(c.b),Scale(c.c),Scale(c.d));
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Types=GetEntityQuery(ComponentType.ReadOnly<BuildingData>(),ComponentType.ReadOnly<PrefabData>(),ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>());
            m_Renders=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<MeshData>(),ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>());
            m_Buildings=GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Game.Objects.Transform>(),ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            if(!HelicopterModelPolicy.ImportedModelsEnabled){Enabled=false;return;}
            var ps=World.GetExistingSystemManaged<PrefabSystem>();if(ps==null)return;
            if(m_Render.Any(p=>p==null))using(var renders=m_Renders.ToEntityArray(Allocator.Temp))foreach(var e in renders)
            {
                if(!ps.TryGetPrefab<RenderPrefab>(e,out var p)||p==null||p.isBuiltin||HelicopterAssetOwnership.RejectForeignSubscription(p))continue;
                int i=System.Array.IndexOf(Renders,p.name);if(i>=0)m_Render[i]=p;
            }
            if(m_Render.Any(p=>p==null||!ps.TryGetEntity(p,out var e)||EntityManager.HasComponent<Created>(e)))return;
            EntityManager.CompleteAllTrackedJobs();
            using(var types=m_Types.ToEntityArray(Allocator.Temp))foreach(var type in types)
            {
                if(m_Done.Contains(type)||!ps.TryGetPrefab<BuildingPrefab>(type,out var p)||p==null||p.name!=ImportedHelipadMenuSystem.Airport||p.isBuiltin||HelicopterAssetOwnership.RejectForeignSubscription(p))continue;
                if(p.m_Meshes==null||p.m_Meshes.Length<6||p.m_Meshes.Length>7||!EntityManager.HasBuffer<SubMesh>(type)||!EntityManager.HasBuffer<Game.Prefabs.SubObject>(type)||!EntityManager.HasBuffer<Game.Prefabs.SubNet>(type))continue;
                if(p.m_Meshes.Length==7&&!p.m_Meshes[6].m_Mesh.name.Contains("CWHeliportLotBorder104"))continue;
                bool already=p.m_Meshes[0].m_Mesh==m_Render[0];
                if(!already&&!p.m_Meshes[0].m_Mesh.name.Contains("6ccc33e623ef471a9a5d9724aeeeb316"))continue;
                var objects=p.GetComponent<ObjectSubObjects>()?.m_SubObjects;var nets=p.GetComponent<ObjectSubNets>()?.m_SubNets;
                if(objects==null||nets==null)continue;
                var meshes=EntityManager.GetBuffer<SubMesh>(type);if(meshes.Length!=p.m_Meshes.Length)continue;
                p.m_Meshes=p.m_Meshes.Take(6).ToArray();
                while(meshes.Length>6)meshes.RemoveAt(meshes.Length-1);
                for(int i=0;i<6;i++){
                    p.m_Meshes[i].m_Mesh=m_Render[i];
                    if(!already)p.m_Meshes[i].m_Position=Scale(p.m_Meshes[i].m_Position);
                    var m=meshes[i];m.m_SubMesh=ps.GetEntity(m_Render[i]);m.m_Position=p.m_Meshes[i].m_Position;meshes[i]=m;
                }
                if(!already){
                    foreach(var o in objects)o.m_Position=Scale(o.m_Position);
                    foreach(var n in nets)n.m_BezierCurve=Scale(n.m_BezierCurve);
                    var runtime=EntityManager.GetBuffer<Game.Prefabs.SubObject>(type);
                    for(int i=0;i<runtime.Length;i++){var o=runtime[i];o.m_Position=Scale(o.m_Position);runtime[i]=o;}
                    var paths=EntityManager.GetBuffer<Game.Prefabs.SubNet>(type);
                    for(int i=0;i<paths.Length;i++){var n=paths[i];n.m_Curve=Scale(n.m_Curve);paths[i]=n;}
                }
                p.m_LotWidth=13;p.m_LotDepth=14;
                var data=EntityManager.GetComponentData<BuildingData>(type);data.m_LotSize=new int2(13,14);EntityManager.SetComponentData(type,data);
                var geometry=EntityManager.GetComponentData<ObjectGeometryData>(type);
                geometry.m_Bounds.min.xz=new float2(-51.8f,-55.8f);geometry.m_Bounds.max.xz=new float2(51.8f,55.8f);
                geometry.m_Size.xz=new float2(103.6f,111.6f);EntityManager.SetComponentData(type,geometry);
                var terra=EntityManager.GetComponentData<BuildingTerraformData>(type);
                var bounds=new Bounds2(new float2(-52,-56),new float2(52,56));
                // Imported native assets carry the old eight-metre apron. Apply
                // the same policy here as for newly constructed prefab layouts.
                var terrainOverride=p.AddOrGetComponent<BuildingTerraformOverride>();
                HeliportTerraformRules.Configure(terrainOverride);
                BuildingInitializeSystem.InitializeTerraformData(terrainOverride,ref terra,bounds,bounds);
                EntityManager.SetComponentData(type,terra);m_Done.Add(type);
                HelicopterRuntime.log.Info("Airport width scaled 104/102 with six modules; border removed: "+type);
            }
            using(var buildings=m_Buildings.ToEntityArray(Allocator.Temp))foreach(var building in buildings)
            {
                if(!Ready(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab)||m_Refreshed.Contains(building))continue;
                if(!Migrate(building))continue;
                if(!EntityManager.HasComponent<BatchesUpdated>(building))EntityManager.AddComponent<BatchesUpdated>(building);
                if(!EntityManager.HasComponent<Updated>(building))EntityManager.AddComponent<Updated>(building);
                m_Refreshed.Add(building);
            }
            m_Refreshed.RemoveWhere(e=>!EntityManager.Exists(e)||EntityManager.HasComponent<Deleted>(e));
        }
        private bool Migrate(Entity building)
        {
            if(EntityManager.HasComponent<HelicopterAirportWidth>(building))return true;
            if(!EntityManager.HasBuffer<Game.Objects.SubObject>(building)||!EntityManager.HasBuffer<Game.Net.SubNet>(building))return false;
            var root=EntityManager.GetComponentData<Game.Objects.Transform>(building);
            float3 Local(float3 p)=>math.rotate(math.inverse(root.m_Rotation),p-root.m_Position);
            float3 Map(float3 p)=>root.m_Position+math.rotate(root.m_Rotation,Scale(Local(p)));
            bool found=false,scaled=false;
            foreach(var sub in EntityManager.GetBuffer<Game.Objects.SubObject>(building,true)){
                var e=sub.m_SubObject;
                if(!EntityManager.HasComponent<Game.Routes.TransportStop>(e)||!EntityManager.HasComponent<Game.Objects.Transform>(e))continue;
                var local=Local(EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position);
                int state=HeliportWidthRules.LayoutState(local.x);if(state<0)return false;
                found=true;scaled=state==1;break;
            }
            if(!found)return false;
            if(!scaled){
                using(var children=EntityManager.GetBuffer<Game.Objects.SubObject>(building,true).ToNativeArray(Allocator.Temp))foreach(var sub in children){
                    var e=sub.m_SubObject;if(!EntityManager.HasComponent<Game.Objects.Transform>(e))continue;
                    var t=EntityManager.GetComponentData<Game.Objects.Transform>(e);t.m_Position=Map(t.m_Position);EntityManager.SetComponentData(e,t);
                    if(EntityManager.HasComponent<Game.Tools.LocalTransformCache>(e)){var c=EntityManager.GetComponentData<Game.Tools.LocalTransformCache>(e);c.m_Position=Scale(c.m_Position);EntityManager.SetComponentData(e,c);}
                    EntityManager.AddComponent<Updated>(e);
                    if(EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(e))using(var routes=EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(e,true).ToNativeArray(Allocator.Temp))foreach(var route in routes)
                        if(EntityManager.Exists(route.m_Waypoint))EntityManager.AddComponent<Updated>(route.m_Waypoint);
                }
                using(var nets=EntityManager.GetBuffer<Game.Net.SubNet>(building,true).ToNativeArray(Allocator.Temp))foreach(var sub in nets){
                    var e=sub.m_SubNet;if(EntityManager.HasComponent<Deleted>(e))continue;
                    if(EntityManager.HasComponent<Game.Net.Node>(e)){var n=EntityManager.GetComponentData<Game.Net.Node>(e);n.m_Position=Map(n.m_Position);EntityManager.SetComponentData(e,n);EntityManager.AddComponent<Updated>(e);}
                    if(EntityManager.HasComponent<Game.Net.Curve>(e)){var c=EntityManager.GetComponentData<Game.Net.Curve>(e);var b=c.m_Bezier;c.m_Bezier=new Bezier4x3(Map(b.a),Map(b.b),Map(b.c),Map(b.d));c.m_Length=MathUtils.Length(c.m_Bezier);EntityManager.SetComponentData(e,c);EntityManager.AddComponent<Updated>(e);}
                }
            }
            EntityManager.AddComponentData(building,new HelicopterAirportWidth{Version=1});return true;
        }
    }
}
