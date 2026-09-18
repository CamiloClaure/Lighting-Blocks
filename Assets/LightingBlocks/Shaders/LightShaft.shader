Shader "LightingBlocks/Light Shaft"
{
 Properties{
 _Shaft("Shaft",Color)=(0.175,0.14,0.35,1)
 }
 SubShader{
 Tags{"RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline"}
 Pass{
 Blend One One
 ZWrite Off
 Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma target 3.5
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _Shaft;
 CBUFFER_END
 struct Attributes{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
 struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
 Varyings Vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;return o;}
 half4 Frag(Varyings i):SV_Target{
 // Bright where the shaft leaves the rim, gone before it reaches the bottom. Squared so it
 // reads as light thinning out into the void rather than a painted ramp.
 float fade=saturate(i.uv.y);fade*=fade;
 return half4(_Shaft.rgb*fade,fade);
 }
 ENDHLSL
 }
 }
 Fallback Off
}
