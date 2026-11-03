using System;
using System.Collections.Generic;

namespace CityWeaver
{
    internal static class HelicopterPadOccupancy<T> where T:struct
    {
        internal const float Radius=80;
        // One physical aircraft can occupy only one pad, even when protection
        // volumes overlap. Keep a departing aircraft assigned to its original pad.
        internal static T? Select(IReadOnlyList<(T pad,float distance)> pads,T? boarding,T? approach,T? previous)
        {
            bool Present(T? candidate,bool requireNear)
            {
                if(!candidate.HasValue)return false;
                foreach(var p in pads)
                    if(EqualityComparer<T>.Default.Equals(candidate.Value,p.pad)&&(!requireNear||p.distance<Radius))return true;
                return false;
            }
            if(Present(boarding,false))return boarding;
            // An approach belongs to its destination even while passing a
            // neighbouring pad's volume; it must never reserve that neighbour.
            if(Present(approach,false))return Present(approach,true)?approach:null;
            if(Present(previous,false))
            {
                foreach(var p in pads)if(p.distance<Radius)return previous;
                return null;
            }
            T? closest=null;float distance=Radius;
            foreach(var p in pads)if(p.distance>=0&&p.distance<distance){closest=p.pad;distance=p.distance;}
            return closest;
        }
    }
}
