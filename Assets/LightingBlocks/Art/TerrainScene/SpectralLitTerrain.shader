// Terrain-only variant of SpectralLit. The shipping SpectralLit.shader is untouched; this one adds
// the three things a Unity Terrain needs and nothing else: the TerrainCompatible tag, hole clipping,
// and a tangent basis, because terrain meshes carry normals and uv0 but no tangents.
// Its splat blend is unchanged: weights come from _HabitatMap in world space, not from Unity's
// _Control splatmap, so terrain layers play no part here.
Shader "LightingBlocks/Spectral Lit Terrain"
{
 Properties {
 _BaseMap("Color",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1)
 _NormalMap("Normal",2D)="bump"{} _NormalStrength("Normal strength",Float)=.25
 _MaskMap("AO (G), Smoothness (A)",2D)="white"{}
 _SoilMap("Soil",2D)="white"{} _SoilNormal("Soil normal",2D)="bump"{} _SoilMask("Soil mask",2D)="white"{}
 _MossMap("Moss",2D)="white"{} _MossNormal("Growth normal",2D)="bump"{} _GrassMap("Grass ground",2D)="white"{} _HabitatMap("Habitat weights",2D)="white"{}
 _Terrain("Terrain blend",Float)=0 _Triplanar("Cliff triplanar",Float)=0 _VertexTint("Vertex tint",Float)=0
 // Moss and grass are 77% of this ground's albedo. ForestGround.mat leaves both slots unbound so
 // they read a flat 1.0, which is fine on a floor that never tilts, but a sloped surface with no
 // albedo variation bands into contour stripes. Binding the real textures restores the detail and
 // this scales them back to the brightness the flat default had: 1/0.31 for their mean luminance.
 _DetailBoost("Ground detail boost",Float)=1
 _SoftFill("Soft vegetation fill",Float)=0
 // Diagnostic only, 0 in every shipped material. 1 shadow attenuation, 2 world normal, 3 albedo,
 // 4 N.L. TerrainDiagnostic drives it to see which term the banding lives in.
 _Debug("Debug view",Float)=0
 _Smoothness("Smoothness",Float)=.3 _AO("Occlusion",Range(0,1))=.4 _Cutoff("Alpha cutoff",Range(0,1))=0 _Cull("Culling",Float)=2
 [HDR]_EmissionColor("Emission",Color)=(0,0,0,1) [HDR]_RimColor("Spectral edge",Color)=(0,0,0,1)
 _RimPower("Edge power",Float)=3 _Wind("Wind",Float)=0 _Charge("Charge",Float)=1 _EmissionShape("Shaped emission",Range(0,1))=0
 // Unity rebinds this per terrain. Declared with a white default so that a material sitting on
 // anything but a terrain reads "solid everywhere" instead of clipping the whole surface away.
 [HideInInspector] _TerrainHolesTexture("Holes",2D)="white"{}
 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "TerrainCompatible"="True"}
 Cull [_Cull]
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_NormalMap);SAMPLER(sampler_NormalMap);TEXTURE2D(_MaskMap);SAMPLER(sampler_MaskMap);
 TEXTURE2D(_SoilMap);SAMPLER(sampler_SoilMap);TEXTURE2D(_SoilNormal);SAMPLER(sampler_SoilNormal);TEXTURE2D(_SoilMask);SAMPLER(sampler_SoilMask);
 TEXTURE2D(_MossMap);SAMPLER(sampler_MossMap);TEXTURE2D(_GrassMap);SAMPLER(sampler_GrassMap);TEXTURE2D(_HabitatMap);SAMPLER(sampler_HabitatMap);
 TEXTURE2D(_MossNormal);SAMPLER(sampler_MossNormal);
 TEXTURE2D(_TerrainHolesTexture);SAMPLER(sampler_TerrainHolesTexture);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST,_BaseColor,_EmissionColor,_RimColor;
 float _SoftFill,_NormalStrength,_Terrain,_Triplanar,_Smoothness,_AO,_Cutoff,_Cull,_RimPower,_Wind,_Charge,_EmissionShape,_VertexTint,_DetailBoost,_Debug;
 CBUFFER_END
 float3 _ForestPlayer,_ForestWind;float _ForestTime;
 struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
 // holeUV is uv0 straight through. _BaseMap_ST must not touch it: on a terrain uv0 runs 0..1 across
 // the whole tile and that is exactly the space the holes texture is addressed in.
 struct V{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half fog:TEXCOORD4;float2 holeUV:TEXCOORD5;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
 V Vert(A a){V o=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);
 float3 p=a.positionOS.xyz;float3 world=TransformObjectToWorld(p);float height=saturate(p.y*.8);
 float gust=sin(_ForestTime*.55+world.x*.14+world.z*.11)+.3*sin(_ForestTime*1.1+world.x*.5);
 float2 away=world.xz-_ForestPlayer.xz;float dist=length(away);float2 bend=away/max(dist,.01)*saturate(1-dist/1.1)*height;
 float2 windDir=_ForestWind.xz;windDir=dot(windDir,windDir)>1e-6?normalize(windDir):float2(.7,.4);
 p.xz+=(windDir*gust*height+bend*1.5)*_Wind;
 VertexPositionInputs vp=GetVertexPositionInputs(p);VertexNormalInputs vn=GetVertexNormalInputs(a.normalOS,a.tangentOS);
 o.positionCS=vp.positionCS;o.positionWS=vp.positionWS;o.normalWS=vn.normalWS;o.tangentWS=half4(vn.tangentWS,a.tangentOS.w*GetOddNegativeScale());o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.holeUV=a.uv;o.fog=ComputeFogFactor(vp.positionCS.z);o.color=a.color;return o;}
 // URP applies the light's depth bias AND slope-scaled normal bias in the shadow caster's VERTEX
 // shader (Shaders/ShadowCasterPass.hlsl). Pointing the ShadowCaster pass at the ordinary Vert
 // skipped that entirely, so every bias slider on the light and in the URP asset did nothing and the
 // terrain self-shadowed into contour stripes: textbook zero-bias shadow acne. It never showed on
 // the shipping ground because that floor is one flat plane, and acne is slope-driven.
 float3 _LightDirection;float3 _LightPosition;
 V ShadowVert(A a)
 {
  V o=Vert(a);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
  float3 toLight=normalize(_LightPosition-o.positionWS);
#else
  float3 toLight=_LightDirection;
#endif
  o.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,toLight)));
  return o;
 }
 // A painted hole reads 0 in the red channel. Every pass clips, not only the lit one, or holes go on
 // writing depth and casting shadows over the void.
 void Hole(V i){clip(SAMPLE_TEXTURE2D(_TerrainHolesTexture,sampler_TerrainHolesTexture,i.holeUV).r-.5);}
 void Cut(V i){Hole(i);clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff);}
 half4 Frag(V i):SV_Target{
 UNITY_SETUP_INSTANCE_ID(i);Hole(i);float2 uv=_Terrain>.5?i.positionWS.xz/3:i.uv;half3 n=normalize(i.normalWS);
 half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv),mask=SAMPLE_TEXTURE2D(_MaskMap,sampler_MaskMap,uv);
 half3 normal=UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uv),_NormalStrength);
 if(_Terrain>.5){float4 weights=SAMPLE_TEXTURE2D(_HabitatMap,sampler_HabitatMap,(i.positionWS.xz+float2(64,64))/float2(128,256));
 half4 soil=SAMPLE_TEXTURE2D(_SoilMap,sampler_SoilMap,uv),moss=SAMPLE_TEXTURE2D(_MossMap,sampler_MossMap,uv*.8),grass=SAMPLE_TEXTURE2D(_GrassMap,sampler_GrassMap,uv);
 moss.rgb*=_DetailBoost;grass.rgb*=_DetailBoost;
 color=color*weights.a+soil*weights.b+moss*weights.r+grass*weights.g;
 mask=mask*weights.a+SAMPLE_TEXTURE2D(_SoilMask,sampler_SoilMask,uv)*weights.b+half4(1,.85,0,.18)*(weights.r+weights.g);normal=normalize(normal*weights.a+UnpackNormalScale(SAMPLE_TEXTURE2D(_SoilNormal,sampler_SoilNormal,uv),_NormalStrength)*weights.b+UnpackNormalScale(SAMPLE_TEXTURE2D(_MossNormal,sampler_MossNormal,uv),_NormalStrength)*(weights.r+weights.g));}
 if(_Triplanar>.5){float3 w=pow(abs(n),4);w/=max(dot(w,1),.001);color=SAMPLE_TEXTURE2D(_SoilMap,sampler_SoilMap,i.positionWS.zy/3)*w.x+SAMPLE_TEXTURE2D(_SoilMap,sampler_SoilMap,i.positionWS.xz/3)*w.y+SAMPLE_TEXTURE2D(_SoilMap,sampler_SoilMap,i.positionWS.xy/3)*w.z;normal=half3(0,0,1);}
 clip(color.a-_Cutoff);color.rgb*=lerp(half3(1,1,1),i.color.rgb,_VertexTint);
 // Terrain geometry has normals and uv0 but no tangents, so i.tangentWS is meaningless there. On
 // terrain the UV is world planar (positionWS.xz), and the frame that matches it is world +X
 // projected onto the surface. A heightmap can never produce a horizontal normal, so n.x is always
 // short of 1 and the projection cannot collapse.
 half3 tangent=_Terrain>.5?normalize(half3(1,0,0)-n*n.x):normalize(i.tangentWS.xyz+half3(.00001,0,0));
 // The sign is -1, not +1: the UV runs U along +X and V along +Z, and cross(up,+X) is -Z, so the
 // positive sign would flip the normal map's green channel and light every bump inside out.
 half3 bitangent=cross(n,tangent)*(_Terrain>.5?-1:i.tangentWS.w);n=normalize(mul(normal,half3x3(tangent,bitangent,n)));
 half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);half rim=pow(1-saturate(dot(n,view)),_RimPower);
 InputData input=(InputData)0;input.positionWS=i.positionWS;input.normalWS=n;input.viewDirectionWS=view;input.shadowCoord=TransformWorldToShadowCoord(i.positionWS);input.bakedGI=SampleSH(n);input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);input.shadowMask=half4(1,1,1,1);
 // Diagnostic taps, dead when _Debug is 0. Each returns one term raw so the banding can be read
 // off the screen instead of inferred from a lit result that mixes all of them together.
 if(_Debug>.5){
  if(_Debug<1.5){half s=MainLightRealtimeShadow(input.shadowCoord);return half4(s,s,s,1);}
  if(_Debug<2.5)return half4(n*.5+.5,1);
  if(_Debug<3.5)return half4(color.rgb,1);
  if(_Debug<4.5){half nl=saturate(dot(n,GetMainLight().direction));return half4(nl,nl,nl,1);}
  // 5 is the terrain's own vertex normal, untouched by the normal maps and the TBN transform;
  // 6 is the tangent-space normal the maps produced. Between them the banding has one owner.
  if(_Debug<5.5)return half4(normalize(i.normalWS)*.5+.5,1);
  return half4(normal*.5+.5,1);}
 SurfaceData surface=(SurfaceData)0;surface.albedo=color.rgb*_BaseColor.rgb;surface.alpha=1;surface.smoothness=mask.a*_Smoothness;surface.occlusion=lerp(1,mask.g,_AO);surface.normalTS=normal;
 surface.emission=surface.albedo*_SoftFill+(_EmissionColor.rgb*lerp(1,.25+.75*pow(saturate(dot(n,view)),1.5),_EmissionShape)+_RimColor.rgb*rim)*_Charge;
 half4 result=UniversalFragmentPBR(input,surface);result.rgb=MixFog(result.rgb,i.fog);return result;}
 half4 Depth(V i):SV_Target{Cut(i);return 0;}
 half4 Normals(V i):SV_Target{Cut(i);return half4(normalize(i.normalWS),0);}
 ENDHLSL
 Pass {Name "ForwardLit" Tags {"LightMode"="UniversalForwardOnly"}
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma target 3.5
 #pragma multi_compile_fog
 #pragma multi_compile_instancing
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
 #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fragment _ _LIGHT_LAYERS
 #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
 ENDHLSL }
 Pass {Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ZWrite On ColorMask R
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Depth
 #pragma multi_compile_instancing
 ENDHLSL }
 Pass {Name "DepthNormals" Tags {"LightMode"="DepthNormalsOnly"} ZWrite On
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Normals
 #pragma multi_compile_instancing
 ENDHLSL }
 Pass {Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ZWrite On ZTest LEqual ColorMask 0
 HLSLPROGRAM
 #pragma vertex ShadowVert
 #pragma fragment Depth
 #pragma multi_compile_instancing
 // Directional and punctual lights use different formulas for the normal bias.
 #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
 ENDHLSL }
 }
}
