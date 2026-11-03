using System;

namespace CityWeaver
{
    internal static class HelipadPedestrianRules
    {
        // Native ReferencesSystem.ValidateConnection requires the outward tangent
        // to face the road within 45 degrees. Turn only after entering the lot.
        internal static float EntryX(float padX,float width=1) => padX+(Source[0].x-1.331073f)*width;
        internal static (float x,float y,float z) EntryTurn(float padX,float front,float width=1) => (EntryX(padX,width),0,front-2f);
        // Surveyed walking surfaces in the authored OBJ. Native import mirrors Z.
        // Include the intermediate stair landing: a single slope cuts through it.
        internal static readonly (float x,float y,float z)[] Source = {
            (15.80f,.20f,-13.10f), (10.754f,2.35f,-13.10f),
            (8.568f,2.35f,-13.10f), (3.789f,4.35f,-13.10f),
            (1.331073f,4.35f,-13.10f), (1.331073f,4.35f,-10.95f),
            (1.331073f,5.19f,-7.50f), (1.331073f,5.04f,-6.80f)
        };
        internal static (float x,float y,float z) Point(int index,float padX,float padY,float padZ,float width=1,float depth=1)
        {
            if(index<0||index>=Source.Length)throw new ArgumentOutOfRangeException(nameof(index));
            var p=Source[index];
            return (padX+(p.x-1.331073f)*width,padY+p.y-5.01f,padZ+(-p.z+2.510134f)*depth);
        }
    }
}
