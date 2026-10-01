using System.Collections.Generic;
using UnityEngine;

namespace LightingBlocks
{
    // Lighting's placed tiles, without a size limit. The cell set is the saved truth; GameObjects exist
    // only for the chunks near the player, pooled per tier as chunks stream out and back in, so the
    // per-tile loops (BridgeSteps, LightingFog.CullTiles, Recalculate) only ever see the nearby ones.
    // Every stored cell is connected: a tile is only laid on the frontier, a reclaim keeps the rest
    // connected and an import that is not is rejected. The cell set is therefore the network itself.
    public sealed class LightingTiles
    {
        // ponytail: tuned against CheckUnboundedWorld in the editor. 8 m chunks; the fog is black at 24 m
        // and a grenade lands at most 28 m out, so the 40 m load radius covers every tile the player can
        // see or touch. A frame retires up to TilesPerFrame tiles and spawns up to TilesPerFrame pooled
        // ones; a fresh Instantiate (about 0.25 ms against 0.03 ms from the pool) counts as
        // InstantiateCost of them, so a walk into denser ground grows the pool a few tiles a frame.
        public const int ChunkSize = 4, TilesPerFrame = 64, InstantiateCost = 8;
        public const float LoadRadius = 40f, UnloadRadius = 56f;

        readonly GameManager game;
        readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        readonly HashSet<string> hubs = new HashSet<string>();
        bool hubsDirty = true;
        readonly Dictionary<Vector2Int, BridgeBlock> visuals = new Dictionary<Vector2Int, BridgeBlock>();
        readonly HashSet<Vector2Int> loaded = new HashSet<Vector2Int>();
        readonly Dictionary<int, Stack<BridgeBlock>> pool = new Dictionary<int, Stack<BridgeBlock>>();
        readonly List<Vector2Int> queue = new List<Vector2Int>(), unloads = new List<Vector2Int>();
        readonly HashSet<BridgeBlock> leaving = new HashSet<BridgeBlock>();
        Vector2Int? planned;

        public LightingTiles(GameManager game) { this.game = game; }

        public int Count => cells.Count;
        // The live set; callers read it and never change it.
        public HashSet<Vector2Int> Cells => cells;
        public int VisualCount => visuals.Count;
        public int LoadedChunks => loaded.Count;
        // Tile GameObjects ever instantiated; the pool should keep this flat on a walk back.
        public int Created { get; private set; }
        public bool Contains(Vector2Int cell) => cells.Contains(cell);
        public BridgeBlock Visual(Vector2Int cell) => visuals.TryGetValue(cell, out var b) ? b : null;
        public IEnumerable<string> Hubs { get { if (hubsDirty) RebuildHubs(); return hubs; } }
        public bool ReachesHub(string hub) { if (hubsDirty) RebuildHubs(); return hubs.Contains(hub); }
        public void InvalidateHubs() => hubsDirty = true;

