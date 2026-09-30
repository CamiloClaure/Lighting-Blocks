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
        [Min(0)] public float worldClearDistance = 12f;
        [Tooltip("Lighting only. The world is completely black at this many metres.")]
        [Min(0)] public float worldBlackDistance = 24f;
        [Header("Particle fade (narrower, applied before bloom)")]
        [Tooltip("Lighting only. Particles read at full colour within this many metres.")]
        [Min(0)] public float particleClearDistance = 12f;
        [Tooltip("Lighting only. Particle renderers switch off beyond this many metres.")]
        [Min(0)] public float particleHiddenDistance = 24f;
        [Tooltip("Lighting only. Visible base colour of fundamental particles; fades toward black.")]
        public Color particleColor = LightingParticles.ParticleColor;

        static readonly int FogActiveId = Shader.PropertyToID("_LightingFogActive");
        static readonly int FogCenterId = Shader.PropertyToID("_LightingFogCenter");
        static readonly int WorldClearId = Shader.PropertyToID("_LightingWorldClear");
        static readonly int WorldBlackId = Shader.PropertyToID("_LightingWorldBlack");
        const int WisdomFogLayer = 31;
        static readonly int WisdomVisibleId = Shader.PropertyToID("_LightingWisdomVisible");

        readonly Dictionary<Renderer, int> wisdomRendererLayers = new Dictionary<Renderer, int>();
        readonly HashSet<Renderer> activeWisdomRenderers = new HashSet<Renderer>();
        readonly List<Renderer> wisdomRenderList = new List<Renderer>();
        readonly Dictionary<Renderer, int> wisdomMaterialCounts = new Dictionary<Renderer, int>();
        readonly Dictionary<LODGroup, LOD[]> wisdomLods = new Dictionary<LODGroup, LOD[]>();
        readonly HashSet<LODGroup> activeWisdomLods = new HashSet<LODGroup>();
        MaterialPropertyBlock wisdomProperties;
        ForestWorld cachedWisdomForest; uint cachedWisdomRevision; bool wisdomSynced;
        ForestWorld cachedWorld; uint cachedRevision;
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        Vector2Int cullCell; int cullRevision = -1;

        void Awake() { wisdomProperties = new MaterialPropertyBlock(); }

        void OnDisable()
        {
            Shader.SetGlobalFloat(FogActiveId, 0);
            foreach (var r in renderers) if (r != null) r.enabled = true;
            foreach (var pair in wisdomRendererLayers)
            {
                if (pair.Key == null) continue;
                pair.Key.gameObject.layer = pair.Value;
                if (wisdomProperties != null && wisdomMaterialCounts.TryGetValue(pair.Key, out int materials))
                {
                    for (int material = 0; material < materials; material++)
                    {
                        pair.Key.GetPropertyBlock(wisdomProperties, material);
                        wisdomProperties.SetFloat(WisdomVisibleId, 0f);
                        pair.Key.SetPropertyBlock(wisdomProperties, material);
                    }
                }
            }
            foreach (var pair in wisdomLods) if (pair.Key != null) pair.Key.SetLODs(pair.Value);
            wisdomRendererLayers.Clear(); activeWisdomRenderers.Clear(); wisdomRenderList.Clear(); wisdomMaterialCounts.Clear(); wisdomLods.Clear(); activeWisdomLods.Clear();
            cachedWisdomForest = null; wisdomSynced = false;
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
            CullTiles(game);

            var forest = ForestWorld.For(game);
            if (forest == null) return;
            SyncWisdomRenderers(forest);
            UpdateWisdomEmission();
            var visuals = forest.PickupVisuals;
            if (cachedWorld != forest || cachedRevision != forest.VisualRevision || renderers.Count != visuals.Count)
            { Resync(visuals); cachedWorld=forest; cachedRevision=forest.VisualRevision; }

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
                block.SetColor("_BaseColor", Color.Lerp(particleColor, Color.black, t));
                block.SetColor("_EmissionColor", LightingDots.Emission * (1 - t));
                r.SetPropertyBlock(block);
            }
        }
        // A tile past the black band still costs its draw calls (3-4 renderers, whose property blocks
        // keep them out of batching) only to be painted black. Stop drawing it; its collider, light
        // and state stay. forceRenderingOff is separate from Renderer.enabled, which BlockArt owns.
        // ponytail: O(tiles) per two-metre step of the player, cheap up to MaxPlacedTiles (4096).
        public void CullTiles(GameManager game, bool force = false)
        {
            var cell = LightingBridgeGrid.CellOf(game.player.transform.position);
            if (!force && cell == cullCell && game.lightingGridRevision == cullRevision) return;
            cullCell = cell; cullRevision = game.lightingGridRevision;
            // Two metres past black: a tile's nearest corner is then beyond it too.
            float reach = (worldBlackDistance + 2f) / LightingBridgeGrid.CellSize; reach *= reach;
            foreach (var b in game.Blocks)
            {
                if (b == null) continue;
                int dx = b.column - cell.x, dz = b.row - cell.y;
                bool hide = b.placed && dx * dx + dz * dz > reach;
                if (hide == b.culled) continue;
                b.culled = hide;
                if (b.renderers == null) b.renderers = b.GetComponentsInChildren<Renderer>(true);
                foreach (var r in b.renderers) if (r != null) r.forceRenderingOff = hide;
            }
        }
        void SyncWisdomRenderers(ForestWorld forest)
        {
            if (wisdomSynced && cachedWisdomForest == forest && cachedWisdomRevision == forest.VisualRevision) return;
            activeWisdomRenderers.Clear(); wisdomRenderList.Clear(); wisdomMaterialCounts.Clear(); activeWisdomLods.Clear();
            foreach (var tree in forest.WisdomTrees)
            {
                if (tree == null) continue;
                var lod = tree.GetComponent<LODGroup>();
                if (lod != null)
                {
                    activeWisdomLods.Add(lod);
                    if (!wisdomLods.ContainsKey(lod))
                    {
                        var originalLevels = lod.GetLODs();
                        wisdomLods.Add(lod, originalLevels);
                        var visibleLevels = (LOD[])originalLevels.Clone();
                        if (visibleLevels.Length > 0)
                        {
                            visibleLevels[visibleLevels.Length - 1].screenRelativeTransitionHeight = .001f;
                            lod.SetLODs(visibleLevels);
                        }
                    }
                }
                foreach (var r in tree.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var materials = r.sharedMaterials;
                    bool spectralLit = false;
                    foreach (var material in materials)
                        if (material != null && material.shader != null && material.shader.name == "LightingBlocks/Spectral Lit") { spectralLit = true; break; }
                    if (!spectralLit) continue;
                    activeWisdomRenderers.Add(r);
                    wisdomRenderList.Add(r);
                    wisdomMaterialCounts[r] = materials.Length;
                    if (!wisdomRendererLayers.ContainsKey(r)) wisdomRendererLayers.Add(r, r.gameObject.layer);
                    r.gameObject.layer = WisdomFogLayer;
                }
            }
            var stale = new List<Renderer>();
            foreach (var pair in wisdomRendererLayers)
                if (pair.Key == null || !activeWisdomRenderers.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (var r in stale)
            {
                if (r != null && wisdomRendererLayers.TryGetValue(r, out int layer)) r.gameObject.layer = layer;
                wisdomRendererLayers.Remove(r);
            }
            var staleLods = new List<LODGroup>();
            foreach (var pair in wisdomLods) if (pair.Key == null || !activeWisdomLods.Contains(pair.Key)) staleLods.Add(pair.Key);
            foreach (var lod in staleLods)
            {
                if (lod != null && wisdomLods.TryGetValue(lod, out var levels)) lod.SetLODs(levels);
                wisdomLods.Remove(lod);
            }
            cachedWisdomForest = forest; cachedWisdomRevision = forest.VisualRevision; wisdomSynced = true;
        }
        void UpdateWisdomEmission()
        {
            for (int i = 0; i < wisdomRenderList.Count; i++)
            {
                var r = wisdomRenderList[i];
                if (r == null || !wisdomMaterialCounts.TryGetValue(r, out int materials)) continue;
                for (int material = 0; material < materials; material++)
                {
                    r.GetPropertyBlock(wisdomProperties, material);
                    wisdomProperties.SetFloat(WisdomVisibleId, 1f);
                    r.SetPropertyBlock(wisdomProperties, material);
                }
            }
        }
        void Resync(IReadOnlyList<Transform> visuals)
        {
            renderers.Clear(); blocks.Clear();
            foreach (var t in visuals) { renderers.Add(t != null ? t.GetComponent<Renderer>() : null); blocks.Add(new MaterialPropertyBlock()); }
        }
    }
}
