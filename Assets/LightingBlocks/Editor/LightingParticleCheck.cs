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
        static void Run(GameManager game)
        {
            var forest=ForestWorld.For(game);var system=game.GetComponent<LightingParticles>();
            Assert(system!=null&&system.Receivers.Count==9,"Three color receivers per tree");
            // LightingIntegrationCheck.RunChecks (just ran) leaves its own save round-trip state
            // behind (forest.State.inventory==7); start from a known-clean slate here instead.
            forest.State.inventory=0;
            var sampleBlock=new MaterialPropertyBlock();forest.PickupVisuals[0].GetComponent<Renderer>().GetPropertyBlock(sampleBlock);
            Assert(sampleBlock.GetColor("_BaseColor")==LightingParticles.ParticleColor,"Fundamental particles are painted the shared warm yellow (#FFD66B)");

            // ---- walk-over pickup: 1.1-unit radius, pause/blocked-gameplay, capacity -----------------
            var audio=game.GetComponent<LightingPickupAudio>();Assert(audio!=null&&audio.clips.All(c=>c!=null),"All four pickup clips are wired");int pickupSounds=audio.PlaybackCount;
            var island=forest.configuration.islands[0];var owner=forest.Inventory(island.id);var slot=owner.slots[0];
            slot.remaining=0;slot.pending=false;
            Teleport(game,slot.position+Vector3.right*100);
            Assert(!forest.TryCollect(0),"Out-of-range walk-over pickup fails");
            Teleport(game,slot.position+Vector3.right*.7f);
            GameHUD.Current.ShowPause();
            Assert(!forest.TryCollect(0),"Paused/blocked gameplay prevents collection");
            GameHUD.Current.BeginJourney();
            Assert(forest.TryCollect(0)&&forest.SharedInventory==1,"In-range pickup adds one particle to the shared inventory");
            Assert(!forest.TryCollect(0),"Already-collected particle cannot be collected twice");
            Assert(audio.PlaybackCount==pickupSounds+1,"Exactly one pickup sound for one successful collection; failed and duplicate pickups are silent");
            forest.State.inventory=28;
            Assert(!forest.TryAddInventory(3)&&forest.SharedInventory==28,"A refund that cannot fully fit is rejected, not partially applied");
            Assert(forest.TryAddInventory(2)&&forest.SharedInventory==30,"A refund that exactly fits is applied");
            var secondSlot=owner.slots[1];secondSlot.remaining=0;secondSlot.pending=false;Teleport(game,secondSlot.position+Vector3.right*.7f);
            Assert(!forest.TryCollect(1)&&secondSlot.remaining==0,"A full shared inventory (30/30) refuses a pickup and leaves the world particle uncollected");
            forest.State.inventory=0;

            // ---- focus loss blocks pickup: ForestWorld.Update passes collect=Application.isFocused,
            // Tick(delta,collect) is the actual gate - exercise it directly, since a batch process has
            // no real OS window to unfocus. ------------------------------------------------------------
            var focusSlot=owner.slots[2];focusSlot.remaining=0;focusSlot.pending=false;Teleport(game,focusSlot.position+Vector3.right*.7f);
            forest.Tick(1f,false);
            Assert(forest.SharedInventory==0&&focusSlot.remaining==0,"Tick(collect:false) - the focus-loss path - does not collect a walked-over particle");
            int soundBeforeFocus=audio.PlaybackCount;
            forest.Tick(1f,true);
            Assert(forest.SharedInventory==1&&focusSlot.remaining>0,"Tick(collect:true) collects it once focus (or the equivalent gate) returns");
            forest.State.inventory=0;

            Assert(audio.PlaybackCount==soundBeforeFocus+1,"Focus recovery collection plays one pickup sound");
            int soundsBeforeDeposits=audio.PlaybackCount;
            // ---- E-key deposit targeting: range, unobstructed path, nearest-of-several -------------
            forest.State.inventory=10;
            var blue=system.Receivers[0];var violet=system.Receivers[1];var amber=system.Receivers[2];
            Assert(blue.Tree.Definition.id==island.id,"Island-1's tree receivers come first, in colour order");
            Teleport(game,blue.transform.position+Vector3.right*100);
            Assert(!system.TryDeposit(game.player),"Out-of-range receiver is not reached by E");
            // Stand a metre off to the side (still well inside the 1.5-unit range) rather than
            // exactly on top of the receiver, so the obstruction sits on a real outside-in ray
            // instead of starting the raycast from inside the obstacle's own geometry.
            Teleport(game,new Vector3(blue.transform.position.x,.025f,blue.transform.position.z)+Vector3.right*1f);
            var origin=game.player.transform.position+Vector3.up*.7f;
            var obstruction=GameObject.CreatePrimitive(PrimitiveType.Cube);obstruction.transform.localScale=Vector3.one*.5f;
            obstruction.transform.position=Vector3.Lerp(origin,blue.transform.position,.5f);Physics.SyncTransforms();
            Assert(!system.TryDeposit(game.player),"An obstructed path to an in-range receiver is refused");
            obstruction.SetActive(false);UnityEngine.Object.Destroy(obstruction);Physics.SyncTransforms();
            // Receivers sit 2.1 units apart (phase 2 spaces them further), so only one is ever in
            // range at once here; this still proves Nearest() singles out the correct in-range
            // receiver among all nine rather than any nearby-but-out-of-range one.
            var nearestPick=system.Nearest(game.player.transform.position+Vector3.up*.7f,LightingParticles.DepositRange,game.player.transform);
            Assert(nearestPick==blue,"Deposit resolves to the one reachable receiver, ignoring farther/out-of-range ones");

            // ---- independent per-receiver charge (spec example: 2 blue + 1 amber => blue 2/3, amber 1/3) --
            Assert(system.TryDeposit(game.player)&&blue.Charge==1&&forest.SharedInventory==9,"First deposit charges blue to 1/3 and spends one shared particle");
            Assert(system.TryDeposit(game.player)&&blue.Charge==2,"Second deposit charges blue to 2/3");
            Assert(amber.Deposit()&&amber.Charge==1,"An amber deposit charges amber independently, to 1/3");
            Assert(violet.Charge==0&&blue.Charge==2&&amber.Charge==1,"Blue 2/3 and amber 1/3 - receivers never share one bank");

            // ---- full receivers reject deposits; blocked outputs retain charge and queue entry -------
            Assert(blue.Deposit()&&blue.Charge==3&&owner.rewards.Select(q=>q.color).SequenceEqual(new[]{0}),"Third blue deposit fills it to 3/3 and queues one blue reward");
            int inventoryBeforeReject=forest.SharedInventory;
            Assert(!blue.Deposit()&&blue.Charge==3&&forest.SharedInventory==inventoryBeforeReject,"A full receiver rejects further deposits and spends nothing");
            var obstacles=island.outputOffsets.Select(offset=>{var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.transform.position=island.wisdomPosition+offset;o.transform.localScale=Vector3.one*.8f;return o;}).ToArray();Physics.SyncTransforms();
            int blocksBefore=game.Blocks.Count;blue.Tree.Tick(1);
            Assert(blue.Charge==3&&owner.rewards.Select(q=>q.color).SequenceEqual(new[]{0})&&game.Blocks.Count==blocksBefore,"A blocked output retains the receiver's charge and its queue entry");
            foreach(var o in obstacles){o.SetActive(false);UnityEngine.Object.Destroy(o);}Physics.SyncTransforms();
            blue.Tree.Tick(1);
            Assert(blue.Charge==0&&owner.rewards.Count==0&&game.Blocks.Count==blocksBefore+1&&game.Blocks.Last().tier==0,"Clearing the output releases exactly one blue tile and consumes charge only now");
            blue.Tree.Tick(1);Assert(game.Blocks.Count==blocksBefore+1,"No duplicate tile once the queue is empty");
            Assert(amber.Charge==1,"Amber's still-partial charge was untouched by blue's production");

            // ---- simultaneous completions: processed in completion order (FIFO), one per charge cycle --
            for(int n=0;n<3;n++)Assert(violet.Deposit(),"Violet deposit "+n+" toward a simultaneous completion");
            for(int n=0;n<2;n++)amber.Deposit(); // amber was already at 1/3; two more complete it in the same frame as violet
            Assert(violet.Charge==3&&amber.Charge==3&&owner.rewards.Select(q=>q.color).SequenceEqual(new[]{1,2}),"Two receivers completed back-to-back queue in the order they completed");
            blocksBefore=game.Blocks.Count;blue.Tree.Tick(1);
            Assert(game.Blocks.Count==blocksBefore+1&&game.Blocks.Last().tier==1&&owner.rewards.Select(q=>q.color).SequenceEqual(new[]{2}),"The earlier (violet) completion produces first");
            blue.Tree.Tick(1);
            Assert(game.Blocks.Count==blocksBefore+2&&game.Blocks.Last().tier==2&&owner.rewards.Count==0,"The later (amber) completion produces second, queue now empty");

            Assert(audio.PlaybackCount==soundsBeforeDeposits,"Deposits and tile rewards do not play the pickup chime");
            // ---- reload while a completed receiver's reward is still queued (mid-charge): no lost or
            // duplicate tile. Only charge/queue are saved - WisdomTree's own 0.6s animation timer is
            // not, so a reload simply restarts that countdown once Tick sees the queue again. -----------
            forest.State.inventory=3;
            Assert(blue.Deposit()&&blue.Deposit()&&blue.Deposit()&&blue.Charge==3&&owner.rewards.Select(q=>q.color).SequenceEqual(new[]{0}),"Blue fills to 3/3 and queues a reward again, for the reload check");
            blue.Tree.Tick(.3f); // partial tick: Charging (remaining>0), no tile created yet
            Assert(blue.Tree.Charging&&owner.rewards.Select(q=>q.color).SequenceEqual(new[]{0})&&blue.Charge==3,"Mid-charge: queue entry and charge both still held, no tile created yet");
            string midCharge=game.ExportSave();int blocksBeforeReload=game.Blocks.Count;
            Assert(game.ImportSave(midCharge),"A save taken mid-charge (queued reward, receiver still full) reloads");
            var reloadedOwner=forest.Inventory(island.id);var reloadedBlue=system.Receivers[0];
            Assert(reloadedOwner.charge[0]==3&&reloadedOwner.rewards.Select(q=>q.color).SequenceEqual(new[]{0})&&!reloadedBlue.Tree.Charging,"Charge and queue survive the reload exactly - only the un-saved charge timer resets");
            reloadedBlue.Tree.Tick(1f);
            Assert(game.Blocks.Count==blocksBeforeReload+1&&reloadedOwner.charge[0]==0&&reloadedOwner.rewards.Count==0,"Completing the reward after reload releases exactly one tile - no loss, no duplicate");

            // ---- migration: particleVersion 1 -> 2, coloured + uncoloured banked rewards and leftovers --
            string fresh=game.ExportSave();
            var v1=JsonUtility.FromJson<GameManager.SaveData>(fresh);v1.forest.particleVersion=1;
            v1.forest.islands[0].treeCharge=8;v1.forest.islands[0].hasChargeColor=true;v1.forest.islands[0].chargeColor=1;v1.forest.islands[0].inventory=4;
            v1.forest.islands[1].treeCharge=5;v1.forest.islands[1].hasChargeColor=false;v1.forest.islands[1].inventory=6;
            v1.forest.islands[2].treeCharge=0;v1.forest.islands[2].inventory=0;
            Assert(game.ImportSave(JsonUtility.ToJson(v1)),"A particleVersion-1 save migrates to 3");
            var m0=forest.Inventory(forest.configuration.islands[0].id);var m1=forest.Inventory(forest.configuration.islands[1].id);
            Assert(m0.rewards.Select(q=>q.color).SequenceEqual(new[]{1,1})&&m0.charge[1]==2,"8 banked violet charge becomes two queued violet rewards plus a 2/3 violet leftover");
            Assert(m1.rewards.Select(q=>q.color).SequenceEqual(new[]{-1})&&m1.charge.All(c=>c==0),"5 banked, uncoloured charge becomes one uncoloured (-1) queued reward, no leftover charge");
            Assert(!m0.hasChargeColor&&m0.treeCharge==0&&!m1.hasChargeColor&&m1.treeCharge==0,"Legacy bank fields are cleared once migrated");
            Assert(forest.SharedInventory==4+6+2,"Unspent island inventories and uncoloured leftover (5%3=2) combine into the shared inventory");

            // Drain banked rewards, including one after completing the leftover receiver.
            var migratedTree=system.Receivers[1].Tree;
            foreach(var b in game.Blocks)b.transform.position+=Vector3.right*100;Physics.SyncTransforms();
            int migratedBefore=game.Blocks.Count;
            migratedTree.Tick(1);
            Assert(m0.charge[1]==2&&m0.rewards.Count==1&&game.Blocks.Count==migratedBefore+1,"Migrated reward never erases partial receiver charge");
            Assert(forest.Deposit(island.id,1)==ForestWorld.DepositResult.Success,"Leftover receiver accepts its third particle");
            migratedTree.Tick(1);
            Assert(m0.charge[1]==3&&m0.rewards.Count==1&&m0.rewards[0].consumesCharge,"Banked reward never consumes newly completed receiver charge");
            migratedTree.Tick(1);
            Assert(m0.charge[1]==0&&m0.rewards.Count==0&&game.Blocks.Count==migratedBefore+3,"Receiver-backed reward consumes charge exactly once after banked rewards");
            var receiverBlock=new MaterialPropertyBlock();system.Receivers[1].GetComponent<Renderer>().GetPropertyBlock(receiverBlock);
            Assert(receiverBlock.GetColor("_EmissionColor")==LightingParticles.Colors[1]*.55f,"Receiver brightness resets immediately after producing its tile");

            var v2=JsonUtility.FromJson<GameManager.SaveData>(fresh);v2.forest.particleVersion=2;
            foreach(var entry in v2.forest.islands){entry.queue.Clear();entry.rewards.Clear();entry.charge=new int[3];}
            v2.forest.islands[0].queue=new List<int>{1,1,1};v2.forest.islands[0].charge[1]=3;
            Assert(game.ImportSave(JsonUtility.ToJson(v2)),"Version 2 full receiver migrates");
            var v2owner=forest.Inventory(island.id);
            Assert(v2owner.rewards.Select(q=>q.color).SequenceEqual(new[]{1,1,1})&&v2owner.rewards.Select(q=>q.consumesCharge).SequenceEqual(new[]{false,false,true}),"Only the last matching v2 reward is receiver-backed");
            Assert(game.ImportSave(game.ExportSave())&&forest.State.particleVersion==3,"Version 3 migration is stable on reload");

            // ---- migration: particleVersion 0 -> 2, chained through 1 --------------------------------
            var v0=JsonUtility.FromJson<GameManager.SaveData>(fresh);v0.forest.particleVersion=0;
            v0.forest.islands[0].inventory=7;v0.forest.islands[1].inventory=0;v0.forest.islands[2].inventory=0;
            Assert(game.ImportSave(JsonUtility.ToJson(v0)),"A particleVersion-0 save migrates all the way to 3 (chained through 1)");
            var m0v0=forest.Inventory(forest.configuration.islands[0].id);
            Assert(m0v0.rewards.Count==0&&m0v0.charge.All(c=>c==0)&&forest.SharedInventory==7,
                "7 raw v0 particles remain unspent in shared inventory");

            // ---- migration: unspent island inventories can combine into an over-capacity shared total --
            var v1c=JsonUtility.FromJson<GameManager.SaveData>(fresh);v1c.forest.particleVersion=1;
            v1c.forest.islands[0].treeCharge=0;v1c.forest.islands[0].inventory=25;
            v1c.forest.islands[1].treeCharge=0;v1c.forest.islands[1].inventory=25;
            v1c.forest.islands[2].treeCharge=0;v1c.forest.islands[2].inventory=0;
            Assert(game.ImportSave(JsonUtility.ToJson(v1c)),"Combined island inventories exceeding capacity still migrate");
            Assert(forest.SharedInventory==50,"Migrated balances may exceed capacity (50 > "+ForestWorld.InventoryCapacity+")");
            Assert(!forest.TryAddInventory(1),"An over-capacity shared inventory refuses new collection");
            Assert(forest.TrySpendInventory(21)&&forest.SharedInventory==29,"An over-capacity shared inventory can still be spent");
            Assert(forest.TryAddInventory(1)&&forest.SharedInventory==30,"Collection resumes once back under capacity");

            // ---- migration preserves respawn state, IDs and positions untouched by the bank rewrite ---
            var v1b=JsonUtility.FromJson<GameManager.SaveData>(fresh);v1b.forest.particleVersion=1;
            string pendingId=v1b.forest.islands[0].slots[3].id;v1b.forest.islands[0].slots[3].remaining=0;v1b.forest.islands[0].slots[3].pending=true;
            Assert(game.ImportSave(JsonUtility.ToJson(v1b)),"A particleVersion-1 save with a mid-respawn fragment migrates");
            var pendingSlot=forest.Inventory(island.id).slots.First(s=>s.id==pendingId);
            Assert(pendingSlot.pending,"A pending (mid-respawn) fragment survives the particle-bank migration untouched");

            // ---- save round-trip for the new v2 fields ------------------------------------------------
            forest.State.inventory=17;
            forest.Inventory(forest.configuration.islands[0].id).charge=new[]{2,0,1};
            forest.Inventory(forest.configuration.islands[0].id).rewards=new List<ParticleRewardSave>{new ParticleRewardSave{color=0},new ParticleRewardSave{color=-1},new ParticleRewardSave{color=2}};
            forest.Inventory(forest.configuration.islands[1].id).charge=new[]{0,3,0};
            forest.Inventory(forest.configuration.islands[1].id).rewards=new List<ParticleRewardSave>{new ParticleRewardSave{color=1,consumesCharge=true}};
            string roundTrip=game.ExportSave();
            // Perturb every field the asserts below check - a match afterward proves ImportSave
            // actually restored it from the string, not that it was merely left alone.
            forest.State.inventory=0;forest.Inventory(forest.configuration.islands[0].id).charge=new[]{0,0,0};forest.Inventory(forest.configuration.islands[0].id).rewards.Clear();
            forest.Inventory(forest.configuration.islands[1].id).charge=new[]{0,0,0};
            Assert(game.ImportSave(roundTrip),"A hand-built v3 save reloads");
            Assert(forest.SharedInventory==17,"Shared inventory round-trips exactly");
            Assert(forest.Inventory(forest.configuration.islands[0].id).charge.SequenceEqual(new[]{2,0,1}),"Per-colour receiver charges round-trip exactly");
            Assert(forest.Inventory(forest.configuration.islands[0].id).rewards.Select(q=>q.color).SequenceEqual(new[]{0,-1,2}),"The ordered pending-reward queue (including an uncoloured -1 entry) round-trips exactly");
            Assert(forest.Inventory(forest.configuration.islands[1].id).charge[1]==3,"A second island's independent charge round-trips exactly");

            // ---- Space still only carries bridge blocks - no particle/block grab arbitration left ----
            Assert(game.TrySpawnAtWisdomTree(island.id,out var looseBlock,0),"Spawn a loose block for the Space regression");
            Teleport(game,looseBlock.transform.position+Vector3.right*.6f);
            Assert(game.player.TryGrab(looseBlock.Box)&&game.player.HeldBox==looseBlock.Box,"Space (BoxPlayer.TryGrab) still picks up a nearby bridge block directly");
            game.player.Release();

            // ---- a saved particle sitting on a receiver relocates on reload, id/respawn state intact --
            var relocSlot=forest.Inventory(island.id).slots[5];
            string relocId=relocSlot.id;float relocRemaining=relocSlot.remaining;bool relocPending=relocSlot.pending;int relocGeneration=relocSlot.generation;
            var relocSite=forest.Layouts[0].receivers[0];
            relocSlot.position=relocSite; // sit it exactly on the blue receiver's site

            // Real complete bridge placements; verifies all three tiers on gap two round-trip.
            foreach(var b in game.Blocks){b.gameObject.SetActive(false);UnityEngine.Object.Destroy(b.gameObject);}game.Blocks.Clear();game.Recalculate();
            var place=typeof(LightingIntegrationCheck).GetMethod("PlaceAt",BindingFlags.Static|BindingFlags.NonPublic);
            for(int gap=0;gap<2;gap++)for(int row=0;row<GameManager.Rows;row++)place.Invoke(null,new object[]{game,forest.configuration.islands[gap].id,gap,0,row});
            Assert(game.Bridges[0]&&game.Bridges[1],"Both full mixed-color bridges complete");Assert(game.Blocks.Any(b=>b.gap==1&&b.tier==0),"Second bridge includes blue tiles");
            // ImportSave -> Restore -> Build() is exactly the production path RelocateIfReserved runs on.
            Assert(game.ImportSave(game.ExportSave()),"Complete bridges reload with all permitted colors");
            var relocReloaded=forest.Inventory(island.id).slots.First(s=>s.id==relocId);
            Assert(Vector3.Distance(relocReloaded.position,relocSite)>=LightingParticles.ReceiverClearance,"A saved particle sitting on a receiver's site relocates clear of its interaction area on reload");
            Assert(relocReloaded.remaining==relocRemaining&&relocReloaded.pending==relocPending&&relocReloaded.generation==relocGeneration,"Relocation preserves the particle's id and respawn state (timer/pending/generation untouched)");
            var voices=audio.GetComponentsInChildren<AudioSource>().Where(a=>a.name.StartsWith("Particle pickup voice")).ToArray();
            Assert(voices.Length==4&&voices.All(a=>a.outputAudioMixerGroup!=null&&a.outputAudioMixerGroup.name=="Effects"),"Four pickup voices route through Effects");
            audio.ResetSequence();for(int i=0;i<8;i++)audio.PlayPickup();
            Assert(audio.SequenceStep==3&&voices.All(a=>a.volume<=.5f),"Rapid pickups cap their rising sequence and preserve burst headroom");
            typeof(LightingPickupAudio).GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(audio,new object[]{false});
            Assert(audio.SequenceStep==0,"Focus loss resets pickup sequence");
            audio.PlayPickup();GameHUD.Current.ShowPause();typeof(LightingPickupAudio).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(audio,null);
            Assert(audio.SequenceStep==0,"Pause resets pickup sequence");GameHUD.Current.BeginJourney();
            game.player.readInput=false;game.cameraRig.readInput=false;
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
