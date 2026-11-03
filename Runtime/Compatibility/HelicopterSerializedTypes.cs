using Colossal.Serialization.Entities;
using Game.Prefabs;
using Unity.Entities;

namespace CityWeaver
{
    // These identities intentionally stay in CityWeaver.dll. Existing cities and
    // imported prefabs serialize both the namespace and the assembly name.
    public struct HelipadLotVersion : IComponentData, ISerializable
    {
        public int Version;
        public void Serialize<T>(T writer) where T : IWriter { writer.Write(Version); }
        public void Deserialize<T>(T reader) where T : IReader { reader.Read(out Version); }
    }

    public struct HelicopterAirportWidth : IComponentData, ISerializable
    {
        public int Version;
        public void Serialize<T>(T writer) where T : IWriter { writer.Write(Version); }
        public void Deserialize<T>(T reader) where T : IReader { reader.Read(out Version); }
    }

    public class ImportedPassengerHeliport : ComponentBase
    {
        public override void GetPrefabComponents(System.Collections.Generic.HashSet<ComponentType> types) { }
        public override void GetArchetypeComponents(System.Collections.Generic.HashSet<ComponentType> types) { }
    }

    public class PassengerHelicopterRoadAccess : ComponentBase
    {
        public override void GetPrefabComponents(System.Collections.Generic.HashSet<ComponentType> types) { }
        public override void GetArchetypeComponents(System.Collections.Generic.HashSet<ComponentType> types) { }
        public override void LateInitialize(EntityManager em, Entity e)
        {
            var building = em.GetComponentData<BuildingData>(e);
            building.m_Flags |= BuildingFlags.RequireRoad;
            building.m_Flags &= ~BuildingFlags.NoRoadConnection;
            em.SetComponentData(e, building);
            var placement = em.GetComponentData<PlaceableObjectData>(e);
            placement.m_Flags |= Game.Objects.PlacementFlags.RoadSide | Game.Objects.PlacementFlags.OnGround;
            placement.m_Flags &= ~(Game.Objects.PlacementFlags.Shoreline | Game.Objects.PlacementFlags.Floating);
            em.SetComponentData(e, placement);
        }
    }
}
