using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LightingBlocks
{
    // A Canvas UI avoids native GUIStyle state being lost during Unity script reloads.
    [DefaultExecutionOrder(-1000)]
    public sealed class LastSparkHUD : MonoBehaviour
    {
        public enum ScreenView { Title, Playing, Pause, Controls, Skills, Memories, ConfirmNewJourney }
        public LastSparkGame game;
        public ScreenView View { get; private set; } = ScreenView.Title;
        public static LastSparkHUD Current { get; private set; }
        public static bool GameplayBlocked => SoulBridgeHUD.Current != null ? SoulBridgeHUD.GameplayBlocked : Current != null && Current.View != ScreenView.Playing;
        public static bool SimulationPaused => SoulBridgeHUD.Current != null ? SoulBridgeHUD.SimulationPaused : Current != null && Current.View != ScreenView.Playing && Current.View != ScreenView.Skills && Current.View != ScreenView.Memories;
        public Text EssenceText { get; private set; }
        public Text FeedbackText => noticeText;
        public Button SkillsButton { get; private set; }
        public Button MusicButton { get; private set; }
        public Button SfxButton { get; private set; }
        public GameObject ControlsPanel => controlsPanel;
        public readonly Dictionary<string, Button> SkillButtons = new Dictionary<string, Button>();
        Font font;
        RectTransform canvas;
        GameObject playRoot, navigation, shade, titlePanel, pausePanel, controlsPanel, skillsPanel, memoriesPanel, confirmPanel, feedbackPanel;
        Text incomeText, objectiveText, contextText, noticeText, journalText, journeyLabel;
        Button startButton, newButton, resumeButton, controlsBack, skillsClose, memoriesClose;
        ScreenView controlsReturn = ScreenView.Title, confirmReturn = ScreenView.Title;
        readonly List<PurchaseBinding> purchases = new List<PurchaseBinding>();
        readonly List<RaycastResult> raycasts = new List<RaycastResult>();
        PointerEventData pointer;
        EventSystem eventSystem;
        float previousTimeScale = 1;
        bool previousAudioPause;
#if UNITY_EDITOR
        UnityEditor.EditorWindow fullscreenView;
        bool wasMaximized;
#else
        int windowWidth = 1280, windowHeight = 720;
#endif
        static readonly Color Ink = new Color(.025f, .04f, .09f, .98f);
        static readonly Color Card = new Color(.065f, .09f, .15f, .98f);
        static readonly Color White = new Color(.91f, .95f, 1);
        static readonly Color Muted = new Color(.62f, .72f, .84f);
        static readonly Color Mint = new Color(.43f, 1, .8f);
        class PurchaseBinding { public string id, description; public Button button; public Text detail; }

        void Awake()
        {
            Current = this;
            previousTimeScale = Time.timeScale > 0 ? Time.timeScale : 1;
            previousAudioPause = AudioListener.pause;
            View = ScreenView.Title;
            Time.timeScale = 0; AudioListener.pause = true;
        }
        void Start()
        {
            BuildUI(); SetView(ScreenView.Title);
        }
        void OnEnable() { Current = this; }
        void OnDisable()
        {
            if (Current != this) return;
            Current = null; Time.timeScale = previousTimeScale; AudioListener.pause = previousAudioPause;
#if UNITY_EDITOR
            if (fullscreenView != null) fullscreenView.maximized = wasMaximized;
#endif
        }
        void Update()
        {
            if (canvas == null || Keyboard.current == null || !Application.isFocused) return;
            var keys = Keyboard.current;
            if (keys.f1Key.wasPressedThisFrame) ToggleFullscreen();
            if (keys.escapeKey.wasPressedThisFrame) HandleEscape();
            else if (keys.tabKey.wasPressedThisFrame || keys.kKey.wasPressedThisFrame) ToggleSkills();
        }
        void LateUpdate() { if (canvas != null && game != null) RefreshUI(); }

        public void ToggleFullscreen()
        {
#if UNITY_EDITOR
            if (fullscreenView == null)
            {
                fullscreenView = UnityEditor.EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));
                wasMaximized = fullscreenView.maximized;
            }
            fullscreenView.Show(); fullscreenView.Focus(); fullscreenView.maximized = !fullscreenView.maximized;
            game.Notify(fullscreenView.maximized ? "EXPANDED GAME VIEW\nPress F1 again to restore the editor layout." : "EDITOR LAYOUT RESTORED\nPress F1 to expand the Game view.", false);
#else
            if (Screen.fullScreen)
            {
                Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.Windowed);
                game.Notify("WINDOWED MODE\nPress F1 for fullscreen.", false);
            }
            else
            {
                windowWidth = Screen.width; windowHeight = Screen.height;
                Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
                game.Notify("FULLSCREEN\nPress F1 to return to windowed mode.", false);
            }
