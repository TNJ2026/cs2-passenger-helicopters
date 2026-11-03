using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CityWeaver
{
    // This tag exists only on transient tool definitions, never on saved buildings.
    public struct HeliportGradeApplied : IComponentData { }

    // One registration before GenerateObjects in Modification1 also precedes
    // GenerateNodes/Areas. Adjust the whole owned definition set before any native
    // preview is generated, so the preview and the committed building agree.
    public partial class HeliportPlacementGradeSystem : GameSystemBase
    {
        private EntityQuery m_Definitions;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Definitions = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(),
                ComponentType.ReadOnly<Updated>(), ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            var tool = World.GetExistingSystemManaged<ToolSystem>()?.activeTool as ObjectToolSystem;
            // Native manual new-building placement only. Leave MCP explicit
            // elevations, moves, upgrades, editor stamps and existing saves alone.
            if (tool == null || tool.actualMode != ObjectToolSystem.Mode.Create ||
                Game.SceneFlow.GameManager.instance?.gameMode != GameMode.Game) return;
            var prefab = tool.GetPrefab() as BuildingPrefab;
            if (prefab == null || prefab.name != ImportedHelipadMenuSystem.Airport || prefab.isBuiltin ||
                HelicopterAssetOwnership.RejectForeignSubscription(prefab)) return;
            var ps = World.GetExistingSystemManaged<PrefabSystem>();
            var terrain = World.GetExistingSystemManaged<TerrainSystem>();
            if (ps == null || terrain == null || !ps.TryGetEntity(prefab, out var prefabEntity)) return;
            EntityManager.CompleteAllTrackedJobs();
            using (var definitions = m_Definitions.ToEntityArray(Allocator.Temp))
            foreach (var root in definitions)
            {
                var creation = EntityManager.GetComponentData<CreationDefinition>(root);
                if (creation.m_Prefab != prefabEntity || creation.m_Original != Entity.Null ||
                    creation.m_Owner != Entity.Null || creation.m_Flags != 0 ||
                    EntityManager.HasComponent<OwnerDefinition>(root) ||
                    EntityManager.HasComponent<HeliportGradeApplied>(root) ||
                    !EntityManager.HasComponent<ObjectDefinition>(root)) continue;
                var original = EntityManager.GetComponentData<ObjectDefinition>(root);
                var owner = new OwnerDefinition { m_Prefab = prefabEntity,
                    m_Position = original.m_Position, m_Rotation = original.m_Rotation };
                // Current heliport has direct objects and nets only. Refuse an
                // unfamiliar definition layout rather than partly lifting it.
                var children = new List<Entity>();
                bool supported = true;
                foreach (var entity in definitions)
                {
                    if (!EntityManager.HasComponent<OwnerDefinition>(entity) ||
                        !EntityManager.GetComponentData<OwnerDefinition>(entity).Equals(owner)) continue;
                    if (!EntityManager.HasComponent<ObjectDefinition>(entity) &&
                        !EntityManager.HasComponent<NetCourse>(entity)) supported = false;
                    var childCreation = EntityManager.GetComponentData<CreationDefinition>(entity);
                    if (childCreation.m_Original != Entity.Null ||
                        (childCreation.m_Flags & CreationFlags.Delete) != 0) supported = false;
                    if (EntityManager.HasComponent<ObjectDefinition>(entity))
                    {
                        var obj = EntityManager.GetComponentData<ObjectDefinition>(entity);
                        var nestedOwner = new OwnerDefinition { m_Prefab = childCreation.m_Prefab,
                            m_Position = obj.m_Position, m_Rotation = obj.m_Rotation };
                        foreach (var candidate in definitions)
                            if (EntityManager.HasComponent<OwnerDefinition>(candidate) &&
                                EntityManager.GetComponentData<OwnerDefinition>(candidate).Equals(nestedOwner))
                                supported = false;
                    }
                    children.Add(entity);
                }
                if (!supported || children.Count == 0) continue;
                var heights = terrain.GetHeightData(true);
                if (!heights.isCreated) continue;
                float halfWidth = prefab.m_LotWidth * 4f, halfDepth = prefab.m_LotDepth * 4f;
                float sx = halfWidth + HeliportPlacementGradeRules.SampleMargin;
                float sz = halfDepth + HeliportPlacementGradeRules.SampleMargin;
                // Sample outside the eight-metre terraform apron. Preview terrain
                // therefore cannot feed its own raised/lowered result back in.
                var samples = new float[HeliportPlacementGradeRules.SampleCount];
                for (int i = 0; i < 3; i++)
                {
                    float along = (i - 1) * .6f;
                    samples[i] = TerrainUtils.SampleHeight(ref heights, original.m_Position +
                        math.rotate(original.m_Rotation, new float3(-sx, 0, along * halfDepth)));
                    samples[i + 3] = TerrainUtils.SampleHeight(ref heights, original.m_Position +
                        math.rotate(original.m_Rotation, new float3(sx, 0, along * halfDepth)));
                    samples[i + 6] = TerrainUtils.SampleHeight(ref heights, original.m_Position +
                        math.rotate(original.m_Rotation, new float3(along * halfWidth, 0, -sz)));
                }
                float target = HeliportPlacementGradeRules.Target(original.m_Position.y, samples);
                float raise = target - original.m_Position.y;
                EntityManager.AddComponent<HeliportGradeApplied>(root);
                if (raise < HeliportPlacementGradeRules.MinimumRaise) continue;
                var updated = original;
                updated.m_Position.y = updated.m_LocalPosition.y = target;
                EntityManager.SetComponentData(root, updated);
                foreach (var child in children)
                {
                    var childOwner = owner; childOwner.m_Position.y = target;
                    EntityManager.SetComponentData(child, childOwner);
                    if (EntityManager.HasComponent<ObjectDefinition>(child))
                    {
                        var obj = EntityManager.GetComponentData<ObjectDefinition>(child);
                        obj.m_Position.y += raise;
                        // LocalPosition and relative elevation remain local.
                        EntityManager.SetComponentData(child, obj);
                    }
                    if (EntityManager.HasComponent<NetCourse>(child))
                    {
                        var net = EntityManager.GetComponentData<NetCourse>(child);
                        var inverse = math.inverse(original.m_Rotation);
                        float start = HeliportPlacementGradeRules.EndpointRaise(
                            math.rotate(inverse, net.m_StartPosition.m_Position - original.m_Position).z, halfDepth, raise);
                        float end = HeliportPlacementGradeRules.EndpointRaise(
                            math.rotate(inverse, net.m_EndPosition.m_Position - original.m_Position).z, halfDepth, raise);
                        net.m_StartPosition.m_Position.y += start;
                        net.m_EndPosition.m_Position.y += end;
                        net.m_Curve.a.y += start;
                        net.m_Curve.b.y += math.lerp(start, end, 1f / 3);
                        net.m_Curve.c.y += math.lerp(start, end, 2f / 3);
                        net.m_Curve.d.y += end;
                        net.m_Length = Colossal.Mathematics.MathUtils.Length(net.m_Curve);
                        EntityManager.SetComponentData(child, net);
                    }
                }
            }
        }
    }
}
