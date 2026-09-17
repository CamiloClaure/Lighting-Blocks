Shader "LightingBlocks/Twilight Mote"
{
 Properties{[HDR]_BaseColor("Tint",Color)=(1,1,1,1)}
 SubShader{Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 Pass{Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor;
 CBUFFER_END
 struct A{float4 p:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float eye:TEXCOORD1;};
 V vert(A i){V o;VertexPositionInputs v=GetVertexPositionInputs(i.p.xyz);o.p=v.positionCS;o.eye=-v.positionVS.z;o.color=i.color*_BaseColor;o.uv=i.uv;return o;}
 half4 frag(V i):SV_Target{float r=length(i.uv-.5)*2;float fade=pow(saturate(1-r),2);float depth=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.p)),_ZBufferParams);return half4(i.color.rgb,i.color.a*fade*saturate((depth-i.eye)*2));}
 ENDHLSL}
 }
}
