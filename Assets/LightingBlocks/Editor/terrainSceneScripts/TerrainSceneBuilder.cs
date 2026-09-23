using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sandbox only. Everything here targets Assets/Scenes/SampleSceneTerrain.unity and never
// SampleScene, so the shipping scene and the 207-check suite are untouched by anything below.
namespace LightingBlocks.TerrainScene
{
    // Unity Terrain is a square tile and the courts are 14-gons, so the island shape comes from
    // TerrainData.SetHoles: build one terrain over each shore's bounding box, then punch out every
    // cell outside the polygon. That is the whole trick, and it is why a Terrain can serve this map.
    [InitializeOnLoad]
    public static class TerrainSceneBuilder
    {
        const string Scene = "Assets/Scenes/SampleSceneTerrain.unity";
        const string Folder = "Assets/LightingBlocks/Art/TerrainScene";
        const string Request = "Library/LightingBlocks.terrain-request";
        const string Result = "Library/LightingBlocks.terrain-result.txt";
        const string DiagnoseMarker = TerrainDiagnostic.Marker;
        const string Captures = "Library/TerrainDiagnose";

        // 2^n+1. 513 over the 93 m island is a 0.18 m cell, which keeps the hole boundary from
        // reading as stair steps against the keel rim below it.
        const int HeightRes = 513;
        const int AlphaRes = 512;
        // The encoding range, not the sculpt amplitude. Raising it later costs heightmap precision.
        const float MaxHeight = 8f;
        // Gentle, per the last call: nothing here is a wall, so nothing here can be dived into.
        const float Relief = 1.6f;
        // Metres of dead flat shoreline. Everything pinned to the rim plane (KeelVertex, RimRocks,
        // RimSteps, VoidShafts, Lanterns) assumes y=0 there, so the falloff starts inside this band.
        const float RimFlat = 6f;
        const float RimBlend = 10f;

