using System;
using System.Collections.Generic;

namespace CityWeaver
{
    internal sealed class HelicopterAirportQueue<T> where T : struct
    {
        private readonly List<T> m_Order = new List<T>();
        internal static bool Managed(bool ownVehicle, bool ownRoute, bool hasRoute) => ownRoute || (ownVehicle && !hasRoute);
        internal void Defer(T arrival)
        {
            if (m_Order.Remove(arrival)) m_Order.Add(arrival);
        }
        internal T? Select(IReadOnlyList<T> arrivals, IReadOnlyList<T> occupants)
        {
            var active = new HashSet<T>(arrivals);
            m_Order.RemoveAll(x => !active.Contains(x));
            foreach (var arrival in arrivals) if (!m_Order.Contains(arrival)) m_Order.Add(arrival);
            if (m_Order.Count == 0) return null;
            T next = m_Order[0];
            foreach (var occupant in occupants)
                if (!EqualityComparer<T>.Default.Equals(occupant, next)) return null;
            return next;
        }
        internal static float HoldSpeed(float nativeSpeed, float distance, float speed, float acceleration)
        {
            // Brake before the protected volume. Native movement applies acceleration;
            // account for one movement tick rather than assuming instantaneous stopping.
            double clearance = Math.Max(0, distance - 110 - Math.Max(0, speed) * (16.0 / 60));
            return Math.Min(nativeSpeed, (float)Math.Sqrt(Math.Max(.1, acceleration) * clearance));
        }
    }
}
