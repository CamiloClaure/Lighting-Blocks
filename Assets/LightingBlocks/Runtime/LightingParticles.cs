using System;
using System.Collections.Generic;
using UnityEngine;
namespace LightingBlocks
{
    // Lighting's island tile stock and the first tree's canopy fireflies. Every island holds
    // StockPerColour loose tiles of each colour at random spots on its own platforms; a stock tile
    // placed into the network is replaced on the same island RestockSeconds later. A tile carried
    // around (or dropped elsewhere) still counts for its home island, by its id.
    public sealed class LightingParticles : MonoBehaviour
    {
        // Receiver placement (ForestLayoutGenerator.PlaceReceivers/RemoveReceiverClearance): the
        // minimum distance between receiver sites in legacy layouts. Lighting has no receivers.
        public const float ReceiverSpacing = 8f;
        public const float ReceiverClearance = 2.2f;
        // ponytail: tune. 3 islands x 5 colours x 3 = 45 loose tiles.
        public const int StockPerColour = 3;
        public const float RestockSeconds = 30f;
        // A stock tile keeps clear of its Wisdom Tree (the rune circle is 3 m), other loose tiles and the player.
        const float TreeClearance = 4f, TileSpacing = 1.6f, PlayerClearance = 3f, Corner = .45f;
        // Every fundamental particle: the reference prototype's soul yellow (1, .86, .18).
        public static readonly Color ParticleColor = new Color(1f, .86f, .18f);

        GameManager game;
        readonly List<WisdomTree> trees = new List<WisdomTree>();
        readonly Dictionary<string, float> timers = new Dictionary<string, float>();
        readonly List<Bounds> boxes = new List<Bounds>();
        bool filled; float poll;

        public void ResetWorld() { trees.Clear(); timers.Clear(); filled = false; }
        public static string StockPrefix(string islandId, int tier) => "stock-" + islandId + "-" + tier + "-";
        public void AddTree(ForestWorld world, WisdomTree tree, Transform parent, IslandForestLayout layout)
        {
            game = world.GetComponent<GameManager>(); trees.Add(tree);
            if (tree.Definition.polygonIndex == 0)
            {
                var lights = new GameObject("Canopy fireflies", typeof(ParticleSystem)); lights.transform.SetParent(tree.transform, false);
                var bounds = new Bounds(tree.transform.position, Vector3.zero); foreach (var renderer in tree.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                lights.transform.position = new Vector3(tree.transform.position.x, bounds.max.y + .5f, tree.transform.position.z);
                var ps = lights.GetComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.startLifetime = 7; main.startSpeed = .04f; main.startSize = .07f; main.maxParticles = 12; main.startColor = new Color(.45f, .4f, .7f, .35f);
                var emission = ps.emission; emission.rateOverTime = 1.5f; var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 2.5f;
                lights.GetComponent<ParticleSystemRenderer>().sharedMaterial = world.configuration.particleMaterial; ps.Play();
            }
        }
        // Loose stock tiles of this island and colour, held ones included.
        public int Stock(string islandId, int tier)
        {
            string prefix = StockPrefix(islandId, tier); int count = 0;
            foreach (var b in game.Blocks) if (b != null && !b.placed && b.id != null && b.id.StartsWith(prefix, StringComparison.Ordinal)) count++;
            return count;
        }

        void Update()
        {
            if (game == null || !game.autoTick || GameHUD.SimulationPaused || !BridgeProfile.Current.isLighting) return;
            poll += Time.deltaTime; if (filled && poll < .5f) return; // ponytail: polled twice a second, the timers are 30 s
            bool fill = !filled; filled = true;
            Restock(poll, fill); poll = 0;
        }
        // fill tops every island colour up at once: Update does that on its first tick after a world build
        // (a new world, or a save from before the stock existed). Otherwise each short island colour adds
        // one tile once it has been short for RestockSeconds.
        // ponytail: the timers are not saved, so a reload refills at once. Save them if that gets abused.
        public void Restock(float delta, bool fill = false)
        {
            if (game == null) return;
            if (fill) timers.Clear();
            int tiers = Mathf.Min(BridgeBlock.TierCount, game.blockPrefabs.Length);
            foreach (var tree in trees)
            {
                if (tree == null) continue;
                for (int tier = 0; tier < tiers; tier++)
                {
                    string prefix = StockPrefix(tree.Definition.id, tier);
                    int missing = StockPerColour - Stock(tree.Definition.id, tier);
                    if (missing <= 0) { timers.Remove(prefix); continue; }
                    if (!fill)
                    {
                        if (!timers.TryGetValue(prefix, out float left)) left = RestockSeconds;
                        left -= delta;
                        if (left > 0) { timers[prefix] = left; continue; }
                        timers.Remove(prefix); missing = 1;
                    }
                    for (; missing > 0; missing--)
                        if (RandomSpot(tree, out var point)) game.AddLooseTile(tier, point, prefix + Guid.NewGuid().ToString("N"));
                }
            }
        }
        // A random spot on this tree's island: on its own platforms corner to corner (as ForestWorld.FindOutput
        // tests an output slot), unobstructed, and clear of the tree, the loose tiles and the player.
        public bool RandomSpot(WisdomTree tree, out Vector3 point)
        {
            point = default; boxes.Clear(); float total = 0;
            foreach (var platform in PlatformSupport.ActivePlatforms)
            {
                if (platform == null || platform.hubId != tree.Definition.id || platform.Surface == null || !platform.Surface.enabled) continue;
                var b = platform.Surface.bounds; boxes.Add(b); total += b.size.x * b.size.z;
            }
            if (total <= 0) return false;
            var forest = ForestWorld.For(game);
            for (int attempt = 0; attempt < 200; attempt++)
            {
                float pick = UnityEngine.Random.value * total; var box = boxes[boxes.Count - 1];
                foreach (var b in boxes) { pick -= b.size.x * b.size.z; if (pick <= 0) { box = b; break; } }
                var p = new Vector3(UnityEngine.Random.Range(box.min.x, box.max.x), .26f, UnityEngine.Random.Range(box.min.z, box.max.z));
                if (!PlatformSupport.Supports(p + new Vector3(-Corner, 0, -Corner)) || !PlatformSupport.Supports(p + new Vector3(Corner, 0, -Corner))
                    || !PlatformSupport.Supports(p + new Vector3(-Corner, 0, Corner)) || !PlatformSupport.Supports(p + new Vector3(Corner, 0, Corner))) continue;
                if (Flat(p - tree.transform.position) < TreeClearance || game.player != null && Flat(p - game.player.transform.position) < PlayerClearance) continue;
                bool crowded = false;
                foreach (var b in game.Blocks) if (b != null && !b.placed && Flat(p - b.transform.position) < TileSpacing) { crowded = true; break; }
                if (crowded || forest != null && forest.Occupied(p, new Vector3(.46f, .24f, .46f), true)) continue;
                point = p; return true;
            }
            return false;
        }
        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    }
}
