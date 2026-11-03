using System;
namespace CityWeaver
{
    internal static class HelicopterTimingRules
    {
        internal static bool InvalidAverage(float average,float expected) =>
            float.IsNaN(average)||float.IsInfinity(average)||average<0||
            average>Math.Max(3600d,Math.Max(0,expected)*64d);
        internal static bool Future(uint frame,uint now)=>frame!=0&&unchecked((int)(frame-now))>0;
        internal static bool InvalidDeparture(uint frame,uint now,bool boarding)=>!boarding&&Future(frame,now);
        internal static uint Shorten(uint old,uint now,float dwell)
        {
            if(!Future(old,now)||float.IsNaN(dwell)||float.IsInfinity(dwell)||dwell<0)return old;
            uint duration=(uint)Math.Min(3600*60,Math.Max(1,dwell)*60);
            return unchecked((uint)(old-now))>duration?unchecked(now+duration):old;
        }
    }
}
