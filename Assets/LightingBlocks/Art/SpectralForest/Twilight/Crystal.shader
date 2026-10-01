Shader "LightingBlocks/Twilight Crystal"
{
    Properties
    {
        [HDR] _BaseColor("Glass tint", Color) = (.08,.3,1,1)
        _Opacity("Glass density", Range(0,1)) = .42
        _Highlight("Interaction", Float) = 0
        _Connected("Connected", Float) = 0
        _Refraction("Refraction", Float) = .002
        _Glow("Step illumination", Range(0,1)) = 1
        _LitGain("Lit brightness", Float) = 1
        _IdleVisibility("Unstepped tint", Range(0,.3)) = .14
        _FacetSeed("Facet variation", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing // far tiles and dots, LightingTiles/LightingDots.DrawFar
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Opacity, _Highlight, _Connected, _Refraction, _Glow, _FacetSeed, _IdleVisibility, _LitGain;
            CBUFFER_END
            // Zero outside Lighting. A bounded pool lets this stylized glass catch the player lamp.
            float4 _LightingCharacterPosition, _LightingCharacterColor;
            // Lighting-only world fade (see LightingFog.cs). _LightingFogActive is 0 outside
            // Lighting, so worldFade always evaluates to 1 there - legacy scenes are unaffected.
            float4 _LightingFogCenter;
            float _LightingFogActive, _LightingWorldClear, _LightingWorldBlack;
            struct A { float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p:SV_POSITION; float3 w:TEXCOORD0; float3 n:TEXCOORD1; float2 uv:TEXCOORD2; float fog:TEXCOORD3; float top:TEXCOORD4; };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); VertexPositionInputs p=GetVertexPositionInputs(i.p.xyz);
                o.p=p.positionCS; o.w=p.positionWS; o.n=TransformObjectToWorldNormal(i.n);
                o.uv=i.uv; o.top=step(.99,i.n.y); o.fog=ComputeFogFactor(o.p.z); return o;
            }
            float2 hash2(float2 p) { return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453); }
            float cloudNoise(float2 p)
            {
                float2 cell=floor(p), f=frac(p);
                f=f*f*(3-2*f);
                return lerp(lerp(hash2(cell).x,hash2(cell+float2(1,0)).x,f.x),
                            lerp(hash2(cell+float2(0,1)).x,hash2(cell+1).x,f.x),f.y);
            }
            // Distance to cell boundaries creates connected, irregular glass facets, not isolated runes.
            float3 facets(float2 p)
            {
                float2 cell=floor(p), f=frac(p), nearest=0, offset=0;
                float dist=100;
                [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 g=float2(x,y), r=g+.2+.6*hash2(cell+g)-f;
                    float d=dot(r,r); if(d<dist) { dist=d; nearest=r; offset=g; }
                }
                float edge=100, secondEdge=100;
                [unroll] for(int j=-1;j<=1;j++) [unroll] for(int i=-1;i<=1;i++)
                {
                    float2 g=offset+float2(i,j), r=g+.2+.6*hash2(cell+g)-f;
                    float2 delta=r-nearest;
                    if(dot(delta,delta)>.0001)
                    {
                        float d=dot(.5*(nearest+r),normalize(delta));
                        if(d<edge) { secondEdge=edge; edge=d; } else secondEdge=min(secondEdge,d);
                    }
                }
                return float3(edge,hash2(cell+offset).x,secondEdge);
            }
            half4 frag(V i):SV_Target
            {
                float3 n=normalize(i.n), view=GetWorldSpaceNormalizeViewDir(i.w);
                float rim=pow(1-saturate(dot(n,view)),3);
                float3 f=facets(i.uv*2.65+float2(.41,.73)+_FacetSeed);
                // Gaussian falloffs blend the light into the glass; derivative filtering
                // keeps distant facets from turning into bright, crawling pixel lines.
                float footprint=max(fwidth(f.x),.002);
                float width=sqrt(.026*.026+footprint*footprint);
                float filament=exp2(-f.x*f.x/(width*width))*(.026/width);
                float halo=exp2(-f.x*f.x/(.15*.15));
                float cloud=.65*cloudNoise(i.uv*2.4+_FacetSeed)+.35*cloudNoise(i.uv*5.2+_FacetSeed);
                float3 tint=_BaseColor.rgb;
                float glow=saturate(_Glow);
                // _LitGain scales only the stepped-on light (BlockArt sets 2 in Lighting); the idle tint keeps its level.
                float lit=glow*_LitGain;
                // Keep resting slabs readable without keeping their point lights on.
                // The tint fades out as the existing stepped-on illumination takes over.
                float idle=_IdleVisibility*(1-glow);
                float3 behind=SampleSceneColor(saturate(GetNormalizedScreenSpaceUV(i.p)+n.xz*_Refraction));
                float opacity=saturate(_Opacity+rim*.12);
                float3 glass=behind*lerp(float3(1,1,1),tint,.55)*.32;
                // Continuous cloud lighting avoids abrupt brightness changes between cells.
                glass+=tint*(.016+idle+lit*(.28+cloud*.22+rim*.18));
                float junction=exp2(-dot(f.xz,f.xz)/(.065*.065));
                float caustic=((filament*.95+halo*.48)*(.65+cloud*.55)+junction*.65)*i.top*(lit+idle*.4);
                float2 edge=min(i.uv,1-i.uv);
                float perimeter=exp2(-pow(min(edge.x,edge.y)/.095,2));
                glass+=tint*perimeter*.35*i.top*(lit+idle*.6);
                glass+=lerp(tint,sqrt(saturate(tint)),.12)*caustic;
                glass+=tint*_Highlight*.12;
                float3 toLamp=_LightingCharacterPosition.xyz-i.w;
                float distanceSquared=dot(toLamp,toLamp);
                float radius=max(_LightingCharacterPosition.w,.1);
                float falloff=saturate(1-distanceSquared/(radius*radius));
                falloff*=falloff;
                float facing=saturate(dot(n,toLamp*rsqrt(max(distanceSquared,.001))));
                glass+=sqrt(saturate(tint))*_LightingCharacterColor.rgb*falloff*facing/(1+distanceSquared);
                float worldT=saturate((length(i.w.xz-_LightingFogCenter.xz)-_LightingWorldClear)/max(_LightingWorldBlack-_LightingWorldClear,1e-4));
                opacity*=1-_LightingFogActive*(worldT*worldT*(3-2*worldT));
                return half4(MixFog(glass,i.fog)*opacity,opacity);
            }
            ENDHLSL
        }
    }
}
