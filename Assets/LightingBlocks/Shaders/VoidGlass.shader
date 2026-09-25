Shader "LightingBlocks/Void Glass"
{
    Properties
    {
        _Smoothness("Smoothness", Range(0,1)) = .82
        _ReflectionStrength("Light reflection strength", Range(0,3)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Smoothness, _ReflectionStrength;
            CBUFFER_END
            float4 _LightingCharacterPosition, _LightingCharacterColor;
            int _VoidParticleCount;
            float4 _VoidParticlePositions[24], _VoidParticleColors[24];
            struct A { float4 p:POSITION; float3 n:NORMAL; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; };
            V vert(A i) { V o; o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.n);return o; }
            float3 reflection(float3 p,float3 n,float3 v,float4 lamp,float3 color)
            {
                float3 delta=lamp.xyz-p;
                float d2=max(dot(delta,delta),.001);
                float3 l=delta*rsqrt(d2);
                float3 h=SafeNormalize(l+v);
                float reach=saturate(1-d2/max(lamp.w*lamp.w,.01));
                float exponent=lerp(24,256,_Smoothness);
                float spec=pow(saturate(dot(n,h)),exponent);
                float fresnel=.12+.88*pow(1-saturate(dot(v,h)),5);
                return color*spec*fresnel*(exponent*.15)*saturate(dot(n,l))*reach*reach/(1+d2);
            }
            half4 frag(V i):SV_Target
            {
                float3 n=normalize(i.normal),v=GetWorldSpaceNormalizeViewDir(i.world);
                float3 result=reflection(i.world,n,v,_LightingCharacterPosition,_LightingCharacterColor.rgb);
                for(int j=0;j<_VoidParticleCount;j++)result+=reflection(i.world,n,v,_VoidParticlePositions[j],_VoidParticleColors[j].rgb);
                // No ambient tint, emission, noise, sky reflection, or diffuse light pool.
                // Clear glass transmits the scene underneath; only reflected light is added.
                return half4(min(result*_ReflectionStrength,1),0);
            }
            ENDHLSL
        }
    }
}
