using System;

namespace CityWeaver
{
    internal static class HelicopterLandingRules
    {
        internal const float CaptureRadius = 2f;
        internal const float CaptureSpeed = 3f;
        internal const float DescentSpeed = 8f;
        // Two native 4/15 s movement steps cover navigation/movement latency.
        private const double ReactionTime = 8.0 / 15;

        internal static bool Managed(bool ownRoute, bool hasRoute, bool returningToOwnDepot)
            => ownRoute || (!hasRoute && returningToOwnDepot);

        internal static bool TryLimit(float horizontalDistance, float heightDelta, float horizontalSpeed,
            float acceleration, float nativeLimit, out float limit, out bool descend)
        {
            limit = nativeLimit; descend = false;
            if (!Finite(horizontalDistance) || horizontalDistance < 0 || !Finite(heightDelta) ||
                !Finite(horizontalSpeed) || horizontalSpeed < 0 || !Finite(acceleration) || acceleration <= 0 ||
                !Finite(nativeLimit) || nativeLimit < 0) return false;
            if (horizontalDistance <= CaptureRadius && horizontalSpeed <= CaptureSpeed)
            {
                limit = Math.Min(nativeLimit, DescentSpeed);
                descend = heightDelta < 0;
                return true;
            }
            // Reserve half the acceleration for vertical/heading corrections.
            // Brake using planar distance; height is not extra stopping room.
            double braking = acceleration * .5;
            double clearance = Math.Max(0, horizontalDistance - .25);
            double reaction = braking * ReactionTime;
            double safeSpeed = Math.Sqrt(reaction * reaction + 2 * braking * clearance) - reaction;
            limit = Math.Min(nativeLimit, (float)safeSpeed);
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
