using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace LightingBlocks.Editor
{
    [InitializeOnLoad]
    public static class LightingParticleCheck
    {
        static int frames;static bool running,capturePending;static readonly StringBuilder report=new StringBuilder();
        static LightingParticleCheck(){EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("LightingParticleCheck.Play",false)){running=true;frames=0;}};}
        public static void Batch()
        {
            report.Clear();SessionState.SetBool("LightingParticleCheck.Play",true);
            EditorSceneManager.OpenScene("Assets/Scenes/Lighting.unity");EditorApplication.isPlaying=true;
        }
        static void Assert(bool value,string message){if(!value)throw new Exception(message);report.AppendLine("PASS: "+message);}
        static void Poll()
        {
            // Library/LightingParticleCheck.request runs it from an open editor, like the integration check.
            if(!running&&!capturePending&&!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&File.Exists("Library/LightingParticleCheck.request")){AssetDatabase.Refresh();if(EditorApplication.isCompiling)return;File.Delete("Library/LightingParticleCheck.request");Batch();return;}
            if(capturePending)
            {
                if(++frames<10)return;capturePending=false;
                try{Capture(GameManager.Instance);report.AppendLine("SUCCESS: all checks passed");}catch(Exception e){report.AppendLine("FAIL: "+e);}
                Finish(!report.ToString().Contains("FAIL:"));return;
            }
            if(!running||++frames<45)return;running=false;bool ok=false;
            try
            {
                var game=GameManager.Instance;
                Assert(!game.persistenceEnabled&&game.savePathOverride!=null,"Save isolation active before startup");game.autoTick=false;
                typeof(LightingIntegrationCheck).GetMethod("RunChecks",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{game});
                report.AppendLine("PASS: Existing income, save, edge movement and four-minute wildlife regression suite");
                Run(game);ok=true;capturePending=true;frames=0;return;
            }
            catch(Exception e){report.AppendLine("FAIL: "+e);}
            Finish(ok);
        }
        static void Finish(bool ok)
        {
            File.WriteAllText("Library/LightingParticleCheck.txt",report.ToString());
            SessionState.SetBool("LightingParticleCheck.Play",false);
            if(Application.isBatchMode)EditorApplication.Exit(ok?0:1);else EditorApplication.isPlaying=false;
        }
        static void Teleport(GameManager game,Vector3 position){var c=game.player.GetComponent<CharacterController>();c.enabled=false;game.player.transform.position=position;c.enabled=true;Physics.SyncTransforms();}
        static BridgeBlock PlaceTestTile(GameManager game)
        {
            var candidates=new Dictionary<Vector2Int,Vector3>();var connected=LightingBridgeGrid.Connected(game);
            var supportedPoints=new List<Vector3>();
            foreach(var cell in connected)supportedPoints.Add(LightingBridgeGrid.Cell(cell.x,cell.y));
            foreach(var platform in PlatformSupport.ActivePlatforms)
            {
                if(platform==null||platform.Surface==null)continue;var b=platform.Surface.bounds;
                int x0=Mathf.FloorToInt((b.min.x-8)/LightingBridgeGrid.CellSize),x1=Mathf.CeilToInt((b.max.x+8)/LightingBridgeGrid.CellSize);
                int z0=Mathf.FloorToInt((b.min.z-8)/LightingBridgeGrid.CellSize),z1=Mathf.CeilToInt((b.max.z+8)/LightingBridgeGrid.CellSize);
                for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)
                {
                    var cell=new Vector2Int(x,z);if(!LightingBridgeGrid.IsFrontier(game,cell)||LightingBridgeGrid.HasObstruction(cell,game))continue;
                    var target=LightingBridgeGrid.Cell(x,z);
                    var stand=new Vector3(Mathf.Clamp(target.x,b.min.x,b.max.x),.025f,Mathf.Clamp(target.z,b.min.z,b.max.z));
                    if(game.CanStand(stand))candidates[cell]=stand;
                }
            }
            foreach(var support in supportedPoints)
            {
                var origin=LightingBridgeGrid.CellOf(support);
                for(int x=origin.x-2;x<=origin.x+2;x++)for(int z=origin.y-2;z<=origin.y+2;z++)
                {
                    var cell=new Vector2Int(x,z);var target=LightingBridgeGrid.Cell(x,z);var delta=target-support;delta.y=0;
                    if(delta.magnitude<=4f&&LightingBridgeGrid.IsFrontier(game,cell)&&!LightingBridgeGrid.HasObstruction(cell,game))candidates[cell]=support;
                }
            }
            var ordered=candidates.OrderBy(pair=>(LightingBridgeGrid.Cell(pair.Key.x,pair.Key.y)-pair.Value).sqrMagnitude).ThenBy(pair=>ForestLayoutGenerator.Hash(1907,pair.Key.x+"/"+pair.Key.y)).ToList();
            foreach(var candidate in ordered)
            {
                Teleport(game,candidate.Value);
                int tier=GameManager.RequiredTier(0,candidate.Key.x,candidate.Key.y);
                if(!game.TrySpawnAtWisdomTree("island-1",out var block,tier))continue;
                block.transform.position=LightingBridgeGrid.Cell(candidate.Key.x,candidate.Key.y);block.Box.Body.position=block.transform.position;Physics.SyncTransforms();
                if(game.TryPlace(block))return block;
                ForestWorld.Remove(block.gameObject);
            }
            throw new Exception("Could not build a supported test tile at a reachable hub or connected frontier");
        }
        // The reference prototype's field: a seeded ~18% of void cells, the nearest 40 within 24 m drawn,
        // the nearest 4 within 12 m lit.
        static void CheckDotField(GameManager game,ForestWorld forest)
        {
            var dots=forest.Dots;
            Assert(dots!=null&&forest.State.particleVersion==5&&forest.State.islands.All(i=>i.slots.Count==0),"Lighting particles are the seeded dot field (particleVersion 5, no slots)");
            Assert(forest.PickupVisuals.Count==LightingDots.MaxVisible&&dots.Lights.Count==LightingDots.DotLights,"A pool of 40 dot visuals and 4 dot lights");
            var material=forest.PickupVisuals[0].GetComponent<Renderer>().sharedMaterial;
            Assert(material!=null&&material.shader.name=="LightingBlocks/Living Spirit"&&material.GetColor("_BaseColor")==LightingParticles.ParticleColor,"Dots use the imported Living Spirit shader in the reference yellow");
            int before=dots.Seed,differ=0;dots.NewField();
            for(int x=-30;x<30;x++)for(int z=-30;z<30;z++){var c=new Vector2Int(x,z);if(LightingDots.HasDot(before,c)!=LightingDots.HasDot(dots.Seed,c))differ++;}
            Assert(dots.Seed!=before&&differ>400,"Each world build lays a new field ("+differ+" of 3600 cells differ)");
            int seed=dots.Seed,hits=0,changed=0;
            for(int x=-30;x<30;x++)for(int z=-30;z<30;z++){var c=new Vector2Int(x,z);if(LightingDots.HasDot(seed,c))hits++;if(LightingDots.HasDot(seed,c)!=LightingDots.HasDot(seed+1,c))changed++;}
            Assert(hits>=540&&hits<=756,"About 18% of grid cells hold a dot ("+hits+" of 3600)");
            Assert(changed>400,"Another seed lays a different field ("+changed+" of 3600 differ)");

            Teleport(game,new Vector3(0,.025f,3));dots.Refresh();
            var player=LightingBridgeGrid.CellOf(game.player.transform.position);
            int reach=Mathf.CeilToInt(LightingDots.StreamDistance/LightingBridgeGrid.CellSize);
            var near=new List<(Vector2Int cell,int distance)>();
            for(int x=-reach;x<=reach;x++)for(int z=-reach;z<=reach;z++){var c=player+new Vector2Int(x,z);if(x*x+z*z<=reach*reach&&dots.IsDot(c))near.Add((c,x*x+z*z));}
            var shown=dots.ShownCells.ToList();
            Assert(near.Count>0&&shown.Count==Mathf.Min(LightingDots.MaxVisible,near.Count),"Spawn sees the nearest dots, at most 40 ("+shown.Count+" of "+near.Count+" within 24 m)");
            Assert(shown.All(c=>!LightingBridgeGrid.OverlapsPlatform(c)&&LightingDots.HasDot(seed,c)),"Every shown dot sits over the void, never on a hub platform");
            int farthestShown=shown.Max(c=>(c-player).sqrMagnitude);
            Assert(near.Where(n=>!shown.Contains(n.cell)).All(n=>n.distance>=farthestShown),"Shown dots are the nearest ones");
            forest.Tick(.25f,false);
            var visible=forest.PickupVisuals.Where(t=>t.gameObject.activeSelf).ToArray();
            Assert(visible.Length==shown.Count&&visible.All(t=>shown.Any(c=>{var p=LightingDots.Position(c);return Mathf.Abs(t.position.x-p.x)<.01f&&Mathf.Abs(t.position.z-p.z)<.01f&&Mathf.Abs(t.position.y-p.y)<=LightingDots.Bob+.01f;})),"Each visible dot floats over its cell centre, bobbing at most 0.16 m");
            var lit=dots.Lights.Where(l=>l.enabled).ToArray();var here=game.player.transform.position;
            Assert(lit.Length<=LightingDots.DotLights&&lit.All(l=>new Vector2(l.transform.position.x-here.x,l.transform.position.z-here.z).magnitude<=LightingDots.LightDistance+.01f),"At most four dot lights, all within twelve metres ("+lit.Length+" lit)");
        }
        static void Run(GameManager game)
        {
            var forest=ForestWorld.For(game);var system=game.GetComponent<LightingParticles>();
            Assert(system!=null&&GameObject.Find("Free tile selector 0")==null,"No colour selector cubes beside the Wisdom Trees");
            Assert(ForestWorld.ParticleUnitsPerParticle==4,"One particle is stored as four integer quarter units");
            Assert(ForestWorld.FormatParticleUnits(0)=="0"&&ForestWorld.FormatParticleUnits(4)=="1"&&ForestWorld.FormatParticleUnits(5)=="1.25"&&ForestWorld.FormatParticleUnits(6)=="1.5"&&ForestWorld.FormatParticleUnits(7)=="1.75"&&ForestWorld.FormatParticleUnits(8)=="2","Quarter-particle wallet formatting is exact");
            CheckDotField(game,forest);
            var island=forest.configuration.islands[0];var owner=forest.Inventory(island.id);var dots=forest.Dots;

            // Wallet accepts quarter units without the former 30-particle cap.
            forest.State.inventory=124;
            Assert(forest.TryAddInventory(4)&&forest.SharedInventory==128&&forest.SharedInventoryLabel=="32","Particle wallet grows beyond thirty particles");
            Assert(forest.TrySpendInventory(1)&&forest.SharedInventoryLabel=="31.75","Spending one quarter keeps fractional balance exact");
            Assert(forest.TrySpendInventory(127)&&forest.SharedInventory==0,"Wallet spends integer quarter units exactly");
            int beforeDeposit=forest.SharedInventory;
            Assert(forest.Deposit(island.id,0)==ForestWorld.DepositResult.Empty&&forest.SharedInventory==beforeDeposit,"Legacy deposit calls cannot charge particles in Lighting");

            var bare=dots.ShownCells.FirstOrDefault(c=>game.BlockUnder(LightingDots.Position(c))==null);
            Assert(dots.IsDot(bare)&&game.BlockUnder(LightingDots.Position(bare))==null,"At least one shown dot floats over bare void");
            var audio=game.GetComponent<LightingPickupAudio>();Assert(audio!=null&&audio.clips.All(c=>c!=null),"All four pickup clips are wired");
            Teleport(game,LightingDots.Position(bare));
            Assert(!dots.TryCollect(bare)&&dots.IsDot(bare),"A void dot cannot be collected without standing on a tile under it");
            var tree=forest.WisdomTrees[0];
            var support=PlaceTestTile(game);var supportCell=LightingBridgeGrid.CellOf(support.transform.position);
            // The test tile lands wherever a frontier is free; move the seed until the field puts a dot there.
            int seed=forest.State.seed;while(!LightingDots.HasDot(dots.Seed,supportCell))forest.State.seed++;
            Teleport(game,support.transform.position+Vector3.up*.205f);dots.Refresh();
            GameHUD.Current.ShowPause();Assert(!dots.TryCollect(supportCell),"Paused gameplay prevents collection");GameHUD.Current.BeginJourney();
            audio.ResetSequence();
            int[] yieldLevels={0,1,8};
            foreach(int level in yieldLevels)
            {
                game.soulGainLevel=level;dots.ResetField();
                int before=forest.SharedInventory;int plays=audio.PlaybackCount;
                Assert(forest.PickupVisuals.Any(t=>t.gameObject.activeSelf&&LightingBridgeGrid.CellOf(t.position)==supportCell),"The dot over the tile is drawn before pickup");
                Assert(dots.TryCollect(supportCell)&&forest.SharedInventory-before==4+level,"Pickup on a real connected tile yields "+(1+level*.25f).ToString("0.00")+" particles at Particle Yield "+level);
                Assert(audio.PlaybackCount==plays+1,"Each successful pickup plays exactly one pickup voice");
            }
            var taken=forest.State.dots.Single(d=>d.x==supportCell.x&&d.z==supportCell.y);
            Assert(taken.remaining==ForestWorld.LightingRespawnSeconds,"A collected dot's cell waits "+ForestWorld.LightingRespawnSeconds+" seconds");
            Assert(!dots.TryCollect(supportCell)&&!dots.ShownCells.Contains(supportCell)&&!forest.PickupVisuals.Any(t=>t.gameObject.activeSelf&&LightingBridgeGrid.CellOf(t.position)==supportCell),"The collected dot disappears and cannot be taken twice");
            forest.Tick(ForestWorld.LightingRespawnSeconds-.1f,false);Assert(!dots.IsDot(supportCell)&&dots.Moved.Count==0,"No dot comes back before its time is up");
            forest.Tick(.2f,false);
            var from=game.player.transform.position;
            Func<Vector2Int,bool> ring=c=>{var p=LightingDots.Position(c);float d=new Vector2(p.x-from.x,p.z-from.z).magnitude;return d>=LightingDots.RespawnNear-1.5f&&d<=LightingDots.StreamDistance+1.5f;};
            var back=dots.Moved.FirstOrDefault();
            Assert(dots.Moved.Count==1&&forest.State.dots.Count==0&&!dots.IsDot(supportCell)&&dots.IsDot(back)&&ring(back),"After "+ForestWorld.LightingRespawnSeconds+" seconds the dot comes back at a random spot 8-24 m away ("+back+"), not its old cell");
            for(int i=0;i<20;i++)dots.Respawn();
            Assert(dots.Moved.Count==21&&dots.Moved.All(c=>dots.IsDot(c)&&ring(c)),"20 more respawns land in 20 distinct free void cells 8-24 m away");
            dots.ResetField();forest.State.seed=seed;dots.Refresh();

            forest.State.inventory=0;int speedBefore=game.speed;
            Teleport(game,tree.transform.position+Vector3.right*2);
            Assert(game.CanBuyLightingUpgrades()&&!game.Purchase("speed")&&game.speed==speedBefore,"Supported ground near a Wisdom Tree passes the purchase gate but an empty wallet cannot buy");
            Teleport(game,tree.transform.position+Vector3.right*8);
            Assert(game.CanStand(game.player.transform.position)&&!game.CanBuyLightingUpgrades()&&!game.Purchase("speed")&&game.speed==speedBefore,"Supported ground more than three metres from any tree cannot buy an upgrade");
            var originalSpeed=game.speed;var originalYield=game.soulGainLevel;game.speed=0;game.soulGainLevel=0;
            int speedPrice=(int)Math.Ceiling(game.Cost("speed")*ForestWorld.ParticleUnitsPerParticle);forest.State.inventory=speedPrice;
            Teleport(game,tree.transform.position+Vector3.right*2);GameHUD.Current.SetView(GameHUD.ScreenView.Skills);
            Assert(game.CanBuyLightingUpgrades(),"The near-tree gate remains valid while the Skills view is open");
            Assert(game.Purchase("speed")&&game.speed==1&&forest.SharedInventory==0,"Speed purchase from Skills view spends exactly its quarter-unit price");
            int yieldPrice=(int)Math.Ceiling(game.Cost("soulgain")*ForestWorld.ParticleUnitsPerParticle);forest.State.inventory=yieldPrice;
            Assert(game.Purchase("soulgain")&&game.soulGainLevel==1&&forest.SharedInventory==0,"Particle Yield purchase from Skills view spends exactly its quarter-unit price");
            game.speed=game.Cap("speed");game.soulGainLevel=game.Cap("soulgain");forest.State.inventory=400;int capWallet=forest.SharedInventory;
            Assert(!game.Purchase("speed")&&!game.Purchase("soulgain")&&forest.SharedInventory==capWallet,"Max-level Speed and Particle Yield reject purchases without spending");
            game.speed=originalSpeed;game.soulGainLevel=originalYield;game.ApplyUpgrades();forest.State.inventory=0;GameHUD.Current.BeginJourney();
            // A free tile an old save still has pending is served only after the tree's output clears.
            owner.freeDeliveryColor=-1;owner.freeDeliveryColors.Clear();owner.charge=new[]{0,0,0};owner.rewards.Clear();
            Teleport(game,tree.transform.position+Vector3.right*2);
            owner.freeDeliveryColor=2;
            int walletBefore=forest.SharedInventory,blocksBefore=game.Blocks.Count(b=>!b.placed);
            var obstacles=new List<GameObject>();foreach(var offset in island.outputOffsets){var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.transform.position=island.wisdomPosition+offset;obstacles.Add(o);}
            Physics.SyncTransforms();tree.Tick(.61f);
            Assert(game.Blocks.Count(b=>!b.placed)==blocksBefore&&owner.freeDeliveryColor==2,"A blocked output preserves the free request");
            foreach(var o in obstacles)ForestWorld.Remove(o);Physics.SyncTransforms();tree.Tick(.61f);
            Assert(game.Blocks.Count(b=>!b.placed)==blocksBefore+1&&game.Blocks.Last(b=>!b.placed).tier==2,"A clear output creates exactly the pending tile");
            Assert(owner.freeDeliveryColor==-1&&owner.charge.All(c=>c==0)&&owner.rewards.Count==0,"Free delivery clears its request without particle charges or paid rewards");
            Assert(forest.SharedInventory==walletBefore,"Free delivery leaves the wallet unchanged");
        }
        static void Capture(GameManager game)
        {
            Teleport(game,ForestWorld.For(game).configuration.islands[0].wisdomPosition+new Vector3(6,.025f,-6));
            game.cameraRig.CenterOnPlayer();game.cameraRig.readInput=false;typeof(StrategyCamera).GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(game.cameraRig,new object[]{1f});
            var camera=game.cameraRig.GetComponent<Camera>();var data=camera.GetUniversalAdditionalCameraData();
            Assert(camera.clearFlags==CameraClearFlags.SolidColor&&camera.backgroundColor==Color.black,"Black void camera background");
            Directory.CreateDirectory("Library/LightingParticleCaptures");
            foreach(bool post in new[]{true,false})
            {
                data.renderPostProcessing=post;var rt=new RenderTexture(1440,900,24);camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
                var image=new Texture2D(1440,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();File.WriteAllBytes("Library/LightingParticleCaptures/"+(post?"post-on":"post-off")+".png",image.EncodeToPNG());
                RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.Destroy(image);rt.Release();UnityEngine.Object.Destroy(rt);
            }
            data.renderPostProcessing=true;
        }
    }
}
