using System;

namespace CityWeaver
{
    internal static class ImportedHelipadRules
    {
        // Native Front access snaps the +Z lot boundary to the road.
        internal static float FrontShift(float meshFront, int lotDepth)
        {
            float shift = lotDepth * 4 - meshFront;
            if (float.IsNaN(shift) || float.IsInfinity(shift) || shift < -.05f || shift > 8)
                throw new InvalidOperationException("Unexpected helipad frontage bounds");
            return Math.Abs(shift) < .001f ? 0 : shift;
        }
        internal static float PathZ(float z, float front, float shift) => Math.Abs(z - front) < .01f ? z : z + shift;
        // On reload the prefab can already be aligned while saved network entities
        // still use the original centered footprint. Recognize both coordinate sets.
        internal static float LegacyPathZ(float z, float front, float width) =>
            Math.Abs(z - front) < .01f ? z : z - (front - width / 2);
        // Parent mesh 0 means building-local height; -1 forces native ground snapping.
        internal static int Parent(float height) => height > .01f ? 0 : -1;
    }
}
