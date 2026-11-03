Shader "PassengerHelicopters/RotorAngularExposure"
{
    Properties { _BaseColorMap("Real blade albedo", 2D) = "white" {} _ControlMask("Native route mask", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "AngularExposure"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _BaseColorMap;
            sampler2D _ControlMask;
            float4x4 _RotorLocalToClip;
            float4 _RotorTint;
            float _RotorSweep, _RotorAmount, _RotorRadius;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; float3 sample:TEXCOORD1; };
            struct Varyings { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float opacity:TEXCOORD1; };
            Varyings Vert(Input input)
            {
                Varyings o;
                float angle=-_RotorSweep*input.sample.x, s,c;
                sincos(angle,s,c);
                float3 p=input.vertex.xyz;
                p.xz=float2(c*p.x+s*p.z,-s*p.x+c*p.z);
                o.position=mul(_RotorLocalToClip,float4(p,1));
                o.uv=input.uv;
                float radial=smoothstep(0.12,0.25,length(p.xz)/max(_RotorRadius,0.001));
                // One current-position blade plus normalized shutter samples.
                // The hub has no repeated translucent copies or disk geometry.
                float trail=1-pow(1-0.65*_RotorAmount,max(input.sample.z,0));
                o.opacity=input.sample.y>0.5 ? trail*radial : 1-0.82*_RotorAmount*radial;
                return o;
            }
            float4 Frag(Varyings input):SV_Target
            {
                float4 color=tex2D(_BaseColorMap,input.uv);
                // The pass is after postprocessing, in display-linear color.
                // Preserve the native neutral paint + per-instance line tint.
                color.rgb*=lerp(float3(1,1,1),_RotorTint.rgb,tex2D(_ControlMask,input.uv).r);
                color.a*=input.opacity;
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