        public static Vector2Int ChunkOf(Vector2Int cell) => new Vector2Int(FloorDiv(cell.x), FloorDiv(cell.y));
        static int FloorDiv(int v) => v >= 0 ? v / ChunkSize : (v - ChunkSize + 1) / ChunkSize;
        // Metres from a point to the chunk's footprint, 0 inside it.
        public static float Distance(Vector2Int chunk, Vector3 point)
        {
            float span = ChunkSize * LightingBridgeGrid.CellSize, half = LightingBridgeGrid.CellSize * .5f;
            float minX = chunk.x * span - half, minZ = chunk.y * span - half;
            float dx = Mathf.Max(minX - point.x, 0, point.x - minX - span), dz = Mathf.Max(minZ - point.z, 0, point.z - minZ - span);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
        bool Near(Vector2Int chunk) => game.player != null && Distance(chunk, game.player.transform.position) <= LoadRadius;

        // A new tile. It gets its GameObject at once when its chunk is near the player (loading the
        // chunk if needed), so a dash never steps onto a tile without a collider. Null when far.
        public BridgeBlock Add(Vector2Int cell)
        {
            if (!cells.Add(cell)) return Visual(cell);
            if (!hubsDirty) foreach (var hub in LightingBridgeGrid.OuterHubs) if (!hubs.Contains(hub) && LightingBridgeGrid.TouchesHub(cell, hub)) hubs.Add(hub);
            var chunk = ChunkOf(cell);
            if (loaded.Contains(chunk)) { var b = Spawn(cell); game.TilesChanged(); return b; }
            if (Near(chunk)) Load(chunk);
            return Visual(cell);
        }
        // A loose stone placed by hand becomes this cell's tile; it is already in game.Blocks.
        public void Adopt(Vector2Int cell, BridgeBlock block)
        {
            if (!cells.Add(cell)) return;
            if (!hubsDirty) foreach (var hub in LightingBridgeGrid.OuterHubs) if (!hubs.Contains(hub) && LightingBridgeGrid.TouchesHub(cell, hub)) hubs.Add(hub);
            block.id = "tile-" + cell.x + "_" + cell.y; visuals[cell] = block;
            Load(ChunkOf(cell)); game.TilesChanged();
        }
        // Takes the cell out and hands back its GameObject (still in game.Blocks) for the caller to reuse.
        public BridgeBlock Detach(Vector2Int cell)
        {
            if (!cells.Remove(cell)) return null;
            hubsDirty = true; game.TilesChanged();
            if (!visuals.TryGetValue(cell, out var b)) return null;
            visuals.Remove(cell); return b;
        }
        public void Remove(Vector2Int cell)
        {
            var b = Detach(cell); if (b == null) return;
            Retire(b); game.Blocks.Remove(b); leaving.Clear();
        }
        // An import: every GameObject goes back to the pool; Stream brings the new network's near chunks in.
        public void Replace(IEnumerable<Vector2Int> next)
        {
            foreach (var b in visuals.Values) Retire(b);
            game.Blocks.RemoveAll(leaving.Contains); leaving.Clear();
            visuals.Clear(); loaded.Clear(); queue.Clear(); unloads.Clear(); planned = null;
            cells.Clear(); cells.UnionWith(next); hubsDirty = true; game.TilesChanged();
        }

        // Every frame (GameManager.Update). A new chunk underfoot replans the radius: far chunks queue to
        // unload, missing ones to load, nearest first. Each call unloads first (so the pool has their
        // GameObjects), then loads the 3x3 chunks under the player at once if missing, then streams the
        // rest: TilesPerFrame retired and TilesPerFrame spawned at most. now does it all in this call
        // (import, teleport).
        public void Stream(Vector3 player, bool now = false)
        {
            var here = ChunkOf(LightingBridgeGrid.CellOf(player));
            if (now || planned != here) { planned = here; Plan(player, here); }
            int retired = 0;
            while (unloads.Count > 0 && (now || retired < TilesPerFrame))
            {
                retired += Unload(unloads[unloads.Count - 1]); unloads.RemoveAt(unloads.Count - 1);
            }
            if (leaving.Count > 0) { game.Blocks.RemoveAll(leaving.Contains); leaving.Clear(); game.TilesChanged(); }
            int cost = 0;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) cost += Load(here + new Vector2Int(x, z));
            while (queue.Count > 0 && (now || cost < TilesPerFrame))
            {
                var next = queue[queue.Count - 1]; queue.RemoveAt(queue.Count - 1);
                cost += Load(next);
            }
        }
        void Plan(Vector3 player, Vector2Int here)
        {
            unloads.Clear();
            foreach (var chunk in loaded) if (Distance(chunk, player) > UnloadRadius) unloads.Add(chunk);
            queue.Clear();
            int reach = Mathf.CeilToInt(LoadRadius / (ChunkSize * LightingBridgeGrid.CellSize)) + 1;
            for (int x = -reach; x <= reach; x++) for (int z = -reach; z <= reach; z++)
            {
                var chunk = here + new Vector2Int(x, z);
                if (!loaded.Contains(chunk) && Distance(chunk, player) <= LoadRadius) queue.Add(chunk);
            }
            queue.Sort((a, b) => Distance(b, player).CompareTo(Distance(a, player))); // nearest last, popped first
        }

