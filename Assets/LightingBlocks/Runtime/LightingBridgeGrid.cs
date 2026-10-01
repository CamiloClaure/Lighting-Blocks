using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightingBlocks
{
    /// <summary>Lighting's unbounded two-metre, orthogonal bridge grid.</summary>
    public static class LightingBridgeGrid
    {
        public const float CellSize = 2f;
        // ponytail: build room around the hubs. Float error is about 1 mm at 8 km; past that the world
        // needs a floating origin.
        public const float HubMargin = 8192f;
        static readonly Vector2Int[] Neighbors = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        // The placed cells live in GameManager.Tiles (LightingTiles); every one of them is connected.
        internal static readonly string[] OuterHubs = { "island-2", "island-3" };
        // Collider.bounds is an engine call and there are ~100 hub platforms: read them once per
        // flood, not once per tile per hub (~600k reads at 2000 tiles). Each hub's XZ extent, grown by
        // a tile, rejects the far tiles (nearly all of them) before the per-platform test.
        // Overlaps/InBounds mirror PlatformSupport.OverlapsCell/TryGetWorldBounds on cached bounds,
        // over enabled colliders only (as those do; hub contact never filtered on it). A frontier scan
        // calls Overlaps ~70k times on a 2000-tile network, so each hub's platforms sit in a group
        // with its extent, and a cell outside that extent skips the whole group.
        sealed class HubSnapshot
        {
            public readonly List<(string hub, Bounds bounds)> platforms = new List<(string, Bounds)>();
            readonly Dictionary<string, Bounds> reach = new Dictionary<string, Bounds>();
            readonly List<(Bounds extent, List<Bounds> boxes)> solid = new List<(Bounds, List<Bounds>)>();
            Bounds world; bool hasWorld;
            public HubSnapshot()
            {
                var groups = new Dictionary<string, int>();
                foreach (var p in PlatformSupport.ActivePlatforms)
                {
                    if (p == null || p.Surface == null) continue;
                    var b = p.Surface.bounds; platforms.Add((p.hubId, b));
                    if (p.Surface.enabled)
                    {
                        if (hasWorld) world.Encapsulate(b); else { world = b; hasWorld = true; }
                        if (!groups.TryGetValue(p.hubId ?? "", out int g)) { groups[p.hubId ?? ""] = g = solid.Count; solid.Add((b, new List<Bounds>())); }
                        var group = solid[g]; group.extent.Encapsulate(b); group.boxes.Add(b); solid[g] = group;
                    }
                    if (p.hubId == null) continue;
                    if (reach.TryGetValue(p.hubId, out var r)) { r.Encapsulate(b); reach[p.hubId] = r; } else reach[p.hubId] = b;
                }
            }
            public bool Near(Vector3 c, string hubId) => hubId == null || !reach.TryGetValue(hubId, out var r)
                || c.x >= r.min.x - 1.1f && c.x <= r.max.x + 1.1f && c.z >= r.min.z - 1.1f && c.z <= r.max.z + 1.1f;
            public bool Overlaps(Vector2Int cell)
            {
                Vector3 c = Cell(cell.x, cell.y); float half = CellSize * .5f;
                foreach (var g in solid)
                {
                    if (c.x + half <= g.extent.min.x || c.x - half >= g.extent.max.x || c.z + half <= g.extent.min.z || c.z - half >= g.extent.max.z) continue;
                    foreach (var b in g.boxes)
                        if (c.x + half > b.min.x + .01f && c.x - half < b.max.x - .01f && c.z + half > b.min.z + .01f && c.z - half < b.max.z - .01f) return true;
                }
                return false;
            }
            public bool InBounds(Vector2Int cell)
            {
                if (!hasWorld) return true;
                Vector3 p = Cell(cell.x, cell.y);
                return p.x >= world.min.x - HubMargin && p.x <= world.max.x + HubMargin && p.z >= world.min.z - HubMargin && p.z <= world.max.z + HubMargin;
            }
        }
        static HubSnapshot sharedPlatforms; // platform bounds, read once; dropped by Invalidate
        static HubSnapshot Platforms() => sharedPlatforms ??= new HubSnapshot();
        public static int PlacedCount(GameManager game) => game.Tiles.Count;
        public static bool ReachesHub(GameManager game, string hubId) => game.Tiles.ReachesHub(hubId);
        // The stored cell holding up this point: a tile reaches 1.02 m from its centre on both axes, so
        // the seam between two tiles is never void. (It used the legacy single-file bridge's .99 m on
        // x, which left a 2 cm strip at every x seam: on wide grenade-laid ground a dash step landing
        // in one found its own cell occupied and stopped dead.) The point's own cell is tried first,
        // so TileAt names the tile actually underfoot. Data only: support never waits on a chunk's
        // GameObjects.
        public static bool Supports(GameManager game, Vector3 point) => SupportingCell(game, point) != null;
        // The loaded tile GameObject holding up this point, or null.
        public static BridgeBlock TileAt(GameManager game, Vector3 point)
        {
            var cell = SupportingCell(game, point);
            return cell == null ? null : game.Tiles.Visual(cell.Value);
        }
        static readonly Vector2[] SeamProbes = { Vector2.zero, new Vector2(.02f, 0), new Vector2(-.02f, 0), new Vector2(0, .02f), new Vector2(0, -.02f),
            new Vector2(.02f, .02f), new Vector2(.02f, -.02f), new Vector2(-.02f, .02f), new Vector2(-.02f, -.02f) };
        public static Vector2Int? SupportingCell(GameManager game, Vector3 point)
        {
            foreach (var probe in SeamProbes)
            {
                var cell = CellOf(point + new Vector3(probe.x, 0, probe.y));
                if (!game.Tiles.Contains(cell)) continue;
                Vector3 p = Cell(cell.x, cell.y);
                if (Mathf.Abs(point.x - p.x) <= 1.02f && Mathf.Abs(point.z - p.z) <= 1.02f) return cell;
            }
            return null;
        }
        // Recalculate and PlatformSupport enable/disable land here: the platform snapshot is re-read. The
        // hubs the network touches are rebuilt (an O(tiles) scan) only when platforms changed, or in Edit
        // Mode, where checks toggle colliders without OnEnable; a laid tile adds its own hub.
        public static void Invalidate(GameManager game=null, bool hubs=true) { sharedPlatforms=null; if(hubs||!Application.isPlaying)game?.Tiles.InvalidateHubs(); }

        public static Vector3 Cell(int x, int z) => new Vector3(x * CellSize, -.18f, z * CellSize);
        public static Vector2Int CellOf(Vector3 point) => new Vector2Int(Mathf.RoundToInt(point.x / CellSize), Mathf.RoundToInt(point.z / CellSize));
        public static IEnumerable<Vector2Int> NeighborsOf(Vector2Int cell)
        { for (int i = 0; i < Neighbors.Length; i++) yield return cell + Neighbors[i]; }
        public static bool IsOccupied(GameManager game, Vector2Int cell) => game.Tiles.Contains(cell);
        public static bool OverlapsPlatform(Vector2Int cell) => PlatformSupport.OverlapsCell(Cell(cell.x, cell.y), CellSize);
        // Open void in bounds, off any platform: OverlapsPlatform and InBounds on the cached snapshot, for
        // scans of thousands of cells (LightingDots.DrawFar).
        public static bool Open(Vector2Int cell) { var p = Platforms(); return !p.Overlaps(cell) && p.InBounds(cell); }

        public static bool InBounds(Vector2Int cell)
        {
            if (!PlatformSupport.TryGetWorldBounds(out Bounds bounds)) return true;
            Vector3 p = Cell(cell.x, cell.y);
            return p.x >= bounds.min.x - HubMargin && p.x <= bounds.max.x + HubMargin &&
                   p.z >= bounds.min.z - HubMargin && p.z <= bounds.max.z + HubMargin;
        }

        public static HashSet<Vector2Int> Connected(GameManager game) => game.Tiles.Cells;

        public static HashSet<Vector2Int> ConnectedCells(IEnumerable<Vector2Int> source)
        {
            var occupied=new HashSet<Vector2Int>(source); var reached=new HashSet<Vector2Int>();
            var hubs=new HashSet<string>{"island-1"}; var queue=new Queue<Vector2Int>(); var platforms=new HubSnapshot();
            foreach(var cell in occupied)if(TouchesHub(cell,"island-1",platforms)&&reached.Add(cell))queue.Enqueue(cell);
            while(queue.Count>0)
            {
                var current=queue.Dequeue();
                foreach(var hub in OuterHubs)if(TouchesHub(current,hub,platforms)&&hubs.Add(hub))
                    foreach(var tile in occupied)if(TouchesHub(tile,hub,platforms)&&reached.Add(tile))queue.Enqueue(tile);
                foreach(var next in NeighborsOf(current))if(occupied.Contains(next)&&reached.Add(next))queue.Enqueue(next);
            }
            return reached;
        }

        public static bool TouchesHub(Vector2Int cell, string hubId) => TouchesHub(cell, hubId, Platforms());
        static bool TouchesHub(Vector2Int cell, string hubId, HubSnapshot hubs)
        {
            Vector3 center = Cell(cell.x, cell.y);
            if (!hubs.Near(center, hubId)) return false;
            foreach (var p in hubs.platforms)
            {
                if (hubId != null && p.hub != hubId) continue;
                Bounds b = p.bounds;
                // Tile face must meet the hub's bounds; corners only are diagonal and do not connect.
                bool zOverlap = center.z + 1f > b.min.z + .01f && center.z - 1f < b.max.z - .01f;
                bool xOverlap = center.x + 1f > b.min.x + .01f && center.x - 1f < b.max.x - .01f;
                if (zOverlap && (Mathf.Abs((center.x - 1f) - b.max.x) < .06f || Mathf.Abs((center.x + 1f) - b.min.x) < .06f)) return true;
                if (xOverlap && (Mathf.Abs((center.z - 1f) - b.max.z) < .06f || Mathf.Abs((center.z + 1f) - b.min.z) < .06f)) return true;
            }
            return false;
        }

        public static bool IsFrontier(GameManager game, Vector2Int cell, HashSet<Vector2Int> connected = null)
        {
            var platforms=Platforms(); if (connected == null) connected = game.Tiles.Cells;
            if (game.Tiles.Contains(cell) || platforms.Overlaps(cell) || !platforms.InBounds(cell)) return false;
            foreach (var h in game.Tiles.Hubs) if (TouchesHub(cell, h, platforms)) return true;
            foreach (var n in NeighborsOf(cell)) if (connected.Contains(n)) return true;
            return false;
        }

        public static bool HasObstruction(Vector2Int cell, GameManager game, BridgeBlock placementBlock = null) => HasObstruction(cell, game, placementBlock, out _);
        public static bool HasObstruction(Vector2Int cell, GameManager game, BridgeBlock placementBlock, out Collider blocker)
        {
            blocker = null;
            Vector3 center = Cell(cell.x, cell.y);
            var hits = Physics.OverlapBox(center + Vector3.up * .15f, new Vector3(.98f, .16f, .98f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit == null) continue;
                if (placementBlock != null && hit.GetComponentInParent<BridgeBlock>() == placementBlock) continue;
                if (hit.GetComponentInParent<PlatformSupport>() != null) continue;
                if (hit.transform == game.player.transform || hit.transform.IsChildOf(game.player.transform)) continue;
                blocker = hit; return true;
            }
            return false;
        }

        public static int RequiredTier(int x, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663);
                h ^= h >> 13; h *= 2654435761u; h ^= h >> 16;
                return (int)(h % 3u);
            }
        }

        public static bool TryGetFrontier(GameManager game, Vector3 origin, Vector3 facing, out Vector2Int result)
        {
            result = default; var connected = Connected(game); var candidates = new HashSet<Vector2Int>();
            Vector2Int center=CellOf(origin);
            for(int x=center.x-2;x<=center.x+2;x++)for(int z=center.y-2;z<=center.y+2;z++)candidates.Add(new Vector2Int(x,z));
            facing.y = 0; if (facing.sqrMagnitude < .01f) facing = Vector3.forward; facing.Normalize();
            float best = float.MaxValue;
            foreach (var c in candidates)
            {
                Vector3 pos = Cell(c.x,c.y); Vector3 delta = pos-origin; delta.y=0;
                if (delta.magnitude > 4f || !IsFrontier(game, c, connected)) continue;
                if (HasObstruction(c, game)) continue;
                float score = delta.sqrMagnitude - Mathf.Max(0, Vector3.Dot(delta.normalized, facing)) * 2.2f;
                if (score < best) { best=score; result=c; }
            }
            return best < float.MaxValue;
        }
    }
}
