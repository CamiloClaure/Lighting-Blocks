using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LightingBlocks
{
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        public BoxPlayer player;
        public StrategyCamera cameraRig;
        public Transform blocksRoot, soulWell, exit;
        public BridgeBlock[] blockPrefabs;
        public Light soulLight;
        [Header("Lighting Character Light")]
        [Tooltip("Lighting scene only. Brightness of the light following the character.")]
        [Min(0)] public float characterLightIntensity = 18f;
        [Tooltip("Lighting scene only. Light reach in metres, with a soft falloff.")]
        [Min(.1f)] public float characterLightRange = 24f;
        public Color characterLightColor = new Color(.65f, .85f, 1f);
        [Tooltip("Lighting scene only. How strongly the crystal tiles catch the character light.")]
        [Range(0, 2)] public float characterTileLight = .65f;
        [Header("Game State")]
        public Renderer wellCore;
        public GameObject nextAreaGlow;
        public bool persistenceEnabled = true, autoTick = true;
        public string savePathOverride;
        public double souls;
        public int speed, links, strength, radiance, generator, summoned;
        public int dashLevel;
        // Lighting-only Light Grenade (LightingGrenade): blast radius and throw reach grow per level.
        public int grenadeLevel;
        // Lighting-only Spirit Pull (LightingPull): F takes every dot within the grenade's radius for its level.
        public int pullLevel;
        // Lighting-only Far Sight (passive, LightingFog.World): each level sees one chunk (8 m) farther.
        public int sightLevel;
        // Lighting-only Daybreak (LightingReveal): V lifts the fog to 16 chunks for 1.5/3/5/8 s by level.
        public int revealLevel;
        // Lighting-only upgrade. Eight levels, price ceil(60 * 1.65^level), each adding 25% of base
        // connected-path income, applied once after existing path bonuses (see Recalculate). Wired
        // into Cost/Level/Purchase, gated on BridgeProfile.Current.isLighting.
        public int soulGainLevel;
        public bool completed;
        // Loose stones, plus (Lighting) the tile GameObjects of the chunks loaded near the player.
        public readonly List<BridgeBlock> Blocks = new List<BridgeBlock>();
        // Lighting: every placed tile, loaded or not; see LightingTiles.
        LightingTiles tiles;
        public LightingTiles Tiles => tiles ??= new LightingTiles(this);
        public readonly bool[] Bridges = new bool[2];
        public int ConnectedCount { get; private set; }
        public int lightingGridRevision { get; private set; }
        // LightingTiles: a tile GameObject came or went, so LightingFog.CullTiles looks again.
        internal void TilesChanged() => lightingGridRevision++;
        public double PathRate { get; private set; }
        public double GeneratorRate => BridgeProfile.Current.isLighting ? 0 : 3 * (Math.Pow(1.6, generator) - 1) / .6;
        public double Rate => BridgeProfile.Current.isLighting ? 0 : PathRate + GeneratorRate;
        public int Capacity => 1 + links;
        public string Notice { get; private set; }
        public float NoticeUntil { get; private set; }
        // Unity serializes an unset string field as "", not null, so treat both as "no override".
        public string SavePath => !string.IsNullOrEmpty(savePathOverride) ? savePathOverride : Path.Combine(Application.persistentDataPath, BridgeProfile.Current.saveFile);
        public static int Rows => BridgeProfile.Current.rows;
        float saveTimer;
        bool initialized;
        float ghostFade;
        bool saveBlockedByRejectedData;
        Transform ghost;
        Renderer[] ghostRenderers;
        MaterialPropertyBlock ghostProperties;
        Vector3 safePosition = new Vector3(0, .025f, 3);
        AudioSource audioSource;
        AudioClip chime;
        static MaterialPropertyBlock emissionProperties;
        bool dashSavePending; float nextDashChime;
        public int CurrentGap => Bridges[0] ? 1 : 0;
        readonly Dictionary<Vector2Int, int> lightingTierOverrides = new Dictionary<Vector2Int, int>();
        // The colour one bridge tile demands. Under mixedBridge every tile draws its own stone from
        // its own coordinates, so all three colours are wanted from the first crossing onward. It is a
        // pure function of the cell, so the pattern survives a reload with no save field, and the
        // ghost, the Wisdom Tree draw and FindPlacement cannot drift apart. Legacy profile: gap 1
        // wanted Grave Stone or better, gap 0 took anything.
        public static int RequiredTier(int gap, int column, int row)
        {
            if (BridgeProfile.Current.isLighting)
            {
                var cell = new Vector2Int(column,row);
                if (Instance != null && Instance.lightingTierOverrides.TryGetValue(cell, out int savedTier)) return savedTier;
                return LightingBridgeGrid.RequiredTier(column, row);
            }
            if (!BridgeProfile.Current.mixedBridge) return gap == 1 ? 1 : 0;
            unchecked
            {
                // ponytail: one xorshift-multiply hash. It only has to look unpatterned to someone
                // walking a 50-tile lane; reach for a real PRNG if the lanes ever read as striped.
                uint h = (uint)(gap * 73856093) ^ (uint)((column + 8) * 19349663) ^ (uint)((row + 1) * 83492791);
                h ^= h >> 13; h *= 2654435761u; h ^= h >> 16;
                return (int)(h % 3u);
            }
        }
        // Whether a stone may land on a cell. Every acceptance test routes through here, so the ghost
        // preview and the placement rule are one rule.
        public static bool Accepts(int gap, int column, int row, int tier)
        {
            if (BridgeProfile.Current.isLighting && Instance != null && Instance.lightingTierOverrides.TryGetValue(new Vector2Int(column,row), out int savedTier)) return tier == savedTier;
            return BridgeProfile.Current.mixedBridge ? tier == RequiredTier(gap, column, row) : gap != 1 || tier != 0;
        }
        // The cell a world point resolves to; the rounding FindPlacement has always used.
        public static void CellOf(Vector3 point, out int gap, out int column, out int row)
        {
            if (BridgeProfile.Current.isLighting)
            {
                var cell = LightingBridgeGrid.CellOf(point); gap = 0; column = cell.x; row = cell.y; return;
            }
            gap = GapAt(point.z);
            column = Mathf.Clamp(Mathf.RoundToInt(point.x / 2), -2, 2);
            row = Mathf.Clamp(Mathf.RoundToInt((point.z - Landscape.BridgeStart(gap)) / 2), 0, Rows - 1);
        }
        public static Vector3 Cell(int gap, int column, int row) => BridgeProfile.Current.isLighting ? LightingBridgeGrid.Cell(column,row) : new Vector3(column * 2, -.18f, Landscape.BridgeStart(gap) + row * 2);
        // Which crossing a depth belongs to. Splitting at the midpoint between the two lane starts
        // worked while both lanes were 14 m long and 67 m apart, but a 100 m crossing swallows that
        // midpoint, so a stone two thirds of the way over the first rift was being read as the
        // second. Measure the distance to each lane instead and take the nearer.
        public static int GapAt(float z)
        {
            float first = Landscape.BridgeStart(0), second = Landscape.BridgeStart(1), reach = (Rows - 1) * 2;
            float toFirst = z < first ? first - z : z > first + reach ? z - first - reach : 0;
            float toSecond = z < second ? second - z : z > second + reach ? z - second - reach : 0;
            return toSecond < toFirst ? 1 : 0;
        }
        // One open 16-point walk over the 12 cube edges; three edges are retraced because every corner has odd degree.
        // Corner index bits are x, y, z, so bit 0 picks max.x, bit 1 max.y, bit 2 max.z.
        static readonly int[] GhostEdgeWalk = { 0, 1, 5, 1, 3, 2, 3, 7, 6, 4, 6, 2, 0, 4, 5, 7 };
        // Share of the first tree's random draw that lands on the tier the current crossing needs. See TrySpawnAtWisdomTree.
        const float RequiredShare = .6f;

        void Awake()
        {
            BridgeProfile.Select(gameObject.scene.name);
            Instance = this;
            ghostProperties = new MaterialPropertyBlock();
            if(ForestPerformanceProbe.Requested||TwilightEvidence.Requested)persistenceEnabled=false;
#if UNITY_EDITOR
            if (File.Exists(Path.Combine(Application.dataPath, "../Library/LightingBlocks.test-active"))) persistenceEnabled = false;
            if(UnityEditor.SessionState.GetBool("LightingIntegrationCheck.Play",false)||UnityEditor.SessionState.GetBool("LightingParticleCheck.Play",false)||UnityEditor.SessionState.GetBool("LightingVisibilityCheck.Play",false)||UnityEditor.SessionState.GetBool("LightingWorldCheck.Play",false)||UnityEditor.SessionState.GetBool("Forest.Test",false)||UnityEditor.SessionState.GetBool("LightingLegacyCompatibilityCheck.Play",false)||UnityEditor.SessionState.GetBool("LightingDashCheck.Play",false))
            {persistenceEnabled=false;savePathOverride=Path.Combine(Application.temporaryCachePath,"lighting-check-"+Guid.NewGuid().ToString("N")+".json");}
#endif
            audioSource = gameObject.AddComponent<AudioSource>(); audioSource.volume = .18f;
            chime = AudioClip.Create("Placeholder soul connection", 11025, 1, 44100, false);
            float[] wave = new float[11025];
            for (int i = 0; i < wave.Length; i++) { float t = i / 44100f; wave[i] = Mathf.Sin(t * 880 * Mathf.PI * 2) * Mathf.Exp(-20 * t) * .4f; }
            chime.SetData(wave, 0);
        }
        void Start()
        {
            Blocks.Clear(); Blocks.AddRange(blocksRoot.GetComponentsInChildren<BridgeBlock>(true));
            if (Landscape.Shifted)
            {
                // Scene-authored props and loose blocks are still positioned in the shipped layout;
                // carry them onto their island now that Landscape has moved it. Placed blocks are
                // re-seated by ConfigureBlock from Cell(...) below, so only the loose ones need this.
                if (exit != null) exit.position = Landscape.ImportPosition(exit.position);
                if (soulWell != null) soulWell.position = Landscape.ImportPosition(soulWell.position);
                if (wellCore != null) wellCore.transform.position = Landscape.ImportPosition(wellCore.transform.position);
                if (nextAreaGlow != null) nextAreaGlow.transform.position = Landscape.ImportPosition(nextAreaGlow.transform.position);
                foreach (var b in Blocks) if (!b.placed) b.transform.position = Landscape.ImportPosition(b.transform.position);
            }
            foreach(var b in Blocks) ConfigureBlock(b);
            ApplyUpgrades(); Recalculate(); initialized = true;
            var forest = ForestWorld.For(this); if (forest != null) forest.BeginSession();
            bool safeToSave = true;
            if (persistenceEnabled) safeToSave = Load();
            // The preview is the authored placed crystal itself, one clone per tier, so each tier previews as it will land.
            ghost = new GameObject("Next bridge block preview").transform;
            var previewProperties = new MaterialPropertyBlock();
            // The ghost reads as a dashed wireframe: 8 lit texels then 8 clear, tiled along every edge.
            var dashes = new Texture2D(16, 1, TextureFormat.RGBA32, false) { name = "Ghost dash", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int d = 0; d < 16; d++) dashes.SetPixel(d, 0, new Color(1, 1, 1, d < 8 ? 1 : 0));
            dashes.Apply();
            var edgeShader = Shader.Find("LightingBlocks/Ghost Edges"); var ghostEdgeShader = edgeShader != null ? edgeShader : Shader.Find("Universal Render Pipeline/Unlit");
            Material ghostEdgeInk = null;
            if (ghostEdgeShader != null)
            {
                // ponytail: both property spellings, so the URP Unlit fallback still tints before GhostEdges.shader imports. The fallback draws the line solid, not dashed.
                ghostEdgeInk = new Material(ghostEdgeShader) { name = "Ghost edges" };
                ghostEdgeInk.SetTexture("_MainTex", dashes); ghostEdgeInk.SetTexture("_BaseMap", dashes);
                ghostEdgeInk.SetColor("_Color", new Color(.55f, .7f, 1.6f)); ghostEdgeInk.SetColor("_BaseColor", new Color(.55f, .7f, 1.6f));
            }
            for (int tier = 0; tier < blockPrefabs.Length; tier++)
            {
                var art = blockPrefabs[tier] == null ? null : blockPrefabs[tier].GetComponent<BlockArt>();
                var visual = art == null || art.placedVisual == null
                    ? new GameObject("Tier " + tier)
                    : Instantiate(art.placedVisual.gameObject);
                visual.name = "Tier " + tier; visual.transform.SetParent(ghost, false);
                // A placed tile renders at world scale 1; the preview has no logical root to cancel, and Update offsets by .205.
                visual.transform.localScale = Vector3.one; visual.transform.localPosition = new Vector3(0, -.205f, 0);
                foreach (var collider in visual.GetComponentsInChildren<Collider>()) Destroy(collider);
                // ponytail: a placed-tile sized stand-in when a tier has no art, so the wireframe still frames a cell instead of collapsing to a point.
                var box = new Bounds(visual.transform.position, new Vector3(2, .36f, 2)); bool framed = false;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.GetPropertyBlock(previewProperties); previewProperties.SetFloat("_Highlight", 1); previewProperties.SetFloat("_Opacity", .10f); previewProperties.SetFloat("_Glow", .25f); renderer.SetPropertyBlock(previewProperties);
                    // Hanging light trails do not enlarge the placement footprint.
                    if (renderer.name == "Crystal runes") continue;
                    if (framed) box.Encapsulate(renderer.bounds); else { box = renderer.bounds; framed = true; }
                }
                // Parented to the tier child, so Update's existing +.205 offset carries the edges with the body.
                var edges = new GameObject("Ghost edges", typeof(LineRenderer)); edges.transform.SetParent(visual.transform, false);
                var line = edges.GetComponent<LineRenderer>();
                line.useWorldSpace = false; line.loop = false; line.positionCount = GhostEdgeWalk.Length; line.widthMultiplier = .035f; line.textureMode = LineTextureMode.Tile;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false; line.sharedMaterial = ghostEdgeInk;
                for (int p = 0; p < GhostEdgeWalk.Length; p++) { int c = GhostEdgeWalk[p]; line.SetPosition(p, visual.transform.InverseTransformPoint(new Vector3((c & 1) == 0 ? box.min.x : box.max.x, (c & 2) == 0 ? box.min.y : box.max.y, (c & 4) == 0 ? box.min.z : box.max.z))); }
                visual.SetActive(tier == 0);
            }
            ghostRenderers=ghost.GetComponentsInChildren<Renderer>(true);
            if (string.IsNullOrEmpty(Notice)) Notify(BridgeProfile.Current.isLighting?LightingCarryHint:"THE WISDOM TREE\nCollect the bright particles. Press E beside the violet tree to charge it and release a bridge block.", false);
            cameraRig.CenterOnPlayer();
            WorldDressing.Build(this);
            if (BridgeProfile.Current.isLighting && GetComponent<LightingGrenade>() == null) gameObject.AddComponent<LightingGrenade>();
            if (BridgeProfile.Current.isLighting && GetComponent<LightingPull>() == null) gameObject.AddComponent<LightingPull>();
            if (BridgeProfile.Current.isLighting && GetComponent<LightingReveal>() == null) gameObject.AddComponent<LightingReveal>();
            if (forest != null) { forest.Build(); if (safeToSave) Save(); }
            GameAudio.PlayMusic("IntroGusta");
        }
        void Update()
        {
            if (!initialized) return;
            if (dashSavePending && !player.IsDashing) { dashSavePending = false; Save(); }
            if (BridgeProfile.Current.isLighting) Tiles.Stream(player.transform.position);
            if (GameHUD.SimulationPaused) { if(ghost!=null)ghost.gameObject.SetActive(false);ghostFade=0;return; }
            if (autoTick) Advance(Time.deltaTime);
            if (!GameHUD.GameplayBlocked && player.readInput && Application.isFocused && Keyboard.current != null)
            {
                var k = Keyboard.current;
                if (k.eKey.wasPressedThisFrame) Interact();
                // Holding E keeps the Wisdom Tree channelling: BeginCharge no-ops while a charge is
                // running, so this only re-arms on the frame after one completes. Silent, so an empty
                // satchel does not repaint the same refusal notice every frame.
                if (!BridgeProfile.Current.isLighting && BridgeProfile.Current.holdChannel && k.eKey.isPressed && !k.eKey.wasPressedThisFrame)
                    ForestWorld.For(this)?.Nearby(player.transform.position)?.BeginCharge(true);
                if (k.rKey.wasPressedThisFrame) ReturnStones();
                // ponytail: test cheat. P fills the satchel on the island underfoot, so bridge
                // behaviour can be exercised without hand-farming 150 particles. Delete the branch
                // and GrantParticles to ship.
                if (!BridgeProfile.Current.isLighting && k.pKey.wasPressedThisFrame) GrantParticles();
                // ponytail: test cheat. L adds 25 particles to the Lighting wallet. Delete to ship.
                if (BridgeProfile.Current.isLighting && k.lKey.wasPressedThisFrame && ForestWorld.For(this) is ForestWorld cheatWorld && cheatWorld.TryAddInventory(25 * ForestWorld.ParticleUnitsPerParticle))
                { Notify("PARTICLES GRANTED\n+25 particles · " + cheatWorld.SharedInventoryLabel, false); Save(); }
                // Lighting's Speed and Particle Yield are passives, learned in the skill tree only.
                if (!BridgeProfile.Current.isLighting)
                {
                    if (k.digit1Key.wasPressedThisFrame) Purchase("speed");
                    if (k.digit2Key.wasPressedThisFrame) Purchase("links");
                    if (k.digit3Key.wasPressedThisFrame) Purchase("strength");
                    if (k.digit4Key.wasPressedThisFrame) Purchase("radiance");
                    if (k.digit5Key.wasPressedThisFrame) Purchase("generator");
                }
            }
            // Every movement step also tests support. This catches external teleports or removed terrain.
            if (player.transform.position.y < -3) RecoverPlayer();
            else if (CanStand(player.transform.position)) safePosition = player.transform.position;
            if (ghost != null)
            {
                bool held = player.HeldBox != null;
                var block = held ? player.HeldBox.GetComponent<BridgeBlock>() : null;
                // Preview the tier that will actually land: the held crystal, or the starter tier when empty handed.
                int previewTier = block == null ? 0 : Mathf.Clamp(block.tier, 0, ghost.childCount - 1);
                // The gap of the placement being previewed, falling back to the next gap to build when
                // nothing here resolves to a valid placement.
                int targetGap = CurrentGap, targetColumn = 0, targetRow = 0;
                bool ghostCanShow = true;
                if(BridgeProfile.Current.isLighting&&player.Orbiting&&TryGetLightingPreviewTarget(out var lightingOrbitCell))
                {
                    ghost.position = lightingOrbitCell + Vector3.up * .205f;
                    CellOf(lightingOrbitCell, out targetGap, out targetColumn, out targetRow);
                }
                else if (player.Orbiting && FindOrbitPlacement(block,out var orbitCell))
                {
                    ghost.position = orbitCell + Vector3.up * .205f;
                    CellOf(orbitCell, out targetGap, out targetColumn, out targetRow);
                }
                else if (block != null && FindPlacement(block, out int g, out int x, out int z, out _))
                {
                    ghost.position = Cell(g, x, z) + Vector3.up * .205f;
                    targetGap = g; targetColumn = x; targetRow = z;
                }
                else if (BridgeProfile.Current.isLighting)
                {
                    Vector3 facing = player.visual != null ? player.visual.forward : Vector3.forward;
                    bool found=false; Vector2Int next=default;
                    if(block!=null)
                    {
                        next=LightingBridgeGrid.CellOf(block.transform.position); Vector3 direct=LightingBridgeGrid.Cell(next.x,next.y)-player.transform.position;direct.y=0;
                        found=direct.magnitude<=4f&&LightingBridgeGrid.IsFrontier(this,next)&&!LightingBridgeGrid.HasObstruction(next,this,block);
                    }
                    if (!found) found=LightingBridgeGrid.TryGetFrontier(this, player.transform.position, facing, out next);
                    if (found)
                    {
                        ghost.position = LightingBridgeGrid.Cell(next.x,next.y) + Vector3.up * .205f;
                        targetGap = 0; targetColumn = next.x; targetRow = next.y;
                    }
                    else ghostCanShow = false;
                }
                else
                {
                    var next = NextCell();
                    ghost.position = next + Vector3.up * .205f;
                    CellOf(next, out targetGap, out targetColumn, out targetRow);
                }
                if (BridgeProfile.Current.requiredTierGhost)
                {
                    // Show the stone this tile needs, unless the stone in hand is about to land here:
                    // it is about to land, and showing a different colour would be a lie.
                    if (block == null || !Accepts(targetGap, targetColumn, targetRow, block.tier))
                        previewTier = Mathf.Clamp(RequiredTier(targetGap, targetColumn, targetRow), 0, ghost.childCount - 1);
                }
                for (int i = 0; i < ghost.childCount; i++) ghost.GetChild(i).gameObject.SetActive(i == previewTier);
                if (BridgeProfile.Current.isLighting)
                {
                    Vector3 ghostDelta=ghost.position-player.transform.position; ghostDelta.y=0;
                    ghostFade=StepLightingPreviewFade(ghostFade,ghostCanShow&&!GameHUD.GameplayBlocked&&CanStand(player.transform.position),ghostDelta.magnitude,Time.deltaTime);
                    foreach(var renderer in ghostRenderers)
                    {
                        renderer.GetPropertyBlock(ghostProperties); ghostProperties.SetFloat("_Opacity",.1f*ghostFade); ghostProperties.SetFloat("_PreviewFade",ghostFade); renderer.SetPropertyBlock(ghostProperties);
                    }
                    ghost.gameObject.SetActive(ghostFade>.001f);
                }
                else ghost.gameObject.SetActive(!Bridges[1]);
            }
        }
        public void Advance(float seconds)
        {
            if (GameHUD.SimulationPaused || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            if (BridgeProfile.Current.isLighting)
            {
                saveTimer += seconds;
                if (saveTimer >= 10) { saveTimer = 0; Save(); }
                return;
            }
            if (BridgeProfile.Current.isLighting) return; // Lighting particles have no passive, path, or offline income.
            souls += Rate * seconds; saveTimer += seconds;
            if (saveTimer >= 10) { saveTimer = 0; Save(); }
        }
        public double Cost(string id)
        {
            switch (id)
            {
                case "speed": return Math.Ceiling((BridgeProfile.Current.isLighting?10:35) * Math.Pow(1.6, speed));
                case "links": return Math.Ceiling(80 * Math.Pow(2.2, links));
                case "strength": return 90 * Math.Pow(2, strength);
                case "radiance": return Math.Ceiling(30 * Math.Pow(1.65, radiance));
                case "generator": return Math.Ceiling(60 * Math.Pow(1.65, generator));
                case "soulgain": return BridgeProfile.Current.isLighting ? Math.Ceiling(15 * Math.Pow(1.65, soulGainLevel)) : Math.Ceiling(60 * Math.Pow(1.65, soulGainLevel));
                case "dash": return BridgeProfile.Current.isLighting ? Math.Ceiling(25 * Math.Pow(1.8, dashLevel)) : 0;
                case "grenade": return BridgeProfile.Current.isLighting ? Math.Ceiling(40 * Math.Pow(1.8, grenadeLevel)) : 0;
                case "pull": return BridgeProfile.Current.isLighting ? Math.Ceiling(30 * Math.Pow(1.8, pullLevel)) : 0;
                // ponytail: tune.
                case "sight": return BridgeProfile.Current.isLighting ? Math.Ceiling(20 * Math.Pow(1.7, sightLevel)) : 0;
                case "reveal": return BridgeProfile.Current.isLighting ? Math.Ceiling(35 * Math.Pow(1.8, revealLevel)) : 0;
                case "stone": return Math.Min(60, Math.Ceiling(12 * Math.Pow(1.12, summoned)));
                case "grave": return 45;
                default: return 0;
            }
        }
        public int Level(string id) => id == "dash" ? dashLevel : id == "grenade" ? grenadeLevel : id == "pull" ? pullLevel : id == "sight" ? sightLevel : id == "reveal" ? revealLevel : id == "speed" ? speed : id == "links" ? links : id == "strength" ? strength : id == "radiance" ? radiance : id == "soulgain" ? soulGainLevel : generator;
        public int Cap(string id) => id == "dash" ? BoxPlayer.DashTiles.Length : id == "grenade" || id == "pull" ? LightingGrenade.GrenadeTiles.Length : id == "sight" ? LightingFog.SightLevels : id == "reveal" ? LightingReveal.Seconds.Length : id == "speed" ? 6 : id == "links" || id == "strength" ? 3 : id == "radiance" ? 5 : 8;
        public bool Purchase(string id)
        {
            if (GameHUD.SimulationPaused) return false;
            if (BridgeProfile.Current.isLighting)
            {
                if (id != "speed" && id != "soulgain" && id != "dash" && id != "grenade" && id != "pull" && id != "sight" && id != "reveal") return false;
                if (Level(id) >= Cap(id)) return Deny("FULLY AWAKENED\nThis power has reached its maximum level.");
                if (!CanBuyLightingUpgrades()) return Deny("STAND BY A WISDOM TREE\nSkill purchases need supported ground within 3 metres of any Wisdom Tree.");
                var world = ForestWorld.For(this);
                int unitPrice = checked((int)Math.Ceiling(Cost(id) * ForestWorld.ParticleUnitsPerParticle));
                if (world == null || world.SharedInventory < unitPrice) return Deny("NOT ENOUGH PARTICLES\nHave " + (world==null?"0":world.SharedInventoryLabel) + " / " + Format(Cost(id)) + " particles.");
                if (!world.TrySpendInventory(unitPrice)) return false;
                switch (id) { case "speed": speed++; break; case "soulgain": soulGainLevel++; break; case "dash": dashLevel++; break; case "grenade": grenadeLevel++; break; case "pull": pullLevel++; break; case "sight": sightLevel++; break; case "reveal": revealLevel++; break; }
                ApplyUpgrades(); // no Recalculate: none of these skills changes a tile or the network

                Notify(id == "speed" ? "SPEED AWAKENED\nWalking and hauling are faster." : id == "soulgain" ? "PARTICLE YIELD " + soulGainLevel + "\nPickups now yield " + (1 + .25 * soulGainLevel).ToString("0.00") + " particles." : id == "grenade" ? "LIGHT GRENADE " + grenadeLevel + "\nHold right mouse to charge, release to throw up to " + LightingGrenade.GrenadeTiles[grenadeLevel - 1] + " tiles (" + LightingGrenade.ThrowCost + " particles)."
                    : id == "pull" ? "SPIRIT PULL " + pullLevel + "\nPress F to pull every particle within " + LightingPull.Radius(pullLevel) + " tiles (" + LightingPull.Cooldown + " s cooldown)."
                    : id == "sight" ? "FAR SIGHT " + sightLevel + "\nYou now see " + (GetComponent<LightingFog>() is LightingFog sightFog ? sightFog.World(this).black : 24 + LightingFog.SightStep * sightLevel).ToString("0") + " metres into the dark."
                    : id == "reveal" ? "DAYBREAK " + revealLevel + "\nPress V to lift the fog for " + LightingReveal.Seconds[revealLevel - 1] + " s (" + LightingReveal.Cooldown + " s cooldown)."
                    : "DASH " + dashLevel + "\nShift bursts " + BoxPlayer.DashTiles[dashLevel - 1] + " tiles, building across the void.");
                Save(); return true;
            }
            if (id == "stone" || id == "grave") return Deny("WISDOM TREES PROVIDE BLOCKS\nCollect particles and press E beside a Wisdom Tree.");
            // Lighting keeps only Speed and Soul Gain; the other three legacy powers are unreachable there.
            var validIds = BridgeProfile.Current.isLighting
                ? new[] { "speed", "soulgain", "stone", "grave" }
                : new[] { "speed", "links", "strength", "radiance", "generator", "stone", "grave" };
            if (!validIds.Contains(id)) return false;
            bool supply = id == "stone" || id == "grave";
            if (!supply && Level(id) >= Cap(id)) return Deny("FULLY AWAKENED\nThis power has reached its maximum level.");
            if (id == "grave" && !Bridges[0]) return Deny("GRAVE STONES LOCKED\nFinish the first bridge to reach the next stone source.");
            double cost = Cost(id);
            if (souls + .0001 < cost) return Deny("NOT ENOUGH SOULS\nHave " + Format(souls) + " / " + Format(cost) + ". Need " + Format(cost - souls) + " more.\nExtend the connected path or improve your Soul Well.");
            if (supply && !Spawn(id == "stone" ? 0 : 1, id == "stone" ? 0 : 1)) return Deny("STONE SOURCE BLOCKED\nMove loose blocks away from the source first.");
            souls -= cost;
            switch (id)
            {
                case "speed": speed++; break; case "links": links++; break; case "strength": strength++; break;
                case "radiance": radiance++; break; case "generator": generator++; break; case "soulgain": soulGainLevel++; break; default: summoned++; break;
            }
            // Recalculate, not just ApplyUpgrades: Soul Gain changes the cached PathRate itself, which
            // is only recomputed inside Recalculate() (unlike GeneratorRate, a live computed property).
            ApplyUpgrades(); Recalculate();
            Notify(supply ? "A NEW STONE ANSWERS\nIt waits on the island beside the stone source."
                : id == "generator" ? "SOUL WELL AWAKENED\nIndependent income: +" + Format(GeneratorRate) + " Souls/sec."
                : id == "soulgain" ? "SOUL GAIN " + soulGainLevel + "\nYour connected path now yields x" + (1 + .25 * soulGainLevel).ToString("0.00") + " Souls."
                : id == "links" ? "SOUL LINKS " + links + "\nClick to gather up to " + Capacity + " floating stones. Q drops or builds them."
                : id == "radiance" ? "LIGHTING SOUL " + radiance + "\nYour light now reaches " + soulLight.range.ToString("0") + " metres."
                : "POWER AWAKENED\n" + (id == "speed" ? "Walking and hauling are faster." : "Strength level " + strength + "." + (BridgeProfile.Current.equalWeight ? "" : " Grave Stones need I; Obsidian needs III.")));
            Save(); return true;
        }
        public bool CanBuyLightingUpgrades()
        {
            if (!BridgeProfile.Current.isLighting || !CanStand(player.transform.position)) return false;
            var forest = ForestWorld.For(this); if (forest == null) return false;
            Vector2 playerXZ = new Vector2(player.transform.position.x, player.transform.position.z);
            return forest.WisdomTrees.Any(tree => tree != null && Vector2.Distance(playerXZ, new Vector2(tree.transform.position.x, tree.transform.position.z)) <= 3f);
        }
        public string LightingCarryHint => ForestWorld.For(this)?.enableLightingParticles == true
            ? "Walk over loose particles to collect them. Press E at a Wisdom Tree to request a free coloured bridge tile."
            : "Stand on a hub or connected tile. Hold SPACE to carry a nearby bridge block.";
        public void ApplyUpgrades()
        {
            player.moveSpeed = 6f + speed * .65f; player.pullSpeed = 3.2f + speed * .65f; player.grabReach = 1.4f;
            if (BridgeProfile.Current.isLighting) ApplyCharacterLight();
            else if (soulLight != null) { soulLight.range = 9 + radiance * 3; soulLight.intensity = 3.5f + radiance * .5f; }
            if (wellCore != null) wellCore.gameObject.SetActive(generator > 0);
        }
        // Separate from legacy Radiance upgrades: authored Lighting settings survive load and purchases.
        public void ApplyCharacterLight()
        {
            if (!BridgeProfile.Current.isLighting || soulLight == null) return;
            soulLight.intensity = Mathf.Max(0, characterLightIntensity);
            soulLight.range = Mathf.Max(.1f, characterLightRange);
            soulLight.color = characterLightColor;
            // The orbiting source sits close to the character; self-shadowing hid the body and path.
            soulLight.shadows = LightShadows.None;
        }
        public bool Spawn(int tier, int island) => false; // Legacy paid supply is retired.
        // forceTier skips the draw. The offline fixtures in ForestPerformanceProbe and TwilightEvidence
        // build a whole completed bridge and cannot roll dice for the colour of 50 tiles in a row.
        public bool TrySpawnAtWisdomTree(string islandId, out BridgeBlock block, int forceTier = -1)
        {
            block = null;
            if(BridgeProfile.Current.isLighting&&Blocks.Count(b=>!b.placed)>=150)return false;
            var forest = ForestWorld.For(this);
            if (forest == null) return false;
            var definition = forest.configuration.Find(islandId);
            if (definition == null || !forest.FindOutput(definition, out var point)) return false;
            // Every tree answers with a random stone, weighted toward the colour the bridge wants
            // next: an equal draw over the three tiers made two stones in three unplaceable, so the
            // wanted tier now comes up about three times in five. It has to be every tree, not just
            // the first: a gap-1 tile can ask for blue or amber, and island 2's configured tier is
            // Grave alone, which would mean hauling the other two colours 100 m back from island 1.
            int tier = definition.blockTier;
            if (forceTier >= 0) tier = Mathf.Clamp(forceTier, 0, blockPrefabs.Length - 1);
            else if (BridgeProfile.Current.randomTreeStones)
            {
                int count = Mathf.Min(3, blockPrefabs.Length);
                // Aim at the next forward tile, not at the crossing: every tile asks for its own
                // colour now, so the only useful draw is whatever the column-0 head wants next. The
                // 40% tail still supplies the side columns, which want their own colours.
                CellOf(NextCell(), out int nextGap, out int nextColumn, out int nextRow);
                int wanted = Mathf.Clamp(RequiredTier(nextGap, nextColumn, nextRow), 0, count - 1);
                // Two-step draw: RequiredShare chance of the wanted tier, else uniform over the other
                // two, gives exactly 60/20/20. Rolling "wanted, else uniform over all three" would
                // still land on wanted a third of the time in the else branch, giving 73/13/13 instead.
                if (UnityEngine.Random.value < RequiredShare) tier = wanted;
                else { int other = UnityEngine.Random.Range(0, count - 1); if (other >= wanted) other++; tier = other; }
            }
            BridgeBlock created = null;
            try {
                created = Instantiate(blockPrefabs[tier], point, Quaternion.identity, blocksRoot);
                created.id = "wisdom-" + Guid.NewGuid().ToString("N");
                ConfigureBlock(created); Blocks.Add(created); Physics.SyncTransforms(); block = created; return true;
            } catch (Exception e) {
                if (created != null) { Blocks.Remove(created); created.gameObject.SetActive(false); Destroy(created.gameObject); }
                Debug.LogException(e); return false;
            }
        }
        // Test aid: top the island underfoot up to a full satchel.
        public bool GrantParticles()
        {
            var forest = ForestWorld.For(this);
            var island = forest == null ? null : forest.IslandAt(player.transform.position);
            var inventory = island == null ? null : forest.Inventory(island.id);
            if (inventory == null) { Notify("STAND ON AN ISLAND\nParticles stay with their island.", false); return false; }
            int before = inventory.inventory;
            // 30 is ForestWorld.Valid's ceiling: a save above it will not load again.
            inventory.inventory = 30;
            Notify("PARTICLES GRANTED\n+" + (30 - before) + " fundamental particles here - 30 / 30");
            Save();
            return true;
        }
        // Random draws hand out stones the first rift refuses and Strength cannot lift, so the tree takes
        // them back. Returning a stone refunds exactly what the tree charged for it: 3 particles.
        public bool ReturnStones()
        {
            if (!BridgeProfile.Current.treeReturn || GameHUD.GameplayBlocked) return false;
            var forest = ForestWorld.For(this);
            var tree = forest?.Nearby(player.transform.position);
            if (tree == null) { Notify("STAND BESIDE A WISDOM TREE\nIt can only take stones back from its own clearing.", false); return false; }
            var loose = Blocks.Where(b => !b.placed && b.Box.Holder == null && !player.HeldBoxes.Contains(b.Box)
                && new Vector2(b.transform.position.x - tree.transform.position.x, b.transform.position.z - tree.transform.position.z).magnitude <= 4).ToArray();
            if (loose.Length == 0) { Notify("NO LOOSE STONES HERE\nDrag unwanted stones into the tree's clearing first.", false); return false; }
            // Lighting: refunds go into the shared inventory (see ForestWorld.TryAddInventory), not
            // any one island's bank - a tile is never destroyed if its full 3-particle refund cannot fit.
            var inventory = BridgeProfile.Current.isLighting ? null : forest.Inventory(tree.Definition.id);
            int returned=0;
            foreach (var b in loose)
            {
                returned++;Blocks.Remove(b); b.gameObject.SetActive(false); Destroy(b.gameObject);
                // Clamp to 30: ForestWorld.Valid rejects any legacy island inventory above 30, so an unclamped refund would leave the save unable to load.
                if(!BridgeProfile.Current.isLighting)inventory.inventory = Mathf.Min(30, inventory.inventory + 3);
            }
            Notify(BridgeProfile.Current.isLighting ? "THE TREE TAKES THEM BACK\n" + returned + " loose stones returned. No particles are awarded." : "THE TREE TAKES THEM BACK\n" + returned + " stones returned · +" + (inventory.inventory) + " particles");
            Save();
            return true;
        }
        public bool FindPlacement(BridgeBlock block, out int gap, out int column, out int row, out string reason)
        {
            CellOf(block.transform.position, out gap, out column, out row);
            if (BridgeProfile.Current.isLighting) return FindLightingPlacement(block, ref gap, ref column, ref row, out reason);
            var cell = Cell(gap, column, row); var difference = cell - block.transform.position; difference.y = 0;
            reason = "Move the block to the glowing edge of the connected bridge, then release.";
            if (difference.magnitude > 1.65f) return false;
            int selectedGap = gap, selectedColumn = column, selectedRow = row;
            if (gap == 1 && !Bridges[0]) { reason = "Finish the first bridge before extending the second."; return false; }
            if (!Accepts(selectedGap, selectedColumn, selectedRow, block.tier))
            {
                int wanted = RequiredTier(selectedGap, selectedColumn, selectedRow);
                reason = "THIS TILE ASKS FOR " + BridgeBlock.TierName(wanted).ToUpper() + "\nEvery tile wants its own colour (" + BridgeBlock.TierColour(wanted) + " here); the preview shows which. Press R beside the Wisdom Tree to return the rest."; return false;
            }
            if (Blocks.Any(b => b.placed && b.gap == selectedGap && b.column == selectedColumn && b.row == selectedRow)) { reason = "That bridge tile is occupied. Move to an empty edge."; return false; }
            bool adjacent = row == 0 && column == 0 || Blocks.Any(b => b.Connected && b.gap == selectedGap && Math.Abs(b.column - selectedColumn) + Math.Abs(b.row - selectedRow) == 1);
            if (!adjacent) { reason = "PATH DISCONNECTED\nAttach this block to the end or side of the glowing connected path. Diagonals do not connect."; return false; }
            reason = "RELEASE TO EXTEND THE BRIDGE\nThis connected block will generate Souls."; return true;
        }

        bool FindLightingPlacement(BridgeBlock block, ref int gap, ref int column, ref int row, out string reason)
        {
            gap = 0; var cellCoord = new Vector2Int(column,row); var cell = LightingBridgeGrid.Cell(column,row);
            Vector3 delta = cell - block.transform.position; delta.y = 0;
            reason = "Move the block to a glowing edge attached to the connected network.";
            if (delta.magnitude > 1.65f) return false;
            if (!CanStand(player.transform.position)) { reason = "STAND ON A LIT PLATFORM OR CONNECTED TILE\nBuild outward from supported ground."; return false; }
            Vector3 playerDelta = cell - player.transform.position; playerDelta.y=0;
            if (playerDelta.magnitude > 4f) { reason = "TOO FAR TO REACH\nBuild from a nearby supported edge."; return false; }
            if (!LightingBridgeGrid.InBounds(cellCoord)) { reason = "VOID EDGE REACHED\nThe bridge is at the supported building limit."; return false; }
            if (LightingBridgeGrid.OverlapsPlatform(cellCoord)) { reason = "PLATFORM ALREADY OCCUPIES THIS TILE\nChoose an empty edge beyond the hub."; return false; }
            if (LightingBridgeGrid.HasObstruction(cellCoord, this, block)) { reason = "TILE BLOCKED\nClear the space before placing a bridge tile."; return false; }
            if (LightingBridgeGrid.IsOccupied(this, cellCoord)) { reason = "TILE OCCUPIED\nMove to an empty edge of the connected network."; return false; }
            if (!LightingBridgeGrid.IsFrontier(this, cellCoord)) { reason = "PATH DISCONNECTED\nAttach orthogonally to the first hub or a connected tile. Diagonals do not connect."; return false; }
            if (!Accepts(0,column,row,block.tier))
            {
                int wanted = RequiredTier(0,column,row);
                reason = "THIS TILE ASKS FOR " + BridgeBlock.TierName(wanted).ToUpper() + "\nThe preview shows this cell's required colour."; return false;
            }
            reason = "RELEASE TO EXTEND THE LIGHT\nThis tile joins the connected bridge network."; return true;
        }

        bool HasLightingPreviewTarget()
        {
            if (!CanStand(player.transform.position)) return false;
            return LightingBridgeGrid.TryGetFrontier(this, player.transform.position, player.visual != null ? player.visual.forward : Vector3.forward, out _);
        }

        public bool FindOrbitPlacement(BridgeBlock block, out Vector3 target)
        {
            target=default; if(block==null || block.placed)return false;
            var saved=block.transform.position; float best=float.MaxValue; bool found=false;
            Vector3 forward=player.visual!=null?player.visual.forward:Vector3.forward;
            var aim=player.transform.position+forward*1.6f;
            if (BridgeProfile.Current.isLighting)
            {
                if(!TryGetLightingPreviewTarget(out target))return false;
                var candidate=LightingBridgeGrid.CellOf(target);
                if(!Accepts(0,candidate.x,candidate.y,block.tier))return false;
                return true;
            }
            for(int gap=0;gap<2;gap++) for(int row=0;row<Rows;row++) for(int col=-2;col<=2;col++)
            {
                Vector3 cell=Cell(gap,col,row); var delta=cell-player.transform.position;delta.y=0;
                if(delta.magnitude>3.2f)continue;
                block.transform.position=cell;
                if(!FindPlacement(block,out _,out _,out _,out _))continue;
                float score=(cell-aim).sqrMagnitude;
                if(score<best){best=score;target=cell;found=true;}
            }
            block.transform.position=saved;return found;
        }
        public bool TryGetLightingPreviewTarget(out Vector3 target)
        {
            target=default;
            if(!BridgeProfile.Current.isLighting||!CanStand(player.transform.position))return false;
            Vector3 forward=player.visual!=null?player.visual.forward:Vector3.forward;
            if(!LightingBridgeGrid.TryGetFrontier(this,player.transform.position,forward,out var candidate))return false;
            target=LightingBridgeGrid.Cell(candidate.x,candidate.y);return true;
        }
        public static float StepLightingPreviewFade(float current,bool candidateVisible,float distance,float elapsed)
        {
            float target=candidateVisible&&distance<4f?Mathf.Clamp01((4f-distance)/1.5f):0f;
            return Mathf.MoveTowards(current,target,Mathf.Max(0,elapsed)/.2f);
        }

        public bool TryPlace(BridgeBlock block)
        {
            if (block.placed || block.Box.Holder != null) return false;
            if (!FindPlacement(block, out int gap, out int x, out int z, out string reason))
            {
                // Loose blocks remain movable on the island; only explain placement when near a rift.
                float depth = block.transform.position.z;
                if (BridgeProfile.Current.isLighting || (depth > Landscape.BridgeStart(0) - 3 && depth < Landscape.BridgeStart(0) + Rows * 2 + 1) || (depth > Landscape.BridgeStart(1) - 3 && depth < Landscape.BridgeStart(1) + Rows * 2 + 1)) Notify(reason, false);
                return false;
            }
            double before = Rate;
            ForestSoundscape.Play("crystal",block.transform.position,.12f);
            block.placed = true; block.gap = gap; block.column = x; block.row = z;
            ConfigureBlock(block);
            if (BridgeProfile.Current.isLighting)
            {
                Tiles.Adopt(new Vector2Int(x, z), block); Recalculate(block);
                Notify(!Bridges[0] ? "BRIDGE EXTENDED\nKeep building from any connected edge." : !Bridges[1] ? "SECOND HUB REACHED\nContinue building from any connected edge." : "THIRD HUB REACHED\nYour network remains open for building.");
                Save(); return true;
            }
            if (!block.rewarded) { souls += ConnectedCount == 0 ? 25 : 5; block.rewarded = true; }
            bool first = Bridges[0], second = Bridges[1]; Recalculate();
            Notify(!second && Bridges[1] ? "THE SECOND PATH IS WHOLE\nCross to the distant beacon. Press E beside it to enter the next area." : !first && Bridges[0] ? (BridgeProfile.Current.mixedBridge?"A NEW SHORE\nMatch each tile color to extend the next bridge.":"A NEW SHORE\nThe next rift needs Grave Stones and Strength I.\nYour old bridge keeps producing Souls.") : "PATH EXTENDED · " + ConnectedCount + " BLOCKS\n+" + Format(Rate - before) + " Souls/sec · Total " + Format(Rate) + "/sec");
            Save(); return true;
        }
        public bool TryReclaimTerminalTile(BridgeBlock block)
        {
            if(!BridgeProfile.Current.isLighting||GameHUD.GameplayBlocked||block==null||!block.placed||!CanStand(player.transform.position))return false;
            Vector3 center=LightingBridgeGrid.Cell(block.column,block.row);Vector3 occupied=player.transform.position-center;occupied.y=0;
            if(Mathf.Abs(occupied.x)<1.05f&&Mathf.Abs(occupied.z)<1.05f)return Deny("STEP OFF THE TILE FIRST\nOnly an empty terminal tile can be reclaimed.");
            if(occupied.magnitude>player.grabReach+1.5f)return false;
            foreach(var hit in Physics.OverlapBox(center+Vector3.up*.12f,new Vector3(.98f,.16f,.98f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore))
                if(hit!=null&&hit.GetComponentInParent<BridgeBlock>()!=block)return Deny("TILE IS OCCUPIED\nClear the terminal tile before reclaiming it.");
            // ponytail: one O(tiles) flood per reclaim, which is rare; it proves the rest stays connected.
            var oldCell=new Vector2Int(block.column,block.row);var remaining=new HashSet<Vector2Int>(Tiles.Cells);remaining.Remove(oldCell);
            int degree=LightingBridgeGrid.NeighborsOf(oldCell).Count(remaining.Contains);
            if(degree>1)return Deny("TILE SUPPORTS A BRANCH\nOnly a terminal tile can be reclaimed.");
            var connected=LightingBridgeGrid.ConnectedCells(remaining);
            if(connected.Count!=remaining.Count)return Deny("TILE SUPPORTS THE NETWORK\nReclaiming it would disconnect another bridge.");
            lightingTierOverrides.Remove(oldCell);Tiles.Detach(oldCell);block.placed=false;block.id="reclaim-"+Guid.NewGuid().ToString("N");
            Vector3 drop=player.transform.position+(player.visual!=null?player.visual.forward:Vector3.forward)*.95f;drop.y=.26f;
            block.gap=0;block.transform.position=drop;ConfigureBlock(block);Physics.SyncTransforms();Recalculate(block);Save();
            return true;
        }
        // Dash lays a free tile under the next step into the void, under the same grid rules as a
        // placed block. A diagonal step lays an orthogonal corner first, since diagonals do not connect.
        public bool TryBuildDashTile(Vector3 point)
        {
            var cell = LightingBridgeGrid.CellOf(point);
            if (BuildDashTile(cell)) return true;
            var here = LightingBridgeGrid.CellOf(player.transform.position);
            if (cell.x == here.x || cell.y == here.y) return false;
            return (BuildDashTile(new Vector2Int(cell.x, here.y)) || BuildDashTile(new Vector2Int(here.x, cell.y))) && (CanStand(point) || BuildDashTile(cell));
        }
        bool BuildDashTile(Vector2Int cell)
        {
            if (!BridgeProfile.Current.isLighting || !LightingBridgeGrid.InBounds(cell) || LightingBridgeGrid.OverlapsPlatform(cell)
                || LightingBridgeGrid.IsOccupied(this, cell) || !LightingBridgeGrid.IsFrontier(this, cell) || LightingBridgeGrid.HasObstruction(cell, this)) return false;
            var block = Tiles.Add(cell); Physics.SyncTransforms(); Recalculate(block);
            // A burst lays a tile every .05 s; one crystal per tile stacked into a loud clip that took
            // every pooled voice. One every .12 s reads as a run of ticks instead.
            if (Time.unscaledTime >= nextDashChime) { nextDashChime = Time.unscaledTime + .12f; ForestSoundscape.Play("crystal", LightingBridgeGrid.Cell(cell.x, cell.y), .12f); }
            dashSavePending = true; // saved once when the burst ends (Update), not once per tile
            return true;
        }
        // Light Grenade: relights every tile already in the blast, then lays every free cell within radius
        // tiles of center that the network reaches through the blast itself: a flood fill from the
        // connected edge, so every new tile is connected and a save taken mid-blast stays valid. The throw
        // never goes farther than the radius, so the blast holds the thrower's side of the network.
        // One step per frame, each yielding the tiles laid so far; the caller saves when it ends.
        // ponytail: 6 new tiles (one Recalculate) and 8 relit ones a frame keep a 149-cell blast under a
        // 60 FPS frame beside a 2000-tile network. All in one frame took 35-52 ms, and relighting ~80 old
        // tiles at once (each builds its step light on first use) cost ~17 ms by itself.
        public const int BlastTilesPerFrame = 6, BlastRelightsPerFrame = 8;
        public IEnumerator<int> GrenadeBlast(Vector2Int center, int radius)
        {
            if (!BridgeProfile.Current.isLighting || radius < 0) yield break;
            bool InBlast(Vector2Int c) => (c - center).sqrMagnitude <= radius * radius;
            void Relight(BridgeBlock tile) { if (tile == null) return; tile.lit = 1; WorldDressing.StepGlow(tile, 1); } // BridgeSteps fades it like a step
            var queue = new Queue<Vector2Int>(); var seen = new HashSet<Vector2Int>(); var batch = new HashSet<Vector2Int>(); var old = new List<BridgeBlock>();
            for (int x = center.x - radius; x <= center.x + radius; x++) for (int z = center.y - radius; z <= center.y + radius; z++)
            {
                var c = new Vector2Int(x, z); if (!InBlast(c)) continue;
                var tile = LightingBridgeGrid.TileAt(this, LightingBridgeGrid.Cell(x, z));
                if (tile != null) old.Add(tile);
                else if (LightingBridgeGrid.IsFrontier(this, c) && seen.Add(c)) queue.Enqueue(c);
            }
            old.Sort((a, b) => (new Vector2Int(a.column, a.row) - center).sqrMagnitude.CompareTo((new Vector2Int(b.column, b.row) - center).sqrMagnitude)); // relit from the impact out
            int laid = 0, relit = 0;
            while (queue.Count > 0 || relit < old.Count)
            {
                for (int end = Mathf.Min(old.Count, relit + BlastRelightsPerFrame); relit < end; relit++) Relight(old[relit]);
                batch.Clear();
                while (queue.Count > 0 && batch.Count < BlastTilesPerFrame)
                {
                    var c = queue.Dequeue();
                    // Tested when laid, not when queued: a load or a reclaim between frames can change the network.
                    bool linked = LightingBridgeGrid.IsFrontier(this, c) || LightingBridgeGrid.NeighborsOf(c).Any(batch.Contains);
                    if (!linked || LightingBridgeGrid.IsOccupied(this, c) || LightingBridgeGrid.OverlapsPlatform(c) || !LightingBridgeGrid.InBounds(c) || LightingBridgeGrid.HasObstruction(c, this)) continue;
                    Tiles.Add(c); batch.Add(c); laid++;
                    foreach (var n in LightingBridgeGrid.NeighborsOf(c)) if (InBlast(n) && seen.Add(n)) queue.Enqueue(n);
                }
                if (batch.Count > 0)
                {
                    Physics.SyncTransforms(); Recalculate();
                    foreach (var c in batch) Relight(LightingBridgeGrid.TileAt(this, LightingBridgeGrid.Cell(c.x, c.y)));
                }
                else if (relit >= old.Count) yield break; // nothing left to lay
                yield return laid;
            }
        }
        // sync: false lets a caller configuring many blocks run Physics.SyncTransforms once at the end.
        public void ConfigureBlock(BridgeBlock block, bool sync = true)
        {
            if (block.placed)
            {
                block.transform.localScale = new Vector3(2, .36f, 2); block.transform.position = Cell(block.gap, block.column, block.row);
            }
            else { block.transform.localScale = new Vector3(.84f, .5f, .84f); var ground=block.transform.position; ground.y=.26f; block.transform.position=ground; }
            block.transform.rotation=Quaternion.identity;
            var art=block.GetComponent<BlockArt>(); if(art!=null)art.Refresh();
            block.Box.Body.position = block.transform.position; if (sync) Physics.SyncTransforms();
        }
        // changed: the one tile a placement, dash or reclaim just touched; it is always repainted.
        // Lighting repaints any other tile only when its look (placed, connected, cell) differs from
        // its last paint, so a 12-tile dash or a reload does not repaint a 2000-tile network.
        public void Recalculate(BridgeBlock changed = null)
        {
            lightingGridRevision++; LightingBridgeGrid.Invalidate(this, false);
            ConnectedCount = 0; PathRate = 0; Bridges[0] = Bridges[1] = false;
            if (BridgeProfile.Current.isLighting)
            {
                Bridges[0] = LightingBridgeGrid.ReachesHub(this, "island-2");
                Bridges[1] = LightingBridgeGrid.ReachesHub(this, "island-3");
                // Blocks holds only the loaded tiles and the loose stones; every stored tile is connected.
                foreach (var b in Blocks)
                {
                    bool now = b.placed && Tiles.Contains(new Vector2Int(b.column,b.row));
                    b.Connected = now;
                    if (b != changed && b.painted == (b.placed, now, b.gap, b.row)) continue;
                    Paint(b);
                }
                ConnectedCount = Tiles.Count; PathRate = 0;
                if (nextAreaGlow != null) nextAreaGlow.SetActive(Bridges[1]);
                return;
            }
            foreach (var b in Blocks) b.Connected = false;
            for (int gap = 0; gap < 2; gap++)
            {
                if (gap == 1 && !Bridges[0]) break;
                var tiles = Blocks.Where(b => b.placed && b.gap == gap && Accepts(b.gap,b.column,b.row,b.tier)).ToArray();
                var root = tiles.FirstOrDefault(b => b.column == 0 && b.row == 0); if (root == null) continue;
                var queue = new Queue<Tuple<BridgeBlock, int>>(); queue.Enqueue(Tuple.Create(root, 0)); root.Connected = true;
                while (queue.Count > 0)
                {
                    var entry = queue.Dequeue(); var block = entry.Item1; int depth = entry.Item2;
                    ConnectedCount++; PathRate += block.SoulValue;
                    if (block.row == Rows - 1) Bridges[gap] = true;
                    foreach (var neighbor in tiles.Where(b => !b.Connected && Math.Abs(b.column - block.column) + Math.Abs(b.row - block.row) == 1))
                    { neighbor.Connected = true; queue.Enqueue(Tuple.Create(neighbor, depth + 1)); }
                }
            }
            // Count connected stones once and reward the length of the network.
            // Adding a shortcut must never reduce income by shortening BFS distances.
            // Default profile leaves linkBonusBlocks at int.MaxValue, so the cap never engages and
            // SampleScene stays byte-identical. The terrain profile caps it at 13, which reproduces
            // the shipped completed-route multiplier (2.95x at 14 connected) and makes income linear,
            // not quadratic, past that.
            PathRate *= 1 + Math.Min(BridgeProfile.Current.linkBonusBlocks, Math.Max(0, ConnectedCount - 1)) * .15;
            // Soul Gain (Lighting only): +25% of the already-bonused path income per level, applied
            // once on top of everything above. Platforms and disconnected blocks are never in
            // Blocks/Connected, so they already contribute 0 to PathRate before this line runs, and
            // there is no independent generator income added here (GeneratorRate stays 0 in Lighting
            // since "generator" is unreachable via Purchase).
            if (BridgeProfile.Current.isLighting) PathRate *= 1 + .25 * soulGainLevel;
            foreach (var b in Blocks)
            {
                var property = new MaterialPropertyBlock(); b.GetComponent<Renderer>().GetPropertyBlock(property);
                var colour = b.Connected ? WorldDressing.StepColor(b.gap, b.row) : Color.black;
                // A recalculation mid-fade must not relight a block the player has already left.
                if (BridgeProfile.Current.occupancyLight) colour *= b.lit;
                property.SetColor("_EmissionColor", colour);
                b.GetComponent<Renderer>().SetPropertyBlock(property);
                var art=b.GetComponent<BlockArt>(); if(art!=null)art.Refresh();
                WorldDressing.StepLight(b);
            }
            if (nextAreaGlow != null) nextAreaGlow.SetActive(Bridges[1]);
        }
        // Lighting: one block's look for its current state (lit cell colour when connected).
        internal void Paint(BridgeBlock b)
        {
            b.painted = (b.placed, b.Connected, b.gap, b.row);
            if (emissionProperties == null) emissionProperties = new MaterialPropertyBlock();
            var renderer = b.GetComponent<Renderer>(); renderer.GetPropertyBlock(emissionProperties);
            emissionProperties.SetColor("_EmissionColor", b.Connected ? WorldDressing.StepColor(0,b.row) : Color.black); renderer.SetPropertyBlock(emissionProperties);
            var art=b.GetComponent<BlockArt>(); if(art!=null)art.Refresh(); WorldDressing.StepLight(b);
        }
        public Vector3 NextCell()
        {
            if (BridgeProfile.Current.isLighting)
            {
                if (LightingBridgeGrid.TryGetFrontier(this, player.transform.position, player.visual != null ? player.visual.forward : Vector3.forward, out var next)) return LightingBridgeGrid.Cell(next.x,next.y);
                var here = LightingBridgeGrid.CellOf(player.transform.position);
                return LightingBridgeGrid.Cell(here.x,here.y);
            }
            int gap = CurrentGap;
            for (int z = 0; z < Rows; z++) if (!Blocks.Any(b => b.placed && b.gap == gap && b.column == 0 && b.row == z)) return Cell(gap, 0, z);
            return Cell(gap, 0, Rows - 1);
        }
        public bool CanStand(Vector3 point)
        {
            // Legacy scenes: only island ground and bridge tiles support the player. Lighting: nothing
            // outside visible crystal platforms or supported bridges counts as ground (see
            // PlatformSupport). Loose blocks never bridge the void either way.
            bool grounded = BridgeProfile.Current.platformGround ? PlatformSupport.Supports(point) : Landscape.Supports(point);
            // Lighting asks the stored cells, so support never waits on a chunk's GameObjects.
            return grounded || (BridgeProfile.Current.isLighting ? LightingBridgeGrid.Supports(this, point) : BlockUnder(point) != null);
        }
        // The connected bridge tile holding up this point, or null. Support and "what am I standing
        // on" are the same question, so they share one box test rather than two copies of it.
        public BridgeBlock BlockUnder(Vector3 point)
        {
            if (BridgeProfile.Current.isLighting) return LightingBridgeGrid.TileAt(this, point);
            return Blocks.FirstOrDefault(b => b.placed && b.Connected && Math.Abs(point.x - b.transform.position.x) <= .99f && Math.Abs(point.z - b.transform.position.z) <= 1.02f);
        }
        public bool CanDragAt(Vector3 point)
        {
            // A held block may project one tile beyond the supported edge so it can be placed from shore.
            if (CanStand(point)) return true;
            return CanStand(point + Vector3.forward * 1.6f) || CanStand(point - Vector3.forward * 1.6f) || CanStand(point + Vector3.right * 1.6f) || CanStand(point - Vector3.right * 1.6f);
        }
        public void RecoverPlayer()
        {
            player.Release(); player.CancelDash(); var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
            player.transform.position = safePosition; cc.enabled = true; cameraRig.CenterOnPlayer();
            Notify("THE LIGHT BROUGHT YOU BACK\nExtend the connected bridge before stepping into the rift.", false);
        }
        public void Interact()
        {
            if (GameHUD.GameplayBlocked) return;
            if (BridgeProfile.Current.isLighting && (GetComponent<LightingParticles>()?.TryDeposit(player) ?? false)) return;
            var wisdom = ForestWorld.For(this)?.Nearby(player.transform.position);
            if (wisdom != null) { if(BridgeProfile.Current.isLighting)Notify(LightingCarryHint,false);else wisdom.BeginCharge(); return; }
            if (completed) { Notify(BridgeProfile.Current.isLighting?"THE NEXT DAWN IS OPEN\nKeep extending the connected network. Upgrade Speed and Particle Yield at a Wisdom Tree.":"THE NEXT DAWN IS OPEN\nContinue growing your paths and powers.", false); return; }
            if (Bridges[1] && Vector3.Distance(player.transform.position, exit.position) < 3)
            { completed = true; Notify(BridgeProfile.Current.isLighting ? "THE NEXT DAWN\nAll three hubs are connected. Keep extending and reshaping your network." : "THE NEXT DAWN\nThe souls were never lost. They were waiting for a way home.\nChapter complete. Your bridges and Soul Well keep generating."); Save(); return; }
            Notify(BridgeProfile.Current.isLighting ? Objective + "\nHold left mouse to move a tile; release beside an open glowing edge." : Bridges[1] ? "NEXT AREA READY\nCross the second bridge and press E beside the distant beacon." : "BUILD THE SOUL PATH\n" + Objective + "\nHold left mouse to drag. Release at the glowing edge.", false);
        }
        public string Objective => BridgeProfile.Current.isLighting ? "Collect loose particles. Press E beside any Wisdom Tree for a free coloured bridge tile, then attach it to a lit hub edge or connected tile. Build in any orthogonal direction, around corners, and across the network to reach all three hubs. Upgrade Speed and Particle Yield at a Wisdom Tree."
            : !Bridges[0] ? (BridgeProfile.Current.mixedBridge
                ? "Collect particles; E at the Wisdom Tree makes blocks. Every tile asks for its own stone colour - the preview shows which; wrong stones go back to the tree with R. Connect the first shore: " + Blocks.Count(b => b.Connected && b.gap == 0) + " path blocks. Reach the far side with " + Rows + " forward tiles."
                : "Collect particles; E at the Wisdom Tree makes blocks. Connect the first shore: " + Blocks.Count(b => b.Connected && b.gap == 0) + " path blocks. Reach the far side with " + Rows + " forward tiles.")
            : !Bridges[1] ? (BridgeProfile.Current.mixedBridge
                ? "Cross the first bridge. The second rift asks for its own colour on every tile too; the tree on island 2 answers with all three."
                : "Cross the first bridge. Learn Strength I and use Grave Stones to bridge the second rift.") : !completed ? "Cross the second bridge. Reach the beacon and press E to enter the next area."
                : (BridgeProfile.Current.isLighting ? "The next dawn is open. Improve your paths and Particle Yield." : "The next dawn is open. Improve your paths, Soul Well and lighting soul.");
        public void Notify(string message, bool sound = true) { Notice = message; NoticeUntil = Time.unscaledTime + 8; if (sound && audioSource != null) audioSource.PlayOneShot(chime); }
        bool Deny(string message) { Notify(message, false); return false; }
        public static string Format(double n) => n < 10000 ? n.ToString("N1") : n < 1000000 ? (n / 1000).ToString("0.0") + "K" : (n / 1000000).ToString("0.0") + "M";

        [Serializable] public class BlockSave { public string id; public int tier, gap, column, row; public int cellTierOverride = -1; public bool placed, rewarded; public Vector3 position; }
        [Serializable] public class SaveData
        {
            public int version = BridgeProfile.Current.isLighting ? 6 : 4; public ForestSave forest; public double souls, rate; public long utc;
            // Explicit save identity: null for every legacy profile (ImportSave then skips the check
            // entirely, so existing saves are unaffected), "lighting-v1" for Lighting. Rejecting a
            // mismatched schema without touching the file is what keeps an incompatible save from
            // ever being overwritten - see ImportSave and Load.
            public string schema;
            public int speed, links, strength, radiance, generator, summoned, soulGainLevel, dashLevel, grenadeLevel, pullLevel, sightLevel, revealLevel; public bool completed; public Vector3 player;
            public List<BlockSave> blocks = new List<BlockSave>();
            // Lighting v6: placed tiles are cells, not blocks (blocks keeps the loose stones): x,z pairs,
            // and the cells whose colour is not LightingBridgeGrid.RequiredTier's as x,z,tier triples.
            // ponytail: rewritten whole on every save, about 10 B a tile (1 MB at 100k); past ~200k tiles
            // move to per-region files written only when dirty.
            public int[] tiles, tileTiers;
        }
        public string ExportSave()
        {
            var data = new SaveData { forest = ForestWorld.For(this)?.State, souls = souls, rate = Rate, utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), schema = BridgeProfile.Current.saveSchema, speed = speed, links = links, strength = strength, radiance = radiance, generator = generator, summoned = summoned, soulGainLevel = soulGainLevel, dashLevel = dashLevel, grenadeLevel = grenadeLevel, pullLevel = pullLevel, sightLevel = sightLevel, revealLevel = revealLevel, completed = completed, player = CanStand(player.transform.position) ? player.transform.position : safePosition };
            bool lighting = BridgeProfile.Current.isLighting;
            foreach (var b in Blocks)
            {
                if (lighting && b.placed) continue; // saved from Tiles below, loaded or not
                int overrideTier=BridgeProfile.Current.isLighting&&lightingTierOverrides.TryGetValue(new Vector2Int(b.column,b.row),out int savedTier)?savedTier:-1;
                data.blocks.Add(new BlockSave { id = b.id, tier = b.tier, cellTierOverride=overrideTier, placed = b.placed, rewarded = b.rewarded, gap = b.gap, column = b.column, row = b.row, position = player.SavedBlockPosition(b) });
            }
            if (lighting)
            {
                data.tiles = new int[Tiles.Count * 2]; int i = 0;
                foreach (var c in Tiles.Cells) { data.tiles[i++] = c.x; data.tiles[i++] = c.y; }
                var tiers = new List<int>();
                foreach (var o in lightingTierOverrides) if (Tiles.Contains(o.Key)) { tiers.Add(o.Key.x); tiers.Add(o.Key.y); tiers.Add(o.Value); }
                data.tileTiers = tiers.ToArray();
            }
            return JsonUtility.ToJson(data); // compact: pretty printing cost ~11 ms per save at 2000 tiles
        }
        public bool ImportSave(string json, bool offline = false, bool adopt = false)
        {
            SaveData d; try { d = JsonUtility.FromJson<SaveData>(json); } catch { return false; }
            bool lighting=BridgeProfile.Current.isLighting;
            bool lightingV4=lighting&&d!=null&&d.version==4&&d.schema=="lighting-v1";
            // Before v6 a Lighting save kept its placed tiles in blocks too; a v6 save holds loose stones only.
            int maximumBlocks=lighting&&d!=null&&d.version<6?int.MaxValue:150;
            bool supportedVersion=d!=null&&(d.version==1||d.version==2||d.version==3||d.version==4||lighting&&(d.version==5||d.version==6));
            if (d == null || !supportedVersion || d.blocks == null || d.blocks.Count > maximumBlocks || !Finite(d.souls) || d.souls < 0 || !Finite(d.rate) || d.rate < 0 || d.speed < 0 || d.speed > 6 || d.links < 0 || d.links > 3 || d.strength < 0 || d.strength > 3 || d.radiance < 0 || d.radiance > 5 || d.generator < 0 || d.generator > 8 || d.soulGainLevel < 0 || d.soulGainLevel > 8 || lighting && (d.dashLevel < 0 || d.dashLevel > BoxPlayer.DashTiles.Length || d.grenadeLevel < 0 || d.grenadeLevel > LightingGrenade.GrenadeTiles.Length || d.pullLevel < 0 || d.pullLevel > LightingGrenade.GrenadeTiles.Length || d.sightLevel < 0 || d.sightLevel > LightingFog.SightLevels || d.revealLevel < 0 || d.revealLevel > LightingReveal.Seconds.Length) || d.summoned < 0 || !(lighting?LightingPositionValid(d.player):PositionValid(d.player))) return false;
            // A save whose schema does not match the active profile's is a different world's save
            // (or a legacy one, under Lighting): reject it without writing anything. Every legacy
            // profile leaves saveSchema null, so this never engages for them.
            if (BridgeProfile.Current.saveSchema != null && d.schema != BridgeProfile.Current.saveSchema) return false;
            if(lighting&&!lightingV4&&d.version!=5&&d.version!=6)return false;
            if(lighting&&d.version>=5&&d.schema!="lighting-v1")return false;
            if(d.blocks.Any(b=>b==null||string.IsNullOrEmpty(b.id)||b.tier<0||b.tier>2||!lightingV4&&(b.cellTierOverride < -1||b.cellTierOverride>2)||!(lighting?LightingPositionValid(b.position):PositionValid(b.position))||b.placed&&(!lighting&&(b.gap<0||b.gap>1||Math.Abs(b.column)>2||b.row<0||b.row>=Rows)||lighting&&(!lightingV4&&(!LightingBridgeGrid.InBounds(new Vector2Int(b.column,b.row))||b.gap!=0)||lightingV4&&(b.gap<0||b.gap>1||Math.Abs(b.column)>2||b.row<0||b.row>=Rows))))||d.blocks.Select(b=>b.id).Distinct().Count()!=d.blocks.Count)return false;
            if(!lighting&&d.blocks.Where(b=>b.placed).Any(b=>!Accepts(b.gap,b.column,b.row,b.tier)))return false;
            if(lighting&&!lightingV4)
            {
                var placed=d.blocks.Where(b=>b.placed).ToArray();
                if(placed.Any(b=>LightingBridgeGrid.OverlapsPlatform(new Vector2Int(b.column,b.row))||(b.cellTierOverride>=0?b.tier!=b.cellTierOverride:b.tier!=LightingBridgeGrid.RequiredTier(b.column,b.row))))return false;
            }
            if(!lighting||!lightingV4) if(d.blocks.Where(b=>b.placed).GroupBy(b=>b.gap+":"+b.column+":"+b.row).Any(g=>g.Count()>1))return false;
            if(d.version<4){d.player=Landscape.UpgradePosition(d.player);foreach(var block in d.blocks)block.position=block.placed?Cell(block.gap,block.column,block.row):Landscape.UpgradePosition(block.position);}
            var platformDeliveries=new List<Tuple<string,int>>();
            if(lightingV4)
            {
                foreach(var b in d.blocks.Where(b=>b.placed).ToArray())
                {
                    var unified=new Vector2Int(b.column,Mathf.FloorToInt(Landscape.BridgeStart(b.gap)/2f)+b.row);
                    b.gap=0;b.column=unified.x;b.row=unified.y;b.position=LightingBridgeGrid.Cell(unified.x,unified.y);b.cellTierOverride=b.tier;
                    if(LightingBridgeGrid.OverlapsPlatform(unified))
                    {
                        var owner=OwningPlatform(unified);if(owner==null)return false;
                        platformDeliveries.Add(Tuple.Create(owner.hubId,b.tier));d.blocks.Remove(b);
                    }
                }
                if(d.blocks.Where(b=>b.placed).GroupBy(b=>b.column+":"+b.row).Any(g=>g.Count()>1))return false;
            }
            if (adopt)
            {
                // A one-time import from the legacy save: the incoming data is in the SHIPPED layout
                // (Landscape.UpgradePosition above already brought pre-v4 saves up to it), so carry
                // the player and loose blocks onto whichever island moved. Placed blocks are re-seated
                // by ConfigureBlock from Cell(...) below and need no adjustment here.
                d.player = Landscape.ImportPosition(d.player);
                foreach (var block in d.blocks) if (!block.placed) block.position = Landscape.ImportPosition(block.position);
            }
            var forest = ForestWorld.For(this);
            ForestSave incoming = null;
            if (forest != null) {
                // The forest cannot survive the shift: every particle slot is validated against a
                // regenerated layout and an island 2/3 slot would now fall outside it. Adopt rebuilds
                // the forest from scratch in the current geometry instead of migrating positions.
                incoming = adopt ? forest.Adopt(d.forest) : forest.Migrate(d.forest,d.version,d.blocks,d.player);
                if (incoming == null) return false;
                int incomingParticleVersion=incoming.particleVersion;
                if(lightingV4&&incomingParticleVersion==3&&!MigrateLightingParticles(incoming,d.souls))return false;
                else if(lightingV4&&incomingParticleVersion>=4&&d.souls>0)
                {
                    if(!AddLegacySoulsToInventory(incoming,d.souls))return false;
                }
                // Migrate cleared the slots: Lighting particles are now the seeded dot field.
                if(lighting)incoming.particleVersion=5;
                foreach(var delivery in platformDeliveries)
                {
                    var owner=incoming.islands.FirstOrDefault(i=>i.id==delivery.Item1);
                    if(owner==null)return false;if(owner.freeDeliveryColors==null)owner.freeDeliveryColors=new List<int>();
                    if(owner.freeDeliveryColor<0)owner.freeDeliveryColor=delivery.Item2;else owner.freeDeliveryColors.Add(delivery.Item2);
                }
            }
            // Lighting keeps its placed tiles as cells (LightingTiles): a v6 save lists them, an older one
            // moves its placed blocks over here.
            var placedCells=new HashSet<Vector2Int>(); var tileTiers=new Dictionary<Vector2Int,int>();
            if(lighting&&d.version==6)
            {
                if(d.blocks.Any(b=>b.placed))return false;
                var raw=d.tiles??Array.Empty<int>(); if(raw.Length%2!=0)return false;
                for(int i=0;i<raw.Length;i+=2){var c=new Vector2Int(raw[i],raw[i+1]);if(!placedCells.Add(c)||!LightingBridgeGrid.InBounds(c)||LightingBridgeGrid.OverlapsPlatform(c))return false;}
                var tiers=d.tileTiers??Array.Empty<int>(); if(tiers.Length%3!=0)return false;
                for(int i=0;i<tiers.Length;i+=3){var c=new Vector2Int(tiers[i],tiers[i+1]);if(tiers[i+2]<0||tiers[i+2]>2||!placedCells.Contains(c)||!tileTiers.TryAdd(c,tiers[i+2]))return false;}
            }
            else if(lighting)
            {
                foreach(var b in d.blocks.Where(b=>b.placed)){var c=new Vector2Int(b.column,b.row);placedCells.Add(c);if(b.cellTierOverride>=0)tileTiers[c]=b.cellTierOverride;}
                d.blocks.RemoveAll(b=>b.placed);
            }
            if(lighting)
            {
                var connectedCells=LightingBridgeGrid.ConnectedCells(placedCells);
                if(connectedCells.Count!=placedCells.Count)return false;
                Vector3 requestedPlayer=new Vector3(d.player.x,.025f,d.player.z);
                if(LightingLayoutSupports(requestedPlayer,connectedCells))d.player=requestedPlayer;
                else
                {
                    // A valid save can point at a tile that was removed during the v4 platform
                    // overlap conversion. Choose the authored start only after checking it against
                    // the incoming layout, before any live blocks, wallet, or forest state changes.
                    var start=new Vector3(0,.025f,3);
                    if(!LightingLayoutSupports(start,connectedCells))return false;
                    d.player=start;
                }
            }
            player.Release(); player.ResetDash();
            lightingTierOverrides.Clear();
            if(lighting){foreach(var t in tileTiers)lightingTierOverrides[t.Key]=t.Value;Tiles.Replace(placedCells);}
            var keep = new HashSet<string>(d.blocks.Select(b => b.id));
            foreach (var b in Blocks.Where(b => !keep.Contains(b.id)).ToArray()) { Blocks.Remove(b); b.gameObject.SetActive(false); Destroy(b.gameObject); }
            // By id, not Blocks.FirstOrDefault per saved block: that was O(tiles squared) on a big network.
            var byId = new Dictionary<string, BridgeBlock>();
            foreach (var b in Blocks) if (b.id != null && !byId.ContainsKey(b.id)) byId[b.id] = b;
            foreach (var saved in d.blocks)
            {
                if (saved.id == null || !byId.TryGetValue(saved.id, out var b)) { b = Instantiate(blockPrefabs[saved.tier], blocksRoot); Blocks.Add(b); if (saved.id != null) byId[saved.id] = b; }
                b.id = saved.id; b.tier = saved.tier; b.placed = saved.placed; b.rewarded = saved.rewarded; b.gap = saved.gap; b.column = saved.column; b.row = saved.row; b.transform.position = saved.position; ConfigureBlock(b, false);
            }
            Physics.SyncTransforms();
            souls = d.souls; speed = d.speed; links = d.links; strength = d.strength; radiance = d.radiance; generator = d.generator; soulGainLevel = d.soulGainLevel; dashLevel = lighting ? d.dashLevel : 0; grenadeLevel = lighting ? d.grenadeLevel : 0; pullLevel = lighting ? d.pullLevel : 0; sightLevel = lighting ? d.sightLevel : 0; revealLevel = lighting ? d.revealLevel : 0; summoned = d.summoned; completed = d.completed;
            if(lighting&&lightingV4)souls=0;
            ApplyUpgrades(); Recalculate(); if(!lighting)completed &= Bridges[1];
            var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.transform.position = CanStand(d.player) ? d.player : new Vector3(0, .025f, 3); cc.enabled = true; safePosition = player.transform.position;
            if (lighting) Tiles.Stream(player.transform.position, true);
            if (offline&&!lighting) { double seconds = Math.Min(3600, Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - d.utc)); double gain = Math.Min(Rate, d.rate) * seconds * .25; souls += gain; if (gain >= 1) Notify("YOUR SOULS KEPT FLOWING\n+" + Format(gain) + " Souls while away.", false); }
            if (forest != null) forest.Restore(incoming);
            cameraRig.CenterOnPlayer(); saveBlockedByRejectedData=false; return true;
        }
        bool MigrateLightingParticles(ForestSave state,double legacySouls)
        {
            if(state==null||state.particleVersion!=3||state.islands==null)return false;
            if(state.inventory<0||state.inventory>int.MaxValue/ForestWorld.ParticleUnitsPerParticle)return false;
            long units=(long)state.inventory*ForestWorld.ParticleUnitsPerParticle;
            foreach(var island in state.islands)
            {
                if(island.charge==null||island.charge.Length!=3||island.rewards==null)return false;
                int[] fullBacked=new int[3];
                foreach(var reward in island.rewards)if(reward!=null&&reward.consumesCharge&&reward.color>=0&&reward.color<3)fullBacked[reward.color]++;
                for(int color=0;color<3;color++)
                {
                    if(island.charge[color]<0||island.charge[color]>3||fullBacked[color]>1)return false;
                    units+=(long)island.charge[color]*ForestWorld.ParticleUnitsPerParticle;
                }
                foreach(var reward in island.rewards)
                {
                    if(reward==null||reward.color< -1||reward.color>2)return false;
                    // A full backed charge and its queued reward represent the same three deposits.
                    if(!reward.consumesCharge)units+=3L*ForestWorld.ParticleUnitsPerParticle;
                }
                Array.Clear(island.charge,0,island.charge.Length);
                island.queue?.Clear(); island.rewards.Clear();
                if(island.freeDeliveryColor < -1 || island.freeDeliveryColor > 2)return false;
            }
            double extra=Math.Ceiling(Math.Max(0,legacySouls)/10d)*ForestWorld.ParticleUnitsPerParticle;
            if(double.IsNaN(extra)||double.IsInfinity(extra)||extra>int.MaxValue-units)return false;units+=(long)extra;
            state.inventory=(int)units; state.particleVersion=4; return true;
        }
        bool AddLegacySoulsToInventory(ForestSave state,double legacySouls)
        {
            double extra=Math.Ceiling(Math.Max(0,legacySouls)/10d)*ForestWorld.ParticleUnitsPerParticle;
            if(double.IsNaN(extra)||double.IsInfinity(extra)||extra>int.MaxValue-state.inventory)return false;
            state.inventory+=(int)extra;return true;
        }
        PlatformSupport OwningPlatform(Vector2Int cell)
        {
            var center=LightingBridgeGrid.Cell(cell.x,cell.y);
            PlatformSupport nearest=null;float best=float.MaxValue;
            foreach(var p in PlatformSupport.ActivePlatforms)
            {
                if(p==null||p.Surface==null)continue;var b=p.Surface.bounds;
                float dx=Mathf.Max(b.min.x-center.x,0,center.x-b.max.x),dz=Mathf.Max(b.min.z-center.z,0,center.z-b.max.z);
                float d=dx*dx+dz*dz;if(d<best){best=d;nearest=p;}
            }
            return nearest;
        }
        static bool LightingPositionValid(Vector3 p)
        {
            if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z)||p.y< -5||p.y>10)return false;
            if(!PlatformSupport.TryGetWorldBounds(out Bounds b))return Math.Abs(p.x)<=2*LightingBridgeGrid.HubMargin&&Math.Abs(p.z)<=2*LightingBridgeGrid.HubMargin;
            return p.x>=b.min.x-LightingBridgeGrid.HubMargin&&p.x<=b.max.x+LightingBridgeGrid.HubMargin&&p.z>=b.min.z-LightingBridgeGrid.HubMargin&&p.z<=b.max.z+LightingBridgeGrid.HubMargin;
        }
        static bool LightingLayoutSupports(Vector3 point,HashSet<Vector2Int> connected)
        {
            if(PlatformSupport.Supports(point))return true;
            var center=LightingBridgeGrid.CellOf(point);
            for(int x=center.x-1;x<=center.x+1;x++)for(int z=center.y-1;z<=center.y+1;z++)
            {
                if(!connected.Contains(new Vector2Int(x,z)))continue;
                var cell=LightingBridgeGrid.Cell(x,z);if(Mathf.Abs(point.x-cell.x)<=.99f&&Mathf.Abs(point.z-cell.z)<=1.02f)return true;
            }
            return false;
        }
        static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        // Bounds a saved position must fall inside. They follow the courts, which grew, so a stone
        // dropped at the far edge is still restored instead of failing the whole save.
        static bool PositionValid(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) && Math.Abs(p.x) <= 60 && p.y >= -5 && p.y <= 10 && p.z >= -70 && p.z <= 120 + Landscape.Expansion;
        public bool Save()
        {
            if (!initialized || !persistenceEnabled || saveBlockedByRejectedData) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                File.WriteAllText(SavePath + ".tmp", ExportSave());
                if (!File.Exists(SavePath)) { File.Move(SavePath + ".tmp", SavePath); return true; }
                try { File.Replace(SavePath + ".tmp", SavePath, SavePath + ".bak"); }
                catch (Exception swap)
                {
                    // File.Replace is unsupported on some volumes. Keep the backup by hand instead of losing the save.
                    Debug.LogWarning(swap.Message);
                    File.Copy(SavePath, SavePath + ".bak", true);
                    File.Delete(SavePath); File.Move(SavePath + ".tmp", SavePath);
                }
                return true;
            }
            catch (Exception e) { Debug.LogWarning(e.Message); Notify("SAVE FAILED\nCheck the save folder and available disk space.", false); return false; }
        }
        // False only when a save file exists at SavePath/.bak but every candidate failed to import
        // (wrong schema, corrupted, etc.) - Start() reads this to skip its post-Build Save() call, so
        // a rejected save is never overwritten by the fresh boot state. True in every other case
        // (loaded fine, or there was nothing to load), matching every other current caller's
        // assumption that Load() always leaves it safe to save.
        public bool Load()
        {
            bool sawOwnSave = false;
            foreach (var path in new[] { SavePath, SavePath + ".bak" })
            {
                if (!File.Exists(path)) continue;
                sawOwnSave = true;
                try { if (ImportSave(File.ReadAllText(path), true)) return true; } catch (Exception e) { Debug.LogWarning(e.Message); }
            }
            if (sawOwnSave) { saveBlockedByRejectedData=true; return false; }
            // Nothing of ours loaded. A profile that names a legacy save offers a one-time, read-only
            // import from it, so a returning player's stones and Souls are not simply lost when a new
            // scene starts with its own empty save file. Never written to.
            if (!string.IsNullOrEmpty(BridgeProfile.Current.seedFromSaveFile))
            {
                string legacyPath = Path.Combine(Application.persistentDataPath, BridgeProfile.Current.seedFromSaveFile);
                try
                {
                    if (File.Exists(legacyPath) && ImportSave(File.ReadAllText(legacyPath), offline: true, adopt: true))
                    {
                        Save(); // persists the import immediately so it only ever happens once
                        Notify("THE RIFTS HAVE WIDENED\nYour stones and Souls carried over. Each crossing now needs " + Rows + " tiles.", false);
                    }
                }
                catch (Exception e) { Debug.LogWarning(e.Message); }
            }
            return true;
        }
        public void NewJourney()
        {
            if (persistenceEnabled && File.Exists(SavePath)) { File.Copy(SavePath, SavePath + ".previous", true); File.Delete(SavePath); if (File.Exists(SavePath + ".bak")) File.Delete(SavePath + ".bak"); }
            persistenceEnabled = false; UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
        void OnApplicationFocus(bool focused) { if (!focused) Save(); }
        void OnApplicationPause(bool paused) { if (paused) Save(); }
        void OnApplicationQuit() { Save(); }
        void OnDestroy() { if (Instance == this) Instance = null; if (chime != null) Destroy(chime); if (ghost != null) Destroy(ghost.gameObject); }
    }
}
