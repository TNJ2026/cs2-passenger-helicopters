using System;

namespace CityWeaver
{
    internal static class HelicopterSpacingRules
    {
        // metres and metres/second; only a same-heading cruise leader can constrain us.
        internal static float Limit(float nativeSpeed, float forward, float lateral, float vertical,
            float alignment, float leaderSpeed, bool leaderWinsTie)
        {
            if (float.IsNaN(nativeSpeed) || nativeSpeed <= 0 || alignment < .95f ||
                Math.Abs(vertical) > 12 || Math.Abs(lateral) > 15 || forward > 120 || forward < -1)
                return nativeSpeed;
            if (forward <= 1 && !leaderWinsTie) return nativeSpeed;
            float allowed = Math.Max(0, leaderSpeed + (Math.Max(0, forward) - 60) * .5f);
            return Math.Min(nativeSpeed, allowed);
        }
    }
}
