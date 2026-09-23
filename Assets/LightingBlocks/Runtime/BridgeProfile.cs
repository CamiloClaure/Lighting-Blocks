using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightingBlocks
{
    // Per-scene numbers and feature switches. Selection is by active scene name, not a scene
    // reference, so a scene regeneration can never drop the wiring. SampleScene always resolves to
    // Default, whose values equal today's shipped constants, so its behaviour never changes.
    public sealed class BridgeProfile
    {
        public string name = "default";
        public int rows = 7;                 // tiles per crossing
        public float islandShift = 0;        // metres island 2 (and gap 1 with it) moves away from island 1
        public float expansion = 52;         // total metres island 3 moves; 52 is the shipped value
        public int linkBonusBlocks = int.MaxValue;   // connected blocks past the first that still pay +15%
        public string saveFile = "soul-bridge-v1.json";
        public string seedFromSaveFile = null;       // one-time import source; never written to
        public bool randomTreeStones = false, mixedBridge = false, treeReturn = false, occupancyLight = false;
        // Control scheme and handling. Default keeps the shipped strategy camera, mouse grab and
        // Strength-gated stones; the terrain profile is the direct-control revision.
        public bool playerCamera = false, spaceGrab = false, equalWeight = false, holdChannel = false, requiredTierGhost = false;

        public static readonly BridgeProfile Default = new BridgeProfile();
        // Both rifts are 50 tiles / 100 m here. Island 1's north edge is fixed at z=7, so 50 tiles
        // run 8..106 and island 2 has to start at 107: islandShift = 107 - 21 = 86. Island 2 then
        // ends at 172, gap 1 runs 173..271, and island 3 must start at 272, so its TOTAL offset is
        // expansion = 272 - 48 = 224 (not an addition on top of islandShift).
        public static readonly BridgeProfile Terrain = new BridgeProfile
        {
            name = "terrain", rows = 50, islandShift = 86, expansion = 224, linkBonusBlocks = 13,
            saveFile = "soul-bridge-terrain-v1.json", seedFromSaveFile = "soul-bridge-v1.json",
            randomTreeStones = true, mixedBridge = true, treeReturn = true, occupancyLight = true,
            playerCamera = true, spaceGrab = true, equalWeight = true, holdChannel = true, requiredTierGhost = true
        };

        public static BridgeProfile Current { get; private set; } = Default;

        // Idempotent and cheap: called from Boot, from GameManager.Awake and from ForestWorld.Awake
        // (their order is undefined), plus the terrain editor tools. Only the first call for a given
        // scene actually reloads the geometry.
        public static void Select(string sceneName)
        {
            // The revision applies everywhere. It started out gated to SampleSceneTerrain so the
            // shipping scene and its check suite stayed untouched, but that meant pressing Play in
            // SampleScene showed none of it. To put the isolation back, restore:
            //     var next = sceneName == "SampleSceneTerrain" ? Terrain : Default;
            var next = Terrain;
            if (next == Current) return;
            Current = next;
            Landscape.Reload();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => Select(SceneManager.GetActiveScene().name);
    }
}
