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
        Font font, titleFont;
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
        // The Spirit Path palette: deep navy glass, warm parchment text, a single gold accent.
        static readonly Color Ink = new Color(.07f, .06f, .10f, .86f);
        static readonly Color Card = new Color(.11f, .10f, .15f, .90f);
        static readonly Color White = new Color(.949f, .922f, .867f);
        static readonly Color Muted = new Color(.604f, .576f, .659f);
        static readonly Color Mint = new Color(.894f, .749f, .478f);
        static Sprite fillSprite, edgeSprite;
        static readonly Dictionary<string, Sprite> iconSprites = new Dictionary<string, Sprite>();
        // Screen-space chip that follows the placement ghost, plus what it needs to project.
        RectTransform placeChip;
        Canvas surface;
        Camera gameCamera;
        Transform ghost;
        float ghostSearch;
        string diamond = "*";
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
        void OnEnable() { Current = this; EnvironmentVisibility.ApplyStars(); }
        void OnDisable()
        {
            if (Current != this) return;
            EnvironmentVisibility.RestoreEditorSky();
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
            // The F1 state lives in a toast, never as a permanent label on the diorama.
            if (game != null) game.Notify(fullscreenView.maximized ? "EXPANDED GAME VIEW\nPress F1 again to restore the editor layout." : "EDITOR LAYOUT RESTORED\nPress F1 to expand the Game view.", false);
#else
            if (Screen.fullScreen)
            {
                Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.Windowed);
                if (game != null) game.Notify("WINDOWED MODE\nPress F1 for fullscreen.", false);
            }
            else
            {
                windowWidth = Screen.width; windowHeight = Screen.height;
                Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
                if (game != null) game.Notify("FULLSCREEN\nPress F1 to return to windowed mode.", false);
            }
