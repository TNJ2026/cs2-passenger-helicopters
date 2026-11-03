using System;

namespace CityWeaver
{
    internal static class HelipadAirwayRules
    {
        // Native PathNode stores curve positions with 15-bit precision.
        internal const float CurveScale = 32767f;
        private const float MinimumGapMetres = 2f;

        internal static bool TrySeparate(float first, float second, float length, out float a, out float b)
        {
            bool changed = TrySeparateAll(new[] { first, second }, length, out var positions);
            a = positions[0]; b = positions[1];
            return changed;
        }

        // Different ground lanes can still enter the very same airway node.
        // Split only collapsed groups; preserve distinct neighboring connectors.
        internal static bool TrySeparateAll(float[] positions, float length, out float[] result)
        {
            result = positions == null ? null : (float[])positions.Clone();
            if (positions == null || positions.Length < 2 || positions.Length > 64 || !Finite(length) || length <= 0) return false;
            var order = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                if (!Finite(positions[i]) || positions[i] < 0 || positions[i] > 1) return false;
                order[i] = i;
            }
            Array.Sort(order, (a, b) => positions[a] == positions[b] ? a.CompareTo(b) : positions[a].CompareTo(positions[b]));
            bool changed = false;
            for (int first = 0; first < order.Length;)
            {
                int last = first + 1;
                int key = (int)(positions[order[first]] * CurveScale);
                while (last < order.Length && (int)(positions[order[last]] * CurveScale) == key) last++;
                int count = last - first;
                if (count > 1)
                {
                    float low = first == 0 ? 0 : (positions[order[first - 1]] + positions[order[first]]) * .5f + 2f / CurveScale;
                    float high = last == order.Length ? 1 : (positions[order[last - 1]] + positions[order[last]]) * .5f - 2f / CurveScale;
                    float available = high - low;
                    // Do not solve an impossibly dense cluster by moving unrelated
                    // connectors or introducing a new quantized collision.
                    if (available >= (count - 1) * (2f / CurveScale))
                    {
                        float step = Math.Min(Math.Max(MinimumGapMetres / length, 4f / CurveScale), available / (count - 1));
                        float span = step * (count - 1), center = 0;
                        for (int i = first; i < last; i++) center += positions[order[i]] / count;
                        center = Math.Max(low + span * .5f, Math.Min(high - span * .5f, center));
                        for (int i = first; i < last; i++) result[order[i]] = center + (i - first) * step - span * .5f;
                        changed = true;
                    }
                }
                first = last;
            }
            return changed;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
