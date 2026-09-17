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
    public sealed class GameHUD : MonoBehaviour
    {
        public enum ScreenView { Title, Playing, Pause, Controls, Skills, Memories, ConfirmNewJourney }
        public GameManager game;
        public ScreenView View { get; private set; } = ScreenView.Title;
        public static GameHUD Current { get; private set; }
        Text particleText;
        public static bool GameplayBlocked => Current != null && Current.View != ScreenView.Playing;
        public static bool SimulationPaused => Current != null && Current.View != ScreenView.Playing && Current.View != ScreenView.Skills && Current.View != ScreenView.Memories;
        public Text SoulsText { get; private set; }
        public Text FeedbackText => noticeText;
        public Button SkillsButton { get; private set; }
        public GameObject ControlsPanel => controlsPanel;
        public readonly Dictionary<string, Button> SkillButtons = new Dictionary<string, Button>();
        // The immersion pass hangs crisp screen-space world labels off the gameplay HUD.
        public Transform WorldLabelRoot { get; private set; }
        public Font LabelFont => font;
        Font font;
        RectTransform canvas;
        GameObject playRoot, navigation, shade, titlePanel, pausePanel, controlsPanel, skillsPanel, memoriesPanel, confirmPanel, feedbackPanel;
        Text incomeText, objectiveText, contextText, noticeText, journalText, journeyLabel;
        Button startButton, newButton, resumeButton, controlsBack, skillsClose, memoriesClose;
        ScreenView controlsReturn = ScreenView.Title, confirmReturn = ScreenView.Title;
        readonly List<PurchaseBinding> purchases = new List<PurchaseBinding>();
        SkillTree skillTree;
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
        static readonly Color Ink = new Color(.055f, .045f, .04f, .97f);
        static readonly Color Card = new Color(.12f, .105f, .09f, .98f);
        static readonly Color White = new Color(.95f, .90f, .82f);
        static readonly Color Muted = new Color(.73f, .69f, .61f);
        static readonly Color Mint = new Color(.90f, .72f, .36f);
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
            if (!(game.ConnectedCount > 0)) game.Notify("Collect 3 particles, then press E beside the Wisdom Tree. Haul its block to the glowing bridge edge.", false);
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
            var canvasComponent = root.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay; canvasComponent.sortingOrder = 100;
            // Fractional canvas scales resample every glyph. Whole-pixel quads plus a reference
            // resolution close to the real window keep the text sharp instead of smeared.
            canvasComponent.pixelPerfect = true;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            eventSystem = FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var events = new GameObject("Last Spark EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false); eventSystem = events.GetComponent<EventSystem>();
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            pointer = new PointerEventData(eventSystem);
            playRoot = FullRect("Gameplay HUD", canvas).gameObject;
            WorldLabelRoot = FullRect("World Labels", playRoot.transform);
            var mission = Panel("Current Objective", playRoot.transform, 22, 0, 418, 158, Ink);
            Bottom(mission, 22, 24, false);
            Label("Objective Heading", mission, "YOUR NEXT DISCOVERY", 18, 14, 380, 26, 17, Mint);
            objectiveText = Label("Objective", mission, "", 18, 45, 380, 100, 18, White);
            var context = Panel("Interaction Hint", playRoot.transform, 0, 0, 540, 84, Ink);
            context.anchorMin = context.anchorMax = new Vector2(.5f, 0); context.pivot = new Vector2(.5f, 0); context.anchoredPosition = new Vector2(0, 24);
            contextText = Label("Interaction", context, "", 18, 12, 504, 60, 19, White, TextAnchor.MiddleCenter);
            var calls = Panel("Fragment Calls", playRoot.transform, 0, 0, 360, 170, Ink); Bottom(calls, 22, 24, true);
            Label("Call Heading", calls, "WISDOM TREE ENERGY", 16, 12, 328, 24, 17, Mint);
            particleText = Label("Particle Inventory", calls, "", 16, 45, 328, 32, 19, White);
            Label("Particle Help", calls, "Walk over particles to collect.\nE near a tree: 3 particles = 1 block.\nSouls still unlock your skills.", 16, 82, 328, 76, 16, White);

            shade = FullRect("Menu Backdrop", canvas).gameObject; shade.AddComponent<Image>().color = new Color(.008f, .016f, .04f, .9f);
            BuildTitle(); BuildPause(); BuildControls(); BuildSkills(); BuildMemories(); BuildConfirmation();

            // This panel is above every modal so Souls is always visible, including during pause and skill selection.
            var wallet = Panel("Souls Counter", canvas, 22, 20, 350, 132, Ink);
            Label("Souls Label", wallet, "SOULS", 20, 12, 310, 23, 17, Mint);
            SoulsText = Label("Souls Amount", wallet, "0.0", 20, 37, 310, 52, 40, Mint);
            incomeText = Label("Souls Income", wallet, "+0.0 / sec", 20, 94, 310, 26, 18, White);
            navigation = Rect("Quick Menu", canvas, 0, 0, 476, 58).gameObject;
            var nav = navigation.GetComponent<RectTransform>(); nav.anchorMin = nav.anchorMax = new Vector2(1, 1); nav.pivot = new Vector2(1, 1); nav.anchoredPosition = new Vector2(-22, -20);
            SkillsButton = Button("Skill Tree Shortcut", nav, "SKILL TREE  [TAB]", 0, 0, 202, 58, ToggleSkills);
            Button("Memories Shortcut", nav, "MEMORIES", 212, 0, 152, 58, () => SetView(View == ScreenView.Memories ? ScreenView.Playing : ScreenView.Memories));
            Button("Pause Shortcut", nav, "ESC", 374, 0, 102, 58, ShowPause);
            var feedback = Panel("Action Feedback", canvas, 0, 0, 620, 108, Ink);
            feedback.anchorMin = feedback.anchorMax = new Vector2(.5f, 1); feedback.pivot = new Vector2(.5f, 1); feedback.anchoredPosition = new Vector2(0, -18);
            feedback.GetComponent<Image>().raycastTarget = false; feedbackPanel = feedback.gameObject;
            noticeText = Label("Action Feedback Text", feedback, "", 16, 10, 588, 88, 19, Mint, TextAnchor.MiddleCenter);
        }
        void BuildTitle()
        {
            var panel = CenterPanel("Title Menu", 600, 610); titlePanel = panel.gameObject;
            Label("Title Eyebrow", panel, "AT THE END OF THE UNIVERSE", 40, 35, 520, 26, 17, Mint, TextAnchor.MiddleCenter);
            Label("Game Title", panel, "THE LAST\nSPARK", 30, 79, 540, 146, 64, White, TextAnchor.MiddleCenter);
            Label("Game Lore", panel, "Build a path for the souls lost in the dark.\nCarry the light toward the next dawn.", 55, 244, 490, 65, 20, Muted, TextAnchor.MiddleCenter);
            startButton = Button("Begin Journey", panel, "BEGIN JOURNEY", 70, 332, 460, 58, BeginJourney);
            journeyLabel = startButton.GetComponentInChildren<Text>();
            newButton = Button("New Journey", panel, "NEW JOURNEY", 70, 400, 460, 48, () => ConfirmNew(ScreenView.Title));
            Button("Title Controls", panel, "CONTROLS", 70, 458, 460, 48, ShowControls);
            Button("Quit", panel, "QUIT", 70, 516, 460, 48, Quit);
        }
        void BuildPause()
        {
            var panel = CenterPanel("Pause Menu", 540, 620); pausePanel = panel.gameObject;
            Label("Pause Title", panel, "PAUSED", 30, 28, 480, 48, 38, White, TextAnchor.MiddleCenter);
            Label("Pause Description", panel, "Your soul paths are resting.", 30, 84, 480, 32, 20, Muted, TextAnchor.MiddleCenter);
            resumeButton = Button("Resume", panel, "RESUME  [ESC]", 40, 137, 460, 56, () => SetView(ScreenView.Playing));
            Button("Pause Controls", panel, "CONTROLS", 40, 205, 460, 56, ShowControls);
            AudioSlider(panel,"Master",272);AudioSlider(panel,"Music",326);AudioSlider(panel,"Ambience",380);AudioSlider(panel,"Effects",434);
            Button("Return To Title", panel, "SAVE & RETURN TO TITLE", 40, 522, 460, 56, ShowTitle);
        }
        void AudioSlider(RectTransform panel,string group,float y)
        {
            Label(group+" Label",panel,group.ToUpperInvariant(),40,y,130,32,17,White,TextAnchor.MiddleLeft);
            var host=new GameObject(group+" Volume",typeof(RectTransform),typeof(Slider));host.transform.SetParent(panel,false);
            var rect=host.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(190,-y);rect.sizeDelta=new Vector2(310,32);
            var track=new GameObject("Track",typeof(RectTransform),typeof(Image));track.transform.SetParent(rect,false);var tr=track.GetComponent<RectTransform>();tr.anchorMin=new Vector2(0,.4f);tr.anchorMax=new Vector2(1,.6f);tr.offsetMin=tr.offsetMax=Vector2.zero;track.GetComponent<Image>().color=Card;
            var handle=new GameObject("Handle",typeof(RectTransform),typeof(Image));handle.transform.SetParent(rect,false);var hr=handle.GetComponent<RectTransform>();hr.sizeDelta=new Vector2(18,28);handle.GetComponent<Image>().color=Mint;
            var slider=host.GetComponent<Slider>();slider.handleRect=hr;slider.targetGraphic=handle.GetComponent<Image>();slider.minValue=0;slider.maxValue=1;slider.value=ForestSoundscape.Volume(group);slider.onValueChanged.AddListener(v=>ForestSoundscape.SetVolume(group,v));
        }
        void BuildControls()
        {
            var panel = CenterPanel("Controls View", 850, 730); controlsPanel = panel.gameObject;
            Label("Controls Heading", panel, "CONTROLS", 32, 28, 786, 52, 36, White, TextAnchor.MiddleCenter);
            Label("Controls Subtitle", panel, "Keyboard and mouse", 32, 82, 786, 26, 18, Muted, TextAnchor.MiddleCenter);
            string[,] rows = {
                {"Move", "W / A / S / D"}, {"Grab / gather floating stones", "Left mouse (hold before Soul Links)"},
                {"Drop / build bridge tiles", "Q in orbit; release mouse when dragging"}, {"Move the camera", "Arrow keys / screen edges / middle drag"},
                {"Zoom", "Mouse wheel"}, {"Center camera on player", "Hold Space"},
                {"Open / close skill tree", "Tab or K"}, {"Pause / resume / go back", "Esc"},
                {"Drag speed / multi-block hauling", "1 / 2"}, {"Strength / lighting soul / Soul Well", "3 / 4 / 5"},
                {"Show objective / enter next area", "E"}, {"Fullscreen / expand Game view", "F1"}
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
            var panel = CenterPanel("Skill Tree", 1180, 800); skillsPanel = panel.gameObject;
            skillTree = SkillTree.Build(panel, game, font, () => SetView(ScreenView.Playing), out skillsClose, out var powers);
            SkillButtons.Clear();
            foreach (var power in powers) SkillButtons[power.Key] = power.Value;
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
            Label("Confirmation Text", panel, "This restarts your bridges and clears their progress.\nThe previous save will be kept as a backup.", 45, 110, 510, 90, 22, White, TextAnchor.MiddleCenter);
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
            if (SoulsText == null || game == null) return;
            SoulsText.text = game.souls.ToString("N1", CultureInfo.InvariantCulture);
            incomeText.text = SimulationPaused ? "PAUSED · " + game.ConnectedCount + " path blocks" : "+" + GameManager.Format(game.Rate) + " / sec · " + game.ConnectedCount + " connected";
            objectiveText.text = game.Objective;
            var forest = ForestWorld.For(game);
            if (particleText != null && forest != null && forest.State != null) particleText.text = forest.InventoryLabel;
            bool journey = game.ConnectedCount > 0 || game.souls > 0 || System.IO.File.Exists(game.SavePath);
            journeyLabel.text = journey ? "CONTINUE JOURNEY" : "BEGIN JOURNEY"; newButton.gameObject.SetActive(journey);
            bool showFeedback = Time.unscaledTime < game.NoticeUntil && !string.IsNullOrEmpty(game.Notice);
            feedbackPanel.SetActive(showFeedback); noticeText.text = showFeedback ? game.Notice : "";
            string hint = game.links>0 ? "WASD to move · Click to gather · Q to drop\nE for your objective · Tab for powers" : "WASD to move · Hold left mouse to haul\nE for your objective · Tab for powers";
            var box = game.player.HeldBox;
            if (box != null)
            {
                var b = box.GetComponent<BridgeBlock>();
                if (game.player.Orbiting) hint = game.FindOrbitPlacement(b,out _) ? "Q TO BUILD AT THE GLOWING EDGE\nOrbiting "+game.player.HeldBoxes.Count+"/"+game.Capacity+" stones" : "SOUL ORBIT · "+game.player.HeldBoxes.Count+"/"+game.Capacity+"\nQ to drop safely · Mouse release keeps carrying";
                else if (b != null && game.FindPlacement(b, out _, out _, out _, out _)) hint = "RELEASE TO BUILD THE BRIDGE\nHauling " + game.player.HeldBoxes.Count + "/" + game.Capacity + " blocks";
                else hint = "HAULING " + game.player.HeldBoxes.Count + "/" + game.Capacity + " BLOCKS\nDrag to the glowing path edge, then release";
            }
            else if (game.player.NearbyBox != null)
            {
                var b = game.player.NearbyBox.GetComponent<BridgeBlock>();
                if (b != null) hint = b.placed ? "CONNECTED BRIDGE TILE\nGrab loose blocks to extend the path" : !b.CanLift ? b.Title.ToUpper() + " · STRENGTH " + b.RequiredStrength + " REQUIRED\nYour Strength: " + game.strength + " · Open Skills [Tab]" : b.Title.ToUpper() + (game.links>0?" IN REACH\nClick to gather · Q to drop":" IN REACH\nHold left click to haul");
            }
            var wisdom = forest?.Nearby(game.player.transform.position);
            if (box == null && wisdom != null) hint = wisdom.Charging ? "Charging the Wisdom Tree..." : "WISDOM TREE\nE: Charge with 3 particles";
            contextText.text = hint;
            journalText.text = "I — They called it the end of the universe. But souls still wander between its broken shores.\n\n"
                + (game.Bridges[0] ? "II — Your first bridge did more than carry you. It gave the lost a path home." : "II — A memory waits across the first rift.") + "\n\n"
                + (game.completed ? "III — The light you carried was never yours alone. Every soul you guided became part of the next dawn." : "III — Build through the darkness to reach the next dawn.");
            foreach (var binding in purchases) UpdatePurchase(binding);
            if (skillTree != null) skillTree.Refresh();
        }
        void UpdatePurchase(PurchaseBinding b)
        {
            bool supply = b.id == "stone" || b.id == "grave";
            bool done = !supply && game.Level(b.id) >= game.Cap(b.id);
            bool unlocked = b.id != "grave" || game.Bridges[0];
            string title = b.id == "speed" ? "Hauling [1]" : b.id == "links" ? "Soul Links [2]" : b.id == "strength" ? "Strength [3]" : b.id == "radiance" ? "Light [4]" : b.id == "generator" ? "Soul Well [5]" : b.id == "stone" ? "Call Stone" : "Call Grave Stone";
            double cost = game.Cost(b.id);
            b.button.interactable = !SimulationPaused;
            var colors = b.button.colors; colors.normalColor = !done && unlocked && game.souls >= cost ? Color.white : new Color(.61f, .56f, .47f); b.button.colors = colors;
            var caption = b.button.GetComponentInChildren<Text>();
            caption.text = done ? "FULLY AWAKENED"
                : title + "\n" + (unlocked ? "" : "LOCKED  ·  ") + GameManager.Format(cost) + " Souls";
            if (b.detail == null) return;
            string detail = b.id == "speed" ? "Level " + game.speed + "/6 · +0.65m/s per level." : b.id == "links" ? "Level " + game.links + "/3 · Orbit " + game.Capacity + " at once. Q drops.\nEach level adds one block." : b.id == "strength" ? "Level " + game.strength + "/3. Grave Stone: I.\nAncient Obsidian: III." : b.id == "radiance" ? "Level " + game.radiance + "/5 · Range " + (9 + game.radiance * 3) + "m.\nEach level adds 3 metres." : b.id == "generator" ? "Level " + game.generator + "/8 · +" + GameManager.Format(game.GeneratorRate) + " Souls/sec.\nNext: +" + GameManager.Format(3 * System.Math.Pow(1.6, game.generator)) + "/sec." : "Ordinary stones build the first bridge.\nGrave Stones build either bridge.";
            b.detail.text = b.description + "\n\n" + detail;
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
            var r = Panel(name, parent, x, y, width, height, new Color(.30f, .235f, .145f)); var button = r.gameObject.AddComponent<Button>();
            button.targetGraphic = r.GetComponent<Image>(); var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.3f, 1.5f, 1.4f); colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.75f, .63f, .43f); colors.disabledColor = new Color(.43f, .49f, .57f, .85f); button.colors = colors;
            Label("Label", r, caption, 9, 3, width - 18, height - 6, 18, White, TextAnchor.MiddleCenter);
            if (action != null) button.onClick.AddListener(action); return button;
        }
    }
}

