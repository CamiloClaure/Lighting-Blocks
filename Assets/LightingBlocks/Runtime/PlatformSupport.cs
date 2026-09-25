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

        void OnEnable() { Surface = GetComponent<Collider>(); Active.Add(this); }
        void OnDisable() { Active.Remove(this); }

        // ponytail: an XZ bounds test, not a polygon test - every hub platform the plan describes is
        // "contiguous crystal tile platforms" on the existing two-metre grid, i.e. axis-aligned
        // rectangles, so bounds are exact. Move to a polygon test (mirroring Landscape.Inside) if a
        // hub ever needs a non-rectangular footprint.
        public static bool Supports(Vector3 point) => At(point) != null;

        // The platform under this XZ point, or null. Callers that need "which hub" (spawning,
        // respawn, hub ownership) read hubId off the result instead of a separate lookup.
        public static PlatformSupport At(Vector3 point)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var p = Active[i];
                if (p == null || p.Surface == null || !p.Surface.enabled || !p.gameObject.activeInHierarchy) continue;
                var bounds = p.Surface.bounds;
                if (point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z) return p;
            }
            return null;
        }

        // The platform's own top height at this point, for callers that need a y rather than a bool
        // (spawn/respawn placement). Falls back to the query point's own y when nothing supports it.
        public static float GroundHeight(Vector3 point)
        {
            var platform = At(point);
            return platform == null ? point.y : platform.Surface.bounds.max.y;
        }
    }
}
