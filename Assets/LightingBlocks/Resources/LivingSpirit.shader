// Imported from the reference prototype (lightning-block-d, Resources/Solar/LivingSpirit.shader): the
// additive rim-and-spiral sphere its yellow souls use. LightingDots draws Lighting's particles with it.
Shader "LightingBlocks/Living Spirit" {
Properties {
_BaseColor("Spirit color",Color)=(1,0.8,0.2,1)
_EmissionColor("Energy",Color)=(3,2,0.3,1)
_Surface("Surface",Float)=0 _ZWrite("Depth",Float)=0
_SrcBlend("Source",Float)=1 _DstBlend("Destination",Float)=1
}
SubShader { Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
Pass {Blend One One ZWrite Off Cull Back
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_instancing // far tiles and dots, LightingTiles/LightingDots.DrawFar
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 local:TEXCOORD2;};
CBUFFER_START(UnityPerMaterial)
float4 _BaseColor;float4 _EmissionColor;float _Surface;float _ZWrite;float _SrcBlend;float _DstBlend;
CBUFFER_END
V vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);float pulse=1+sin(_Time.y*1.7)*0.025;o.local=i.positionOS.xyz;o.world=TransformObjectToWorld(i.positionOS.xyz*pulse);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normalOS);return o;}
half4 frag(V i):SV_Target {
float facing=saturate(dot(normalize(i.normal),normalize(GetWorldSpaceViewDir(i.world))));
float rim=pow(1-facing,2.5);
float spiral=sin(i.local.y*28+sin(i.local.x*13+_Time.y)*2-_Time.y*2.2)*0.5+0.5;
float core=pow(facing,8);
float breathing=0.85+0.15*sin(_Time.y*1.7+i.world.x);
float3 color=(_BaseColor.rgb*(0.12+rim*0.65+spiral*0.1)+_EmissionColor.rgb*(core*0.4+rim*0.2))*breathing;
return half4(color,1);
}
ENDHLSL
} } }
