using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Sandbox only. Writes to Art/TerrainScene/ForestTerrain.asset, which is the configuration copy
// that SampleSceneTerrain alone points at, so SampleScene and its 207-check suite never see any of
// this. The guard in Config() refuses to write the shipping Forest.asset even if it is handed one.
namespace LightingBlocks.TerrainScene
{
    // Turns an imported nature pack into tree prefabs this game can actually place.
    //
    // A store tree is not a drop-in replacement for config.trees. Three things have to change:
    //
    //   height   ForestWorld.cs:91 multiplies localScale by .8 to 1.2 and nothing else, so the
    //            prefab is the final size. Store trees come in at 10-20 m; the islands are 25 m
    //            across. Every prefab is rescaled so its bounds are exactly 4.5 m or 6 m, which is
    //            what ForestBuilder.cs:153 does for the shipping trees.
    //   material Their URP Lit does not know about _Wind, the player bend or the fog reveal, so it
    //            reads as a photograph pasted on a ghost. Each source material is remapped onto
    //            LightingBlocks/Spectral Lit, keeping its own textures and taking the palette from
    //            ForestBuilder.cs:133-136.
    //   colliders ForestWorld.cs:92 adds its own .32 m capsule on the trunk. A pack MeshCollider on
    //            top of that catches the player on branches, so theirs are stripped.
    //
    // Pack layout is unknown until it is imported, so nothing here hardcodes a path: Discover()
    // takes every model and prefab outside the folders this project already owns.
    [InitializeOnLoad]
    public static class TerrainFoliage
    {
        const string Folder = "Assets/LightingBlocks/Art/TerrainScene";
        const string Config = Folder + "/ForestTerrain.asset";
        const string Shipping = "Assets/LightingBlocks/Art/SpectralForest/Configuration/Forest.asset";
        const string Request = "Library/LightingBlocks.foliage-request";
        const string Result = "Library/LightingBlocks.foliage-result.txt";

        // Folders this project already owns. Everything else under Assets is imported content.
        static readonly string[] Ours =
        {
            "Assets/LightingBlocks/", "Assets/Models/", "Assets/Animations/",
            "Assets/Resources/", "Assets/Settings/", "Assets/Scenes/",
        };

        // Matched against the asset file name, lowercased. A pack names a fir "Fir_01" or
        // "SM_Tree_Pine_A"; either way one of these lands.
        static readonly string[] TreeWords =
        {
            "tree", "pine", "birch", "oak", "maple", "spruce", "fir", "willow",
            "poplar", "aspen", "cedar", "palm", "beech", "conifer",
        };

        // A leaf card is alpha cut, two sided and moves in wind. A trunk is none of those, and
        // getting it wrong is visible immediately: cutout bark goes see-through in patches.
        static readonly string[] LeafWords =
        {
            "leaf", "leaves", "needle", "frond", "branch", "foliage", "canopy",
            "grass", "plant", "moss", "card", "billboard", "twig",
        };

        // Anything with these in the name is not a tree even when it sits in the same folder.
        static readonly string[] SkipWords =
        {
            "lod_group", "_ref", "demo", "showcase", "preview", "terrain", "skybox", "water",
        };

        // Two heights per model when the pack is short on species. Ultimate Nature Starter ships
        // exactly two trees, and 120 of two silhouettes an island reads as a plantation; the same
        // two at 4.5 and 6.8 m read as young and old growth, which costs nothing. On top of this
        // the generator still rolls its own .8 to 1.2 scale per tree.
        static readonly float[][] Spread = { new[] { 4.5f, 6.8f }, new[] { 5.4f, 7.6f } };

        enum Look { Bark, Leaf, Ground, Glow }

        // Size is the LONGEST axis, not the height. A tree is the one thing whose height is also its
        // longest axis; a grass fan is 4.6x wider than tall and a log is 5x longer than thick, so
        // normalising those by height scales the footprint by that ratio - a 0.6 m grass tuft comes
        // out 2.8 m across and a 0.22 m twig comes out 7.5 m long.
        //
        // Stretch then scales Y alone, which is how one flat grass fan yields three tiers.
        sealed class Slot
        {
            public readonly string Model; public readonly float Size, Stretch; public readonly int Lod; public readonly Look Look;
            public Slot(string model, float size, Look look, float stretch = 1, int lod = 2)
            { Model = model; Size = size; Look = look; Stretch = stretch; Lod = lod; }
        }

