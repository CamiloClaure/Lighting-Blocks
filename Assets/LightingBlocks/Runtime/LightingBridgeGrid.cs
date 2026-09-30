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
        public const int MaxPlacedTiles = 4096;
        public const float HubMargin = 1024f;
        static readonly Vector2Int[] Neighbors = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        sealed class BoardCache { public GameManager game; public int revision; public HashSet<Vector2Int> occupied, connected; public HashSet<string> hubs; public Dictionary<Vector2Int, BridgeBlock> tiles; public HubSnapshot platforms; public List<Vector3> respawn; }
        static BoardCache boardCache;

        static BoardCache Cache(GameManager game)
        {
            unchecked
            {
                int revision=game.lightingGridRevision;
                if(boardCache!=null&&boardCache.game==game&&boardCache.revision==revision)return boardCache;
                var tiles=new Dictionary<Vector2Int,BridgeBlock>();
                foreach(var b in game.Blocks)if(b!=null&&b.placed)tiles[new Vector2Int(b.column,b.row)]=b;
                var occupied=new HashSet<Vector2Int>(tiles.Keys);
                var platforms=new HubSnapshot();
                var connected=new HashSet<Vector2Int>(); var hubs=new HashSet<string>{"island-1"}; var queue=new Queue<Vector2Int>();
                foreach(var cell in occupied)if(TouchesHub(cell,"island-1",platforms)&&connected.Add(cell))queue.Enqueue(cell);
                while(queue.Count>0)
                {
                    var current=queue.Dequeue();
                    foreach(var hub in OuterHubs)if(TouchesHub(current,hub,platforms)&&hubs.Add(hub))
                        foreach(var tile in occupied)if(TouchesHub(tile,hub,platforms)&&connected.Add(tile))queue.Enqueue(tile);
                    foreach(var next in NeighborsOf(current))if(occupied.Contains(next)&&connected.Add(next))queue.Enqueue(next);
                }
                return boardCache=new BoardCache{game=game,revision=revision,occupied=occupied,connected=connected,hubs=hubs,tiles=tiles,platforms=platforms};
            }
        }
        static readonly string[] OuterHubs = { "island-2", "island-3" };
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
        static HubSnapshot sharedPlatforms; // TouchesHub's public overload; dropped by Invalidate
        public static int PlacedCount(GameManager game) => Cache(game).occupied.Count;
        public static bool ReachesHub(GameManager game, string hubId) => Cache(game).hubs.Contains(hubId);
        // The connected tile holding up this point, or null: GameManager.BlockUnder's box test
        // (.99 x, 1.02 z half-extents) over the tile's own cell and its z neighbours, not every tile.
        public static BridgeBlock TileAt(GameManager game, Vector3 point)
        {
            var tiles=Cache(game).tiles;
            for(int i=0;i<3;i++)
            {
                if(!tiles.TryGetValue(CellOf(point+new Vector3(0,0,i==1?.02f:i==2?-.02f:0)),out var b)||b==null||!b.placed||!b.Connected)continue;
                Vector3 p=b.transform.position;
                if(Mathf.Abs(point.x-p.x)<=.99f&&Mathf.Abs(point.z-p.z)<=1.02f)return b;
            }
            return null;
        }
        // PlatformSupport enable/disable lands here too, so the shared platform snapshot goes with it.
        public static void Invalidate(GameManager game=null) { sharedPlatforms=null; if(game==null||boardCache==null||boardCache.game==game)boardCache=null; }

        // Void cells two to four beyond the network's open frontier, where a respawning particle may
        // land (ForestWorld.LightingRespawnCandidates). Built once per network revision on cached
        // platform bounds, and ordered by a cell hash so a walk from any start spreads out.
        public static List<Vector3> RespawnCells(GameManager game)
        {
            var data=Cache(game); if(data.respawn!=null)return data.respawn;
            var cells=new HashSet<Vector2Int>();
            if(data.connected.Count<MaxPlacedTiles)
            foreach(var path in data.connected)
                foreach(var direction in Neighbors)
                {
                    // IsFrontier for a cell beside the network: its hub and neighbour scans are moot here.
                    var frontier=path+direction;
                    if(data.occupied.Contains(frontier)||data.platforms.Overlaps(frontier)||!data.platforms.InBounds(frontier))continue;
                    for(int distance=1;distance<=3;distance++)
                    {
                        var cell=frontier+direction*distance; bool clear=true;
                        for(int step=1;step<=distance;step++){var route=frontier+direction*step;if(data.platforms.Overlaps(route)||data.occupied.Contains(route)){clear=false;break;}}
                        if(clear&&data.platforms.InBounds(cell))cells.Add(cell);
                    }
                }
            return data.respawn=cells.OrderBy(c=>unchecked(((uint)(c.x*73856093)^(uint)(c.y*19349663))*2654435761u))
                .Select(c=>new Vector3(c.x*CellSize,0,c.y*CellSize)).ToList();
        }

        public static Vector3 Cell(int x, int z) => new Vector3(x * CellSize, -.18f, z * CellSize);
        public static Vector2Int CellOf(Vector3 point) => new Vector2Int(Mathf.RoundToInt(point.x / CellSize), Mathf.RoundToInt(point.z / CellSize));
        public static IEnumerable<Vector2Int> NeighborsOf(Vector2Int cell)
        { for (int i = 0; i < Neighbors.Length; i++) yield return cell + Neighbors[i]; }
        public static bool IsOccupied(GameManager game, Vector2Int cell) => Cache(game).occupied.Contains(cell);
        public static bool OverlapsPlatform(Vector2Int cell) => PlatformSupport.OverlapsCell(Cell(cell.x, cell.y), CellSize);

        public static bool InBounds(Vector2Int cell)
        {
            if (!PlatformSupport.TryGetWorldBounds(out Bounds bounds)) return true;
            Vector3 p = Cell(cell.x, cell.y);
            return p.x >= bounds.min.x - HubMargin && p.x <= bounds.max.x + HubMargin &&
                   p.z >= bounds.min.z - HubMargin && p.z <= bounds.max.z + HubMargin;
        }

        public static HashSet<Vector2Int> Connected(GameManager game) => Cache(game).connected;

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

        public static bool TouchesHub(Vector2Int cell, string hubId) => TouchesHub(cell, hubId, sharedPlatforms ??= new HubSnapshot());
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
            var data=Cache(game); if (connected == null) connected = data.connected;
            if (data.occupied.Contains(cell) || data.platforms.Overlaps(cell) || !data.platforms.InBounds(cell)) return false;
            if (connected.Count >= MaxPlacedTiles) return false;
            foreach (var h in data.hubs) if (TouchesHub(cell, h, data.platforms)) return true;
            foreach (var n in NeighborsOf(cell)) if (connected.Contains(n)) return true;
            return false;
        }

        public static bool HasObstruction(Vector2Int cell, GameManager game, BridgeBlock placementBlock = null)
        {
            Vector3 center = Cell(cell.x, cell.y);
            var hits = Physics.OverlapBox(center + Vector3.up * .15f, new Vector3(.98f, .16f, .98f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit == null) continue;
                if (placementBlock != null && hit.GetComponentInParent<BridgeBlock>() == placementBlock) continue;
                if (hit.GetComponentInParent<PlatformSupport>() != null) continue;
                if (hit.transform == game.player.transform || hit.transform.IsChildOf(game.player.transform)) continue;
                return true;
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
