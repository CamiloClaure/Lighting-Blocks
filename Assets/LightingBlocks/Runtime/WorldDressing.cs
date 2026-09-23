using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightingBlocks
{
    // The playable islands are authored by the editor setup scripts. This pass rebuilds them at
    // their full size and fills the rest of the frame at runtime so the camera never looks at
    // empty background: wide courts you have to drag the camera across, rock keels hanging off
    // each one into the void, a few far-off islets for depth, neon paving, a light on every
    // bridge step, and crisp labels.
    public static class WorldDressing
    {
        // Both rifts are carved past the full width of the courts: a gap has to read as a chasm
        // splitting the world, not as a notch you could walk around. Full depth to 58, gone by 74.
        public const float RiftHalfWidth = 74f;
        // Follows the islands: 50.5 in the shipped layout, which is where the old literal 51 sat.
        static Vector2 Centre => new Vector2(0, Landscape.CentreZ);
        // Shared with the editor bake so a baked scene and a runtime rebuild light the same way.
        // public static readonly Color PavingEmission = new Color(1.7f, 1.7f, 1.85f);
        static Texture2D halo;
        static Material seamNeon, additive;
        static MaterialPropertyBlock glowProperties;
        static Mesh box, shaft;

        public static void Build(GameManager game)
        {
            if (game == null || game.blocksRoot == null) return;
            var world = game.blocksRoot.parent;
            var existing = world.Find("Immersive Deeps");
            if (existing != null) ForestWorld.Remove(existing.gameObject);
            var root = new GameObject("Immersive Deeps").transform;
            root.SetParent(world, false);
            Reshape(world);
            BuildBackgroundIslands(root, world);
            RimRocks(root, world);
            var configuration = ForestWorld.For(game)?.configuration;
            SpiritPaving(root, world, configuration);
            RimSteps(root, world);
            SpiritRuins(root, world, configuration);
            VoidShafts(root);
            Lanterns(root, world);
            // ForestWorld owns all vegetation and wildlife.
            Atmosphere(game);
            WorldLabels.Adopt();
            // Occupancy lighting owns the crossing, and nothing lights up from a footfall on the islands.
            if (!BridgeProfile.Current.occupancyLight) Footlights.Attach(game.player);
            else
            {
                var steps = game.GetComponent<BridgeSteps>();
                if (steps == null) steps = game.gameObject.AddComponent<BridgeSteps>();
                steps.enabled = true;
            }
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
        // The islands float over a starfield, so every court needs a rock keel under it instead of
        // a short skirt that meets a valley floor. Six rings pulled in towards the centroid and a
        // capped point give the silhouette, the whole drop scaled to the width of the polygon;
        // ring 0 is untouched because it is the collision rim CanStand and the MeshCollider read.
        static readonly float[] KeelInset = { 1f, .92f, .78f, .60f, .40f, .20f };
        static readonly float[] KeelDrop = { 0f, -3f, -9f, -17f, -27f, -36f };
        const float KeelTip = -40f;
        public static Mesh CliffMesh(Vector2[] points, int index)
        {
            int count = points.Length, rings = KeelInset.Length;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            Vector2 centre = Centroid(points);
            // A 40 m court earns the full 40 m keel; a 12 m islet given the same drop reads as a
            // spike. Scale the depth (never the insets) by how wide the polygon actually is.
            float mean = 0;
            foreach (var p in points) mean += Vector2.Distance(centre, p);
            mean /= count;
            float depth = Mathf.Clamp01(mean / 35f);
            var ring = new Vector3[rings][];
            for (int r = 0; r < rings; r++)
            {
                ring[r] = new Vector3[count];
                for (int i = 0; i < count; i++) ring[r][i] = KeelVertex(points[i], centre, i, r, index, depth);
            }
            for (int r = 0; r < rings - 1; r++)
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count, n = vertices.Count;
                    Vector3 a = ring[r][i], b = ring[r][j], below = ring[r + 1][j], under = ring[r + 1][i];
                    vertices.AddRange(new[] { a, b, below, under });
                    float span = Vector2.Distance(points[i], points[j]) / 6;
                    // World-metre tiling down the keel as well as along it, or the cliff texture
                    // gets stretched over forty metres of rock.
                    uv.AddRange(new[] { new Vector2(0, -a.y / 6), new Vector2(span, -b.y / 6),
                        new Vector2(span, -below.y / 6), new Vector2(0, -under.y / 6) });
                    triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
                }
            // Cap, wound the opposite way round to FloorMesh's fan so the face points down.
            int tip = vertices.Count;
            vertices.Add(new Vector3(centre.x, KeelTip * depth, centre.y)); uv.Add(centre / 6);
            // ponytail: planar uv on the cap, nobody gets under a floating island to see the seam.
            for (int i = 0; i < count; i++) { var p = ring[rings - 1][i]; vertices.Add(p); uv.Add(new Vector2(p.x, p.z) / 6); }
            for (int i = 0; i < count; i++) triangles.AddRange(new[] { tip, tip + i + 1, tip + (i + 1) % count + 1 });
            return Finish(vertices, uv, triangles, "Cliff " + index);
        }
        // Deterministic wobble keyed on the point index, never on the vertex list position: the
        // corner shared by two quads has to move the same way in both or the keel tears open.
        static Vector3 KeelVertex(Vector2 p, Vector2 centre, int i, int r, int index, float depth)
        {
            if (r == 0) return new Vector3(p.x, 0, p.y);
            float inset = KeelInset[r] + (Mathf.PerlinNoise(i * .37f + r * 5.13f + index * 13.7f, r * 2.31f + index * 3.17f + .7f) - .5f) * .16f;
            // The wobble scales with the drop too, or a shallow keel gets a 1.5 m ripple on a 10 m fall.
            float y = (KeelDrop[r] + (Mathf.PerlinNoise(i * .53f + r * 3.91f + index * 7.31f + 31.7f, r * 1.73f + index * 5.91f + 17.3f) - .5f) * 3f) * depth;
            return new Vector3(centre.x + (p.x - centre.x) * inset, y, centre.y + (p.y - centre.y) * inset);
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

        // ---- the void --------------------------------------------------------

        // There is no ground under the courts any more, only the starfield skybox. Depth comes
        // from the keels hanging off each island and from a handful of islets far out in the dark.

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
        // Small islands drifting out past the play area, purely to give the void a sense of scale.
        // Scenery only: no colliders, never saved, rebuilt identically every Play from one seed.
        static readonly List<(Vector2[] points, Vector3 origin)> islets = new List<(Vector2[] points, Vector3 origin)>();
        public static void BuildBackgroundIslands(Transform root, Transform world)
        {
            islets.Clear();
            var random = new System.Random(2026);
            var radii = new List<float>();
            var top = SceneMaterial(world, "ShoreFloor0"); var rock = SceneMaterial(world, "ShoreCliff0");
            for (int attempt = 0; attempt < 400 && islets.Count < 6; attempt++)
            {
                // Scaled off the world's own length, not the shipped 80 + 80: a profile that pushes
                // the islands apart would otherwise ring only the middle and leave the far court
                // against empty void. Landscape.Span is 217 in the shipped layout, so this is the
                // same 80.3 + 80.3 it always was.
                float reach = Landscape.Span * .37f;
                float angle = (float)random.NextDouble() * Mathf.PI * 2, distance = reach + (float)random.NextDouble() * reach;
                float cx = Centre.x + Mathf.Cos(angle) * distance, cz = Centre.y + Mathf.Sin(angle) * distance;
                float r = 10 + (float)random.NextDouble() * 12;
                // Never crowd a court, never block the view down a rift, never overlap a sibling.
                if (ShoreDistance(cx, cz) < r + 15) continue;
                // The no-islet band over each crossing, measured off the crossing itself. In the
                // shipped layout this is exactly the old pair of literals: 1 to 27 and 80 to 106.
                bool overRift = false;
                for (int gap = 0; gap < 2 && !overRift; gap++)
                    overRift = cz > Landscape.BridgeStart(gap) - 7 && cz < Landscape.BridgeStart(gap) + GameManager.Rows * 2 + 5;
                if (Mathf.Abs(cx) < RiftHalfWidth && overRift) continue;
                bool crowded = false;
                for (int k = 0; k < islets.Count && !crowded; k++)
                    crowded = Vector2.Distance(new Vector2(cx, cz), new Vector2(islets[k].origin.x, islets[k].origin.z)) < r + radii[k] + 12;
                if (crowded) continue;
                int sides = 8 + random.Next(3);
                var points = new Vector2[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = (i * Mathf.PI * 2 / sides) + ((float)random.NextDouble() - .5f) * 30 * Mathf.Deg2Rad;
                    float d = r * (.7f + (float)random.NextDouble() * .3f);
                    points[i] = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
                }
                int index = islets.Count;
                var origin = new Vector3(cx, -22 + (float)random.NextDouble() * 14, cz);
                var obj = new GameObject("Islet " + index);
                obj.transform.SetParent(root, false); obj.transform.position = origin;
                Islet(obj.transform, "Top", FloorMesh(points, 100 + index), top);
                Islet(obj.transform, "Keel", CliffMesh(points, 100 + index), rock);
                islets.Add((points, origin));
                radii.Add(r);
            }
        }
        static GameObject Islet(Transform parent, string name, Mesh mesh, Material material)
        {
            var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            // Tier 3 masks the Wisdom Tree lights off anything on this layer.
            renderer.renderingLayerMask = 2;
            return obj;
        }
        // Ground height over a background islet, so scenery can be planted on one. Zero and false
        // everywhere else, because everywhere else is the void.
        public static bool BackgroundHeight(float x, float z, out float y)
        {
            foreach (var islet in islets)
                if (Landscape.Inside(new Vector2(x - islet.origin.x, z - islet.origin.z), islet.points))
                { y = islet.origin.y; return true; }
            y = 0; return false;
        }
        // The islets borrow whatever the courts are already wearing, so one material change in the
        // scene carries to the background too.
        static Material SceneMaterial(Transform world, string name)
        {
            var obj = Find(world, name);
            var renderer = obj != null ? obj.GetComponent<Renderer>() : null;
            return renderer != null && renderer.sharedMaterial != null
                ? renderer.sharedMaterial
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }

        // ---- the rim ---------------------------------------------------------

        // A court that ends in a clean polygon edge reads as a cut-out. Rocks along every shore
        // and a lantern every so often give the rim a silhouette and something to light it.
        // Scenery only: no colliders, one seed, rebuilt with the rest of Immersive Deeps.

        // The bridge lanes have to stay clear of both, or the crossing you are aiming at is
        // hidden behind a boulder. Same band the background islands avoid.
        // Measured off the crossings themselves, so a profile that lengthens them keeps its landing
        // approaches clear. In the shipped layout this is the old pair of literals: 1 to 27, 80 to 106.
        static bool RiftLane(Vector2 p) => Mathf.Abs(p.x) < 8 && OverRift(p.y);
        // True inside either crossing's band, with the metre or so of landing at each end.
        public static bool OverRift(float z)
        {
            for (int gap = 0; gap < 2; gap++)
                if (z > Landscape.BridgeStart(gap) - 7 && z < Landscape.BridgeStart(gap) + GameManager.Rows * 2 + 5) return true;
            return false;
        }
        static void RimRocks(Transform root, Transform world)
        {
            var random = new System.Random(4127);
            // ponytail: rocks sit in the veil at eye level, the shared cliff material reads black there; one lighter clone, tuned by eye.
            var rock = new Material(SceneMaterial(world, "ShoreCliff0")) { name = "Rim rock" };
            if (rock.HasProperty("_BaseColor")) { var c = rock.GetColor("_BaseColor"); rock.SetColor("_BaseColor", new Color(Mathf.Min(1, c.r * 1.6f), Mathf.Min(1, c.g * 1.6f), Mathf.Min(1, c.b * 1.7f), c.a)); }
            if (rock.HasProperty("_SoftFill")) rock.SetFloat("_SoftFill", .28f);
            if (rock.HasProperty("_RimColor")) rock.SetColor("_RimColor", new Color(.42f, .36f, .66f));
            // Four shapes shared by every rock: yaw, tilt and scale do the rest of the variety.
            var shapes = new Mesh[4];
            for (int i = 0; i < shapes.Length; i++) shapes[i] = RockMesh(random);
            foreach (var shore in Landscape.Shores)
            {
                Vector2 centre = Centroid(shore);
                for (int i = 0, j = shore.Length - 1; i < shore.Length; j = i++)
                {
                    Vector2 a = shore[j], b = shore[i];
                    float length = Vector2.Distance(a, b);
                    for (float t = 3.5f + (float)random.NextDouble() * 1.5f; t < length; t += 3.5f + (float)random.NextDouble() * 1.5f)
                    {
                        Vector2 p = Vector2.Lerp(a, b, t / length);
                        if (RiftLane(p)) continue;
                        // ponytail: outward is the direction from the centroid, not the edge
                        // normal. On these near-convex courts the two agree to a few degrees.
                        Vector2 away = (p - centre).normalized * .3f;
                        var obj = Islet(root, "Rim rock", shapes[random.Next(shapes.Length)], rock);
                        // A rock sitting on the rim has to take the Wisdom Tree light the paving
                        // beside it takes, or it reads as a hole. Only the far islets stay masked.
                        obj.GetComponent<Renderer>().renderingLayerMask = 1;
                        obj.transform.position = new Vector3(p.x + away.x, -.25f, p.y + away.y);
                        obj.transform.rotation = Quaternion.Euler(
                            (float)random.NextDouble() * 24 - 12,
                            (float)random.NextDouble() * 360,
                            (float)random.NextDouble() * 24 - 12);
                        obj.transform.localScale = Vector3.one * (.6f + (float)random.NextDouble() * .8f);
                    }
                }
            }
        }
        // An icosahedron subdivided once - 42 vertices - with every vertex pulled in a random
        // amount, then split into loose triangles so RecalculateNormals shades it faceted.
        static Mesh RockMesh(System.Random random)
        {
            const float t = 1.618034f;
            var points = new List<Vector3> {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
            int[] faces = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var middles = new Dictionary<int, int>();
            var indices = new List<int>();
            for (int f = 0; f < faces.Length; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Middle(points, middles, a, b), bc = Middle(points, middles, b, c), ca = Middle(points, middles, c, a);
                indices.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            // Jitter the shared vertices, never the split copies: a corner has to move the same
            // way in every face that touches it or the rock opens up along the seam.
            for (int i = 0; i < points.Count; i++) points[i] *= .7f + (float)random.NextDouble() * .3f;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            foreach (var index in indices)
            {
                var p = points[index];
                triangles.Add(vertices.Count); vertices.Add(p);
                // ponytail: planar uv, a rock this small never shows the stretch on its sides.
                uv.Add(new Vector2(p.x, p.z) * .5f + Vector2.one * .5f);
            }
            return Finish(vertices, uv, triangles, "Rim rock");
        }
        // Midpoint of an edge, created once and shared by both faces that own it.
        static int Middle(List<Vector3> points, Dictionary<int, int> cache, int a, int b)
        {
            int key = a < b ? a * 64 + b : b * 64 + a;
            if (cache.TryGetValue(key, out int found)) return found;
            points.Add(((points[a] + points[b]) * .5f).normalized);
            cache[key] = points.Count - 1;
            return points.Count - 1;
        }
        // ---- pack dressing ---------------------------------------------------

        // Nothing in the scene references the art pack's ENV meshes and materials, and there is no
        // Resources copy of them, so the only loader this project owns is ArtIntegration's
        // AssetDatabase lookup. The runtime borrows the same one behind the editor guard.
        // ponytail: editor-only pack lookup. Ceiling: a player build dresses with the plain boxes
        // below and the scene's own materials. Copy the pack under Resources, or hang the two
        // meshes off ForestConfiguration, the day a standalone build has to match the editor.
        static T Pack<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>("Assets/LightingBlocks/Art/Pack/" + path);
#else
            return null;
#endif
        }
        // A unit cube built the same way the rocks are, shared by the rim steps, the ruin lintel
        // and both pack fallbacks: size comes off the transform, so one mesh serves all of them.
        static readonly Vector3[] BoxFaces = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        static Mesh UnitBox()
        {
            if(box!=null)return box;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            foreach(var face in BoxFaces)
            {
                // u is the axis cycled once, which is never parallel to the face normal, so u and
                // v span the face whichever way it points.
                var u=new Vector3(face.y,face.z,face.x);var v=Vector3.Cross(face,u);int n=vertices.Count;
                foreach(var corner in new[]{-u-v,u-v,u+v,-u+v})vertices.Add((face+corner)*.5f);
                uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
                triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            return box=Finish(vertices,uv,triangles,"Dressing box");
        }
        // Two quads crossed at right angles, one metre wide and hanging one metre down, so a shaft
        // still reads as a volume from any camera angle and the real size comes off the transform.
        static Mesh Shaft()
        {
            if(shaft!=null)return shaft;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            foreach(var axis in new[]{Vector3.right,Vector3.forward})
            {
                int n=vertices.Count;var side=axis*.5f;
                vertices.AddRange(new[]{-side,side,side+Vector3.down,-side+Vector3.down});
                uv.AddRange(new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),new Vector2(0,0)});
                triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            return shaft=Finish(vertices,uv,triangles,"Void shaft");
        }
        // Dressing standing on a court has to take the Wisdom Tree light the paving beside it
        // takes; only the far islets and the shafts stay on the masked layer.
        static GameObject Lit(Transform root, string name, Mesh mesh, Material material)
        {
            var obj=Islet(root,name,mesh,material);
            obj.GetComponent<Renderer>().renderingLayerMask=1;
            return obj;
        }
        // A broken ring of pack paving around every Wisdom Tree, so the hero of each island stands
        // on something authored instead of on bare ground.
        static void SpiritPaving(Transform root, Transform world, ForestConfiguration configuration)
        {
            if(configuration==null||configuration.islands==null)return;
            var random=new System.Random(3571);
            var paving=Pack<Material>("Materials/M_ENV_Paving.mat");if(paving==null)paving=SceneMaterial(world,"ShoreFloor0");
            var slab=Pack<Mesh>("Meshes/ENV_PavingModule.asset");var slabSize=Vector3.one;
            if(slab==null){slab=UnitBox();slabSize=new Vector3(2,.3f,2);}
            foreach(var d in configuration.islands)
            {
                int count=8+random.Next(5);
                for(int i=0;i<count;i++)
                {
                    // Even spokes with a little play on each, or the ring reads as a machined disc.
                    float angle=(i+((float)random.NextDouble()-.5f)*.4f)*Mathf.PI*2/count;
                    float radius=2+(float)random.NextDouble()*2;
                    var stone=Lit(root,"Spirit paving",slab,paving);
                    stone.transform.position=new Vector3(d.wisdomPosition.x+Mathf.Cos(angle)*radius,.02f,d.wisdomPosition.z+Mathf.Sin(angle)*radius);
                    stone.transform.rotation=Quaternion.Euler(0,angle*Mathf.Rad2Deg,0);
                    stone.transform.localScale=Vector3.Scale(slabSize,Vector3.one*(.5f+(float)random.NextDouble()*.3f));
                }
            }
        }
        // Where a crossing meets a shore the rim just stops. Three slabs stepping down past the
        // edge give the landing somewhere to come from. The bridge cells run from BridgeStart to
        // BridgeStart + 12, and the two courts either side end a metre short of each: four
        // landings, all on the lane axis.
        static void RimSteps(Transform root, Transform world)
        {
            var cliff=SceneMaterial(world,"ShoreCliff0");
            for(int gap=0;gap<2;gap++)
                foreach(int side in new[]{1,-1})
                {
                    // side is the direction out into the gap, so the near shore steps forward and
                    // the far shore steps back towards it.
                    float edge=side>0?Landscape.BridgeStart(gap)-1:Landscape.BridgeStart(gap)+GameManager.Rows*2-1;
                    for(int n=0;n<3;n++)
                    {
                        var step=Lit(root,"Rim step",UnitBox(),cliff);
                        // The first tread starts below a placed block's underside at -.36, so no
                        // step ever pokes up through the crossing it leads onto.
                        step.transform.position=new Vector3(0,-.5f-.25f*n,edge+side*(.7f+n*.8f));
                        step.transform.localScale=new Vector3(5.2f-n*.7f,.22f,.8f);
                    }
                }
        }
        // The middle court carries the ruins. One island's worth of broken columns reads as a
        // place that used to be lived in; three islands' worth reads as set dressing.
        static void SpiritRuins(Transform root, Transform world, ForestConfiguration configuration)
        {
            if(configuration==null||configuration.islands==null||configuration.islands.Length<2)return;
            var d=configuration.islands[1];
            if(d.polygonIndex<0||d.polygonIndex>=Landscape.Shores.Length)return;
            var shore=Landscape.Shores[d.polygonIndex];
            var random=new System.Random(6151);
            var stone=Pack<Material>("Materials/M_ENV_Paving.mat");if(stone==null)stone=SceneMaterial(world,"ShoreFloor0");
            var drum=Pack<Mesh>("Meshes/ENV_ColumnStone.asset");var drumSize=Vector3.one;
            if(drum==null){drum=UnitBox();drumSize=new Vector3(.85f,.8f,.85f);}
            var min=shore[0];var max=min;
            foreach(var q in shore){min=Vector2.Min(min,q);max=Vector2.Max(max,q);}
            var spots=new List<Vector3>();
            for(int attempt=0;attempt<400&&spots.Count<6;attempt++)
            {
                var p=new Vector3(Mathf.Lerp(min.x,max.x,(float)random.NextDouble()),0,Mathf.Lerp(min.y,max.y,(float)random.NextDouble()));
                // Same rejection the rim rocks run, plus the two reservations the forest owns: a
                // column in the hauling lane or over a particle output is a column in the way.
                if(RiftLane(new Vector2(p.x,p.z)))continue;
                if(!ForestLayoutGenerator.Inside(p,shore,4)||ForestLayoutGenerator.Reserved(p,d,6)||ForestHabitat.MainPath(p,d))continue;
                bool crowded=false;
                foreach(var other in spots)if((other-p).sqrMagnitude<64)crowded=true;
                if(crowded)continue;
                spots.Add(p);
            }
            for(int i=0;i<spots.Count;i++)
            {
                var p=spots[i];
                float yaw=(float)random.NextDouble()*360;
                if(i<5)
                {
                    // Tilt on one axis under a yaw, never Euler x and z together: two six degree
                    // angles compound to eight and the column stops reading as merely settled.
                    float height=.6f+(float)random.NextDouble()*.4f,tilt=(float)random.NextDouble()*12-6;
                    var column=Lit(root,"Spirit ruin",drum,stone);
                    column.transform.position=new Vector3(p.x,.4f*height,p.z);
                    column.transform.rotation=Quaternion.Euler(0,yaw,0)*Quaternion.Euler(tilt,0,0);
                    column.transform.localScale=Vector3.Scale(drumSize,new Vector3(1,height,1));
                    continue;
                }
                // The arch is one Spirit ruin carrying three pieces, so the ruin count stays at
                // the six the rest of the world is asserted against.
                var arch=new GameObject("Spirit ruin").transform;
                arch.SetParent(root,false);
                arch.position=p;arch.rotation=Quaternion.Euler(0,yaw,0);
                foreach(int leg in new[]{-1,1})
                {
                    var post=Lit(arch,"Arch column",drum,stone);
                    post.transform.localPosition=new Vector3(leg*.95f,.8f,0);
                    post.transform.localScale=Vector3.Scale(drumSize,new Vector3(1,2,1));
                }
                var lintel=Lit(arch,"Arch lintel",UnitBox(),stone);
                lintel.transform.localPosition=new Vector3(0,1.75f,0);
                lintel.transform.localScale=new Vector3(2.7f,.3f,.8f);
            }
        }
        // Light spilling off every rim and falling away into the dark, which is what tells the eye
        // the courts are floating rather than sitting on something it cannot see.
        static void VoidShafts(Transform root)
        {
            var found=Shader.Find("LightingBlocks/Light Shaft");
            // ponytail: the URP Unlit fallback paints the quad flat and opaque - no gradient, no
            // additive blend. It is only here so a run before LightShaft.shader imports never
            // throws; the look needs the real shader.
            if(found==null)found=Shader.Find("Universal Render Pipeline/Unlit");
            if(found==null)return;
            var ink=new Color(.5f,.4f,1f)*.35f;
            var glow=new Material(found){name="Void shaft"};
            if(glow.HasProperty("_Shaft"))glow.SetColor("_Shaft",ink);
            if(glow.HasProperty("_BaseColor"))glow.SetColor("_BaseColor",ink);
            var random=new System.Random(5309);
            var placed=new List<Vector2>();
            foreach(var shore in Landscape.Shores)
            {
                Vector2 centre=Centroid(shore);
                placed.Clear();
                int count=4+random.Next(3);
                for(int attempt=0;attempt<200&&placed.Count<count;attempt++)
                {
                    int e=random.Next(shore.Length);
                    Vector2 a=shore[(e+shore.Length-1)%shore.Length],b=shore[e];
                    Vector2 p=Vector2.Lerp(a,b,(float)random.NextDouble());
                    if(RiftLane(p))continue;
                    bool crowded=false;
                    foreach(var other in placed)if(Vector2.Distance(other,p)<10)crowded=true;
                    if(crowded)continue;
                    placed.Add(p);
                    Vector2 away=(p-centre).normalized*.4f;
                    // ponytail: shaft area is pure additive overdraw and it dominates the GPU frame.
                    // At 35 m x 3-6 m the profile ran mean GPU 14.9 ms against a 6.1 ms budget, so
                    // the beams are kept short and narrow. Widen only with a profile run beside you.
                    float width=2+(float)random.NextDouble()*2;
                    var beam=Islet(root,"Void shaft",Shaft(),glow);
                    var renderer=beam.GetComponent<Renderer>();
                    renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                    beam.transform.position=new Vector3(p.x+away.x,0,p.y+away.y);
                    beam.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*90,0);
                    beam.transform.localScale=new Vector3(width,16,width);
                }
            }
        }

        // The scene already holds six soul-well lanterns. Cloning one around every court rim
        // lights the edge you are about to walk off, and the clones cost nothing to author.
        static void Lanterns(Transform root, Transform world)
        {
            GameObject template = null;
            foreach (var light in world.GetComponentsInChildren<Light>(true))
                if (light.name == "Courtyard lantern glow" && light.transform.parent != null)
                    { template = light.transform.parent.gameObject; break; }
            if (template == null) return;
            var random = new System.Random(8613);
            foreach (var shore in Landscape.Shores)
            {
                Vector2 centre = Centroid(shore);
                // One walk around the whole perimeter, so the spacing carries across corners.
                float next = (float)random.NextDouble() * 26, walked = 0;
                for (int i = 0, j = shore.Length - 1; i < shore.Length; j = i++)
                {
                    Vector2 a = shore[j], b = shore[i];
                    float length = Vector2.Distance(a, b);
                    for (; next < walked + length; next += 26)
                    {
                        Vector2 p = Vector2.Lerp(a, b, (next - walked) / length);
                        if (RiftLane(p)) continue;
                        // ponytail: no wisdom-tree spacing check. Landscape has no wisdom
                        // position - it lives on ForestConfiguration, which is not built yet -
                        // and every authored tree sits 10 m or more inside its rim anyway.
                        Vector2 inward = (centre - p).normalized;
                        var clone = Object.Instantiate(template, root);
                        clone.name = "Rim lantern";
                        clone.transform.position = new Vector3(p.x + inward.x * 1.2f, 0, p.y + inward.y * 1.2f);
                        clone.transform.rotation = Quaternion.LookRotation(new Vector3(inward.x, 0, inward.y));
                        clone.transform.localScale = template.transform.localScale;
                        // Block placement and the tests read the world through physics overlaps,
                        // so dressing may never own a collider.
                        foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) ForestWorld.Remove(collider);
                    }
                    walked += length;
                }
            }
            foreach (var light in Object.FindObjectsByType<Light>())
                if (light.name == "Courtyard lantern glow") Lantern(light);
        }
        // Tier 3 lantern: a warm ember that pools on the paving around it and lights nothing
        // further out, so thirty of them still cost less than one shadowed lamp.
        public static void Lantern(Light light)
        {
            if (light == null) return;
            light.color = new Color(1f, .714f, .380f);   // #FFB661
            light.intensity = 4; light.range = 6; light.shadows = LightShadows.None;
        }

        static void Atmosphere(GameManager game)
        {
            var haze = new Color(.062f, .072f, .098f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            var config = ForestWorld.For(game)?.configuration;
            RenderSettings.fogColor = config != null ? config.haze : haze; RenderSettings.fogStartDistance = config != null ? config.fogStart : 45; RenderSettings.fogEndDistance = config != null ? config.fogEnd : 145;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=config!=null?config.ambientSky:new Color(.22f,.27f,.35f);RenderSettings.ambientEquatorColor=config!=null?config.ambientEquator:new Color(.12f,.19f,.2f);RenderSettings.ambientGroundColor=config!=null?config.ambientGround:new Color(.075f,.105f,.125f);
            foreach (var light in Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional) { light.intensity = config!=null?config.moonIntensity:.42f; light.color = config!=null?config.moonColor:new Color(.62f,.74f,.92f); }
            if (game.cameraRig == null) return;
            game.cameraRig.distance = 20f;
            // Let the camera be dragged to every edge of the enlarged courts, and no further.
            var bounds = CourtBounds();
            game.cameraRig.minimumFocus = new Vector2(bounds.min.x + 2, bounds.min.z + 2);
            game.cameraRig.bounds = new Vector2(bounds.max.x - 2, bounds.max.z - 2);
            var camera = game.cameraRig.GetComponent<Camera>();
            if (camera == null) return;
            camera.backgroundColor = config!=null?config.haze:haze; camera.farClipPlane = 420;
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
            var breath = light.GetComponent<Breath>();
            if (breath == null) breath = light.gameObject.AddComponent<Breath>();
            // Phase off where the block sits, so the crossing ripples instead of pulsing as one.
            breath.phase = .7f * (block.transform.position.x + block.transform.position.z);
            if (BridgeProfile.Current.occupancyLight)
            {
                // Occupancy owns the light level here: no connected/unconnected constants, only the fade.
                StepGlow(block, block.lit);
            }
            else
            {
                light.range = block.Connected ? 6.8f : 3.4f;
                light.intensity = block.Connected ? 4.4f : 1f;
                breath.baseIntensity = light.intensity;
            }
            Seams(block, colour);
        }
        // Scales the step light and the emission of a placed block by how lit it is (0 dark, 1 full).
        // Occupancy lighting only; called from StepLight under the profile gate and from BridgeSteps
        // every frame a block's lit amount changes.
        public static void StepGlow(BridgeBlock block, float lit)
        {
            if (block == null) return;
            var existing = block.transform.Find("Step light");
            if (existing != null)
            {
                var light = existing.GetComponent<Light>();
                if (light != null)
                {
                    float intensity = 4.4f * lit;
                    var breath = light.GetComponent<Breath>();
                    // Write through Breath when present, so the swell rides the fade instead of
                    // fighting it. The direct write matters too: Breath skips any frame where the
                    // live intensity is above baseIntensity * 1.13, and a block left at the top of
                    // the swell would sit above every later, smaller base and freeze there, fully
                    // lit, until the fade ended. Resetting the live value keeps that guard false.
                    if (breath != null) breath.baseIntensity = intensity;
                    light.intensity = intensity;
                    light.range = 6.8f * lit;
                }
                // A dark crossing costs no lights.
                existing.gameObject.SetActive(lit > 0f);
            }
            var colour = StepColor(block.gap, block.row);
            var renderer = block.GetComponent<Renderer>();
            // One shared block, reused: a crossing mid-fade calls this for every tile every frame,
            // and a fresh MaterialPropertyBlock per call would be pure per-frame garbage.
            if (glowProperties == null) glowProperties = new MaterialPropertyBlock();
            if (renderer != null)
            {
                var properties = glowProperties;
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_EmissionColor", colour * lit);
                renderer.SetPropertyBlock(properties);
            }
            var art = block.GetComponent<BlockArt>();
            var runes = art != null && art.placedVisual != null ? art.placedVisual.Find("Crystal runes") : null;
            var runesRenderer = runes != null ? runes.GetComponent<Renderer>() : null;
            if (runesRenderer != null)
            {
                // Only _EmissionColor, on a property block: _BaseColor stays white or the rune
                // lines read as dark scratches instead of simply unlit. Same 2.4x Seams uses.
                var properties = glowProperties;
                runesRenderer.GetPropertyBlock(properties);
                properties.SetColor("_EmissionColor", colour * 2.4f * lit);
                runesRenderer.SetPropertyBlock(properties);
            }
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

    // A step light holding one exact value reads as a decal stuck to the tile. A slow twelve
    // percent swell, phased per block, is what makes the crossing look lit rather than painted.
    sealed class Breath : MonoBehaviour
    {
        public float baseIntensity, phase;
        Light lamp;
        void Awake() { lamp = GetComponent<Light>(); }
        void Update()
        {
            if (lamp == null) return;
            // ponytail: the footfall flare drives this same light to 2.6x for about a second.
            // While it is above the swell's own ceiling, leave it alone rather than wire the
            // two effects together - if they ever need to compose, give Footlights the phase.
            if (lamp.intensity > baseIntensity * 1.13f) return;
            lamp.intensity = baseIntensity * (1 + .12f * Mathf.Sin(Time.time * 1.3f + phase));
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
        public float fadeDistance = 135f;
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
                // The top 120 px are spoken for by the HUD title and the toast, so a far label
                // parks under that band instead of at the very edge, where it collided with both.
                Vector2 half = entry.rect.sizeDelta * .5f * scale;
                entry.rect.position = new Vector3(
                    Mathf.Clamp(view.x, half.x + 6, Screen.width - half.x - 6),
                    Mathf.Clamp(view.y, half.y + 6, Screen.height - half.y - 120), 0);
            }
        }
    }
}
