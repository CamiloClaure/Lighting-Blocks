using System;
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
            Assert(system.Particles.GroupBy(p=>p.Slot.color).Select(g=>g.Count()).Distinct().Count()==1,"Balanced particle colors");
            Assert(!forest.TryCollect(0),"Walk-over pickup disabled");
            var receiver=system.Receivers[0];var owner=forest.Inventory(receiver.Tree.Definition.id);owner.treeCharge=0;
            var wrong=system.Particles.First(p=>p.Owner==owner&&p.Slot.color!=receiver.ColorIndex);wrong.Slot.remaining=0;wrong.Slot.pending=false;wrong.gameObject.SetActive(true);
            Assert(!receiver.Receive(wrong)&&owner.treeCharge==0,"Wrong color does not consume or charge");
            for(int i=0;i<3;i++)
            {
                var particle=system.Particles.First(p=>p.Owner==owner&&p.Slot.color==i);particle.Slot.remaining=0;particle.Slot.pending=false;particle.gameObject.SetActive(true);
                var matching=system.Receivers.First(r=>r.Tree==receiver.Tree&&r.ColorIndex==i);
                Assert(matching.Receive(particle),"Accepted matching color "+i);
                Assert(!matching.Receive(particle),"Duplicate delivery refused "+i);
            }
            Assert(owner.treeCharge==3,"Three deliveries fill tree");
            // Fill every output slot, then ensure banked charge survives and resumes once clear.
            var obstacles=receiver.Tree.Definition.outputOffsets.Select(offset=>{var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.transform.position=receiver.Tree.Definition.wisdomPosition+offset;o.transform.localScale=Vector3.one*.8f;return o;}).ToArray();Physics.SyncTransforms();
            int before=game.Blocks.Count;receiver.Tree.Tick(1);
            Assert(owner.treeCharge==3&&game.Blocks.Count==before,"Blocked outputs retain full charge");
            foreach(var o in obstacles){o.SetActive(false);UnityEngine.Object.Destroy(o);}Physics.SyncTransforms();receiver.Tree.Tick(1);
            Assert(owner.treeCharge==0&&game.Blocks.Count==before+1,"Cleared outputs release exactly one tile");receiver.Tree.Tick(1);Assert(game.Blocks.Count==before+1,"No duplicate tile");
            owner.treeCharge=2;string partial=game.ExportSave();owner.treeCharge=0;Assert(game.ImportSave(partial),"Reload partial charge");
            system=game.GetComponent<LightingParticles>();owner=forest.Inventory(receiver.Tree.Definition.id);Assert(owner.treeCharge==2,"Partial charge restored");
            var legacy=JsonUtility.FromJson<GameManager.SaveData>(partial);legacy.forest.particleVersion=0;foreach(var hub in legacy.forest.islands){hub.treeCharge=0;hub.inventory=7;}
            Assert(game.ImportSave(JsonUtility.ToJson(legacy)),"Previous Lighting save migrates");Assert(forest.State.islands.All(h=>h.treeCharge==7&&h.inventory==0),"Old inventory becomes banked charge");
            system=game.GetComponent<LightingParticles>();var target=system.Particles.First();target.Slot.remaining=0;target.Slot.pending=false;target.gameObject.SetActive(true);target.transform.position=target.Slot.position+Vector3.up*.7f;
            Teleport(game,target.Slot.position+Vector3.right*.7f);Assert(system.TryGrab(game.player,null),"Space arbitration can select a nearby particle");
            Vector3 saved=system.Held.Slot.position;system.Held.transform.position+=Vector3.right*100;system.Cancel();Assert(system.Held==null&&target.Slot.position==saved,"Canceled drag preserves last supported position");
            // Exercise release/cancel paths through the same component used by Space input.
            var right=system.Receivers.First(r=>r.ColorIndex==target.Slot.color&&r.Tree.Definition.id==target.Owner.id);
            var bad=system.Receivers.First(r=>r.ColorIndex!=target.Slot.color&&r.Tree==right.Tree);
            target.Owner.treeCharge=0;target.transform.position=target.Slot.position+Vector3.up*.7f;
            Assert(system.TryGrab(game.player,null),"Grab for wrong receiver test");system.Held.transform.position=bad.transform.position;system.Drop();
            Assert(target.gameObject.activeSelf&&target.Owner.treeCharge==0&&target.transform.position==target.Slot.position+Vector3.up*.7f,"Wrong receiver drop returns particle without loss");
            Assert(system.TryGrab(game.player,null),"Grab for accepted delivery test");system.Held.transform.position=right.transform.position;system.Drop();
            Assert(target.Owner.treeCharge==1&&!target.gameObject.activeSelf,"Space release delivers into matching receiver");
            target.Slot.remaining=0;target.Slot.pending=true;forest.Tick(.6f,false);
            Assert(!target.Slot.pending&&target.gameObject.activeSelf,"Delivered particle respawns on supported ground");
            Teleport(game,target.Slot.position+Vector3.right*.7f);target.transform.position=target.Slot.position+Vector3.up*.7f;
            Assert(system.TryGrab(game.player,null),"Grab before save/pause");saved=target.Slot.position;system.Held.transform.position+=Vector3.right*100;
            var airborneSave=JsonUtility.FromJson<GameManager.SaveData>(game.ExportSave());
            Assert(airborneSave.forest.islands.First(h=>h.id==target.Owner.id).slots.First(p=>p.id==target.Slot.id).position==saved,"In-flight save uses supported particle position");
            GameHUD.Current.ShowPause();system.SendMessage("LateUpdate");Assert(system.Held==null&&target.transform.position==saved+Vector3.up*.7f,"Pause cancels particle carry safely");GameHUD.Current.BeginJourney();
            var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.transform.localScale=Vector3.one*.2f;obstacle.transform.position=target.transform.position+Vector3.right*.35f;Physics.SyncTransforms();
            Assert(!system.TryGrab(game.player,null),"Obstruction prevents particle pickup");obstacle.SetActive(false);UnityEngine.Object.Destroy(obstacle);
            Assert(game.TrySpawnAtWisdomTree(target.Owner.id,out var competing,0),"Spawn competing block");competing.transform.position=game.player.transform.position+Vector3.right*.1f+Vector3.up*.26f;competing.Box.Body.position=competing.transform.position;Physics.SyncTransforms();
            Assert(!system.TryGrab(game.player,competing.Box)&&system.Held==null,"Closer loose block wins shared grab arbitration");
            // Real complete bridge placements; verifies all three tiers on gap two round-trip.
            foreach(var b in game.Blocks){b.gameObject.SetActive(false);UnityEngine.Object.Destroy(b.gameObject);}game.Blocks.Clear();game.Recalculate();
            var place=typeof(LightingIntegrationCheck).GetMethod("PlaceAt",BindingFlags.Static|BindingFlags.NonPublic);
            for(int gap=0;gap<2;gap++)for(int row=0;row<GameManager.Rows;row++)place.Invoke(null,new object[]{game,forest.configuration.islands[gap].id,gap,0,row});
            Assert(game.Bridges[0]&&game.Bridges[1],"Both full mixed-color bridges complete");Assert(game.Blocks.Any(b=>b.gap==1&&b.tier==0),"Second bridge includes blue tiles");
            Assert(game.ImportSave(game.ExportSave()),"Complete bridges reload with all permitted colors");
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