        // Marks the chunk loaded (empty ones too, so a tile laid there later spawns at once) and spawns
        // every stored cell in it that has no GameObject yet. Returns its cost: pooled spawns plus
        // InstantiateCost per new GameObject.
        int Load(Vector2Int chunk)
        {
            if (!loaded.Add(chunk)) return 0;
            int spawned = 0, created = Created;
            for (int x = 0; x < ChunkSize; x++) for (int z = 0; z < ChunkSize; z++)
            {
                var cell = new Vector2Int(chunk.x * ChunkSize + x, chunk.y * ChunkSize + z);
                if (cells.Contains(cell) && !visuals.ContainsKey(cell)) { Spawn(cell); spawned++; }
            }
            if (spawned > 0) { Physics.SyncTransforms(); game.TilesChanged(); }
            return spawned + (Created - created) * (InstantiateCost - 1);
        }
        // Returns how many GameObjects went back to the pool; the caller takes them out of game.Blocks.
        int Unload(Vector2Int chunk)
        {
            if (!loaded.Remove(chunk)) return 0;
            int retired = 0;
            for (int x = 0; x < ChunkSize; x++) for (int z = 0; z < ChunkSize; z++)
            {
                var cell = new Vector2Int(chunk.x * ChunkSize + x, chunk.y * ChunkSize + z);
                if (visuals.TryGetValue(cell, out var b)) { visuals.Remove(cell); Retire(b); retired++; }
            }
            return retired;
        }
        BridgeBlock Spawn(Vector2Int cell)
        {
            int tier = Mathf.Clamp(GameManager.RequiredTier(0, cell.x, cell.y), 0, game.blockPrefabs.Length - 1);
            BridgeBlock b = null;
            if (pool.TryGetValue(tier, out var stack)) while (b == null && stack.Count > 0) b = stack.Pop();
            if (b == null) { b = Object.Instantiate(game.blockPrefabs[tier], LightingBridgeGrid.Cell(cell.x, cell.y), Quaternion.identity, game.blocksRoot); Created++; }
            else b.gameObject.SetActive(true);
            b.id = "tile-" + cell.x + "_" + cell.y; b.tier = tier; b.placed = true; b.rewarded = false;
            b.gap = 0; b.column = cell.x; b.row = cell.y; b.Connected = true; b.lit = 0; b.painted = null;
            if (b.culled) { b.culled = false; if (b.renderers != null) foreach (var r in b.renderers) if (r != null) r.forceRenderingOff = false; }
            game.ConfigureBlock(b, false); game.Paint(b);
            visuals[cell] = b; game.Blocks.Add(b);
            return b;
        }
        // Back to the pool. The caller takes it out of game.Blocks (in one RemoveAll over `leaving`).
        void Retire(BridgeBlock b)
        {
            if (b == null) return;
            b.gameObject.SetActive(false); leaving.Add(b);
            if (!pool.TryGetValue(b.tier, out var stack)) pool[b.tier] = stack = new Stack<BridgeBlock>();
            stack.Push(b);
        }
        // island-1, plus every outer hub a stored cell touches (every cell is connected, so touching is reaching).
        void RebuildHubs()
        {
            hubs.Clear(); hubs.Add("island-1");
            foreach (var hub in LightingBridgeGrid.OuterHubs)
                foreach (var cell in cells) if (LightingBridgeGrid.TouchesHub(cell, hub)) { hubs.Add(hub); break; }
            hubsDirty = false;
        }
    }
}
