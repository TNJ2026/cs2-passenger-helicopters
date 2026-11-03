using System;

namespace CityWeaver.HelicopterModels
{
    // Pure shutter/scheduling rules. Radians and real seconds, never route speed changes.
    internal static class HelicopterRotorExposureRules
    {
        internal const int Samples=48,MaxVisibleVehicles=8;
        internal const float CruiseResumeSeconds=1,MaximumVerticalSpeed=1.5f;
        internal static float Clamp(float v,float lo,float hi)=>Math.Max(lo,Math.Min(hi,v));
        internal static bool Paused(float selectedSpeed,float smoothSpeed)=>!Finite(selectedSpeed)||!Finite(smoothSpeed)||selectedSpeed<=0||smoothSpeed<=0;
        internal static bool CruiseEligible(float speed,float verticalSpeed,bool flying,bool maneuver,bool boarding,bool paused)
        {
            return !paused&&flying&&!maneuver&&!boarding&&Finite(speed)&&Finite(verticalSpeed)&&speed*3.6f>90&&Math.Abs(verticalSpeed)<=MaximumVerticalSpeed;
        }
        internal static float StableCruise(float current,bool eligible,float seconds)
        {
            if(!eligible||!Finite(current)||!Finite(seconds))return 0;
            return Clamp(current+Clamp(seconds,0,.05f),0,CruiseResumeSeconds);
        }
        internal static float Target(float spin,float speed,bool flying,bool maneuver,bool boarding,bool paused,float verticalSpeed=0)
        {
            if(!CruiseEligible(speed,verticalSpeed,flying,maneuver,boarding,paused)||!Finite(spin)||Math.Abs(spin)<3)return 0;
            float t=Clamp((speed*3.6f-90)/70,0,1);
            return t*t*(3-2*t);
        }
        internal static float Blend(float current,float target,float seconds)
        {
            float step=Clamp(seconds,0,.05f)/.6f;
            return current+Clamp(target-current,-step,step);
        }
        internal static float Advance(float current,float target,float seconds,bool eligible,float stableSeconds)
        {
            // Flight safety phases clear on the first rendering update. Only
            // steady cruise gets a gradual fade-in; never fade an old trail out
            // over a takeoff, approach, landing or boarding animation.
            if(!eligible||!Finite(stableSeconds)||stableSeconds<CruiseResumeSeconds)return 0;
            return Blend(current,target,seconds);
        }
        internal static bool CustomBlades(float amount,bool nativeMotionBlur)=>amount>.001f||nativeMotionBlur;
        internal static int DrawSubmesh(float amount)=>amount>.001f?0:1;
        internal static float Sweep(float spin,bool tail)
        {
            if(!Finite(spin))return 0;
            // 1/125s exposure, capped below adjacent-blade spacing; never a full disk.
            return Math.Sign(spin)*Math.Min(Math.Abs(spin)*.008f,tail?.90f:.62f);
        }
        internal static float Weight(int sample,int count)
        {
            if(count<1||sample<0||sample>=count)throw new ArgumentOutOfRangeException();
            return 2f*(count-sample)/(count*(count+1f));
        }
        internal static bool Fresh(int now,int last)=>last>=0&&now>=last&&now-last<=2;
        internal static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
