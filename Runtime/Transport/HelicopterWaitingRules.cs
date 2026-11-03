using System;
namespace CityWeaver
{
    internal static class HelicopterWaitingRules
    {
        internal static (float x,float z) WidthEdge(float x,float z,float towardX,float towardZ,float radius,float width)
        {
            if(float.IsNaN(width)||float.IsInfinity(width)||width<=0)throw new ArgumentException("Invalid width scale.");
            var edge=Edge(x/width,z,towardX/width,towardZ,radius);
            return (edge.x*width,edge.z);
        }
        internal static (float x,float z) Edge(float x,float z,float towardX,float towardZ,float radius)
        {
            double dx=towardX-x,dz=towardZ-z,length=Math.Sqrt(dx*dx+dz*dz);
            if(double.IsNaN(length)||double.IsInfinity(length)||float.IsNaN(radius)||float.IsInfinity(radius)||radius<=0||length<=radius+.25)
                throw new ArgumentException("Pedestrian approach is too short for the waiting setback.");
            return (x+(float)(dx/length*radius),z+(float)(dz/length*radius));
        }
    }
}
