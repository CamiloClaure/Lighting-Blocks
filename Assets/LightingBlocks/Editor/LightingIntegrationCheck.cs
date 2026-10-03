using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LightingBlocks.Editor
{
    // Coordinator evidence for Lighting's open grid and particle economy: no passive income, stable
    // two-metre cell colors on negative coordinates, quarter-particle save/reload, supported hub
    // movement, and animal ground support over a simulated multi-minute run. Same poll+SessionState Play-mode pattern as
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
                // An unfocused editor never auto-refreshes: pick up outside edits (and recompile) first.
                AssetDatabase.Refresh(); if (EditorApplication.isCompiling) return;
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
        // Streams the tile chunks around the new spot at once (stream false leaves that to the caller).
        static void Teleport(GameManager game, Vector3 p, bool stream = true) { var c = game.player.GetComponent<CharacterController>(); c.enabled = false; game.player.transform.position = new Vector3(p.x, .025f, p.z); c.enabled = true; if (stream) game.Tiles.Stream(game.player.transform.position, true); Physics.SyncTransforms(); }
        // The placed network, loaded or not: a snapshot, and every cell with the colour it was laid in.
        static System.Collections.Generic.HashSet<Vector2Int> Cells(GameManager game) => new System.Collections.Generic.HashSet<Vector2Int>(game.Tiles.Cells);
        static (Vector2Int cell, int tier)[] PlacedCells(GameManager game) => game.Tiles.Cells.Select(c => (cell: c, tier: GameManager.RequiredTier(0, c.x, c.y))).OrderBy(t => t.cell.x).ThenBy(t => t.cell.y).ToArray();
        // DrawFar until every chunk in reach has its poses (a cold cast spreads them over frames); returns the frames.
        static int DrawFarAll(GameManager game, Vector3 at, float reach) { int frames = 0; do { game.Tiles.DrawFar(at, reach); frames++; } while (game.Tiles.FarPending && frames < 100); return frames; }
        // Takes back every tile laid since the snapshot.
        static void RestoreTiles(GameManager game, System.Collections.Generic.HashSet<Vector2Int> before)
        {
            foreach (var c in game.Tiles.Cells.Where(c => !before.Contains(c)).ToArray()) game.Tiles.Remove(c);
            game.Recalculate(); Physics.SyncTransforms();
        }
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

            CheckLightingGridAndEconomy(game, forest);
            CheckTileStockAndColours(game, forest);
            CheckBranchingGrid(game,forest);
            CheckSaveRoundTrip(game, forest);
            CheckSkillTreeClick(game, forest);
            CheckDash(game, forest);
            CheckGrenade(game, forest);
            CheckDashAfterGrenade(game, forest);
            CheckPull(game, forest);
            CheckSightAndReveal(game, forest);
            CheckAbilityHud(game);
            CheckSounds(game);
            CheckGrounding(game, forest);
            CheckDashPerformance(game, forest);
            CheckUnboundedWorld(game, forest);
            CheckDotSaveMigration(game, forest);
            CheckLegacyLightingMigration(game, forest);
            CheckEdgeMovement(game);
            CheckAnimalSupport(game, forest);
        }

        // ---- branching grid coordinates/colors and particle-only economy -------------------------
        static void CheckLightingGridAndEconomy(GameManager game, ForestWorld forest)
        {
            Vector2Int negative = new Vector2Int(-47,-86);
            Check(GameManager.RequiredTier(0,negative.x,negative.y)==GameManager.RequiredTier(0,negative.x,negative.y),"Cell color is deterministic at negative coordinates");
            GameManager.CellOf(LightingBridgeGrid.Cell(negative.x,negative.y),out int gap,out int x,out int z);
            Check(gap==0&&x==negative.x&&z==negative.y,"World positions resolve to the unified grid for negative cells");
            Check(game.Rate==0&&game.GeneratorRate==0,"Lighting has no path, generator, or passive income");
            Check(Math.Abs(game.Cost("speed")-10)<.001&&Math.Abs(game.Cost("soulgain")-15)<.001,"Lighting upgrade costs use 10 particles for Speed and 15 for Particle Yield at level zero");
            var platforms=UnityEngine.Object.FindObjectsByType<PlatformSupport>(FindObjectsSortMode.None);
            Check(platforms.Select(p=>p.hubId).Distinct().Count()==3&&platforms.All(p=>p.GetComponent<BridgeBlock>()==null),"The three hubs use platform support separate from placed grid tiles");
        }

        // ---- island tile stock and the five Lighting colours ------------------------------------
        static void CheckTileStockAndColours(GameManager game, ForestWorld forest)
        {
            Check(BridgeBlock.TierCount == 5 && game.blockPrefabs.Length == 5 && Enumerable.Range(0, 5).All(t => game.blockPrefabs[t] != null && game.blockPrefabs[t].tier == t)
                && BridgeBlock.TierColour(3) == "green" && BridgeBlock.TierColour(4) == "red", "Lighting tiles come in five colours, one prefab each: blue, violet, amber, green and red");
            var asked = new System.Collections.Generic.HashSet<int>();
            for (int x = 0; x < 20; x++) for (int z = 0; z < 20; z++) asked.Add(LightingBridgeGrid.RequiredTier(x, z));
            Check(asked.SetEquals(Enumerable.Range(0, 5)), "Cells ask for all five colours");

            var stock = game.GetComponent<LightingParticles>(); var trees = forest.WisdomTrees;
            stock.Restock(0, true);
            bool Full() => trees.Count == 3 && trees.All(t => Enumerable.Range(0, 5).All(tier => stock.Stock(t.Definition.id, tier) == LightingParticles.StockPerColour));
            Check(Full(), "Every island holds " + LightingParticles.StockPerColour + " loose tiles of each colour");
            var loose = game.Blocks.Where(b => !b.placed && b.id.StartsWith("stock-")).ToArray();
            Check(loose.Length == 45 && loose.All(b =>
            {
                var home = trees.Single(t => b.id.StartsWith(LightingParticles.StockPrefix(t.Definition.id, b.tier)));
                var d = b.transform.position - home.transform.position;
                return forest.IslandAt(b.transform.position)?.id == home.Definition.id && new Vector2(d.x, d.z).magnitude >= 4;
            }), "All 45 stock tiles lie on their own island, clear of its Wisdom Tree");
            var art = loose[0].GetComponent<BlockArt>(); var body = art.bodies.First(r => r != null && r.transform.IsChildOf(art.looseVisual));
            var shade = art.looseVisual.GetComponentInChildren<GroundShadow>();
            Check(body.bounds.min.y >= 0 && body.bounds.min.y <= .02f && shade != null && shade.Shown && Mathf.Abs(shade.transform.position.y - GroundShadow.FloorY) < .001f,
                "A loose tile rests on the floor (bottom y " + body.bounds.min.y.ToString("F3") + ") over a soft ground shadow");
            string island = trees[0].Definition.id; var taken = loose.First(b => b.id.StartsWith(LightingParticles.StockPrefix(island, 4)));
            game.Blocks.Remove(taken); ForestWorld.Remove(taken.gameObject); Physics.SyncTransforms();
            stock.Restock(LightingParticles.RestockSeconds - 1);
            Check(stock.Stock(island, 4) == LightingParticles.StockPerColour - 1, "A taken stock tile is not replaced early");
            stock.Restock(2);
            var replaced = game.Blocks.LastOrDefault(b => !b.placed && b.id.StartsWith(LightingParticles.StockPrefix(island, 4)));
            Check(Full() && replaced != null && replaced.tier == 4 && forest.IslandAt(replaced.transform.position)?.id == island,
                "A red tile taken from " + island + " is replaced there, in red, after " + LightingParticles.RestockSeconds + " s");
            string saved = game.ExportSave();
            Check(game.ImportSave(saved) && Full(), "A save keeps the island stock where it lies");

            var before = Cells(game);
            var edges = HubEdges("island-1").Where(c => !game.Tiles.Contains(c) && LightingBridgeGrid.IsFrontier(game, c) && !LightingBridgeGrid.HasObstruction(c, game)).ToArray();
            var red = edges.First(c => LightingBridgeGrid.RequiredTier(c.x, c.y) == 4); var other = edges.First(c => LightingBridgeGrid.RequiredTier(c.x, c.y) != 4);
            SpawnGridTile(game, other, 4, false, "THIS TILE ASKS FOR");
            var placed = SpawnGridTile(game, red, 4);
            Check(placed.placed && placed.tier == 4 && game.Tiles.Contains(red), "A red tile fits a cell that asks for red");
            var placedShade = placed == null ? null : placed.GetComponentInChildren<GroundShadow>(true);
            Check(placedShade == null || !placedShade.Shown, "A placed tile, flush with the floor, has no ground shadow");
            var data = JsonUtility.FromJson<GameManager.SaveData>(game.ExportSave());
            data.tileTiers = (data.tileTiers ?? new int[0]).Concat(new[] { red.x, red.y, 3 }).ToArray();
            Check(game.ImportSave(JsonUtility.ToJson(data)) && GameManager.RequiredTier(0, red.x, red.y) == 3, "A save keeps a green colour override on a placed tile");
            Check(game.ImportSave(saved) && Cells(game).SetEquals(before), "The network from before the colour check reloads");
            // The rest of the suite walks and dashes over island 1 along fixed paths: take the stock off the islands.
            foreach (var b in game.Blocks.Where(b => !b.placed && b.id.StartsWith("stock-")).ToArray()) { game.Blocks.Remove(b); ForestWorld.Remove(b.gameObject); }
            Physics.SyncTransforms();
            // The imports above reset the stock's first-tick fill, and CheckSharedPreviewAndFade's
            // SendMessage("Update") reaches LightingParticles too: mark it filled so nothing refills mid-suite.
            typeof(LightingParticles).GetField("filled", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(stock, true);
            Check(StockLeft() == 0, "The stock is off the islands for the rest of the suite");
        }
        // Active stock tiles in the scene, listed in game.Blocks or not.
        static int StockLeft() => UnityEngine.Object.FindObjectsByType<BridgeBlock>(FindObjectsSortMode.None).Count(b => b.isActiveAndEnabled && b.id != null && b.id.StartsWith("stock-"));

        // ---- grounding: the player's soft shadow, and the rig's soles on the floor ----------------
        static void CheckGrounding(GameManager game, ForestWorld forest)
        {
            var at = game.player.transform.position; var shadow = game.player.Shadow;
            var ground = forest.WisdomTrees[0].transform.position + Vector3.right * 2; ground.y = .025f;
            Teleport(game, ground); if (shadow != null) shadow.Follow();
            Check(shadow != null && shadow.Shown && Mathf.Abs(shadow.transform.position.x - ground.x) < .001f && Mathf.Abs(shadow.transform.position.y - GroundShadow.FloorY) < .001f,
                "The player casts a soft ground shadow on a hub");
            var bounds = HubBounds(forest.WisdomTrees[0].Definition.id); var open = new Vector3(bounds.min.x - 9, .025f, ground.z);
            for (int i = 0; i < 40 && game.CanStand(open); i++) open.x -= 2;
            Teleport(game, open); shadow.Follow();
            Check(!game.CanStand(open) && !shadow.Shown, "No ground shadow over the open void");
            Teleport(game, ground);
            // The lowest skinned vertex standing idle: the planted soles.
            var animator = game.player.animator;
            if (animator != null) { animator.Play("Base Layer.Idle", 0, 0); animator.Update(0); }
            float lowest = float.MaxValue; var mesh = new Mesh();
            foreach (var skin in game.player.visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.BakeMesh(mesh, true); var t = skin.transform;
                foreach (var v in mesh.vertices) lowest = Mathf.Min(lowest, (t.position + t.rotation * v).y);
            }
            UnityEngine.Object.DestroyImmediate(mesh);
            Check(lowest >= -.01f && lowest <= .03f, "The player's soles touch the floor standing idle (lowest vertex y " + lowest.ToString("F3") + ")");
            Teleport(game, at);
        }

        static Bounds HubBounds(string id)
        {
            var platforms=PlatformSupport.ActivePlatforms.Where(p=>p!=null&&p.hubId==id&&p.Surface!=null).ToArray();
            if(platforms.Length==0)throw new Exception("No platform supports for "+id);
            var bounds=platforms[0].Surface.bounds;for(int i=1;i<platforms.Length;i++)bounds.Encapsulate(platforms[i].Surface.bounds);return bounds;
        }
        static Vector2Int[] HubEdges(string id)
        {
            var b=HubBounds(id);var center=b.center;var cells=new System.Collections.Generic.List<Vector2Int>();
            int minX=Mathf.FloorToInt((b.min.x-1)/2)-1,maxX=Mathf.CeilToInt((b.max.x+1)/2)+1;
            int minZ=Mathf.FloorToInt((b.min.z-1)/2)-1,maxZ=Mathf.CeilToInt((b.max.z+1)/2)+1;
            for(int x=minX;x<=maxX;x++)for(int z=minZ;z<=maxZ;z++)
            {
                var c=new Vector2Int(x,z);if(!LightingBridgeGrid.OverlapsPlatform(c)&&LightingBridgeGrid.TouchesHub(c,id))cells.Add(c);
            }
            return cells.ToArray();
        }
        static Vector2Int EdgeFor(Vector2Int[] edges,Vector2Int direction,Vector3 origin)
        {
            return edges.OrderByDescending(c=>Vector3.Dot((LightingBridgeGrid.Cell(c.x,c.y)-origin),new Vector3(direction.x,0,direction.y))).First();
        }
        static Vector3 SupportedNear(GameManager game,Vector2Int cell)
        {
            Vector3 target=LightingBridgeGrid.Cell(cell.x,cell.y);Vector3 best=default;float distance=float.MaxValue;bool found=false;
            for(int x=cell.x-2;x<=cell.x+2;x++)for(int z=cell.y-2;z<=cell.y+2;z++)
            {
                Vector3 p=LightingBridgeGrid.Cell(x,z);if(!game.CanStand(p))continue;
                float d=(p-target).sqrMagnitude;if(d<distance){distance=d;best=p;found=true;}
            }
            if(!found||distance>16)throw new Exception("No supported player position within 4 m of grid cell "+cell);
            return best;
        }
        static BridgeBlock SpawnGridTile(GameManager game,Vector2Int cell,int tier,bool expectSuccess=true,string expectedReason=null)
        {
            Teleport(game,SupportedNear(game,cell));
            string island=PlatformSupport.ActivePlatforms.First(p=>p!=null&&p.hubId=="island-1").hubId;
            if(!game.TrySpawnAtWisdomTree(island,out var block,tier))throw new Exception("Could not spawn test tile");
            block.transform.position=LightingBridgeGrid.Cell(cell.x,cell.y);block.Box.Body.position=block.transform.position;Physics.SyncTransforms();
            bool valid=game.FindPlacement(block,out _,out _,out _,out string reason);
            if(expectSuccess&&!valid)throw new Exception("Grid edge was rejected: "+reason+" at "+cell);
            if(!expectSuccess)
            {
                Check(!valid&&(expectedReason==null||reason.Contains(expectedReason)),"Grid rejected "+(expectedReason??"the invalid cell")+" at "+cell+" ("+reason.Replace('\n',' ')+")");
                game.Blocks.Remove(block);block.gameObject.SetActive(false);UnityEngine.Object.Destroy(block.gameObject);Physics.SyncTransforms();return null;
            }
            if(!game.TryPlace(block))throw new Exception("TryPlace rejected valid test cell "+cell);
            return block;
        }
        static void CheckBranchingGrid(GameManager game,ForestWorld forest)
        {
            var firstBounds=HubBounds("island-1");var origin=firstBounds.center;
            var cardinal=new[]{Vector2Int.right,Vector2Int.left,Vector2Int.up,Vector2Int.down};
            var edgeCells=HubEdges("island-1");
            var roots=new System.Collections.Generic.Dictionary<Vector2Int,BridgeBlock>();
            foreach(var dir in cardinal)
            {
                var cell=EdgeFor(edgeCells,dir,origin);
                if(roots.Values.Any(b=>new Vector2Int(b.column,b.row)==cell))
                {
                    // Pick the next-most outward edge on the same side when a hub has a narrow
                    // footprint and two directions would otherwise select the same corner.
                    cell=edgeCells.Where(c=>!roots.Values.Any(b=>b.column==c.x&&b.row==c.y))
                        .OrderByDescending(c=>Vector3.Dot(LightingBridgeGrid.Cell(c.x,c.y)-origin,new Vector3(dir.x,0,dir.y))).First();
                }
                var block=SpawnGridTile(game,cell,GameManager.RequiredTier(0,cell.x,cell.y));roots[dir]=block;
            }
            Check(roots.Count==4&&roots.Values.Select(b=>new Vector2Int(b.column,b.row)).Distinct().Count()==4&&roots.Values.All(b=>b!=null&&b.Connected),"A branching network accepts starter tiles on all four orthogonal hub edges");
            var east=roots[Vector2Int.right];var start=new Vector2Int(east.column,east.row);
            CheckSharedPreviewAndFade(game,forest,start);
            CheckConnectedNetworkStress(start);
            var diagonalCandidates=new[]{start+Vector2Int.right+Vector2Int.up,start+Vector2Int.right+Vector2Int.down,start+Vector2Int.left+Vector2Int.up,start+Vector2Int.left+Vector2Int.down};
            var diagonal=diagonalCandidates.FirstOrDefault(c=>LightingBridgeGrid.InBounds(c)&&!LightingBridgeGrid.OverlapsPlatform(c)&&!LightingBridgeGrid.TouchesHub(c,null)&&!LightingBridgeGrid.NeighborsOf(c).Any(game.Tiles.Contains));
            Check(!LightingBridgeGrid.NeighborsOf(diagonal).Any(game.Tiles.Contains),"Diagonal test cell has no orthogonal tile connection");int diagonalTier=GameManager.RequiredTier(0,diagonal.x,diagonal.y);
            SpawnGridTile(game,diagonal,diagonalTier,false,"PATH DISCONNECTED");
            var wrongCell=start+Vector2Int.right;int wanted=GameManager.RequiredTier(0,wrongCell.x,wrongCell.y);
            SpawnGridTile(game,wrongCell,(wanted+1)%3,false,"THIS TILE ASKS FOR");
            var straight=start+Vector2Int.right;
            SpawnGridTile(game,straight,GameManager.RequiredTier(0,straight.x,straight.y));
            var northTurn=straight+Vector2Int.up;
            SpawnGridTile(game,northTurn,GameManager.RequiredTier(0,northTurn.x,northTurn.y));
            var southBranch=straight+Vector2Int.down;
            SpawnGridTile(game,southBranch,GameManager.RequiredTier(0,southBranch.x,southBranch.y));
            Check(game.Tiles.Count>=7,"Connected tiles can turn and branch around a loop-capable network");
            Vector2Int towardHub2=HubBounds("island-2").center.z>=origin.z?Vector2Int.up:Vector2Int.down;
            if(Mathf.Abs(HubBounds("island-2").center.x-origin.x)>Mathf.Abs(HubBounds("island-2").center.z-origin.z))towardHub2=HubBounds("island-2").center.x>=origin.x?Vector2Int.right:Vector2Int.left;
            Vector2Int route=EdgeFor(edgeCells,towardHub2,origin);int steps=0;
            while(!LightingBridgeGrid.TouchesHub(route,"island-2")&&steps<512)
            {
                var next=route+towardHub2;if(LightingBridgeGrid.OverlapsPlatform(next))throw new Exception("Hub 2 route ran into platform overlap before reaching its edge");
                SpawnGridTile(game,next,GameManager.RequiredTier(0,next.x,next.y));route=next;steps++;
            }
            Check(steps<512&&game.Bridges[0],"The connected network reaches hub 2");
            var hub2Bounds=HubBounds("island-2");var opposite=HubEdges("island-2").OrderByDescending(c=>Vector3.Dot(LightingBridgeGrid.Cell(c.x,c.y)-hub2Bounds.center,new Vector3(towardHub2.x,0,towardHub2.y))).First();
            Teleport(game,SupportedNear(game,opposite));
            Check(LightingBridgeGrid.IsFrontier(game,opposite),"A reached hub exposes its opposite platform edge to the same network");
            var terminal=SpawnGridTile(game,opposite,GameManager.RequiredTier(0,opposite.x,opposite.y));
            Teleport(game,SupportedNear(game,opposite-towardHub2));
            Check(game.TryReclaimTerminalTile(terminal)&&!terminal.placed&&!game.Tiles.Contains(opposite)&&game.Blocks.Contains(terminal),"An empty terminal tile can be reclaimed as a loose stone while preserving the remaining network");
            var articulation=straight;
            Teleport(game,SupportedNear(game,northTurn));var articulationBlock=game.Tiles.Visual(articulation);
            Check(!game.TryReclaimTerminalTile(articulationBlock)&&articulationBlock.placed,"An articulation tile cannot be reclaimed");
            var occupied=game.Tiles.Visual(northTurn);
            Teleport(game,LightingBridgeGrid.Cell(northTurn.x,northTurn.y));
            Check(!game.TryReclaimTerminalTile(occupied)&&occupied.placed,"A terminal tile occupied by the player cannot be reclaimed");
        }

        static void CheckSharedPreviewAndFade(GameManager game,ForestWorld forest,Vector2Int supportedCell)
        {
            Teleport(game,SupportedNear(game,supportedCell));
            Vector3 forward=game.player.visual!=null?game.player.visual.forward:Vector3.forward;
            Check(LightingBridgeGrid.TryGetFrontier(game,game.player.transform.position,forward,out var candidate),"A nearby supported tile exposes a local grid preview candidate");
            int wanted=GameManager.RequiredTier(0,candidate.x,candidate.y);
            Check(game.TryGetLightingPreviewTarget(out var previewTarget)&&Vector3.Distance(previewTarget,LightingBridgeGrid.Cell(candidate.x,candidate.y))<.01f,"The ghost obtains its geometric target from the shared local candidate resolver");
            var island=forest.configuration.islands[0];
            Check(game.TrySpawnAtWisdomTree(island.id,out var previewBlock,wanted),"Spawned a temporary correctly-coloured preview block");
            Check(game.FindOrbitPlacement(previewBlock,out var target)&&Vector3.Distance(target,LightingBridgeGrid.Cell(candidate.x,candidate.y))<.01f,"Orbit placement and the ghost use the same nearby geometric candidate");
            var heldField=typeof(BoxPlayer).GetField("<HeldBox>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);
            var orbitField=typeof(BoxPlayer).GetField("<Orbiting>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);
            Check(heldField!=null&&orbitField!=null,"Orbit preview state is available for a controlled test");
            game.player.HeldBoxes.Clear();game.player.HeldBoxes.Add(previewBlock.Box);heldField.SetValue(game.player,previewBlock.Box);orbitField.SetValue(game.player,true);
            game.SendMessage("Update",SendMessageOptions.RequireReceiver);
            Vector3 expectedGhost=LightingBridgeGrid.Cell(candidate.x,candidate.y)+Vector3.up*.205f;
            Check(Vector3.Distance(ghostFieldPosition(game),expectedGhost)<.01f,"The live orbit ghost sits on the same candidate as its placement preview");
            previewBlock.tier=(wanted+1)%3;
            Check(!game.FindOrbitPlacement(previewBlock,out _)&&game.TryGetLightingPreviewTarget(out var wrongColorTarget)&&Vector3.Distance(wrongColorTarget,target)<.01f,"A wrong-colour block keeps the same ghost candidate while validated orbit placement remains rejected");
            game.SendMessage("Update",SendMessageOptions.RequireReceiver);
            Check(Vector3.Distance(ghostFieldPosition(game),expectedGhost)<.01f,"The live ghost does not jump to another edge when the carried colour is wrong");
            game.player.DropOrbit(true);
            bool safeWrongColorDrop=!previewBlock.placed&&((game.player.Orbiting&&game.player.HeldBoxes.Contains(previewBlock.Box))||game.CanStand(previewBlock.transform.position));
            Check(safeWrongColorDrop,"A wrong-colour Q/build drop remains safely supported or stays in orbit");
            game.player.HeldBoxes.Clear();heldField.SetValue(game.player,null);orbitField.SetValue(game.player,false);
            game.Blocks.Remove(previewBlock);previewBlock.gameObject.SetActive(false);UnityEngine.Object.Destroy(previewBlock.gameObject);Physics.SyncTransforms();

            var ghostField=typeof(GameManager).GetField("ghost",BindingFlags.Instance|BindingFlags.NonPublic);
            var fadeField=typeof(GameManager).GetField("ghostFade",BindingFlags.Instance|BindingFlags.NonPublic);
            var ghost=ghostField?.GetValue(game) as Transform;
            Check(ghost!=null&&fadeField!=null,"The Lighting preview renderers are initialized");
            float firstFade=GameManager.StepLightingPreviewFade(0,true,2.5f,.05f);float settled=firstFade;
            for(int i=1;i<4;i++)settled=GameManager.StepLightingPreviewFade(settled,true,2.5f,.05f);
            float faded=GameManager.StepLightingPreviewFade(settled,false,2.5f,.05f);
            Check(Math.Abs(firstFade-.25f)<.001f&&Math.Abs(settled-1)<.001f&&Math.Abs(faded-.75f)<.001f,
                "The reachable ghost fades in and out over 0.2 seconds (0.05s steps: first="+firstFade.ToString("0.00")+", full="+settled.ToString("0.00")+", fading="+faded.ToString("0.00")+")");
            fadeField.SetValue(game,settled);game.SendMessage("Update",SendMessageOptions.RequireReceiver);
            Check(ghost.gameObject.activeSelf,"A nearby, reachable candidate renders its ghost");

            var bounds=HubBounds("island-1");Vector3 interior=default;float deepest=-1;
            var hubEdges=HubEdges("island-1");
            for(int x=Mathf.CeilToInt(bounds.min.x/2);x<=Mathf.FloorToInt(bounds.max.x/2);x++)
            for(int z=Mathf.CeilToInt(bounds.min.z/2);z<=Mathf.FloorToInt(bounds.max.z/2);z++)
            {
                Vector3 p=LightingBridgeGrid.Cell(x,z);p.y=.025f;if(!PlatformSupport.Supports(p))continue;
                float edgeDistance=hubEdges.Min(c=>(LightingBridgeGrid.Cell(c.x,c.y)-p).sqrMagnitude);
                if(edgeDistance>deepest){deepest=edgeDistance;interior=p;}
            }
            Check(deepest>16,"The hub provides an interior location more than four metres from any open edge");
            Teleport(game,interior);Check(!LightingBridgeGrid.TryGetFrontier(game,interior,forward,out _),"An interior player cannot receive a distant edge fallback");
            fadeField.SetValue(game,0f);game.SendMessage("Update",SendMessageOptions.RequireReceiver);
            Check((float)fadeField.GetValue(game)<.01f&&!ghost.gameObject.activeSelf,"The preview fades out and hides when no reachable edge is within four metres");

            fadeField.SetValue(game,1f);ghost.gameObject.SetActive(true);GameHUD.Current.ShowPause();game.SendMessage("Update",SendMessageOptions.RequireReceiver);
            Check(!ghost.gameObject.activeSelf&&(float)fadeField.GetValue(game)==0,"A paused menu hides the ghost immediately");
            GameHUD.Current.BeginJourney();
        }
        static Vector3 ghostFieldPosition(GameManager game)
        {
            return (typeof(GameManager).GetField("ghost",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(game) as Transform)?.position??default;
        }
        static void CheckConnectedNetworkStress(Vector2Int anchor)
        {
            var fixture=new System.Collections.Generic.HashSet<Vector2Int>();
            for(int x=0;x<320;x++)for(int z=-159;z<=160;z++)fixture.Add(anchor+new Vector2Int(x,z));
            var timer=System.Diagnostics.Stopwatch.StartNew();var connected=LightingBridgeGrid.ConnectedCells(fixture);timer.Stop();
            Check(fixture.Count>100000&&connected.Count==fixture.Count&&timer.Elapsed.TotalSeconds<10,
                "A connected 100,000-cell fixture floods without dropping tiles ("+fixture.Count+" cells in "+timer.Elapsed.TotalSeconds.ToString("0.000")+" s)");
        }

        // ---- save/reload round trip: souls, upgrades, inventory, a pending fragment, block state --
        static void CheckSaveRoundTrip(GameManager game, ForestWorld forest)
        {
            var island = forest.configuration.islands[0];
            game.souls = 0; game.speed = 3; game.soulGainLevel = 5;
            forest.State.inventory = 28; // 7 whole particles in quarter-particle units
            // A dot taken 7.75 s ago: its cell comes back in 7.25 s.
            var takenCell = Enumerable.Range(0, 400).Select(i => new Vector2Int(60 + i % 20, i / 20)).First(c => forest.Dots.IsDot(c));
            forest.State.dots.Clear(); forest.State.dots.Add(new DotSave { x = takenCell.x, z = takenCell.y, remaining = 7.25f });
            var placedBefore=PlacedCells(game);
            Check(placedBefore.Length>0,"Branching fixture has placed tiles to round-trip");

            Check(game.TrySpawnAtWisdomTree(island.id, out var looseBlock, 1), "Spawned a loose (unplaced) block for the save check");
            string looseId = looseBlock.id; Vector3 loosePos = looseBlock.transform.position;

            // Poll() above sets persistenceEnabled=false every frame (so the running check never
            // touches a real player save file); redirect to a throwaway path and flip it on just for
            // this round trip, same isolation ForestVerification.cs uses for its own save checks.
            game.savePathOverride = Path.Combine(Application.temporaryCachePath, "LightingIntegrationCheck-"+Guid.NewGuid().ToString("N")+".json");
            game.persistenceEnabled = true;
            bool loaded;
            try
            {
            Check(game.Save(), "Save() writes the current state");
            // Mutate everything away from the saved values before reloading - a match afterward
            // proves Load() actually restored from disk, not that the fields were untouched.
            game.souls = 0; game.speed = 0; game.soulGainLevel = 0;
            forest.State.inventory = 0; forest.State.dots.Clear();

            loaded = game.Load();
            }
            finally
            {
            game.persistenceEnabled = false;
            if (File.Exists(game.SavePath)) File.Delete(game.SavePath);
            if (File.Exists(game.SavePath + ".bak")) File.Delete(game.SavePath + ".bak");
            if(File.Exists(game.SavePath+".tmp"))File.Delete(game.SavePath+".tmp");
            }
            // Keep the isolated path even after the temporary save has been deleted.
            Check(loaded, "Load() reports success reading the just-written save");
            Check(game.souls==0&&game.Rate==0,"Lighting save/reload has no Soul or offline-income accrual");
            Check(game.speed == 3 && game.soulGainLevel == 5, "Upgrades survive save/reload (speed=" + game.speed + " soulGain=" + game.soulGainLevel + ")");
            Check(forest.State.inventory == 28&&forest.SharedInventoryLabel=="7", "Quarter-particle wallet survives save/reload (" + forest.SharedInventoryLabel + " particles)");
            Check(forest.State.particleVersion == 5 && forest.State.dots.Count == 1 && forest.State.dots[0].x == takenCell.x && forest.State.dots[0].z == takenCell.y && Math.Abs(forest.State.dots[0].remaining - 7.25f) < .001f,
                "A collected dot's return timer survives save/reload");
            var placedAfter=PlacedCells(game);
            Check(placedBefore.SequenceEqual(placedAfter),"Every placed grid cell and its saved color survive save/reload");
            var malformedSave=JsonUtility.FromJson<GameManager.SaveData>(game.ExportSave());
            Check(malformedSave.version==6&&!malformedSave.blocks.Any(b=>b.placed)&&malformedSave.tiles.Length==2*game.Tiles.Count,"A version-6 save lists the placed tiles as cells and keeps only loose stones as blocks");
            malformedSave.tiles[0]=1000000; // far past the build margin
            int walletBefore=forest.SharedInventory;var cellsBefore=placedAfter.ToArray();
            Check(!game.ImportSave(JsonUtility.ToJson(malformedSave))&&forest.SharedInventory==walletBefore&&cellsBefore.SequenceEqual(PlacedCells(game)),"A rejected malformed grid import leaves the active wallet and network untouched");
            var reloadedLoose = game.Blocks.FirstOrDefault(b => b.id == looseId);
            Check(reloadedLoose != null && !reloadedLoose.placed && Vector3.Distance(reloadedLoose.transform.position, loosePos) < .1f, "Loose (unplaced) block position survives save/reload");
        }

        // ---- Lighting Dash purchase, persistence, cooldown, pause, and void-gap safety ----------
        // Clicking a skill circle in the Skills view awakens its next level and spends its particles.
        static void CheckSkillTreeClick(GameManager game, ForestWorld forest)
        {
            var tree = forest.WisdomTrees.First(t => t != null);
            var skills = UnityEngine.Object.FindAnyObjectByType<SkillTree>(FindObjectsInactive.Include);
            int oldSpeed = game.speed;
            game.speed = 0; game.ApplyUpgrades(); forest.State.inventory = 100; // 25 whole particles
            Teleport(game, tree.transform.position + Vector3.right * 2);
            GameHUD.Current.SetView(GameHUD.ScreenView.Skills);
            try
            {
                var corners = new Vector3[4]; ((RectTransform)GameObject.Find("Power speed").transform).GetWorldCorners(corners);
                Vector2 speedCentre = (corners[0] + corners[2]) * .5f; // overlay canvas: world corners are screen pixels
                Check(skills != null && skills.ClickAt(speedCentre) && game.speed == 1 && forest.SharedInventory == 60,
                    "Clicking Swift Stride at a Wisdom Tree awakens level I and spends its 10 particles");
                Teleport(game, tree.transform.position + Vector3.right * 8);
                Check(skills.ClickAt(speedCentre) && game.speed == 1 && forest.SharedInventory == 60 && game.Notice.StartsWith("STAND BY A WISDOM TREE"),
                    "Clicking a skill away from a Wisdom Tree is refused with a notice and spends nothing");
                Check(!skills.ClickAt(new Vector2(-50, -50)) && game.speed == 1, "Clicks outside every skill buy nothing");
            }
            finally
            {
                GameHUD.Current.SetView(GameHUD.ScreenView.Playing);
                game.speed = oldSpeed; game.ApplyUpgrades();
            }
        }

        static void CheckDash(GameManager game, ForestWorld forest)
        {
            var tree = forest.WisdomTrees.First(t => t != null);
            game.dashLevel = 0;
            forest.State.inventory = 100; // 25 whole particles in quarter units
            Teleport(game, tree.transform.position + Vector3.right * 8);
            Check(!game.CanBuyLightingUpgrades() && !game.Purchase("dash"), "Dash cannot be purchased away from a Wisdom Tree");
            Teleport(game, tree.transform.position + Vector3.right * 2);
            Check(game.CanBuyLightingUpgrades() && Math.Abs(game.Cost("dash") - 25) < .001 && game.Cap("dash") == 5,
                "Dash costs 25 particles, has five levels, and uses the Wisdom Tree purchase radius");
            Check(string.Join("/", BoxPlayer.DashTiles) == "3/5/7/9/12" && Math.Abs(BoxPlayer.DashDistance(5) - 12 * LightingBridgeGrid.CellSize) < .001f,
                "Dash distance scales 3/5/7/9/12 tiles across its levels");
            GameHUD.Current.SetView(GameHUD.ScreenView.Skills);
            Check(game.Purchase("dash") && game.dashLevel == 1 && forest.SharedInventory == 0,
                "Buying Dash in the Skills view spends exactly 25 particles");
            GameHUD.Current.SetView(GameHUD.ScreenView.Playing);

            string unlockedSave = game.ExportSave();
            game.dashLevel = 0;
            Check(game.ImportSave(unlockedSave) && game.dashLevel == 1, "Dash unlock survives save and load");
            string oldSave = unlockedSave.Replace("\"dashLevel\":1", "\"retiredDashLevel\":1");
            game.dashLevel = 1;
            Check(game.ImportSave(oldSave) && game.dashLevel == 0, "Saves without Dash data load with Dash locked");
            Check(game.ImportSave(unlockedSave) && game.dashLevel == 1, "Reloading the current save restores the Dash unlock");
            var invalid = JsonUtility.FromJson<GameManager.SaveData>(unlockedSave); invalid.dashLevel = 6;
            int walletBeforeInvalid = forest.SharedInventory;
            Check(!game.ImportSave(JsonUtility.ToJson(invalid)) && game.dashLevel == 1 && forest.SharedInventory == walletBeforeInvalid,
                "Invalid Dash levels are rejected without changing the unlock or wallet");
            game.dashLevel = 5; forest.State.inventory = 100000;
            Check(!game.Purchase("dash") && game.dashLevel == 5 && forest.SharedInventory == 100000,
                "The five-level Dash cap rejects repeat purchases without spending");
            game.dashLevel = 1;

            // Dashing off the island-1 hub edge into the void lays a straight run of connected
            // tiles under the burst, even in one oversized frame, and leaves the player on one.
            var bounds = HubBounds("island-1");
            Quaternion oldCameraRotation = game.player.viewCamera != null ? game.player.viewCamera.transform.rotation : Quaternion.identity;
            GameObject wall = null;
            var baseline = Cells(game);
            try
            {
                float startX = bounds.min.x + .22f;
                Teleport(game, new Vector3(startX, .025f, bounds.center.z));
                Check(game.CanStand(game.player.transform.position), "Void dash fixture begins on supported hub ground");
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                Physics.SyncTransforms();
                float xBefore = game.player.transform.position.x;
                Check(game.player.TryStartDash(Vector2.up), "Unlocked Dash starts from supported ground");
                game.player.StepMovement(Vector2.zero, BoxPlayer.DashDuration(1) * 2); // one oversized frame
                Physics.SyncTransforms();
                var laid = game.Tiles.Cells.Where(c => !baseline.Contains(c)).Select(game.Tiles.Visual).ToArray();
                Check(laid.Length >= 2 && laid.All(b => b != null && b.placed && b.Connected),
                    "Dashing into the void lays connected bridge tiles (" + laid.Length + " laid)");
                Check(xBefore - game.player.transform.position.x > BoxPlayer.DashDistance(1) - .5f && game.CanStand(game.player.transform.position),
                    "Level-1 Dash covers 3 tiles into the void and lands on a laid tile");
                Teleport(game, new Vector3(startX, .025f, bounds.center.z));
                RestoreTiles(game, baseline);
                Check(!game.player.TryStartDash(Vector2.up) && game.player.DashCooldownRemaining > BoxPlayer.DashCooldown - .1f,
                    "Dash enters its " + BoxPlayer.DashCooldown + "-second cooldown after the burst");
                for (int i = 0; i < 42; i++) game.player.StepMovement(Vector2.zero, .05f);
                Check(game.player.DashCooldownRemaining <= .001f && game.player.TryStartDash(Vector2.up),
                    "Dash can be used again after the " + BoxPlayer.DashCooldown + "-second cooldown");
                GameHUD.Current.SetView(GameHUD.ScreenView.Pause);
                float pausedCooldown = game.player.DashCooldownRemaining;
                Check(!game.player.IsDashing && !game.player.TryStartDash(Vector2.up), "Opening a menu cancels the active burst and blocks Dash input");
                game.player.StepMovement(Vector2.zero, .2f);
                Check(Math.Abs(game.player.DashCooldownRemaining - pausedCooldown) < .001f, "Paused time does not consume the Dash cooldown");
                GameHUD.Current.SetView(GameHUD.ScreenView.Playing);
                game.player.CancelDash();
                for (int i = 0; i < 42; i++) game.player.StepMovement(Vector2.zero, .05f);
                Teleport(game, tree.transform.position + Vector3.right * 2);
                Vector3 forward = game.player.viewCamera != null ? Vector3.ProjectOnPlane(game.player.viewCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Dash wall collision test";
                wall.transform.position = game.player.transform.position + forward * 1.1f + Vector3.up * .9f;
                wall.transform.rotation = Quaternion.LookRotation(forward, Vector3.up); wall.transform.localScale = new Vector3(3, 1.8f, .2f);
                Physics.SyncTransforms();
                float beforeWall = game.player.transform.position.x * forward.x + game.player.transform.position.z * forward.z;
                Check(game.player.TryStartDash(Vector2.up), "Dash restarts after the cooldown before the wall test");
                game.player.StepMovement(Vector2.zero, BoxPlayer.DashDuration(1));
                Physics.SyncTransforms();
                float afterWall = game.player.transform.position.x * forward.x + game.player.transform.position.z * forward.z;
                Check(afterWall - beforeWall < .9f && game.CanStand(game.player.transform.position), "The CharacterController wall collision stops Dash safely");

                // Standing still, Dash follows the body, not the camera: camera north, body west.
                game.player.ResetDash();
                Teleport(game, new Vector3(startX, .025f, bounds.center.z));
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                if (game.player.visual != null) game.player.visual.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                Physics.SyncTransforms();
                float standingX = game.player.transform.position.x;
                Check(game.player.TryStartDash(Vector2.zero), "Dash starts while standing still");
                game.player.StepMovement(Vector2.zero, BoxPlayer.DashDuration(1) * 2);
                Physics.SyncTransforms();
                Check(game.player.visual != null && standingX - game.player.transform.position.x > BoxPlayer.DashDistance(1) - .5f,
                    "A standing Dash bursts the way the character faces, not the camera");
                RestoreTiles(game, baseline); game.player.ResetDash();

                // Dashing through a dot collects it: a pickup poll tests the whole path since the last
                // poll, not only the spot where the burst ended 6 m later.
                var dots = forest.Dots; int oldSeed = forest.State.seed;
                float rowZ = LightingBridgeGrid.Cell(0, Mathf.RoundToInt(bounds.center.z / LightingBridgeGrid.CellSize)).z;
                var dotCell = LightingBridgeGrid.CellOf(new Vector3(bounds.min.x - 3f, 0, rowZ));
                try
                {
                    for (int i = 0; i < 1000 && !dots.IsDot(dotCell); i++) forest.State.seed++;
                    Check(dots.IsDot(dotCell), "A dot floats over the dash path, 3 m into the void");
                    Teleport(game, new Vector3(startX, .025f, rowZ));
                    if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                    Physics.SyncTransforms(); dots.Refresh();
                    forest.Tick(.1f, true); // the poll before the burst
                    int walletBefore = forest.SharedInventory; float dashFrom = game.player.transform.position.x;
                    Check(game.player.TryStartDash(Vector2.up), "Dash starts toward the dot");
                    game.player.StepMovement(Vector2.zero, BoxPlayer.DashDuration(1) * 2); Physics.SyncTransforms();
                    forest.Tick(.1f, true); // the first poll lands after the burst, 6 m out
                    // Another seeded dot on the same row is taken too; every pickup pays the full yield.
                    int perDot = ForestWorld.ParticleUnitsPerParticle + Mathf.Clamp(game.soulGainLevel, 0, 8), gained = forest.SharedInventory - walletBefore;
                    Check(gained >= perDot && gained % perDot == 0
                        && forest.State.dots.Any(d => d.x == dotCell.x && d.z == dotCell.y), "Dashing through a dot collects it (dashed "
                        + (dashFrom - game.player.transform.position.x).ToString("0.00") + " m, tile under the dot " + (game.BlockUnder(LightingDots.Position(dotCell)) != null) + ", wallet +"
                        + (forest.SharedInventory - walletBefore) + ", dot " + dotCell + " from x " + dashFrom.ToString("0.00") + " z " + rowZ.ToString("0.00") + ", shown " + dots.ShownCells.Contains(dotCell) + ")");
                }
                finally
                {
                    forest.State.seed = oldSeed; dots.ResetField();
                    RestoreTiles(game, baseline); game.player.ResetDash();
                }
            }
            finally
            {
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = oldCameraRotation;
                if (wall != null) UnityEngine.Object.Destroy(wall);
                Physics.SyncTransforms();
            }
        }

        // ---- Light Grenade: a charged throw lays connected tiles in its blast and relights old ones ----
        static void CheckGrenade(GameManager game, ForestWorld forest)
        {
            var grenade = game.GetComponent<LightingGrenade>();
            Check(grenade != null, "The Light Grenade rides on the Lighting GameManager");
            var tree = forest.WisdomTrees.First(t => t != null);
            int oldLevel = game.grenadeLevel; game.grenadeLevel = 0;
            Check(Math.Abs(game.Cost("grenade") - 40) < .001 && game.Cap("grenade") == 3 && string.Join("/", LightingGrenade.GrenadeTiles) == "5/6/7",
                "Light Grenade costs 40 particles and has three levels (5/6/7 tiles)");
            forest.State.inventory = 40 * ForestWorld.ParticleUnitsPerParticle;
            Teleport(game, tree.transform.position + Vector3.right * 8);
            Check(!game.Purchase("grenade") && game.grenadeLevel == 0, "Light Grenade cannot be bought away from a Wisdom Tree");
            Teleport(game, tree.transform.position + Vector3.right * 2);
            Check(game.Purchase("grenade") && game.grenadeLevel == 1 && forest.SharedInventory == 0, "Buying Light Grenade at a tree spends exactly 40 particles");
            string save = game.ExportSave(); game.grenadeLevel = 0;
            Check(game.ImportSave(save) && game.grenadeLevel == 1, "The Light Grenade level survives save and load");
            var invalid = JsonUtility.FromJson<GameManager.SaveData>(save); invalid.grenadeLevel = 4;
            Check(!game.ImportSave(JsonUtility.ToJson(invalid)) && game.grenadeLevel == 1, "A Light Grenade level past the cap is rejected");
            game.grenadeLevel = 3; forest.State.inventory = 100000;
            Check(!game.Purchase("grenade") && game.grenadeLevel == 3 && forest.SharedInventory == 100000, "The three-level cap refuses a fourth purchase without spending");
            game.grenadeLevel = 1;

            // Full charge from the island-1 west edge, the camera looking west, out over the void.
            var bounds = HubBounds("island-1");
            Quaternion oldCameraRotation = game.player.viewCamera != null ? game.player.viewCamera.transform.rotation : Quaternion.identity;
            var baseline = Cells(game);
            try
            {
                float rowZ = LightingBridgeGrid.Cell(0, Mathf.RoundToInt(bounds.center.z / LightingBridgeGrid.CellSize)).z;
                Vector3 edge = new Vector3(bounds.min.x + .22f, .025f, rowZ);
                Teleport(game, edge);
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                Physics.SyncTransforms();
                int price = LightingGrenade.ThrowCost * ForestWorld.ParticleUnitsPerParticle, tilesBefore = game.Tiles.Count;
                forest.State.inventory = price - 1;
                Check(!grenade.TryThrow(1) && !grenade.Flying && forest.SharedInventory == price - 1 && game.Tiles.Count == tilesBefore, "A throw without 10 particles is refused and spends nothing");
                forest.State.inventory = price;
                Check(grenade.TryThrow(1) && grenade.Flying && forest.SharedInventory == 0, "A full-charge throw spends exactly 10 particles");
                var landing = LightingBridgeGrid.CellOf(grenade.Landing); var thrower = LightingBridgeGrid.CellOf(game.player.transform.position);
                Check(Mathf.Abs(((Vector2)(landing - thrower)).magnitude - 5) <= 1, "Full charge at level 1 lands 5 tiles out");
                for (int i = 0; i < 240 && grenade.Busy; i++) grenade.Tick(1f / 60);
                Check(!grenade.Busy, "The grenade lands and its blast finishes within four seconds");
                var laidCells = game.Tiles.Cells.Where(c => !baseline.Contains(c)).ToArray();
                var laid = laidCells.Select(game.Tiles.Visual).ToArray();
                Check(laid.Length > 20 && laid.All(b => b != null && b.placed && b.Connected && (new Vector2Int(b.column, b.row) - landing).sqrMagnitude <= 25),
                    "The blast lays connected tiles within 5 tiles of where it lands (" + laid.Length + " laid)");
                Check(laid.All(b => b.lit == 1), "Every tile in the blast lights up");
                var body = laid[0].GetComponent<BlockArt>().bodies.First(r => r != null); var props = new MaterialPropertyBlock(); body.GetPropertyBlock(props);
                var lamp = laid[0].transform.Find("Step light")?.GetComponent<Light>();
                Check(Math.Abs(props.GetFloat("_LitGain") - BlockArt.LightingLitGain) < .001f && lamp != null && Math.Abs(lamp.intensity - WorldDressing.LightingStepLight) < .01f,
                    "A lit Lighting tile shines at twice the crystal brightness with an intensity-8 step light");
                Check(game.ImportSave(game.ExportSave()), "A save taken after the blast reloads: every blast tile is connected");
                laid = laidCells.Select(game.Tiles.Visual).ToArray(); // the reload streamed fresh GameObjects in
                Check(laid.All(b => b != null), "The reload streams every blast tile back in around the player");

                // A second throw at the same spot finds no free cell it can reach; it relights the old tiles.
                foreach (var b in laid) { b.lit = 0; WorldDressing.StepGlow(b, 0); }
                int before = game.Tiles.Count; forest.State.inventory = price;
                Teleport(game, edge);
                Check(grenade.TryThrow(1), "A second throw starts");
                for (int i = 0; i < 240 && grenade.Busy; i++) grenade.Tick(1f / 60);
                Check(!grenade.Busy && game.Tiles.Count == before && laid.All(b => b.lit == 1), "A blast over placed tiles lays nothing new and relights them");
            }
            finally
            {
                RestoreTiles(game, baseline); game.grenadeLevel = oldLevel;
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = oldCameraRotation;
                Physics.SyncTransforms();
            }
        }

        // ---- Dash after a grenade, diagonals included: no collider on the open network stops a dash ----
        static void CheckDashAfterGrenade(GameManager game, ForestWorld forest)
        {
            Check(StockLeft() == 0, "No stock tiles lie in the dash paths (" + StockLeft() + " active, " + game.Blocks.Count(b => b.id != null && b.id.StartsWith("stock-")) + " listed)");
            var grenade = game.GetComponent<LightingGrenade>();
            var bounds = HubBounds("island-1");
            int oldDash = game.dashLevel, oldGrenade = game.grenadeLevel, oldInventory = forest.State.inventory;
            Quaternion oldCameraRotation = game.player.viewCamera != null ? game.player.viewCamera.transform.rotation : Quaternion.identity;
            var baseline = Cells(game);
            var stops = new System.Collections.Generic.Dictionary<string, int>(); var samples = new System.Collections.Generic.List<string>();
            var other = new System.Collections.Generic.Dictionary<string, int>();
            int dashes = 0;
            void Look(Vector3 forward) { if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = Quaternion.LookRotation(forward, Vector3.up); }
            void Note(string where)
            {
                var stop = game.player.DashStop; if (stop == null) return;
                int at = stop.IndexOf(" at "); string key = at > 0 ? stop.Substring(0, at) : stop;
                // An occupied cell refused as a dash tile means support missed a tile that is there; one not on
                // the frontier means a diagonal skipped its stepping tile. From the network, neither may happen.
                if (stop.StartsWith("blocked") || stop.StartsWith("slid") || stop.Contains("occupied") || stop.Contains("not frontier")) { stops[key] = stops.TryGetValue(key, out int n) ? n + 1 : 1; if (samples.Count < 5) samples.Add(where + ": " + stop); }
                else
                {
                    // The refused cell is the last word of the reason; drop it so reasons group.
                    if (key.StartsWith("no tile")) { int cell = key.LastIndexOf(" ("); if (cell > 0) key = key.Substring(0, cell); }
                    if (key.StartsWith("left support")) key = "left support";
                    other[key] = other.TryGetValue(key, out int n) ? n + 1 : 1;
                    if (samples.Count < 12 && !samples.Any(s => s.Contains(key))) samples.Add(where + ": " + stop);
                }
            }
            var inputs = new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) };
            try
            {
                game.grenadeLevel = LightingGrenade.GrenadeTiles.Length; game.dashLevel = BoxPlayer.DashTiles.Length;
                float rowZ = LightingBridgeGrid.Cell(0, Mathf.RoundToInt(bounds.center.z / LightingBridgeGrid.CellSize)).z;
                var edge = new Vector3(bounds.min.x + .22f, .025f, rowZ);

                // Dashing while the blast is still laying its tiles, from three spots on the edge.
                for (int spot = -1; spot <= 1; spot++)
                    foreach (var input in inputs)
                    {
                        RestoreTiles(game, baseline);
                        var from = edge + Vector3.forward * (spot * 4f);
                        Teleport(game, from); Look(Vector3.left); Physics.SyncTransforms();
                        forest.State.inventory = LightingGrenade.ThrowCost * ForestWorld.ParticleUnitsPerParticle;
                        if (!grenade.TryThrow(.5f)) continue;
                        while (grenade.Flying) grenade.Tick(1f / 60);
                        game.player.ResetDash(); Look(Vector3.forward);
                        if (!game.player.TryStartDash(input)) continue;
                        dashes++;
                        for (int f = 0; f < 120 && (game.player.IsDashing || grenade.Busy); f++)
                        { grenade.Tick(1f / 60); game.player.StepMovement(Vector2.zero, 1f / 60); Physics.SyncTransforms(); game.Tiles.Stream(game.player.transform.position); }
                        Note("mid-blast from " + from.ToString("F1") + " input " + input);
                    }

                // Then from all over a finished level-3 blast: tile centres and near their corners.
                RestoreTiles(game, baseline);
                Teleport(game, edge); Look(Vector3.left); Physics.SyncTransforms();
                forest.State.inventory = LightingGrenade.ThrowCost * ForestWorld.ParticleUnitsPerParticle;
                Check(grenade.TryThrow(1), "Fixture: a full-charge level-3 grenade off the island-1 edge");
                for (int i = 0; i < 240 && grenade.Busy; i++) grenade.Tick(1f / 60);
                var blob = Cells(game); var laid = blob.Where(c => !baseline.Contains(c)).OrderBy(c => c.x).ThenBy(c => c.y).ToList();
                Check(laid.Count > 60, "Fixture: the blast laid " + laid.Count + " tiles");
                var pair = laid.First(c => blob.Contains(c + Vector2Int.right)); var seam = LightingBridgeGrid.Cell(pair.x, pair.y) + Vector3.right;
                Check(new[] { -.015f, 0, .015f }.All(dx => game.CanStand(seam + new Vector3(dx, .2f, .3f))),
                    "The 2 cm either side of an x seam between two tiles is ground (it was void, and stopped dashes)");
                Look(Vector3.forward);
                var offsets = new[] { Vector3.zero, new Vector3(.6f, 0, .6f), new Vector3(-.6f, 0, .6f), new Vector3(.6f, 0, -.6f), new Vector3(-.6f, 0, -.6f) };
                for (int t = 0; t < laid.Count; t += 2)
                    foreach (var offset in offsets)
                        foreach (var input in inputs)
                        {
                            var from = LightingBridgeGrid.Cell(laid[t].x, laid[t].y) + offset;
                            Teleport(game, from);
                            if (!game.CanStand(game.player.transform.position)) continue;
                            game.player.ResetDash();
                            if (!game.player.TryStartDash(input)) continue;
                            dashes++;
                            for (int f = 0; f < 120 && game.player.IsDashing; f++) { game.player.StepMovement(Vector2.zero, 1f / 60); Physics.SyncTransforms(); game.Tiles.Stream(game.player.transform.position); }
                            Note("from " + from.ToString("F2") + " input " + input);
                            if (game.Tiles.Count != blob.Count) RestoreTiles(game, blob);
                        }
                report.AppendLine("INFO: " + dashes + " dashes after a grenade; other stops: " + string.Join(", ", other.OrderByDescending(kv => kv.Value).Select(kv => kv.Value + "x " + kv.Key)));
                foreach (var sample in samples) report.AppendLine("INFO: " + sample);
                Check(stops.Count == 0, "Across " + dashes + " dashes after a grenade (diagonals included), none stops against a collider, at a tile already there or off the frontier"
                    + (stops.Count > 0 ? ": " + string.Join("; ", stops.OrderByDescending(kv => kv.Value).Select(kv => kv.Value + "x " + kv.Key)) + " | " + string.Join(" | ", samples) : ""));
            }
            finally
            {
                RestoreTiles(game, baseline);
                game.dashLevel = oldDash; game.grenadeLevel = oldGrenade; forest.State.inventory = oldInventory; game.player.ResetDash();
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = oldCameraRotation;
                Physics.SyncTransforms();
            }
        }

        // ---- Spirit Pull: F takes every dot within the grenade radius, over the void too ----
        static void CheckPull(GameManager game, ForestWorld forest)
        {
            var pull = game.GetComponent<LightingPull>();
            Check(pull != null, "Spirit Pull rides on the Lighting GameManager");
            var tree = forest.WisdomTrees.First(t => t != null);
            int oldLevel = game.pullLevel; game.pullLevel = 0;
            Check(Math.Abs(game.Cost("pull") - 30) < .001 && game.Cap("pull") == 3 && LightingPull.Radius(1) == 5 && LightingPull.Radius(3) == 7,
                "Spirit Pull costs 30 particles and has three levels with the grenade's 5/6/7-tile radius");
            forest.State.inventory = 30 * ForestWorld.ParticleUnitsPerParticle;
            Teleport(game, tree.transform.position + Vector3.right * 8);
            Check(!game.Purchase("pull") && game.pullLevel == 0, "Spirit Pull cannot be bought away from a Wisdom Tree");
            Teleport(game, tree.transform.position + Vector3.right * 2);
            Check(game.Purchase("pull") && game.pullLevel == 1 && forest.SharedInventory == 0, "Buying Spirit Pull at a tree spends exactly 30 particles");
            string save = game.ExportSave(); game.pullLevel = 0;
            Check(game.ImportSave(save) && game.pullLevel == 1, "The Spirit Pull level survives save and load");
            var invalid = JsonUtility.FromJson<GameManager.SaveData>(save); invalid.pullLevel = 4;
            Check(!game.ImportSave(JsonUtility.ToJson(invalid)) && game.pullLevel == 1, "A Spirit Pull level past the cap is rejected");
            game.pullLevel = 3; forest.State.inventory = 100000;
            Check(!game.Purchase("pull") && game.pullLevel == 3 && forest.SharedInventory == 100000, "The three-level cap refuses a fourth Spirit Pull purchase");
            game.pullLevel = 1;

            // From the island-1 west edge, half the 10 m circle is open void.
            var dots = forest.Dots; int oldSeed = forest.State.seed;
            var bounds = HubBounds("island-1");
            float rowZ = LightingBridgeGrid.Cell(0, Mathf.RoundToInt(bounds.center.z / LightingBridgeGrid.CellSize)).z;
            var at = new Vector3(bounds.min.x + .22f, .025f, rowZ);
            float radius = LightingPull.Radius(1) * LightingBridgeGrid.CellSize;
            var middle = LightingBridgeGrid.CellOf(at);
            Func<Vector2Int, float> distance = c => { var p = LightingDots.Position(c); return new Vector2(p.x - at.x, p.z - at.z).magnitude; };
            var near = new System.Collections.Generic.List<Vector2Int>(); var outside = new System.Collections.Generic.List<Vector2Int>();
            try
            {
                for (int i = 0; i < 1000; i++)
                {
                    near.Clear(); outside.Clear();
                    for (int x = -8; x <= 8; x++)
                        for (int z = -8; z <= 8; z++)
                        {
                            var c = new Vector2Int(middle.x + x, middle.y + z);
                            if (!dots.IsDot(c)) continue;
                            if (distance(c) <= radius) near.Add(c); else if (distance(c) <= radius + 4) outside.Add(c);
                        }
                    if (near.Count(c => game.BlockUnder(LightingDots.Position(c)) == null) >= 3 && outside.Count > 0) break;
                    forest.State.seed++;
                }
                Check(near.Count(c => game.BlockUnder(LightingDots.Position(c)) == null) >= 3 && outside.Count > 0,
                    "Fixture: at least 3 dots over the open void within 5 tiles, and one just beyond");
                Teleport(game, at); dots.Refresh(); pull.CooldownLeft = 0;
                int perDot = ForestWorld.ParticleUnitsPerParticle + Mathf.Clamp(game.soulGainLevel, 0, 8), walletBefore = forest.SharedInventory;
                Check(pull.TryPull(), "Spirit Pull fires with dots in range");
                Check(forest.SharedInventory - walletBefore == near.Count * perDot && near.All(c => !dots.IsDot(c) && forest.State.dots.Any(d => d.x == c.x && d.z == c.y)),
                    "Spirit Pull takes every dot within 5 tiles, over the void too (" + near.Count + " dots, wallet +" + (forest.SharedInventory - walletBefore) + ")");
                Check(outside.All(c => dots.IsDot(c)), "Dots beyond 5 tiles stay (" + outside.Count + ")");
                Check(dots.Flying && !dots.ShownCells.Any(c => near.Contains(c)), "Pulled dots leave the field and fly to the player");
                for (int i = 0; i < 30; i++) dots.Tick(1f / 60, false);
                Check(!dots.Flying, "Every pulled dot lands within " + LightingDots.PullSeconds + " s");
                int walletAfter = forest.SharedInventory;
                Check(!pull.TryPull() && forest.SharedInventory == walletAfter && pull.CooldownLeft > LightingPull.Cooldown - .1f,
                    "A second pull inside the " + LightingPull.Cooldown + "-second cooldown is refused");
                pull.CooldownLeft = 0;
                Check(!pull.TryPull() && pull.CooldownLeft == 0 && forest.SharedInventory == walletAfter, "A pull with no dots in range is refused and costs no cooldown");
            }
            finally
            {
                forest.State.seed = oldSeed; dots.ResetField();
                for (int i = 0; i < 30; i++) dots.Tick(1f / 60, false);
                game.pullLevel = oldLevel; pull.CooldownLeft = 0;
            }
        }

        // ---- Far Sight (passive) and Daybreak (V): sight grows by chunks; past the streamed world the
        // tiles and dots are drawn from data ----
        static void CheckSightAndReveal(GameManager game, ForestWorld forest)
        {
            var fog = game.GetComponent<LightingFog>(); var reveal = game.GetComponent<LightingReveal>(); var dots = forest.Dots;
            Check(fog != null && reveal != null && dots != null, "Daybreak rides on the Lighting GameManager beside its fog");
            var tree = forest.WisdomTrees.First(t => t != null);
            int oldSight = game.sightLevel, oldReveal = game.revealLevel, oldInventory = forest.State.inventory;
            var baseline = Cells(game);
            var lateUpdate = typeof(LightingFog).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                game.sightLevel = 0; game.revealLevel = 0;
                Check(Math.Abs(game.Cost("sight") - 20) < .001 && game.Cap("sight") == 4 && Math.Abs(game.Cost("reveal") - 35) < .001 && game.Cap("reveal") == 4,
                    "Far Sight costs 20 particles and Daybreak 35, four levels each");
                forest.State.inventory = 55 * ForestWorld.ParticleUnitsPerParticle;
                Teleport(game, tree.transform.position + Vector3.right * 8);
                Check(!game.Purchase("sight") && game.sightLevel == 0, "Far Sight cannot be bought away from a Wisdom Tree");
                Teleport(game, tree.transform.position + Vector3.right * 2);
                Check(game.Purchase("sight") && game.Purchase("reveal") && game.sightLevel == 1 && game.revealLevel == 1 && forest.SharedInventory == 0,
                    "Buying Far Sight and Daybreak at a tree spends exactly 55 particles");
                string save = game.ExportSave(); game.sightLevel = game.revealLevel = 0;
                Check(game.ImportSave(save) && game.sightLevel == 1 && game.revealLevel == 1, "Both levels survive save and load");
                var invalid = JsonUtility.FromJson<GameManager.SaveData>(save); invalid.sightLevel = 5;
                Check(!game.ImportSave(JsonUtility.ToJson(invalid)) && game.sightLevel == 1, "A Far Sight level past the cap is rejected");
                invalid.sightLevel = 1; invalid.revealLevel = 5;
                Check(!game.ImportSave(JsonUtility.ToJson(invalid)) && game.revealLevel == 1, "A Daybreak level past the cap is rejected");

                game.sightLevel = 0; var (clear0, black0) = fog.World(game);
                game.sightLevel = 4; var (clear4, black4) = fog.World(game);
                Check(clear0 == 12 && black0 == 24 && Mathf.Approximately(clear4, 28) && Mathf.Approximately(black4, 56),
                    "Sight is 12/24 m at level 0 and 28/56 m at Far Sight 4, one chunk per level");

                // Daybreak: rise, hold, fall, cooldown.
                game.sightLevel = 0; game.revealLevel = 0; reveal.CooldownLeft = 0;
                Check(!reveal.TryReveal() && !reveal.Active, "Daybreak needs a level");
                game.revealLevel = 1;
                Check(reveal.TryReveal() && reveal.Active, "V lifts the fog");
                for (int i = 0; i < 30; i++) reveal.Step(1f / 60);
                var (clearLifted, blackLifted) = fog.World(game);
                Check(LightingReveal.Reach == 128 && Mathf.Approximately(blackLifted, 128) && Mathf.Approximately(clearLifted, 120),
                    "Lifted, the fog sits at 16 chunks: clear to 120 m, black at 128 m");
                Check(!reveal.TryReveal(), "A second cast while the fog is lifted is refused");
                float held = 0; while (reveal.Active && held < 10) { reveal.Step(1f / 60); held += 1f / 60; }
                Check(!reveal.Active && held > 2.1f && held < 2.3f && reveal.CooldownLeft == LightingReveal.Cooldown && fog.World(game).black == 24,
                    "Level 1 holds the fog back 1.5 s, it returns over 0.8 s and the 30 s cooldown starts (" + held.ToString("0.00") + " s after the rise)");
                Check(!reveal.TryReveal() && !reveal.Active, "Daybreak inside its cooldown is refused");
                reveal.Step(LightingReveal.Cooldown);
                Check(reveal.CooldownLeft == 0 && reveal.TryReveal(), "After 30 s it casts again");
                while (reveal.Active) reveal.Step(.1f);
                reveal.CooldownLeft = 0;

                // Far tiles: a streamed tile's renderers are where its far copy draws.
                var cell0 = game.Tiles.Cells.First();
                Teleport(game, LightingBridgeGrid.Cell(cell0.x, cell0.y));
                var live = game.Tiles.Visual(cell0);
                var renderers = live.GetComponent<BlockArt>().placedVisual.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled).ToArray();
                bool Close(Matrix4x4 a, Matrix4x4 b) { for (int i = 0; i < 16; i++) if (Math.Abs(a[i] - b[i]) > 1e-3f) return false; return true; }
                Check(renderers.Length > 0 && renderers.Length == game.Tiles.FarParts(live.tier) && Enumerable.Range(0, renderers.Length).All(k => Close(game.Tiles.FarPose(cell0, live.tier, k), renderers[k].localToWorldMatrix)),
                    "A far tile draws its " + renderers.Length + " parts exactly where a streamed tile's own renderers are");

                // A data-only block 60-100 m out over the void (the side with the least platform): no chunk
                // of it loads, and only a lifted fog draws it.
                var at = game.player.transform.position; var origin = LightingBridgeGrid.CellOf(at);
                DrawFarAll(game, at, LightingReveal.Reach); int farBefore = game.Tiles.FarCount;
                int visuals = game.Tiles.VisualCount, created = game.Tiles.Created;
                System.Collections.Generic.List<Vector2Int> Block(Vector2Int along)
                {
                    var side = new Vector2Int(along.y, along.x); var open = new System.Collections.Generic.List<Vector2Int>();
                    for (int a = 30; a < 50; a++) for (int s = -5; s < 5; s++)
                    {
                        var c = origin + along * a + side * s;
                        if (!LightingBridgeGrid.OverlapsPlatform(c) && !game.Tiles.Contains(c)) open.Add(c);
                    }
                    return open;
                }
                var fixture = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down }.Select(Block).OrderByDescending(b => b.Count).First();
                foreach (var c in fixture) game.Tiles.Add(c);
                Check(fixture.Count > 100 && game.Tiles.VisualCount == visuals && game.Tiles.Created == created, "Fixture: " + fixture.Count + " tiles 60-100 m out, stored without GameObjects");
                DrawFarAll(game, at, 24);
                Check(game.Tiles.FarCount == 0, "Under the normal fog nothing past the streamed chunks is drawn");
                DrawFarAll(game, at, LightingReveal.Reach);
                Check(game.Tiles.FarCount - farBefore == fixture.Count && game.Tiles.VisualCount == visuals && game.Tiles.Created == created,
                    "Lifted, every far tile is drawn from data (" + game.Tiles.FarCount + " instanced) with no new GameObject");
                RestoreTiles(game, baseline);

                // Far dots: past the near pool, only once sight grows past its base.
                dots.Refresh();
                game.sightLevel = 0; lateUpdate.Invoke(fog, null);
                Check(!dots.SkipAppear, "At base sight the near pool keeps its scale-in");
                game.sightLevel = 4; lateUpdate.Invoke(fog, null);
                var shown = new System.Collections.Generic.HashSet<Vector2Int>(dots.ShownCells);
                Func<Vector2Int, float> flat = c => { var p = LightingDots.Position(c); return new Vector2(p.x - at.x, p.z - at.z).magnitude; };
                at = game.player.transform.position;
                Check(dots.SkipAppear && dots.FarCount > shown.Count && !dots.FarCells.Any(shown.Contains) && dots.FarCells.All(c => dots.IsDot(c) && flat(c) <= 56.01f),
                    "At Far Sight 4 the " + dots.FarCount + " dots past the near pool's " + shown.Count + " out to 56 m are drawn from data");
            }
            finally
            {
                RestoreTiles(game, baseline);
                while (reveal.Active) reveal.Step(.1f);
                game.sightLevel = oldSight; game.revealLevel = oldReveal; reveal.CooldownLeft = 0; forest.State.inventory = oldInventory;
                lateUpdate.Invoke(fog, null);
            }
        }

        // ---- Ability bar and skill badges: game-icons.net icons, a richer frame per level, a tree-shaped tree ----
        static void CheckAbilityHud(GameManager game)
        {
            var hud = GameHUD.Current; var bar = hud.Abilities;
            var atmosphere = UnityEngine.Object.FindAnyObjectByType<PlayerAtmosphere>(); var orb = atmosphere != null && atmosphere.soul != null ? atmosphere.soul.GetComponent<Renderer>() : null;
            Check(orb != null && !orb.enabled && atmosphere.halo != null && !atmosphere.halo.enabled && game.soulLight.enabled && game.soulLight.range > 0,
                "No soul orb or halo floats beside the player; its light still shines");
            Check(bar != null && string.Join("/", bar.Slots.Select(s => s.id)) == "dash/grenade/pull/reveal", "The ability bar holds the active skills: Dash, Light Grenade, Spirit Pull and Daybreak");
            game.Notify("CHECK NOTICE"); hud.RefreshUI();
            var hidden = hud.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Fragment Calls" || t.name == "Interaction Hint" || t.name == "Action Feedback").ToArray();
            Check(hidden.Length == 3 && hidden.All(t => !t.gameObject.activeSelf) && game.Notice == "CHECK NOTICE",
                "Lighting draws no resources panel, hint box or notice box; a notice is still recorded");
            Check(bar.Slots.All(s => s.badge.Icon.sprite != null && s.badge.Icon.sprite.texture.name == SkillBadge.IconName(s.id) && s.padlock.sprite.texture.name == "padlock"),
                "Every ability shows its game-icons.net icon and padlock from Resources/UI/Icons");
            Check(SkillBadge.Tier(0, 3) == 0 && SkillBadge.Tier(1, 3) == 1 && SkillBadge.Tier(2, 3) == 2 && SkillBadge.Tier(3, 3) == 3
                && string.Join(",", Enumerable.Range(1, 5).Select(l => SkillBadge.Tier(l, 5))) == "1,1,2,2,3", "Badge tiers run locked, bronze, silver, gold at max (Dash spreads its five levels)");
            var grenade = game.GetComponent<LightingGrenade>(); var pull = game.GetComponent<LightingPull>(); var reveal = game.GetComponent<LightingReveal>();
            var charge = typeof(LightingGrenade).GetField("charge", BindingFlags.Instance | BindingFlags.NonPublic);
            int oldDash = game.dashLevel, oldGrenade = game.grenadeLevel, oldPull = game.pullLevel, oldReveal = game.revealLevel;
            try
            {
                game.dashLevel = 0; game.grenadeLevel = 1; game.pullLevel = 3; pull.CooldownLeft = 0; bar.Refresh(0);
                var dash = bar.Slots[0]; var throwSlot = bar.Slots[1]; var pullSlot = bar.Slots[2];
                Check(dash.padlock.enabled && dash.badge.Icon.color.a < .6f && !throwSlot.padlock.enabled && throwSlot.badge.Icon.color.a > .99f,
                    "A locked ability is grey with a padlock; a learned one is lit without it");
                Check(throwSlot.ring.color == SkillBadge.Bronze && !throwSlot.badge.Outer.enabled && pullSlot.ring.color == SkillBadge.Gold && pullSlot.badge.Outer.enabled,
                    "Level 1 wears a bronze frame, max level a gold double ring");
                pull.CooldownLeft = LightingPull.Cooldown * .5f; charge.SetValue(grenade, .5f); bar.Refresh(0);
                Check(Math.Abs(pullSlot.cooldown.fillAmount - .5f) < .01f && Math.Abs(throwSlot.charge.fillAmount - .5f) < .01f,
                    "The Spirit Pull slot sweeps dark for its remaining cooldown and the grenade slot rings its charge");
                var revealSlot = bar.Slots[3]; game.revealLevel = 1; reveal.CooldownLeft = 0;
                reveal.TryReveal(); for (int i = 0; i < 30; i++) reveal.Step(1f / 60); bar.Refresh(0);
                Check(revealSlot.charge.fillAmount > .9f && revealSlot.charge.fillAmount < 1, "The Daybreak slot rings the time the fog stays lifted");
                while (reveal.Active) reveal.Step(.1f); reveal.CooldownLeft = LightingReveal.Cooldown * .5f; bar.Refresh(0);
                Check(Math.Abs(revealSlot.cooldown.fillAmount - .5f) < .01f && revealSlot.charge.fillAmount == 0, "Then it sweeps dark for its cooldown");

                hud.SetView(GameHUD.ScreenView.Skills);
                var tree = UnityEngine.Object.FindAnyObjectByType<SkillTree>(FindObjectsInactive.Include);
                for (int i = 0; i < 200; i++) tree.Refresh();
                var branches = tree.Branches.ToArray();
                Check(branches.Length == 7 && branches.All(b => b.bark != null && b.sap != null && b.bark.Grown == 1 && b.bark.name.StartsWith("Star line")
                    && (b.bark.control - (b.bark.a + b.bark.b) * .5f).sqrMagnitude < .01f) && tree.RootCount == 0,
                    "The Lighting skill screen is a star chart: 7 straight star lines with starlight over them, no roots");
                Check(branches.Where(b => b.sap.name.StartsWith("Sap root")).All(b => b.sap.Grown > .99f && (b.sap.Point(1) - b.bark.b).sqrMagnitude < .01f),
                    "Starlight grows the whole way along the lines from the always-lit pole star");
                var images = tree.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                float Flare(string id) => images.Where(i => i.name == "Star flare " + id).Max(i => i.color.a);
                Check(images.Count(i => i.name == "Sky star") == 90 && images.Any(i => i.name == "Night sky"), "A night sky with 90 twinkling stars sits behind it");
                Check(Flare("dash") == 0 && Flare("grenade") > .3f && Flare("root") > .5f, "An unlearned skill's star has no flare; a learned one and the pole star shine");
                Check(tree.GetComponentsInChildren<UnityEngine.UI.Image>(true).Count(i => i.name == "Badge icon") == 7, "Every Lighting skill node shows its icon");
                var discs = tree.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i => i.name == "Disc" && i.transform.parent.name != "Power root").ToArray();
                var passive = new[] { "Power speed", "Power soulgain", "Power sight" };
                Check(discs.Length == 7 && discs.All(d => (d.sprite.texture.name == "Skill tree square") == passive.Contains(d.transform.parent.name)),
                    "Passive skills (Swift Stride, Particle Yield, Far Sight) wear a square frame, active ones a round one");
            }
            finally
            {
                hud.SetView(GameHUD.ScreenView.Playing); charge.SetValue(grenade, -1f);
                game.dashLevel = oldDash; game.grenadeLevel = oldGrenade; game.pullLevel = oldPull; pull.CooldownLeft = 0;
                while (reveal.Active) reveal.Step(.1f); game.revealLevel = oldReveal; reveal.CooldownLeft = 0;
            }
        }

        static int CrystalVoices() => UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
            .Count(s => s.name.StartsWith("Forest sound") && s.isPlaying && s.clip != null && s.clip.name == "crystal");

        // Every clip the Lighting runtime loads exists, the mixer routes, exactly one listener hears
        // it, and each gameplay cue actually starts a voice.
        static void CheckSounds(GameManager game)
        {
            string[] paths = { "Audio/Forest/wind", "Audio/Forest/leaves", "Audio/Forest/collect", "Audio/Forest/charge", "Audio/Forest/release", "Audio/Forest/crystal",
                "Audio/SFX/PasosNotas/04LB A#",
                "Audio/LightingSteps/soft_step_01", "Audio/LightingSteps/soft_step_02", "Audio/LightingSteps/soft_step_03", "Audio/LightingSteps/soft_step_04",
                "Audio/LightingSteps/soft_step_05", "Audio/LightingSteps/soft_step_06", "Audio/LightingSteps/soft_step_07", "Audio/LightingSteps/soft_step_08",
                "Audio/LightingPickups/pickup_01", "Audio/LightingPickups/pickup_02", "Audio/LightingPickups/pickup_03", "Audio/LightingPickups/pickup_04",
                "Audio/Music/IntroGusta", "Audio/SFX/Bloques" };
            var missing = paths.Where(p => Resources.Load<AudioClip>(p) == null).ToArray();
            Check(missing.Length == 0, "Every Lighting sound clip loads" + (missing.Length > 0 ? " (missing: " + string.Join(", ", missing) + ")" : ""));
            Check(paths.Where(p => p.Contains("soft_step")).All(p => Resources.Load<AudioClip>(p).channels == 2), "The ASMR footsteps are stereo");
            var mixer = Resources.Load<UnityEngine.Audio.AudioMixer>("Forest/ForestMix");
            Check(mixer != null && new[] { "Master", "Music", "Ambience", "Effects", "Wildlife", "Magic", "UI" }.All(g => mixer.FindMatchingGroups(g).Length > 0),
                "The Forest mixer has the Master/Music/Ambience/Effects/Wildlife/Magic/UI groups");
            int listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled);
            Check(listeners == 1, "Exactly one AudioListener is live (" + listeners + ")");
            var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            var music = sources.FirstOrDefault(s => s.name == "Music");
            Check(music != null && music.isPlaying && music.clip != null && music.clip.name == "IntroGusta" && music.outputAudioMixerGroup != null,
                "Music plays IntroGusta through the mixer");
            var wind = sources.FirstOrDefault(s => s.name == "wind ambience");
            Check(wind != null && wind.isPlaying && wind.loop, "The wind ambience loop plays");

            ForestSoundscape.Play("crystal", game.player.transform.position, .12f);
            Check(CrystalVoices() >= 1, "A placed tile's crystal cue starts a world voice");
            // One note per floor cell entered (the reference's platform_step), driven by BridgeSteps.
            var bridgeSteps = game.GetComponent<BridgeSteps>();
            var stepUpdate = typeof(BridgeSteps).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            var stepCooldown = typeof(BridgeSteps).GetField("nextNote", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(bridgeSteps != null && stepUpdate != null && stepCooldown != null, "Lighting has the floor-cell step note driver");
            AudioSource StepVoice() => game.player.GetComponents<AudioSource>().FirstOrDefault(s => s.clip != null && s.clip.name == "04LB A#");
            void StepOnto(Vector3 p) { Teleport(game, p); stepCooldown.SetValue(bridgeSteps, 0f); stepUpdate.Invoke(bridgeSteps, null); }
            var hubCell = new Vector3(0, .025f, 4); var nextHubCell = hubCell + Vector3.right * LightingBridgeGrid.CellSize;
            Check(PlatformSupport.At(hubCell) != null && PlatformSupport.At(nextHubCell) != null, "Two neighbouring hub cells to step across");
            StepOnto(hubCell); StepVoice()?.Stop();
            StepOnto(nextHubCell);
            Check(StepVoice() != null && StepVoice().isPlaying, "Stepping onto a new hub cell plays the 04LB A# note");
            StepVoice().Stop(); StepOnto(nextHubCell + Vector3.forward * .4f);
            Check(!StepVoice().isPlaying, "Moving within the same cell plays no second note");
            Check(!game.player.GetComponents<AudioSource>().Any(s => s.clip != null && s.clip.name.StartsWith("step_")), "No legacy forest step clip is loaded in Lighting");
            // The note is a little quieter now; a soft ASMR step sounds per stride on top of it, never during a dash.
            float sliders = ForestSoundscape.Volume("Master") * ForestSoundscape.Volume("Effects") * ForestSoundscape.Volume("Footsteps");
            StepOnto(hubCell); // back across: a new cell, so a note
            var feet = game.player.GetComponent<LightingFootsteps>();
            Check(feet != null && StepVoice().isPlaying && Mathf.Abs(StepVoice().volume - LightingFootsteps.NoteVolume * sliders) < .001f && LightingFootsteps.NoteVolume < .5f,
                "The floor note plays at " + LightingFootsteps.NoteVolume + " of the sliders, down from .5");
            var playFootstep = typeof(BoxPlayer).GetMethod("PlayFootstep", BindingFlags.NonPublic | BindingFlags.Instance);
            var dashTime = typeof(BoxPlayer).GetField("dashTimeRemaining", BindingFlags.NonPublic | BindingFlags.Instance);
            int strides = feet.StrideCount; playFootstep.Invoke(game.player, null); string first = feet.LastStride; playFootstep.Invoke(game.player, null); string second = feet.LastStride;
            int Foot(string clip) => int.Parse(clip.Substring(clip.Length - 2)) % 2;
            Check(feet.StrideCount == strides + 2 && first.StartsWith("soft_step_") && second.StartsWith("soft_step_") && Foot(first) != Foot(second) && StepVoice().isPlaying,
                "Each stride plays a soft ASMR step, feet alternating (" + first + ", " + second + "), without cutting off the floor note");
            dashTime.SetValue(game.player, 1f); playFootstep.Invoke(game.player, null); dashTime.SetValue(game.player, 0f);
            Check(feet.StrideCount == strides + 2, "A dash makes no footsteps");
            StepVoice().Stop(); feet.Stride.Stop();
            var pickup = game.GetComponent<LightingPickupAudio>();
            int played = pickup != null ? pickup.PlaybackCount : -1;
            if (pickup != null) pickup.PlayPickup();
            Check(pickup != null && pickup.PlaybackCount == played + 1 && pickup.GetComponentsInChildren<AudioSource>().Any(s => s.name.StartsWith("Particle pickup voice") && s.isPlaying),
                "A particle pickup plays its chime");

            // A device change that Unity fails to open is retried by ForestSoundscape.RecoverAudio: the
            // loops keep playing, the mixer keeps its levels, and cues still start voices.
            var soundscape = game.GetComponent<ForestSoundscape>();
            Check(soundscape != null, "The Lighting GameManager carries the soundscape that recovers audio");
            soundscape.RecoverAudio();
            Check(music.isPlaying && wind.isPlaying, "Music and the wind loop play again after an audio device reset");
            Check(mixer.GetFloat("Master", out float masterDb) && Mathf.Abs(masterDb - Mathf.Log10(Mathf.Max(.001f, ForestSoundscape.Volume("Master"))) * 20) < .1f,
                "The mixer keeps the saved Master level after a reset (" + masterDb.ToString("0.0") + " dB)");
            ForestSoundscape.Play("crystal", game.player.transform.position, .12f);
            Check(CrystalVoices() >= 1, "A cue starts a voice after the reset");
        }

        // A big network must not make Dash, respawn or drawing stutter. About 2000 connected tiles
        // east of island-1 laid as a comb (every other row plus a spine), so every tile borders the
        // void: the widest frontier a network of that size can have. Then a level-5 dash west into
        // the void, timed frame by frame at 60 FPS steps.
        static void CheckDashPerformance(GameManager game, ForestWorld forest)
        {
            var bounds = HubBounds("island-1");
            int x0 = HubEdges("island-1").Where(c => LightingBridgeGrid.Cell(c.x, c.y).x > bounds.max.x).Min(c => c.x);
            int zc = Mathf.RoundToInt(bounds.center.z / LightingBridgeGrid.CellSize);
            var fixture = new System.Collections.Generic.List<Vector2Int>();
            var baseline = Cells(game);
            int oldDash = game.dashLevel;
            Quaternion oldCameraRotation = game.player.viewCamera != null ? game.player.viewCamera.transform.rotation : Quaternion.identity;
            float oldDistance = game.cameraRig.distance;
            var fog = game.GetComponent<LightingFog>(); float black = fog.worldBlackDistance;
            try
            {
                for (int x = x0; x < x0 + 50; x++) for (int z = zc - 40; z < zc + 40; z++)
                {
                    if (x != x0 && (z - zc) % 2 != 0) continue;
                    var cell = new Vector2Int(x, z);
                    if (LightingBridgeGrid.OverlapsPlatform(cell) || LightingBridgeGrid.IsOccupied(game, cell)) continue;
                    game.Tiles.Add(cell); fixture.Add(cell);
                }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                game.Recalculate(); double recalculateMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart(); string bigSave = game.ExportSave(); double exportMs = watch.Elapsed.TotalMilliseconds;
                int connected = fixture.Count(game.Tiles.Contains);
                Check(connected >= 1800 && LightingBridgeGrid.ConnectedCells(game.Tiles.Cells).Count == game.Tiles.Count, "Performance fixture holds " + connected + " connected tiles");
                // Nothing changed: a full Recalculate (start, load) repaints no tile.
                watch.Restart(); game.Recalculate(); double repaintMs = watch.Elapsed.TotalMilliseconds;
                Check(repaintMs < 1000.0 / 60, "A full Recalculate with nothing changed fits a 60 FPS frame at " + connected + " tiles (" + repaintMs.ToString("0.0") + " ms)");
                watch.Restart(); bool reloaded = game.ImportSave(bigSave); double reloadMs = watch.Elapsed.TotalMilliseconds;
                Check(reloaded && fixture.All(game.Tiles.Contains), "The " + connected + "-tile save reloads every tile");

                // Dots restream on every cell the player enters; the network size must not matter.
                double restreamMs = -1;
                if (forest.Dots != null) { forest.Dots.Refresh(); watch.Restart(); forest.Dots.Refresh(); restreamMs = watch.Elapsed.TotalMilliseconds; } // the first call pays the JIT

                // Zoomed all the way out over the middle of the comb: CPU cost of one frame's draw
                // submission, with the zero-intensity directional light casting shadows (the old scene
                // state) and without.
                var middle = fixture[fixture.Count / 2];
                Teleport(game, LightingBridgeGrid.Cell(middle.x, middle.y) + Vector3.up * .205f);
                var shown = fixture.Select(game.Tiles.Visual).Where(b => b != null).ToArray();
                Check(shown.Length > 0 && shown.Length < fixture.Count, "Only the comb's chunks near the player have GameObjects (" + shown.Length + " of " + fixture.Count + ")");
                var camera = game.cameraRig.GetComponent<Camera>();
                game.cameraRig.distance = 42; game.cameraRig.CenterOnPlayer(); game.cameraRig.SendMessage("Apply", 1f);
                var sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
                var sunShadows = sun.shadows;
                Check(sunShadows == LightShadows.None, "The zero-intensity Lighting sun casts no shadows");
                // Old state: sun shadows on, every loaded tile drawn. Then each fix on its own.
                fog.worldBlackDistance = 1e6f; fog.CullTiles(game, true);
                sun.shadows = LightShadows.Soft; double shadowedMs = RenderMs(camera);
                sun.shadows = sunShadows; double unshadowedMs = RenderMs(camera);
                fog.worldBlackDistance = black; fog.CullTiles(game, true); double culledMs = RenderMs(camera);
                int drawn = shown.Count(b => !b.culled);
                Vector3 eye = game.player.transform.position; float Flat(BridgeBlock b) => new Vector2(b.transform.position.x - eye.x, b.transform.position.z - eye.z).magnitude;
                Check(shown.All(b => Flat(b) > black - 1.5f || !b.culled && (b.renderers == null || b.renderers.All(r => r == null || !r.forceRenderingOff))),
                    "Every tile inside the " + black + " m fog stays drawn");
                Check(shown.All(b => Flat(b) <= black + 3.5f || b.culled && b.renderers.All(r => r == null || r.forceRenderingOff)),
                    "Every loaded tile past the fog stops drawing (" + drawn + " of " + shown.Length + " loaded comb tiles drawn)");

                game.dashLevel = BoxPlayer.DashTiles.Length; game.player.ResetDash();
                Teleport(game, new Vector3(bounds.min.x + .22f, .025f, bounds.center.z));
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                Physics.SyncTransforms();
                watch.Restart(); LightingBridgeGrid.ConnectedCells(game.Tiles.Cells); double floodMs = watch.Elapsed.TotalMilliseconds;
                // Earlier checks placed tiles in this same frame; only this dash's crystals count.
                foreach (var voice in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) if (voice.name.StartsWith("Forest sound")) voice.Stop();
                int before = game.Tiles.Count;
                Check(game.player.TryStartDash(Vector2.up), "Level-5 Dash starts beside the big network");
                double worst = 0, total = 0; int frames = 0;
                while (game.player.IsDashing && frames < 120)
                {
                    watch.Restart(); game.player.StepMovement(Vector2.zero, 1f / 60); Physics.SyncTransforms();
                    double ms = watch.Elapsed.TotalMilliseconds; worst = Math.Max(worst, ms); total += ms; frames++;
                }
                int laid = game.Tiles.Count - before;
                // A level-3 Light Grenade (radius 7) into the comb's open rows beside the spine column.
                Teleport(game, LightingBridgeGrid.Cell(x0, zc));
                var blast = game.GrenadeBlast(new Vector2Int(x0 + 10, zc), LightingGrenade.GrenadeTiles[LightingGrenade.GrenadeTiles.Length - 1]);
                int blastLaid = 0, blastFrames = 0; double blastWorst = 0, blastTotal = 0; var blastMs = new System.Collections.Generic.List<string>();
                while (true)
                {
                    watch.Restart(); bool more = blast.MoveNext(); double ms = watch.Elapsed.TotalMilliseconds;
                    blastWorst = Math.Max(blastWorst, ms); blastTotal += ms; blastMs.Add(ms.ToString("0.0")); if (!more) break; blastLaid = blast.Current; blastFrames++;
                }
                report.AppendLine("PERF: grenade blast laid " + blastLaid + " tiles over " + blastFrames + " frames, worst frame " + blastWorst.ToString("0.0") + " ms, total " + blastTotal.ToString("0.0") + " ms (the one save at the end excluded; ExportSave below)");
                Check(blastLaid >= 40 && blastWorst < 1000.0 / 60, "A level-3 Light Grenade beside a " + connected + "-tile network lays " + blastLaid + " tiles, every frame under 60 FPS (" + blastWorst.ToString("0.0") + " ms worst; frames " + string.Join("/", blastMs) + ")");
                report.AppendLine("PERF: dot restream " + restreamMs.ToString("0.00") + " ms; zoom-42 render CPU "
                    + shadowedMs.ToString("0.0") + " ms before (sun shadows, all loaded tiles), " + unshadowedMs.ToString("0.0") + " ms without shadows, " + culledMs.ToString("0.0") + " ms with the fog cull too; "
                    + drawn + " of " + shown.Length + " loaded comb tiles drawn");
                Check(forest.Dots == null || restreamMs < 2, "A dot restream beside a " + connected + "-tile comb stays under 2 ms (" + restreamMs.ToString("0.00") + " ms)");
                report.AppendLine("PERF: " + connected + " tiles, first paint " + recalculateMs.ToString("0.0") + " ms, unchanged Recalculate " + repaintMs.ToString("0.0") + " ms, reload " + reloadMs.ToString("0.0") + " ms, ExportSave " + exportMs.ToString("0.0")
                    + " ms, network flood " + floodMs.ToString("0.0") + " ms, dash laid " + laid + " tiles over " + frames + " frames, worst frame " + worst.ToString("0.0") + " ms, total " + total.ToString("0.0") + " ms");
                Check(CrystalVoices() <= 4, "A 12-tile Dash plays at most a few crystal ticks, not one per tile (" + CrystalVoices() + " voices)");
                Check(laid >= 10, "Level-5 Dash lays its run beside a " + connected + "-tile network (" + laid + " laid)");
                Check(worst < 1000.0 / 60, "The worst Dash frame over a " + connected + "-tile network fits a 60 FPS frame (" + worst.ToString("0.0") + " ms)");
            }
            finally
            {
                RestoreTiles(game, baseline);
                game.dashLevel = oldDash; game.player.ResetDash();
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = oldCameraRotation;
                game.cameraRig.distance = oldDistance; fog.worldBlackDistance = black;
                Physics.SyncTransforms();
            }
        }

        // No tile cap: a 100,000-tile network west of island-1 keeps Dash and the grenade laying tiles,
        // keeps GameObjects to the chunks near the player (pooled on the way back), keeps every frame
        // under 60 FPS, and saves as cells (version 6). An older version-5 save moves its placed blocks over.
        static void CheckUnboundedWorld(GameManager game, ForestWorld forest)
        {
            string baselineSave = game.ExportSave(); var baseline = Cells(game);
            int oldDash = game.dashLevel;
            Quaternion oldCameraRotation = game.player.viewCamera != null ? game.player.viewCamera.transform.rotation : Quaternion.identity;
            try
            {
                // Version 5 kept every placed tile as a block with an id and a position.
                var old = JsonUtility.FromJson<GameManager.SaveData>(baselineSave);
                old.version = 5; var recoloured = baseline.First();
                int oddTier = (LightingBridgeGrid.RequiredTier(recoloured.x, recoloured.y) + 1) % BridgeBlock.TierCount, overrides = 0;
                foreach (var c in baseline)
                {
                    int tier = c == recoloured ? oddTier : GameManager.RequiredTier(0, c.x, c.y), hashed = LightingBridgeGrid.RequiredTier(c.x, c.y);
                    if (tier != hashed) overrides++;
                    old.blocks.Add(new GameManager.BlockSave { id = "v5-" + c.x + "_" + c.y, tier = tier, cellTierOverride = tier != hashed ? tier : -1, placed = true, column = c.x, row = c.y, position = LightingBridgeGrid.Cell(c.x, c.y) });
                }
                old.tiles = null; old.tileTiers = null;
                Check(game.ImportSave(JsonUtility.ToJson(old)) && game.Tiles.Cells.SetEquals(baseline) && GameManager.RequiredTier(0, recoloured.x, recoloured.y) == oddTier,
                    "A version-5 save moves its " + baseline.Count + " placed blocks into the tile store, colour overrides included");
                var upgraded = JsonUtility.FromJson<GameManager.SaveData>(game.ExportSave());
                Check(upgraded.version == 6 && !upgraded.blocks.Any(b => b.placed) && upgraded.tiles.Length == 2 * baseline.Count && upgraded.tileTiers.Length == 3 * overrides,
                    "It saves back as version 6: cells plus its colour overrides");
                Check(game.ImportSave(baselineSave), "The current save reloads after the version-5 check");

                // The fixture: a 320 x 320 block of cells a short spine west of the island-1 edge.
                var bounds = HubBounds("island-1");
                var edge = HubEdges("island-1").Where(c => LightingBridgeGrid.Cell(c.x, c.y).x < bounds.min.x).OrderBy(c => Math.Abs(c.y - Mathf.RoundToInt(bounds.center.z / LightingBridgeGrid.CellSize))).First();
                Teleport(game, SupportedNear(game, edge));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int x = 0; x < 4; x++) game.Tiles.Add(edge + Vector2Int.left * x);
                int west = edge.x - 3 - 320;
                for (int x = edge.x - 4; x > west; x--) for (int z = edge.y - 160; z < edge.y + 160; z++)
                {
                    var c = new Vector2Int(x, z);
                    if (!LightingBridgeGrid.OverlapsPlatform(c)) game.Tiles.Add(c);
                }
                game.Recalculate(); double addMs = watch.Elapsed.TotalMilliseconds;
                int count = game.Tiles.Count;
                watch.Restart(); int linked = LightingBridgeGrid.ConnectedCells(game.Tiles.Cells).Count; double floodMs = watch.Elapsed.TotalMilliseconds;
                Check(count > 100000 && linked == count, "A " + count + "-tile network stays connected with no cap (" + addMs.ToString("0") + " ms to add, " + floodMs.ToString("0") + " ms to flood)");
                int visuals = game.Tiles.VisualCount;
                Check(visuals > 0 && visuals < 6400, "Only the chunks near the player have tile GameObjects (" + visuals + " of " + count + ")");

                // Far west side: the tile underfoot streams in with a collider, and the start's chunks go.
                var far = new Vector2Int(west + 1, edge.y);
                watch.Restart(); Teleport(game, LightingBridgeGrid.Cell(far.x, far.y)); double teleportMs = watch.Elapsed.TotalMilliseconds;
                var under = game.Tiles.Visual(far);
                bool hit = Physics.Raycast(game.player.transform.position + Vector3.up, Vector3.down, out var ground, 3f, ~0, QueryTriggerInteraction.Ignore);
                Check(under != null && hit && ground.collider.GetComponentInParent<BridgeBlock>() == under && game.Tiles.Visual(edge) == null && game.CanStand(game.player.transform.position),
                    "Teleporting 640 m west streams the tile underfoot in with its collider and unloads the start");
                int created = game.Tiles.Created;
                Teleport(game, SupportedNear(game, edge)); Teleport(game, LightingBridgeGrid.Cell(far.x, far.y));
                Check(game.Tiles.Created - created <= LightingTiles.ChunkSize * LightingTiles.ChunkSize,
                    "The round trip reuses pooled tiles (" + (game.Tiles.Created - created) + " new GameObjects)");

                // Walking 160 m east at 30 m/s streams chunks in and out; no frame may stall.
                double streamWorst = 0; var streamMs = new System.Collections.Generic.List<double>();
                for (int i = 0; i < 320; i++)
                {
                    var p = LightingBridgeGrid.Cell(far.x, far.y) + Vector3.right * (i * .5f);
                    Teleport(game, p, false);
                    watch.Restart(); game.Tiles.Stream(game.player.transform.position); double ms = watch.Elapsed.TotalMilliseconds;
                    streamWorst = Math.Max(streamWorst, ms); streamMs.Add(ms);
                }
                report.AppendLine("PERF: teleport 640 m (stream the whole radius) " + teleportMs.ToString("0") + " ms; walking stream frames over 2 ms: "
                    + string.Join("/", streamMs.Where(ms => ms > 2).Select(ms => ms.ToString("0.0"))));
                Check(streamWorst < 1000.0 / 60 && game.Tiles.VisualCount < 6400, "Streaming while walking across the network fits a 60 FPS frame (" + streamWorst.ToString("0.0") + " ms worst, " + game.Tiles.VisualCount + " loaded)");

                // Daybreak in the middle of it: every unloaded tile within 128 m drawn from data. The cast
                // builds each chunk's poses once; a step to the next chunk only copies them.
                var middle = game.player.transform.position;
                game.Tiles.DrawFar(middle, 0); // parts and JIT
                double farCastMs = 0; int castFrames = 0; var castMs = new System.Collections.Generic.List<double>();
                do { watch.Restart(); game.Tiles.DrawFar(middle, LightingReveal.Reach); castMs.Add(watch.Elapsed.TotalMilliseconds); farCastMs = Math.Max(farCastMs, castMs[castMs.Count - 1]); castFrames++; }
                while (game.Tiles.FarPending && castFrames < 100);
                watch.Restart(); game.Tiles.DrawFar(middle, LightingReveal.Reach); double farFrameMs = watch.Elapsed.TotalMilliseconds;
                var step = middle + Vector3.right * LightingTiles.ChunkSize * LightingBridgeGrid.CellSize;
                Teleport(game, step, false); game.Tiles.Stream(step, true);
                double farStepMs = 0; int stepFrames = 0;
                do { watch.Restart(); game.Tiles.DrawFar(step, LightingReveal.Reach); farStepMs = Math.Max(farStepMs, watch.Elapsed.TotalMilliseconds); stepFrames++; }
                while (game.Tiles.FarPending && stepFrames < 100);
                report.AppendLine("PERF: Daybreak over the dense network: " + game.Tiles.FarCount + " far tiles, cast frames " + string.Join("/", castMs.Select(ms => ms.ToString("0.0"))) + " ms, next chunk " + farStepMs.ToString("0.0") + " ms, steady frame " + farFrameMs.ToString("0.00") + " ms");
                Check(game.Tiles.FarCount > 5000 && farCastMs < 1000.0 / 60 && castFrames <= 24 && farStepMs < 8 && farFrameMs < 2,
                    "Daybreak draws " + game.Tiles.FarCount + " far tiles: the cast fills in over " + castFrames + " frames (worst " + farCastMs.ToString("0.0") + " ms, inside the 0.4 s rise), next chunk " + farStepMs.ToString("0.0") + " ms, steady " + farFrameMs.ToString("0.00") + " ms");

                // Dash west off the far edge, then a level-3 blast past it: both lay tiles at 100k.
                game.dashLevel = BoxPlayer.DashTiles.Length; game.player.ResetDash();
                Teleport(game, LightingBridgeGrid.Cell(far.x, far.y) + Vector3.left * .78f);
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                Physics.SyncTransforms();
                int before = game.Tiles.Count; double worst = 0; int frames = 0;
                Check(game.player.TryStartDash(Vector2.up), "Level-5 Dash starts at the edge of the 100,000-tile network");
                while (game.player.IsDashing && frames < 120)
                {
                    watch.Restart(); game.player.StepMovement(Vector2.zero, 1f / 60); Physics.SyncTransforms(); game.Tiles.Stream(game.player.transform.position);
                    worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds); frames++;
                }
                int laid = game.Tiles.Count - before;
                Check(laid >= 10 && worst < 1000.0 / 60, "Dash lays " + laid + " tiles past " + before + ", worst frame " + worst.ToString("0.0") + " ms");
                Teleport(game, LightingBridgeGrid.Cell(far.x, far.y));
                before = game.Tiles.Count; double blastWorst = 0; int blastLaid = 0;
                var blast = game.GrenadeBlast(far + new Vector2Int(-3, 9), LightingGrenade.GrenadeTiles[LightingGrenade.GrenadeTiles.Length - 1]);
                while (true)
                {
                    watch.Restart(); bool more = blast.MoveNext(); blastWorst = Math.Max(blastWorst, watch.Elapsed.TotalMilliseconds);
                    if (!more) break; blastLaid = blast.Current;
                }
                Check(blastLaid > 0 && game.Tiles.Count == before + blastLaid && blastWorst < 1000.0 / 60,
                    "A Light Grenade lays " + blastLaid + " tiles past " + before + ", worst frame " + blastWorst.ToString("0.0") + " ms");

                // Save and load at this size.
                bool island2 = game.Bridges[0], island3 = game.Bridges[1]; var cells = Cells(game);
                watch.Restart(); string big = game.ExportSave(); double exportMs = watch.Elapsed.TotalMilliseconds;
                var data = JsonUtility.FromJson<GameManager.SaveData>(big);
                Check(data.version == 6 && data.tiles.Length == 2 * cells.Count && !data.blocks.Any(b => b.placed), "The " + cells.Count + "-tile save lists every tile as a cell");
                watch.Restart(); bool reloaded = game.ImportSave(big); double importMs = watch.Elapsed.TotalMilliseconds;
                Check(reloaded && game.Tiles.Cells.SetEquals(cells) && game.Bridges[0] == island2 && game.Bridges[1] == island3 && game.Tiles.Visual(LightingBridgeGrid.CellOf(game.player.transform.position)) != null,
                    "It reloads the same network and streams the player's chunk in (" + exportMs.ToString("0") + " ms export, " + importMs.ToString("0") + " ms import)");
                report.AppendLine("PERF: " + cells.Count + " tiles: add " + addMs.ToString("0") + " ms, flood " + floodMs.ToString("0") + " ms, stream worst " + streamWorst.ToString("0.0") + " ms, dash worst " + worst.ToString("0.0")
                    + " ms, blast worst " + blastWorst.ToString("0.0") + " ms, ExportSave " + exportMs.ToString("0") + " ms (" + (big.Length / 1024) + " KB), ImportSave " + importMs.ToString("0") + " ms");
                Check(exportMs < 500 && importMs < 5000, "A 100,000-tile save exports under 0.5 s and loads under 5 s");
            }
            finally
            {
                game.ImportSave(baselineSave);
                game.dashLevel = oldDash; game.player.ResetDash();
                if (game.player.viewCamera != null) game.player.viewCamera.transform.rotation = oldCameraRotation;
                Physics.SyncTransforms();
            }
        }

        // A save from the 40-slot era (particleVersion 4) loads with its wallet; its slots give way to the
        // seeded dot field.
        static void CheckDotSaveMigration(GameManager game, ForestWorld forest)
        {
            string full = game.ExportSave();
            var data = JsonUtility.FromJson<GameManager.SaveData>(full);
            data.forest.particleVersion = 4; data.forest.dots = null; data.forest.inventory = 57;
            foreach (var island in data.forest.islands)
                for (int i = 0; i < 40; i++) island.slots.Add(new ParticleSlotSave { id = island.id + ":" + i, color = i % 3, position = new Vector3(100 + i * 2, 0, 0), positioned = true, pending = i >= 24 });
            Check(game.ImportSave(JsonUtility.ToJson(data)), "A particleVersion-4 save with 40 slots per hub still loads");
            Check(forest.State.particleVersion == 5 && forest.State.islands.All(i => i.slots.Count == 0) && forest.State.dots != null && forest.State.dots.Count == 0 && forest.SharedInventory == 57,
                "Its wallet is kept and its slots give way to the dot field (particleVersion 5)");
            Check(game.ImportSave(full), "The current save reloads after the migration check");
        }

        // CPU time of one synchronous render, after a warm-up render that pays any state change.
        static double RenderMs(Camera camera)
        {
            camera.Render();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 3; i++) camera.Render();
            return watch.Elapsed.TotalMilliseconds / 3;
        }

        // ---- movement stays supported while crossing the hub tiles --------------------------------
        static void CheckEdgeMovement(GameManager game)
        {
            game.cameraRig.yaw = 0; game.cameraRig.pitch = 42; game.cameraRig.SendMessage("LateUpdate");
            foreach(var island in ForestWorld.For(game).configuration.islands)
            {
                var start=island.wisdomPosition+Vector3.left*2;
                Teleport(game,start);
                Check(PlatformSupport.Supports(game.player.transform.position),island.id+" start is supported by its hub platform");
                for(int i=0;i<12;i++)
                {
                    game.player.StepMovement(new Vector2(.4f,.6f),1f/30);
                    Physics.SyncTransforms();
                    Check(game.CanStand(game.player.transform.position),island.id+" movement remains on supported hub ground");
                    Check(game.player.transform.position.y>-1,island.id+" movement does not fall into the void");
                }
            }
        }

        // ---- version-4 Lighting migration refunds paid deposits and normalizes odd-z corridors ----
        static void CheckLegacyLightingMigration(GameManager game,ForestWorld forest)
        {
            var legacy=forest.NewState(1907);legacy.particleVersion=3;legacy.inventory=5;
            foreach(var island in legacy.islands)
            {
                var definition=forest.configuration.Find(island.id);
                var layout=ForestLayoutGenerator.Generate(definition,legacy.seed,Landscape.Shores[definition.polygonIndex],forest.configuration.trees.Length);
                // A real version-4 save: 24 slots per hub (Lighting's NewState has none now).
                for(int i=0;i<definition.particleCount;i++)island.slots.Add(new ParticleSlotSave{id=island.id+":"+i,color=i%3,position=layout.particles[i],positioned=true});
                island.charge=new[]{0,0,0};island.rewards=new System.Collections.Generic.List<ParticleRewardSave>();
            }
            var first=legacy.islands[0];first.charge[0]=3;first.rewards.Add(new ParticleRewardSave{color=0,consumesCharge=true});
            first.charge[1]=1;first.rewards.Add(new ParticleRewardSave{color=2,consumesCharge=false});
            var data=new GameManager.SaveData{version=4,schema="lighting-v1",forest=legacy,souls=11,speed=2,soulGainLevel=3,player=game.player.transform.position};
            Check(game.ImportSave(JsonUtility.ToJson(data),offline:true),"lighting-v1 version-4 save migrates to the open grid schema");
            Check(forest.State.particleVersion==5&&forest.SharedInventory==56,"migration converts 5 banked particles, outstanding deposits/rewards, and 11 Souls exactly once to 56 quarter units");
            var migratedFirst=forest.Inventory(first.id);
            Check(migratedFirst.charge.All(c=>c==0)&&migratedFirst.rewards.Count==0,"migration clears refunded receiver charge and reward queues");
            Check(forest.State.islands.All(i=>i.slots.Count==0),"migration drops the old slots for the seeded dot field");
            int before=forest.SharedInventory;var malformed="{\"version\":7,\"schema\":\"lighting-v1\"}";
            Check(!game.ImportSave(malformed)&&forest.SharedInventory==before,"unsupported future schema is rejected without mutating the active wallet");

            var corridorForest=forest.NewState(1908);corridorForest.particleVersion=3;
            foreach(var island in corridorForest.islands)
            {
                var definition=forest.configuration.Find(island.id);
                var layout=ForestLayoutGenerator.Generate(definition,corridorForest.seed,Landscape.Shores[definition.polygonIndex],forest.configuration.trees.Length);
                for(int i=0;i<definition.particleCount;i++)island.slots.Add(new ParticleSlotSave{id=island.id+":"+i,color=i%3,position=layout.particles[i],positioned=true});
            }
            var oldBlocks=new System.Collections.Generic.List<GameManager.BlockSave>();
            for(int gap=0;gap<2;gap++)for(int row=0;row<GameManager.Rows;row++)
            {
                float oldZ=Landscape.BridgeStart(gap)+2*row;
                oldBlocks.Add(new GameManager.BlockSave{id="odd-z-bridge-"+gap+"-"+row,tier=0,gap=gap,column=0,row=row,placed=true,position=new Vector3(0,.26f,oldZ)});
            }
            int expectedPlatformDeliveries=oldBlocks.Count(b=>LightingBridgeGrid.OverlapsPlatform(new Vector2Int(b.column,Mathf.FloorToInt(Landscape.BridgeStart(b.gap)/2f)+b.row)));
            var corridorSave=new GameManager.SaveData{version=4,schema="lighting-v1",forest=corridorForest,blocks=oldBlocks,souls=0,player=game.player.transform.position};
            Check(game.ImportSave(JsonUtility.ToJson(corridorSave)),"A complete old two-corridor save migrates across both hubs");
            var migratedCells=PlacedCells(game);
            int pendingPlatformDeliveries=forest.State.islands.Sum(i=>(i.freeDeliveryColor>=0?1:0)+(i.freeDeliveryColors?.Count??0));
            bool sampleRowsMigrated=Enumerable.Range(0,3).All(row=>
            {
                var cell=new Vector2Int(0,86+row);
                return LightingBridgeGrid.OverlapsPlatform(cell)||migratedCells.Any(p=>p.cell==cell&&p.tier==0);
            });
            Check(Mathf.FloorToInt(Landscape.BridgeStart(1)/2f)==86&&sampleRowsMigrated&&pendingPlatformDeliveries==expectedPlatformDeliveries,
                "Old odd-z second-bridge rows normalize contiguously to indices 86/87/88; platform-overlap tiles become exactly one free delivery each (placed="+string.Join(",",migratedCells.Where(p=>p.cell.y>=84&&p.cell.y<=90).Select(p=>p.cell.y).OrderBy(y=>y))+", overlap deliveries="+pendingPlatformDeliveries+"/"+expectedPlatformDeliveries+")");
            var endpointCells=new[]{new Vector2Int(0,4),new Vector2Int(0,53),new Vector2Int(0,86),new Vector2Int(0,135)};
            bool endpointsMigrated=endpointCells.All(cell=>LightingBridgeGrid.OverlapsPlatform(cell)||migratedCells.Any(p=>p.cell==cell&&p.tier==0));
            Check(endpointsMigrated,"Every non-platform-overlap corridor endpoint remains a placed tile; overlapping endpoints are accounted for by the exact delivery count");
            Check(game.Bridges[0]&&game.Bridges[1],"Migrated legacy corridors remain connected through hubs two and three");
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
                // Both SpectralAnimal and ForestCreature only simulate real movement near the player
                // (60 m, 26 m in Lighting; see their own Simulate()); cycle near each island in turn so every
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
