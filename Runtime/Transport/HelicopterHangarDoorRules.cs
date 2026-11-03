using System;

namespace CityWeaver
{
    internal static class HelicopterHangarDoorRules
    {
        internal const float Duration=4, HoldSeconds=3;
        internal static float Step(float progress,bool occupied,float seconds)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return progress;
            return Math.Max(0,Math.Min(1,progress+(occupied?1:-1)*seconds/Duration));
        }
        // Authored coordinates. Include rotor/tail clearance beyond the doorway.
        internal static bool Occupied(float x,float y,float z) =>
            Math.Abs(x-ImportedHeliportRules.DepotX)<29 && y>-2 && y<13 && z>-48 && z<18;
        internal static bool ShouldOpen(float progress,bool occupied,uint elapsedFrames) =>
            occupied||(progress>0&&elapsedFrames<HoldSeconds*60);
        internal static bool HoldDeparture(float progress,float z) => progress<.999f && z<1;
        // Two accordion stacks: eight 2m leaves on either side, folding inward.
        // All pivots are root-local in the native (Z-mirrored) imported mesh.
        internal static (float x,float z,float angle) Pose(int panel,float progress)
        {
            if(panel<1||panel>16)throw new ArgumentOutOfRangeException(nameof(panel));
            progress=Math.Max(0,Math.Min(1,progress));
            float eased=progress*progress*(3-2*progress);
            bool left=panel<=8;int n=left?panel-1:16-panel;
            float a=eased*85*(float)Math.PI/180,side=left?1:-1;
            return ((left?-20.312f:11.688f)+side*n*2*(float)Math.Cos(a),
                .27f+(n%2)*2*(float)Math.Sin(a),-side*(n%2==0?1:-1)*a);
        }
    }
}
