using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LightingBlocks
{
    // The powers screen is an actual tree: a trunk with the stone sources at its base, two
    // branches that grow as their parent awakens, and a burst on the node you just bought.
    // Everything is drawn from procedural circle sprites, so it needs no imported art.
    public sealed class SkillTree : MonoBehaviour
    {
        public GameManager game;
        public Font font;

        const float PanelWidth = 1180, PanelHeight = 800;
        static readonly Color Card = new Color(.12f, .105f, .09f, .98f);
        static readonly Color White = new Color(.95f, .90f, .82f);
        static readonly Color Muted = new Color(.73f, .69f, .61f);
        static readonly Color Mint = new Color(.90f, .72f, .36f);
        static readonly Color Gold = new Color(1f, .84f, .48f);
        static readonly Color Dormant = new Color(.34f, .30f, .26f);
        static readonly string[] Numerals = { "·", "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        sealed class Node
        {
            public string id, title, blurb;
            public Vector2 centre;
            public float size;
            public bool supply;
            public RectTransform circle;
            public Image ring, disc, burst;
            public Text numeral, label, price;
            public Image[] pips;
            public Button button;
            public int shown;
            public float punch, hover;
        }
        sealed class Edge
        {
            public Node from, to;
            public RectTransform rect;
            public Image line, spark;
            public float length, grown;
        }

        readonly List<Node> nodes = new List<Node>();
        readonly List<Edge> edges = new List<Edge>();
        RectTransform root;
        Text detailTitle, detailLevel, detailBlurb, detailRequirement;
        Button detailButton;
        Text detailButtonLabel;
        Node focus;
        float time;
        int clickFrame = -1;
        static Sprite discSprite, ringSprite, dotSprite;

        public static SkillTree Build(RectTransform panel, GameManager game, Font font, UnityEngine.Events.UnityAction close, out Button closeButton, out Dictionary<string, Button> skills)
        {
            var tree = panel.gameObject.AddComponent<SkillTree>();
            tree.game = game; tree.font = font; tree.root = panel;
            tree.Compose(close, out closeButton, out skills);
            return tree;
        }

        void Compose(UnityEngine.Events.UnityAction close, out Button closeButton, out Dictionary<string, Button> skills)
        {
            Label("Skill Heading", root, BridgeProfile.Current.isLighting ? "THE WISDOM TREE" : "THE TREE OF THE LIGHTING SOUL", 30, 76, 640, 36, 28, White);
            Label("Skill Help", root, BridgeProfile.Current.isLighting ? "Click a skill to awaken its next level. Learning needs a Wisdom Tree." : "Powers grow from the path you build. Souls keep flowing while you choose.",
                30, 112, 700, 24, 17, Muted);
            closeButton = Button("Skills Close", root, "CLOSE  [TAB]", PanelWidth - 190, 24, 160, 44, close);

            // Lighting keeps its movement and particle skills; legacy-only Strength/Links/Radiance/Soul Well
            // do not exist there (removed from both the tree and the purchase routes - see GameManager).
            if (BridgeProfile.Current.isLighting)
            {
                Add("speed", "SWIFT STRIDE", "Move faster across the platforms, with or without stones.", new Vector2(295, 550), 104);
                Add("soulgain", "PARTICLE YIELD", "Each level makes every particle pickup worth 0.25 more particles.", new Vector2(560, 550), 104);
                Add("dash", "DASH", "Burst forward, building tiles across the void.", new Vector2(430, 375), 104);
                Add("root", BridgeProfile.Current.isLighting ? "THE BRIDGE PATH" : "THE SOUL PATH", "Every power grows from the bridge you build.", new Vector2(430, 680), 76);
                Connect("root", "speed"); Connect("root", "soulgain"); Connect("root", "dash");
            }
            else
            {
                Add("strength", "ASTRAL STRENGTH", "Haul heavier stones from the deeper rift.", new Vector2(430, 200), 112);
                Add("links", "SOUL LINKS", "Gather orbiting stones with one click. Q drops or builds.", new Vector2(215, 375), 104);
                Add("radiance", "LIGHTING SOUL", "Reveal more of the dark around you.", new Vector2(645, 375), 104);
                Add("speed", "SWIFT STRIDE", "Move faster throughout the forest, with or without stones.", new Vector2(295, 550), 104);
                Add("generator", "SOUL WELL", "A second source of Souls, independent of the bridge.", new Vector2(560, 550), 104);
                Add("root", "THE SOUL PATH", "Every power grows from the bridge you build.", new Vector2(430, 680), 76);

                Connect("root", "speed"); Connect("root", "generator");
                Connect("speed", "links"); Connect("generator", "radiance");
                Connect("links", "strength"); Connect("radiance", "strength");
            }

            // Branches are drawn first so the nodes always sit on top of them.
            foreach (var edge in edges) BuildEdge(edge);
            foreach (var node in nodes) BuildNode(node);
            BuildDetail();

            skills = new Dictionary<string, Button>();
            foreach (var node in nodes)
                if (node.id != "root" && node.id != "grave") skills[node.id] = node.button;
            focus = Get("speed");
            Refresh();
        }

        void Add(string id, string title, string blurb, Vector2 centre, float size, bool supply = false)
            => nodes.Add(new Node { id = id, title = title, blurb = blurb, centre = centre, size = size, supply = supply });
        Node Get(string id) { foreach (var node in nodes) if (node.id == id) return node; return null; }
        void Connect(string from, string to) => edges.Add(new Edge { from = Get(from), to = Get(to) });

        void BuildEdge(Edge edge)
        {
            Vector2 delta = edge.to.centre - edge.from.centre;
            edge.length = delta.magnitude - (edge.from.size + edge.to.size) * .42f;
            var rect = Rect("Branch " + edge.from.id + " to " + edge.to.id, root, 0, 0, edge.length, 9);
            rect.pivot = new Vector2(0, .5f);
            Vector2 direction = delta.normalized;
            Vector2 start = edge.from.centre + direction * edge.from.size * .46f;
            rect.anchoredPosition = new Vector2(start.x, -start.y);
            // Canvas Y grows upward, the layout numbers grow downward, so the angle is negated.
            rect.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            var ghost = Rect("Branch ghost", root, 0, 0, edge.length, 5);
            ghost.pivot = rect.pivot; ghost.anchoredPosition = rect.anchoredPosition; ghost.localRotation = rect.localRotation;
            var ghostImage = ghost.gameObject.AddComponent<Image>();
            ghostImage.color = new Color(Dormant.r, Dormant.g, Dormant.b, .45f); ghostImage.raycastTarget = false;
            ghost.SetSiblingIndex(rect.GetSiblingIndex());
            edge.line = rect.gameObject.AddComponent<Image>();
            edge.line.color = Dormant; edge.line.raycastTarget = false;
            var spark = Rect("Branch spark", rect, 0, 0, 16, 16);
            spark.anchorMin = spark.anchorMax = new Vector2(0, .5f); spark.pivot = new Vector2(.5f, .5f);
            edge.spark = spark.gameObject.AddComponent<Image>();
            edge.spark.sprite = Dot(); edge.spark.color = new Color(1, .85f, .5f, 0); edge.spark.raycastTarget = false;
            edge.rect = rect;
        }
        void BuildNode(Node node)
        {
            var circle = Rect("Power " + node.id, root, node.centre.x - node.size * .5f, node.centre.y - node.size * .5f, node.size, node.size);
            node.circle = circle;
            node.disc = Graphic("Disc", circle, Disc(), new Color(.10f, .085f, .07f, .96f));
            node.ring = Graphic("Ring", circle, Ring(), Dormant);
            node.burst = Graphic("Burst", circle, Ring(), new Color(1, 1, 1, 0));
            node.numeral = Label("Level", circle, "·", 0, 0, node.size, node.size, Mathf.RoundToInt(node.size * .34f), Gold, TextAnchor.MiddleCenter);
            node.numeral.rectTransform.anchorMin = Vector2.zero; node.numeral.rectTransform.anchorMax = Vector2.one;
            node.numeral.rectTransform.offsetMin = node.numeral.rectTransform.offsetMax = Vector2.zero;

            float below = node.centre.y + node.size * .5f;
            int cap = Cap(node);
            node.pips = new Image[cap];
            float span = cap * 14 - 4;
            for (int i = 0; i < cap; i++)
            {
                var pip = Rect("Pip " + i, root, node.centre.x - span * .5f + i * 14, below + 8, 10, 10);
                node.pips[i] = pip.gameObject.AddComponent<Image>();
                node.pips[i].sprite = Dot(); node.pips[i].color = Dormant; node.pips[i].raycastTarget = false;
            }
            node.label = Label("Name " + node.id, root, node.title, node.centre.x - 110, below + 22, 220, 22, 16, White, TextAnchor.MiddleCenter);
            node.price = Label("Price " + node.id, root, "", node.centre.x - 110, below + 42, 220, 20, 15, Mint, TextAnchor.MiddleCenter);

            if (node.id == "root") return;
            node.disc.raycastTarget = true;
            node.button = circle.gameObject.AddComponent<Button>();
            node.button.targetGraphic = node.disc;
            var colors = node.button.colors;
            colors.normalColor = colors.highlightedColor = colors.selectedColor = colors.pressedColor = colors.disabledColor = Color.white;
            node.button.colors = colors;
            string id = node.id;
            if (!BridgeProfile.Current.isLighting) node.button.onClick.AddListener(() => Purchase(id));
        }
        void BuildDetail()
        {
            var panel = Rect("Power Detail", root, 860, 110, 300, 580);
            panel.gameObject.AddComponent<Image>().color = Card;
            detailTitle = Label("Detail Title", panel, "", 18, 18, 264, 56, 21, Mint);
            detailLevel = Label("Detail Level", panel, "", 18, 78, 264, 24, 17, Gold);
            detailBlurb = Label("Detail Blurb", panel, "", 18, 112, 264, 120, 17, White);
            detailRequirement = Label("Power Requirements", panel, "", 18, 240, 264, 230, 17, Muted);
            detailButton = Button("Detail Purchase", panel, "", 18, 500, 264, 56, BridgeProfile.Current.isLighting ? null : (UnityEngine.Events.UnityAction)(() => { if (focus != null) Purchase(focus.id); }));
            detailButtonLabel = detailButton.GetComponentInChildren<Text>();
        }
        // Lighting: clicking a skill awakens its next level (GameManager.Purchase checks tree, cap and
        // wallet, and spends the particles); AWAKEN in the detail card does the same for the focus.
        // A direct pointer test, like hover: EventSystem clicks were not reaching the panel.
        public bool ClickAt(Vector2 screenPoint)
        {
            foreach (var node in nodes)
                if (node.button != null && RectTransformUtility.RectangleContainsScreenPoint(node.circle, screenPoint)) { focus = node; Purchase(node.id); return true; }
            if (focus != null && detailButton.gameObject.activeInHierarchy && detailButton.interactable
                && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)detailButton.transform, screenPoint)) { Purchase(focus.id); return true; }
            return false;
        }
        void Purchase(string id)
        {
            if (game == null) return;
            game.Purchase(id);
            Refresh();
        }

        int Cap(Node node) => node.supply || node.id == "root" ? 1 : game != null ? game.Cap(node.id) : 1;
        int ParticleWalletUnits() => ForestWorld.For(game)?.SharedInventory ?? 0;
        static int ParticleCostUnits(double particles) => (int)System.Math.Ceiling(particles * ForestWorld.ParticleUnitsPerParticle - .000001);
        int Level(Node node) => node.id == "root" ? 1 : node.supply ? 1 : game != null ? game.Level(node.id) : 0;
        bool Locked(Node node) => node.id == "grave" && game != null && !game.Bridges[0];

        public void Refresh()
        {
            if (game == null || !gameObject.activeInHierarchy) return;
            float step = Mathf.Min(.05f, Time.unscaledDeltaTime);
            time += step;
            var mouse = Mouse.current;
            Vector2 pointer = mouse != null ? mouse.position.ReadValue() : new Vector2(-1000, -1000);
            // Lighting clicks use the same direct pointer test as hover (see ClickAt).
            // clickFrame stops a second Refresh in the same frame from buying twice.
            bool click = BridgeProfile.Current.isLighting && mouse != null && mouse.leftButton.wasPressedThisFrame && clickFrame != Time.frameCount && !GameHUD.SimulationPaused;
            if (click) { clickFrame = Time.frameCount; if (ClickAt(pointer)) return; }

            foreach (var node in nodes)
            {
                int level = Level(node), cap = Cap(node);
                bool supply = node.supply, maxed = !supply && node.id != "root" && level >= cap;
                bool locked = Locked(node);
                double cost = node.id == "root" ? 0 : game.Cost(node.id);
                bool affordable = !maxed && !locked && node.id != "root" && (BridgeProfile.Current.isLighting ? ParticleWalletUnits() >= ParticleCostUnits(cost) : game.souls + .0001 >= cost);
                bool atTree = !BridgeProfile.Current.isLighting || game.CanBuyLightingUpgrades();
                if(node.button != null) node.button.interactable = !GameHUD.SimulationPaused;

                if (node.shown != level) { node.shown = level; node.punch = 1; }
                bool over = node.button != null && RectTransformUtility.RectangleContainsScreenPoint(node.circle, pointer);
                if (over) focus = node;
                node.hover = Mathf.MoveTowards(node.hover, over ? 1 : 0, step * 6);
                node.punch = Mathf.Max(0, node.punch - step * 2.1f);

                float pop = node.punch * node.punch;
                float breathe = affordable ? Mathf.Sin(time * 3.4f) * .028f : 0;
                node.circle.localScale = Vector3.one * (1 + pop * .34f + breathe + node.hover * .07f);
                node.burst.color = new Color(1, .93f, .74f, node.punch * .85f);
                node.burst.rectTransform.localScale = Vector3.one * (1 + (1 - node.punch) * 1.9f);

                Color ring = maxed ? Gold
                    : locked ? new Color(.30f, .27f, .27f)
                    : affordable ? Color.Lerp(Dormant, Mint, .5f + Mathf.Sin(time * 3.4f) * .5f) // breathes from dim: available, not learned
                    : level > 0 ? new Color(.62f, .50f, .32f)
                    : Dormant;
                if (node.id == "root") ring = Gold;
                node.ring.color = Color.Lerp(ring, Color.white, pop);
                node.disc.color = Color.Lerp(new Color(.10f, .085f, .07f, .96f), new Color(.24f, .17f, .08f, .97f), level > 0 ? 1 : 0);
                node.numeral.text = supply ? "+" : node.id == "root" ? game.ConnectedCount.ToString() : Numerals[Mathf.Clamp(level, 0, Numerals.Length - 1)];
                node.numeral.color = Color.Lerp(level > 0 || supply ? Gold : Muted, Color.white, pop);
                node.label.color = Color.Lerp(White, Gold, node.hover);
                node.price.text = node.id == "root" ? "BLOCKS CONNECTED"
                    : maxed ? "FULLY AWAKENED"
                    : locked ? "LOCKED"
                    : !atTree ? "RETURN TO A WISDOM TREE"
                    : GameManager.Format(cost) + (BridgeProfile.Current.isLighting ? " particles" : " Souls");
                node.price.color = maxed ? Gold : locked ? Dormant : affordable ? Mint : new Color(.55f, .49f, .40f);
                for (int i = 0; i < node.pips.Length; i++)
                {
                    bool filled = supply || i < level;
                    var color = filled ? Gold : new Color(.26f, .23f, .20f);
                    if (filled && i == level - 1) color = Color.Lerp(color, Color.white, pop);
                    node.pips[i].color = color;
                    node.pips[i].rectTransform.localScale = Vector3.one * (filled && i == level - 1 ? 1 + pop * .8f : 1);
                }
            }

            foreach (var edge in edges)
            {
                bool live = Level(edge.from) > 0;
                edge.grown = Mathf.MoveTowards(edge.grown, live ? 1 : 0, step * 2.2f);
                float eased = Mathf.SmoothStep(0, 1, edge.grown);
                edge.rect.sizeDelta = new Vector2(Mathf.Max(2, edge.length * eased), 9);
                edge.line.color = Color.Lerp(Dormant, new Color(.78f, .58f, .28f), eased);
                bool flowing = eased > .98f;
                float travel = Mathf.Repeat(time * .42f + edge.from.centre.x * .003f, 1);
                edge.spark.rectTransform.anchoredPosition = new Vector2(edge.length * travel, 0);
                edge.spark.color = new Color(1, .88f, .56f, flowing ? Mathf.Sin(travel * Mathf.PI) * .85f : 0);
            }

            RefreshDetail();
        }
        void RefreshDetail()
        {
            if (focus == null) return;
            int level = Level(focus), cap = Cap(focus);
            bool supply = focus.supply, maxed = !supply && focus.id != "root" && level >= cap;
            bool locked = Locked(focus);
            double cost = focus.id == "root" ? 0 : game.Cost(focus.id);
            detailTitle.text = focus.title;
            detailLevel.text = focus.id == "root" ? "THE ROOT OF THE TREE"
                : supply ? "STONE SOURCE"
                : "LEVEL " + Numerals[Mathf.Clamp(level, 0, Numerals.Length - 1)] + "  /  " + Numerals[Mathf.Clamp(cap, 0, Numerals.Length - 1)];
            detailBlurb.text = focus.blurb;
            detailRequirement.text = Requirement(focus.id);
            bool buyable = focus.id != "root" && !maxed && !locked;
            detailButton.gameObject.SetActive(focus.id != "root");
            bool atTree = !BridgeProfile.Current.isLighting || game.CanBuyLightingUpgrades();
            detailButton.interactable = buyable && atTree && !GameHUD.SimulationPaused;
            detailButtonLabel.text = maxed ? "FULLY AWAKENED"
                : locked ? "LOCKED  ·  FINISH THE FIRST BRIDGE"
                : !atTree ? "RETURN TO A WISDOM TREE\nTO LEARN THIS SKILL" : "AWAKEN  ·  " + GameManager.Format(cost) + (BridgeProfile.Current.isLighting ? " particles" : " Souls");
            var colors = detailButton.colors;
            colors.normalColor = buyable && (BridgeProfile.Current.isLighting ? ParticleWalletUnits() >= ParticleCostUnits(cost) : game.souls + .0001 >= cost) && atTree ? Color.white : new Color(.61f, .56f, .47f);
            detailButton.colors = colors;
        }
        string Requirement(string id)
        {
            switch (id)
            {
                case "speed": return BridgeProfile.Current.isLighting ? "Hotkey [1].\nMove faster across platforms, with or without stones." : "Hotkey [1].\nEach level adds 0.65 m/s to walking and hauling speed.";
                case "soulgain": return BridgeProfile.Current.isLighting ? "Hotkey [2].\nEach level adds 0.25 particle to every pickup.\nCurrent pickup: " + (1 + .25 * game.soulGainLevel).ToString("0.00") + " particles." : "Hotkey [2].\nMultiplies your connected path's income.\nNow x" + (1 + .25 * game.soulGainLevel).ToString("0.00")
                    + ".\nNext level: x" + (1 + .25 * (game.soulGainLevel + 1)).ToString("0.00") + ".";
                case "dash": return "Press Shift to burst " + BoxPlayer.DashTiles[Mathf.Clamp(game.dashLevel, 1, BoxPlayer.DashTiles.Length) - 1] + " tiles the way you face or move (levels: " + string.Join("/", BoxPlayer.DashTiles) + ").\nDashing off an edge lays bridge tiles into the void.\n1-second cooldown. Unavailable while hauling.";
                case "links": return "Hotkey [2].\nOrbit " + game.Capacity + " stones at once.\nEach level adds one stone. Q drops them.";
                case "strength": return "Hotkey [3].\nGrave Stone needs Strength I.\nAncient Obsidian needs Strength III.";
                case "radiance": return "Hotkey [4].\nRange " + (9 + game.radiance * 3) + " m.\nEach level adds 3 m of light.";
                case "generator": return "Hotkey [5].\nNow +" + GameManager.Format(game.GeneratorRate) + " Souls/sec.\nNext level: +"
                    + GameManager.Format(3 * System.Math.Pow(1.6, game.generator)) + "/sec.";
                case "stone": return "Ordinary stones build the first bridge only.\nThey appear beside the first shore.";
                case "grave": return "Grave Stones bridge either rift.\nThey appear on the second shore.\nHauling them needs Strength I.";
                default: return "Connect blocks into a path. Every connected block feeds the tree.";
            }
        }

        // ---- canvas plumbing -------------------------------------------------

        RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
        Image Graphic(string name, Transform parent, Sprite sprite, Color color)
        {
            var rect = Rect(name, parent, 0, 0, 0, 0);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; rect.pivot = new Vector2(.5f, .5f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite; image.color = color; image.raycastTarget = false;
            return image;
        }
        Text Label(string name, Transform parent, string value, float x, float y, float width, float height, int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var text = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color; text.alignment = alignment;
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        Button Button(string name, Transform parent, string caption, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(name, parent, x, y, width, height);
            rect.gameObject.AddComponent<Image>().color = new Color(.30f, .235f, .145f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.3f, 1.5f, 1.4f);
            colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.75f, .63f, .43f);
            colors.disabledColor = new Color(.43f, .49f, .57f, .85f); button.colors = colors;
            Label("Label", rect, caption, 9, 3, width - 18, height - 6, 17, White, TextAnchor.MiddleCenter);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        static Sprite Disc() => discSprite != null ? discSprite : discSprite = Paint(d => Mathf.InverseLerp(1f, .94f, d));
        static Sprite Ring() => ringSprite != null ? ringSprite : ringSprite = Paint(d => Mathf.Clamp01(1 - Mathf.Abs(d - .87f) / .1f));
        static Sprite Dot() => dotSprite != null ? dotSprite : dotSprite = Paint(d => Mathf.InverseLerp(1f, .55f, d));
        static Sprite Paint(System.Func<float, float> alpha)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Skill tree shape" };
            var pixels = new Color[size * size];
            float half = (size - 1) * .5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(alpha(distance)));
                }
            texture.SetPixels(pixels); texture.Apply();
            return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }
    }
}
