using System;
namespace CityWeaver
{
    internal static class HelicopterDepartureRules
    {
        internal const float TaxiSpeed=3f;
        internal static bool Eligible(bool sameAirport,bool enRoute,bool boarding,bool returning,bool routeSource,bool flying,float distanceTravelled)
            =>sameAirport&&enRoute&&!boarding&&!returning&&!routeSource&&!flying&&
              !float.IsNaN(distanceTravelled)&&!float.IsInfinity(distanceTravelled)&&distanceTravelled>=0&&distanceTravelled<3;
    }
}
