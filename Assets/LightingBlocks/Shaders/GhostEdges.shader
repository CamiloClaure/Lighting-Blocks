Shader "LightingBlocks/Ghost Edges"
{
 Properties{
 _MainTex("Dash",2D)="white"{}
 _Color("Edge ink",Color)=(0.55,0.7,1.6,1)
 }
 SubShader{
 Tags{"RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline"}
 Pass{
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off
 Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma target 3.5
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
 CBUFFER_START(UnityPerMaterial)
 float4 _MainTex_ST;float4 _Color;
 CBUFFER_END
 // Lighting-only world fade (see LightingFog.cs). _LightingFogActive is 0 outside Lighting, so
 // worldFade always evaluates to 1 there - legacy scenes (this placement ghost is shared) are unaffected.
 float4 _LightingFogCenter;
 float _LightingFogActive,_LightingWorldClear,_LightingWorldBlack;
 struct Attributes{float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float3 world:TEXCOORD1;};
 Varyings Vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.world=TransformObjectToWorld(v.positionOS.xyz);o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.color=v.color;return o;}
 half4 Frag(Varyings i):SV_Target{
  float worldT=saturate((length(i.world.xz-_LightingFogCenter.xz)-_LightingWorldClear)/max(_LightingWorldBlack-_LightingWorldClear,1e-4));
  float worldFade=1-_LightingFogActive*(worldT*worldT*(3-2*worldT));
  return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*_Color*i.color*worldFade;
 }
 ENDHLSL
 }
 }
 Fallback Off
}
