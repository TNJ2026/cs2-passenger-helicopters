using Game.Prefabs;
using Unity.Mathematics;

namespace CityWeaver
{
    internal static class HeliportTerraformRules
    {
        // Keep the level core inside the authored apron. A narrow native blend
        // outside it avoids a hard terrain seam without cutting an 8 m moat.
        internal const float SmoothMargin = 2f;

        internal static void Configure(BuildingTerraformOverride terrain)
        {
            terrain.m_LevelMinOffset = new float2(0);
            terrain.m_LevelMaxOffset = new float2(0);
            terrain.m_SmoothMinOffset = new float2(-SmoothMargin);
            terrain.m_SmoothMaxOffset = new float2(SmoothMargin);
        }
    }
}