#endif
        }

        public void BeginJourney()
        {
            SetView(ScreenView.Playing);
            if (game != null && !(game.ConnectedCount > 0)) game.Notify(BridgeProfile.Current.isLighting?game.LightingCarryHint:"Collect 3 particles, then press E beside the Wisdom Tree. Haul its block to the glowing bridge edge.", false);
        }
        public void ShowPause() { if (View != ScreenView.Title) { if (game != null) game.Save(); SetView(ScreenView.Pause); } }
        public void ShowTitle() { if (game != null) game.Save(); SetView(ScreenView.Title); }
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
            if (GameplayBlocked && game != null && game.player != null) { game.player.Release(); game.player.CancelDash(); }
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
            // Authored OFL faces live in Resources so no importer settings are needed; before their first
            // editor import the loads return null, so both fall back to the built-in face.
            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var body = Resources.Load<Font>("UI/Fonts/Body"); font = body != null ? body : builtin;
            var display = Resources.Load<Font>("UI/Fonts/Title"); titleFont = display != null ? display : font;
            var root = new GameObject("Last Spark Canvas UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false); canvas = root.GetComponent<RectTransform>();
            var canvasComponent = surface = root.GetComponent<Canvas>();
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
            diamond = Glyph('\u2726', '\u25c6', '*');
            var location = Heading("Location Title", playRoot.transform, diamond + "  " + Spaced("THE SPIRIT PATH") + "  " + diamond,
                0, 0, 760, 34, 21, Mint, TextAnchor.MiddleCenter).rectTransform;
            location.anchorMin = location.anchorMax = new Vector2(.5f, 1); location.pivot = new Vector2(.5f, 1); location.anchoredPosition = new Vector2(0, -14);
            // Resources sit under the active Lighting wallet or legacy Souls wallet.
            var calls = Panel("Fragment Calls", playRoot.transform, 22, 162, 350, 152, Ink);
            Label("Call Heading", calls, diamond + "  RESOURCES", 18, 12, 314, 22, 15, Mint).fontStyle = FontStyle.Bold;
            particleText = Label("Particle Inventory", calls, "", 18, 40, 314, 52, 17, White);
            Label("Particle Help", calls, BridgeProfile.Current.isLighting
                ? "Stand on the tile under a particle to collect it.\nChoose a free tile colour at any Wisdom Tree with E."
                : "Walk over particles to collect them.\nE near a wisdom tree: 3 particles = 1 block.", 18, 96, 314, 46, 14, Muted);
            var mission = Panel("Current Objective", playRoot.transform, 22, 0, 430, 118, Ink);
            Bottom(mission, 22, 24, false);
            Label("Objective Heading", mission, diamond + "  YOUR NEXT DISCOVERY", 16, 12, 398, 22, 15, Mint).fontStyle = FontStyle.Bold;
            objectiveText = Label("Objective", mission, "", 16, 40, 398, 66, 16, Muted);
            var context = Panel("Interaction Hint", playRoot.transform, 0, 0, 430, 66, Ink);
            Bottom(context, 22, 150, false);
            context.GetComponent<Image>().raycastTarget = false;
            contextText = Label("Interaction", context, "", 16, 10, 398, 46, 15, White);
            var keycaps = Panel("Key Hints", playRoot.transform, 0, 0, 470, 46, Ink);
            keycaps.anchorMin = keycaps.anchorMax = new Vector2(.5f, 0); keycaps.pivot = new Vector2(.5f, 0); keycaps.anchoredPosition = new Vector2(0, 24);
            keycaps.GetComponent<Image>().raycastTarget = false;
            Label("Key Hints Text", keycaps, "[W][A][S][D] Move    [E] Interact", 12, 0, 446, 46, 16, White, TextAnchor.MiddleCenter);
            placeChip = Panel("Place Prompt", playRoot.transform, 0, 0, 170, 38, Ink);
            placeChip.anchorMin = placeChip.anchorMax = placeChip.pivot = new Vector2(.5f, .5f);
            placeChip.GetComponent<Image>().raycastTarget = false;
            Label("Place Prompt Text", placeChip, "[E] Place block", 8, 0, 154, 38, 15, White, TextAnchor.MiddleCenter);
            placeChip.gameObject.SetActive(false);

            shade = FullRect("Menu Backdrop", canvas).gameObject; shade.AddComponent<Image>().color = new Color(.008f, .016f, .04f, .9f);
            BuildTitle(); BuildPause(); BuildControls(); BuildSkills(); BuildMemories(); BuildConfirmation();

            // This panel is above every modal so the active wallet stays visible during pause and skill selection.
            bool lighting = BridgeProfile.Current.isLighting;
            var wallet = Panel(lighting ? "Particle Counter" : "Souls Counter", canvas, 22, 20, 350, 132, Ink);
            Label(lighting ? "Particle Wallet Label" : "Souls Label", wallet, diamond + (lighting ? "  PARTICLES" : "  SOULS"), 20, 12, 310, 23, 15, Mint).fontStyle = FontStyle.Bold;
            SoulsText = Label(lighting ? "Particle Wallet Amount" : "Souls Amount", wallet, "0.0", 20, 37, 310, 52, 40, White);
            incomeText = Label(lighting ? "Particle Gathering Help" : "Souls Income", wallet, lighting ? "ACTIVE GATHERING ONLY" : "+0.0 / sec", 20, 94, 310, 26, 16, Muted);
            // Equal rounded squares, the way the diorama concept stacks its icon buttons.
            navigation = Rect("Quick Menu", canvas, 0, 0, 218, 66).gameObject;
            var nav = navigation.GetComponent<RectTransform>(); nav.anchorMin = nav.anchorMax = new Vector2(1, 1); nav.pivot = new Vector2(1, 1); nav.anchoredPosition = new Vector2(-22, -20);
            SkillsButton = Button("Skill Tree Shortcut", nav, "SKILLS\n[TAB]", 0, 0, 66, 66, ToggleSkills, 9);
            Iconify(SkillsButton, "map");
            Iconify(Button("Memories Shortcut", nav, "MEMORY", 76, 0, 66, 66, () => SetView(View == ScreenView.Memories ? ScreenView.Playing : ScreenView.Memories), 9), "bag");
            Iconify(Button("Pause Shortcut", nav, "PAUSE\n[ESC]", 152, 0, 66, 66, ShowPause, 9), "settings");
            var feedback = Panel("Action Feedback", canvas, 0, 0, 620, 84, Ink);
            feedback.anchorMin = feedback.anchorMax = new Vector2(.5f, 1); feedback.pivot = new Vector2(.5f, 1); feedback.anchoredPosition = new Vector2(0, -58);
            feedback.GetComponent<Image>().raycastTarget = false; feedbackPanel = feedback.gameObject;
            noticeText = Label("Action Feedback Text", feedback, "", 16, 8, 588, 68, 17, Mint, TextAnchor.MiddleCenter);
            if(BridgeProfile.Current.isLighting)
            {
                // No floating world labels; resource and particle wallet panels remain visible.
                WorldLabelRoot.gameObject.SetActive(false);location.gameObject.SetActive(false);
                mission.gameObject.SetActive(false);keycaps.gameObject.SetActive(false);
            }
        }
        void BuildTitle()
        {
            var panel = CenterPanel("Title Menu", 600, 610); titlePanel = panel.gameObject;
            Label("Title Eyebrow", panel, "AT THE END OF THE UNIVERSE", 40, 35, 520, 26, 17, Mint, TextAnchor.MiddleCenter);
            Heading("Game Title", panel, "THE LAST\nSPARK", 30, 79, 540, 146, 64, White, TextAnchor.MiddleCenter);
            Label("Game Lore", panel, BridgeProfile.Current.isLighting ? "Gather particles and build a path through the void.\nCarry the light toward the next dawn." : "Build a path for the souls lost in the dark.\nCarry the light toward the next dawn.", 55, 244, 490, 65, 20, Muted, TextAnchor.MiddleCenter);
            startButton = Button("Begin Journey", panel, "BEGIN JOURNEY", 70, 332, 460, 58, BeginJourney);
            journeyLabel = startButton.GetComponentInChildren<Text>();
            newButton = Button("New Journey", panel, "NEW JOURNEY", 70, 400, 460, 48, () => ConfirmNew(ScreenView.Title));
            Button("Title Controls", panel, "CONTROLS", 70, 458, 460, 48, ShowControls);
            Button("Quit", panel, "QUIT", 70, 516, 460, 48, Quit);
        }
        void BuildPause()
        {
            var panel = CenterPanel("Pause Menu", 540, 724); pausePanel = panel.gameObject;
            Heading("Pause Title", panel, "PAUSED", 30, 16, 480, 64, 38, White, TextAnchor.MiddleCenter);
            Label("Pause Description", panel, BridgeProfile.Current.isLighting ? "Your journey is paused." : "Your soul paths are resting.", 30, 84, 480, 32, 20, Muted, TextAnchor.MiddleCenter);
            resumeButton = Button("Resume", panel, "RESUME  [ESC]", 40, 137, 460, 56, () => SetView(ScreenView.Playing));
            Button("Pause Controls", panel, "CONTROLS", 40, 205, 460, 56, ShowControls);
            AudioSlider(panel,"Master",272);AudioSlider(panel,"Music",326);AudioSlider(panel,"Ambience",380);AudioSlider(panel,"Effects",434);
            // Lighting has no decorative forest to toggle (BridgeProfile.hasForestToggle is false
            // there); the Stars row just moves up into the Forest row's slot instead of leaving a gap.
            bool showForest = BridgeProfile.Current.hasForestToggle;
            if (showForest) VisibilityToggle(panel,"Forest",490,EnvironmentVisibility.ForestVisible,EnvironmentVisibility.SetForestVisible);
            if(!BridgeProfile.Current.isLighting)VisibilityToggle(panel,"Stars",showForest ? 548 : 490,EnvironmentVisibility.StarsVisible,EnvironmentVisibility.SetStarsVisible);
            Button("Return To Title", panel, "SAVE & RETURN TO TITLE", 40, 628, 460, 56, ShowTitle);
        }
        void VisibilityToggle(RectTransform panel,string name,float y,bool visible,UnityAction<bool> changed)
        {
            var row=Panel(name+" Visibility",panel,40,y,460,44,Card);
            var toggle=row.gameObject.AddComponent<Toggle>(); toggle.targetGraphic=row.GetComponent<Image>();
            Label(name+" Label",row,name=="Forest"?"FOREST":"STAR BACKGROUND",14,0,320,44,17,White,TextAnchor.MiddleLeft);
            var box=Rect("Checkbox",row,418,10,24,24).gameObject.AddComponent<Image>();
            box.sprite=Fill(); box.type=Image.Type.Sliced; box.color=Ink;
            var mark=Rect("Check",box.transform,5,5,14,14).gameObject.AddComponent<Image>(); mark.color=Mint;
            toggle.graphic=mark; toggle.SetIsOnWithoutNotify(visible);
            var state=Label("State",row,visible?"ON":"OFF",338,0,68,44,15,visible?Mint:Muted,TextAnchor.MiddleCenter);
            toggle.onValueChanged.AddListener(value=>{changed(value);state.text=value?"ON":"OFF";state.color=value?Mint:Muted;});
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
            Heading("Controls Heading", panel, "CONTROLS", 32, 28, 786, 52, 36, White, TextAnchor.MiddleCenter);
            Label("Controls Subtitle", panel, "Keyboard and mouse", 32, 82, 786, 26, 18, Muted, TextAnchor.MiddleCenter);
            // Lighting keeps Speed, Particle Yield, Dash, Light Grenade and Spirit Pull; the other legacy powers,
            // and Soul Links' click-to-gather grab mode, do not exist there.
            string[,] rows = BridgeProfile.Current.isLighting ? new[,] {
                {"Move", "W / A / S / D"}, {"Carry block / place tile", "Hold / release Space"},
                {"Rotate / zoom camera", "Move mouse / wheel"},
                {"Collect particles", "Stand on their supporting tile"}, {"Request matching free tile", "E at a Wisdom Tree"},
                {"Return unused blocks", "R near a Wisdom Tree"}, {"Open / close skill tree", "Tab or K"},
                {"Pause / resume / go back", "Esc"}, {"Speed / Particle Yield", "1 / 2"}, {"Dash (after unlock)", "Shift"},
                {"Light Grenade (after unlock)", "Hold / release right mouse"}, {"Spirit Pull (after unlock)", "F"},
                {"Fullscreen / expand Game view", "F1"}
            } : new[,] {
                {"Move", "W / A / S / D"}, {"Grab / gather floating stones", "Left mouse (hold before Soul Links)"},
                {"Drop / build bridge tiles", "Q in orbit; release mouse when dragging"}, {"Move the camera", "Arrow keys / screen edges / middle drag"},
                {"Zoom", "Mouse wheel"}, {"Center camera on player", "Hold Space"},
                {"Open / close skill tree", "Tab or K"}, {"Pause / resume / go back", "Esc"},
                {"Drag speed / multi-block hauling", "1 / 2"}, {"Strength / lighting soul / Soul Well", "3 / 4 / 5"},
                {"Show objective / enter next area", "E"}, {"Fullscreen / expand Game view", "F1"}
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                var row = Panel("Control Row " + i, panel, 35, 124 + i * 39, 780, 38, i % 2 == 0 ? Card : Ink);
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
            Heading("Memories Title", panel, "MEMORIES OF THE END", 34, 28, 612, 50, 32, White, TextAnchor.MiddleCenter);
            journalText = Label("Journal Entries", panel, "", 45, 105, 590, 358, 23, White);
            memoriesClose = Button("Memories Close", panel, "RETURN  [ESC]", 45, 488, 285, 55, () => SetView(ScreenView.Playing));
            Button("Restart From Memories", panel, "NEW JOURNEY", 350, 488, 285, 55, () => ConfirmNew(ScreenView.Memories));
        }
        void BuildConfirmation()
        {
            var panel = CenterPanel("Confirm New Journey", 600, 345); confirmPanel = panel.gameObject;
            Heading("Confirmation Heading", panel, "START AGAIN?", 32, 28, 536, 52, 34, White, TextAnchor.MiddleCenter);
            Label("Confirmation Text", panel, "This restarts your bridges and clears their progress.\nThe previous save will be kept as a backup.", 45, 110, 510, 90, 22, White, TextAnchor.MiddleCenter);
            Button("Cancel New Journey", panel, "KEEP MY JOURNEY", 32, 241, 258, 58, () => SetView(confirmReturn));
            Button("Confirm Restart", panel, "RESTART", 310, 241, 258, 58, game != null ? (UnityAction)game.NewJourney : null);
        }
        void ConfirmNew(ScreenView previous) { confirmReturn = previous; SetView(ScreenView.ConfirmNewJourney); }
        void Quit()
        {
            if (game != null) game.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        Button AddPurchase(string id, Transform parent, float x, float y, float width, float height, Text detail, string description)
        {
            var b = Button("Purchase - " + id, parent, "", x, y, width, height, () => { if (game != null) game.Purchase(id); RefreshUI(); });
            purchases.Add(new PurchaseBinding { id = id, button = b, detail = detail, description = description }); return b;
        }
        public void RefreshUI()
        {
            if (SoulsText == null || game == null) return;
            if (BridgeProfile.Current.isLighting)
            {
                SoulsText.text = ForestWorld.For(game)?.SharedInventoryLabel ?? "0";
                incomeText.text = SimulationPaused ? "PAUSED" : "ACTIVE GATHERING ONLY";
            }
            else
            {
                SoulsText.text = game.souls.ToString("N1", CultureInfo.InvariantCulture);
                incomeText.text = SimulationPaused ? "PAUSED · " + game.ConnectedCount + " path blocks" : "+" + GameManager.Format(game.Rate) + " / sec · " + game.ConnectedCount + " connected";
            }
            objectiveText.text = game.Objective;
            var forest = ForestWorld.For(game);
            if (particleText != null && forest != null && forest.State != null)
            {
                particleText.text = BridgeProfile.Current.isLighting
                    ? "Particles: " + forest.SharedInventoryLabel + "\n" + NearbyReceiverLabel(game)
                    : forest.InventoryLabel + "\n" + game.ConnectedCount + " path blocks";
            }
            bool journey = game.ConnectedCount > 0 || (BridgeProfile.Current.isLighting ? (ForestWorld.For(game)?.SharedInventory ?? 0) > 0 : game.souls > 0) || System.IO.File.Exists(game.SavePath);
            journeyLabel.text = journey ? "CONTINUE JOURNEY" : "BEGIN JOURNEY"; newButton.gameObject.SetActive(journey);
            bool showFeedback = Time.unscaledTime < game.NoticeUntil && !string.IsNullOrEmpty(game.Notice);
            feedbackPanel.SetActive(showFeedback); noticeText.text = showFeedback ? game.Notice : "";
            string hint = game.links>0 ? "Click to gather · Q to drop · Tab for powers" : "Hold left mouse to haul · Tab for powers";
            var box = game.player == null ? null : game.player.HeldBox;
            if (box != null)
            {
                var b = box.GetComponent<BridgeBlock>();
                if (game.player.Orbiting) hint = game.FindOrbitPlacement(b,out _) ? "Q TO BUILD AT THE GLOWING EDGE\nOrbiting "+game.player.HeldBoxes.Count+"/"+game.Capacity+" stones" : "SOUL ORBIT · "+game.player.HeldBoxes.Count+"/"+game.Capacity+"\nQ to drop safely · Mouse release keeps carrying";
                else if (b != null && game.FindPlacement(b, out _, out _, out _, out _)) hint = "RELEASE TO BUILD THE BRIDGE\nHauling " + game.player.HeldBoxes.Count + "/" + game.Capacity + " blocks";
                else hint = "HAULING " + game.player.HeldBoxes.Count + "/" + game.Capacity + " BLOCKS\nDrag to the glowing path edge, then release";
            }
            else if (game.player != null && game.player.NearbyBox != null)
            {
                var b = game.player.NearbyBox.GetComponent<BridgeBlock>();
                if (b != null) hint = b.placed ? "CONNECTED BRIDGE TILE\nGrab loose blocks to extend the path" : !b.CanLift ? b.Title.ToUpper() + " · STRENGTH " + b.RequiredStrength + " REQUIRED\nYour Strength: " + game.strength + " · Open Skills [Tab]" + (BridgeProfile.Current.treeReturn ? " · R at the tree to return it" : "") : b.Title.ToUpper() + (game.links>0?" IN REACH\nClick to gather · Q to drop":" IN REACH\nHold left click to haul");
            }
            var wisdom = game.player == null ? null : forest?.Nearby(game.player.transform.position);
            if (box == null && wisdom != null) hint = wisdom.Charging ? "Charging the Wisdom Tree..." : "WISDOM TREE\nE: Charge with 3 particles" + (BridgeProfile.Current.treeReturn ? " · R: Return stones" : "");
            contextText.text = BridgeProfile.Current.isLighting ? (game.player.HeldBox!=null?"Release SPACE at the glowing bridge edge":LightingDepositHint(game)) : diamond + "  " + hint;
            RefreshPlacePrompt();
            journalText.text = "I · They called it the end of the universe. But souls still wander between its broken shores.\n\n"
                + (game.Bridges[0] ? "II · Your first bridge did more than carry you. It gave the lost a path home." : "II · A memory waits across the first rift.") + "\n\n"
                + (game.completed ? "III · The light you carried was never yours alone. Every soul you guided became part of the next dawn." : "III · Build through the darkness to reach the next dawn.");
            foreach (var binding in purchases) UpdatePurchase(binding);
            if (skillTree != null) skillTree.Refresh();
        }
        // Lighting only: the nearest nearby tree selector's colour, or a fallback when
        // none is in range. Shared by the resources panel and the E-key context hint below.
        static string NearbyReceiverLabel(GameManager game)
        {
            var receiver = NearestReceiver(game);
            return receiver != null ? BridgeBlock.TierColour(receiver.ColorIndex).ToUpperInvariant() + " tile selector · E to request" : "Visit a Wisdom Tree for free tiles";
        }
        static string LightingDepositHint(GameManager game)
        {
            var receiver = NearestReceiver(game);
            return receiver != null ? "E TO REQUEST A FREE " + BridgeBlock.TierColour(receiver.ColorIndex).ToUpperInvariant() + " TILE" : game.LightingCarryHint;
        }
        static ColorReceiver NearestReceiver(GameManager game)
        {
            var particles = game.GetComponent<LightingParticles>();
            if (particles == null || game.player == null) return null;
            return particles.Nearest(game.player.transform.position + Vector3.up * .7f, LightingParticles.DepositRange, game.player.transform);
        }
        // The placement preview has no public handle, so the chip finds it by name once and caches it.
        // ponytail: one Find per second while it is missing; make GameManager expose the ghost if that ever shows up in a profile.
        void RefreshPlacePrompt()
        {
            if (placeChip == null) return;
            if (ghost == null && Time.unscaledTime >= ghostSearch)
            {
                ghostSearch = Time.unscaledTime + 1;
                var found = GameObject.Find("Next bridge block preview");
                if (found != null) ghost = found.transform;
            }
            if (gameCamera == null && game != null && game.cameraRig != null) gameCamera = game.cameraRig.GetComponent<Camera>();
            bool show = false;
            if (View == ScreenView.Playing && gameCamera != null && ghost != null && ghost.gameObject.activeInHierarchy)
            {
                var point = gameCamera.WorldToScreenPoint(ghost.position);
                // Behind the camera or off the frame: no chip. Otherwise keep it fully inside the frame.
                show = point.z > 1f && point.x >= 0 && point.y >= 0 && point.x <= Screen.width && point.y <= Screen.height;
                if (show)
                {
                    float scale = surface == null || surface.scaleFactor <= 0 ? 1 : surface.scaleFactor;
                    Vector2 half = placeChip.sizeDelta * .5f * scale;
                    placeChip.position = new Vector3(
                        Mathf.Clamp(point.x + half.x + 22, half.x + 6, Screen.width - half.x - 6),
                        Mathf.Clamp(point.y, half.y + 6, Screen.height - half.y - 6), 0);
                }
            }
            if(BridgeProfile.Current.isLighting)show=false;
            if (placeChip.gameObject.activeSelf != show) placeChip.gameObject.SetActive(show);
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
        {
            var r = Rect(name, parent, x, y, width, height);
            var image = r.gameObject.AddComponent<Image>();
            image.sprite = Fill(); image.type = Image.Type.Sliced; image.color = color;
            // ponytail: the gold ring is a stretched child rather than a second colour in the
            // sprite, because an Image tint multiplies the whole graphic and panels tint their fill.
            var edge = Rect("Edge", r, 0, 0, 0, 0);
            edge.anchorMin = Vector2.zero; edge.anchorMax = Vector2.one; edge.offsetMin = edge.offsetMax = Vector2.zero;
            var ring = edge.gameObject.AddComponent<Image>();
            ring.sprite = Edge(); ring.type = Image.Type.Sliced; ring.color = Mint; ring.raycastTarget = false;
            return r;
        }
        static Sprite Fill() => fillSprite != null ? fillSprite : fillSprite = Rounded(d => Mathf.Clamp01(.5f - d));
        static Sprite Edge() => edgeSprite != null ? edgeSprite : edgeSprite = Rounded(d => Mathf.Clamp01(1.1f - Mathf.Abs(d + .55f) * 1.7f));
        // One 32x32 rounded rect with a 10 px slice border dresses every panel and button.
        static Sprite Rounded(Func<float, float> alpha)
        {
            const int size = 32, radius = 8;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "HUD panel shape" };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + .5f, py = y + .5f;
                    float dx = px - Mathf.Clamp(px, radius, size - radius), dy = py - Mathf.Clamp(py, radius, size - radius);
                    // Signed distance to the rounded edge: negative inside, positive outside.
                    pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(alpha(Mathf.Sqrt(dx * dx + dy * dy) - radius)));
                }
            texture.SetPixels(pixels); texture.Apply();
            return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
        }
        // Quick Menu buttons read as a glyph first, with the caption and its key hint tucked underneath.
        void Iconify(Button button, string icon)
        {
            var art = Rect("Icon", button.transform, 19, 6, 28, 28).gameObject.AddComponent<Image>();
            art.sprite = IconSprite(icon); art.color = Mint; art.raycastTarget = false; art.preserveAspect = true;
            var caption = button.GetComponentInChildren<Text>().rectTransform;
            caption.anchoredPosition = new Vector2(6, -38); caption.sizeDelta = new Vector2(54, 26);
        }
        static Sprite IconSprite(string name)
        {
            if (iconSprites.TryGetValue(name, out var cached) && cached != null) return cached;
            var authored = Resources.Load<Sprite>("UI/Icons/" + name);
            return iconSprites[name] = authored != null ? authored : DrawIcon(name);
        }
        // ponytail: 48x48 hard-edged glyphs drawn in code so the HUD needs no art import.
        // Drop map.png / settings.png / bag.png into Resources/UI/Icons to override them with real art.
        static Sprite DrawIcon(string name)
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "HUD icon " + name };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + .5f - size * .5f, py = y + .5f - size * .5f;
                    bool on;
                    if (name == "settings")
                    {
                        // Gear: an annulus body with eight teeth cut by the angle.
                        float d = Mathf.Sqrt(px * px + py * py), a = Mathf.Atan2(py, px);
                        on = (d > 6 && d < 14) || (d >= 14 && d < 20 && Mathf.Cos(a * 8) > .45f);
                    }
                    else if (name == "bag")
                    {
                        // Rounded rect body with a strap arc springing from its top edge.
                        float bx = Mathf.Abs(px) - 11, by = Mathf.Abs(py + 4) - 9;
                        float body = Mathf.Sqrt(Mathf.Max(bx, 0) * Mathf.Max(bx, 0) + Mathf.Max(by, 0) * Mathf.Max(by, 0)) + Mathf.Min(Mathf.Max(bx, by), 0);
                        float strap = Mathf.Sqrt(px * px + (py - 9) * (py - 9));
                        on = body < 4 || (py > 9 && strap > 6 && strap < 9);
                    }
                    else
                    {
                        // Map: a rectangle outline plus two vertical fold creases.
                        bool inside = Mathf.Abs(px) < 18 && Mathf.Abs(py) < 14;
                        on = inside && (Mathf.Abs(px) > 15 || Mathf.Abs(py) > 11 || Mathf.Abs(Mathf.Abs(px) - 6) < 1.4f);
                    }
                    pixels[y * size + x] = new Color(1, 1, 1, on ? 1 : 0);
                }
            texture.SetPixels(pixels); texture.Apply();
            return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }
        // Legacy Text has no tracking, so caps are spaced by hand; the ornament degrades to what the font owns.
        static string Spaced(string value) => string.Join(" ", value.ToCharArray());
        string Glyph(params char[] options)
        {
            if (font != null) foreach (var option in options) if (font.HasCharacter(option)) return option.ToString();
            return "*";
        }
        RectTransform CenterPanel(string name, float width, float height)
        {
            var r = Panel(name, canvas, 0, 0, width, height, Ink); r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = Vector2.zero; return r;
        }
        static void Bottom(RectTransform r, float x, float y, bool right)
        { r.anchorMin = r.anchorMax = new Vector2(right ? 1 : 0, 0); r.pivot = new Vector2(right ? 1 : 0, 0); r.anchoredPosition = new Vector2(right ? -x : x, y); }
        // Screen and panel titles carry the display face; everything else stays on the body face.
        Text Heading(string name, Transform parent, string value, float x, float y, float width, float height, int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft)
        { var text = Label(name, parent, value, x, y, width, height, size, color, alignment); text.font = titleFont; return text; }
        Text Label(string name, Transform parent, string value, float x, float y, float width, float height, int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var text = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>(); text.font = font; text.text = value; text.fontSize = size;
            text.color = color; text.alignment = alignment; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        Button Button(string name, Transform parent, string caption, float x, float y, float width, float height, UnityAction action, int captionSize = 18)
        {
            var r = Panel(name, parent, x, y, width, height, new Color(.13f, .12f, .18f, .92f)); var button = r.gameObject.AddComponent<Button>();
            button.targetGraphic = r.GetComponent<Image>(); var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.45f, 1.28f, .90f); colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.75f, .63f, .43f); colors.disabledColor = new Color(.43f, .42f, .52f, .85f); button.colors = colors;
            Label("Label", r, caption, 6, 3, width - 12, height - 6, captionSize, White, TextAnchor.MiddleCenter);
            if (action != null) button.onClick.AddListener(action); return button;
        }
    }
}