        // The slot index IS the variant the generator rolls, and those weights are fixed in
        // ForestConfiguration.cs:118, so the mapping has to earn its place against them:
        //
        //   0  42%   1  26%   2  12%   3  5%   4  5%   5  3%   6  3%   7  2%   8  2%
        //
        // Slot 0 is also forced on every placement within 3.2 m of the hauling path and beside
        // every animal habitat, so it has to be the shortest thing here or the path silts up.
        // Slots 9 and 10 are never scattered: they are the single centrepiece of a habitat grove
        // (ForestConfiguration.cs:135) at scale exactly 1, so they carry the size of a landmark.
        //
        // The pack ships one grass mesh, so slots 0-2 are that fan at three footprints and three
        // stretches, which is what GrassShort/Medium/Tall were doing. Footprints stay under the
        // 1.05 m cover grid (ForestConfiguration.cs:116) so neighbours meet instead of merging.
        // UNS_Flower is the one asset left out: it needs three materials and this renderer draws
        // submesh 0 with one.
        static readonly Slot[] Slots =
        {
            new Slot("UNS_Grass",             .80f, Look.Ground, 1.0f),
            new Slot("UNS_Grass",            1.00f, Look.Ground, 1.7f),
            new Slot("UNS_Grass",            1.25f, Look.Ground, 2.4f),
            new Slot("UNS_Bush",             1.10f, Look.Ground),
            new Slot("UNS_Mushroom_Patch",    .45f, Look.Glow),
            new Slot("UNS_Tiny_Rock_01",      .40f, Look.Bark),
            new Slot("UNS_Tiny_Rock_03",      .30f, Look.Bark),
            new Slot("UNS_Mushroom_Patch",    .30f, Look.Glow),
            new Slot("UNS_Branch",            .55f, Look.Bark),
            new Slot("UNS_Standard_Rock_02", 1.40f, Look.Bark, 1, 1),
            new Slot("UNS_Log",              2.20f, Look.Bark, 1, 1),
        };

        static TerrainFoliage() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            try { File.WriteAllText(Result, Build()); }
            catch (Exception e) { File.WriteAllText(Result, "FAILED\n" + e); Debug.LogException(e); }
        }

        [MenuItem("Tools/Lighting Blocks/Terrain: Rebuild foliage")]
        public static void Rebuild() { Debug.Log(Build()); }

        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play before rebuilding foliage.");
            var log = new StringBuilder();
            Directory.CreateDirectory(Folder + "/Prefabs");
            Directory.CreateDirectory(Folder + "/Materials");
            AssetDatabase.Refresh();

            var catalog = Catalog();
            var sources = Discover(catalog, log);
            if (sources.Count == 0)
                return log + "\nNOTHING TO DO: no imported models found outside this project's own folders.\n" +
                       "Import the nature pack first, then run this again.";

            var config = AssetDatabase.LoadAssetAtPath<ForestConfiguration>(Config);
            if (config == null) throw new Exception("Missing " + Config);
            if (AssetDatabase.GetAssetPath(config) == Shipping) throw new Exception("Refusing to write the shipping configuration.");

            var cache = new Dictionary<string, Material>();
            var atlases = new Dictionary<string, Texture>();
            var built = new List<GameObject>();
            for (int i = 0; i < sources.Count; i++)
            {
                // Four or more species: one prefab each, alternating the two heights
                // ForestBuilder.cs:153 uses. Fewer: two prefabs each, per Spread.
                var heights = sources.Count >= 4 ? new[] { i % 2 == 0 ? 4.5f : 6f } : Spread[i % Spread.Length];
                foreach (var height in heights)
                {
                    var suffix = heights.Length > 1 ? "_" + height.ToString("0.0").Replace('.', '_') + "m" : "";
                    var prefab = Prefab(sources[i], height, suffix, cache, atlases, log);
                    if (prefab != null) built.Add(prefab);
                }
            }
            if (built.Count == 0) return log + "\nNOTHING TO DO: every candidate failed to build, see above.";

