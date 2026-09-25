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
        // Explicit save schema/world id, embedded in every export and checked on import. Null here
        // (every legacy profile) means GameManager.ImportSave skips the check entirely, so existing
        // saves keep loading exactly as before. Only the Lighting profile sets this.
        public string saveSchema = null;
        public string seedFromSaveFile = null;       // one-time import source; never written to
        public bool randomTreeStones = false, mixedBridge = false, treeReturn = false, occupancyLight = false;
        // Control scheme and handling. Default keeps the shipped strategy camera, mouse grab and
        // Strength-gated stones; the terrain profile is the direct-control revision.
        public bool playerCamera = false, spaceGrab = false, equalWeight = false, holdChannel = false, requiredTierGhost = false;
        // ---- Lighting capability flags -----------------------------------------------------
        // How the rest of the code asks "is this Lighting, and what does that change". Everything
        // else (which save file, bridge geometry, control scheme) already has a field above;
        // "which upgrades exist" is answered by isLighting directly at the few purchase/UI call
        // sites (GameManager.Purchase/Cost/Level/Cap, SkillTree, GameHUD hotkeys) rather than a
        // second list here, since there are only ever two upgrade sets.
        public bool isLighting = false;      // true only for the Lighting profile below
        public bool skipScenery = false;     // TODO(A): ForestWorld.Build must skip tree/undergrowth/background
                                              // generation outright when set, not merely hide it afterwards
                                              // (EnvironmentVisibility.SetForestVisible stays legacy-only).
                                              // TODO(B): WorldDressing.Build must skip Reshape/BuildBackgroundIslands/
                                              // RimRocks/SpiritPaving/RimSteps/SpiritRuins/VoidShafts/Lanterns/
                                              // Atmosphere and build the void dressing instead, while still calling
                                              // StepLight per block so bridge glow keeps working.
        public bool platformGround = false;  // TODO(A): true means ground comes from PlatformSupport.Supports
                                              // (see PlatformSupport.cs) instead of the island polygons - wire this
                                              // into ForestNavigation.Walkable, ForestWorld.Occupied/Reserved/IslandAt
                                              // and any respawn/spawn footprint checks. GameManager.CanStand already
                                              // branches on this flag for player movement.
        public bool hasForestToggle = true;  // TODO(C): gate the Esc menu's Forest visibility row on this
        public bool starsDefaultOn = true;   // TODO(C): Lighting's own default for the Stars toggle, applied without
                                              // touching EnvironmentVisibility's shared legacy PlayerPrefs key/default

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
        // Lighting: same crossing geometry as Terrain (rows, islandShift, expansion, tier rules,
        // hub footprints and creature/fragment counts all come straight from Terrain's numbers, per
        // the plan's "preserve current bridge coordinates... three hub footprints"), but ground,
        // scenery and the save file are its own. A separate, fresh save: seedFromSaveFile stays
        // null so nothing auto-imports from either legacy save.
        public static readonly BridgeProfile Lighting = new BridgeProfile
        {
            name = "lighting", rows = Terrain.rows, islandShift = Terrain.islandShift, expansion = Terrain.expansion,
            linkBonusBlocks = Terrain.linkBonusBlocks,
            saveFile = "lighting-v1.json", saveSchema = "lighting-v1", seedFromSaveFile = null,
            randomTreeStones = true, mixedBridge = true, treeReturn = true, occupancyLight = true,
            playerCamera = true, spaceGrab = true, equalWeight = true, holdChannel = true, requiredTierGhost = true,
            isLighting = true, skipScenery = true, platformGround = true, hasForestToggle = false, starsDefaultOn = false
        };

        public static BridgeProfile Current { get; private set; } = Default;

        // Idempotent and cheap: called from Boot, from GameManager.Awake and from ForestWorld.Awake
        // (their order is undefined), plus the terrain editor tools. Only the first call for a given
        // scene actually reloads the geometry.
        public static void Select(string sceneName)
        {
            // The revision applies everywhere except the new Lighting scene. It started out gated to
            // SampleSceneTerrain so the shipping scene and its check suite stayed untouched, but that
            // meant pressing Play in SampleScene showed none of it. To put the isolation back, restore:
            //     var next = sceneName == "SampleSceneTerrain" ? Terrain : Default;
            var next = sceneName == "Lighting" ? Lighting : Terrain;
            if (next == Current) return;
            Current = next;
            Landscape.Reload();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => Select(SceneManager.GetActiveScene().name);
    }
}
