using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LightingBlocks
{
    [DefaultExecutionOrder(100)]
    public sealed class LastSparkGame : MonoBehaviour
    {
        public static LastSparkGame Instance { get; private set; }
        public LastSparkBalance balance;
        public BoxPlayer player;
        public StrategyCamera cameraRig;
        public AstralFragment[] fragmentPrefabs;
        public AstralAltar[] altars;
        public Transform fragmentsRoot, shrine, spiritVisual;
        public GameObject bridge, bridgeBarrier, outerFragments;
        public Light worldLight;
        public double essence, lifetime;
        public int resonance, strength, summoned, livesBought;
        public bool spirit, bridgeOpen, completed, firstLight;
        public bool persistenceEnabled = true;
        public bool autoTick = true;
        [NonSerialized] public string savePathOverride;
        public double Rate { get; private set; }
        public int ActiveCount => altars.Count(a => a.Occupant != null);
        public int AncientCount => altars.Count(a => a.outerIsland && a.Occupant != null && a.Occupant.kind == FragmentKind.Ancient);
        public readonly List<AstralFragment> Fragments = new List<AstralFragment>();
        public string Notice { get; private set; }
        public float NoticeUntil { get; private set; }
        public float SpiritProgress => spirit ? spiritTimer / balance.spiritInterval : 0;
        float refreshTimer, saveTimer, spiritTimer;
        bool initialized;
        AudioSource audioSource;
        AudioClip chime;
        Vector3 spiritTarget;
        public string SavePath => string.IsNullOrEmpty(savePathOverride) ? Path.Combine(Application.persistentDataPath, "last-spark-v1.json") : savePathOverride;
        public double SummonCost => Math.Ceiling(balance.summonBase * Math.Pow(balance.summonGrowth, summoned));
        public double ResonanceCost => Math.Ceiling(balance.resonanceBase * Math.Pow(balance.resonanceGrowth, resonance));
        public double StrengthCost => balance.strengthBase * Math.Pow(2, strength);
        public bool AtShrine => bridgeOpen && Vector3.Distance(player.transform.position, shrine.position) < 3.5f;

        void Awake()
        {
            Instance = this;
#if UNITY_EDITOR
            if (File.Exists(Path.Combine(Application.dataPath, "../Library/LastSpark.test-active"))) persistenceEnabled = false;
#endif
            audioSource = gameObject.AddComponent<AudioSource>(); audioSource.spatialBlend = 0; audioSource.volume = .18f;
            chime = AudioClip.Create("Placeholder resonance chime", 13230, 1, 44100, false);
            var wave = new float[13230];
            for (int i = 0; i < wave.Length; i++) { float t = i / 44100f; wave[i] = Mathf.Sin(t * Mathf.PI * 2 * 660) * Mathf.Exp(-t * 13) * .5f; }
            chime.SetData(wave, 0);
        }
        void Start()
        {
            Fragments.Clear(); Fragments.AddRange(fragmentsRoot.GetComponentsInChildren<AstralFragment>(true));
            ApplyUnlocks();
            if (persistenceEnabled) Load();
            Recalculate(); initialized = true;
            if (string.IsNullOrEmpty(Notice)) Notify(firstLight ? "Welcome back. Your constellation is waiting." : "A spark survived. Drag the blue fragment onto a circular altar, then release.", false);
            cameraRig.CenterOnPlayer();
            GameAudio.PlayMusic("music_last_spark");
        }
        void Update()
        {
            if (!initialized || LastSparkHUD.SimulationPaused) return;
            if (autoTick) Advance(Time.deltaTime);
            if (player.transform.position.y < -4) RecoverPlayer();
            if (Keyboard.current != null && player.readInput && Application.isFocused && !LastSparkHUD.GameplayBlocked)
            {
                var k = Keyboard.current;
                if (k.digit1Key.wasPressedThisFrame) Purchase("star");
                if (k.digit2Key.wasPressedThisFrame) Purchase("resonance");
                if (k.digit3Key.wasPressedThisFrame) Purchase("strength");
                if (k.digit4Key.wasPressedThisFrame) Purchase("life");
                if (k.eKey.wasPressedThisFrame) Purchase("ending");
            }
            if (spiritVisual != null && spirit)
            {
                spiritVisual.position = Vector3.Lerp(spiritVisual.position, spiritTarget + Vector3.up * (1.8f + Mathf.Sin(Time.time * 2) * .25f), Time.deltaTime * 3);
                spiritVisual.Rotate(Vector3.up, Time.deltaTime * 60);
            }
        }
        public void Advance(float seconds)
        {
            if (LastSparkHUD.SimulationPaused || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            refreshTimer += seconds;
            if (refreshTimer >= .15f) { refreshTimer = 0; Recalculate(); }
            double amount = Rate * seconds; essence += amount; lifetime += amount;
            if (spirit)
            {
                spiritTimer += seconds;
                if (spiritTimer >= balance.spiritInterval) { spiritTimer %= balance.spiritInterval; DeliverOne(); }
            }
            saveTimer += seconds;
            if (saveTimer >= 10) { saveTimer = 0; Save(); }
        }
        public void Recalculate()
        {
            foreach (var a in altars) a.Occupant = null;
            foreach (var fragment in Fragments)
            {
                fragment.Altar = null;
                if (!fragment.gameObject.activeInHierarchy || fragment.Box == null || fragment.Box.Holder != null) continue;
                var altar = altars.FirstOrDefault(a => (!a.outerIsland || bridgeOpen) && a.Occupant == null && Planar(fragment.transform.position - a.Seat) < .35f);
                if (altar != null) { altar.Occupant = fragment; fragment.Altar = altar; }
            }
            Rate = CalculateRate();
            foreach (var altar in altars) altar.RefreshVisual();
            if (!firstLight && ActiveCount > 0)
            {
                firstLight = true; essence += balance.firstLightReward;
                Notify("FIRST LIGHT  +25 Essence\nMemory I: The universe did not die. Someone put it to sleep.");
            }
            if (worldLight != null) worldLight.intensity = Mathf.Lerp(.45f, 1.1f, Mathf.Clamp01(ActiveCount / 10f));
        }
        public double CalculateRate()
        {
            double total = 0;
            foreach (var altar in altars)
            {
                var f = altar.Occupant;
                if (f == null || f.BaseProduction == 0) continue;
                int neighbors = altars.Count(a => a.Occupant != null && a.Occupant.kind == FragmentKind.Life && Planar(a.transform.position - altar.transform.position) <= balance.lifeRadius);
                total += f.BaseProduction * (1 + neighbors * balance.lifeBonus);
            }
            return total * Math.Pow(balance.resonanceMultiplier, resonance) * (completed ? 2 : 1);
        }
        public double PreviewGain(AstralFragment fragment, AstralAltar target)
        {
            var old = target.Occupant;
            if (old != null) return 0;
            double before = CalculateRate(); target.Occupant = fragment;
            double after = CalculateRate(); target.Occupant = old;
            return after - before;
        }
        public AstralAltar NearestSeat(AstralFragment fragment, float reach = 1.1f)
        {
            return altars.Where(a => (!a.outerIsland || bridgeOpen) && (a.Occupant == null || a.Occupant == fragment) && Planar(a.Seat - fragment.transform.position) <= reach)
                .OrderBy(a => Planar(a.Seat - fragment.transform.position)).FirstOrDefault();
        }
        public bool TrySeat(AstralFragment fragment)
        {
            Recalculate();
            var altar = NearestSeat(fragment);
            if (altar == null || fragment.Box.Holder != null) return false;
            var hits = Physics.OverlapBox(altar.Seat, Vector3.one * .54f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Any(c => c != fragment.Box.Shape && !c.transform.IsChildOf(fragment.transform))) return false;
            Place(fragment, altar); return true;
        }
        public void Place(AstralFragment fragment, AstralAltar altar)
        {
            fragment.Box.Body.position = altar.Seat; fragment.transform.position = altar.Seat;
            Physics.SyncTransforms(); Recalculate();
            if (ActiveCount > 1) Notify(fragment.Title + " attuned  |  " + Format(Rate) + " Essence/sec");
        }
        public bool Purchase(string item)
        {
            if (LastSparkHUD.SimulationPaused) return false;
            double cost;
            switch (item)
            {
                case "star":
                    if (!firstLight) return Deny("Attune your first fragment before calling another.");
                    if (!CanSummon()) return Deny("Place your loose fragments or free an altar first."); cost = SummonCost; break;
                case "life":
                    if (ActiveCount < 2) return Deny("Attune two fragments to discover Life.");
                    if (!CanSummon()) return Deny("Place your loose fragments or free an altar first."); cost = balance.lifeCost * Math.Pow(1.8, livesBought); break;
                case "resonance":
                    if (resonance >= balance.resonanceCap) return Deny("Resonance is fully awakened."); cost = ResonanceCost; break;
                case "strength":
                    if (strength >= 3) return Deny("Astral Strength is fully awakened."); cost = StrengthCost; break;
                case "spirit":
                    if (spirit) return Deny("Wisp Recall is already awakened.");
                    if (ActiveCount < 3) return Deny("Wisp Recall needs 3 active altars (" + ActiveCount + "/3)."); cost = balance.spiritCost; break;
                case "bridge":
                    if (bridgeOpen) return Deny("The Starbridge is already restored. Cross it to find the final shrine.");
                    if (ActiveCount < 4 || resonance < 2) return Deny("STARBRIDGE LOCKED\nActive altars: " + ActiveCount + "/4. Resonance: " + resonance + "/2.\nAttune more fragments and upgrade Resonance in the skill tree."); cost = balance.bridgeCost; break;
                case "ending":
                    string reason = ShrineBlockReason;
                    if (reason != null) return Deny(reason);
                    cost = balance.finalCost; break;
                default: return false;
            }
            if (essence + .0001 < cost) return Deny("NOT ENOUGH ESSENCE\nHave " + Format(essence) + " / " + Format(cost) + ". Need " + Format(cost - essence) + " more.\nAttune fragments to generate Essence.");
            if (item == "star" || item == "life")
            {
                if (!Spawn(item == "star" ? FragmentKind.Starlight : FragmentKind.Life, "summoned-" + Guid.NewGuid().ToString("N"))) return Deny("The arrival area is blocked. Move a fragment away.");
            }
            essence = Math.Max(0, essence - cost);
            switch (item)
            {
                case "star": summoned++; Notify("A new Starlight fragment has arrived beside the origin well."); break;
                case "life": livesBought++; Notify("Life remembers. Place it within 4.5m of generators for +50% output each."); break;
                case "resonance": resonance++; Notify("RESONANCE " + resonance + "  |  All production x" + Math.Pow(balance.resonanceMultiplier, resonance).ToString("0.00")); break;
                case "strength": strength++; Notify("ASTRAL STRENGTH " + strength + "  |  Faster pulling. Ancient Hearts can now be moved."); break;
                case "spirit": spirit = true; Notify("A WISP AWAKENS\nEvery 12 seconds, it recalls one loose fragment to a free altar."); break;
                case "bridge": bridgeOpen = true; Notify("THE PATH REMEMBERS\nMemory II: These fragments are seeds. Someone expected you to return."); break;
                case "ending": completed = true; Notify("THE LAST SPARK\nYou are not witnessing the end. You are carrying the beginning.\nChapter complete. Production doubled. Your constellation can keep growing."); break;
            }
            ApplyUnlocks(); Recalculate(); Save(); return true;
        }
        bool CanSummon() => Fragments.Count(f => f.gameObject.activeInHierarchy && f.Altar == null) < 5 && Fragments.Count(f => f.gameObject.activeInHierarchy) < altars.Count(a => !a.outerIsland || bridgeOpen) + 3;
        public bool Spawn(FragmentKind kind, string id, Vector3? savedPosition = null)
        {
            Vector3 position = Vector3.zero;
            bool found = savedPosition.HasValue;
            if (found) position = savedPosition.Value;
            else for (int i = 0; i < 8; i++)
            {
                var candidate = new Vector3(-5 + i % 4 * 1.6f, .62f, -6 - i / 4 * 1.6f);
                if (Physics.OverlapBox(candidate, Vector3.one * .58f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length > 0) continue;
                position = candidate; found = true; break;
            }
            if (!found) return false;
            var fragment = Instantiate(fragmentPrefabs[(int)kind], position, Quaternion.identity, fragmentsRoot);
            fragment.id = id; fragment.name = fragment.Title + " " + Fragments.Count;
            Fragments.Add(fragment); Physics.SyncTransforms(); return true;
        }
        public bool DeliverOne()
        {
            Recalculate();
            var fragment = Fragments.FirstOrDefault(f => f.gameObject.activeInHierarchy && f.Altar == null && f.Box.Holder == null && f.CanLift);
            if (fragment == null) return false;
            var altar = altars.Where(a => a.Occupant == null && (!a.outerIsland || bridgeOpen) && (fragment.kind != FragmentKind.Ancient || a.outerIsland))
                .FirstOrDefault(a => Physics.OverlapBox(a.Seat, Vector3.one * .58f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length == 0);
            if (altar == null) return false;
            spiritTarget = altar.Seat; Place(fragment, altar); Notify("The wisp recalled a " + fragment.Title + "."); return true;
        }
        public void ApplyUnlocks()
        {
            bridge.SetActive(bridgeOpen); bridgeBarrier.SetActive(!bridgeOpen); outerFragments.SetActive(bridgeOpen);
            player.moveSpeed = 4.5f + strength * .35f; player.pullSpeed = 2.3f + strength * .65f;
            if (spiritVisual != null) { spiritVisual.gameObject.SetActive(spirit); spiritTarget = player.transform.position; }
        }
        public string Objective
        {
            get
            {
                if (!firstLight) return "THE FIRST LIGHT\nDrag a blue fragment onto a circular altar. Release to attune it.";
                if (ActiveCount < 2) return "GATHER THE ECHOES\nAttune a second fragment. Explore the island for free fragments.";
                if (resonance < 2) return "AWAKEN RESONANCE\nReach Resonance II (" + resonance + "/2). Each level multiplies all production by 1.4.";
                if (!bridgeOpen) return "RESTORE THE STARBRIDGE\nAttune 4 fragments (" + ActiveCount + "/4), then spend 300 Essence.";
                if (strength == 0) return "A HEAVIER MEMORY\nUnlock Astral Strength to move Ancient Hearts across the bridge.";
                if (AncientCount < 2) return "THE TWO HEARTS\nCross the bridge. Attune both violet Ancient Hearts on the far island (" + AncientCount + "/2).";
                if (!completed) return "THE TRUTH WAITS\nBring 1,200 Essence to the far shrine. Stand beside it and press E.";
                return "CHAPTER COMPLETE\nYou carry the beginning. Build your constellation; all production is now doubled.";
            }
        }
        public string ShrineBlockReason
        {
            get
            {
                if (completed) return "SHRINE ALREADY AWAKENED\nThis chapter is complete. Your constellation's production is doubled.";
                if (!bridgeOpen) return "E AWAKENS THE FINAL SHRINE\nFirst restore the Starbridge in the skill tree [Tab].\nTo grab nearby fragments, hold left mouse instead.";
                if (!AtShrine) return "FINAL SHRINE OUT OF REACH\nCross the bridge and stand beside the far shrine (" + Mathf.CeilToInt(Vector3.Distance(player.transform.position, shrine.position)) + "m away).\nTo grab fragments, hold left mouse.";
                if (AncientCount < 2) return "THE SHRINE NEEDS TWO HEARTS\nAncient Hearts attuned on the far island: " + AncientCount + "/2.\nDrag both violet Hearts onto far-island altars, then press E.";
                if (essence + .0001 < balance.finalCost) return "NOT ENOUGH ESSENCE\nThe shrine needs " + Format(balance.finalCost) + "; you have " + Format(essence) + ".\nCollect " + Format(balance.finalCost - essence) + " more, then press E.";
                return null;
            }
        }
        public void Notify(string message, bool sound = true)
        {
            Notice = message; NoticeUntil = Time.unscaledTime + (message.Contains("Memory") || message.Contains("CHAPTER") || message.Contains("LAST SPARK") ? 12 : 6);
            if (sound && audioSource != null) audioSource.PlayOneShot(chime);
        }
        bool Deny(string message) { Notify(message, false); return false; }
        public void RecoverPlayer()
        {
            player.Release(); var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
            player.transform.position = new Vector3(0, .025f, -4); cc.enabled = true; cameraRig.CenterOnPlayer();
        }
        public static float Planar(Vector3 v) { v.y = 0; return v.magnitude; }
        public static string Format(double value) => value >= 1000000 ? (value / 1000000).ToString("0.00") + "M" : value >= 10000 ? (value / 1000).ToString("0.0") + "K" : value.ToString(value < 100 ? "0.0" : "0");

        [Serializable] public class FragmentSave { public string id; public int kind; public Vector3 position; }
        [Serializable] public class SaveData
        {
            public int version = 1;
            public double essence, lifetime, savedRate;
            public long savedUtc;
            public int resonance, strength, summoned, livesBought;
            public bool spirit, bridgeOpen, completed, firstLight;
            public Vector3 playerPosition;
            public List<FragmentSave> fragments = new List<FragmentSave>();
        }
        public string ExportSave()
        {
            var data = new SaveData { essence = essence, lifetime = lifetime, savedRate = Rate, savedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), resonance = resonance, strength = strength,
                summoned = summoned, livesBought = livesBought, spirit = spirit, bridgeOpen = bridgeOpen, completed = completed, firstLight = firstLight, playerPosition = player.transform.position };
            foreach (var f in Fragments) data.fragments.Add(new FragmentSave { id = f.id, kind = (int)f.kind, position = f.transform.position });
            return JsonUtility.ToJson(data, true);
        }
        public bool Save()
        {
            if (!persistenceEnabled || !initialized) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                File.WriteAllText(SavePath + ".tmp", ExportSave());
                if (File.Exists(SavePath)) File.Replace(SavePath + ".tmp", SavePath, SavePath + ".bak");
                else File.Move(SavePath + ".tmp", SavePath);
                return true;
            }
            catch (Exception e) { Debug.LogWarning("Last Spark save failed: " + e.Message); Notify("Could not save progress. Check free disk space.", false); return false; }
        }
        public void NewJourney()
        {
            try
            {
                if (File.Exists(SavePath)) { File.Copy(SavePath, SavePath + ".previous", true); File.Delete(SavePath); }
                if (File.Exists(SavePath + ".bak")) File.Delete(SavePath + ".bak");
                persistenceEnabled = false;
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
            catch (Exception e) { Notify("Could not start a new journey: " + e.Message, false); }
        }
        public bool ImportSave(string json, bool offline = false)
        {
            SaveData data;
            try { data = JsonUtility.FromJson<SaveData>(json); } catch { return false; }
            if (data == null || data.version != 1 || data.fragments == null || data.fragments.Count > 100 || !Finite(data.essence) || data.essence < 0 || !Finite(data.lifetime) || !Finite(data.savedRate)) return false;
            if (data.fragments.Any(f => f == null || string.IsNullOrEmpty(f.id) || f.kind < 0 || f.kind > 2 || !ValidPosition(f.position)) || data.fragments.Select(f => f.id).Distinct().Count() != data.fragments.Count) return false;
            player.Release();
            essence = data.essence; lifetime = Math.Max(0, data.lifetime); resonance = Mathf.Clamp(data.resonance, 0, balance.resonanceCap); strength = Mathf.Clamp(data.strength, 0, 3);
            summoned = Mathf.Clamp(data.summoned, 0, 100); livesBought = Mathf.Clamp(data.livesBought, 0, 100);
            spirit = data.spirit; bridgeOpen = data.bridgeOpen; completed = data.completed; firstLight = data.firstLight;
            foreach (var f in Fragments.ToArray()) if (!data.fragments.Any(d => d.id == f.id)) { Fragments.Remove(f); f.gameObject.SetActive(false); Destroy(f.gameObject); }
            foreach (var saved in data.fragments)
            {
                var fragment = Fragments.FirstOrDefault(f => f.id == saved.id);
                if (fragment == null) { Spawn((FragmentKind)saved.kind, saved.id, saved.position); fragment = Fragments.Last(); }
                fragment.transform.position = saved.position;
                // Inactive far-island fragments acquire their Rigidbody reference when the island is unlocked.
                var body = fragment.GetComponent<Rigidbody>(); body.position = saved.position;
            }
            ApplyUnlocks(); Physics.SyncTransforms();
            var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
            player.transform.position = ValidPosition(data.playerPosition) && data.playerPosition.y > -1 ? data.playerPosition : new Vector3(0, .025f, -4); cc.enabled = true;
            Recalculate();
            if (offline && data.savedUtc > 0)
            {
                double elapsed = Math.Clamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - data.savedUtc, 0, balance.offlineHoursCap * 3600);
                double earned = Math.Max(0, Math.Min(Rate, data.savedRate)) * elapsed * balance.offlineMultiplier;
                essence += earned; lifetime += earned;
                if (earned >= 1) Notify("The embers kept glowing: +" + Format(earned) + " Essence while away.", false);
            }
            return true;
        }
        public void Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                if (!ImportSave(File.ReadAllText(SavePath), true) && (!File.Exists(SavePath + ".bak") || !ImportSave(File.ReadAllText(SavePath + ".bak"), true)))
                    Notify("Save could not be read. A fresh constellation is ready.", false);
            }
            catch (Exception e) { Debug.LogWarning("Last Spark load failed: " + e.Message); }
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static bool ValidPosition(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) && Mathf.Abs(p.x) < 40 && p.z > -20 && p.z < 45 && p.y > -10 && p.y < 10;
        void OnApplicationPause(bool paused) { if (paused) Save(); }
        void OnApplicationFocus(bool focused) { if (!focused) Save(); }
        void OnApplicationQuit() { Save(); }
        void OnDestroy() { if (Instance == this) Instance = null; if (chime != null) Destroy(chime); }
    }
}
