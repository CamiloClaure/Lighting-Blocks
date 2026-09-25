// Lighting-only opaque-world fog: a full-screen pass (FullScreenPassRendererFeature on
// LightingRenderer.asset only - see LightingSceneBuilder.BuildLightingRenderer) that reconstructs
// world position from the depth texture and fades scene colour to black by horizontal (XZ)
// distance from the player. Runs BeforeRenderingTransparents, so it never touches transparent
// crystal/filament/edge/effect draws - those fade themselves (see Crystal/CrystalFilaments/
// GhostEdges/SpectralMotes.shader), gated the same way, by _LightingFogActive.
// Legacy scenes never carry this renderer feature at all, so this shader never runs outside
// Lighting - unlike the transparent shaders above it needs no _LightingFogActive gate of its own.
Shader "LightingBlocks/Lighting Fog"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // Set every frame by LightingFog.cs (player XZ, and the 6/12-unit default clear/black
            // distances from its Inspector fields). Zero until that component runs, which only
            // happens in the Lighting scene.
            float4 _LightingFogCenter;
            float _LightingWorldClear, _LightingWorldBlack;
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float dist = length(world.xz - _LightingFogCenter.xz);
                float span = max(_LightingWorldBlack - _LightingWorldClear, 1e-4);
                float t = saturate((dist - _LightingWorldClear) / span);
                float fade = 1 - t * t * (3 - 2 * t); // 1 within clear distance, 0 at/beyond black distance
                return half4(color.rgb * fade, color.a);
            }
            ENDHLSL
        }
    }
}
