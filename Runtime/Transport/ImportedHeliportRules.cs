using System;

namespace CityWeaver
{
    internal static class ImportedHeliportRules
    {
        // Layout points are authored in OBJ coordinates. Native OBJ import mirrors Z;
        // then rotate native +X frontage to building +Z and center the native footprint.
        internal static (float x, float z) Position(float x, float z) => (z - 1, x - 6);
        internal static bool Footprint(int modules, float minX, float minZ, float maxX, float maxZ) =>
            modules == 6 && Near(minX,-50) && (Near(minZ,-52) || Near(minZ,-62)) && Near(maxX,62) && Near(maxZ,50);
        // The expanded apron reaches authored Z=62 (local X=61), requiring 16 cells.
        internal static int LotWidth(float nativeMinZ) => Near(nativeMinZ,-62) ? 16 : 14;
        internal const float SecondPadX = -32, SecondPadZ = 26;
        // Measured from heliport_hangar_door_1..16: opening X -20.312..11.688,
        // front Z approximately -0.3. The old X=-32 belongs to the side annex.
        internal const float DepotX = -4.312f, DepotZ = -16;
        internal const float HangarApronZ = 12;
        // Each boarding pad lies on its own 16 m segment. The apron/depot
        // connectors branch behind the pads instead of crossing either stop.
        internal static (float x, float z) TaxiNode(int node)
        {
            switch(node) {
                case 0: return (5,26);
                case 1: return (-11,26);
                case 2: return (-40,4);
                case 3: return (-32,4); // existing apron junction, preserve saved node identity
                case 4: return (-24,26);
                case 5: return (-40,26);
                case 6: return (-32,-8); // preserve the legacy side spur; no spawn marker remains here
                case 7: return (DepotX,HangarApronZ); // clear the door before turning
                case 8: return (DepotX,DepotZ); // center of the tall hangar
                default: throw new ArgumentOutOfRangeException(nameof(node));
            }
        }
        internal static readonly (int a,int b)[] TaxiEdges = { (0,1),(1,2),(2,3),(4,5),(5,2),(3,6),(3,7),(7,8) };
        internal static (float x,float z) TaxiLocal(int node)
        {
            var p=TaxiNode(node);p=AlignLegacyPad(p.x,p.z);return Position(p.x,p.z);
        }
        internal static int LegacyTaxiJunctionEnd(float ax,float az,float dx,float dz)
        {
            var front=TaxiLocal(0);var old=TaxiLocal(5);
            bool Near(float x,float z,(float x,float z) p)=>Math.Abs(x-p.x)<.1f&&Math.Abs(z-p.z)<.1f;
            if(Near(ax,az,front)&&Near(dx,dz,old))return 1;
            if(Near(dx,dz,front)&&Near(ax,az,old))return 0;
            return -1;
        }
        // Measured from the matching ring meshes (heliport-matched-v10).
        internal const float PadOffsetX = -1.0814967f, PadOffsetZ = 5.39467335f;
        internal static (float x, float z) AlignLegacyPad(float x, float z) =>
            Math.Abs(z - 26f) < .01f ? (x + PadOffsetX, z + PadOffsetZ) : (x, z);
        private static bool Near(float a, float b) => !float.IsNaN(a) && !float.IsInfinity(a) && Math.Abs(a-b)<=.1f;
    }
}
