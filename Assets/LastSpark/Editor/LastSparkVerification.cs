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
    public static class LastSparkVerification
    {
        static int frame;
        static bool running;
        static double next;
        static string pristine;
        static bool captured;
        static readonly StringBuilder report = new StringBuilder();
        static LastSparkVerification()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("LastSpark.Verification",false))
                { running = true; frame = 0; captured = false; report.Clear(); var view = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")); view.Show(); view.Focus(); view.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); }
                if (state == PlayModeStateChange.EnteredEditMode) { running = false; SessionState.SetBool("LastSpark.Verification",false); if(File.Exists("Library/LastSpark.test-active")) File.Delete("Library/LastSpark.test-active"); }
            };
        }
        [MenuItem("Tools/Last Spark/Run MVP Checks")]
        public static void Request() { if (UnityEngine.Object.FindAnyObjectByType<SoulBridgeGame>() != null) { SoulBridgeVerification.Request(); return; } File.WriteAllText("Library/LastSpark.test-request","run"); }
        static void Tick()
        {
            if (!running)
            {
                if(EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
                next = EditorApplication.timeSinceStartup + 1;
                if(!File.Exists("Library/LastSpark.test-request")) return;
                File.Delete("Library/LastSpark.test-request"); File.WriteAllText("Library/LastSpark.test-active","isolate player saves");
                SessionState.SetBool("LastSpark.Verification",true); EditorApplication.isPlaying = true; return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            frame++;
            if(frame < 30 && LastSparkGame.Instance != null)
            {
                LastSparkGame.Instance.autoTick = false;
                LastSparkGame.Instance.player.readInput = false; LastSparkGame.Instance.cameraRig.readInput = false;
            }
            if(frame == 30)
            {
                try { Verify(); }
                catch(Exception e) { report.AppendLine("FAIL: " + e); Debug.LogException(e); }
                File.WriteAllText("Library/LastSpark.test-result.txt",report.ToString());
                LastSparkGame.Instance.StartCoroutine(CapturePreview());
            }
            if(frame >= 110 && captured)
            {
                SessionState.SetBool("LastSpark.Verification",false); running = false; EditorApplication.isPlaying = false;
            }
        }
        static IEnumerator CapturePreview()
        {
            var hud = LastSparkHUD.Current;
            // Let Game view resize and repaint before checking the editor preview,
            // rather than relying only on full-resolution screenshot captures.
            hud.ToggleFullscreen();
            yield return new WaitForSecondsRealtime(.6f);
            CheckPreviewFits("docked Game view");
            hud.ToggleFullscreen();
            yield return new WaitForSecondsRealtime(.6f);
            CheckPreviewFits("expanded Game view");
            hud.BeginJourney(); hud.game.Purchase("ending"); hud.RefreshUI();
            yield return new WaitForSecondsRealtime(.4f);
            yield return new WaitForEndOfFrame();
            var feedback = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes("Library/LastSpark-Feedback.png", feedback.EncodeToPNG());
            UnityEngine.Object.Destroy(feedback);
            var views = new[] { LastSparkHUD.ScreenView.Title, LastSparkHUD.ScreenView.Playing, LastSparkHUD.ScreenView.Skills, LastSparkHUD.ScreenView.Pause, LastSparkHUD.ScreenView.Controls, LastSparkHUD.ScreenView.Memories, LastSparkHUD.ScreenView.ConfirmNewJourney };
            foreach (var view in views)
            {
                hud.SetView(view);
                yield return new WaitForSecondsRealtime(.4f);
                yield return new WaitForEndOfFrame();
                Canvas.ForceUpdateCanvases();
                bool inside = true;
                var corners = new Vector3[4];
                foreach (var rect in hud.GetComponentsInChildren<RectTransform>())
                {
                    rect.GetWorldCorners(corners);
                    foreach (var corner in corners)
                        if (corner.x < -.5f || corner.y < -.5f || corner.x > Screen.width + .5f || corner.y > Screen.height + .5f) inside = false;
                }
                Check(inside, view + " UI rectangles stay inside the rendered screen");
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes("Library/LastSpark-" + view + ".png", texture.EncodeToPNG());
                UnityEngine.Object.Destroy(texture);
            }
            File.WriteAllText("Library/LastSpark.test-result.txt", report.ToString());
            captured = true;
        }
        static void CheckPreviewFits(string label)
        {
            var view = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));
            var settings = new SerializedObject(view);
            var zoom = settings.FindProperty("m_ZoomArea");
            var area = zoom.FindPropertyRelative("m_DrawArea").rectValue;
            var scale = zoom.FindPropertyRelative("m_Scale").vector2Value;
            float width = zoom.FindPropertyRelative("m_HBaseRangeMax").floatValue - zoom.FindPropertyRelative("m_HBaseRangeMin").floatValue;
            float height = zoom.FindPropertyRelative("m_VBaseRangeMax").floatValue - zoom.FindPropertyRelative("m_VBaseRangeMin").floatValue;
            bool fits = width * scale.x <= area.width + .5f && height * scale.y <= area.height + .5f;
            Check(fits, label + " fits " + width + "x" + height + " into " + area.width + "x" + area.height + " at " + scale.x.ToString("0.000") + "x zoom");
        }
        static void Check(bool condition,string description)
        { if(!condition) throw new Exception(description); report.AppendLine("PASS: " + description); }
        static void Teleport(BoxPlayer p,Vector3 position)
        { var cc=p.GetComponent<CharacterController>(); cc.enabled=false; p.transform.position=position; cc.enabled=true; Physics.SyncTransforms(); }
        static void WaitFor(LastSparkGame g,double cost,ref float simulated)
        {
            if(g.essence>=cost) return;
            Check(g.Rate>0,"Progression has positive income before a purchase");
            float wait=(float)((cost-g.essence)/g.Rate)+.05f;
            g.Advance(wait); simulated+=wait;
        }
        static void Verify()
        {
            var g=LastSparkGame.Instance; var p=g.player;
            Check(g!=null && !g.persistenceEnabled,"Tests are isolated from player save files");
            Check(g.altars.Length==16 && g.Fragments.Count==7,"Two-island scene has 16 altars and seven authored fragments");
            Check(!g.bridgeOpen && !g.bridge.activeSelf && g.bridgeBarrier.activeSelf,"Bridge blocks travel before restoration");
            Check(p.animator.avatar.isValid && p.animator.avatar.isHuman,"Supplied animated character is preserved");
            var hud = LastSparkHUD.Current;
            Check(hud != null && hud.View == LastSparkHUD.ScreenView.Title, "New Play session opens the title menu");
            Check(LastSparkHUD.SimulationPaused && Time.timeScale == 0 && AudioListener.pause, "Title pauses gameplay and audio");
            hud.RefreshUI(); Canvas.ForceUpdateCanvases();
            Check(hud.EssenceText.gameObject.activeInHierarchy && hud.EssenceText.text == "0.0", "Essence counter is visible on the title menu");
            hud.ShowControls(); Check(hud.ControlsPanel.activeInHierarchy, "Title menu opens the controls view");
            hud.HandleEscape(); Check(hud.View == LastSparkHUD.ScreenView.Title, "Controls returns to its originating title menu");
            hud.BeginJourney();
            Check(!LastSparkHUD.GameplayBlocked && Time.timeScale == 1 && !AudioListener.pause, "Begin Journey resumes gameplay");
            pristine=g.ExportSave();
            var first=g.Fragments.First(f=>f.id=="star-free-0");
            Check(p.TryGrab(first.Box),"Starting fragment is within reach");
            for(int i=0;i<53;i++) p.StepMovement(Vector2.up,.02f);
            p.Release();
            Check(first.Altar==g.altars[0],"Physical WASD dragging and release snaps onto the first altar");
            Check(Math.Abs(g.Rate-1)<.001 && Math.Abs(g.essence-25)<.001,"First activation generates 1 Essence/sec and a one-time 25 Essence reward");
            hud.RefreshUI(); Check(hud.EssenceText.text == "25.0", "Essence display updates immediately after the discovery reward");
            hud.HandleEscape(); var stoppedPosition = p.transform.position;
            double pausedEssence = g.essence; g.Advance(100); p.StepMovement(Vector2.up, 1);
            Check(hud.View == LastSparkHUD.ScreenView.Pause && Time.timeScale == 0 && AudioListener.pause, "Escape opens pause and stops time and audio");
            Check(g.essence == pausedEssence && p.transform.position == stoppedPosition, "Pause prevents income and player movement");
            Check(!p.TryGrab(first.Box) && !g.Purchase("star"), "Pause blocks grabbing and gameplay purchases");
            Check(!hud.MusicButton.interactable && !hud.SfxButton.interactable, "Music and SFX are disabled placeholders");
            hud.ShowControls(); Check(hud.ControlsPanel.activeInHierarchy && LastSparkHUD.SimulationPaused, "Controls remains paused");
            hud.HandleEscape(); Check(hud.View == LastSparkHUD.ScreenView.Pause, "Escape from pause controls returns to pause");
            hud.HandleEscape(); Check(hud.View == LastSparkHUD.ScreenView.Playing && Time.timeScale == 1, "Escape resumes play");
            Check(p.TryGrab(first.Box),"Attuned fragments remain movable");
            Check(g.Rate==0,"Held fragments stop generating income");
            p.Release(); Check(Math.Abs(g.essence-25)<.001,"Reattuning does not repeat the discovery reward");
            g.Advance(10); Check(Math.Abs(g.essence-35)<.01,"Income is calculated from elapsed time");
            hud.RefreshUI(); Check(hud.EssenceText.text == "35.0", "Essence display reflects passive income");
            double before=g.essence; Check(!g.Purchase("bridge") && g.essence==before,"Locked purchases cannot spend currency");
            Check(g.Purchase("star"),"Affordable summon produces a physical fragment");
            Check(g.Fragments.Count==8 && Math.Abs(g.essence-15)<.01,"Summon price is deducted exactly once");
            before=g.essence; Check(!g.Purchase("resonance") && g.essence==before,"Unaffordable upgrades cannot spend currency");
            var freeStars=g.Fragments.Where(f=>f.kind==FragmentKind.Starlight && f!=first).ToArray();
            // Seat the next stars around the central altar to verify the Life adjacency calculation.
            g.Place(freeStars[0],g.altars[1]); g.Place(freeStars[1],g.altars[2]);
            var life=g.Fragments.First(f=>f.kind==FragmentKind.Life);
            g.Place(life,g.altars[3]);
            Check(Math.Abs(g.Rate-3.5)<.01,"Life boosts only generators inside its 4.5m radius");
            float simulated=10;
            WaitFor(g,g.ResonanceCost,ref simulated); Check(g.Purchase("resonance"),"First production upgrade purchased with earned currency");
            Check(Math.Abs(g.Rate-4.9)<.02,"Resonance multiplies the full production total");
            WaitFor(g,g.ResonanceCost,ref simulated); Check(g.Purchase("resonance"),"Second production upgrade unlocks bridge eligibility");
            WaitFor(g,g.balance.spiritCost,ref simulated); Check(g.Purchase("spirit"),"Wisp automation unlocks after three attunements");
            var loose=g.Fragments.First(f=>f.kind==FragmentKind.Starlight && f.Altar==null);
            int previousCount=g.ActiveCount; g.Advance(g.balance.spiritInterval+.1f); simulated+=g.balance.spiritInterval;
            Check(g.ActiveCount>previousCount,"Wisp delivers a loose fragment to a free altar");
            WaitFor(g,g.balance.bridgeCost,ref simulated); Check(g.Purchase("bridge"),"Bridge restoration can be earned through the economy");
            Check(g.bridge.activeSelf && !g.bridgeBarrier.activeSelf && g.outerFragments.activeSelf,"Restoration opens a physical route and reveals Ancient Hearts");
            var heart=g.Fragments.First(f=>f.kind==FragmentKind.Ancient);
            Teleport(p,heart.transform.position+new Vector3(0,-.595f,-1.5f));
            Check(!p.TryGrab(heart.Box),"Ancient Hearts require Astral Strength");
            WaitFor(g,g.StrengthCost,ref simulated); Check(g.Purchase("strength"),"Strength is purchased using generated Essence");
            Check(p.pullSpeed>2.3f && p.TryGrab(heart.Box),"Strength improves handling and permits heavy-fragment grabbing");
            p.Release();
            g.Place(heart,g.altars[8]);
            g.Place(g.Fragments.First(f=>f.kind==FragmentKind.Ancient && f!=heart),g.altars[9]);
            Check(g.AncientCount==2,"Both Ancient Hearts must be attuned on the far island");
            Teleport(p,new Vector3(0,.025f,29.4f));
            WaitFor(g,g.balance.finalCost,ref simulated); double rate=g.Rate;
            Check(g.Purchase("ending") && g.completed,"The player can reach and complete the final shrine");
            Check(Math.Abs(g.Rate-rate*2)<.1,"Completing the chapter doubles production");
            report.AppendLine("Economy-only waiting along tested route: " + simulated.ToString("0") + " seconds (excludes player travel and decisions).");
            string saved=g.ExportSave(); double currency=g.essence; int count=g.Fragments.Count;
            g.essence=0; g.resonance=0; Check(g.ImportSave(saved),"Valid JSON save loads successfully");
            Check(Math.Abs(g.essence-currency)<.01 && g.completed && g.Fragments.Count==count,"Save/load restores currency, progression and fragment inventory without duplicates");
            var data=JsonUtility.FromJson<LastSparkGame.SaveData>(saved); data.savedUtc=DateTimeOffset.UtcNow.AddHours(-10).ToUnixTimeSeconds();
            Check(g.ImportSave(JsonUtility.ToJson(data),true),"Offline save loads");
            Check(g.essence-currency<=g.Rate*7200*.5+.1,"Offline earnings are capped at two hours and 50 percent production");
            string corrupt="{broken"; before=g.essence; Check(!g.ImportSave(corrupt) && g.essence==before,"Malformed saves are rejected without changing live state");
            data.essence=double.NaN; Check(!g.ImportSave(JsonUtility.ToJson(data)),"Invalid numeric save values are rejected");
            g.ImportSave(saved);
            g.savePathOverride=Path.GetFullPath("Library/LastSpark-verification-save.json"); g.persistenceEnabled=true;
            try
            {
                Check(g.Save() && File.Exists(g.SavePath),"Atomic save writes a readable file to disk");
                g.essence+=10; Check(g.Save() && File.Exists(g.SavePath+".bak"),"Subsequent save retains a backup");
                File.WriteAllText(g.SavePath,"broken JSON"); g.essence=0; g.Load();
                Check(g.completed && g.Rate>0,"Corrupted primary save recovers from the backup file");
            }
            finally { g.persistenceEnabled=false; g.savePathOverride=null; }
            g.ImportSave(pristine); Teleport(p,new Vector3(0,.025f,-4.8f));
            hud.RefreshUI(); Canvas.ForceUpdateCanvases();
            var mouse=InputSystem.AddDevice<Mouse>("LastSparkTestMouse");
            try
            {
                p.readInput=true;
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(50,Screen.height-50)}.WithButton(MouseButton.Left)); InputSystem.Update(); p.SendMessage("Update");
                Check(p.HeldBox==null,"Clicking the HUD cannot grab a fragment behind it");
                InputSystem.QueueStateEvent(mouse,new MouseState()); InputSystem.Update(); p.SendMessage("Update");
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width/2,Screen.height/2)}.WithButton(MouseButton.Left)); InputSystem.Update(); p.SendMessage("Update");
                Check(p.HeldBox!=null,"A world mouse press grabs the nearby fragment");
                InputSystem.QueueStateEvent(mouse,new MouseState()); InputSystem.Update(); p.SendMessage("Update");
                Check(p.HeldBox==null,"Releasing the mouse releases the fragment");
            }
            finally { p.readInput=false; InputSystem.RemoveDevice(mouse); }
            g.cameraRig.CenterOnPlayer(); g.cameraRig.Pan(new Vector2(1000,1000));
            Check(g.cameraRig.Focus.x<=14 && g.cameraRig.Focus.z<=37,"Camera bounds cover both islands");
            g.cameraRig.CenterOnPlayer();
            // Preview the first meaningful reward state, without writing a player save.
            first=g.Fragments.First(f=>f.id=="star-free-0"); g.Place(first,g.altars[0]);
            hud.SkillsButton.onClick.Invoke();
            Check(hud.View == LastSparkHUD.ScreenView.Skills && LastSparkHUD.GameplayBlocked && !LastSparkHUD.SimulationPaused, "Skill tree button opens powers and blocks world input while income continues");
            Check(hud.SkillButtons.Count == 6 && hud.SkillButtons["bridge"].GetComponentInChildren<UnityEngine.UI.Text>().text.Contains("LOCKED"), "Skill tree displays all six powers with explicit locked labels");
            before = g.essence; hud.SkillButtons["bridge"].onClick.Invoke(); hud.RefreshUI();
            Check(g.essence == before && hud.FeedbackText.gameObject.activeInHierarchy && hud.FeedbackText.text.Contains("STARBRIDGE LOCKED"), "Clicking a locked skill explains its requirements without spending Essence");
            before = g.essence; g.Advance(100); hud.RefreshUI();
            Check(g.essence > before && hud.SkillButtons["resonance"].interactable, "Skill tree continues production and updates affordability");
            int rank = g.resonance; hud.SkillButtons["resonance"].onClick.Invoke();
            Check(g.resonance == rank + 1, "Skill tree purchase button buys its actual upgrade");
            hud.HandleEscape(); Check(hud.View == LastSparkHUD.ScreenView.Playing, "Escape closes the skill tree");
            Teleport(p, first.transform.position + new Vector3(0, -.595f, -1.5f));
            Check(p.TryGrab(first.Box), "Nearby attuned fragment can be grabbed before opening menus");
            hud.ToggleSkills(); Check(p.HeldBox == null, "Opening the skill tree releases a held fragment");
            hud.ToggleSkills(); Check(hud.View == LastSparkHUD.ScreenView.Playing, "Skill shortcut toggles back to play");
            before = g.essence; hud.ShowTitle(); g.Advance(100);
            Check(g.essence == before && LastSparkHUD.SimulationPaused, "Returning to title preserves currency and freezes income");
            hud.BeginJourney(); Check(g.essence == before, "Continue Journey retains the existing session");
            var keyboard = InputSystem.AddDevice<Keyboard>("LastSparkMenuTestKeyboard");
            try
            {
                PressMenuKey(hud, keyboard, Key.Escape);
                Check(hud.View == LastSparkHUD.ScreenView.Pause, "Actual Escape input opens pause");
                PressMenuKey(hud, keyboard, Key.Escape);
                Check(hud.View == LastSparkHUD.ScreenView.Playing, "Actual Escape input resumes gameplay");
                PressMenuKey(hud, keyboard, Key.Tab);
                Check(hud.View == LastSparkHUD.ScreenView.Skills, "Actual Tab input opens the skill tree");
                Canvas.ForceUpdateCanvases();
                foreach (var label in hud.GetComponentsInChildren<UnityEngine.UI.Text>())
                    if (label.name == "Power Requirements") Check(label.preferredHeight <= label.rectTransform.rect.height + .5f, "Skill description fits: " + label.transform.parent.name);
                PressMenuKey(hud, keyboard, Key.K);
                Check(hud.View == LastSparkHUD.ScreenView.Playing, "Actual K input closes the skill tree");
                var gameView = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));
                bool wasFull = gameView.maximized;
                PressMenuKey(hud, keyboard, Key.F1);
                Check(gameView.maximized != wasFull, "Actual F1 input toggles expanded Game view");
                PressMenuKey(hud, keyboard, Key.F1);
                Check(gameView.maximized == wasFull, "A second F1 press restores the previous view");
                hud.ShowPause(); PressMenuKey(hud, keyboard, Key.F1);
                Check(gameView.maximized != wasFull && Time.timeScale == 0, "F1 works while paused without resuming gameplay");
                PressMenuKey(hud, keyboard, Key.F1); hud.HandleEscape();
                VerifyShrineFeedback(g, hud, keyboard);
            }
            finally { InputSystem.RemoveDevice(keyboard); }
            g.Notify("FIRST LIGHT  +25 Essence\nMemory I: The universe did not die. Someone put it to sleep.");
            p.StepMovement(Vector2.zero,.02f);
            report.AppendLine("SUCCESS: all Last Spark MVP checks passed.");
        }
        static void PressMenuKey(LastSparkHUD hud, Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); InputSystem.Update(); hud.SendMessage("Update");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        }
        static void VerifyShrineFeedback(LastSparkGame g, LastSparkHUD hud, Keyboard keyboard)
        {
            string snapshot = g.ExportSave(); var p = g.player;
            try
            {
                p.readInput = true;
                PressShrineKey(g, keyboard); hud.RefreshUI();
                Check(hud.FeedbackText.gameObject.activeInHierarchy && hud.FeedbackText.text.Contains("First restore the Starbridge"), "Actual E input explains the locked bridge and left-click grab control");
                g.bridgeOpen = true; g.ApplyUnlocks();
                PressShrineKey(g, keyboard);
                Check(g.Notice.Contains("OUT OF REACH"), "E outside shrine range gives directions and distance");
                Teleport(p, new Vector3(0, .025f, 29.4f));
                PressShrineKey(g, keyboard);
                Check(g.Notice.Contains("0/2"), "E at the shrine reports missing Ancient Hearts");
                var hearts = g.Fragments.Where(f => f.kind == FragmentKind.Ancient).ToArray();
                g.Place(hearts[0], g.altars[8]); g.Place(hearts[1], g.altars[9]); g.essence = 0;
                PressShrineKey(g, keyboard); hud.RefreshUI();
                Check(g.Notice.Contains("NOT ENOUGH ESSENCE") && !g.completed && g.essence == 0, "E explains insufficient shrine currency without changing progress");
                Check(hud.FeedbackText.gameObject.activeInHierarchy && hud.FeedbackText.text == g.Notice, "Failed E feedback is actually visible in the HUD");
                g.essence = g.balance.finalCost;
                PressShrineKey(g, keyboard);
                Check(g.completed && g.essence == 0 && g.Notice.Contains("Chapter complete"), "Actual E awakens a ready shrine, charges once and confirms success");
                PressShrineKey(g, keyboard);
                Check(g.essence == 0 && g.Notice.Contains("ALREADY AWAKENED"), "Repeated E explains completion without another charge");
            }
            finally { p.readInput = false; g.ImportSave(snapshot); }
        }
        static void PressShrineKey(LastSparkGame g, Keyboard keyboard)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E)); InputSystem.Update(); g.SendMessage("Update");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        }
    }
}
