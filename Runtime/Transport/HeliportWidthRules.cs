namespace CityWeaver
{
    internal static class HeliportWidthRules
    {
        internal const float Factor=104f/102f;
        internal static float AuthoredZ(float localX)=>localX/Factor+1;
        internal static (float x,float z,float angle,float length) DoorPose(int panel,float progress)
        {
            var p=HelicopterHangarDoorRules.Pose(panel,progress);
            var c=System.Math.Cos(p.angle);var s=System.Math.Sin(p.angle)*Factor;
            return (p.x,p.z*Factor,(float)System.Math.Atan2(s,c),(float)System.Math.Sqrt(c*c+s*s));
        }
        // Match known pre/post migration rows, never scale an unknown custom layout.
        internal static int LayoutState(float x)
        {
            foreach(var original in new[]{25f,25f+ImportedHeliportRules.PadOffsetZ})
                if(System.Math.Abs(x-original*Factor)<.1f)return 1;
            foreach(var original in new[]{25f,25f+ImportedHeliportRules.PadOffsetZ})
                if(System.Math.Abs(x-original)<.1f)return 0;
            return -1;
        }
    }
}
