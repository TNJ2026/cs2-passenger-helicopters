using System;

namespace CityWeaver.HelicopterModels
{
    // Defensive idempotency if another integration schedules a second update.
    internal struct HelicopterRotorFrameGate
    {
        private bool m_Seen;
        private int m_Frame;
        internal bool Enter(int frame)
        {
            if(m_Seen&&frame==m_Frame)return false;
            m_Seen=true;m_Frame=frame;return true;
        }
        internal void Reset(){m_Seen=false;}
    }

    // Display-only power envelope. Never changes native momentum, departure
    // times, navigation or flight speed. Durations use native animation seconds.
    internal static class HelicopterRotorSpeedRules
    {
        internal const float IdleFactor=.65f,SpoolUpSeconds=3,SpoolDownSeconds=6,PrepareSeconds=4;
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        internal static float DepartureSeconds(uint now,uint departure)
        {
            if(departure==0)return float.NaN;
            int frames=unchecked((int)(departure-now));
            return frames/60f;
        }
        internal static float StepSeconds(float nativeFrameDelta,bool paused)
        {
            if(paused||!Finite(nativeFrameDelta)||nativeFrameDelta<=0)return 0;
            // Native interpolation also uses frameDelta/60. Bound long stalls
            // without using real-time speed or mistaking a stalled frame for pause.
            return Math.Min(nativeFrameDelta/60f,.5f);
        }
        internal static float Target(bool flying,bool maneuver,bool boarding,float speed,float verticalSpeed,float departureSeconds)
        {
            // Only a confirmed, stationary boarding stop with a known future
            // departure may idle. An expired/unknown schedule remains ready.
            if(flying||maneuver||!boarding||!Finite(speed)||!Finite(verticalSpeed)||
                speed>.5f||Math.Abs(verticalSpeed)>.2f||!Finite(departureSeconds)||departureSeconds<=PrepareSeconds)return 1;
            return IdleFactor;
        }
        internal static float Advance(float current,float target,float seconds,bool flyingOrManeuver)
        {
            if(!Finite(current))current=1;
            current=Math.Max(IdleFactor,Math.Min(1,current));
            if(!Finite(seconds)||seconds<=0)return current;
            // Native boarding can end early (for example, a forced return).
            // Keep airborne RPM full even when no advance notice was available;
            // do not delay or intercept native departure to finish an animation.
            if(flyingOrManeuver)return 1;
            target=Finite(target)?Math.Max(IdleFactor,Math.Min(1,target)):1;
            float duration=target>current?SpoolUpSeconds:SpoolDownSeconds;
            float step=(1-IdleFactor)*Math.Min(seconds,.5f)/duration;
            return current+Math.Max(-step,Math.Min(step,target-current));
        }
        internal static float Phase(float current,float spin,float seconds,float before,float after)
        {
            if(!Finite(current))current=0;
            if(!Finite(spin)||!Finite(seconds)||!Finite(before)||!Finite(after)||seconds<=0)return current;
            double angle=current+(double)spin*Math.Min(seconds,.5f)*(before+after)*.5;
            return (float)Math.IEEERemainder(angle,2*Math.PI);
        }
    }
}
