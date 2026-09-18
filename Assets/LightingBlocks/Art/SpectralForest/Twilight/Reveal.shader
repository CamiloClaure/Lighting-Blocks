Shader "LightingBlocks/Twilight Reveal"
{
 Properties{_FogRadius("Reveal radius",Float)=9 _FogFade("Feather",Float)=12 _FogStrength("Veil strength",Range(0,1))=.35 _FogColor("Twilight veil",Color)=(.05,.07,.13,1)
 _MistColor("Mist colour",Color)=(.36,.30,.62,1) _MistTop("Mist top height",Float)=-2 _MistDepth("Mist falloff depth",Float)=28 _MistStrength("Mist strength",Range(0,1))=.85 _VoidMist("Void mist below horizon",Range(0,1))=.7}
 SubShader{Tags{"RenderPipeline"="UniversalPipeline"} ZWrite Off ZTest Always Cull Off
 Pass{HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float _FogRadius,_FogFade,_FogStrength;float4 _FogColor;
 float _MistTop,_MistDepth,_MistStrength,_VoidMist;float4 _MistColor;
 CBUFFER_END
 float3 _FogCenter;float _SpectralRevealRadius;
 half4 Frag(Varyings i):SV_Target{
 float2 uv=i.texcoord;half4 color=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv);float depth=SampleSceneDepth(uv);
 if(Linear01Depth(depth,_ZBufferParams)>.999){
 float3 dir=normalize(ComputeWorldSpacePosition(uv,UNITY_RAW_FAR_CLIP_VALUE,UNITY_MATRIX_I_VP)-_WorldSpaceCameraPos);
 float voidMist=_VoidMist*smoothstep(.05,.75,saturate(-dir.y));
 return half4(lerp(color.rgb,_MistColor.rgb,voidMist),color.a);}
 #if !UNITY_REVERSED_Z
 depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
 #endif
 float3 world=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);float radius=max(_FogRadius,_SpectralRevealRadius);
 float mist=_MistStrength*saturate((_MistTop-world.y)/max(_MistDepth,1e-4));
 color.rgb=lerp(color.rgb,_MistColor.rgb,mist);
 float veil=smoothstep(radius,radius+_FogFade,length(world.xz-_FogCenter.xz))*_FogStrength;
 return half4(lerp(color.rgb,_FogColor.rgb,veil),color.a);
 }
 ENDHLSL}
 }
}
