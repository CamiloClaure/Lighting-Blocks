using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace LightingBlocks
{
    // Lighting's fundamental particles, imported from the reference prototype (lightning-block-d,
    // SoulCollectionSystem): a seeded field floats a yellow spirit dot over about 18% of the void grid
    // cells, and nothing is stored per dot. Only the nearest MaxVisible within StreamDistance are drawn,
    // and the nearest DotLights within LightDistance carry a small light. A dot is collected standing on
    // a tile under it; its cell then stays empty for ForestWorld.LightingRespawnSeconds (the timers are
    // ForestSave.dots, the only saved state).
    public sealed class LightingDots : MonoBehaviour
    {
        public const int DensityPerMille = 180, MaxVisible = 40, DotLights = 4;
        public const float StreamDistance = 24f, LightDistance = 12f, Hover = .85f, Bob = .16f, Size = .3f, AppearSeconds = .6f, PullSeconds = .35f;
        // The reference soul's emission, (1, .72, .08) x 4; LightingFog fades it with distance.
        public static readonly Color Emission = new Color(4f, 2.88f, .32f);
        static readonly Color LightColor = new Color(1f, .8f, .15f);

        ForestWorld world; GameManager game; Material material;
        readonly List<Transform> visuals = new List<Transform>();
        readonly List<Light> lights = new List<Light>();
        readonly Vector2Int[] cells = new Vector2Int[MaxVisible];
        readonly bool[] used = new bool[MaxVisible];
        readonly int[] litDots = new int[DotLights];
        readonly float[] appear = new float[MaxVisible];
        // A dot taken by LightingPull keeps its visual while it flies to the player: progress 0..1, 0 = idle.
        readonly float[] flight = new float[MaxVisible];
        readonly Vector3[] flyFrom = new Vector3[MaxVisible];
        readonly List<(Vector2Int cell, int distance)> candidates = new List<(Vector2Int, int)>();
        readonly HashSet<Vector2Int> wanted = new HashSet<Vector2Int>(), shown = new HashSet<Vector2Int>(), collected = new HashSet<Vector2Int>();
        Vector2Int streamCell; bool dirty = true;
        float time, lightTimer, collectTimer;
        // The player's position at the last pickup poll: a poll tests the whole path since then, so a
        // 40 m/s dash (4 m between polls) still takes every dot it passes through.
        Vector3? swept;

        public IReadOnlyList<Transform> Visuals => visuals;
        public IReadOnlyList<Light> Lights => lights;
        public IEnumerable<Vector2Int> ShownCells { get { for (int i = 0; i < MaxVisible; i++) if (used[i]) yield return cells[i]; } }

        // Pure function of seed and cell: a murmur3 finalizer over the three inputs.
        public static bool HasDot(int seed, Vector2Int cell)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)cell.x * 0x85EBCA77u ^ (uint)cell.y * 0xC2B2AE3Du;
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return h % 1000u < DensityPerMille;
            }
        }
        // A dot is there now: seeded, over the void (tiled or not), in bounds, and not waiting to return.
        public bool IsDot(Vector2Int cell)
        {
            var state = world != null ? world.State : null;
            if (state == null || !HasDot(state.seed, cell)) return false;
            if (state.dots != null) foreach (var d in state.dots) if (d.x == cell.x && d.z == cell.y) return false;
            return !LightingBridgeGrid.OverlapsPlatform(cell) && LightingBridgeGrid.InBounds(cell);
        }

        // Called from ForestWorld.Build under the new forest root, so a rebuild destroys the old pool with it.
        public void Build(ForestWorld owner, Transform parent)
        {
            world = owner; game = owner.GetComponent<GameManager>();
            if (material == null) material = SpiritMaterial();
            visuals.Clear(); lights.Clear(); swept = null;
            var root = new GameObject("Spirit dots").transform; root.SetParent(parent, false);
            for (int i = 0; i < MaxVisible; i++)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere); dot.name = "Spirit dot " + i;
                ForestWorld.Remove(dot.GetComponent<Collider>());
                dot.transform.SetParent(root, false); dot.transform.localScale = Vector3.zero;
                var r = dot.GetComponent<Renderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                dot.SetActive(false); visuals.Add(dot.transform); used[i] = false; flight[i] = 0;
            }
            // Its own objects, not children of a dot: a light never rides a hidden or fading visual.
            for (int i = 0; i < DotLights; i++)
            {
                var light = new GameObject("Spirit dot light " + i).AddComponent<Light>(); light.transform.SetParent(root, false);
                light.type = LightType.Point; light.color = LightColor; light.range = 2.5f; light.intensity = .8f; light.shadows = LightShadows.None; light.enabled = false;
                lights.Add(light);
            }
            dirty = true;
        }
        // The yellow spirit look; LightingGrenade throws a bigger one.
        public static Material SpiritMaterial()
        {
            var shader = Resources.Load<Shader>("LivingSpirit");
            var spirit = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "Spirit dot" };
            spirit.SetColor("_BaseColor", LightingParticles.ParticleColor); spirit.SetColor("_EmissionColor", Emission);
            return spirit;
        }

        public void Tick(float delta, bool collect)
        {
            var state = world != null ? world.State : null;
            if (state == null || visuals.Count == 0 || game == null || game.player == null) return;
            time += delta;
            if (state.dots == null) state.dots = new List<DotSave>();
            for (int i = state.dots.Count - 1; i >= 0; i--)
            {
                state.dots[i].remaining -= delta;
                if (state.dots[i].remaining <= 0) { state.dots.RemoveAt(i); dirty = true; }
            }
            Stream();
            for (int i = 0; i < MaxVisible; i++)
            {
                if (!used[i]) continue;
                appear[i] = Mathf.Min(1, appear[i] + delta / AppearSeconds);
                visuals[i].position = Position(cells[i]) + Vector3.up * (Mathf.Sin(time * 2 + cells[i].x * .7f + cells[i].y * 1.3f) * Bob);
                visuals[i].localScale = Vector3.one * (Size * Mathf.SmoothStep(0, 1, appear[i]));
            }
            var catcher = game.player.transform.position + Vector3.up;
            for (int i = 0; i < MaxVisible; i++)
            {
                if (flight[i] <= 0) continue;
                flight[i] = Mathf.Min(1, flight[i] + delta / PullSeconds); float t = Mathf.SmoothStep(0, 1, flight[i]);
                visuals[i].position = Vector3.Lerp(flyFrom[i], catcher, t); visuals[i].localScale = Vector3.one * (Size * (1 - .6f * t));
                if (flight[i] >= 1) { flight[i] = 0; visuals[i].gameObject.SetActive(false); dirty = true; }
            }
            lightTimer -= delta; if (lightTimer <= 0) { lightTimer = .2f; PlaceLights(); }
            collectTimer -= delta;
            if (collectTimer <= 0)
            {
                collectTimer = .1f;
                var to = game.player.transform.position; var from = swept ?? to; swept = to;
                // ponytail: teleport cap. A jump longer than 12 m (load, fall recovery) is not a path;
                // a dash covers 4 m per poll, so only a frame slower than .3 s would lose its sweep.
                if ((to - from).sqrMagnitude > 144) from = to;
                if (collect) for (int i = 0; i < MaxVisible; i++) if (used[i]) TryCollect(cells[i], from, to);
            }
        }
        public static Vector3 Position(Vector2Int cell) { var p = LightingBridgeGrid.Cell(cell.x, cell.y); p.y = Hover; return p; }

        // Rebuilds the shown set only when the player changes cell or a dot is taken or returns. A dot
        // that stays keeps its visual; a new one takes a free visual and scales in.
        public void Refresh() { dirty = true; Stream(); }
        void Stream()
        {
            var state = world.State;
            var cell = LightingBridgeGrid.CellOf(game.player.transform.position);
            if (!dirty && cell == streamCell) return;
            dirty = false; streamCell = cell;
            collected.Clear(); foreach (var d in state.dots) collected.Add(new Vector2Int(d.x, d.z));
            candidates.Clear();
            int reach = Mathf.CeilToInt(StreamDistance / LightingBridgeGrid.CellSize), limit = reach * reach;
            for (int x = -reach; x <= reach; x++)
                for (int z = -reach; z <= reach; z++)
                {
                    int distance = x * x + z * z; if (distance > limit) continue;
                    var c = new Vector2Int(cell.x + x, cell.y + z);
                    // Hash first: the platform test only runs on the ~18% of cells that hold a dot.
                    if (!HasDot(state.seed, c) || collected.Contains(c) || LightingBridgeGrid.OverlapsPlatform(c) || !LightingBridgeGrid.InBounds(c)) continue;
                    candidates.Add((c, distance));
                }
            candidates.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.cell.x != b.cell.x ? a.cell.x.CompareTo(b.cell.x) : a.cell.y.CompareTo(b.cell.y));
            wanted.Clear(); for (int i = 0; i < candidates.Count && i < MaxVisible; i++) wanted.Add(candidates[i].cell);
            shown.Clear();
            for (int i = 0; i < MaxVisible; i++)
            {
                if (!used[i]) continue;
                if (wanted.Contains(cells[i])) { shown.Add(cells[i]); continue; }
                used[i] = false; visuals[i].gameObject.SetActive(false);
            }
            int free = 0;
            foreach (var c in wanted)
            {
                if (shown.Contains(c)) continue;
                // ponytail: a dot left without a slot while pulled dots fly shows on the restream when they land.
                while (free < MaxVisible && (used[free] || flight[free] > 0)) free++;
                if (free == MaxVisible) break;
                used[free] = true; cells[free] = c; appear[free] = 0;
                visuals[free].position = Position(c); visuals[free].localScale = Vector3.zero; visuals[free].gameObject.SetActive(true);
            }
        }

        // The nearest shown dots within LightDistance get the few lights, like the reference's four.
        void PlaceLights()
        {
            var player = game.player.transform.position;
            for (int k = 0; k < lights.Count; k++)
            {
                int best = -1; float bestDistance = LightDistance * LightDistance;
                for (int i = 0; i < MaxVisible; i++)
                {
                    if (!used[i]) continue;
                    bool taken = false; for (int j = 0; j < k; j++) if (litDots[j] == i) taken = true;
                    if (taken) continue;
                    var p = Position(cells[i]); float d = (p.x - player.x) * (p.x - player.x) + (p.z - player.z) * (p.z - player.z);
                    if (d <= bestDistance) { best = i; bestDistance = d; }
                }
                litDots[k] = best; lights[k].enabled = best >= 0;
                if (best >= 0) lights[k].transform.position = Position(cells[best]);
            }
        }

        // Same rule the slot pickups had: within 1.05 m, standing on the very tile under the dot. It is
        // tested at the point of the path from..to nearest the dot.
        public bool TryCollect(Vector2Int cell) { var p = game.player.transform.position; return TryCollect(cell, p, p); }
        public bool TryCollect(Vector2Int cell, Vector3 from, Vector3 to)
        {
            if (GameHUD.GameplayBlocked || world == null || world.State == null || !IsDot(cell)) return false;
            var dot = Position(cell);
            Vector2 a = new Vector2(from.x, from.z), path = new Vector2(to.x - from.x, to.z - from.z);
            float t = path.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector2.Dot(new Vector2(dot.x, dot.z) - a, path) / path.sqrMagnitude) : 0;
            var player = Vector3.Lerp(from, to, t);
            float dx = player.x - dot.x, dz = player.z - dot.z;
            if (dx * dx + dz * dz > 1.1025f || Mathf.Abs(player.y - dot.y) > 1.5f) return false;
            var tile = game.BlockUnder(new Vector3(dot.x, 0, dot.z));
            if (tile == null || tile != game.BlockUnder(player)) return false;
            return Take(cell, false);
        }

        // Spirit Pull: every dot whose cell centre lies within radius of center, over a tile or the open
        // void. Returns how many were taken; each shown one flies to the player.
        public int Pull(Vector3 center, float radius)
        {
            if (GameHUD.GameplayBlocked || world == null || world.State == null || game == null || game.player == null) return 0;
            if (world.State.dots == null) world.State.dots = new List<DotSave>();
            int reach = Mathf.CeilToInt(radius / LightingBridgeGrid.CellSize), taken = 0;
            var middle = LightingBridgeGrid.CellOf(center);
            for (int x = -reach; x <= reach; x++)
                for (int z = -reach; z <= reach; z++)
                {
                    var c = new Vector2Int(middle.x + x, middle.y + z); var p = Position(c);
                    if ((p.x - center.x) * (p.x - center.x) + (p.z - center.z) * (p.z - center.z) > radius * radius || !IsDot(c)) continue;
                    if (Take(c, true)) taken++;
                }
            return taken;
        }

        // Credits one dot and starts its respawn timer. A walked-over dot vanishes with a chime; a pulled
        // one flies in silently (LightingPull plays one chime for the whole pull).
        bool Take(Vector2Int cell, bool fly)
        {
            if (!world.TryAddInventory(ForestWorld.ParticleUnitsPerParticle + Mathf.Clamp(game.soulGainLevel, 0, 8))) return false;
            world.State.dots.Add(new DotSave { x = cell.x, z = cell.y, remaining = ForestWorld.LightingRespawnSeconds });
            for (int i = 0; i < MaxVisible; i++)
            {
                if (!used[i] || cells[i] != cell) continue;
                used[i] = false;
                if (fly) { flight[i] = .0001f; flyFrom[i] = visuals[i].position; } else visuals[i].gameObject.SetActive(false);
            }
            dirty = true;
            if (!fly) { var audio = GetComponent<LightingPickupAudio>(); if (audio != null) audio.PlayPickup(); }
            return true;
        }
        public bool Flying { get { for (int i = 0; i < MaxVisible; i++) if (flight[i] > 0) return true; return false; } }

        void OnDestroy() => ForestWorld.Remove(material);
    }
}
