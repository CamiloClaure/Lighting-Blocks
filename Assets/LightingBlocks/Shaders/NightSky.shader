Shader "LightingBlocks/Night Sky"
{
 Properties{
 _SkyTop("Sky top",Color)=(0.043,0.051,0.141,1)
 _SkyHorizon("Sky horizon",Color)=(0.102,0.106,0.239,1)
 _SkyBottom("Sky bottom",Color)=(0.027,0.031,0.102,1)
 _StarDensity("Star density",Range(0,1))=0.35
 _StarBrightness("Star brightness",Float)=2.5
 _StarTint("Star tint",Color)=(0.85,0.9,1,1)
 _StarCells("Star cells",Float)=220
 }
 SubShader{
 Tags{"RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline"}
 Pass{
 Cull Off ZWrite Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma target 3.5
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _SkyTop,_SkyHorizon,_SkyBottom,_StarTint;float _StarDensity,_StarBrightness,_StarCells;
 CBUFFER_END
 struct Attributes{float4 positionOS:POSITION;};
 struct Varyings{float4 positionCS:SV_POSITION;float3 dir:TEXCOORD0;};
 Varyings Vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.dir=v.positionOS.xyz;return o;}
 float Hash3(float3 p,float3 c){return frac(sin(dot(p,c))*43758.5453);}
 half4 Frag(Varyings i):SV_Target{
 float3 d=normalize(i.dir);
 float3 col=d.y>=0?lerp(_SkyHorizon.rgb,_SkyTop.rgb,smoothstep(0,0.6,d.y)):lerp(_SkyHorizon.rgb,_SkyBottom.rgb,smoothstep(0,0.5,-d.y));
 float3 cell=floor(d*_StarCells);float3 f=frac(d*_StarCells);
 float r0=Hash3(cell,float3(12.9898,78.233,37.719));
 float r1=Hash3(cell,float3(39.346,11.135,83.155));
 float r2=Hash3(cell,float3(73.156,52.235,9.151));
 float r3=Hash3(cell,float3(21.233,66.912,44.812));
 float hash4=Hash3(cell,float3(91.345,17.821,58.129));
 float hash5=Hash3(cell,float3(4.671,29.981,63.447));
 if(r0<_StarDensity*0.08){
 float3 c=float3(r1,r2,hash4);
 float dist=length(f-c);
 float s=smoothstep(0.12+0.1*r3,0.0,dist);
 float3 tint=lerp(_StarTint.rgb,float3(1,0.9,0.8),hash5*0.3);
 col+=s*_StarBrightness*lerp(0.35,1.0,r3)*tint;
 }
 return half4(col,1);
 }
 ENDHLSL
 }
 }
 Fallback Off
}
