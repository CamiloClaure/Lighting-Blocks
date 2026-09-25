Shader "LightingBlocks/Crystal Filaments"
{
    Properties
    {
        [HDR] _EmissionColor("Filament tint",Color)=(.2,.7,3,1)
        _Glow("Step illumination",Range(0,1))=1
        _Highlight("Interaction",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _EmissionColor; float _Glow,_Highlight;
            CBUFFER_END
            // Lighting-only world fade (see LightingFog.cs). _LightingFogActive is 0 outside
            // Lighting, so worldFade always evaluates to 1 there - legacy scenes are unaffected.
            float4 _LightingFogCenter;
            float _LightingFogActive, _LightingWorldClear, _LightingWorldBlack;
            struct A { float4 p:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float fog:TEXCOORD1; float3 world:TEXCOORD2; };
            V vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.world=TransformObjectToWorld(i.p.xyz); o.color=i.color; o.uv=i.uv; o.fog=ComputeFogFactor(o.p.z); return o; }
            half4 frag(V i):SV_Target
            {
                float transverse=abs(i.uv.x*2-1);
                float footprint=max(fwidth(transverse),.025);
                float across=exp2(-transverse*transverse*5);
                across*=1-smoothstep(1-min(footprint,1),1,transverse);
                across*=min(1,2/footprint);
                float glow=saturate(_Glow);
                float3 hue=lerp(_EmissionColor.rgb,sqrt(max(_EmissionColor.rgb,0))*1.5,i.color.r);
                // A faint resting rim marks the footprint; full step brightness is unchanged.
                float3 light=hue*i.color.a*across*(lerp(.04,1.003,glow)+_Highlight*.12);
                float worldT=saturate((length(i.world.xz-_LightingFogCenter.xz)-_LightingWorldClear)/max(_LightingWorldBlack-_LightingWorldClear,1e-4));
                light*=1-_LightingFogActive*(worldT*worldT*(3-2*worldT));
                return half4(MixFogColor(light,float3(0,0,0),i.fog),0);
            }
            ENDHLSL
        }
    }
}