        static TerrainSceneBuilder() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Request)) return;
            string command = File.ReadAllText(Request).Trim().ToLowerInvariant();
            File.Delete(Request);
            try
            {
                if (command.StartsWith("audit")) Audit();
                else if (command.StartsWith("palette")) Diagnose("palette");
                else if (command.StartsWith("diagnose")) Diagnose("stripes");
                else if (command.StartsWith("shader")) ShaderReport();
                else if (command.StartsWith("light")) Lift();
                else if (command.StartsWith("repair")) Repair();
                else if (command.StartsWith("rim")) FlattenRim();
                else if (command.StartsWith("rebuild")) Build(true);
                else Build(false);
            }
            catch (Exception e) { File.WriteAllText(Result, "FAIL: " + e.Message); Debug.LogError(e); }
        }

        // Match the shipping ForestGround.mat exactly rather than inventing a brighter look.
        //
        // The shader blends by habitat weights: colour = base*a + soil*b + moss*r + grass*g, and
        // over these islands that reads moss .50, grass .27, soil .15, base .08. ForestGround.mat
        // binds neither _MossMap nor _GrassMap, so both sample the shader's "white" default and 77%
        // of the ground is flat 1.0, landing the floor near luminance .85. Binding the real Moss and
        // GrassGround textures (luminance .31) instead drops it to .31, which is the entire reason
        // the terrain read as unlit. Clearing those two slots is the fix; no tint can cover a 2.7x
        // albedo loss.
        // Clearing the slots fixed the brightness but left 77% of the albedo flat, and a flat albedo
        // over a sloped surface bands into contour stripes. Binding them back with _DetailBoost at
        // 1/0.31 keeps that brightness and returns the texture variation that hides the banding.
        const float SoftFill = .14f;      // TwilightBuilder.cs:65 applies this to the shipping ground
        const float Occlusion = .4f;
        // 1/0.082, the mean LINEAR luminance of Moss and GrassGround. Their sRGB mean is 0.31, which
        // would suggest 3.2, but the shader samples linear and 0.31 sRGB is 0.085 linear. Boosting by
        // the sRGB figure leaves the ground at a quarter of the brightness the flat white gave it.
        const float DetailBoost = 12f;
        static readonly Color Tint = new Color(.627451f, .69411767f, .6901961f, 1f);
        const string Twilight = "Assets/LightingBlocks/Art/SpectralForest/Twilight/Textures/";
        // _MossNormal is the third entry for a reason. Moss and grass are 77% of this ground's
        // habitat weights, so whatever sits in _MossNormal supplies most of the surface normal. It
        // was bound to Grass.png, a plain colour texture: UnpackNormalScale decoded a grass photo as
        // a normal map, tiled every 3 m, and painted the terrain in bands that no amount of shadow
        // bias could touch. The shipping ForestGround.mat leaves the slot empty, which reads as a
        // flat normal, which is why the flat ground never showed it.
        static readonly string[,] DetailSlots =
            { { "_MossMap", "Moss" }, { "_GrassMap", "GrassGround" }, { "_MossNormal", "MossNormal" } };

        // Reads the material off the terrains rather than a hard-coded path, so this reports what is
        // actually being rendered instead of what a file on disk claims.
        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Lift Terrain Brightness")]
        public static void Lift()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Scene) scene = EditorSceneManager.OpenScene(Scene);
            var report = "";
            var seen = new System.Collections.Generic.List<Material>();
            // The three courts are one ground, so a material assigned to any of them is the right
            // one for all of them. Filling the gaps here beats making anyone repeat the same drag
            // three times, and Terrain refuses multi-object editing so there is no shortcut in the UI.
            var shared = Ground();
            for (int i = 0; i < Landscape.Shores.Length; i++)
            {
                var obj = FindByName("ShoreFloorTerrain" + i);
                var terrain = obj == null ? null : obj.GetComponent<Terrain>();
                if (terrain != null && terrain.materialTemplate == null && shared != null)
                {
                    terrain.materialTemplate = shared;
                    terrain.drawInstanced = false;
                    EditorUtility.SetDirty(obj);
                    report += "island " + i + ": assigned " + shared.name + "\n";
                }
                var material = terrain == null ? null : terrain.materialTemplate;
                if (material == null) { report += "island " + i + ": no material assigned\n"; continue; }
                if (seen.Contains(material))
                {
                    report += "island " + i + ": shares " + material.name + "\n";
                    continue;
                }
                seen.Add(material);
                for (int s = 0; s < DetailSlots.GetLength(0); s++)
                {
                    var path = Twilight + DetailSlots[s, 1] + ".png";
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null) throw new Exception("Missing ground detail texture: " + DetailSlots[s, 1]);
                    NormalSlotGuard(DetailSlots[s, 0], path);
                    material.SetTexture(DetailSlots[s, 0], texture);
                }
                material.SetFloat("_DetailBoost", DetailBoost);
                material.SetFloat("_SoftFill", SoftFill);
                material.SetFloat("_AO", Occlusion);
                material.SetColor("_BaseColor", Tint);
                EditorUtility.SetDirty(material);
                // Reported after the writes, never before: printing the pre-write state reads as a
                // failure every time and has already sent me chasing the wrong cause twice.
                report += "island " + i + ": " + material.name + " on " + material.shader.name
                        + ", habitat " + Bound(material, "_HabitatMap") + ", moss " + Bound(material, "_MossMap")
                        + ", grass " + Bound(material, "_GrassMap")
                        + ", boost " + material.GetFloat("_DetailBoost").ToString("0.00")
                        + "," + NormalReport(material) + "\n";
            }
            AssetDatabase.SaveAssets();
            // materialTemplate lives on the Terrain component, so it is scene data: saving the
            // material asset alone would leave the assignment only in memory.
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Result, "SUCCESS: lifted " + seen.Count + " material(s) to SoftFill "
                + SoftFill + ", AO " + Occlusion + ", tint " + Tint.r + "/" + Tint.g + "/" + Tint.b + ".\n" + report);
            Debug.Log("Terrain brightness.\n" + report);
        }

        // Enters play mode so TerrainDiagnostic can capture one frame per suspect. Play mode is the
        // only place the bands exist: the world, the grass and the player are all built at runtime.
        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Diagnose Stripes")]
        public static void DiagnoseStripes() { Diagnose("stripes"); }

        // Renders the ground at several _DetailBoost and tint settings so the look can be chosen off
        // real frames. Every setting is applied to a material CLONE in play mode, so the asset is
        // never written: picking one still means editing the constants below.
        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Palette Options")]
        public static void PaletteOptions() { Diagnose("palette"); }

        // Reads the assembled scene instead of a screenshot: what sits at which height against the
        // terrain under it, how big it is, and what it draws with. TerrainAudit writes the file and
        // leaves play mode on its own.
        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Audit Placement")]
        public static void Audit()
        {
            if (SceneManager.GetActiveScene().path != Scene) EditorSceneManager.OpenScene(Scene);
            File.Delete("Library/LightingBlocks.terrain-audit.txt");
            File.WriteAllText(TerrainAudit.Marker, "go");
            File.WriteAllText(Result, "SUCCESS: entering play mode, audit lands in Library/LightingBlocks.terrain-audit.txt");
            EditorApplication.EnterPlaymode();
        }

        static void Diagnose(string mode)
        {
            if (SceneManager.GetActiveScene().path != Scene) EditorSceneManager.OpenScene(Scene);
            Directory.CreateDirectory(Captures);
            foreach (var f in Directory.GetFiles(Captures)) File.Delete(f);
            File.WriteAllText(DiagnoseMarker, mode);
            File.WriteAllText(Result, "SUCCESS: entering play mode for " + mode + ", captures land in " + Captures);
            EditorApplication.EnterPlaymode();
        }

        // A shader compile error never reaches Assembly-CSharp, so the Roslyn check says clean while
        // the terrain renders magenta. This is the only way to read the importer's own verdict from
        // outside the editor.
        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Shader Report")]
        public static void ShaderReport()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/SpectralLitTerrain.shader");
            if (shader == null) { File.WriteAllText(Result, "FAIL: SpectralLitTerrain.shader not found"); return; }
            // Compiling the variants the scene actually uses is what surfaces a per-pass error: the
            // importer only reports what it has been asked to build.
            ShaderUtil.ClearShaderMessages(shader);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(shader), ImportAssetOptions.ForceUpdate);
            int count = ShaderUtil.GetShaderMessageCount(shader);
            var report = "SpectralLitTerrain: " + count + " message(s), errors " + ShaderUtil.ShaderHasError(shader) + "\n";
            foreach (var m in ShaderUtil.GetShaderMessages(shader))
                report += "  " + m.severity + " line " + m.line + " [" + m.platform + "] " + m.message + " " + m.messageDetails + "\n";
            File.WriteAllText(Result, (ShaderUtil.ShaderHasError(shader) ? "FAIL: " : "SUCCESS: ") + report);
            Debug.Log(report);
        }

        // A colour texture in a normal slot is silent: it imports, binds, compiles and renders, and
        // only shows up as banding on a surface that tilts. It cost a full diagnostic pass to find
        // once, so it fails loudly from here on.
        static void NormalSlotGuard(string slot, string path)
        {
            if (!slot.EndsWith("Normal")) return;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new Exception("No importer for " + path);
            if (importer.textureType != TextureImporterType.NormalMap)
                throw new Exception(slot + " needs a normal map, but " + path + " imports as "
                                    + importer.textureType + ". UnpackNormalScale would decode it as noise.");
        }

        // Reports what every normal slot on the terrain material actually holds, so a wrong binding
        // made by hand in the inspector is visible without another capture run.
        static string NormalReport(Material material)
        {
            var report = "";
            foreach (var slot in new[] { "_NormalMap", "_SoilNormal", "_MossNormal" })
            {
                if (!material.HasProperty(slot)) continue;
                var texture = material.GetTexture(slot);
                if (texture == null) { report += " " + slot + "=flat"; continue; }
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                bool ok = importer != null && importer.textureType == TextureImporterType.NormalMap;
                report += " " + slot + "=" + texture.name + (ok ? "" : " [NOT A NORMAL MAP]");
            }
            return report;
        }

        static string Bound(Material material, string property)
        {
            if (!material.HasProperty(property)) return "absent";
            var texture = material.GetTexture(property);
            return texture == null ? "EMPTY" : texture.name;
        }

        // Fixes the rendering setup on terrains that already exist and reports what the data
        // actually contains, without regenerating a single height. Use this before assuming the
        // heightmap or splatmap is wrong: the difference is usually the material.
        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Repair Materials")]
        public static void Repair()
        {
            BridgeProfile.Select("SampleSceneTerrain");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Scene) scene = EditorSceneManager.OpenScene(Scene);
            var report = "";
            for (int i = 0; i < Landscape.Shores.Length; i++)
            {
                var obj = FindByName("ShoreFloorTerrain" + i);
                if (obj == null) { report += "island " + i + ": terrain object missing\n"; continue; }
                var terrain = obj.GetComponent<Terrain>();
                Dress(terrain);
                var data = terrain.terrainData;
                var alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
                var peak = new float[data.terrainLayers.Length];
                for (int z = 0; z < data.alphamapHeight; z += 8)
                    for (int x = 0; x < data.alphamapWidth; x += 8)
                        for (int l = 0; l < peak.Length; l++) peak[l] = Mathf.Max(peak[l], alpha[z, x, l]);
                var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                float high = 0;
                foreach (var h in heights) high = Mathf.Max(high, h);
                report += "island " + i + ": " + data.terrainLayers.Length + " layers, peak weights "
                        + string.Join("/", Array.ConvertAll(peak, w => w.ToString("0.00")))
                        + ", tallest " + (high * data.size.y).ToString("0.00") + " m"
                        + ", material " + (terrain.materialTemplate == null ? "pipeline default" : terrain.materialTemplate.name) + "\n";
                EditorUtility.SetDirty(obj);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Result, "SUCCESS: repaired materials.\n" + report);
            Debug.Log("Terrain repair.\n" + report);
        }

        // Draw Instanced must stay off. Instanced terrain rebuilds vertices from the heightmap
        // texture inside the vertex shader, and SpectralLitTerrain's Vert reads positionOS like any
        // ordinary mesh, so instancing renders nothing at all through it.
        // A material already on the terrain is never overwritten: it is hand-tuned art, and a
        // rebuild exists to regenerate ground data, not to throw that away.
        static void Dress(Terrain terrain)
        {
            if (terrain.materialTemplate == null) terrain.materialTemplate = Ground();
            terrain.heightmapPixelError = 3f;
            terrain.drawInstanced = false;
            terrain.basemapDistance = 10000f;
            terrain.allowAutoConnect = false;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // Falls back to whatever is already on a sibling terrain, so a material saved under any name
        // still propagates rather than silently leaving two islands unpainted.
        static Material Ground()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/SpectralGroundTerrain.mat");
            if (material != null) return material;
            for (int i = 0; i < Landscape.Shores.Length; i++)
            {
                var obj = FindByName("ShoreFloorTerrain" + i);
                var terrain = obj == null ? null : obj.GetComponent<Terrain>();
                if (terrain != null && terrain.materialTemplate != null) return terrain.materialTemplate;
            }
            return null;
        }

        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Build Terrains")]
        public static void Build() { Build(false); }

        // Separate menu item because the generated heightmap is a starting point, not the deliverable:
        // once anyone has touched a terrain with a brush, regenerating it silently throws that away.
        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Rebuild Terrains (discards sculpting)")]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog("Rebuild terrains",
                "This discards every brush stroke on all three terrains and regenerates them from noise. Continue?",
                "Discard and rebuild", "Cancel")) return;
            Build(true);
        }

        public static void Build(bool overwrite)
        {
            // The static Landscape/BridgeProfile state may have been initialised under whatever
            // scene the editor last touched. This is an edit-time tool that always targets the
            // terrain scene, so pin the profile before reading Landscape.Shores below.
            BridgeProfile.Select("SampleSceneTerrain");
            if (EditorApplication.isPlaying) throw new Exception("Stop Play before building terrains.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Scene) scene = EditorSceneManager.OpenScene(Scene);
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();

            var layers = Layers();
            var report = "";
            for (int i = 0; i < Landscape.Shores.Length; i++) report += Island(i, layers, overwrite) + "\n";

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Result, "SUCCESS: built " + Landscape.Shores.Length + " terrains into SampleSceneTerrain.\n" + report);
            Debug.Log("Terrain scene built.\n" + report);
        }

        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Remove Terrains")]
        public static void Remove()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Scene) scene = EditorSceneManager.OpenScene(Scene);
            for (int i = 0; i < Landscape.Shores.Length; i++)
            {
                var existing = FindByName("ShoreFloorTerrain" + i);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
                var floor = FindByName("ShoreFloor" + i);
                if (floor != null) Show(floor, true);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ---- one island ------------------------------------------------------

        static string Island(int index, TerrainLayer[] layers, bool overwrite)
        {
            var points = Landscape.Shores[index];
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in points)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y);
            }
            float width = maxX - minX, depth = maxZ - minZ;

            string path = Folder + "/ShoreTerrain" + index + ".asset";
            if (!overwrite && AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null)
                return "island " + index + ": kept, sculpting preserved (Rebuild Terrains to regenerate)";
            AssetDatabase.DeleteAsset(path);
            var data = new TerrainData();
            // The asset has to exist before the splatmap is written: TerrainData keeps its alphamaps
            // as sub-asset textures, and writing them into an unsaved TerrainData drops them on
            // serialisation, leaving layer 0 at weight 1 everywhere.
            AssetDatabase.CreateAsset(data, path);
            // Resolution before size: setting it afterwards resamples and silently flattens the map.
            data.heightmapResolution = HeightRes;
            data.size = new Vector3(width, MaxHeight, depth);
            data.alphamapResolution = AlphaRes;
            // Layers before alphamaps, always: assigning terrainLayers reallocates the splatmap and
            // resets it to layer 0.
            data.terrainLayers = layers;
            data.SetDetailResolution(256, 16);

            var heights = new float[HeightRes, HeightRes];
            for (int z = 0; z < HeightRes; z++)
                for (int x = 0; x < HeightRes; x++)
                {
                    var p = new Vector2(minX + width * x / (HeightRes - 1f), minZ + depth * z / (HeightRes - 1f));
                    heights[z, x] = Height(p, points, index) / MaxHeight;
                }
            data.SetHeights(0, 0, heights);

            // A cell survives if any of its four corners is inside the shore. Centre-only testing
            // leaves up to a cell of void showing between the terrain and the keel; overhanging by
            // one cell instead is hidden by the rim rocks that already sit on that edge.
            int holeRes = HeightRes - 1;
            var holes = new bool[holeRes, holeRes];
            int kept = 0;
            for (int z = 0; z < holeRes; z++)
                for (int x = 0; x < holeRes; x++)
                {
                    bool any = false;
                    for (int c = 0; c < 4 && !any; c++)
                    {
                        var p = new Vector2(minX + width * (x + (c & 1)) / (HeightRes - 1f),
                                            minZ + depth * (z + ((c >> 1) & 1)) / (HeightRes - 1f));
                        any = Landscape.Inside(p, points);
                    }
                    holes[z, x] = any; if (any) kept++;
                }
            data.SetHoles(0, 0, holes);

            data.SetAlphamaps(0, 0, Splat(data, points, minX, minZ, width, depth, index));
            EditorUtility.SetDirty(data);

            var old = FindByName("ShoreFloorTerrain" + index);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var obj = Terrain.CreateTerrainGameObject(data);
            // StartsWith("ShoreFloor") is the whitelist ForestWorld.Occupied uses to ignore the
            // ground when it tests a cell, so the prefix is load bearing. The exact names
            // "ShoreFloor0..2" stay on the old mesh objects, which WorldDressing.Reshape and
            // SceneMaterial still look up by exact name.
            obj.name = "ShoreFloorTerrain" + index;
            Dress(obj.GetComponent<Terrain>());

            var floor = FindByName("ShoreFloor" + index);
            if (floor != null)
            {
                obj.transform.SetParent(floor.transform.parent, true);
                obj.layer = floor.layer;
                // Keep the GameObject and its material: WorldDressing.SceneMaterial reads
                // ShoreFloor0's renderer for the islets, rim rocks and paving fallback.
                Show(floor, false);
            }
            obj.transform.position = new Vector3(minX, 0, minZ);
            EditorUtility.SetDirty(obj);

            return "island " + index + ": " + width.ToString("0") + " x " + depth.ToString("0")
                 + " m, " + kept + " of " + (holeRes * holeRes) + " cells solid";
        }

        static void Show(GameObject floor, bool visible)
        {
            var renderer = floor.GetComponent<MeshRenderer>(); if (renderer != null) renderer.enabled = visible;
            var collider = floor.GetComponent<MeshCollider>(); if (collider != null) collider.enabled = visible;
            EditorUtility.SetDirty(floor);
        }

        // ---- the height field ------------------------------------------------

        // Pinned to zero at the rim three ways: outside the shore, inside the flat band, and through
        // a smoothstep that only reaches full amplitude RimBlend metres further in. Layered Perlin
        // keeps it deterministic, so a rebuild never moves the ground under a saved game.
        static float Falloff(Vector2 p, Vector2[] points)
        {
            if (!Landscape.Inside(p, points)) return 0;
            return Mathf.SmoothStep(0, 1, Mathf.Clamp01((ForestLayoutGenerator.Edge(p, points) - RimFlat) / RimBlend));
        }

        // Brushes do not know about the rim contract, so this puts it back without touching the
        // interior: every sample is scaled by the same envelope the generator used, which is 1 over
        // the middle of the court and 0 inside the flat band. Sculpt survives, the rim returns to y=0.
        [MenuItem("Tools/Lighting Blocks/Terrain Scene/Flatten Rim")]
        public static void FlattenRim()
        {
            BridgeProfile.Select("SampleSceneTerrain");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Scene) EditorSceneManager.OpenScene(Scene);
            for (int i = 0; i < Landscape.Shores.Length; i++)
            {
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(Folder + "/ShoreTerrain" + i + ".asset");
                if (data == null) continue;
                var points = Landscape.Shores[i];
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var p in points)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y);
                }
                int res = data.heightmapResolution;
                var heights = data.GetHeights(0, 0, res, res);
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        heights[z, x] *= Falloff(new Vector2(minX + (maxX - minX) * x / (res - 1f),
                                                             minZ + (maxZ - minZ) * z / (res - 1f)), points);
                data.SetHeights(0, 0, heights);
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Rim flattened back to y=0 on all terrains; interior sculpting untouched.");
        }

        static float Height(Vector2 p, Vector2[] points, int index)
        {
            float falloff = Falloff(p, points);
            if (falloff <= 0) return 0;
            float offset = 131f * index + 17f;
            float value = 0, amplitude = 1, frequency = 1 / 26f, total = 0;
            for (int octave = 0; octave < 3; octave++)
            {
                value += amplitude * Mathf.PerlinNoise((p.x + offset) * frequency, (p.y + offset) * frequency);
                total += amplitude; amplitude *= .5f; frequency *= 2.1f;
            }
            return Relief * falloff * Mathf.SmoothStep(0, 1, value / total);
        }

        // ---- texturing -------------------------------------------------------

        // Slope picks rock, a second noise field scatters paving, ground takes the rest. This is the
        // "paint it randomly" pass: it is automatic and varied, but driven by the surface rather
        // than by a random number, so re-running it does not reshuffle the map.
        static float[,,] Splat(TerrainData data, Vector2[] points, float minX, float minZ, float width, float depth, int index)
        {
            var map = new float[AlphaRes, AlphaRes, 3];
            for (int z = 0; z < AlphaRes; z++)
                for (int x = 0; x < AlphaRes; x++)
                {
                    float u = x / (AlphaRes - 1f), v = z / (AlphaRes - 1f);
                    var p = new Vector2(minX + width * u, minZ + depth * v);
                    // GetSteepness takes normalised coordinates as (x, z). Gentle relief tops out
                    // near 7 degrees, so the rock knee sits at 2.5, not at the 10-plus a real
                    // mountain terrain would use - a higher knee paints nothing at all here.
                    float steep = data.GetSteepness(u, v);
                    float rock = Mathf.Clamp01((steep - 2.5f) / 5f);
                    float patch = Mathf.PerlinNoise((p.x + 700f + index * 53f) * .05f, (p.y + 700f + index * 53f) * .05f);
                    float pave = Mathf.Clamp01((patch - .55f) * 5f) * (1 - rock);
                    float ground = Mathf.Max(.001f, 1 - rock - pave);
                    float sum = rock + pave + ground;
                    map[z, x, 0] = ground / sum; map[z, x, 1] = rock / sum; map[z, x, 2] = pave / sum;
                }
            return map;
        }

        static TerrainLayer[] Layers()
        {
            return new[]
            {
                Layer("GroundLayer", "Assets/LightingBlocks/Art/SpectralForest/Textures/forest_ground_04_Color.png", null, 9),
                Layer("RockLayer", "Assets/LightingBlocks/Art/Pack/Textures/T_ENV_Cliff_BaseColor.png",
                                   "Assets/LightingBlocks/Art/Pack/Textures/T_ENV_Cliff_Normal.png", 7),
                Layer("PavingLayer", "Assets/LightingBlocks/Art/Pack/Textures/T_ENV_Paving_BaseColor.png",
                                     "Assets/LightingBlocks/Art/Pack/Textures/T_ENV_Paving_Normal.png", 5)
            };
        }

        static TerrainLayer Layer(string name, string diffuse, string normal, float tile)
        {
            string path = Folder + "/" + name + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse);
            if (layer.diffuseTexture == null) throw new Exception("Missing terrain texture: " + diffuse);
            if (normal != null) layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
            layer.tileSize = new Vector2(tile, tile);
            layer.tileOffset = Vector2.zero;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static GameObject FindByName(string name)
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == name) return t.gameObject;
            return null;
        }
    }
}
