using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LightingBlocks.Editor
{
    // Coordinator evidence for the plan.md acceptance checks that still needed the real Lighting
    // scene in Play Mode after Agents A/B/C's own packets: Soul Gain income at levels 0/1/8 with a
    // disconnected block and hub platforms earning zero, save/reload round trip (souls, upgrades,
    // inventory, a pending fragment, placed and loose block state), diagonal movement along both
    // bridge edges at normal and maximum Speed without sticking or falling into the void, and animal
    // ground support over a simulated multi-minute run. Same poll+SessionState Play-mode pattern as
    // LightingWorldCheck/LightingSceneBuilder's capture routine (BridgeBlock/PullableBox only wire
    // their Rigidbody/Collider in Awake, which the editor never calls outside Play Mode).
    [InitializeOnLoad]
    public static class LightingIntegrationCheck
    {
        const string ScenePath = "Assets/Scenes/Lighting.unity";
        const string Result = "Library/LightingIntegrationCheck.txt";
        const string SceneKey = "LightingIntegrationCheck.Play";
        const string BatchKey = "LightingIntegrationCheck.Batch";
        const string RequestFile = "Library/LightingIntegrationCheck.request";
        static readonly StringBuilder report = new StringBuilder();
        static bool running; static int frames;

        static LightingIntegrationCheck()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(SceneKey, false)) { running = true; frames = 0; }
                if (s == PlayModeStateChange.EnteredEditMode) { running = false; SessionState.SetBool(SceneKey, false); }
            };
        }
        [MenuItem("Tools/Lighting Blocks/Run Lighting Integration Check")]
        public static void RequestCheck() => File.WriteAllText(RequestFile, "run");
        // -executeMethod entry point for batchmode.
        public static void BatchCheck() { SessionState.SetBool(BatchKey, true); RequestCheck(); }

        static void Poll()
        {
            if (!running)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(RequestFile)) return;
                File.Delete(RequestFile); report.Clear();
                EditorSceneManager.OpenScene(ScenePath);
                SessionState.SetBool(SceneKey, true); EditorApplication.isPlaying = true; return;
            }
            var game = GameManager.Instance;
            if (game != null) game.persistenceEnabled = false;
            if (++frames == 40)
            {
                try { RunChecks(game); report.AppendLine("SUCCESS: Lighting integration checks passed."); }
                catch (Exception e) { report.AppendLine("FAIL: " + e); }
                File.WriteAllText(Result, report.ToString());
                running = false; EditorApplication.isPlaying = false;
                if (SessionState.GetBool(BatchKey, false)) { SessionState.SetBool(BatchKey, false); EditorApplication.Exit(report.ToString().Contains("FAIL:") ? 1 : 0); }
            }
        }

        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); report.AppendLine("PASS: " + message); }
        static void Teleport(GameManager game, Vector3 p) { var c = game.player.GetComponent<CharacterController>(); c.enabled = false; game.player.transform.position = new Vector3(p.x, .025f, p.z); c.enabled = true; Physics.SyncTransforms(); }
        // Places a freshly-spawned block at (gap,column,row) via the real TryPlace path (mirrors
        // TwilightEvidence.cs's own proven full-bridge fixture), not a hand-set placed flag.
        static BridgeBlock PlaceAt(GameManager game, string islandId, int gap, int column, int row)
        {
            if (!game.TrySpawnAtWisdomTree(islandId, out var block, GameManager.RequiredTier(gap, column, row))) throw new Exception("Could not spawn a block for gap" + gap + "/col" + column + "/row" + row);
            block.transform.position = GameManager.Cell(gap, column, row) + Vector3.up * .54f;
            block.Box.Body.position = block.transform.position;
            Physics.SyncTransforms();
            if (!game.TryPlace(block)) throw new Exception("Could not place block at gap" + gap + "/col" + column + "/row" + row);
            return block;
        }

        static void RunChecks(GameManager game)
        {
            Check(BridgeProfile.Current.isLighting, "Lighting profile is active for Assets/Scenes/Lighting.unity");
            GameHUD.Current.BeginJourney(); // leaves the Title screen - GameplayBlocked otherwise no-ops every interaction below
            var forest = ForestWorld.For(game);

            CheckIncome(game, forest);
            CheckSaveRoundTrip(game, forest);
            CheckEdgeMovement(game);
            CheckAnimalSupport(game, forest);
        }

        // ---- income at Soul Gain 0/1/8; disconnected blocks and hub platforms earn nothing --------
        static void CheckIncome(GameManager game, ForestWorld forest)
        {
            var island = forest.configuration.islands[0];
            // FindPlacement requires every new cell be orthogonally adjacent to an already-placed
            // (and by then Connected, via the Recalculate() TryPlace runs internally) block, except
            // the root at column 0/row 0 - real gameplay rule, not a test-harness detail. Grow a
            // column-0 spine down four rows, then expand sideways from each spine cell outward one
            // step at a time, so every placement is always adjacent to one already on the board.
            int placed = 0;
            for (int row = 0; row < 4; row++) { PlaceAt(game, island.id, 0, 0, row); placed++; }
            for (int row = 0; row < 4; row++)
            {
                PlaceAt(game, island.id, 0, -1, row); placed++;
                PlaceAt(game, island.id, 0, -2, row); placed++;
                PlaceAt(game, island.id, 0, 1, row); placed++;
                PlaceAt(game, island.id, 0, 2, row); placed++;
            }

            var disconnected = null as BridgeBlock;
            Check(game.TrySpawnAtWisdomTree(island.id, out disconnected, 0), "Spawned a deliberately-disconnected block");
            // Off the placed run entirely and never placed, so it can never join Blocks' connected set.
            disconnected.transform.position = new Vector3(20, .18f, Landscape.BridgeStart(0) + 90);
            Physics.SyncTransforms();

            game.soulGainLevel = 0; game.Recalculate();
            Check(game.ConnectedCount == placed, "All " + placed + " placed blocks are connected, no more (" + game.ConnectedCount + ")");
            double base0 = game.PathRate;
            Check(base0 > 0, "The connected path earns a positive base rate at Soul Gain 0 (" + base0 + ")");

            game.soulGainLevel = 1; game.Recalculate();
            double rate1 = game.PathRate;
            Check(Math.Abs(rate1 - base0 * 1.25) < base0 * .001 + .0005, "Soul Gain 1 multiplies the same base rate by x1.25 (" + rate1 + " vs " + (base0 * 1.25) + ")");

            game.soulGainLevel = 8; game.Recalculate();
            double rate8 = game.PathRate;
            Check(Math.Abs(rate8 - base0 * 3.0) < base0 * .001 + .0005, "Soul Gain 8 multiplies the same base rate by x3.00 (" + rate8 + " vs " + (base0 * 3) + ")");

            Check(!disconnected.Connected && !disconnected.placed, "The disconnected block never joins the connected path and earns nothing");
            var platforms = UnityEngine.Object.FindObjectsByType<PlatformSupport>(FindObjectsSortMode.None);
            Check(platforms.Length > 0 && platforms.All(p => p.GetComponent<BridgeBlock>() == null),
                "Every hub platform (" + platforms.Length + ") carries no BridgeBlock component, so PathRate (which sums only Blocks) can never include one");

            game.soulGainLevel = 0; game.Recalculate(); // leave state clean for the checks below
        }

        // ---- save/reload round trip: souls, upgrades, inventory, a pending fragment, block state --
        static void CheckSaveRoundTrip(GameManager game, ForestWorld forest)
        {
            var island = forest.configuration.islands[0];
            game.souls = 1234.5; game.speed = 3; game.soulGainLevel = 5;
            forest.Inventory(island.id).inventory = 7;
            var slot = forest.Inventory(island.id).slots[2];
            slot.remaining = 0; slot.pending = true; // a fragment mid-respawn wait

            // Row 4/column 0 is adjacent to CheckIncome's row-3/column-0 spine cell (placement
            // requires orthogonal adjacency to an already-connected block - see PlaceAt/FindPlacement).
            // PlaceAt spawns via TrySpawnAtWisdomTree(..., RequiredTier(...)), so the tier this cell
            // actually gets is whatever that per-cell hash picks, not necessarily 0.
            int expectedTier = GameManager.RequiredTier(0, 0, 4);
            var placedBlock = PlaceAt(game, island.id, 0, 0, 4);
            Check(game.TrySpawnAtWisdomTree(island.id, out var looseBlock, 1), "Spawned a loose (unplaced) block for the save check");
            string looseId = looseBlock.id; Vector3 loosePos = looseBlock.transform.position;

            // Poll() above sets persistenceEnabled=false every frame (so the running check never
            // touches a real player save file); redirect to a throwaway path and flip it on just for
            // this round trip, same isolation ForestVerification.cs uses for its own save checks.
            game.savePathOverride = Path.Combine(Application.temporaryCachePath, "LightingIntegrationCheck-save.json");
            game.persistenceEnabled = true;
            double rateAtSave = game.Rate; long utcBefore = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Check(game.Save(), "Save() writes the current state");
            // Mutate everything away from the saved values before reloading - a match afterward
            // proves Load() actually restored from disk, not that the fields were untouched.
            game.souls = 0; game.speed = 0; game.soulGainLevel = 0;
            forest.Inventory(island.id).inventory = 0; slot.pending = false;
            placedBlock.tier = 2;

            bool loaded = game.Load();
            long utcAfter = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            game.persistenceEnabled = false;
            if (File.Exists(game.SavePath)) File.Delete(game.SavePath);
            if (File.Exists(game.SavePath + ".bak")) File.Delete(game.SavePath + ".bak");
            game.savePathOverride = null;
            Check(loaded, "Load() reports success reading the just-written save");
            // Load() always imports with offline:true, so real wall-clock seconds between Save() and
            // Load() legitimately earn offline income on top of the saved souls (see ImportSave) -
            // that is a real feature, not reload noise, so bound it instead of expecting exact
            // equality: souls must never be less than what was saved, and the gain on top can never
            // exceed rate * elapsed-seconds * .25 (ImportSave's own formula), with a small buffer for
            // clock-second rounding.
            double maxOfflineGain = rateAtSave * (utcAfter - utcBefore + 2) * .25;
            Check(game.souls >= 1234.5 - .01 && game.souls <= 1234.5 + maxOfflineGain + .5,
                "Souls survive save/reload, allowing for legitimate offline-income accrual between Save() and Load() (" + game.souls + ", expected 1234.5 plus up to " + maxOfflineGain + ")");
            Check(game.speed == 3 && game.soulGainLevel == 5, "Upgrades survive save/reload (speed=" + game.speed + " soulGain=" + game.soulGainLevel + ")");
            Check(forest.Inventory(island.id).inventory == 7, "Particle inventory count survives save/reload (" + forest.Inventory(island.id).inventory + ")");
            Check(forest.Inventory(island.id).slots[2].pending, "A pending (mid-respawn) fragment survives save/reload");
            var reloadedPlaced = game.Blocks.FirstOrDefault(b => b.placed && b.gap == 0 && b.column == 0 && b.row == 4);
            Check(reloadedPlaced != null && reloadedPlaced.tier == expectedTier, "Placed block state (gap/column/row/tier=" + expectedTier + ") survives save/reload");
            var reloadedLoose = game.Blocks.FirstOrDefault(b => b.id == looseId);
            Check(reloadedLoose != null && !reloadedLoose.placed && Vector3.Distance(reloadedLoose.transform.position, loosePos) < .1f, "Loose (unplaced) block position survives save/reload");
        }

        // ---- diagonal movement along both bridge edges, normal and maximum Speed -------------------
        static void CheckEdgeMovement(GameManager game)
        {
            // Face world +Z exactly: StepMovement reads the camera's forward projected onto XZ, and
            // at yaw 0 that projection is (0,0,1) regardless of pitch, so input.y/input.x map onto
            // world z/x directly instead of whatever direction the strategy camera last orbited to.
            game.cameraRig.yaw = 0; game.cameraRig.pitch = 42; game.cameraRig.SendMessage("LateUpdate");

            void RunEdge(int edgeColumn, string label)
            {
                float startZ = Landscape.BridgeStart(0) + 1; // inside CheckIncome's rows-0..3 grid
                Teleport(game, new Vector3(edgeColumn * 2, 0, startZ));
                Vector2 diagonal = new Vector2(edgeColumn > 0 ? 1 : -1, 1).normalized; // outward off the edge + forward, at once
                float startZPos = game.player.transform.position.z;
                for (int i = 0; i < 24; i++)
                {
                    game.player.StepMovement(diagonal, 1f / 30);
                    Physics.SyncTransforms();
                    Check(game.CanStand(game.player.transform.position), label + " stays supported mid-run (step " + i + ", pos=" + game.player.transform.position + ")");
                    Check(game.player.transform.position.y > -1, label + " does not fall into the void (step " + i + ", y=" + game.player.transform.position.y + ")");
                }
                Check(game.player.transform.position.z > startZPos + .05f, label + " still advances forward while running the edge (not stuck)");
            }
            game.speed = 0; game.ApplyUpgrades();
            RunEdge(-2, "Left edge at normal speed"); RunEdge(2, "Right edge at normal speed");
            game.speed = 6; game.ApplyUpgrades(); // GameManager.Cap("speed") == 6, the maximum level
            RunEdge(-2, "Left edge at maximum speed"); RunEdge(2, "Right edge at maximum speed");
            game.speed = 0; game.ApplyUpgrades();
        }

        // ---- animals stay supported over a simulated multi-minute run -----------------------------
        static void CheckAnimalSupport(GameManager game, ForestWorld forest)
        {
            var grounded = UnityEngine.Object.FindObjectsByType<SpectralAnimal>(FindObjectsSortMode.None);
            var flyers = UnityEngine.Object.FindObjectsByType<ForestCreature>(FindObjectsSortMode.None);
            Check(grounded.Length + flyers.Length > 0, "At least one animal exists to check (" + grounded.Length + " grazing, " + flyers.Length + " forest creature)");
            var islands = forest.configuration.islands;
            const float step = .25f;
            int ticks = Mathf.RoundToInt(240f / step); // 4 simulated minutes, fast-forwarded - no real wall-clock wait
            for (int t = 0; t < ticks; t++)
            {
                // Both SpectralAnimal and ForestCreature only simulate real movement within 60 m of
                // the player (see their own Simulate()); cycle near each island in turn so every
                // island's wildlife gets an awake window over the run, not just whichever is closest
                // to wherever CheckEdgeMovement left the player.
                if (t % 80 == 0) Teleport(game, islands[(t / 80) % islands.Length].wisdomPosition);
                foreach (var c in grounded) c.Simulate(step);
                foreach (var f in flyers) f.Simulate(step);
            }
            foreach (var c in grounded)
                Check(PlatformSupport.Supports(c.transform.position), "Grounded animal " + c.name + " stays supported after a simulated multi-minute run (pos=" + c.transform.position + ")");
            foreach (var f in flyers)
                // Mid-flight (Airborne) is fine - the landing point itself is what has to be
                // supported, and ForestCreature.ClearGround already gates that via nav.Walkable.
                Check(f.Airborne || PlatformSupport.Supports(f.transform.position), "Forest creature " + f.name + " is airborne or supported after a simulated multi-minute run (pos=" + f.transform.position + ", airborne=" + f.Airborne + ")");
        }
    }
}
