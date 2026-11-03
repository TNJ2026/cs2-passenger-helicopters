using System.Collections.Generic;

namespace CityWeaver
{
    // Two simulation checks must agree before changing a user's route policy.
    internal sealed class HelicopterLineSafetyRules<T> where T : struct
    {
        internal const uint GraceFrames = 512;
        private readonly Dictionary<T, uint> m_Since = new Dictionary<T, uint>();
        internal bool ShouldStop(T line, uint frame, bool ownLine, bool active, bool invalid)
        {
            if (!ownLine || !active || !invalid) { m_Since.Remove(line); return false; }
            if (!m_Since.TryGetValue(line, out var since) || frame < since)
            { m_Since[line] = frame; return false; }
            if (frame - since < GraceFrames) return false;
            m_Since[line] = frame; // Bound retries if the native policy event is delayed.
            return true;
        }
        internal void Retain(ISet<T> live)
        {
            var stale = new List<T>();
            foreach (var key in m_Since.Keys) if (!live.Contains(key)) stale.Add(key);
            foreach (var key in stale) m_Since.Remove(key);
        }
    }

    internal struct PassengerManifestCounts
    {
        internal int Passengers, Pets;
        internal int Total => Passengers + Pets;
        internal void Add(bool pet) { if (pet) Pets++; else Passengers++; }
    }
}
