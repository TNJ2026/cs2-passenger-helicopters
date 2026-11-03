using System;
using System.Collections.Generic;
using System.Linq;

namespace CityWeaver
{
    internal static class PassengerHelicopterRules
    {
        internal const int PassengerCapacity = 60;
        internal const int AirportVehicleCapacity = 6;
        internal const float FlyingMaxSpeedKph = 350f;

        // The repeated closing node is harmless; different pads of one airport
        // must never form a flight leg, including the closing leg of a loop.
        internal static string ValidateRoute<T>(IReadOnlyList<T> stops, IReadOnlyList<T> airports, bool startsAtAirport, bool closed)
        {
            if (stops == null || airports == null || stops.Count != airports.Count || stops.Count == 0)
                return "INVALID_ARGUMENT";
            if (!startsAtAirport) return "HELICOPTER_FIRST_STOP_REQUIRES_AIRPORT";
            var equal = EqualityComparer<T>.Default;
            for (int i = 1; i < stops.Count; i++)
                if (!equal.Equals(stops[i - 1], stops[i]) && equal.Equals(airports[i - 1], airports[i]))
                    return "HELICOPTER_SAME_AIRPORT_LEG";
            int last = stops.Count - 1;
            if (closed && !equal.Equals(stops[last], stops[0]) && equal.Equals(airports[last], airports[0]))
                return "HELICOPTER_SAME_AIRPORT_LEG";
            return null;
        }
        // Distinct pads in the same airport do not satisfy an inter-airport route.
        internal static string ValidateAirports<T>(IReadOnlyList<T> airports, IReadOnlyList<bool> roadConnected, bool hasDepot)
        {
            if (airports == null || roadConnected == null || airports.Count < 2 || airports.Count > 64 || airports.Count != roadConnected.Count)
                return "INVALID_ARGUMENT";
            if (roadConnected.Any(x => !x)) return "AIRPORT_REQUIRES_ROAD";
            if (airports.Distinct().Count() < 2) return "DISTINCT_AIRPORTS_REQUIRED";
            if (!hasDepot) return "HELICOPTER_DEPOT_REQUIRED";
            return null;
        }
    }
}
