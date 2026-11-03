using System;

namespace CityWeaver
{
    internal static class HeliportPlacementGradeRules
    {
        internal const int SampleCount = 9;
        internal const float SampleMargin = 20f;
        internal const float MinimumRaise = .15f, MaximumRaise = 4f;

        // Only correct a low entrance beside a reasonably level plateau. A hill,
        // isolated high sample, missing height data or large cut/fill is not a
        // reliable automatic placement decision.
        internal static float Target(float entrance, float[] samples)
        {
            if (!Finite(entrance) || samples == null || samples.Length != SampleCount) return entrance;
            var sorted = (float[])samples.Clone();
            foreach (float value in sorted) if (!Finite(value)) return entrance;
            Array.Sort(sorted);
            float median = sorted[SampleCount / 2];
            int support = 0;
            foreach (float value in sorted) if (Math.Abs(value - median) <= .75f) support++;
            float raise = median - entrance;
            return support >= 6 && sorted[8] - sorted[0] <= 4f &&
                raise >= MinimumRaise && raise <= MaximumRaise ? median : entrance;
        }

        // The single roadside pedestrian entrance stays at the native frontage
        // height. Interior nodes rise with the platform; native network generation
        // still performs attachment, collision and slope validation.
        internal static float EndpointRaise(float localZ, float halfDepth, float raise)
            => localZ >= halfDepth - .5f ? 0 : raise;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
