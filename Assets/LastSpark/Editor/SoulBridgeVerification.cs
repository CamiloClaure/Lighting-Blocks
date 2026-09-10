using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LightingBlocks.Editor
{
    [InitializeOnLoad]
    public static class SoulBridgeVerification
    {
        static bool running;
        static int ticks;
        static double started;
        static readonly StringBuilder report = new StringBuilder();
        static SoulBridgeVerification()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("SoulBridge.Test", false))
                { running = true; ticks = 0; started = EditorApplication.timeSinceStartup; report.Clear(); var view=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")); view.Show(); view.Focus(); }
                if (state == PlayModeStateChange.EnteredEditMode)
                { running = false; SessionState.SetBool("SoulBridge.Test", false); if (File.Exists("Library/SoulBridge.test-active")) File.Delete("Library/SoulBridge.test-active"); }
            };
        }
        [MenuItem("Tools/Last Spark/Run Soul Bridge Checks")]
        public static void Request() { File.WriteAllText("Library/SoulBridge.test-request", "run"); }
        static void Tick()
        {
            if (!running)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists("Library/SoulBridge.test-request")) return;
                File.Delete("Library/SoulBridge.test-request"); File.WriteAllText("Library/SoulBridge.test-active", "isolate saves");
                SessionState.SetBool("SoulBridge.Test", true); EditorApplication.isPlaying = true; return;
            }
            if (SoulBridgeGame.Instance != null) { SoulBridgeGame.Instance.autoTick = false; SoulBridgeGame.Instance.player.readInput = false; SoulBridgeGame.Instance.cameraRig.readInput = false; }
            if (++ticks == 30)
            {
                try { Verify(); SoulBridgeGame.Instance.StartCoroutine(Capture()); }
                catch (Exception e) { report.AppendLine("FAIL: " + e); Finish(); }
            }
            if (EditorApplication.timeSinceStartup - started > 45) { report.AppendLine("FAIL: render timeout"); Finish(); }
        }
        static void Finish() { File.WriteAllText("Library/SoulBridge.test-result.txt", report.ToString()); running = false; EditorApplication.isPlaying = false; }
        static void Check(bool ok, string description) { if (!ok) throw new Exception(description); report.AppendLine("PASS: " + description); }
        static void Teleport(Vector3 p) { var player=SoulBridgeGame.Instance.player; var c=player.GetComponent<CharacterController>(); c.enabled=false; player.transform.position=p; c.enabled=true; Physics.SyncTransforms(); }
        static void Position(SoulBridgeBlock b, Vector3 p) { b.transform.position=p; b.Box.Body.position=p; Physics.SyncTransforms(); }
        static void WaitForCost(SoulBridgeGame g,string id) { if(g.souls < g.Cost(id)) { Check(g.Rate>0,"Income funds the next upgrade"); g.Advance((float)((g.Cost(id)-g.souls)/g.Rate)+.1f); } }
        static void Place(SoulBridgeGame g,SoulBridgeBlock b,int gap,int column,int row) { Position(b,SoulBridgeGame.Cell(gap,column,row)+Vector3.up*.54f); Check(g.TryPlace(b),"Place connected "+b.Title+" at "+gap+":"+column+":"+row); }
        static void Verify()
        {
            var g=SoulBridgeGame.Instance; var p=g.player; var hud=SoulBridgeHUD.Current;
            Check(g!=null && !g.persistenceEnabled,"Tests isolate the player's Soul Bridge save");
            Check(hud.View==SoulBridgeHUD.ScreenView.Title && Time.timeScale==0,"New scene starts at paused title");
            Check(g.Blocks.Count==20 && !g.Bridges[0] && !g.Bridges[1],"Three shores start with 20 stones and two unbuilt bridges");
            Check(UnityEngine.Object.FindObjectsByType<AstralAltar>().Length==0,"Scene contains no altar circles");
            Check(!Physics.Raycast(new Vector3(0,2,14),Vector3.down,4),"The first rift has no hidden floor or prebuilt bridge");
            Check(p.animator.avatar.isHuman && p.animator.avatar.isValid,"Original animated player is preserved");
            Check(g.cameraRig.distance==27 && g.soulLight.range==9,"Wider camera and initial player light are configured");
            hud.BeginJourney(); string pristine=g.ExportSave();
            var first=g.Blocks.First(b=>b.id=="home-0");
            Check(p.TryGrab(first.Box),"Starting block can be grabbed");
            for(int i=0;i<160;i++)p.StepMovement(Vector2.up,.02f);
            Check(p.transform.position.z<7,"Player stops at unsupported shore while projecting the held block");
            p.Release(); Check(first.placed && first.Connected,"Physical WASD dragging and release creates a walkable bridge tile");
            Check(g.ConnectedCount==1 && Math.Abs(g.PathRate-1)<.001 && g.souls==25,"First connected block starts Souls income and grants one discovery reward");
            double before=g.souls; Check(!g.TryPlace(first)&&g.souls==before,"An anchored tile cannot repeat its placement reward");
            for(int i=0;i<20;i++)p.StepMovement(Vector2.up,.02f);
            Check(p.transform.position.z>7 && p.transform.position.y>-.2f,"Player can actually walk on its placed bridge block");
            var second=g.Blocks.First(b=>b.id=="home-1"); Position(second,SoulBridgeGame.Cell(0,0,3)+Vector3.up*.54f);
            Check(!g.TryPlace(second),"Disconnected blocks cannot become bridge tiles");
            Place(g,second,0,0,1); Check(g.PathRate>2,"Each consecutive block increases Souls generation");
            for(int i=2;i<7;i++)Place(g,g.Blocks.First(b=>b.id=="home-"+i),0,0,i);
            Check(g.Bridges[0] && g.CanStand(new Vector3(0,0,14)) && g.CanStand(new Vector3(0,0,22)),"Seven tiles form a connected walkable path to the second shore");
            Check(g.soulLight.transform.IsChildOf(p.transform),"The lighting soul follows the player");
            Check(UnityEngine.Object.FindObjectsByType<Light>().Where(l=>l.type==LightType.Directional).All(l=>l.intensity<.1f),"Ambient scene lighting stays dark enough for local illumination to matter");
            Check(!g.CanStand(new Vector3(4,0,14)),"Empty cells beside the bridge remain unsafe");
            before=g.souls; hud.ShowPause(); g.Advance(100); Check(g.souls==before && Time.timeScale==0,"Pause freezes Souls generation"); hud.HandleEscape();
            WaitForCost(g,"speed"); float oldSpeed=p.pullSpeed; Check(g.Purchase("speed") && p.pullSpeed>oldSpeed,"Souls buy faster dragging");
            WaitForCost(g,"generator"); double oldRate=g.Rate; Check(g.Purchase("generator")&&Math.Abs(g.GeneratorRate-3)<.001&&g.Rate>oldRate,"Soul Well adds a separate income source");
            WaitForCost(g,"generator"); Check(g.Purchase("generator")&&g.GeneratorRate>7,"Soul Well can be improved to fund later upgrades");
            WaitForCost(g,"radiance"); float radius=g.soulLight.range; Check(g.Purchase("radiance")&&g.soulLight.range==radius+3,"Lighting Soul upgrade increases illumination range");
            var heavy=g.Blocks.First(b=>b.id=="grave-0"); Teleport(heavy.transform.position+new Vector3(0,-.335f,-1.5f));
            Check(!p.TryGrab(heavy.Box)&&g.Notice.Contains("Requires Strength 1"),"Heavy stones explain their required skill level");
            WaitForCost(g,"strength"); Check(g.Purchase("strength")&&p.TryGrab(heavy.Box),"Strength I unlocks Grave Stone hauling"); p.Release();
            var basic=g.Blocks.First(b=>b.id=="home-7"); Position(basic,SoulBridgeGame.Cell(1,0,0)+Vector3.up*.54f);
            Check(!g.TryPlace(basic)&&g.Notice.Contains("Grave Stone"),"Second rift rejects ordinary stones with an explanation");
            Position(basic,new Vector3(-3,.36f,1));
            WaitForCost(g,"links"); Check(g.Purchase("links")&&g.Capacity==2,"Soul Links increases group capacity");
            var extra=g.Blocks.First(b=>b.id=="home-8"); Position(extra,new Vector3(-1.4f,.36f,1)); Teleport(new Vector3(-3,.025f,-.5f));
            Check(p.TryGrab(basic.Box)&&p.HeldBoxes.Count==2,"One grab picks up two neighboring blocks after the upgrade");
            var a=basic.transform.position;var bpos=extra.transform.position; p.StepMovement(Vector2.up,.1f);
            Check(Vector3.Distance(basic.transform.position-a,extra.transform.position-bpos)<.001f&&basic.transform.position!=a,"Both held blocks move together through actual player movement");
            var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.transform.position=new Vector3(-2.2f,.6f,3);obstacle.transform.localScale=new Vector3(4,1.2f,.3f);Physics.SyncTransforms();
            for(int i=0;i<20;i++)p.StepMovement(Vector2.up,.1f);
            Check(basic.transform.position.z<2.2f&&extra.transform.position.z<2.2f,"Group collision sweeps stop every held block before an obstacle");
            obstacle.SetActive(false);UnityEngine.Object.Destroy(obstacle);
            p.Release(); Check(basic.Box.Holder==null&&extra.Box.Holder==null,"Releasing the mouse clears every group member");
            for(int i=0;i<7;i++)Place(g,g.Blocks.First(b=>b.id=="grave-"+i),1,0,i);
            Check(g.Bridges[1]&&g.ConnectedCount==14,"Heavy-stone path connects the second rift to the next area");
            var obs=g.Blocks.First(b=>b.tier==2); Teleport(obs.transform.position+new Vector3(0,-.335f,-1.5f)); Check(!p.TryGrab(obs.Box),"Obsidian remains gated beyond Strength I");
            WaitForCost(g,"strength"); g.Purchase("strength"); WaitForCost(g,"strength"); g.Purchase("strength"); Check(p.TryGrab(obs.Box),"Strength III permits Ancient Obsidian hauling");p.Release();
            Teleport(new Vector3(0,.025f,55)); g.Interact(); Check(g.completed,"Player reaches the next-area beacon after constructing both bridges");
            string saved=g.ExportSave(); before=g.souls; g.souls=0; Check(g.ImportSave(saved)&&Math.Abs(g.souls-before)<.01&&g.ConnectedCount==14&&g.generator==2,"Save roundtrip restores bridges, Souls, upgrades and generators");
            Check(!g.ImportSave("broken JSON"),"Corrupted save is rejected");
            var d=JsonUtility.FromJson<SoulBridgeGame.SaveData>(saved); d.links=99; Check(!g.ImportSave(JsonUtility.ToJson(d)),"Out-of-range skill data is rejected");
            d=JsonUtility.FromJson<SoulBridgeGame.SaveData>(saved); d.blocks[1].gap=d.blocks[0].gap;d.blocks[1].column=d.blocks[0].column;d.blocks[1].row=d.blocks[0].row; Check(!g.ImportSave(JsonUtility.ToJson(d)),"Overlapping saved bridge tiles are rejected");
            d=JsonUtility.FromJson<SoulBridgeGame.SaveData>(saved);d.utc=DateTimeOffset.UtcNow.AddHours(-10).ToUnixTimeSeconds();g.ImportSave(JsonUtility.ToJson(d),true);Check(g.souls-before<=g.Rate*3600*.25+.1,"Offline production is capped at one hour and 25 percent");
            g.savePathOverride=Path.GetFullPath("Library/SoulBridge-test-save.json");g.persistenceEnabled=true;
            try {Check(g.Save(),"Soul Bridge progress writes atomically");g.souls+=1;Check(g.Save()&&File.Exists(g.SavePath+".bak"),"Soul Bridge save keeps a recovery backup");File.WriteAllText(g.SavePath,"broken");g.Load();Check(g.ConnectedCount==14,"Backup restores a corrupt primary save");}
            finally {g.persistenceEnabled=false;g.savePathOverride=null;}
            g.ImportSave(pristine); hud.BeginJourney(); first=g.Blocks.First(b=>b.id=="home-0");
            var mouse=InputSystem.AddDevice<Mouse>("SoulBridgeMouseTest");
            try
            {
                p.readInput=true;Canvas.ForceUpdateCanvases();
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(50,Screen.height-50)}.WithButton(MouseButton.Left));InputSystem.Update();p.SendMessage("Update");Check(p.HeldBox==null,"HUD click does not grab a world block");
                InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();p.SendMessage("Update");
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width/2,Screen.height/2)}.WithButton(MouseButton.Left));InputSystem.Update();p.SendMessage("Update");Check(p.HeldBox!=null,"Actual left mouse input grabs a nearby bridge stone (focused="+Application.isFocused+", nearby="+(p.NearbyBox==null?"none":p.NearbyBox.name)+", menu="+hud.View+")");
                InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();p.SendMessage("Update");Check(p.HeldBox==null,"Actual left mouse release ends hauling");
            }
            finally {p.readInput=false;InputSystem.RemoveDevice(mouse);}
            hud.ToggleSkills();Check(hud.SkillButtons.Count==6&&SoulBridgeHUD.GameplayBlocked&&!SoulBridgeHUD.SimulationPaused,"Accessible skills expose five powers plus a stone source");hud.HandleEscape();hud.ShowPause();hud.ShowControls();Check(hud.ControlsPanel.activeInHierarchy&&!hud.MusicButton.interactable&&!hud.SfxButton.interactable,"Pause and Controls retain placeholder audio options");hud.HandleEscape();hud.HandleEscape();
            Place(g,first,0,0,0);Place(g,g.Blocks.First(b=>b.id=="home-1"),0,0,1);Place(g,g.Blocks.First(b=>b.id=="home-2"),0,0,2);
            Teleport(new Vector3(0,.025f,8));g.cameraRig.CenterOnPlayer();
            report.AppendLine("SUCCESS: Soul Bridge mechanics and persistence passed.");
        }
        static IEnumerator Capture()
        {
            var hud=SoulBridgeHUD.Current;
            foreach(var view in new[]{SoulBridgeHUD.ScreenView.Playing,SoulBridgeHUD.ScreenView.Skills,SoulBridgeHUD.ScreenView.Title,SoulBridgeHUD.ScreenView.Controls})
            {
                hud.SetView(view);yield return new WaitForSecondsRealtime(.5f);yield return new WaitForEndOfFrame();
                Canvas.ForceUpdateCanvases();bool inside=true;var corners=new Vector3[4];
                foreach(var rect in hud.GetComponentsInChildren<RectTransform>()) {rect.GetWorldCorners(corners);foreach(var c in corners)if(c.x<-.5f||c.y<-.5f||c.x>Screen.width+.5f||c.y>Screen.height+.5f)inside=false;}
                if(!inside) {report.AppendLine("FAIL: UI bounds in "+view);Finish();yield break;}
                report.AppendLine("PASS: "+view+" UI fits the rendered screen");
                foreach(var label in hud.GetComponentsInChildren<UnityEngine.UI.Text>()) if(label.name=="Power Requirements"&&label.preferredHeight>label.rectTransform.rect.height+.5f) {report.AppendLine("FAIL: clipped "+label.transform.parent.name);Finish();yield break;}
                var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Library/SoulBridge-"+view+".png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
            }
            Finish();
        }
    }
}
