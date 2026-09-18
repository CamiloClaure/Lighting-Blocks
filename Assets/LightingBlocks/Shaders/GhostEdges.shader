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
 struct Attributes{float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 Varyings Vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.color=v.color;return o;}
 half4 Frag(Varyings i):SV_Target{return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*_Color*i.color;}
 ENDHLSL
 }
 }
 Fallback Off
}
