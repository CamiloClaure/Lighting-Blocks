using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        // ponytail: tuned against CheckUnboundedWorld. DrawFar builds the poses of chunks it has not
        // seen yet nearest first, at most this many (a chunk counts 1 plus its tiles) per frame: the
        // first Daybreak over 12k tiles fills in over ~12 frames, inside the fog's 0.4 s rise.
        public const int FarBuildPerFrame = 1024;
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
        // Bumped whenever the stored cells or the loaded chunks change; DrawFar rebuilds on it.
        int revision;
        // DrawFar: each tier's placed look as (mesh, material, pose from the cell centre), and the
        // instance poses of the last rebuild per tier and part.
        (Mesh mesh, Material material, Matrix4x4 local)[][] parts;
        List<Matrix4x4>[][] farPoses;
        // Each chunk's instance poses [tier][part], built once and dropped when a cell in it changes, so
        // a rebuild only copies arrays.
        readonly Dictionary<Vector2Int, (int count, Matrix4x4[][][] poses)> chunkPoses = new Dictionary<Vector2Int, (int, Matrix4x4[][][])>();
        static readonly Matrix4x4[][][] NoPoses = new Matrix4x4[0][][];
        // The chunks already in the pose lists, so a frame finishing a cold cast only appends.
        readonly HashSet<Vector2Int> farListed = new HashSet<Vector2Int>();
        readonly int[] chunkTiers = new int[ChunkSize * ChunkSize], tierCounts = new int[8], tierFill = new int[8];
        readonly MaterialPropertyBlock farLook = new MaterialPropertyBlock();
        Vector2Int farChunk; int farRevision = -1, farSpan = -1;
        // Chunk offsets nearest first, out to DrawFar's widest reach.
        static Vector2Int[] farOrder;

        public LightingTiles(GameManager game) { this.game = game; }

        public int Count => cells.Count;
        // The live set; callers read it and never change it.
        public HashSet<Vector2Int> Cells => cells;
        public int VisualCount => visuals.Count;
        public int LoadedChunks => loaded.Count;
        // Tile GameObjects ever instantiated; the pool should keep this flat on a walk back.
        public int Created { get; private set; }
        // Stored tiles DrawFar drew from data in its last rebuild.
        public int FarCount { get; private set; }
        // Some chunk in reach still waits for its poses; the next DrawFar picks it up.
        public bool FarPending { get; private set; }
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
            revision++; chunkPoses.Remove(ChunkOf(cell));
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
            revision++; chunkPoses.Remove(ChunkOf(cell));
            if (!hubsDirty) foreach (var hub in LightingBridgeGrid.OuterHubs) if (!hubs.Contains(hub) && LightingBridgeGrid.TouchesHub(cell, hub)) hubs.Add(hub);
            block.id = "tile-" + cell.x + "_" + cell.y; visuals[cell] = block;
            Load(ChunkOf(cell)); game.TilesChanged();
        }
        // Takes the cell out and hands back its GameObject (still in game.Blocks) for the caller to reuse.
        public BridgeBlock Detach(Vector2Int cell)
        {
            if (!cells.Remove(cell)) return null;
            hubsDirty = true; revision++; chunkPoses.Remove(ChunkOf(cell)); game.TilesChanged();
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
            cells.Clear(); cells.UnionWith(next); hubsDirty = true; revision++; chunkPoses.Clear(); game.TilesChanged();
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
            if (spawned > 0) { Physics.SyncTransforms(); revision++; game.TilesChanged(); }
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
            if (retired > 0) revision++;
            return retired;
        }
        BridgeBlock Spawn(Vector2Int cell)
        {
            int tier = Tier(cell);
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
        int Tier(Vector2Int cell) => Mathf.Clamp(GameManager.RequiredTier(0, cell.x, cell.y), 0, game.blockPrefabs.Length - 1);

        // LightingFog, every frame: the stored tiles of the chunks within reach that are not loaded,
        // GPU-instanced in the look of a freshly streamed tile (connected, never stepped on). No
        // GameObjects and no colliders: past the load radius nothing is in reach of feet or grenade.
        // Normally that is none (the fog is black well inside the load radius); Far Sight and Daybreak
        // push the reach out to LightingReveal.Reach. The pose lists are rebuilt from the per-chunk
        // cache on a new chunk underfoot, a stored or loaded change, or a new reach.
        public void DrawFar(Vector3 player, float reach)
        {
            if (game.blockPrefabs == null || game.blockPrefabs.Length == 0) return;
            if (parts == null) CaptureParts();
            reach = Mathf.Min(reach, LightingReveal.Reach) + 2;
            int span = Mathf.CeilToInt(reach / (ChunkSize * LightingBridgeGrid.CellSize));
            var here = ChunkOf(LightingBridgeGrid.CellOf(player));
            bool fresh = here != farChunk || revision != farRevision || span != farSpan;
            if (fresh || FarPending)
            {
                if (fresh)
                {
                    farChunk = here; farRevision = revision; farSpan = span; FarCount = 0; farListed.Clear();
                    foreach (var tier in farPoses) foreach (var poses in tier) poses.Clear();
                }
                FarPending = false;
                if (farOrder == null)
                {
                    int widest = Mathf.CeilToInt((LightingReveal.Reach + 2) / (ChunkSize * LightingBridgeGrid.CellSize));
                    var order = new List<Vector2Int>();
                    for (int x = -widest; x <= widest; x++) for (int z = -widest; z <= widest; z++) order.Add(new Vector2Int(x, z));
                    order.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
                    farOrder = order.ToArray();
                }
                int budget = FarBuildPerFrame;
                foreach (var offset in farOrder)
                {
                    if (Mathf.Abs(offset.x) > span || Mathf.Abs(offset.y) > span) continue;
                    var chunk = here + offset;
                    if (farListed.Contains(chunk) || loaded.Contains(chunk) || Distance(chunk, player) > reach) continue;
                    if (!chunkPoses.ContainsKey(chunk)) { if (budget <= 0) { FarPending = true; continue; } budget -= 1 + ChunkPoses(chunk).count; }
                    farListed.Add(chunk);
                    var (count, poses) = ChunkPoses(chunk);
                    if (count == 0) continue;
                    FarCount += count;
                    for (int tier = 0; tier < poses.Length; tier++)
                        for (int k = 0; k < poses[tier].Length; k++) if (poses[tier][k] != null) farPoses[tier][k].AddRange(poses[tier][k]);
                }
            }
            if (FarCount == 0) return;
            farLook.SetFloat("_Connected", 1); farLook.SetFloat("_Highlight", 0); farLook.SetFloat("_Glow", 0); farLook.SetFloat("_LitGain", BlockArt.LightingLitGain);
            var bounds = new Bounds(player, new Vector3(reach * 2 + 8, 20, reach * 2 + 8));
            for (int tier = 0; tier < parts.Length; tier++)
                for (int k = 0; k < parts[tier].Length; k++)
                {
                    var poses = farPoses[tier][k]; if (poses.Count == 0) continue;
                    var rp = new RenderParams(parts[tier][k].material) { matProps = farLook, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = bounds };
                    for (int start = 0; start < poses.Count; start += 1023) Graphics.RenderMeshInstanced(rp, parts[tier][k].mesh, 0, poses, Mathf.Min(1023, poses.Count - start), start);
                }
        }
        // Sized arrays in two passes (count by tier, then fill): no lists, so a cold cast makes little garbage.
        (int count, Matrix4x4[][][] poses) ChunkPoses(Vector2Int chunk)
        {
            if (chunkPoses.TryGetValue(chunk, out var cached)) return cached;
            int count = 0;
            for (int t = 0; t < parts.Length; t++) { tierCounts[t] = 0; tierFill[t] = 0; }
            for (int i = 0; i < chunkTiers.Length; i++)
            {
                var cell = new Vector2Int(chunk.x * ChunkSize + i / ChunkSize, chunk.y * ChunkSize + i % ChunkSize);
                chunkTiers[i] = cells.Contains(cell) ? Tier(cell) : -1;
                if (chunkTiers[i] >= 0) { tierCounts[chunkTiers[i]]++; count++; }
            }
            if (count == 0) return chunkPoses[chunk] = (0, NoPoses);
            var poses = new Matrix4x4[parts.Length][][];
            for (int t = 0; t < parts.Length; t++)
            {
                poses[t] = new Matrix4x4[parts[t].Length][];
                if (tierCounts[t] > 0) for (int k = 0; k < parts[t].Length; k++) poses[t][k] = new Matrix4x4[tierCounts[t]];
            }
            for (int i = 0; i < chunkTiers.Length; i++)
            {
                int tier = chunkTiers[i]; if (tier < 0) continue;
                var cell = new Vector2Int(chunk.x * ChunkSize + i / ChunkSize, chunk.y * ChunkSize + i % ChunkSize);
                int at = tierFill[tier]++;
                for (int k = 0; k < parts[tier].Length; k++) poses[tier][k][at] = FarPose(cell, tier, k);
            }
            return chunkPoses[chunk] = (count, poses);
        }
        // Where part k of a tier-tier tile at this cell draws: what the tile's own renderer would be. The
        // tile is never rotated or scaled per cell, so this is the part's pose moved to the cell.
        public Matrix4x4 FarPose(Vector2Int cell, int tier, int k)
        {
            if (parts == null) CaptureParts();
            var at = LightingBridgeGrid.Cell(cell.x, cell.y); var m = parts[tier][k].local;
            m.m03 += at.x; m.m13 += at.y; m.m23 += at.z;
            return m;
        }
        public int FarParts(int tier) { if (parts == null) CaptureParts(); return parts[tier].Length; }
        // Read once from the prefabs, no instantiate: every enabled mesh renderer under
        // BlockArt.placedVisual, posed as GameManager.ConfigureBlock (root scale 2, .36, 2) and
        // BlockArt.Refresh (placedVisual scaled back to physical metres) leave it.
        void CaptureParts()
        {
            parts = new (Mesh, Material, Matrix4x4)[game.blockPrefabs.Length][];
            farPoses = new List<Matrix4x4>[parts.Length][];
            var root = new Vector3(2, .36f, 2);
            for (int t = 0; t < parts.Length; t++)
            {
                var list = new List<(Mesh, Material, Matrix4x4)>();
                var art = game.blockPrefabs[t] != null ? game.blockPrefabs[t].GetComponent<BlockArt>() : null;
                if (art != null && art.placedVisual != null)
                {
                    var visual = art.placedVisual;
                    var pose = Matrix4x4.Scale(root) * Matrix4x4.TRS(visual.localPosition, visual.localRotation, new Vector3(1 / root.x, 1 / root.y, 1 / root.z));
                    foreach (var r in visual.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var filter = r.GetComponent<MeshFilter>();
                        if (!r.enabled || filter == null || filter.sharedMesh == null || r.sharedMaterial == null) continue;
                        list.Add((filter.sharedMesh, r.sharedMaterial, pose * visual.worldToLocalMatrix * r.localToWorldMatrix));
                    }
                }
                parts[t] = list.ToArray();
                farPoses[t] = new List<Matrix4x4>[parts[t].Length];
                for (int k = 0; k < parts[t].Length; k++) farPoses[t][k] = new List<Matrix4x4>();
            }
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
