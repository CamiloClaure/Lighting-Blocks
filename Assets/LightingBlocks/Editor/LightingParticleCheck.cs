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
        static void CheckActualVoidLayout(ForestWorld forest)
        {
            var all=forest.State.islands.SelectMany(owner=>owner.slots.Select(slot=>(owner.id,slot))).ToArray();
            int slots=forest.configuration.islands.Sum(i=>i.ParticleCount);
            Assert(all.Length==slots&&slots==120,"The live scene has all "+slots+" particle slots (40 per hub)");
            // Slots the ring could not fit yet wait, pending, for the respawn loop; only placed ones are laid out.
            all=all.Where(a=>!a.slot.pending).ToArray();
            Assert(all.Length>72,"More than the old 72 particles sit in the void at once ("+all.Length+")");
            bool spaced=true;for(int a=0;a<all.Length;a++)for(int b=a+1;b<all.Length;b++)if((all[a].slot.position-all[b].slot.position).sqrMagnitude<16f)spaced=false;
            Assert(spaced,"All live-scene particles remain globally spaced by at least four metres");
            var distances=new List<int>();var sectorsPerHub=new List<int>();
            foreach(var owner in forest.State.islands)
            {
                var footprints=PlatformSupport.ActivePlatforms.Where(p=>p!=null&&p.hubId==owner.id&&p.Surface!=null).Select(p=>p.Surface.bounds).ToArray();
                Assert(footprints.Length>0,"Live hub "+owner.id+" has support bounds for void placement");
                var bounds=footprints[0];foreach(var extra in footprints.Skip(1))bounds.Encapsulate(extra);
                int x0=Mathf.FloorToInt((bounds.min.x-12)/LightingBridgeGrid.CellSize),x1=Mathf.CeilToInt((bounds.max.x+12)/LightingBridgeGrid.CellSize);
                int z0=Mathf.FloorToInt((bounds.min.z-12)/LightingBridgeGrid.CellSize),z1=Mathf.CeilToInt((bounds.max.z+12)/LightingBridgeGrid.CellSize);
                var steps=new Dictionary<Vector2Int,int>();var queue=new Queue<Vector2Int>();
                for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)
                {
                    var cell=new Vector2Int(x,z);if(LightingBridgeGrid.OverlapsPlatform(cell)||!LightingBridgeGrid.TouchesHub(cell,owner.id))continue;
                    steps[cell]=1;queue.Enqueue(cell);
                }
                while(queue.Count>0)
                {
                    var cell=queue.Dequeue();int step=steps[cell];if(step>=6)continue;
                    foreach(var next in LightingBridgeGrid.NeighborsOf(cell))if(next.x>=x0&&next.x<=x1&&next.y>=z0&&next.y<=z1&&!steps.ContainsKey(next)&&!LightingBridgeGrid.OverlapsPlatform(next)){steps[next]=step+1;queue.Enqueue(next);}
                }
                var radialSectors=new HashSet<int>();
                foreach(var slot in owner.slots)
                {
                    if(slot.pending)continue;
                    if(steps.TryGetValue(LightingBridgeGrid.CellOf(slot.position),out int step))distances.Add(step);else distances.Add(-1);
                    var radial=slot.position-bounds.center;radialSectors.Add(Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(radial.z,radial.x)+Mathf.PI,Mathf.PI*2)/(Mathf.PI*2)*8),0,7));
                }
                sectorsPerHub.Add(radialSectors.Count);
            }
            Assert(distances.Count==all.Length&&distances.All(step=>step>=2&&step<=6),"Every live-scene particle is two to six orthogonal grid tiles from a hub frontier (the extra slots use the 5-6 ring)");
            Assert(sectorsPerHub.All(count=>count>=6),"Live particles are distributed around at least six sectors of each hub edge");
            var starter=forest.State.islands[0].slots[0];
            Assert(Vector3.Distance(starter.position,new Vector3(0,0,3))<=12f,"The deterministic live-scene starter particle is visible within twelve metres of spawn");
        }
        static void Run(GameManager game)
        {
            var forest=ForestWorld.For(game);var system=game.GetComponent<LightingParticles>();
            Assert(system!=null&&system.Receivers.Count==9,"Three near-tree free color selectors per Wisdom Tree");
            Assert(ForestWorld.ParticleUnitsPerParticle==4,"One particle is stored as four integer quarter units");
            Assert(ForestWorld.FormatParticleUnits(0)=="0"&&ForestWorld.FormatParticleUnits(4)=="1"&&ForestWorld.FormatParticleUnits(5)=="1.25"&&ForestWorld.FormatParticleUnits(6)=="1.5"&&ForestWorld.FormatParticleUnits(7)=="1.75"&&ForestWorld.FormatParticleUnits(8)=="2","Quarter-particle wallet formatting is exact");
            CheckActualVoidLayout(forest);
            var island=forest.configuration.islands[0];var owner=forest.Inventory(island.id);var slot=owner.slots[0];
            Assert(forest.PickupVisuals.Count==forest.State.islands.Sum(i=>i.slots.Count),"Every saved void particle has one visual");
            Assert(forest.PickupVisuals.All(t=>!LightingBridgeGrid.OverlapsPlatform(LightingBridgeGrid.CellOf(t.position))),"Visible particle positions stay outside every hub platform");
            var sampleBlock=new MaterialPropertyBlock();forest.PickupVisuals[0].GetComponent<Renderer>().GetPropertyBlock(sampleBlock);
            Assert(sampleBlock.GetColor("_BaseColor")==LightingParticles.ParticleColor,"Particles share the warm yellow material color");

            // Wallet accepts quarter units without the former 30-particle cap.
            forest.State.inventory=124;
            Assert(forest.TryAddInventory(4)&&forest.SharedInventory==128&&forest.SharedInventoryLabel=="32","Particle wallet grows beyond thirty particles");
            Assert(forest.TrySpendInventory(1)&&forest.SharedInventoryLabel=="31.75","Spending one quarter keeps fractional balance exact");
            Assert(forest.TrySpendInventory(127)&&forest.SharedInventory==0,"Wallet spends integer quarter units exactly");
            int beforeDeposit=forest.SharedInventory;
            Assert(forest.Deposit(island.id,0)==ForestWorld.DepositResult.Empty&&forest.SharedInventory==beforeDeposit,"Legacy deposit calls cannot charge particles in Lighting");

            int unsupportedIndex=owner.slots.FindIndex(s=>PlatformSupport.At(s.position)==null&&game.BlockUnder(s.position)==null);
            Assert(unsupportedIndex>=0,"At least one initial void particle remains above unsupported ground");
            slot=owner.slots[unsupportedIndex];var pickupVisual=forest.PickupVisuals[unsupportedIndex];
            var audio=game.GetComponent<LightingPickupAudio>();Assert(audio!=null&&audio.clips.All(c=>c!=null),"All four pickup clips are wired");
            slot.remaining=0;slot.pending=false;
            Teleport(game,slot.position);
            Assert(!forest.TryCollect(unsupportedIndex)&&slot.remaining==0,"A void particle cannot be collected without standing on its supporting tile");
            GameHUD.Current.ShowPause();Assert(!forest.TryCollect(unsupportedIndex),"Paused gameplay prevents collection");GameHUD.Current.BeginJourney();
            var tree=forest.WisdomTrees[0];
            var support=PlaceTestTile(game);
            slot.position=support.transform.position;pickupVisual.gameObject.SetActive(true);audio.ResetSequence();
            int[] yieldLevels={0,1,8};
            foreach(int level in yieldLevels)
            {
                game.soulGainLevel=level;slot.remaining=0;slot.pending=false;slot.position=support.transform.position;
                pickupVisual.position=slot.position+Vector3.up*.7f;pickupVisual.gameObject.SetActive(true);
                Teleport(game,support.transform.position+Vector3.up*.205f);int before=forest.SharedInventory;int plays=audio.PlaybackCount;
                Assert(forest.TryCollect(unsupportedIndex)&&forest.SharedInventory-before==4+level,"Pickup on a real connected tile yields "+(1+level*.25f).ToString("0.00")+" particles at Particle Yield "+level);
                Assert(audio.PlaybackCount==plays+1,"Each successful pickup plays exactly one pickup voice");
            }
            Assert(slot.remaining==ForestWorld.LightingRespawnSeconds,"Collected particle enters the "+ForestWorld.LightingRespawnSeconds+"-second cooldown");
            forest.Tick(ForestWorld.LightingRespawnSeconds-.1f,false);Assert(slot.remaining>0&&!slot.pending,"Pickup remains unavailable until its cooldown expires");
            forest.Tick(.2f,false);for(int retry=0;slot.pending&&retry<12;retry++)forest.Tick(.51f,false);
            Assert(!slot.pending&&slot.remaining==0&&slot.generation>0,"Expired pickup respawns successfully after bounded retries");
            var connected=LightingBridgeGrid.Connected(game);var respawnCell=LightingBridgeGrid.CellOf(slot.position);
            int pathDistance=connected.Min(c=>Math.Abs(c.x-respawnCell.x)+Math.Abs(c.y-respawnCell.y));
            Assert(pathDistance>=2&&pathDistance<=4&&!LightingBridgeGrid.OverlapsPlatform(respawnCell),"Respawn lands two to four grid cells beyond the reachable bridge frontier");

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
            // Each tree serves one free selected color only after its output clears.
            owner.freeDeliveryColor=-1;owner.freeDeliveryColors.Clear();owner.charge=new[]{0,0,0};owner.rewards.Clear();
            Teleport(game,tree.transform.position+Vector3.right*8);
            Assert(!tree.RequestTile(2)&&owner.freeDeliveryColor==-1,"A remote direct tree request is rejected");
            Teleport(game,tree.transform.position+Vector3.right*2);
            Assert(tree.RequestTile(2)&&owner.freeDeliveryColor==2,"Supported ground within three metres requests a free matching tile");
            Assert(!tree.RequestTile(1)&&owner.freeDeliveryColor==2,"Only one player-selected tile can wait at a tree");
            int walletBefore=forest.SharedInventory,blocksBefore=game.Blocks.Count;
            var obstacles=new List<GameObject>();foreach(var offset in island.outputOffsets){var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.transform.position=island.wisdomPosition+offset;obstacles.Add(o);}
            Physics.SyncTransforms();tree.Tick(.61f);
            Assert(game.Blocks.Count==blocksBefore&&owner.freeDeliveryColor==2,"A blocked output preserves the free request");
            foreach(var o in obstacles)ForestWorld.Remove(o);Physics.SyncTransforms();tree.Tick(.61f);
            Assert(game.Blocks.Count==blocksBefore+1&&game.Blocks.Last().tier==2,"A clear output creates exactly the selected tile");
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
