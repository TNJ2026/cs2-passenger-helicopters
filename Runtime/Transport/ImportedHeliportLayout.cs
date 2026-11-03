using System;
using System.Linq;
using Colossal.Mathematics;
using Game.Prefabs;
using Unity.Mathematics;

namespace CityWeaver
{
    // Versioned layout for the six-module TF3 conversion. Never applied to existing airports.
    internal static class ImportedHeliportLayout
    {
        internal const string Preset = "passenger_heliport_v7";
        private static readonly quaternion Rotation = quaternion.RotateY(-math.PI / 2);
        internal static float3 Point(float x, float y, float z)
        {
            var aligned = ImportedHeliportRules.AlignLegacyPad(x,z);
            var p = ImportedHeliportRules.Position(aligned.x,aligned.z);
            return new float3(p.x,y,p.z);
        }

        internal static void Validate(ObjectMeshInfo[] meshes)
        {
            if (meshes.Length != 6) throw new ArgumentException("Heliport V7 requires its six converted modules.");
            var min = new float3(float.MaxValue); var max = new float3(float.MinValue);
            foreach (var m in meshes)
            {
                if (math.lengthsq(m.m_Position) > .0001f || math.abs(m.m_Rotation.value.w) < .99999f)
                    throw new ArgumentException("Heliport V7 source transforms must be identity.");
                var b = ((RenderPrefab)m.m_Mesh).bounds;
                min = math.min(min, b.min); max = math.max(max, b.max);
            }
            if (!ImportedHeliportRules.Footprint(meshes.Length,min.x,min.z,max.x,max.z))
                throw new QueryException("HELIPORT_FOOTPRINT_MISMATCH",
                    $"Heliport native footprint expected (-50,-52 or -62)..(62,50), found ({min.x},{min.z})..({max.x},{max.z}).");
        }

        internal static BuildingPrefab Create(string name, ObjectMeshInfo[] meshes, PrefabSystem ps)
        {
            Validate(meshes);
            T Find<T>(string n) where T : PrefabBase
            {
                if (!ps.TryGetPrefab(new PrefabID(typeof(T).Name, n), out var p) || !(p is T value))
                    throw new InvalidOperationException("Required heliport template missing: " + n);
                return value;
            }
            var p = (BuildingPrefab)Find<BuildingPrefab>(PassengerHelicopterSystem.Airport).Clone(name);
            p.m_Meshes = meshes.Select(m => new ObjectMeshInfo { m_Mesh = m.m_Mesh,
                m_Position = Point(m.m_Position.x, m.m_Position.y, m.m_Position.z),
                m_Rotation = math.mul(Rotation, m.m_Rotation), m_RequireState = m.m_RequireState }).ToArray();
            var nativeMinZ = meshes.Min(m => ((RenderPrefab)m.m_Mesh).bounds.min.z);
            p.m_LotWidth = ImportedHeliportRules.LotWidth(nativeMinZ); p.m_LotDepth = 14;
            p.m_AccessType = BuildingAccessType.Front;
            // Remove template geometry and internal paths rather than keeping invisible old facilities.
            p.Remove<ObjectSubAreas>(); p.Remove<ObjectSubLanes>(); p.Remove<BuildingTerraformOverride>();
            p.AddOrGetComponent<ImportedPassengerHeliport>();
            var terra = p.AddOrGetComponent<BuildingTerraformOverride>();
            HeliportTerraformRules.Configure(terra);
            ObjectSubObjectInfo At(MarkerObjectPrefab marker, float x, float z) => new ObjectSubObjectInfo {
                m_Object = marker, m_Position = Point(x, .2f, z), m_Rotation = Rotation,
                m_ParentMesh = -1, m_Probability = 100 };
            var takeoff = Find<MarkerObjectPrefab>("Helicopter Takeoff Location");
            p.AddOrGetComponent<ObjectSubObjects>().m_SubObjects = new[] {
                At(Find<MarkerObjectPrefab>(PassengerHelicopterSystem.Stop), -3, 26),
                At(Find<MarkerObjectPrefab>("Integrated Helipad"), -32, 26),
                At(takeoff, 5, 26), At(takeoff, -40, 26) };
            ObjectSubNetInfo Path(NetPrefab net, float3 a, float3 b, int first, int last) => new ObjectSubNetInfo {
                m_NetPrefab = net, m_BezierCurve = new Bezier4x3(a, math.lerp(a,b,1f/3), math.lerp(a,b,2f/3), b),
                m_NodeIndex = new int2(first,last), m_ParentMesh = new int2(-1),
                m_Upgrades = Array.Empty<NetPieceRequirements>() };
            var foot = Find<PathwayPrefab>("Invisible Pedestrian Path");
            p.AddOrGetComponent<ObjectSubNets>().m_SubNets = new[] {
                Path(Find<TaxiwayPrefab>("Invisible Helicopter Taxiway"), Point(5,.2f,26), Point(-40,.2f,26), 0,1),
                Path(foot, Point(62,0,48), Point(20,.2f,48),10,11),
                Path(foot, Point(20,.2f,48), Point(20,.2f,26),11,12),
                Path(foot, Point(20,.2f,26), Point(-3,.2f,26),12,13) };
            return p;
        }
    }

}