#endif
        }

        public void BeginJourney()
        {
            SetView(ScreenView.Playing);
            if (!game.firstLight) game.Notify("Drag the blue fragment onto the first circular altar, then release.", false);
        }
        public void ShowPause() { if (View != ScreenView.Title) { game.Save(); SetView(ScreenView.Pause); } }
        public void ShowTitle() { game.Save(); SetView(ScreenView.Title); }
        public void ShowControls()
        {
            controlsReturn = View == ScreenView.Title ? ScreenView.Title : ScreenView.Pause;
            SetView(ScreenView.Controls);
        }
        public void BackFromControls() { SetView(controlsReturn); }
        public void ToggleSkills()
        {
            if (View == ScreenView.Skills) SetView(ScreenView.Playing);
            else if (View == ScreenView.Playing || View == ScreenView.Memories) SetView(ScreenView.Skills);
        }
        public void HandleEscape()
        {
            switch (View)
            {
                case ScreenView.Playing: ShowPause(); break;
                case ScreenView.Pause: SetView(ScreenView.Playing); break;
                case ScreenView.Controls: BackFromControls(); break;
                case ScreenView.Skills:
                case ScreenView.Memories: SetView(ScreenView.Playing); break;
                case ScreenView.ConfirmNewJourney: SetView(confirmReturn); break;
            }
        }
        public void SetView(ScreenView next)
        {
            View = next;
            if (GameplayBlocked && game != null) game.player.Release();
            Time.timeScale = SimulationPaused ? 0 : previousTimeScale;
            AudioListener.pause = SimulationPaused || previousAudioPause;
            if (canvas == null) return;
            bool live = next == ScreenView.Playing || next == ScreenView.Skills || next == ScreenView.Memories;
            playRoot.SetActive(next == ScreenView.Playing); navigation.SetActive(live); shade.SetActive(next != ScreenView.Playing);
            titlePanel.SetActive(next == ScreenView.Title); pausePanel.SetActive(next == ScreenView.Pause);
            controlsPanel.SetActive(next == ScreenView.Controls); skillsPanel.SetActive(next == ScreenView.Skills);
            memoriesPanel.SetActive(next == ScreenView.Memories); confirmPanel.SetActive(next == ScreenView.ConfirmNewJourney);
            GameObject selection = next == ScreenView.Title ? startButton.gameObject : next == ScreenView.Pause ? resumeButton.gameObject
                : next == ScreenView.Controls ? controlsBack.gameObject : next == ScreenView.Skills ? skillsClose.gameObject
                : next == ScreenView.Memories ? memoriesClose.gameObject : null;
            eventSystem.SetSelectedGameObject(selection);
            RefreshUI();
        }
        public static bool PointerOverUI()
        {
            if (SoulBridgeHUD.Current != null) return SoulBridgeHUD.PointerOverUI();
            if (Current == null) return false;
            if (GameplayBlocked) return true;
            if (Mouse.current == null || Current.eventSystem == null) return false;
            Current.pointer.position = Mouse.current.position.ReadValue();
            Current.raycasts.Clear(); Current.eventSystem.RaycastAll(Current.pointer, Current.raycasts);
            return Current.raycasts.Count > 0;
        }

        void BuildUI()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("Last Spark Canvas UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false); canvas = root.GetComponent<RectTransform>();
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; root.GetComponent<Canvas>().sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 1000); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            eventSystem = FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var events = new GameObject("Last Spark EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false); eventSystem = events.GetComponent<EventSystem>();
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            pointer = new PointerEventData(eventSystem);
            playRoot = FullRect("Gameplay HUD", canvas).gameObject;
            var mission = Panel("Current Objective", playRoot.transform, 22, 0, 418, 158, Ink);
            Bottom(mission, 22, 24, false);
            Label("Objective Heading", mission, "YOUR NEXT DISCOVERY", 18, 14, 380, 26, 17, Mint);
            objectiveText = Label("Objective", mission, "", 18, 45, 380, 100, 18, White);
            var context = Panel("Interaction Hint", playRoot.transform, 0, 0, 540, 84, Ink);
            context.anchorMin = context.anchorMax = new Vector2(.5f, 0); context.pivot = new Vector2(.5f, 0); context.anchoredPosition = new Vector2(0, 24);
            contextText = Label("Interaction", context, "", 18, 12, 504, 60, 19, White, TextAnchor.MiddleCenter);
            var calls = Panel("Fragment Calls", playRoot.transform, 0, 0, 335, 158, Ink); Bottom(calls, 22, 24, true);
            Label("Call Heading", calls, "CALL FRAGMENTS", 16, 12, 300, 24, 17, Mint);
            AddPurchase("star", calls, 14, 44, 307, 46, null, "");
            AddPurchase("life", calls, 14, 99, 307, 46, null, "");

            shade = FullRect("Menu Backdrop", canvas).gameObject; shade.AddComponent<Image>().color = new Color(.008f, .016f, .04f, .9f);
            BuildTitle(); BuildPause(); BuildControls(); BuildSkills(); BuildMemories(); BuildConfirmation();

            // This panel is above every modal so Essence is always visible, including during pause and skill selection.
            var wallet = Panel("Essence Counter", canvas, 22, 20, 350, 132, Ink);
            Label("Essence Label", wallet, "ESSENCE", 20, 12, 310, 23, 17, Mint);
            EssenceText = Label("Essence Amount", wallet, "0.0", 20, 37, 310, 52, 40, Mint);
            EssenceText.resizeTextForBestFit = true; EssenceText.resizeTextMinSize = 22; EssenceText.resizeTextMaxSize = 40;
            incomeText = Label("Essence Income", wallet, "+0.0 / sec", 20, 94, 310, 26, 18, White);
            navigation = Rect("Quick Menu", canvas, 0, 0, 440, 58).gameObject;
            var nav = navigation.GetComponent<RectTransform>(); nav.anchorMin = nav.anchorMax = new Vector2(1, 1); nav.pivot = new Vector2(1, 1); nav.anchoredPosition = new Vector2(-22, -20);
            SkillsButton = Button("Skill Tree Shortcut", nav, "SKILL TREE  [TAB]", 0, 0, 202, 58, ToggleSkills);
            Button("Memories Shortcut", nav, "MEMORIES", 212, 0, 116, 58, () => SetView(View == ScreenView.Memories ? ScreenView.Playing : ScreenView.Memories));
            Button("Pause Shortcut", nav, "ESC", 338, 0, 102, 58, ShowPause);
            var feedback = Panel("Action Feedback", canvas, 0, 0, 620, 108, Ink);
            feedback.anchorMin = feedback.anchorMax = new Vector2(.5f, 1); feedback.pivot = new Vector2(.5f, 1); feedback.anchoredPosition = new Vector2(0, -18);
            feedback.GetComponent<Image>().raycastTarget = false; feedbackPanel = feedback.gameObject;
            noticeText = Label("Action Feedback Text", feedback, "", 16, 10, 588, 88, 20, Mint, TextAnchor.MiddleCenter);
            noticeText.resizeTextForBestFit = true; noticeText.resizeTextMinSize = 16; noticeText.resizeTextMaxSize = 20;
        }
        void BuildTitle()
        {
            var panel = CenterPanel("Title Menu", 600, 610); titlePanel = panel.gameObject;
            Label("Title Eyebrow", panel, "AT THE END OF THE UNIVERSE", 40, 35, 520, 26, 17, Mint, TextAnchor.MiddleCenter);
            Label("Game Title", panel, "THE LAST\nSPARK", 30, 79, 540, 146, 64, White, TextAnchor.MiddleCenter);
            Label("Game Lore", panel, "Gather the fragments. Awaken forgotten powers.\nDiscover what the universe left behind.", 55, 244, 490, 65, 20, Muted, TextAnchor.MiddleCenter);
            startButton = Button("Begin Journey", panel, "BEGIN JOURNEY", 70, 332, 460, 58, BeginJourney);
            journeyLabel = startButton.GetComponentInChildren<Text>();
            newButton = Button("New Journey", panel, "NEW JOURNEY", 70, 400, 460, 48, () => ConfirmNew(ScreenView.Title));
            Button("Title Controls", panel, "CONTROLS", 70, 458, 460, 48, ShowControls);
            Button("Quit", panel, "QUIT", 70, 516, 460, 48, Quit);
        }
        void BuildPause()
        {
            var panel = CenterPanel("Pause Menu", 540, 540); pausePanel = panel.gameObject;
            Label("Pause Title", panel, "PAUSED", 30, 28, 480, 48, 38, White, TextAnchor.MiddleCenter);
            Label("Pause Description", panel, "Your constellation is resting.", 30, 84, 480, 32, 20, Muted, TextAnchor.MiddleCenter);
            resumeButton = Button("Resume", panel, "RESUME  [ESC]", 40, 137, 460, 56, () => SetView(ScreenView.Playing));
            Button("Pause Controls", panel, "CONTROLS", 40, 205, 460, 56, ShowControls);
            MusicButton = Button("Music Placeholder", panel, "MUSIC  —  COMING SOON", 40, 273, 460, 56, null); MusicButton.interactable = false;
            SfxButton = Button("SFX Placeholder", panel, "SFX  —  COMING SOON", 40, 341, 460, 56, null); SfxButton.interactable = false;
            Button("Return To Title", panel, "SAVE & RETURN TO TITLE", 40, 422, 460, 56, ShowTitle);
        }
        void BuildControls()
        {
            var panel = CenterPanel("Controls View", 850, 730); controlsPanel = panel.gameObject;
            Label("Controls Heading", panel, "CONTROLS", 32, 28, 786, 52, 36, White, TextAnchor.MiddleCenter);
            Label("Controls Subtitle", panel, "Keyboard and mouse", 32, 82, 786, 26, 18, Muted, TextAnchor.MiddleCenter);
            string[,] rows = {
                {"Move", "W / A / S / D"}, {"Grab a nearby fragment", "Hold left mouse button"},
                {"Release / attune on an altar", "Release left mouse button"}, {"Move the camera", "Arrow keys / screen edges / middle drag"},
                {"Zoom", "Mouse wheel"}, {"Center camera on player", "Hold Space"},
                {"Open / close skill tree", "Tab or K"}, {"Pause / resume / go back", "Esc"},
                {"Call Starlight / upgrade Resonance", "1 / 2"}, {"Upgrade Strength / call Life", "3 / 4"},
                {"Final shrine / show requirements", "E"}, {"Fullscreen / expand Game view", "F1"}
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                var row = Panel("Control Row " + i, panel, 35, 124 + i * 41, 780, 40, i % 2 == 0 ? Card : Ink);
                Label("Action", row, rows[i, 0], 12, 0, 328, 40, 18, White, TextAnchor.MiddleLeft);
                Label("Binding", row, rows[i, 1], 354, 0, 414, 40, 17, Mint, TextAnchor.MiddleLeft);
            }
            controlsBack = Button("Controls Back", panel, "BACK  [ESC]", 225, 647, 400, 54, BackFromControls);
        }
        void BuildSkills()
        {
            var panel = CenterPanel("Skill Tree", 1120, 730); skillsPanel = panel.gameObject;
            Label("Skill Heading", panel, "CONSTELLATION OF POWERS", 30, 25, 870, 45, 33, White);
            skillsClose = Button("Skills Close", panel, "CLOSE  [TAB]", 922, 26, 170, 44, () => SetView(ScreenView.Playing));
            Label("Skill Help", panel, "Choose your next awakening. Essence keeps growing while you plan.", 30, 80, 1040, 33, 19, Muted);
            SkillCard(panel, "resonance", "RESONANCE", "Multiply the output of your entire constellation.", 24, 142);
            SkillCard(panel, "strength", "ASTRAL STRENGTH", "Pull faster and lift heavy Ancient Hearts.", 389, 142);
            SkillCard(panel, "life", "LIFE FRAGMENTS", "Arrange living fragments beside your generators.", 754, 142);
            SkillCard(panel, "spirit", "WISP RECALL", "Let a spirit attune loose fragments for you.", 24, 375);
            SkillCard(panel, "bridge", "THE STARBRIDGE", "Restore the path to the second island.", 389, 375);
            SkillCard(panel, "ending", "THE FINAL SEED", "Awaken the last memory and complete this chapter.", 754, 375);
            Label("Skill Route", panel, "FIRST LIGHT  →  GROW YOUR CONSTELLATION  →  RESTORE THE BRIDGE  →  AWAKEN THE SEED", 26, 620, 1068, 36, 16, Mint, TextAnchor.MiddleCenter);
            AddPurchase("star", panel, 349, 667, 422, 43, null, "");
        }
        void SkillCard(Transform parent, string id, string name, string description, float x, float y)
        {
            var card = Panel("Power - " + id, parent, x, y, 342, 224, Card);
            Label("Power Name", card, name, 17, 16, 308, 31, 23, Mint);
            var detail = Label("Power Requirements", card, "", 17, 54, 308, 107, 17, White);
            var b = AddPurchase(id, card, 15, 169, 312, 43, detail, description); SkillButtons[id] = b;
        }
        void BuildMemories()
        {
            var panel = CenterPanel("Memories", 680, 620); memoriesPanel = panel.gameObject;
            Label("Memories Title", panel, "MEMORIES OF THE END", 34, 28, 612, 50, 32, White, TextAnchor.MiddleCenter);
            journalText = Label("Journal Entries", panel, "", 45, 105, 590, 358, 23, White);
            memoriesClose = Button("Memories Close", panel, "RETURN  [ESC]", 45, 488, 285, 55, () => SetView(ScreenView.Playing));
            Button("Restart From Memories", panel, "NEW JOURNEY", 350, 488, 285, 55, () => ConfirmNew(ScreenView.Memories));
        }
        void BuildConfirmation()
        {
            var panel = CenterPanel("Confirm New Journey", 600, 345); confirmPanel = panel.gameObject;
            Label("Confirmation Heading", panel, "START AGAIN?", 32, 28, 536, 52, 34, White, TextAnchor.MiddleCenter);
            Label("Confirmation Text", panel, "This restarts your constellation and clears its progress.\nThe previous save will be kept as a backup.", 45, 110, 510, 90, 22, White, TextAnchor.MiddleCenter);
            Button("Cancel New Journey", panel, "KEEP MY JOURNEY", 32, 241, 258, 58, () => SetView(confirmReturn));
            Button("Confirm Restart", panel, "RESTART", 310, 241, 258, 58, game.NewJourney);
        }
        void ConfirmNew(ScreenView previous) { confirmReturn = previous; SetView(ScreenView.ConfirmNewJourney); }
        void Quit()
        {
            game.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        Button AddPurchase(string id, Transform parent, float x, float y, float width, float height, Text detail, string description)
        {
            var b = Button("Purchase - " + id, parent, "", x, y, width, height, () => { game.Purchase(id); RefreshUI(); });
            purchases.Add(new PurchaseBinding { id = id, button = b, detail = detail, description = description }); return b;
        }
        public void RefreshUI()
        {
            if (EssenceText == null || game == null) return;
            EssenceText.text = game.essence.ToString("N1", CultureInfo.InvariantCulture);
            incomeText.text = SimulationPaused ? "PAUSED   ·   " + game.ActiveCount + " active altars" : "+" + LastSparkGame.Format(game.Rate) + " / sec   ·   " + game.ActiveCount + (game.ActiveCount == 1 ? " altar" : " altars");
            objectiveText.text = game.Objective;
            bool journey = game.firstLight || game.lifetime > 0 || System.IO.File.Exists(game.SavePath);
            journeyLabel.text = journey ? "CONTINUE JOURNEY" : "BEGIN JOURNEY"; newButton.gameObject.SetActive(journey);
            bool showFeedback = Time.unscaledTime < game.NoticeUntil && !string.IsNullOrEmpty(game.Notice);
            feedbackPanel.SetActive(showFeedback); noticeText.text = showFeedback ? game.Notice : "";
            string hint = "WASD to explore  ·  Hold left click near a fragment to grab";
            if (game.player.HeldBox != null)
            {
                var f = game.player.HeldBox.GetComponent<AstralFragment>(); var a = f != null ? game.NearestSeat(f, 1.1f) : null;
                hint = a != null ? "RELEASE TO ATTUNE\n+" + LastSparkGame.Format(game.PreviewGain(f, a)) + " Essence / sec" : "FRAGMENT HELD\nMove onto a circular altar, then release left click";
            }
            else if (game.player.NearbyBox != null)
            {
                var f = game.player.NearbyBox.GetComponent<AstralFragment>();
                hint = f != null && !f.CanLift ? "ANCIENT HEART\nUnlock Astral Strength in the skill tree [TAB]" : "FRAGMENT IN REACH\nHold left click to grab";
            }
            if (game.AtShrine && game.player.HeldBox == null)
                hint = game.completed ? "SHRINE AWAKENED\nChapter complete · Production doubled" : "[E] AWAKEN FINAL SHRINE\nHearts " + game.AncientCount + "/2 · Essence " + LastSparkGame.Format(game.essence) + "/" + LastSparkGame.Format(game.balance.finalCost);
            contextText.text = hint;
            journalText.text = (game.firstLight ? "I  —  The universe did not die. Someone put it to sleep." : "I  —  A memory waits inside the first light.") + "\n\n"
                + (game.bridgeOpen ? "II  —  These fragments are seeds. Someone expected you to return." : "II  —  Restore the bridge to remember.") + "\n\n"
                + (game.completed ? "III  —  You carry the beginning. The universe entrusted its next dawn to you." : "III  —  A truth sleeps beyond the bridge.");
            foreach (var binding in purchases) UpdatePurchase(binding);
        }
        void UpdatePurchase(PurchaseBinding b)
        {
            bool unlocked = true, done = false; double price = 0; string name = "", requirement = "";
            switch (b.id)
            {
                case "star": name = "Call Starlight [1]"; price = game.SummonCost; unlocked = game.firstLight; requirement = "Attune the first fragment."; break;
                case "life": name = "Call Life [4]"; price = game.balance.lifeCost * Math.Pow(1.8, game.livesBought); unlocked = game.ActiveCount >= 2; requirement = "Needs 2 active altars.\n+" + game.balance.lifeBonus * 100 + "% output within " + game.balance.lifeRadius + "m."; break;
                case "resonance": name = "Resonance [2]"; price = game.ResonanceCost; done = game.resonance >= game.balance.resonanceCap; requirement = "Level " + game.resonance + " / " + game.balance.resonanceCap + ".\nProduction ×" + game.balance.resonanceMultiplier + " per level."; break;
                case "strength": name = "Strength [3]"; price = game.StrengthCost; done = game.strength >= 3; requirement = "Level " + game.strength + " / 3. Level 1 unlocks Ancient Hearts."; break;
                case "spirit": name = "Awaken Wisp"; price = game.balance.spiritCost; done = game.spirit; unlocked = game.ActiveCount >= 3; requirement = "Needs 3 active altars.\nAttunes one fragment every " + game.balance.spiritInterval + "s."; break;
                case "bridge": name = "Restore Bridge"; price = game.balance.bridgeCost; done = game.bridgeOpen; unlocked = game.ActiveCount >= 4 && game.resonance >= 2; requirement = "Needs 4 active altars and Resonance II."; break;
                case "ending": name = "Awaken Seed"; price = game.balance.finalCost; done = game.completed; unlocked = game.AncientCount >= 2 && game.AtShrine; requirement = "Needs 2 far-island Ancient Hearts.\nVisit the final shrine."; break;
            }
            // Block spending in the game model, but let unavailable buttons explain their requirements.
            b.button.interactable = !SimulationPaused;
            var colors = b.button.colors;
            colors.normalColor = !done && unlocked && game.essence >= price ? Color.white : new Color(.48f, .58f, .64f);
            b.button.colors = colors;
            var caption = b.button.GetComponentInChildren<Text>();
            caption.resizeTextForBestFit = true; caption.resizeTextMinSize = 14; caption.resizeTextMaxSize = 18;
            caption.text = done ? "AWAKENED" : (!unlocked ? "LOCKED · " : "") + name + "  ·  " + LastSparkGame.Format(price) + " Essence";
            if (b.detail != null) b.detail.text = b.description + "\n\n" + requirement;
        }

        RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height); return r;
        }
        RectTransform FullRect(string name, Transform parent)
        {
            var r = Rect(name, parent, 0, 0, 0, 0); r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; return r;
        }
        RectTransform Panel(string name, Transform parent, float x, float y, float width, float height, Color color)
        { var r = Rect(name, parent, x, y, width, height); r.gameObject.AddComponent<Image>().color = color; return r; }
        RectTransform CenterPanel(string name, float width, float height)
        {
            var r = Panel(name, canvas, 0, 0, width, height, Ink); r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = Vector2.zero; return r;
        }
        static void Bottom(RectTransform r, float x, float y, bool right)
        { r.anchorMin = r.anchorMax = new Vector2(right ? 1 : 0, 0); r.pivot = new Vector2(right ? 1 : 0, 0); r.anchoredPosition = new Vector2(right ? -x : x, y); }
        Text Label(string name, Transform parent, string value, float x, float y, float width, float height, int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var text = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>(); text.font = font; text.text = value; text.fontSize = size;
            text.color = color; text.alignment = alignment; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        Button Button(string name, Transform parent, string caption, float x, float y, float width, float height, UnityAction action)
        {
            var r = Panel(name, parent, x, y, width, height, new Color(.1f, .28f, .29f)); var button = r.gameObject.AddComponent<Button>();
            button.targetGraphic = r.GetComponent<Image>(); var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.3f, 1.5f, 1.4f); colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.6f, .95f, .85f); colors.disabledColor = new Color(.43f, .49f, .57f, .85f); button.colors = colors;
            Label("Label", r, caption, 9, 3, width - 18, height - 6, 18, White, TextAnchor.MiddleCenter);
            if (action != null) button.onClick.AddListener(action); return button;
        }
    }
}
