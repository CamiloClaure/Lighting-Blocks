using System.Collections.Generic;
using UnityEngine;

namespace LightingBlocks
{
    // The Lighting equivalent of Landscape's island polygons: marks a hub platform's walkable top
    // surface so movement, navigation, spawning, respawn and hub ownership all answer "is this
    // ground" the same way. Bridge tiles are NOT marked here - GameManager.BlockUnder already
    // supports connected bridge blocks, and GameManager.CanStand combines the two (see there).
    // Legacy scenes are untouched: they keep testing Landscape.Supports (island polygons) and never
    // register a PlatformSupport.
    //
    // TODO(B): put one of these on each generated hub platform's collider (the crystal tile mesh
    // collider, or a matching box) when building the Lighting scene, with hubId set to that hub's
    // island id (e.g. "island-1") so spawning/respawn/hub ownership can group by hub the same way
    // ForestWorld.IslandAt groups by Landscape polygon today. Do not add this component to bridge
    // stones, decoration, or anything a block/player should collide with but not stand on top of as
    // "always ground".
    //
    // TODO(A): read this from ForestNavigation.Walkable, ForestWorld.Occupied/Reserved/IslandAt and
    // any respawn/candidate-placement footprint check, in place of the island polygon test, whenever
    // BridgeProfile.Current.platformGround is set. ForestWorld.Occupied must also ignore colliders
    // that carry this component (mirroring the existing "ShoreFloor" name skip), so standing on a
    // platform is never itself reported as an obstruction.
    [RequireComponent(typeof(Collider))]
    public sealed class PlatformSupport : MonoBehaviour
    {
        // TODO(B): the hub/island id this platform belongs to.
        public string hubId;
        public Collider Surface { get; private set; }
        static readonly List<PlatformSupport> Active = new List<PlatformSupport>();
        public static IReadOnlyList<PlatformSupport> ActivePlatforms => Active;

        void OnEnable() { Surface = GetComponent<Collider>(); Active.Add(this); solidValid = false; LightingBridgeGrid.Invalidate(GameManager.Instance); }
        void OnDisable() { Active.Remove(this); solidValid = false; LightingBridgeGrid.Invalidate(GameManager.Instance); }

        // Every enabled platform with its bounds, read from the engine once per change of the active
        // set: the hubs' walk maps ask Supports tens of thousands of times per load, and reading 97
        // colliders on every call cost ~4 s per save load. Edit mode reads live (checks toggle
        // platforms there without OnEnable/OnDisable). ponytail: Play Mode platforms never move or
        // toggle their collider (the Lighting scene is built in the editor); if one ever does, set
        // it inactive and active again, or add an explicit refresh here.
        static readonly List<(PlatformSupport platform, Bounds bounds)> solid = new List<(PlatformSupport, Bounds)>();
        static bool solidValid, hasWorld;
        static Bounds world;
        static List<(PlatformSupport platform, Bounds bounds)> Solid()
        {
            if (solidValid && Application.isPlaying) return solid;
            solid.Clear(); hasWorld = false;
            foreach (var p in Active)
            {
                if (p == null || p.Surface == null || !p.Surface.enabled || !p.gameObject.activeInHierarchy) continue;
                var b = p.Surface.bounds; solid.Add((p, b));
                if (hasWorld) world.Encapsulate(b); else { world = b; hasWorld = true; }
            }
            solidValid = true; return solid;
        }

        // ponytail: an XZ bounds test, not a polygon test - every hub platform the plan describes is
        // "contiguous crystal tile platforms" on the existing two-metre grid, i.e. axis-aligned
        // rectangles, so bounds are exact. Move to a polygon test (mirroring Landscape.Inside) if a
        // hub ever needs a non-rectangular footprint.
        public static bool Supports(Vector3 point) => At(point) != null;

        public static bool OverlapsCell(Vector3 center, float size)
        {
            float half = size * .5f;
            foreach (var (_, b) in Solid())
            {
                if (center.x + half > b.min.x + .01f && center.x - half < b.max.x - .01f &&
                    center.z + half > b.min.z + .01f && center.z - half < b.max.z - .01f) return true;
            }
            return false;
        }

        public static bool TryGetWorldBounds(out Bounds bounds)
        {
            Solid(); bounds = world; return hasWorld;
        }

        // The platform under this XZ point, or null. Callers that need "which hub" (spawning,
        // respawn, hub ownership) read hubId off the result instead of a separate lookup.
        public static PlatformSupport At(Vector3 point)
        {
            foreach (var (p, bounds) in Solid())
                if (point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z) return p;
            return null;
        }

        // The platform's own top height at this point, for callers that need a y rather than a bool
        // (spawn/respawn placement). Falls back to the query point's own y when nothing supports it.
        public static float GroundHeight(Vector3 point)
        {
            foreach (var (_, bounds) in Solid())
                if (point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z) return bounds.max.y;
            return point.y;
        }
    }
}
