Shader "LightingBlocks/Twilight Crystal"
{
 Properties{[HDR]_BaseColor("Crystal tint",Color)=(.2,.65,1,1) _Opacity("Frost",Range(0,1))=.7 _Highlight("Interaction",Float)=0 _Connected("Connected",Float)=0 _Refraction("Refraction",Float)=.003}
 SubShader{Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 Pass{Tags{"LightMode"="UniversalForward"} Blend One OneMinusSrcAlpha ZWrite Off Cull Back
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor;float _Opacity,_Highlight,_Connected,_Refraction;
 CBUFFER_END
 struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;float fog:TEXCOORD3;};
 V vert(A i){V o;VertexPositionInputs v=GetVertexPositionInputs(i.p.xyz);o.p=v.positionCS;o.w=v.positionWS;o.n=TransformObjectToWorldNormal(i.n);o.uv=i.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
 half4 frag(V i):SV_Target{float3 n=normalize(i.n);float3 view=GetWorldSpaceNormalizeViewDir(i.w);float rim=pow(1-saturate(dot(n,view)),3);float cloud=.5+.25*sin(i.w.x*13+i.w.z*7+sin(i.w.z*19))+.25*sin(i.w.z*22+i.w.x*9);
 float2 screen=GetNormalizedScreenSpaceUV(i.p);float3 behind=SampleSceneColor(saturate(screen+n.xz*_Refraction*(cloud-.5)));
 float3 tint=_BaseColor.rgb;float opacity=saturate(_Opacity+rim*.18);
 // Weight the crystal's own colour over the refracted scene: the scene term washes the hue out, and with an HDR tint the
 // bloom core clips toward white. More internal colour keeps the glow reading as cyan/lavender/amber rather than white.
 float3 body=lerp(behind*tint, tint*(.5+cloud*.3),.78);body+=tint*(rim*(.8+_Connected*.4)+_Highlight*.3);body=MixFog(body,i.fog);return half4(body*opacity,opacity);}
 ENDHLSL}
 }
}
