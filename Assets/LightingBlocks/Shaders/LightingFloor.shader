Shader "LightingBlocks/Lighting Floor"
{
    Properties
    {
        _BaseColor("Outline color", Color) = (1,1,1,1)
        _Opacity("Face opacity", Range(0,1)) = .015
        _EdgeBrightness("Outline brightness", Range(0,1)) = .48
        _EdgeGlow("Faint edge glow", Range(0,.2)) = .035
        _EdgeWidth("Outline width", Range(.001,.05)) = .003
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
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Opacity, _EdgeBrightness, _EdgeWidth, _EdgeGlow;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                // UVs span each individual face of the bridge slab. Preserve a
                // minimum screen width instead of fading subpixel outlines away.
                float2 distanceToEdge=min(input.uv,1-input.uv);
                float2 footprint=max(fwidth(input.uv),float2(.0001,.0001));
                float2 distancePixels=distanceToEdge/footprint;
                float2 widthPixels=max(_EdgeWidth/footprint,float2(.35,.35));
                float2 edges=1-smoothstep(widthPixels-.5,widthPixels+.5,distancePixels);
                float outline=max(edges.x,edges.y);
                // A restrained screen-space halo keeps the thin white ink softly lit
                // without increasing global bloom or filling the transparent faces.
                float2 haloDistance=max(distancePixels-widthPixels,0);
                float2 halos=exp2(-haloDistance*haloDistance/1.5625);
                float halo=max(halos.x,halos.y)*(1-outline);
                float alpha=lerp(_Opacity,1,outline)*_BaseColor.a;
                // Premultiplied output applies coverage once. The previous shader
                // multiplied the thin edge into both brightness and alpha.
                float ink=_EdgeBrightness*outline+_EdgeGlow*halo+.008*_Opacity*(1-outline);
                return half4(_BaseColor.rgb*ink*_BaseColor.a,alpha);
            }
            ENDHLSL
        }
    }
}