            config.trees = built.ToArray();
            Cover(catalog, config, cache, atlases, log);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine();
            log.AppendLine("SUCCESS: " + built.Count + " tree variants written to " + Config);
            log.AppendLine("Materials: " + cache.Count + " remapped onto LightingBlocks/Spectral Lit.");
            log.AppendLine("Open SampleSceneTerrain and press Play. Tree positions are unchanged:");
            log.AppendLine("rng.Next(variants) draws once whatever the array length, so only species moved.");
            return log.ToString();
        }

        // Every GameObject asset outside Ours, keyed by file name. The prefab wins over the .fbx of
        // the same name because the pack already bound its materials there, and those bindings are
        // what Spectral() reads.
        static Dictionary<string, string> Catalog()
        {
            var best = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" })
                .Concat(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }));
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Ours.Any(path.StartsWith)) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                bool prefab = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
                if (best.TryGetValue(name, out var have) && !(prefab && !have.EndsWith(".prefab"))) continue;
                best[name] = path;
            }
            return best;
        }

        static List<GameObject> Discover(Dictionary<string, string> catalog, StringBuilder log)
        {
            var paths = new List<string>();
            foreach (var entry in catalog)
            {
                var name = entry.Key.ToLowerInvariant();
                if (SkipWords.Any(name.Contains)) continue;
                if (!TreeWords.Any(name.Contains)) continue;
                // A leaf-named mesh inside a tree pack is a loose card, not a tree.
                if (LeafWords.Any(name.Contains)) continue;
                paths.Add(entry.Value);
            }
            paths.Sort(StringComparer.Ordinal);

            log.AppendLine("Scanned Assets: " + catalog.Count + " imported models, " + paths.Count + " read as trees.");
            foreach (var p in paths) log.AppendLine("  found " + p);
            if (paths.Count == 0) return new List<GameObject>();
            log.AppendLine("Pack root: " + string.Join(", ", paths.Select(Top).Distinct()));
            return paths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(g => g != null).ToList();
        }

        // Ground cover is not instantiated: ForestCoverRenderer.cs:19 batches it into
        // Graphics.RenderMeshInstanced, which draws one mesh with one material and only submesh 0.
        // So each slot is baked flat - every LOD stage but one dropped, every submesh combined, the
        // height baked into the vertices because the renderer's only per-item transform is the
        // generator's own .8 to 1.2 roll.
        static void Cover(Dictionary<string, string> catalog, ForestConfiguration config,
            Dictionary<string, Material> cache, Dictionary<string, Texture> atlases, StringBuilder log)
        {
            Directory.CreateDirectory(Folder + "/Meshes");
            var meshes = new Mesh[Slots.Length];
            var materials = new Material[Slots.Length];
            log.AppendLine();
            log.AppendLine("Cover slots (weights fixed by ForestConfiguration.cs:118):");

            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (!catalog.TryGetValue(slot.Model, out var path))
                { log.AppendLine("  slot " + i + " MISSING " + slot.Model); continue; }
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (source == null) { log.AppendLine("  slot " + i + " UNLOADABLE " + path); continue; }

                var root = new GameObject("cover");
                try
                {
                    var visual = UnityEngine.Object.Instantiate(source, root.transform);
                    foreach (var g in visual.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(g);
                    foreach (var c in visual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);

                    var filters = Stage(visual, slot.Lod);
                    if (filters.Length == 0) { log.AppendLine("  slot " + i + " NO MESH " + slot.Model); continue; }

                    var renderers = filters.Select(f => f.GetComponent<Renderer>()).Where(r => r != null).ToArray();
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                    if (longest < .001f) { log.AppendLine("  slot " + i + " DEGENERATE " + slot.Model); continue; }
                    // Baked, not applied at draw time: the batcher has no per-slot transform.
                    float scale = slot.Size / longest;
                    visual.transform.localScale = new Vector3(scale, scale * slot.Stretch, scale);
                    visual.transform.localPosition = Vector3.up * (-bounds.min.y * scale * slot.Stretch);

                    var parts = new List<CombineInstance>();
                    foreach (var f in filters)
                        for (int s = 0; s < f.sharedMesh.subMeshCount; s++)
                            parts.Add(new CombineInstance { mesh = f.sharedMesh, subMeshIndex = s, transform = f.transform.localToWorldMatrix });

                    var name = "Cover" + i + "_" + slot.Model;
                    var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(parts.ToArray(), true, true);   // one submesh out, which is all slot 0 can draw
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    meshes[i] = SaveMesh(mesh, Folder + "/Meshes/" + name + ".asset");
                    materials[i] = Spectral(renderers[0].sharedMaterial, slot.Look, cache, atlases, log);
                    var got = meshes[i].bounds.size;
                    log.AppendLine("  slot " + i + " " + slot.Model + " LOD" + slot.Lod + " -> " +
                                   got.x.ToString("0.00") + " x " + got.y.ToString("0.00") + " y " + got.z.ToString("0.00") +
                                   " z, " + meshes[i].vertexCount + " verts, " + slot.Look);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }

            // A hole would make ForestCoverRenderer.cs:19 index a null mesh, so keep what works.
            if (meshes.Any(m => m == null))
            { log.AppendLine("  INCOMPLETE: cover left untouched, fix the misses above first."); return; }
            config.coverMeshes = meshes;
            config.coverMaterials = materials;
            log.AppendLine("  all " + Slots.Length + " slots filled, " + meshes.Sum(m => m.vertexCount) + " verts total.");
        }

        // One LOD stage, by the digit in the renderer name. Falls back to the coarsest stage that
        // exists, then to everything, so an untagged pack still yields a mesh.
        static MeshFilter[] Stage(GameObject visual, int wanted)
        {
            var all = visual.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
            var tagged = all.Select(f => new { f, m = Regex.Match(f.name, @"lod_?(\d)", RegexOptions.IgnoreCase) })
                            .Where(x => x.m.Success)
                            .Select(x => new { x.f, n = int.Parse(x.m.Groups[1].Value) }).ToArray();
            if (tagged.Length == 0) return all;
            int stage = tagged.Any(x => x.n == wanted) ? wanted : tagged.Max(x => x.n);
            return tagged.Where(x => x.n == stage).Select(x => x.f).ToArray();
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, old);
            UnityEngine.Object.DestroyImmediate(mesh);
            return old;
        }

        static string Top(string path)
        {
            var parts = path.Split('/');
            return parts.Length > 1 ? parts[0] + "/" + parts[1] : path;
        }

        static GameObject Prefab(GameObject source, float height, string suffix,
            Dictionary<string, Material> cache, Dictionary<string, Texture> atlases, StringBuilder log)
        {
            var root = new GameObject(source.name + suffix);
            try
            {
                var visual = UnityEngine.Object.Instantiate(source, root.transform);
                visual.name = "Visual";

                // Their LODGroup indexes their renderer list; it is rebuilt below against ours.
                foreach (var group in visual.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(group);
                // ForestWorld.cs:92 owns the trunk collider. Two colliders means the player snags.
                foreach (var c in visual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                // Impostor cards need the pack's own billboard shader, which has no URP path here.
                foreach (var b in visual.GetComponentsInChildren<BillboardRenderer>(true)) UnityEngine.Object.DestroyImmediate(b.gameObject);

                var renderers = visual.GetComponentsInChildren<Renderer>(true).Where(r => r.sharedMaterials.Length > 0).ToArray();
                if (renderers.Length == 0) { log.AppendLine("  SKIP " + source.name + ": no renderers"); return null; }

                // Bounds are world space and the instance sits at the origin unrotated, so these
                // are the model's own extents. Scale to the target height, then sit it on y=0.
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                if (bounds.size.y < .01f) { log.AppendLine("  SKIP " + source.name + ": flat bounds"); return null; }
                float scale = height / bounds.size.y;
                visual.transform.localScale = Vector3.one * scale;
                visual.transform.localPosition = Vector3.up * (-bounds.min.y * scale);

                foreach (var r in renderers)
                {
                    r.sharedMaterials = r.sharedMaterials.Select(m => Spectral(m, null, cache, atlases, log)).ToArray();
                    // Shipping trees do not cast; 120 casters an island is the frame budget.
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }

                renderers = Lods(root, renderers);
                var path = Folder + "/Prefabs/" + source.name + suffix + ".prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                log.AppendLine("  built " + source.name + suffix + " at " + height + " m (source " + bounds.size.y.ToString("0.0") +
                               " m, x" + scale.ToString("0.00") + "), " + renderers.Length + " renderers kept");
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Shipping builds three stages by matching "_LOD0/1/2" in the renderer name. Pack naming is
        // anyone's guess, so read whatever digit follows "lod" and fall back to a single stage -
        // an unmatched name would otherwise leave all three arrays empty and the tree invisible.
        //
        // A pack with four or more stages (Ultimate Nature Starter ships _LOD0 to _LOD3) has the
        // extras deleted, not folded into stage 2: two meshes sharing one LOD stage both draw, at
        // the same place, which is double the triangles and z-fighting on every coplanar face.
        // Returns the renderers still standing.
        static Renderer[] Lods(GameObject root, Renderer[] renderers)
        {
            var stages = new List<Renderer>[3] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            var drop = new List<GameObject>();
            bool tagged = false;
            foreach (var r in renderers)
            {
                var m = Regex.Match(r.name, @"lod_?(\d)", RegexOptions.IgnoreCase);
                if (!m.Success) { stages[0].Add(r); continue; }
                tagged = true;
                int stage = int.Parse(m.Groups[1].Value);
                if (stage > 2) { drop.Add(r.gameObject); continue; }
                stages[stage].Add(r);
            }
            foreach (var go in drop) UnityEngine.Object.DestroyImmediate(go);
            var kept = root.GetComponentsInChildren<Renderer>(true);

            var group = root.AddComponent<LODGroup>();
            if (!tagged || stages[1].Count + stages[2].Count == 0)
            {
                group.SetLODs(new[] { new LOD(.025f, kept) });
            }
            else
            {
                float[] cut = { .3f, .12f, .025f };
                group.SetLODs(Enumerable.Range(0, 3).Where(n => stages[n].Count > 0)
                    .Select(n => new LOD(cut[n], stages[n].ToArray())).ToArray());
            }
            group.RecalculateBounds();
            return kept;
        }

        // One Spectral Lit material per source material, cached by name so a pack that shares a
        // bark material across six trees produces one asset. Textures come across as they are; the
        // tint, cutout and wind come from the project palette (ForestBuilder.cs:133-136).
        // look = null lets the source decide (a tree trunk vs its needle cards). The cover slots
        // pass theirs, because one palette material has to serve both a rock and a lit mushroom.
        static Material Spectral(Material source, Look? look, Dictionary<string, Material> cache,
            Dictionary<string, Texture> atlases, StringBuilder log)
        {
            if (source == null) return null;
            var use = look ?? (Leaf(source) ? Look.Leaf : Look.Bark);
            var key = source.name + "/" + use;
            if (cache.TryGetValue(key, out var done)) return done;
            var shader = Shader.Find("LightingBlocks/Spectral Lit");
            if (shader == null) throw new Exception("Spectral shader missing");

            var id = "Foliage_" + source.name + (look == null ? "" : "_" + use);
            var path = Folder + "/Materials/" + id + ".mat";
            var m = new Material(shader) { name = id, enableInstancing = true };
            m.SetTexture("_BaseMap", Atlas(Tex(source, "_BaseMap", "_MainTex", "_BaseColorMap"), atlases, log));
            m.SetTexture("_NormalMap", Tex(source, "_NormalMap", "_BumpMap"));
            m.SetTexture("_MaskMap", Tex(source, "_MaskMap", "_MetallicGlossMap", "_OcclusionMap"));
            m.SetFloat("_NormalStrength", .35f);
            // Shipping cover meshes carry per-vertex tints and switch this on; a pack that paints
            // with a texture has no colour channel to read, so leaving it on would darken to black.
            m.SetFloat("_VertexTint", 0);
            // The pack knows its own alpha better than a guess does; .4 is the fallback the
            // shipping leaf materials use (ForestBuilder.cs:136).
            float cutoff = source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : .4f;

            switch (use)
            {
                case Look.Leaf:     // tree canopy: TwilightBuilder.cs:66 LivingPlants
                    m.SetColor("_BaseColor", Hex("#708EAC"));
                    m.SetFloat("_Cutoff", cutoff);
                    m.SetFloat("_Cull", 0);   // one sided cards seen from both
                    m.SetFloat("_Wind", .08f);
                    m.SetFloat("_SoftFill", .24f);
                    m.SetFloat("_Smoothness", .06f);
                    break;
                case Look.Ground:   // grass and bushes stand in open wind, so .14 like LivingPlants
                    m.SetColor("_BaseColor", Hex("#7E9CA8"));
                    m.SetFloat("_Cutoff", cutoff);
                    m.SetFloat("_Cull", 0);
                    m.SetFloat("_Wind", .14f);
                    m.SetFloat("_SoftFill", .24f);
                    m.SetFloat("_Smoothness", .06f);
                    break;
                case Look.Glow:     // mushrooms: TwilightBuilder.cs:67 LuminousPlants
                    m.SetColor("_BaseColor", Hex("#A38AD0"));
                    m.SetColor("_EmissionColor", new Color(.055f, .035f, .10f));
                    m.SetFloat("_Cutoff", cutoff);
                    m.SetFloat("_Cull", 0);
                    m.SetFloat("_Wind", .035f);
                    m.SetFloat("_Smoothness", .06f);
                    break;
                default:            // trunks, rocks, logs
                    m.SetColor("_BaseColor", Hex("#9B94B0"));
                    m.SetFloat("_Smoothness", .3f);
                    break;
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) AssetDatabase.CreateAsset(m, path);
            else { EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); }
            var asset = AssetDatabase.LoadAssetAtPath<Material>(path);
            cache[key] = asset;
            log.AppendLine("    material " + source.name + " -> " + use);
            return asset;
        }

        // Name first, then the pack's own alpha settings: a cutout queue or an enabled _ALPHATEST_ON
        // is the pack telling us this is a card, whatever it called the file.
        static bool Leaf(Material m)
        {
            var name = (m.name ?? "").ToLowerInvariant();
            if (LeafWords.Any(name.Contains)) return true;
            if (m.IsKeywordEnabled("_ALPHATEST_ON")) return true;
            if (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > .5f) return true;
            return m.renderQueue >= (int)RenderQueue.AlphaTest;
        }

        // A palette atlas is not a texture, it is a lookup table: the mesh UVs land on single flat
        // swatches a few pixels wide. Bilinear filtering blends across swatch borders, block
        // compression smears them further, and the coarse mip levels average the whole sheet - so a
        // trunk at 30 m samples the mean of the wood block instead of its colour.
        //
        // Copied into the sandbox rather than reimported in place: the pack's own settings are
        // right for the pack's own demo scene, and an imported asset edited here would be silently
        // reverted the next time the package is updated.
        static Texture Atlas(Texture source, Dictionary<string, Texture> cache, StringBuilder log)
        {
            if (source == null) return null;
            var name = source.name.ToLowerInvariant();
            if (!name.Contains("palette") && !name.Contains("atlas")) return source;
            if (cache.TryGetValue(source.name, out var done)) return done;

            var from = AssetDatabase.GetAssetPath(source);
            var to = Folder + "/Textures/" + source.name + Path.GetExtension(from);
            Directory.CreateDirectory(Folder + "/Textures");
            if (AssetDatabase.LoadAssetAtPath<Texture>(to) == null && !AssetDatabase.CopyAsset(from, to))
            { log.AppendLine("    atlas copy FAILED " + from); cache[source.name] = source; return source; }

            var importer = (TextureImporter)AssetImporter.GetAtPath(to);
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;      // kept: point sampling alone shimmers at distance
            importer.anisoLevel = 0;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();

            var copy = AssetDatabase.LoadAssetAtPath<Texture>(to);
            cache[source.name] = copy;
            log.AppendLine("    atlas " + source.name + " -> point/uncompressed copy in sandbox");
            return copy;
        }

        static Texture Tex(Material m, params string[] names)
        {
            foreach (var n in names) if (m.HasProperty(n)) { var t = m.GetTexture(n); if (t != null) return t; }
            return null;
        }

        static Color Hex(string color) { ColorUtility.TryParseHtmlString(color, out var c); return c; }
    }
}
