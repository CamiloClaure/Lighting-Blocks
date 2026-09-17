Shader "LightingBlocks/Twilight Reveal"
{
 Properties{_FogRadius("Reveal radius",Float)=9 _FogFade("Feather",Float)=12 _FogStrength("Veil strength",Range(0,1))=.35 _FogColor("Twilight veil",Color)=(.05,.07,.13,1)}
 SubShader{Tags{"RenderPipeline"="UniversalPipeline"} ZWrite Off ZTest Always Cull Off
 Pass{HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float _FogRadius,_FogFade,_FogStrength;float4 _FogColor;
 CBUFFER_END
 float3 _FogCenter;float _SpectralRevealRadius;
 half4 Frag(Varyings i):SV_Target{
 float2 uv=i.texcoord;half4 color=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv);float depth=SampleSceneDepth(uv);
 #if !UNITY_REVERSED_Z
 depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
 #endif
 float3 world=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);float radius=max(_FogRadius,_SpectralRevealRadius);
 float veil=smoothstep(radius,radius+_FogFade,length(world.xz-_FogCenter.xz))*_FogStrength;
 return half4(lerp(color.rgb,_FogColor.rgb,veil),color.a);
 }
 ENDHLSL}
 }
}
