using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightingBlocks
{
    // The playable islands are authored by the editor setup scripts. This pass rebuilds them at
    // their full size and fills the rest of the frame at runtime so the camera never looks at
    // empty background: wide courts you have to drag the camera across, a lit valley under them,
    // distant ruin silhouettes, neon paving, a light on every bridge step, and crisp labels.
    public static class WorldDressing
    {
        // The player recovers below y = -3, so scenery at -5 can never be stood on.
        public const float Floor = -5f;
        // The authored cliff skirts hang to -5..-7. The valley meets them at -3.6 so no sliver of
        // empty background shows under a shore, while staying under the -3 recovery threshold.
        public const float Shoulder = -3.6f;
        // The courts now reach 84 units from the centre, so the far rim has to begin past them.
        public const float RimStart = 96f, RimEnd = 150f, Radius = 210f;
        // Both rifts are carved past the full width of the courts: a gap has to read as a chasm
        // splitting the world, not as a notch you could walk around. Full depth to 58, gone by 74.
        public const float RiftHalfWidth = 74f;
        static readonly Vector2 Centre = new Vector2(0, 25);
        // Shared with the editor bake so a baked scene and a runtime rebuild light the same way.
        // public static readonly Color PavingEmission = new Color(1.7f, 1.7f, 1.85f);
        static Texture2D halo;
        static Material seamNeon, additive;

        public static void Build(GameManager game)
        {
            if (game == null || game.blocksRoot == null) return;
            var world = game.blocksRoot.parent;
            var existing = world.Find("Immersive Deeps");
            if (existing != null) Object.Destroy(existing.gameObject);
            var root = new GameObject("Immersive Deeps").transform;
            root.SetParent(world, false);
            Reshape(world);
            BuildValley(root, world);
            Dress(root, world);
            Atmosphere(game);
            WorldLabels.Adopt();
            Footlights.Attach(game.player);
            foreach (var block in game.Blocks) StepLight(block);
        }

        // ---- the courts ------------------------------------------------------

        // The scene was baked from smaller polygons. Rebuilding the floor and cliff meshes from
        // the current ones keeps one source of truth for terrain, collision and CanStand, and
        // means nobody has to remember to re-run the editor art command.
        static void Reshape(Transform world)
        {
            for (int i = 0; i < Landscape.Shores.Length; i++)
            {
                var points = Landscape.Shores[i];
                var floor = Find(world, "ShoreFloor" + i);
                var cliff = Find(world, "ShoreCliff" + i);
                if (floor != null)
                {
                    Swap(floor, FloorMesh(points, i));
                }
                if (cliff != null) Swap(cliff, CliffMesh(points, i));
            }
        }
        static void Swap(GameObject obj, Mesh mesh)
        {
            var filter = obj.GetComponent<MeshFilter>(); if (filter != null) filter.sharedMesh = mesh;
            var collider = obj.GetComponent<MeshCollider>(); if (collider != null) collider.sharedMesh = mesh;
        }
        public static Mesh FloorMesh(Vector2[] points, int index)
        {
            int count = points.Length;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            Vector2 centre = Centroid(points);
            vertices.Add(new Vector3(centre.x, 0, centre.y)); uv.Add(centre / 8);
            foreach (var p in points) { vertices.Add(new Vector3(p.x, 0, p.y)); uv.Add(p / 8); }
            for (int i = 0; i < count; i++) triangles.AddRange(new[] { 0, (i + 1) % count + 1, i + 1 });
            return Finish(vertices, uv, triangles, "Shore " + index);
        }
        public static Mesh CliffMesh(Vector2[] points, int index)
        {
            int count = points.Length;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            Vector2 centre = Centroid(points);
            for (int i = 0; i < count; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % count]; int n = vertices.Count;
                vertices.AddRange(new[] {
                    new Vector3(a.x, 0, a.y), new Vector3(b.x, 0, b.y),
                    new Vector3(centre.x + (b.x - centre.x) * .88f, -5 - (i % 3), centre.y + (b.y - centre.y) * .88f),
                    new Vector3(centre.x + (a.x - centre.x) * .88f, -5 - (i % 3), centre.y + (a.y - centre.y) * .88f) });
                float span = Vector2.Distance(a, b) / 6;
                uv.AddRange(new[] { new Vector2(0, 1), new Vector2(span, 1), new Vector2(span, 0), new Vector2(0, 0) });
                triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
            }
            return Finish(vertices, uv, triangles, "Cliff " + index);
        }
        static Mesh Finish(List<Vector3> vertices, List<Vector2> uv, List<int> triangles, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            // Without a tangent basis the paving and cliff normal maps have nothing to work
            // against and the surface renders flat. This is why the courts looked untextured.
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
        static Vector2 Centroid(Vector2[] points)
        {
            Vector2 centre = Vector2.zero;
            foreach (var p in points) centre += p;
            return centre / points.Length;
        }
        // The widest span of playable ground, used for the camera limits and the rift carving.
        public static Bounds CourtBounds()
        {
            var min = new Vector2(float.MaxValue, float.MaxValue); var max = -min;
            foreach (var shore in Landscape.Shores)
                foreach (var p in shore) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(min.x, 0, min.y), new Vector3(max.x, 0, max.y));
            return bounds;
        }

        // ---- the deeps -------------------------------------------------------

        // Ground height around the play area. The rim lifts far away so the frame ends in land,
        // and both rifts are carved deep so the gaps read as chasms instead of flat holes.
        public static float Height(float x, float z)
        {
            float radius = Vector2.Distance(new Vector2(x, z), Centre);
            float hills = (Mathf.PerlinNoise(x * .034f + 11.3f, z * .034f + 7.1f) - .5f) * 3.6f
                + (Mathf.PerlinNoise(x * .115f + 3.7f, z * .115f + 1.9f) - .5f) * .9f;
            float rim = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(RimStart, RimEnd, radius)) * 12f;
            float ground = Mathf.Lerp(Shoulder, Floor + hills, Mathf.SmoothStep(0, 1, ShoreDistance(x, z) / 10f));
            return ground + rim - Chasm(x, z);
        }
        static float Chasm(float x, float z)
        {
            float rift = Mathf.Max(Band(z, 7, 21), Band(z, 34, 48));
            return rift * Mathf.Clamp01((RiftHalfWidth - Mathf.Abs(x)) / 16f) * 14f;
        }
        // Distance from a point to the nearest playable shore, zero when standing over one.
        public static float ShoreDistance(float x, float z)
        {
            var point = new Vector2(x, z); float best = float.MaxValue;
            foreach (var shore in Landscape.Shores)
            {
                if (Landscape.Inside(point, shore)) return 0;
                for (int i = 0, j = shore.Length - 1; i < shore.Length; j = i++)
                    best = Mathf.Min(best, EdgeDistance(point, shore[j], shore[i]));
            }
            return best;
        }
        // Distance to the nearest court edge whether the point is on a court or off it, which is
        // what keeps a column from being planted half over a cliff.
        public static float EdgeGap(float x, float z)
        {
            var point = new Vector2(x, z); float best = float.MaxValue;
            foreach (var shore in Landscape.Shores)
                for (int i = 0, j = shore.Length - 1; i < shore.Length; j = i++)
                    best = Mathf.Min(best, EdgeDistance(point, shore[j], shore[i]));
            return best;
        }
        static float EdgeDistance(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float t = edge.sqrMagnitude < .0001f ? 0 : Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude);
            return Vector2.Distance(point, a + edge * t);
        }
        static float Band(float value, float from, float to)
            => Mathf.Min(Mathf.SmoothStep(0, 1, (value - from) / 3.5f), Mathf.SmoothStep(0, 1, (to - value) / 3.5f));

        static void BuildValley(Transform root, Transform world)
        {
            var radii = new List<float>();
            for (float r = 0; r <= 120; r += 4) radii.Add(r);
            foreach (float r in new[] { 138f, 158f, 180f, 196f, Radius }) radii.Add(r);
            const int segments = 120;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            for (int ring = 0; ring < radii.Count; ring++)
                for (int step = 0; step <= segments; step++)
                {
                    float angle = step * Mathf.PI * 2 / segments;
                    float x = Centre.x + Mathf.Cos(angle) * radii[ring], z = Centre.y + Mathf.Sin(angle) * radii[ring];
                    vertices.Add(new Vector3(x, Height(x, z), z)); uv.Add(new Vector2(x, z) / 9f);
                }
            for (int ring = 0; ring < radii.Count - 1; ring++)
                for (int step = 0; step < segments; step++)
                {
                    int a = ring * (segments + 1) + step, b = a + segments + 1;
                    triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var obj = new GameObject("Deep Valley", typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(root, false);
            obj.GetComponent<MeshFilter>().sharedMesh = Finish(vertices, uv, triangles, "Deep Valley");
            obj.GetComponent<Renderer>().sharedMaterial = ValleyMaterial(world);
            // No collider: the valley is scenery, the bridges stay the only way across.
        }
        static Material ValleyMaterial(Transform world)
        {
             var cliff = Find(world, "ShoreCliff0");
            var renderer = cliff != null ? cliff.GetComponent<Renderer>() : null;
            return renderer != null && renderer.sharedMaterial != null
                ? renderer.sharedMaterial
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }
        static void Dress(Transform root, Transform world)
        {
            var templates = new List<GameObject>();
            foreach (string name in new[] { "ENV_BrokenColumn", "ENV_PartialArch", "ENV_CliffChunk", "ENV_RubbleCluster", "ENV_LowWall" })
            {
                var found = Find(world, name);
                if (found != null) templates.Add(found);
            }
            if (templates.Count == 0) return;
            var random = new System.Random(1907);
            int placed = 0;
            for (int attempt = 0; attempt < 1400 && placed < 150; attempt++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float radius = 30 + (float)random.NextDouble() * 178;
                float x = Centre.x + Mathf.Cos(angle) * radius, z = Centre.y + Mathf.Sin(angle) * radius;
                if (!Clear(x, z)) continue;
                var clone = Plant(templates[random.Next(templates.Count)], root, random, x, Height(x, z) - .5f, z);
                clone.name = "Deep ruin " + placed++;
                // Distance sets the size, so nothing near a court grows tall enough to hide it.
                float reach = Mathf.Lerp(1.1f, 4.6f, Mathf.InverseLerp(9, 45, ShoreDistance(x, z)));
                clone.transform.localScale = Vector3.one * reach * (.85f + (float)random.NextDouble() * .4f);
            }
            CourtRuins(root, random, templates);
        }
        // The courts are now wide enough to be empty, so they get their own dressing. It stays
        // out of the middle so the walk from the stone source to a rift is never obstructed.
        static void CourtRuins(Transform root, System.Random random, List<GameObject> templates)
        {
            var bounds = CourtBounds();
            int placed = 0;
            for (int attempt = 0; attempt < 2600 && placed < 130; attempt++)
            {
                float x = Mathf.Lerp(bounds.min.x, bounds.max.x, (float)random.NextDouble());
                float z = Mathf.Lerp(bounds.min.z, bounds.max.z, (float)random.NextDouble());
                if (Mathf.Abs(x) < 17) continue;
                if (ShoreDistance(x, z) > 0 || EdgeGap(x, z) < 5) continue;
                var clone = Plant(templates[random.Next(templates.Count)], root, random, x, 0, z);
                clone.name = "Court ruin " + placed++;
                clone.transform.localScale = Vector3.one * (.7f + (float)random.NextDouble() * 1.1f);
            }
        }
        // ponytail: court ruins are decoration, not obstacles - colliders are stripped like the
        // deeps so nothing here can ever pocket the player. Keep them if navigation ever matters.
        static GameObject Plant(GameObject template, Transform root, System.Random random, float x, float y, float z)
        {
            var clone = Object.Instantiate(template, new Vector3(x, y, z),
                Quaternion.Euler(0, (float)random.NextDouble() * 360, 0), root);
            foreach (var collider in clone.GetComponentsInChildren<Collider>()) Object.Destroy(collider);
            foreach (var light in clone.GetComponentsInChildren<Light>()) Object.Destroy(light);
            clone.SetActive(true);
            return clone;
        }
        // Scenery stands five metres below the courts, so anything under a shore would spear through
        // its floor, and anything in a rift would read as a crossing. Both are kept clear.
        static bool Clear(float x, float z)
        {
            if (Mathf.Abs(x) < RiftHalfWidth && (z > 4 && z < 24 || z > 31 && z < 51)) return false;
            return ShoreDistance(x, z) > 9;
        }
        static void Atmosphere(GameManager game)
        {
            var haze = new Color(.062f, .072f, .098f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = haze; RenderSettings.fogStartDistance = 10; RenderSettings.fogEndDistance = 20;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.15f, .18f, .25f);
            foreach (var light in Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional) { light.intensity = .14f; light.color = new Color(.56f, .66f, .86f); }
            if (game.cameraRig == null) return;
            game.cameraRig.distance = 13f;
            // Let the camera be dragged to every edge of the enlarged courts, and no further.
            var bounds = CourtBounds();
            game.cameraRig.minimumFocus = new Vector2(bounds.min.x + 2, bounds.min.z + 2);
            game.cameraRig.bounds = new Vector2(bounds.max.x - 2, bounds.max.z - 2);
            var camera = game.cameraRig.GetComponent<Camera>();
            if (camera == null) return;
            camera.backgroundColor = haze; camera.farClipPlane = 420;
        }
        static GameObject Find(Transform world, string name)
        {
            foreach (var child in world.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child.gameObject;
            return null;
        }

        // ---- neon ------------------------------------------------------------

       
 
        // A soft radial disc, used additively for the glow a footfall leaves on the ground.
        public static Texture2D Halo()
        {
            if (halo != null) return halo;
            const int size = 128;
            halo = new Texture2D(size, size, TextureFormat.RGBA32, true)
            { name = "Footfall halo", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            float middle = (size - 1) * .5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - middle) * (x - middle) + (y - middle) * (y - middle)) / middle;
                    float a = Mathf.Clamp01(1 - d);
                    // Squared falloff plus a brighter core, so it reads as a pool of light and not a dot.
                    pixels[y * size + x] = new Color(1, 1, 1, a * a * (.45f + a * .55f));
                }
            halo.SetPixels(pixels); halo.Apply();
            return halo;
        }
        // Additive unlit material for the footfall discs, matching the Soul halo recipe the art
        // pass already uses: transparent surface, SrcAlpha/One blending, no depth write.
        public static Material Additive()
        {
            if (additive != null) return additive;
            additive = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Footfall glow" };
            additive.SetTexture("_BaseMap", Halo());
            additive.SetFloat("_Surface", 1); additive.SetFloat("_Blend", 2);
            additive.SetFloat("_SrcBlend", 5); additive.SetFloat("_DstBlend", 1);
            additive.SetFloat("_ZWrite", 0); additive.SetFloat("_Cull", 0);
            additive.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            additive.renderQueue = 3000;
            return additive;
        }


        // ---- bridge steps ----------------------------------------------------

        // Neon ramp along the whole crossing: the first rift runs cyan to violet, the second
        // violet to magenta, so the path reads as one gradient from shore to shore.
        public static Color StepColor(int gap, int row)
        {
            float t = Mathf.Clamp01((gap * GameManager.Rows + row) / (GameManager.Rows * 2f - 1));
            return t < .5f
                ? Color.Lerp(new Color(.16f, 1f, .92f), new Color(.58f, .42f, 1f), t * 2)
                : Color.Lerp(new Color(.58f, .42f, 1f), new Color(1f, .3f, .78f), (t - .5f) * 2);
        }
        // Every placed step carries its own light. The renderer runs Forward+, which has no
        // per-object additional-light limit, so seventy small unshadowed points are affordable.
        public static void StepLight(BridgeBlock block)
        {
            if (block == null) return;
            var existing = block.transform.Find("Step light");
            if (!block.placed) { if (existing != null) existing.gameObject.SetActive(false); return; }
            Light light;
            if (existing == null)
            {
                var obj = new GameObject("Step light");
                obj.transform.SetParent(block.transform, false);
                light = obj.AddComponent<Light>();
                light.type = LightType.Point; light.shadows = LightShadows.None;
            }
            else { existing.gameObject.SetActive(true); light = existing.GetComponent<Light>(); }
            var colour = StepColor(block.gap, block.row);
            // A placed block is scaled 2 x .36 x 2, so the local offset has to divide that out.
            float lift = Mathf.Max(.01f, block.transform.localScale.y);
            light.transform.localPosition = new Vector3(0, .85f / lift, 0);
            light.color = colour;
            light.range = block.Connected ? 6.8f : 3.4f;
            light.intensity = block.Connected ? 4.4f : 1f;
            Seams(block, colour);
        }
        static void Seams(BridgeBlock block, Color colour)
        {
            var art = block.GetComponent<BlockArt>();
            if (art == null || art.seams == null) return;
            var properties = new MaterialPropertyBlock();
            foreach (var seam in art.seams)
            {
                if (seam == null) continue;
                // The art pack's seam material has no emission pass. One shared clone with the
                // keyword on lets every step tint itself through a property block.
                if (seamNeon == null && seam.sharedMaterial != null)
                {
                    seamNeon = new Material(seam.sharedMaterial) { name = "Neon Seam" };
                    seamNeon.EnableKeyword("_EMISSION");
                    seamNeon.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                if (seamNeon != null) seam.sharedMaterial = seamNeon;
                seam.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", colour);
                properties.SetColor("_EmissionColor", colour * 2.4f);
                seam.SetPropertyBlock(properties);
            }
        }
    }

    // Walking lights the ground. Each completed stride drops a neon pool at the foot that fades
    // over about a second, and if that foot landed on a placed bridge tile, the tile's own light
    // surges with it. The pool is fixed, so a long walk allocates nothing.
    public sealed class Footlights : MonoBehaviour
    {
        const int Pool = 8;
        const float Life = 1.2f;
        class Print { public Transform root; public Light lamp; public Renderer disc; public MaterialPropertyBlock block; public float age = Life; public Color ink; }
        readonly List<Print> prints = new List<Print>();
        int next;
        BoxPlayer player;
        Light flareLamp;
        float flare, flareIntensity, flareRange;

        public static void Attach(BoxPlayer player)
        {
            if (player == null) return;
            var host = player.GetComponent<Footlights>();
            if (host == null) host = player.gameObject.AddComponent<Footlights>();
            host.enabled = true;
        }
        void OnEnable()
        {
            player = GetComponent<BoxPlayer>();
            if (player != null) player.Stepped += Land;
        }
        void OnDisable()
        {
            if (player != null) player.Stepped -= Land;
            Release();
        }
        Print Borrow()
        {
            if (prints.Count < Pool)
            {
                var root = new GameObject("Footfall " + prints.Count).transform;
                var lampObject = new GameObject("Footfall lamp", typeof(Light));
                lampObject.transform.SetParent(root, false);
                lampObject.transform.localPosition = Vector3.up * .55f;
                var lamp = lampObject.GetComponent<Light>();
                lamp.type = LightType.Point; lamp.shadows = LightShadows.None; lamp.range = 4.5f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Footfall glow"; Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(root, false);
                // Flat on the ground, lifted a little so it never fights the floor for depth.
                quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
                quad.transform.localPosition = Vector3.up * .04f;
                quad.transform.localScale = Vector3.one * 2.6f;
                var disc = quad.GetComponent<Renderer>();
                disc.sharedMaterial = WorldDressing.Additive();
                disc.shadowCastingMode = ShadowCastingMode.Off; disc.receiveShadows = false;
                var print = new Print { root = root, lamp = lamp, disc = disc, block = new MaterialPropertyBlock() };
                prints.Add(print);
                return print;
            }
            // Oldest first, so a fast walk recycles the print that has already faded furthest.
            var reuse = prints[next]; next = (next + 1) % Pool;
            return reuse;
        }
        void Land(Vector3 foot)
        {
            var game = GameManager.Instance;
            if (game == null) return;
            var tile = game.BlockUnder(foot);
            // The footfall takes the colour of the tile it landed on, so a crossing reads as one
            // gradient rather than the path and the walker disagreeing about the palette.
            var ink = tile != null ? WorldDressing.StepColor(tile.gap, tile.row) : new Color(.42f, .92f, 1f);
            var print = Borrow();
            print.age = 0; print.ink = ink;
            print.root.position = new Vector3(foot.x, foot.y + .02f, foot.z);
            print.root.gameObject.SetActive(true);
            if (tile == null) { Restore(); return; }
            var lamp = tile.transform.Find("Step light");
            var light = lamp != null ? lamp.GetComponent<Light>() : null;
            if (light == null) { Restore(); return; }
            if (light != flareLamp) { Restore(); flareLamp = light; flareIntensity = light.intensity; flareRange = light.range; }
            flare = 1;
        }
        // Hand the previous tile back at exactly the values Recalculate gave it.
        void Restore()
        {
            if (flareLamp != null) { flareLamp.intensity = flareIntensity; flareLamp.range = flareRange; }
            flareLamp = null; flare = 0;
        }
        void Release()
        {
            Restore();
            foreach (var print in prints) if (print.root != null) Destroy(print.root.gameObject);
            prints.Clear(); next = 0;
        }
        void Update()
        {
            // Clamped like the skill tree's animation clock: one slow frame - loading, alt-tab, a
            // breakpoint - must not advance a 1.2 second fade to its end and wipe every footfall.
            float step = Mathf.Min(.05f, Time.deltaTime);
            foreach (var print in prints)
            {
                if (print.age >= Life) { if (print.root.gameObject.activeSelf) print.root.gameObject.SetActive(false); continue; }
                print.age += step;
                float fade = Mathf.Clamp01(1 - print.age / Life);
                float eased = fade * fade;
                print.lamp.color = print.ink;
                print.lamp.intensity = eased * 3.4f;
                print.lamp.range = 3.2f + eased * 2.2f;
                print.disc.GetPropertyBlock(print.block);
                var tint = print.ink; tint.a = eased * .8f;
                print.block.SetColor("_BaseColor", tint);
                print.disc.SetPropertyBlock(print.block);
                print.root.localScale = Vector3.one * (.7f + (1 - eased) * .6f);
            }
            if (flareLamp == null) return;
            flare = Mathf.Max(0, flare - step / Life);
            float surge = flare * flare;
            flareLamp.intensity = flareIntensity * (1 + surge * 1.6f);
            flareLamp.range = flareRange * (1 + surge * .35f);
            if (flare <= 0) Restore();
        }
    }

    // A TextMesh seen from 22 metres is a 48 px raster smeared over a handful of screen pixels.
    // These labels live on the HUD canvas instead, so they rasterise at their real pixel size.
    public sealed class WorldLabels : MonoBehaviour
    {
        class Entry { public RectTransform rect; public UnityEngine.UI.Text text; public UnityEngine.UI.Outline outline; public Vector3 anchor; }
        readonly List<Entry> entries = new List<Entry>();
        public float fadeDistance = 54f;
        static readonly Color Ink = new Color(.98f, .84f, .56f);

        public static void Adopt()
        {
            var hud = GameHUD.Current;
            if (hud == null || hud.WorldLabelRoot == null) return;
            var host = hud.WorldLabelRoot.GetComponent<WorldLabels>();
            if (host == null) host = hud.WorldLabelRoot.gameObject.AddComponent<WorldLabels>();
            foreach (var mesh in Object.FindObjectsByType<TextMesh>())
            {
                host.Add(mesh.text, mesh.transform.position, hud.LabelFont);
                mesh.gameObject.SetActive(false);
            }
        }
        public void Add(string value, Vector3 anchor, Font font)
        {
            var rect = new GameObject("World label - " + value, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(300, 32);
            var text = rect.gameObject.AddComponent<UnityEngine.UI.Text>();
            text.font = font; text.text = value; text.fontSize = 19; text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter; text.color = Ink; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(0, 0, 0, .9f); outline.effectDistance = new Vector2(1.4f, -1.4f);
            entries.Add(new Entry { rect = rect, text = text, outline = outline, anchor = anchor + Vector3.up * 1.1f });
        }
        // The scene has no camera tagged MainCamera, so Camera.main is null here and
        // every label would silently stop tracking. Ask the game for its rig instead.
        public static Camera View()
        {
            var game = GameManager.Instance;
            if (game != null && game.cameraRig != null)
            {
                var rig = game.cameraRig.GetComponent<Camera>();
                if (rig != null) return rig;
            }
            return Camera.main;
        }
        void LateUpdate()
        {
            var camera = View();
            var canvas = GetComponentInParent<Canvas>();
            if (camera == null || canvas == null) return;
            float scale = canvas.scaleFactor <= 0 ? 1 : canvas.scaleFactor;
            foreach (var entry in entries)
            {
                var view = camera.WorldToScreenPoint(entry.anchor);
                float distance = Vector3.Distance(camera.transform.position, entry.anchor);
                // Fade with distance, and again near the frame edge so a clamped label never
                // sits pinned to the border shouting about something you cannot see.
                float edge = Mathf.Min(Mathf.Min(view.x, Screen.width - view.x), Mathf.Min(view.y, Screen.height - view.y));
                float alpha = view.z < 1f ? 0 : Mathf.Clamp01((fadeDistance - distance) / 12f) * Mathf.Clamp01(edge / 80f);
                var color = Ink; color.a = alpha; entry.text.color = color;
                var shade = entry.outline.effectColor; shade.a = .9f * alpha; entry.outline.effectColor = shade;
                if (alpha <= 0) { entry.rect.position = new Vector3(Screen.width * .5f, Screen.height * .5f, 0); continue; }
                // Keep every label fully inside the frame: half-offscreen text reads as a glitch.
                Vector2 half = entry.rect.sizeDelta * .5f * scale;
                entry.rect.position = new Vector3(
                    Mathf.Clamp(view.x, half.x + 6, Screen.width - half.x - 6),
                    Mathf.Clamp(view.y, half.y + 6, Screen.height - half.y - 6), 0);
            }
        }
    }
}
