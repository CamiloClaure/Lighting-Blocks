Shader "LightingBlocks/Spectral Motes"
{
 Properties{[HDR]_BaseColor("Color",Color)=(1,0.8,0.4,1) _BaseMap("Texture",2D)="white"{}}
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Blend SrcAlpha One ZWrite Off Cull Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor,_BaseMap_ST;
   CBUFFER_END
   float3 _FogCenter;float _SpectralRevealRadius;
   // Lighting-only world fade (see LightingFog.cs), on top of the existing reveal-radius fade
   // above. _LightingFogActive is 0 outside Lighting, so worldFade always evaluates to 1 there -
   // legacy scenes (this material is shared by legacy pickups) are unaffected.
   float4 _LightingFogCenter;float _LightingFogActive,_LightingWorldClear,_LightingWorldBlack;
   struct A{float4 p:POSITION;float4 c:COLOR;float2 uv:TEXCOORD0;};
   struct V{float4 p:SV_POSITION;float4 c:COLOR;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;};
   V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.c=a.c*_BaseColor;o.uv=a.uv;return o;}
   half4 frag(V i):SV_Target{
    float radius=length(i.uv-.5)*2;float fade=1-smoothstep(max(9,_SpectralRevealRadius),max(9,_SpectralRevealRadius)+6,distance(i.world.xz,_FogCenter.xz));
    float worldT=saturate((length(i.world.xz-_LightingFogCenter.xz)-_LightingWorldClear)/max(_LightingWorldBlack-_LightingWorldClear,1e-4));
    float worldFade=1-_LightingFogActive*(worldT*worldT*(3-2*worldT));
    i.c.a*=pow(saturate(1-radius),2)*fade*worldFade;return i.c;
   }
   ENDHLSL
  }
 }
}
