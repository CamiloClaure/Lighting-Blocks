using System.Collections.Generic;
using UnityEngine;
namespace LightingBlocks
{
    // Lighting-only visibility limiter. Distance is horizontal (XZ) from the player, not the
    // camera, so orbiting/zooming out never reveals anything farther away - only walking does.
    // Two independent bands:
    //  - World: every opaque/transparent surface fades to black between worldClearDistance and
    //    worldBlackDistance. The opaque floor/geometry fade is a render pass (LightingFog.shader,
    //    added to LightingRenderer.asset only - see LightingSceneBuilder.BuildLightingRenderer);
    //    the transparent crystal/filament/edge/effect shaders fade themselves, reading the same
    //    _LightingFogCenter/_LightingWorldClear/_LightingWorldBlack globals this sets every frame,
    //    gated by _LightingFogActive so legacy scenes (which share those shaders) are unaffected.
    //  - Particles: a narrower band (color/emission fade before bloom, renderer disabled past
    //    particleHiddenDistance) applied directly here in C#, not through a shader global - see
    //    ForestWorld.PickupVisuals.
    // All global shader state is cleared in OnDisable, which Unity calls when this scene unloads.
    public sealed class LightingFog : MonoBehaviour
    {
        [Header("World fade (horizontal distance from the player)")]
        [Tooltip("Lighting only. The world reads at full brightness within this many metres.")]
        [Min(0)] public float worldClearDistance = 6f;
        [Tooltip("Lighting only. The world is completely black at this many metres.")]
        [Min(0)] public float worldBlackDistance = 12f;
        [Header("Particle fade (narrower, applied before bloom)")]
        [Tooltip("Lighting only. Particles read at full colour within this many metres.")]
        [Min(0)] public float particleClearDistance = 4f;
        [Tooltip("Lighting only. Particle renderers switch off beyond this many metres.")]
        [Min(0)] public float particleHiddenDistance = 6f;
        [Tooltip("Lighting only. Colour particles fade toward as they leave clear range.")]
        public Color particleTint = Color.black;

        static readonly int FogActiveId = Shader.PropertyToID("_LightingFogActive");
        static readonly int FogCenterId = Shader.PropertyToID("_LightingFogCenter");
        static readonly int WorldClearId = Shader.PropertyToID("_LightingWorldClear");
        static readonly int WorldBlackId = Shader.PropertyToID("_LightingWorldBlack");

        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();

        void OnDisable()
        {
            Shader.SetGlobalFloat(FogActiveId, 0);
            foreach (var r in renderers) if (r != null) r.enabled = true;
        }

        void LateUpdate()
        {
            var game = GameManager.Instance;
            if (game == null || game.player == null) return;
            Vector3 player = game.player.transform.position;
            Shader.SetGlobalFloat(FogActiveId, 1);
            Shader.SetGlobalVector(FogCenterId, player);
            Shader.SetGlobalFloat(WorldClearId, worldClearDistance);
            Shader.SetGlobalFloat(WorldBlackId, Mathf.Max(worldBlackDistance, worldClearDistance + .01f));

            var forest = ForestWorld.For(game);
            if (forest == null) return;
            var visuals = forest.PickupVisuals;
            if (renderers.Count != visuals.Count) Resync(visuals);

            float clear = particleClearDistance, hidden = Mathf.Max(particleHiddenDistance, clear + .01f);
            for (int i = 0; i < visuals.Count; i++)
            {
                var visual = visuals[i]; var r = renderers[i];
                if (visual == null || r == null) continue;
                var p = visual.position;
                float distance = new Vector2(p.x - player.x, p.z - player.z).magnitude;
                r.enabled = distance <= hidden;
                if (!r.enabled) continue;
                float t = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(clear, hidden, distance));
                var block = blocks[i];
                block.SetColor("_BaseColor", Color.Lerp(LightingParticles.ParticleColor, particleTint, t));
                block.SetColor("_EmissionColor", LightingParticles.ParticleColor * 1.6f * (1 - t));
                r.SetPropertyBlock(block);
            }
        }
        void Resync(IReadOnlyList<Transform> visuals)
        {
            renderers.Clear(); blocks.Clear();
            foreach (var t in visuals) { renderers.Add(t != null ? t.GetComponent<Renderer>() : null); blocks.Add(new MaterialPropertyBlock()); }
        }
    }
}
