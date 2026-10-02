using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LightingBlocks.Editor
{
    // Builds Assets/Scenes/Lighting.unity from scratch every run: three hub platforms of contiguous
    // crystal tiles (own colliders, own PlatformSupport, own dedicated materials under
    // Assets/LightingBlocks/Art/Lighting/), a restyled exit beacon, and a restrained-bloom void
    // volume. Player, camera, GameManager/GameHUD/ForestWorld and the Directional Light are cloned
    // (moved, never saved back) from SampleSceneTerrain.unity, which already has working controls,
    // UI and forest data wired up - this scene only differs in ground and dressing.
    // Idempotent: the whole scene is discarded and rebuilt every call, so two runs never accumulate
    // duplicates. Never opens the terrain scene for writing and never calls SaveScene on it.
    [InitializeOnLoad]
    public static class LightingSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Lighting.unity";
        const string TerrainScenePath = "Assets/Scenes/SampleSceneTerrain.unity";
        const string Art = "Assets/LightingBlocks/Art/Lighting";
        // Matches CrystalTiles' bridge tier colours (blue/violet/amber), so a hub's tiles read as
        // the same identity as the stones that will eventually cross it.
        static readonly string[] TierHex = { "#246DFF", "#B03DFF", "#FFAE38" };

        [MenuItem("Tools/Lighting Blocks/Build Lighting Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play before building the Lighting scene.");
            Directory.CreateDirectory(Art + "/Materials"); AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Left on whatever profile this process booted with (Default: platformGround false) while
            // the terrain scene is open, so its own already-built ForestCoverRenderer instances
            // OnEnable() correctly against island-polygon ground instead of an as-yet-empty
            // PlatformSupport.Active list. Only switch to Lighting once that scene is closed again.
            var source = EditorSceneManager.OpenScene(TerrainScenePath, OpenSceneMode.Additive);
            // Everything except the terrain's own world (ShoreFloorTerrain, Spectral Forest, rim
            // dressing) moves over: Player, Main Camera, Directional Light, Soul Bridge Systems
            // (GameManager + GameHUD + ForestWorld + ForestSoundscape) and whatever infra roots
            // (EventSystem, etc.) the terrain scene carries. Moving keeps every cross-reference
            // between them intact (soulLight, cameraRig.player, player.viewCamera, forest.configuration)
            // - cloning each root separately would not. The source scene is closed unsaved afterwards,
            // so nothing here ever reaches SampleSceneTerrain.unity on disk.
            foreach (var root in source.GetRootGameObjects())
                if (root.name != "Soul Bridge World") SceneManager.MoveGameObjectToScene(root, scene);
            EditorSceneManager.CloseScene(source, true);
            BridgeProfile.Select("Lighting");

            var game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            if (game == null) throw new Exception("No GameManager found in " + TerrainScenePath + " to clone controls from.");
            game.characterLightRange = 24f;
            if (game.cameraRig != null)
            {
                game.cameraRig.lightingFarClip = 4096f;
                var lightingCamera = game.cameraRig.GetComponent<Camera>();
                if (lightingCamera != null) lightingCamera.farClipPlane = 4096f;
            }
            // ForestWorld is cloned from SampleSceneTerrain.unity, which never serializes this field
            // (it is missing from that scene's YAML entirely, so it loads at its C# default: false).
            // The shipped Lighting.unity has always carried it as 1 - a fresh build must reproduce
            // that, not silently turn off every particle, receiver and canopy firefly.
            var forestWorld = game.GetComponent<ForestWorld>();
            if (forestWorld == null) throw new Exception("No ForestWorld found alongside GameManager in " + TerrainScenePath + ".");
            forestWorld.enableLightingParticles = true;
            // Lighting-only visibility limiter (world + particle fade) - see LightingFog.cs and the
            // matching render pass built below. Lighting keeps a wider double-sight radius.
            var lightingFog = game.GetComponent<LightingFog>() ?? game.gameObject.AddComponent<LightingFog>();
            lightingFog.worldClearDistance = 12f; lightingFog.worldBlackDistance = 24f;
            lightingFog.particleClearDistance = 12f; lightingFog.particleHiddenDistance = 24f; // the reference shows dots to 24 m
            var pickupAudio=game.GetComponent<LightingPickupAudio>() ?? game.gameObject.AddComponent<LightingPickupAudio>();
            pickupAudio.clips=Enumerable.Range(1,4).Select(i=>Resources.Load<AudioClip>("Audio/LightingPickups/pickup_"+i.ToString("00"))).ToArray();

            var world = new GameObject("Lighting World").transform;
            var blocksRoot = new GameObject("Bridge Stones").transform; blocksRoot.SetParent(world, false);
            game.blocksRoot = blocksRoot;
            game.blockPrefabs = Enumerable.Range(0, 5).Select(LoadPrefab).ToArray(); // blue, violet, amber, green, red (CrystalTiles)
            // Soul Gain replaces Soul Well in Lighting (Agent C's economy work): no physical prop.
            game.soulWell = null; game.wellCore = null;

            var platforms = new GameObject("Hub Platforms").transform; platforms.SetParent(world, false);
            int tiles = 0, runs = 0;
            for (int i = 0; i < Landscape.Shores.Length; i++) BuildHub(platforms, i, ref tiles, ref runs);

            BuildBeacon(world, game);
            BuildLightingRenderer();
            BuildVoidVolume(world, game);
            // Editor-time baseline only; WorldDressing.AtmosphereVoid enforces this every Start() too.
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional) light.intensity = 0f;

            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Assets/Scenes");
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            string result = (saved ? "SUCCESS" : "FAIL") + ": hubs=" + Landscape.Shores.Length + " tiles=" + tiles + " colliders=" + runs;
            File.WriteAllText("Library/Lighting.build-result.txt", result);
            Debug.Log("Lighting scene built. " + result);
        }

        // Lighting first and enabled; every legacy scene stays listed (so Play/File>Build Settings
        // still shows them, and switching to one by hand still works) but disabled, so a Build only
        // ships Lighting by default. Idempotent - safe to run after every rebuild.
        [MenuItem("Tools/Lighting Blocks/Set Lighting As First Build Scene")]
        public static void SetBuildScenes()
        {
            string[] legacy = { "Assets/Scenes/SampleScene.unity", "Assets/Scenes/SampleSceneTerrain.unity", "Assets/Scenes/terrainScene.unity" };
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var path in legacy) if (File.Exists(path)) scenes.Add(new EditorBuildSettingsScene(path, false));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("Build scenes set: " + string.Join(", ", System.Array.ConvertAll(scenes.ToArray(), s => System.IO.Path.GetFileNameWithoutExtension(s.path) + (s.enabled ? "(on)" : "(off)"))));
        }

        static BridgeBlock LoadPrefab(int tier) =>
            AssetDatabase.LoadAssetAtPath<BridgeBlock>("Assets/LightingBlocks/Art/Prefabs/Bridge Stone " + tier + ".prefab");

        // ---- hub platforms -----------------------------------------------------------------

        // One hub per Landscape.Shores polygon (already Terrain's numbers, reused verbatim by the
        // Lighting profile - "preserve... three hub footprints"). Tiles sit on the existing 2 m
        // grid; a cell is included if its centre or any corner falls inside the polygon, which
        // gives full interior coverage with a slightly jagged, crystal-formation edge rather than a
        // gap. Contiguous runs along a row share ONE collider + PlatformSupport (an axis-aligned
        // rectangle is still an exact AABB match - see PlatformSupport.cs), while the mesh still
        // draws every 2 m cell separately so the tiled-crystal look matches the grid exactly.
        static void BuildHub(Transform platforms, int index, ref int tiles, ref int runs)
        {
            string hubId = "island-" + (index + 1);
            var polygon = Landscape.Shores[index];
            var material = PlatformMaterial(index);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in polygon) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            int xLo = Mathf.FloorToInt(minX / 2) - 1, xHi = Mathf.CeilToInt(maxX / 2) + 1;
            int zLo = Mathf.FloorToInt(minZ / 2) - 1, zHi = Mathf.CeilToInt(maxZ / 2) + 1;
            for (int zi = zLo; zi <= zHi; zi++)
            {
                // Column indices run negative-to-positive (these islands straddle x=0), so "no run
                // yet" cannot be sentinelled with a column value like -1 - every hub west of centre
                // silently lost its whole run the moment a real run legitimately started at a
                // negative column. int.MinValue can never collide with a real 2 m grid column.
                int runStart = int.MinValue;
                for (int xi = xLo; xi <= xHi + 1; xi++)
                {
                    bool included = xi <= xHi && CellInside(xi * 2f, zi * 2f, polygon);
                    if (included && runStart == int.MinValue) runStart = xi;
                    else if (!included && runStart != int.MinValue)
                    {
                        BuildRun(platforms, hubId, material, runStart, xi - runStart, zi);
                        tiles += xi - runStart; runs++;
                        runStart = int.MinValue;
                    }
                }
            }
        }
        static bool CellInside(float x, float z, Vector2[] polygon)
        {
            if (Landscape.Inside(new Vector2(x, z), polygon)) return true;
            foreach (var corner in new[] { new Vector2(x - 1, z - 1), new Vector2(x + 1, z - 1), new Vector2(x + 1, z + 1), new Vector2(x - 1, z + 1) })
                if (Landscape.Inside(corner, polygon)) return true;
            return false;
        }
        static void BuildRun(Transform parent, string hubId, Material material, int colStart, int cells, int zi)
        {
            var go = new GameObject(hubId + " tiles"); go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(colStart * 2f, 0, zi * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = TileRun(cells);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            // One solid collider under the whole run (no seam gaps for the CharacterController),
            // even though the mesh above draws every cell with a visible gap between them.
            var collider = go.AddComponent<BoxCollider>();
            collider.center = new Vector3((cells - 1) * 1f, -.09f, 0);
            collider.size = new Vector3(cells * 2f, .18f, 2f);
            var support = go.AddComponent<PlatformSupport>();
            support.hubId = hubId;
        }
        // A beveled slab per cell (same ring/quad technique as CrystalTiles.Slab), offset along
        // local x by the cell index, merged into one mesh per run.
        static Mesh TileRun(int cells)
        {
            var source = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/LightingBlocks/Art/SpectralForest/Twilight/Meshes/CrystalBody1.asset");
            if (source == null) throw new InvalidOperationException("Bridge crystal mesh is missing.");
            var instances = new CombineInstance[cells];
            for (int i = 0; i < cells; i++)
                instances[i] = new CombineInstance { mesh = source, transform = Matrix4x4.Translate(new Vector3(i * 2f, -source.bounds.max.y, 0)) };
            var mesh = new Mesh { name = "Hub bridge crystals x" + cells };
            if (source.vertexCount * cells > 60000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(instances, true, true);
            return mesh;
        }

        // ---- dedicated Lighting materials ---------------------------------------------------

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        // Opaque, depth-writing floor: the batched hub tile meshes themselves are now the visible
        // island surface (see LightingWorldCheck's removal note) - no more transparent near-invisible
        // face with a separately-duplicated white dashed outline on top (LightingPlaceholderFloor,
        // deleted). Plain URP Lit, pitch black and glossy like the reference prototype's glass floor:
        // the player, step and spirit-dot lights leave shiny pools, and with a solid black sky the
        // environment reflection is black, so nothing else shows.
        static Material PlatformMaterial(int tier)
        {
            string path = Art + "/Materials/Platform" + tier + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.renderQueue = -1;
            material.SetColor("_BaseColor", Hex("#050507"));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", .85f);
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
        static Material BeaconMaterial()
        {
            string path = Art + "/Materials/Beacon.mat";
            var shader = Shader.Find("LightingBlocks/Twilight Crystal");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            var tint = Hex("#FFAE38");
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", tint * 3f);
            if (material.HasProperty("_Opacity")) material.SetFloat("_Opacity", .88f);
            if (material.HasProperty("_Glow")) material.SetFloat("_Glow", 1f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---- exit beacon ---------------------------------------------------------------------

        // Same reserved spot ForestLayoutGenerator.Reserved already keeps clear for island 3
        // (0, 0, 55 + Expansion) - restyled with the dedicated Beacon material instead of the
        // legacy grave-stone look.
        static void BuildBeacon(Transform world, GameManager game)
        {
            var material = BeaconMaterial();
            var beaconRoot = new GameObject("Lighting Beacon").transform;
            beaconRoot.SetParent(world, false);
            beaconRoot.position = new Vector3(0, 0, 55f + Landscape.Expansion);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Beacon body"; body.transform.SetParent(beaconRoot, false);
            body.transform.localPosition = new Vector3(0, .3f, 0); body.transform.localScale = new Vector3(2, .3f, 2);
            var bodyRenderer = body.GetComponent<Renderer>(); bodyRenderer.sharedMaterial = material; bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Beacon core"; core.transform.SetParent(beaconRoot, false);
            core.transform.localPosition = new Vector3(0, 1.4f, 0); core.transform.localScale = Vector3.one * .65f;
            var coreRenderer = core.GetComponent<Renderer>(); coreRenderer.sharedMaterial = material; coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
            UnityEngine.Object.DestroyImmediate(core.GetComponent<Collider>());
            var light = core.AddComponent<Light>();
            light.type = LightType.Point; light.color = Hex("#FFB661"); light.range = 9; light.intensity = 3.2f; light.shadows = LightShadows.Soft;
            core.SetActive(false); // GameManager.Recalculate turns this on once the second bridge connects.
            game.exit = beaconRoot; game.nextAreaGlow = core;
        }

        // ---- void volume -----------------------------------------------------------------------

        // A dedicated, much darker instance of the Night Sky shader: the shared NightSky.mat (used
        // by the forest scenes) reads as a visible navy-violet when it fills the whole frame with
        // nothing else to anchor against, which is not the "black void" the reference calls for.
        // Same shader (so the Esc menu's star toggle - _LightingBlocksHideStars - still works),
        // own material, own asset.
        // NightSky.shader uses _SkyHorizon as the colour at d.y==0 regardless of top/bottom (see
        // its Frag: lerp(_SkyHorizon,_SkyTop,...) / lerp(_SkyHorizon,_SkyBottom,...)), so a first
        // pass that only darkened top/bottom still left a plainly visible blue band across most of
        // the frame (linear ~0.03 encodes to sRGB ~62/255 - nowhere near "near-black"). All three
        // are now the SAME flat near-black value, so there is no gradient to read as "navy blue" -
        // this is a uniform void, not a horizon.
        static Material VoidSkyMaterial()
        {
            string path = Art + "/Materials/VoidSky.mat";
            var shader = Shader.Find("LightingBlocks/Night Sky");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            // Linear ~0.0009/0.0025 encodes to sRGB ~#030308 on screen - the reference's near-black void.
            var flat = new Color(.0009f, .0009f, .0025f);
            material.SetColor("_SkyTop", flat);
            material.SetColor("_SkyHorizon", flat);
            material.SetColor("_SkyBottom", flat);
            material.SetColor("_Nebula", new Color(.01f, .008f, .016f));
            material.SetFloat("_StarDensity", .5f); material.SetFloat("_StarBrightness", 9f);
            material.SetFloat("_StarCells", 140f); material.SetFloat("_StarSize", 6f);
            material.SetColor("_StarTint", new Color(.85f, .9f, 1f));
            EditorUtility.SetDirty(material);
            return material;
        }
        static void BuildVoidVolume(Transform world, GameManager game)
        {
            var host = new GameObject("Void Volume", typeof(Volume));
            host.transform.SetParent(world, false);
            var volume = host.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = VoidProfile();
            RenderSettings.skybox = null;
            var camera = game.cameraRig == null ? null : game.cameraRig.GetComponent<Camera>();
            if (camera == null) return;
            camera.clearFlags = CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var data = camera.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }
        // Dedicated Lighting profile: restrained bloom (lower intensity than the forest's .32) and
        // Neutral tonemapping.
        // VolumeProfile.Add<T>() only builds the in-memory component and appends it to
        // profile.components - it does NOT persist it as a sub-asset of the profile. Build() and the
        // capture routine each run as a separate -executeMethod batch process, so the capture process
        // was always loading LightingVolume.asset fresh off disk with a components list of three
        // dangling {fileID: 0} entries (verified directly in the saved .asset text) - no bloom, no
        // tonemapping, ever actually active. That is why repeated tuning passes on these values never
        // changed a single captured pixel. Fix: explicitly persist each component with
        // AddObjectToAsset, and destroy the previous run's real sub-assets first so a rebuild stays
        // idempotent (no orphaned VolumeComponent assets accumulating on disk).
        static VolumeProfile VoidProfile()
        {
            string path = Art + "/LightingVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            foreach (var c in profile.components) if (c != null) UnityEngine.Object.DestroyImmediate(c, true);
            profile.components.Clear();
            var bloom = profile.Add<Bloom>(true); AssetDatabase.AddObjectToAsset(bloom, profile);
            // Restrained means a tight halo on genuinely emissive things, not a wash across the
            // frame: a lower scatter keeps the glow close to its source (trunk, lanterns, lit
            // blocks) instead of bleeding across the whole canopy/platform.
            bloom.active = true; bloom.threshold.Override(1.3f); bloom.intensity.Override(.10f); bloom.scatter.Override(.15f);
            var depth=profile.Add<DepthOfField>(true);AssetDatabase.AddObjectToAsset(depth,profile);depth.mode.Override(DepthOfFieldMode.Off);
            var motion=profile.Add<MotionBlur>(true);AssetDatabase.AddObjectToAsset(motion,profile);motion.intensity.Override(0);
            // ACES rolls bright HDR emission (trunk/canopy magnitude well past 1) toward white -
            // Tonemapping.cs's own doc comment on ACES says exactly this ("has an effect on actual
            // color hue and saturation"). Neutral is documented as "minimal impact on color hue and
            // saturation" - the highlight rolls off without bleaching the colour out. A small negative
            // post-exposure keeps the brightest props (trunk, lanterns) from still clipping.
            var tonemap = profile.Add<Tonemapping>(true); AssetDatabase.AddObjectToAsset(tonemap, profile);
            tonemap.active = true; tonemap.mode.Override(TonemappingMode.Neutral);
            var grade = profile.Add<ColorAdjustments>(true); AssetDatabase.AddObjectToAsset(grade, profile);
            grade.active = true; grade.postExposure.Override(-1f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static void BuildLightingRenderer()
        {
            // A dedicated pipeline keeps the legacy full-screen fog out of Lighting only.
            const string folder="Assets/LightingBlocks/Resources";
            Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var source=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var sourceData=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            var renderer=UnityEngine.Object.Instantiate(sourceData);renderer.name="Lighting Renderer";
            renderer.rendererFeatures.RemoveAll(feature=>feature is FullScreenPassRendererFeature pass&&pass.passMaterial!=null&&pass.passMaterial.name=="M_PlayerFog");
            const string rendererPath="Assets/LightingBlocks/Art/Lighting/LightingRenderer.asset";
            var existing=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if(existing==null){AssetDatabase.CreateAsset(renderer,rendererPath);existing=renderer;}else {EditorUtility.CopySerialized(renderer,existing);UnityEngine.Object.DestroyImmediate(renderer);}
            AddWisdomFogMaskFeature(existing,rendererPath);
            AddFogFeature(existing,rendererPath);
            var pipeline=UnityEngine.Object.Instantiate(source);pipeline.name="Lighting Pipeline";pipeline.renderScale=1;
            var serialized=new SerializedObject(pipeline);var list=serialized.FindProperty("m_RendererDataList");list.arraySize=1;list.GetArrayElementAtIndex(0).objectReferenceValue=existing;serialized.ApplyModifiedPropertiesWithoutUndo();
            const string pipelinePath=folder+"/LightingPipeline.asset";var old=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if(old==null)AssetDatabase.CreateAsset(pipeline,pipelinePath);else {EditorUtility.CopySerialized(pipeline,old);UnityEngine.Object.DestroyImmediate(pipeline);EditorUtility.SetDirty(old);}
            EditorUtility.SetDirty(existing);AssetDatabase.SaveAssets();
        }
        // Opaque-world fog pass (spec: "after opaques/skybox and before transparents and post-
        // processing"): the built-in FullScreenPassRendererFeature, injected BeforeRenderingTransparents,
        // running M_LightingFog (LightingFog.shader - reconstructs world position from the depth
        // texture, fades to black by XZ distance from LightingFog.cs's _LightingFogCenter global).
        // Scoped to Lighting by construction: this feature only ever gets added to LightingRenderer.asset,
        // never to PC_Renderer/Mobile_Renderer, so legacy scenes' renderers never carry it.
        static void AddWisdomFogMaskFeature(UniversalRendererData renderer,string rendererPath)
        {
            foreach(var stale in AssetDatabase.LoadAllAssetsAtPath(rendererPath).OfType<RenderObjects>().Where(f=>f.name=="Lighting Wisdom Fog Mask").ToArray())
                UnityEngine.Object.DestroyImmediate(stale,true);
            var feature=ScriptableObject.CreateInstance<RenderObjects>();
            feature.name="Lighting Wisdom Fog Mask";
            feature.settings.passTag="Lighting Wisdom Fog Mask";
            feature.settings.Event=(RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents-1);
            feature.settings.filterSettings.RenderQueueType=RenderQueueType.Opaque;
            feature.settings.filterSettings.LayerMask=1<<31;
            feature.settings.filterSettings.PassNames=new[]{"LightingFogMask"};
            feature.settings.overrideMode=RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None;
            feature.settings.overrideDepthState=false;
            feature.settings.enableWrite=false;
            AssetDatabase.AddObjectToAsset(feature,renderer);
            renderer.rendererFeatures.Add(feature);
        }
        static void AddFogFeature(UniversalRendererData renderer,string rendererPath)
        {
            // CopySerialized above just overwrote rendererFeatures with PC_Renderer's own list (which
            // never included this one), so any feature object a previous rebuild added is now an
            // orphaned subasset of this same file - destroy it before adding a fresh one, same
            // idempotency concern as VoidProfile()'s volume components below.
            foreach(var stale in AssetDatabase.LoadAllAssetsAtPath(rendererPath).OfType<FullScreenPassRendererFeature>().Where(f=>f.name=="Lighting Fog").ToArray())
                UnityEngine.Object.DestroyImmediate(stale,true);
            var feature=ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name="Lighting Fog";
            feature.passMaterial=FogMaterial();
            feature.injectionPoint=FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
            feature.requirements=ScriptableRenderPassInput.Depth;
            feature.bindDepthStencilAttachment=true;
            AssetDatabase.AddObjectToAsset(feature,renderer);
            renderer.rendererFeatures.Add(feature);
        }
        static Material FogMaterial()
        {
            string path=Art+"/Materials/M_LightingFog.mat";
            var shader=Shader.Find("LightingBlocks/Lighting Fog");
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
            material.shader=shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---- captures --------------------------------------------------------------------------
        // Play-mode driven, same poll + SessionState pattern as ForestVerification/TwilightBatch:
        // -executeMethod returns immediately, so entering Play Mode (inherently async) has to be
        // picked up on a later editor tick. Run without -quit (this drives its own EditorApplication.Exit)
        // and without -nographics (captures need a real render).

        static bool capturing; static int frame;
        const string CaptureRequest = "Library/Lighting.capture-request";
        const string CaptureResult = "Library/Lighting.capture-result.txt";

        static LightingSceneBuilder()
        {
            EditorApplication.update += PollCapture;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Lighting.Capturing", false)) { capturing = true; frame = 0; }
                if (change == PlayModeStateChange.EnteredEditMode) { capturing = false; SessionState.SetBool("Lighting.Capturing", false); }
            };
        }
        static void PollCapture()
        {
            if (!capturing)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(CaptureRequest)) return;
                File.Delete(CaptureRequest);
                EditorSceneManager.OpenScene(ScenePath);
                SessionState.SetBool("Lighting.Capturing", true);
                EditorApplication.isPlaying = true;
                return;
            }
            var game = GameManager.Instance;
            if (game == null) return;
            game.persistenceEnabled = false;
            if (++frame == 50) game.StartCoroutine(CaptureRoutine(game));
        }
        [MenuItem("Tools/Lighting Blocks/Capture Lighting Overview")]
        public static void RequestCapture() => File.WriteAllText(CaptureRequest, "go");
        // -executeMethod entry point for batchmode: requests the capture and lets the coroutine below
        // exit the process once both PNGs are written.
        public static void BatchCapture() { SessionState.SetBool("Lighting.CaptureBatch", true); RequestCapture(); }

        static void Teleport(GameManager game, Vector3 p)
        {
            var controller = game.player.GetComponent<CharacterController>();
            controller.enabled = false; game.player.transform.position = new Vector3(p.x, .025f, p.z); controller.enabled = true;
            Physics.SyncTransforms();
        }
        static IEnumerator CaptureRoutine(GameManager game)
        {
            string folder = "docs/art-direction/lighting"; Directory.CreateDirectory(folder);
            game.player.readInput = false; game.cameraRig.readInput = false;
            var forest = ForestWorld.For(game);
            var config = forest != null ? forest.configuration : null;
            var rig = game.cameraRig;

            // Overview: a wide shot from south of island 1, looking north over both gaps, so all
            // three hubs and both bridges read in one frame - distinct from the gameplay shot below.
            // The normal 26-degree play lens can't fit ~400 m of world inside its far clip plane
            // (420), so this shot temporarily widens the FOV and pushes the far clip out; both are
            // restored before the gameplay capture so that shot keeps its ordinary cinematic lens.
            // Derived from Landscape.Span/CentreZ (not hardcoded z literals), so this keeps working
            // if the profile's geometry numbers ever change.
            // The focus point has to be real, standable ground - Landscape.CentreZ sits in the
            // middle of the whole span, which lands in gap 0 (no platform there on a fresh save with
            // no blocks placed yet), so the player fell into the void and RecoverPlayer snapped the
            // camera back onto whatever it last stood on instead. Bound the focus to island 1's own
            // polygon instead (biased toward its southern edge, so the rest of the world - both
            // gaps, hubs 2 and 3 - reads ahead of it, like the reference's foreground-hub framing).
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in Landscape.Shores[0]) { minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            float focusZ = Mathf.Lerp(minZ, maxZ, .3f);
            var overviewCamera = rig.GetComponent<Camera>();
            float savedFov = overviewCamera.fieldOfView, savedFar = overviewCamera.farClipPlane;
            Teleport(game, new Vector3(0, 0, focusZ));
            rig.CenterOnPlayer(); rig.pitch = 35; rig.yaw = 0; rig.distance = Landscape.Span * .38f;
            overviewCamera.fieldOfView = 55; overviewCamera.farClipPlane = Landscape.Span * 2f;
            for (int i = 0; i < 30; i++) { rig.SendMessage("LateUpdate"); yield return null; }
            SceneCapture.CaptureCamera(overviewCamera, folder + "/overview.png");
            overviewCamera.fieldOfView = savedFov; overviewCamera.farClipPlane = savedFar;

            // Gameplay angle: close at the Wisdom Tree, matching the reference's own composition
            // (tree + player + the first few path blocks, rift extending away) - distinct from the
            // overview's pulled-back framing. Deliberately much closer than a first pass at distance
            // 13 (world distance ~32): the tree's LOD group only shows its real canopy/trunk geometry
            // above 30% of screen height (LODGroup.m_LODs[0].screenRelativeHeight), and anything
            // farther than roughly this falls back to a low-poly stand-in that reads as a blob.
            Vector3 treePos = config != null && config.islands.Length > 0 ? config.islands[0].wisdomPosition : Vector3.zero;
            Teleport(game, treePos + new Vector3(4, 0, -3));
            rig.CenterOnPlayer(); rig.pitch = 24; rig.yaw = 35; rig.distance = 4.5f;
            for (int i = 0; i < 30; i++) { rig.SendMessage("LateUpdate"); yield return null; }
            SceneCapture.CaptureCamera(rig.GetComponent<Camera>(), folder + "/gameplay.png");

            // UI review shots (Library, not committed): the ability bar mid-cooldown and the skill tree
            // with a locked, bronze, silver and gold skill. Overlay UI needs the Game view, so no batch mode.
            if (!Application.isBatchMode)
            {
                string ui = "Library/LightingUiCaptures"; Directory.CreateDirectory(ui);
                var hud = GameHUD.Current; hud.BeginJourney();
                game.dashLevel = 0; game.grenadeLevel = 3; game.pullLevel = 2; game.speed = 1; game.soulGainLevel = 5;
                var pull = game.GetComponent<LightingPull>(); if (pull != null) pull.CooldownLeft = 6;
                hud.SetView(GameHUD.ScreenView.Playing); for (int i = 0; i < 10; i++) yield return null;
                ScreenCapture.CaptureScreenshot(ui + "/hud.png"); yield return null; yield return null;
                hud.SetView(GameHUD.ScreenView.Skills); for (int i = 0; i < 90; i++) yield return null;
                ScreenCapture.CaptureScreenshot(ui + "/skills.png"); yield return null; yield return null;
            }

            File.WriteAllText(CaptureResult, "SUCCESS: captures in " + folder);
            Debug.Log("Lighting captures written to " + folder);
            EditorApplication.isPlaying = false;
            if (SessionState.GetBool("Lighting.CaptureBatch", false))
            {
                SessionState.SetBool("Lighting.CaptureBatch", false);
                EditorApplication.Exit(0);
            }
        }
    }
}
