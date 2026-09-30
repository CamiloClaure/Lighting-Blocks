using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LightingBlocks.Editor
{
    // Agent A evidence for the Lighting migration's world-gameplay packet: skip-scenery gating,
    // platform-only fragment/animal/output placement, and support-collider occupancy filtering.
    // Touches: Assets/LightingBlocks/Runtime/ForestWorld.cs, ForestNavigation.cs.
    //
    // EditCheck runs right now, entirely in edit mode - no Play Mode, no Lighting scene required.
    // It opens a throwaway empty scene (never saved), loads the real ForestConfiguration asset, and
    // exercises ForestLayoutGenerator.Generate / ForestNavigation.Walkable / ForestWorld.FindOutput,
    // Occupied and IslandAt directly against a synthetic PlatformSupport rig. This is the check to
    // run today; see the class comment on PlayCheck below for what it cannot prove yet.
    [InitializeOnLoad]
    public static class LightingWorldCheck
    {
        const string EditResult = "Library/LightingWorldCheck.txt";
        static readonly StringBuilder report = new StringBuilder();
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL: " + message);
            report.AppendLine("PASS: " + message);
        }

        [MenuItem("Tools/Lighting Blocks/Run Lighting World Edit Check")]
        public static void EditCheck()
        {
            report.Clear();
            bool ok = true;
            try { RunEditChecks(); report.AppendLine("SUCCESS: Lighting world-gameplay edit-mode checks passed."); }
            catch (Exception e) { ok = false; report.AppendLine(e.Message); Debug.LogError(e); }
            finally { BridgeProfile.Select("SampleScene"); }
            File.WriteAllText(EditResult, report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static void RunEditChecks()
        {
            // A throwaway scene, never saved: guarantees no pre-existing colliders (real terrain,
            // starter blocks, trees) can leak into the Physics.OverlapBox queries below.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var guids = AssetDatabase.FindAssets("t:ForestConfiguration");
            if (guids.Length == 0) throw new Exception("No ForestConfiguration asset found in the project - cannot run the check.");
            var config = AssetDatabase.LoadAssetAtPath<ForestConfiguration>(AssetDatabase.GUIDToAssetPath(guids[0]));
            var island = config.islands[0];
            var polygon = Landscape.Shores[0]; // island-1's footprint never shifts under islandShift/expansion

            var host = new GameObject("LightingWorldCheck rig");
            var gm = host.AddComponent<GameManager>();
            var forest = host.AddComponent<ForestWorld>();
            forest.configuration = config;
            // ForestWorld.game is private and only ever set from Awake/EnsureState, neither of which
            // Unity calls outside Play Mode. FindOutput only reads game.Blocks (an empty list is
            // fine here), so wiring the field directly is cheaper and safer than faking a whole
            // EnsureState() pass across all three islands under a profile mismatch.
            typeof(ForestWorld).GetField("game", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(forest, gm);
            // PlatformSupport.OnEnable (which registers it into the static Active list Supports/At
            // read) is a MonoBehaviour lifecycle callback: Unity only invokes it in Play Mode or for
            // ExecuteAlways scripts. Neither applies here, so every synthetic platform below is
            // registered by calling it directly.
            var enablePlatformSupport = typeof(PlatformSupport).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            void Register(PlatformSupport ps) => enablePlatformSupport.Invoke(ps, null);

            BridgeProfile.Select("Lighting");
            Check(BridgeProfile.Current.platformGround && BridgeProfile.Current.skipScenery, "Lighting profile selected (platformGround + skipScenery on)");

            // A platform covering island-1's real footprint - the same bounding rectangle a hub's
            // contiguous crystal tiles would cover on the existing two-metre grid.
            Vector2 min = polygon[0], max = polygon[0];
            foreach (var v in polygon) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            float gridMinX=Mathf.FloorToInt((min.x-1f)/2f)*2f+1f,gridMaxX=Mathf.CeilToInt((max.x-1f)/2f)*2f+1f;
            float gridMinZ=Mathf.FloorToInt((min.y-1f)/2f)*2f+1f,gridMaxZ=Mathf.CeilToInt((max.y-1f)/2f)*2f+1f;
            var platformGO = new GameObject("Check platform");
            platformGO.transform.position = new Vector3((gridMinX+gridMaxX)*.5f, 0, (gridMinZ+gridMaxZ)*.5f);
            var platformBox = platformGO.AddComponent<BoxCollider>();
            platformBox.size = new Vector3(gridMaxX-gridMinX, 1, gridMaxZ-gridMinZ);
            var support = platformGO.AddComponent<PlatformSupport>();
            support.hubId = island.id;
            Register(support);
            Physics.SyncTransforms();

            // Fragment / animal / respawn-candidate placement lands only on supported platform ground.
            var layout = ForestLayoutGenerator.Generate(island, 1907, polygon, config.trees.Length);
            Check(layout.particles.Count == island.particleCount && layout.particles.All(PlatformSupport.Supports),
                "Every generated fragment position (" + layout.particles.Count + ") sits on supported platform ground");
            int wildlifeTotal = island.animalCount + island.rabbitCount + island.birdCount;
            Check(layout.animals.Count == wildlifeTotal && layout.animals.All(PlatformSupport.Supports),
                "Every generated animal/bird spawn point (" + layout.animals.Count + ") sits on supported platform ground");
            Check(layout.candidates.Count > 0 && layout.candidates.All(PlatformSupport.Supports),
                "Every respawn candidate (" + layout.candidates.Count + ") sits on supported platform ground");

            // The gate is live, not coincidentally identical to the old polygon test: a point with no
            // covering platform must go unwalkable under Lighting even though it sits inside the
            // legacy island polygon, and the SAME point must stay walkable under the legacy profile.
            var farPoint = island.wisdomPosition + new Vector3(0, 0, 6);
            Check(ForestLayoutGenerator.Inside(farPoint, polygon, .8f), "Fixture point sits inside the island-1 polygon (sanity check on the fixture itself)");
            var stubGO = new GameObject("Check platform stub");
            stubGO.transform.position = island.wisdomPosition + new Vector3(1000, 0, 1000); // nowhere near farPoint
            stubGO.AddComponent<BoxCollider>().size = new Vector3(4, 1, 4);
            var stubSupport = stubGO.AddComponent<PlatformSupport>(); stubSupport.hubId = island.id; Register(stubSupport);
            platformGO.SetActive(false); // the main platform covers farPoint too - take it out of the picture for this sub-test
            Physics.SyncTransforms();
            Check(!PlatformSupport.Supports(farPoint) && !layout.navigation.Walkable(farPoint),
                "Lighting: a point with no covering platform is unwalkable even though it sits inside the legacy island polygon");
            BridgeProfile.Select("SampleScene"); // -> Terrain: platformGround false, same as every other legacy scene
            Check(layout.navigation.Walkable(farPoint),
                "Legacy (Terrain) profile: the same point stays walkable via the untouched polygon test - Lighting's gate does not leak into legacy scenes");
            BridgeProfile.Select("Lighting");
            platformGO.SetActive(true);
            UnityEngine.Object.DestroyImmediate(stubGO);
            Physics.SyncTransforms();

            // Wisdom Tree output: succeeds on supported ground, ignores the PlatformSupport collider
            // itself as an obstruction, but still refuses a genuinely blocked slot (this is exactly
            // the guard WisdomTree.BeginCharge checks before it ever touches the particle inventory -
            // see WisdomTree.cs:44 - so proving FindOutput refuses here proves a blocked output slot
            // cannot consume fragments).
            Check(forest.FindOutput(island, out var outputPoint), "An unobstructed Wisdom Tree output slot is found on supported platform ground");
            foreach (float x in new[] { -.45f, .45f }) foreach (float z in new[] { -.45f, .45f })
                Check(PlatformSupport.Supports(outputPoint + new Vector3(x, 0, z)), "Output slot corner (" + x + "," + z + ") sits on supported platform ground");
            Check(!forest.Occupied(outputPoint, new Vector3(.46f, .24f, .46f)), "Occupied ignores the PlatformSupport collider directly beneath the output slot");

            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.position = outputPoint; obstacle.transform.localScale = Vector3.one * .5f;
            Physics.SyncTransforms();
            Check(forest.Occupied(outputPoint, new Vector3(.46f, .24f, .46f)), "Occupied still detects a genuine loose obstacle at the same point");
            UnityEngine.Object.DestroyImmediate(obstacle);
            Physics.SyncTransforms();
            Check(forest.FindOutput(island, out _), "Output slot is available again once the obstacle is gone");

            var blockers = new List<GameObject>();
            foreach (var offset in island.outputOffsets)
            {
                var o = GameObject.CreatePrimitive(PrimitiveType.Cube);
                o.transform.position = island.wisdomPosition + offset; o.transform.localScale = Vector3.one * .6f;
                blockers.Add(o);
            }
            Physics.SyncTransforms();
            Check(!forest.FindOutput(island, out _), "Every output slot blocked: FindOutput refuses, so a blocked output slot never reaches the particle-spend in WisdomTree.Tick");
            foreach (var o in blockers) UnityEngine.Object.DestroyImmediate(o);
            Physics.SyncTransforms();

            // Hub ownership follows PlatformSupport.hubId under Lighting.
            Check(forest.IslandAt(island.wisdomPosition)?.id == island.id, "IslandAt resolves hub ownership via PlatformSupport.hubId under Lighting");
            Check(forest.IslandAt(island.wisdomPosition + new Vector3(0, 0, 500)) == null, "IslandAt returns no hub for a point off every platform");

            // The current Lighting world uses these generated legacy ecology layouts only for
            // vegetation/wildlife. Runtime pickups come from ForestWorld.NewState on the void grid.
            var extraPlatforms = new List<GameObject>();
            for (int i = 1; i < config.islands.Length; i++)
            {
                var otherIsland = config.islands[i];var otherPolygon = Landscape.Shores[otherIsland.polygonIndex];
                Vector2 oMin = otherPolygon[0], oMax = otherPolygon[0];
                foreach (var v in otherPolygon) { oMin = Vector2.Min(oMin, v); oMax = Vector2.Max(oMax, v); }
                float ominx=Mathf.FloorToInt((oMin.x-1f)/2f)*2f+1f,omaxx=Mathf.CeilToInt((oMax.x-1f)/2f)*2f+1f;
                float ominz=Mathf.FloorToInt((oMin.y-1f)/2f)*2f+1f,omaxz=Mathf.CeilToInt((oMax.y-1f)/2f)*2f+1f;
                var otherGO = new GameObject("Check platform " + otherIsland.id);
                otherGO.transform.position = new Vector3((ominx+omaxx)*.5f, 0, (ominz+omaxz)*.5f);
                var otherBox = otherGO.AddComponent<BoxCollider>();otherBox.size = new Vector3(omaxx-ominx, 1, omaxz-ominz);
                var otherSupport = otherGO.AddComponent<PlatformSupport>();otherSupport.hubId = otherIsland.id;Register(otherSupport);
                extraPlatforms.Add(otherGO);Physics.SyncTransforms();
            }
            var state=forest.NewState(1907);
            var allSlots=state.islands.SelectMany(i=>i.slots).ToArray();
            Check(allSlots.Length==config.islands.Sum(i=>i.ParticleCount)&&allSlots.Length==120,"All three hubs hold 40 persistent particle slots each (120)");
            // Slots neither ring (2-4, then 5-6 tiles) can fit wait as pending for the respawn loop.
            var placedSlots=allSlots.Where(s=>!s.pending).ToArray();
            Check(placedSlots.Length>72,"The rings place more than the old 72 particles at once ("+placedSlots.Length+")");
            Check(allSlots.All(s=>Mathf.Abs(s.position.x/LightingBridgeGrid.CellSize-Mathf.Round(s.position.x/LightingBridgeGrid.CellSize))<.001f&&Mathf.Abs(s.position.z/LightingBridgeGrid.CellSize-Mathf.Round(s.position.z/LightingBridgeGrid.CellSize))<.001f),"Every particle sits at a future two-metre grid-cell center");
            Check(allSlots.All(s=>!LightingBridgeGrid.OverlapsPlatform(LightingBridgeGrid.CellOf(s.position))),"Initial particles are placed in the void beyond the hub platforms");
            bool fourMetreSpacing=true;for(int a=0;a<placedSlots.Length;a++)for(int b=a+1;b<placedSlots.Length;b++)if((placedSlots[a].position-placedSlots[b].position).sqrMagnitude<16f)fourMetreSpacing=false;
            Check(fourMetreSpacing,"All "+placedSlots.Length+" placed initial particles are globally spaced at least four metres apart");
            var tileDistances=new List<int>();var edgeSectorCounts=new List<int>();
            foreach(var owner in state.islands)
            {
                var platforms=PlatformSupport.ActivePlatforms.Where(p=>p!=null&&p.hubId==owner.id).ToArray();var definition=config.Find(owner.id);var poly=Landscape.Shores[definition.polygonIndex];Vector2 low=poly[0],high=low;
                foreach(var point in poly){low=Vector2.Min(low,point);high=Vector2.Max(high,point);}
                var hubBounds=new Bounds(new Vector3((low.x+high.x)*.5f,0,(low.y+high.y)*.5f),new Vector3(high.x-low.x,1,high.y-low.y));
                foreach(var platform in platforms)hubBounds.Encapsulate(platform.Surface.bounds);
                var center=hubBounds.center;var sectors=new HashSet<int>();int x0=Mathf.FloorToInt((hubBounds.min.x-12)/LightingBridgeGrid.CellSize),x1=Mathf.CeilToInt((hubBounds.max.x+12)/LightingBridgeGrid.CellSize);
                int z0=Mathf.FloorToInt((hubBounds.min.z-12)/LightingBridgeGrid.CellSize),z1=Mathf.CeilToInt((hubBounds.max.z+12)/LightingBridgeGrid.CellSize);
                var distances=new Dictionary<Vector2Int,int>();var queue=new Queue<Vector2Int>();
                for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)
                {
                    var cell=new Vector2Int(x,z);if(LightingBridgeGrid.OverlapsPlatform(cell)||!LightingBridgeGrid.TouchesHub(cell,owner.id))continue;
                    distances[cell]=1;queue.Enqueue(cell);
                }
                while(queue.Count>0)
                {
                    var cell=queue.Dequeue();int distance=distances[cell];if(distance>=6)continue;
                    foreach(var next in LightingBridgeGrid.NeighborsOf(cell))if(next.x>=x0&&next.x<=x1&&next.y>=z0&&next.y<=z1&&!distances.ContainsKey(next)&&!LightingBridgeGrid.OverlapsPlatform(next)){distances[next]=distance+1;queue.Enqueue(next);}
                }
                foreach(var slot in owner.slots)
                {
                    if(slot.pending)continue;
                    if(distances.TryGetValue(LightingBridgeGrid.CellOf(slot.position),out int distance))tileDistances.Add(distance);
                    var radial=slot.position-center;sectors.Add(Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(radial.z,radial.x)+Mathf.PI,Mathf.PI*2)/(Mathf.PI*2)*8),0,7));
                }
                edgeSectorCounts.Add(sectors.Count);
            }
            Check(tileDistances.Count==placedSlots.Length&&tileDistances.All(d=>d>=2&&d<=6)&&tileDistances.Count(d=>d<=4)>=72,"Every initial particle is two to six orthogonal future tiles from its hub, at least 72 of them two to four");
            Check(edgeSectorCounts.All(n=>n>=6),"Initial particle distribution reaches at least six of eight sectors around every hub edge");
            var first=state.islands[0].slots[0];
            Check(Vector3.Distance(first.position,new Vector3(0,0,3))<=12f,"A deterministic starting particle is visible within twelve metres of the authored spawn");
            var selectorHost=new GameObject("Selector check host");var particles=selectorHost.AddComponent<LightingParticles>();
            var testTreeObject=new GameObject("Selector check Wisdom Tree");testTreeObject.transform.position=island.wisdomPosition;
            var testTree=testTreeObject.AddComponent<WisdomTree>();testTree.Initialize(forest,island);
            var selectorParent=new GameObject("Selector check parent").transform;
            particles.AddTree(forest,testTree,selectorParent,layout);
            Check(particles.Receivers.Count==3,"Each Wisdom Tree owns exactly three free color selectors");
            for(int color=0;color<3;color++)
            {
                var selector=particles.Receivers[color];
                Check(selector.ColorIndex==color&&Vector3.Distance(selector.transform.position,testTree.transform.position)<=3f,"Selector "+color+" matches its tile tier and sits beside its Wisdom Tree");
                Check(PlatformSupport.Supports(selector.transform.position),"Selector "+color+" sits on supported hub ground");
            }
            UnityEngine.Object.DestroyImmediate(selectorParent.gameObject);UnityEngine.Object.DestroyImmediate(testTreeObject);UnityEngine.Object.DestroyImmediate(selectorHost);
            foreach (var go in extraPlatforms) UnityEngine.Object.DestroyImmediate(go);
            Physics.SyncTransforms();
        }

        // ---------------------------------------------------------------------------------------
        // PlayCheck needs the actual Lighting scene (Agent B builds it): BridgeBlock/PullableBox only
        // wire their Rigidbody/BoxCollider references in Awake, which the editor does not call outside
        // Play Mode, so the literal "charge a Wisdom Tree -> Instantiate a BridgeBlock" step cannot be
        // proven without entering Play Mode in that scene. This mirrors ForestVerification.cs's own
        // Play-mode polling harness (collect three -> charge -> block created; blocked output consumes
        // no particles), trimmed to Agent A's packet and scoped to Assets/Scenes/Lighting.unity. It
        // self-skips with exit code 0 until that scene exists; re-run it once Agent B's scene lands.
        // ---------------------------------------------------------------------------------------
        const string PlayResult = "Library/LightingWorldPlayCheck.txt";
        const string SceneKey = "LightingWorldCheck.Play";
        const string BatchKey = "LightingWorldCheck.Batch";
        const string RequestFile = "Library/LightingWorldCheck.play-request";
        static readonly StringBuilder playReport = new StringBuilder();
        static bool playRunning; static int playFrames;

        static LightingWorldCheck()
        {
            EditorApplication.update += PollPlay;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(SceneKey, false)) { playRunning = true; playFrames = 0; }
                if (s == PlayModeStateChange.EnteredEditMode) { playRunning = false; SessionState.SetBool(SceneKey, false); }
            };
        }
        [MenuItem("Tools/Lighting Blocks/Run Lighting World Play Check")]
        public static void RequestPlayCheck()
        {
            if (!File.Exists("Assets/Scenes/Lighting.unity"))
            {
                playReport.Clear();
                playReport.AppendLine("SKIP: Assets/Scenes/Lighting.unity does not exist yet (Agent B builds it). Re-run Tools > Lighting Blocks > Run Lighting World Play Check once it does.");
                File.WriteAllText(PlayResult, playReport.ToString());
                Debug.Log(playReport.ToString());
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }
            File.WriteAllText(RequestFile, "run");
        }
        // -executeMethod entry point for batchmode.
        public static void BatchPlayCheck() { SessionState.SetBool(BatchKey, true); RequestPlayCheck(); }

        static void PollPlay()
        {
            if (!playRunning)
            {
                // Library/LightingWorldCheck.edit-request runs the edit check from an open editor, then reopens Lighting.
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists("Library/LightingWorldCheck.edit-request"))
                {
                    AssetDatabase.Refresh(); if (EditorApplication.isCompiling) return;
                    File.Delete("Library/LightingWorldCheck.edit-request"); EditCheck(); EditorSceneManager.OpenScene("Assets/Scenes/Lighting.unity"); return;
                }
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(RequestFile)) return;
                AssetDatabase.Refresh(); if (EditorApplication.isCompiling) return; // unfocused editors never auto-refresh
                File.Delete(RequestFile); playReport.Clear();
                EditorSceneManager.OpenScene("Assets/Scenes/Lighting.unity");
                SessionState.SetBool(SceneKey, true); EditorApplication.isPlaying = true; return;
            }
            var game = GameManager.Instance;
            if (game != null) { game.persistenceEnabled = false; game.autoTick = false; }
            if (++playFrames == 40)
            {
                try { RunPlayChecks(game); playReport.AppendLine("SUCCESS: Lighting world-gameplay play-mode checks passed."); }
                catch (Exception e) { playReport.AppendLine("FAIL: " + e); }
                File.WriteAllText(PlayResult, playReport.ToString());
                playRunning = false; EditorApplication.isPlaying = false;
                if (SessionState.GetBool(BatchKey, false)) { SessionState.SetBool(BatchKey, false); EditorApplication.Exit(playReport.ToString().Contains("FAIL:") ? 1 : 0); }
            }
        }
        static void Teleport(GameManager game, Vector3 p) { var c = game.player.GetComponent<CharacterController>(); c.enabled = false; game.player.transform.position = new Vector3(p.x, .025f, p.z); c.enabled = true; Physics.SyncTransforms(); }
        static void RunPlayChecks(GameManager game)
        {
            void Check(bool condition, string message) { if (!condition) throw new Exception(message); playReport.AppendLine("PASS: " + message); }
            Check(BridgeProfile.Current.isLighting, "Lighting profile is active for Assets/Scenes/Lighting.unity");
            // GameHUD boots on the Title screen (View defaults to ScreenView.Title in Awake), and
            // GameHUD.GameplayBlocked is true for every View except Playing - so TryCollect/Interact/
            // Purchase all silently no-op until this runs, regardless of player position. Missing
            // here (unlike ForestVerification.cs's own proven Play-mode harness, which calls this
            // first thing) is exactly why the collection loop below used to fail on every fragment.
            GameHUD.Current.BeginJourney();
            var forest=ForestWorld.For(game);var island=forest.configuration.islands[0];var tree=forest.WisdomTrees[0];
            var owner=forest.Inventory(island.id);owner.freeDeliveryColor=-1;owner.freeDeliveryColors.Clear();owner.charge=new[]{0,0,0};owner.rewards.Clear();
            forest.State.inventory=13;int walletBefore=forest.SharedInventory;int blocksBefore=game.Blocks.Count;
            Teleport(game,tree.transform.position+Vector3.right*8);
            Check(!tree.RequestTile(2)&&owner.freeDeliveryColor==-1,"A direct remote tree request is refused");
            Teleport(game,tree.transform.position+Vector3.right*2);
            Check(tree.RequestTile(2)&&owner.freeDeliveryColor==2,"Supported ground within three metres can request one free matching tile");
            Check(!tree.RequestTile(1)&&owner.freeDeliveryColor==2,"Each tree accepts only one user request at a time");
            Check(forest.SharedInventory==walletBefore,"Requesting a tile spends no particles");
            var obstacles=new List<GameObject>();
            foreach(var offset in island.outputOffsets){var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.transform.position=island.wisdomPosition+offset;obstacles.Add(o);}
            Physics.SyncTransforms();tree.Tick(.61f);
            Check(game.Blocks.Count==blocksBefore&&owner.freeDeliveryColor==2,"A blocked output keeps the selected free tile pending");
            foreach(var o in obstacles)ForestWorld.Remove(o);Physics.SyncTransforms();
            tree.Tick(.61f);
            Check(game.Blocks.Count==blocksBefore+1&&game.Blocks.Last().tier==2,"A clear output releases exactly the selected free tile after its delay");
            Check(owner.freeDeliveryColor==-1&&owner.charge.All(c=>c==0)&&owner.rewards.Count==0,"Successful free delivery clears only its request and creates no particle charge");
            Check(forest.SharedInventory==walletBefore,"Free tile creation leaves the particle wallet unchanged");
        }
    }
}
