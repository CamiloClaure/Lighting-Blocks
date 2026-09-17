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
        public Renderer wellCore;
        public GameObject nextAreaGlow;
        public bool persistenceEnabled = true, autoTick = true;
        public string savePathOverride;
        public double souls;
        public int speed, links, strength, radiance, generator, summoned;
        public bool completed;
        public readonly List<BridgeBlock> Blocks = new List<BridgeBlock>();
        public readonly bool[] Bridges = new bool[2];
        public int ConnectedCount { get; private set; }
        public double PathRate { get; private set; }
        public double GeneratorRate => 3 * (Math.Pow(1.6, generator) - 1) / .6;
        public double Rate => PathRate + GeneratorRate;
        public int Capacity => 1 + links;
        public string Notice { get; private set; }
        public float NoticeUntil { get; private set; }
        public string SavePath => savePathOverride ?? Path.Combine(Application.persistentDataPath, "soul-bridge-v1.json");
        public const int Rows = 7;
        float saveTimer;
        bool initialized;
        Transform ghost;
        Vector3 safePosition = new Vector3(0, .025f, 3);
        AudioSource audioSource;
        AudioClip chime;
        public int CurrentGap => Bridges[0] ? 1 : 0;
        public static Vector3 Cell(int gap, int column, int row) => new Vector3(column * 2, -.18f, Landscape.BridgeStart(gap) + row * 2);

        void Awake()
        {
            Instance = this;
            if(ForestPerformanceProbe.Requested||TwilightEvidence.Requested)persistenceEnabled=false;
#if UNITY_EDITOR
            if (File.Exists(Path.Combine(Application.dataPath, "../Library/LightingBlocks.test-active"))) persistenceEnabled = false;
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
            foreach(var b in Blocks) ConfigureBlock(b);
            ApplyUpgrades(); Recalculate(); initialized = true;
            var forest = ForestWorld.For(this); if (forest != null) forest.BeginSession();
            if (persistenceEnabled) Load();
            // The preview is the authored placed crystal itself, one clone per tier, so each tier previews as it will land.
            ghost = new GameObject("Next bridge block preview").transform;
            var previewProperties = new MaterialPropertyBlock();
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
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.GetPropertyBlock(previewProperties); previewProperties.SetFloat("_Highlight", 1); renderer.SetPropertyBlock(previewProperties);
                }
                visual.SetActive(tier == 0);
            }
            if (string.IsNullOrEmpty(Notice)) Notify("THE WISDOM TREE\nCollect the bright particles. Press E beside the violet tree to charge it and release a bridge block.", false);
            cameraRig.CenterOnPlayer();
            WorldDressing.Build(this);
            if (forest != null) { forest.Build(); Save(); }
            GameAudio.PlayMusic("IntroGusta");
        }
        void Update()
        {
            if (!initialized || GameHUD.SimulationPaused) return;
            if (autoTick) Advance(Time.deltaTime);
            if (!GameHUD.GameplayBlocked && player.readInput && Application.isFocused && Keyboard.current != null)
            {
                var k = Keyboard.current;
                if (k.eKey.wasPressedThisFrame) Interact();
                if (k.digit1Key.wasPressedThisFrame) Purchase("speed");
                if (k.digit2Key.wasPressedThisFrame) Purchase("links");
                if (k.digit3Key.wasPressedThisFrame) Purchase("strength");
                if (k.digit4Key.wasPressedThisFrame) Purchase("radiance");
                if (k.digit5Key.wasPressedThisFrame) Purchase("generator");
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
                for (int i = 0; i < ghost.childCount; i++) ghost.GetChild(i).gameObject.SetActive(i == previewTier);
                if (player.Orbiting && FindOrbitPlacement(block,out var orbitCell)) ghost.position=orbitCell+Vector3.up*.205f;
                else if (block != null && FindPlacement(block, out int g, out int x, out int z, out _))
                    ghost.position = Cell(g, x, z) + Vector3.up * .205f;
                else ghost.position = NextCell() + Vector3.up * .205f;
                ghost.gameObject.SetActive(!Bridges[1]);
            }
        }
        public void Advance(float seconds)
        {
            if (GameHUD.SimulationPaused || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            souls += Rate * seconds; saveTimer += seconds;
            if (saveTimer >= 10) { saveTimer = 0; Save(); }
        }
        public double Cost(string id)
        {
            switch (id)
            {
                case "speed": return Math.Ceiling(35 * Math.Pow(1.6, speed));
                case "links": return Math.Ceiling(80 * Math.Pow(2.2, links));
                case "strength": return 90 * Math.Pow(2, strength);
                case "radiance": return Math.Ceiling(30 * Math.Pow(1.65, radiance));
                case "generator": return Math.Ceiling(60 * Math.Pow(1.65, generator));
                case "stone": return Math.Min(60, Math.Ceiling(12 * Math.Pow(1.12, summoned)));
                case "grave": return 45;
                default: return 0;
            }
        }
        public int Level(string id) => id == "speed" ? speed : id == "links" ? links : id == "strength" ? strength : id == "radiance" ? radiance : generator;
        public int Cap(string id) => id == "speed" ? 6 : id == "links" || id == "strength" ? 3 : id == "radiance" ? 5 : 8;
        public bool Purchase(string id)
        {
            if (GameHUD.SimulationPaused) return false;
            if (id == "stone" || id == "grave") return Deny("WISDOM TREES PROVIDE BLOCKS\nCollect particles and press E beside a Wisdom Tree.");
            if (!(new[] { "speed", "links", "strength", "radiance", "generator", "stone", "grave" }).Contains(id)) return false;
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
                case "radiance": radiance++; break; case "generator": generator++; break; default: summoned++; break;
            }
            ApplyUpgrades();
            Notify(supply ? "A NEW STONE ANSWERS\nIt waits on the island beside the stone source." : id == "generator" ? "SOUL WELL AWAKENED\nIndependent income: +" + Format(GeneratorRate) + " Souls/sec." : id == "links" ? "SOUL LINKS " + links + "\nClick to gather up to " + Capacity + " floating stones. Q drops or builds them." : id == "radiance" ? "LIGHTING SOUL " + radiance + "\nYour light now reaches " + soulLight.range.ToString("0") + " metres." : "POWER AWAKENED\n" + (id == "speed" ? "Walking and hauling are faster." : "Strength level " + strength + ". Grave Stones need I; Obsidian needs III."));
            Save(); return true;
        }
        public void ApplyUpgrades()
        {
            player.moveSpeed = 6f + speed * .65f; player.pullSpeed = 3.2f + speed * .65f; player.grabReach = 1.4f;
            if (soulLight != null) { soulLight.range = 9 + radiance * 3; soulLight.intensity = 3.5f + radiance * .5f; }
            if (wellCore != null) wellCore.gameObject.SetActive(generator > 0);
        }
        public bool Spawn(int tier, int island) => false; // Legacy paid supply is retired.
        public bool TrySpawnAtWisdomTree(string islandId, out BridgeBlock block)
        {
            block = null;
            var forest = ForestWorld.For(this);
            if (forest == null) return false;
            var definition = forest.configuration.Find(islandId);
            if (definition == null || !forest.FindOutput(definition, out var point)) return false;
            BridgeBlock created = null;
            try {
                created = Instantiate(blockPrefabs[definition.blockTier], point, Quaternion.identity, blocksRoot);
                created.id = "wisdom-" + Guid.NewGuid().ToString("N");
                ConfigureBlock(created); Blocks.Add(created); Physics.SyncTransforms(); block = created; return true;
            } catch (Exception e) {
                if (created != null) { Blocks.Remove(created); created.gameObject.SetActive(false); Destroy(created.gameObject); }
                Debug.LogException(e); return false;
            }
        }
        public bool FindPlacement(BridgeBlock block, out int gap, out int column, out int row, out string reason)
        {
            gap = block.transform.position.z > (Landscape.BridgeStart(0)+Landscape.BridgeStart(1))*.5f ? 1 : 0;
            row = Mathf.Clamp(Mathf.RoundToInt((block.transform.position.z - Landscape.BridgeStart(gap)) / 2), 0, Rows - 1);
            column = Mathf.Clamp(Mathf.RoundToInt(block.transform.position.x / 2), -2, 2);
            var cell = Cell(gap, column, row); var difference = cell - block.transform.position; difference.y = 0;
            reason = "Move the block to the glowing edge of the connected bridge, then release.";
            if (difference.magnitude > 1.65f) return false;
            int selectedGap = gap, selectedColumn = column, selectedRow = row;
            if (gap == 1 && !Bridges[0]) { reason = "Finish the first bridge before extending the second."; return false; }
            if (gap == 1 && block.tier == 0) { reason = "The second rift needs Grave Stone or Obsidian. Upgrade Strength in the skill tree [Tab]."; return false; }
            if (Blocks.Any(b => b.placed && b.gap == selectedGap && b.column == selectedColumn && b.row == selectedRow)) { reason = "That bridge tile is occupied. Move to an empty edge."; return false; }
            bool adjacent = row == 0 && column == 0 || Blocks.Any(b => b.Connected && b.gap == selectedGap && Math.Abs(b.column - selectedColumn) + Math.Abs(b.row - selectedRow) == 1);
            if (!adjacent) { reason = "PATH DISCONNECTED\nAttach this block to the end or side of the glowing connected path. Diagonals do not connect."; return false; }
            reason = "RELEASE TO EXTEND THE BRIDGE\nThis connected block will generate Souls."; return true;
        }

        public bool FindOrbitPlacement(BridgeBlock block, out Vector3 target)
        {
            target=default; if(block==null || block.placed)return false;
            var saved=block.transform.position; float best=float.MaxValue; bool found=false;
            Vector3 forward=player.visual!=null?player.visual.forward:Vector3.forward;
            var aim=player.transform.position+forward*1.6f;
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

        public bool TryPlace(BridgeBlock block)
        {
            if (block.placed || block.Box.Holder != null) return false;
            if (!FindPlacement(block, out int gap, out int x, out int z, out string reason))
            {
                // Loose blocks remain movable on the island; only explain placement when near a rift.
                if (block.transform.position.z > 5 && block.transform.position.z < 22 || block.transform.position.z > 32 && block.transform.position.z < 49) Notify(reason, false);
                return false;
            }
            double before = Rate;
            ForestSoundscape.Play("crystal",block.transform.position,.12f);
            block.placed = true; block.gap = gap; block.column = x; block.row = z;
            ConfigureBlock(block);
            if (!block.rewarded) { souls += ConnectedCount == 0 ? 25 : 5; block.rewarded = true; }
            bool first = Bridges[0], second = Bridges[1]; Recalculate();
            Notify(!second && Bridges[1] ? "THE SECOND PATH IS WHOLE\nCross to the distant beacon. Press E beside it to enter the next area." : !first && Bridges[0] ? "A NEW SHORE\nThe next rift needs Grave Stones and Strength I.\nYour old bridge keeps producing Souls." : "PATH EXTENDED · " + ConnectedCount + " BLOCKS\n+" + Format(Rate - before) + " Souls/sec · Total " + Format(Rate) + "/sec");
            Save(); return true;
        }
        public void ConfigureBlock(BridgeBlock block)
        {
            if (block.placed)
            {
                block.transform.localScale = new Vector3(2, .36f, 2); block.transform.position = Cell(block.gap, block.column, block.row);
            }
            else { block.transform.localScale = new Vector3(.84f, .5f, .84f); var ground=block.transform.position; ground.y=.26f; block.transform.position=ground; }
            block.transform.rotation=Quaternion.identity;
            var art=block.GetComponent<BlockArt>(); if(art!=null)art.Refresh();
            block.Box.Body.position = block.transform.position; Physics.SyncTransforms();
        }
        public void Recalculate()
        {
            ConnectedCount = 0; PathRate = 0; Bridges[0] = Bridges[1] = false;
            foreach (var b in Blocks) b.Connected = false;
            for (int gap = 0; gap < 2; gap++)
            {
                if (gap == 1 && !Bridges[0]) break;
                var tiles = Blocks.Where(b => b.placed && b.gap == gap && (gap == 0 || b.tier > 0)).ToArray();
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
            PathRate *= 1 + Math.Max(0, ConnectedCount - 1) * .15;
            foreach (var b in Blocks)
            {
                var property = new MaterialPropertyBlock(); b.GetComponent<Renderer>().GetPropertyBlock(property);
                property.SetColor("_EmissionColor", b.Connected ? WorldDressing.StepColor(b.gap, b.row) : Color.black);
                b.GetComponent<Renderer>().SetPropertyBlock(property);
                var art=b.GetComponent<BlockArt>(); if(art!=null)art.Refresh();
                WorldDressing.StepLight(b);
            }
            if (nextAreaGlow != null) nextAreaGlow.SetActive(Bridges[1]);
        }
        public Vector3 NextCell()
        {
            int gap = CurrentGap;
            for (int z = 0; z < Rows; z++) if (!Blocks.Any(b => b.placed && b.gap == gap && b.column == 0 && b.row == z)) return Cell(gap, 0, z);
            return Cell(gap, 0, Rows - 1);
        }
        public bool CanStand(Vector3 point)
        {
            // Only island ground and bridge tiles support the player. Loose blocks never bridge the void.
            return Landscape.Supports(point) || BlockUnder(point) != null;
        }
        // The connected bridge tile holding up this point, or null. Support and "what am I standing
        // on" are the same question, so they share one box test rather than two copies of it.
        public BridgeBlock BlockUnder(Vector3 point)
        {
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
            player.Release(); var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
            player.transform.position = safePosition; cc.enabled = true; cameraRig.CenterOnPlayer();
            Notify("THE LIGHT BROUGHT YOU BACK\nExtend the connected bridge before stepping into the rift.", false);
        }
        public void Interact()
        {
            if (GameHUD.GameplayBlocked) return;
            var wisdom = ForestWorld.For(this)?.Nearby(player.transform.position);
            if (wisdom != null) { wisdom.BeginCharge(); return; }
            if (completed) { Notify("THE NEXT DAWN IS OPEN\nContinue growing your paths and powers.", false); return; }
            if (Bridges[1] && Vector3.Distance(player.transform.position, exit.position) < 3)
            { completed = true; Notify("THE NEXT DAWN\nThe souls were never lost. They were waiting for a way home.\nChapter complete. Your bridges and Soul Well keep generating."); Save(); return; }
            Notify(Bridges[1] ? "NEXT AREA READY\nCross the second bridge and press E beside the distant beacon." : "BUILD THE SOUL PATH\n" + Objective + "\nHold left mouse to drag. Release at the glowing edge.", false);
        }
        public string Objective => !Bridges[0] ? "Collect particles; E at the Wisdom Tree makes blocks. Connect the first shore: " + Blocks.Count(b => b.Connected && b.gap == 0) + " path blocks. Reach the far side with 7 forward tiles."
            : !Bridges[1] ? "Cross the first bridge. Learn Strength I and use Grave Stones to bridge the second rift." : !completed ? "Cross the second bridge. Reach the beacon and press E to enter the next area." : "The next dawn is open. Improve your paths, Soul Well and lighting soul.";
        public void Notify(string message, bool sound = true) { Notice = message; NoticeUntil = Time.unscaledTime + 8; if (sound && audioSource != null) audioSource.PlayOneShot(chime); }
        bool Deny(string message) { Notify(message, false); return false; }
        public static string Format(double n) => n < 10000 ? n.ToString("N1") : n < 1000000 ? (n / 1000).ToString("0.0") + "K" : (n / 1000000).ToString("0.0") + "M";

        [Serializable] public class BlockSave { public string id; public int tier, gap, column, row; public bool placed, rewarded; public Vector3 position; }
        [Serializable] public class SaveData
        {
            public int version = 4; public ForestSave forest; public double souls, rate; public long utc;
            public int speed, links, strength, radiance, generator, summoned; public bool completed; public Vector3 player;
            public List<BlockSave> blocks = new List<BlockSave>();
        }
        public string ExportSave()
        {
            var data = new SaveData { forest = ForestWorld.For(this)?.State, souls = souls, rate = Rate, utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), speed = speed, links = links, strength = strength, radiance = radiance, generator = generator, summoned = summoned, completed = completed, player = CanStand(player.transform.position) ? player.transform.position : safePosition };
            foreach (var b in Blocks) data.blocks.Add(new BlockSave { id = b.id, tier = b.tier, placed = b.placed, rewarded = b.rewarded, gap = b.gap, column = b.column, row = b.row, position = player.SavedBlockPosition(b) });
            return JsonUtility.ToJson(data, true);
        }
        public bool ImportSave(string json, bool offline = false)
        {
            SaveData d; try { d = JsonUtility.FromJson<SaveData>(json); } catch { return false; }
            if (d == null || (d.version != 1 && d.version != 2 && d.version != 3 && d.version != 4) || d.blocks == null || d.blocks.Count > 150 || !Finite(d.souls) || d.souls < 0 || !Finite(d.rate) || d.rate < 0 || d.speed < 0 || d.speed > 6 || d.links < 0 || d.links > 3 || d.strength < 0 || d.strength > 3 || d.radiance < 0 || d.radiance > 5 || d.generator < 0 || d.generator > 8 || d.summoned < 0 || !PositionValid(d.player)) return false;
            if (d.blocks.Any(b => b == null || string.IsNullOrEmpty(b.id) || b.tier < 0 || b.tier > 2 || !PositionValid(b.position) || b.placed && (b.gap < 0 || b.gap > 1 || Math.Abs(b.column) > 2 || b.row < 0 || b.row >= Rows || b.gap == 1 && b.tier == 0)) || d.blocks.Select(b => b.id).Distinct().Count() != d.blocks.Count) return false;
            if (d.blocks.Where(b => b.placed).GroupBy(b => b.gap + ":" + b.column + ":" + b.row).Any(g => g.Count() > 1)) return false;
            if(d.version<4){d.player=Landscape.UpgradePosition(d.player);foreach(var block in d.blocks)block.position=block.placed?Cell(block.gap,block.column,block.row):Landscape.UpgradePosition(block.position);}
            var forest = ForestWorld.For(this);
            ForestSave incoming = null;
            if (forest != null) {
                incoming = forest.Migrate(d.forest,d.version,d.blocks,d.player);
                if (incoming == null) return false;
            }
            player.Release();
            var keep = new HashSet<string>(d.blocks.Select(b => b.id));
            foreach (var b in Blocks.Where(b => !keep.Contains(b.id)).ToArray()) { Blocks.Remove(b); b.gameObject.SetActive(false); Destroy(b.gameObject); }
            foreach (var saved in d.blocks)
            {
                var b = Blocks.FirstOrDefault(b => b.id == saved.id);
                if (b == null) { b = Instantiate(blockPrefabs[saved.tier], blocksRoot); Blocks.Add(b); }
                b.id = saved.id; b.tier = saved.tier; b.placed = saved.placed; b.rewarded = saved.rewarded; b.gap = saved.gap; b.column = saved.column; b.row = saved.row; b.transform.position = saved.position; ConfigureBlock(b);
            }
            souls = d.souls; speed = d.speed; links = d.links; strength = d.strength; radiance = d.radiance; generator = d.generator; summoned = d.summoned; completed = d.completed;
            ApplyUpgrades(); Recalculate(); completed &= Bridges[1];
            var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.transform.position = CanStand(d.player) ? d.player : new Vector3(0, .025f, 3); cc.enabled = true; safePosition = player.transform.position;
            if (offline) { double seconds = Math.Min(3600, Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - d.utc)); double gain = Math.Min(Rate, d.rate) * seconds * .25; souls += gain; if (gain >= 1) Notify("YOUR SOULS KEPT FLOWING\n+" + Format(gain) + " Souls while away.", false); }
            if (forest != null) forest.Restore(incoming);
            cameraRig.CenterOnPlayer(); return true;
        }
        static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        // Bounds a saved position must fall inside. They follow the courts, which grew, so a stone
        // dropped at the far edge is still restored instead of failing the whole save.
        static bool PositionValid(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) && Math.Abs(p.x) <= 60 && p.y >= -5 && p.y <= 10 && p.z >= -70 && p.z <= 120 + Landscape.Expansion;
        public bool Save()
        {
            if (!initialized || !persistenceEnabled) return false;
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
        public void Load()
        {
            foreach (var path in new[] { SavePath, SavePath + ".bak" }) try { if (File.Exists(path) && ImportSave(File.ReadAllText(path), true)) return; } catch (Exception e) { Debug.LogWarning(e.Message); }
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
