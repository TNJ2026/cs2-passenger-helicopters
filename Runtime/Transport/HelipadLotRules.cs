using System;
namespace CityWeaver
{
    internal static class HelipadLotRules
    {
        internal const float Width=1.1051733428928905f,Depth=.9963529095125803f;
        internal static float MapX(float x)=>x*Width;
        internal static float MapZ(float z)=>(z-20)*Depth+16;
        internal static int Cells(float metres)
        {
            if(float.IsNaN(metres)||float.IsInfinity(metres)||metres<=0)throw new ArgumentException("Invalid lot extent");
            var cells=metres/8;var rounded=(float)Math.Round(cells);
            return (int)(Math.Abs(cells-rounded)<.0001f?rounded:Math.Ceiling(cells));
        }
        internal static int Layout(float padZ)
        {
            const float old=4.61930847f-2.510134f;
            if(Math.Abs(padZ-MapZ(old))<.1f)return 1;
            if(Math.Abs(padZ-old)<.1f)return 0;
            return -1;
        }
    }
}
