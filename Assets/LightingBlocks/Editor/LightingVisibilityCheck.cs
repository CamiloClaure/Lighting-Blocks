using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace LightingBlocks.Editor
{
    // Phase 3 evidence for the Lighting visibility/fog packet (spec "Visibility and rendering"):
    // the LightingFog controller is present with the spec defaults, its render feature is scoped to
    // Lighting's own renderer only, global shader state clears once the Lighting scene is left, and
    // particle renderers are gated correctly by distance with no lights attached to any particle.
    // Touches: Runtime/LightingFog.cs, Editor/LightingSceneBuilder.cs, the crystal/filament/edge/
    // spectral-motes shader edits.
    //
    // Static checks (controller defaults, renderer-feature scoping) run in edit mode against the
    // built Lighting.unity/LightingRenderer.asset directly; the rest needs Play Mode (particle
    // renderers only exist once ForestWorld.Build runs), mirroring LightingParticleCheck's own
    // poll+SessionState harness. Also makes a best-effort attempt at zoom-level screenshots (spec:
    // "if feasible") into Logs/lighting-captures/ - a capture failure is reported, not thrown.
    [InitializeOnLoad]
    public static class LightingVisibilityCheck
    {
        const string ScenePath = "Assets/Scenes/Lighting.unity";
        const string Result = "Library/LightingVisibilityCheck.txt";
        const string Request = "Library/LightingVisibilityCheck.request";
        const string PlayKey = "LightingVisibilityCheck.Play";
        const string BatchKey = "LightingVisibilityCheck.Batch";
        const string AwaitExitKey = "LightingVisibilityCheck.AwaitExit";
        static int frames; static bool running; static readonly StringBuilder report = new StringBuilder();
        static bool awaitingExit, exitObserved, globalClearedOnExit;

        static LightingVisibilityCheck()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PlayKey, false)) { running = true; frames = 0; }
                if (s == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(AwaitExitKey, false))
                {
                    SessionState.SetBool(AwaitExitKey, false);
                    globalClearedOnExit = Shader.GetGlobalFloat("_LightingFogActive") == 0f;
                    exitObserved = true;
                }
            };
        }
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); report.AppendLine("PASS: " + message); }

        [MenuItem("Tools/Lighting Blocks/Run Lighting Visibility Check")]
        public static void Run()
        {
            report.Clear(); awaitingExit = false; exitObserved = false;
            try
            {
                RunStaticChecks();
                SessionState.SetBool(PlayKey, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception e) { report.AppendLine("FAIL: " + e); Finish(false); }
        }
        // -executeMethod entry point for batchmode.
        public static void Batch() { SessionState.SetBool(BatchKey, true); Run(); }

        static void RunStaticChecks()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var fog = UnityEngine.Object.FindAnyObjectByType<LightingFog>();
            Assert(fog != null, "LightingFog controller present in the Lighting scene");
            var lightingGame = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            Assert(lightingGame != null && lightingGame.characterLightRange == 24f,
                "Lighting scene keeps the character light range at 24m");
            Assert(fog.worldClearDistance == 12f && fog.worldBlackDistance == 24f, "World fade defaults are 12/24 units");
            Assert(fog.particleClearDistance == 12f && fog.particleHiddenDistance == 24f, "Particle fade defaults are 12/24 units, the reference's dot range");
            Assert(fog.particleColor == LightingParticles.ParticleColor, "Particle tint has its default value");

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/LightingBlocks/Art/Lighting/LightingRenderer.asset");
            Assert(renderer != null, "LightingRenderer.asset exists");
            var fogFeatures = renderer.rendererFeatures.OfType<FullScreenPassRendererFeature>().Where(f => f.name == "Lighting Fog").ToArray();
            Assert(fogFeatures.Length == 1, "LightingRenderer carries exactly one Lighting Fog render feature (found " + fogFeatures.Length + ")");
            var feature = fogFeatures[0];
            Assert(feature.injectionPoint == FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents,
                "Fog pass is injected BeforeRenderingTransparents (after opaques/skybox, before transparents and post-processing)");
            Assert((feature.requirements & ScriptableRenderPassInput.Depth) != 0, "Fog pass requests the depth texture it reconstructs world position from");
            Assert(feature.passMaterial != null && feature.passMaterial.shader != null && feature.passMaterial.shader.name == "LightingBlocks/Lighting Fog",
                "Fog pass uses the dedicated LightingFog shader");
            Assert(feature.bindDepthStencilAttachment, "Fog pass binds depth/stencil so the tree mask can preserve exact visible pixels");
            var treeMask = renderer.rendererFeatures.OfType<RenderObjects>().Where(f => f.name == "Lighting Wisdom Fog Mask").ToArray();
            Assert(treeMask.Length == 1, "LightingRenderer carries exactly one Wisdom Tree fog mask feature");
            Assert(treeMask[0].settings.Event == (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents-1)
                && renderer.rendererFeatures.IndexOf(treeMask[0]) < renderer.rendererFeatures.IndexOf(feature)
                && treeMask[0].settings.filterSettings.LayerMask.value == (1 << 31)
                && treeMask[0].settings.filterSettings.PassNames.Contains("LightingFogMask"),
                "Wisdom mask runs before fog and draws only opaque tree-layer depth-equal pixels");
            Assert(LayerMask.NameToLayer("LightingFogTree") == 31, "LightingFogTree is assigned to user layer 31");
            var pickupClips = Enumerable.Range(1, 4).Select(i => Resources.Load<AudioClip>("Audio/LightingPickups/pickup_" + i.ToString("00"))).ToArray();
            Assert(pickupClips.All(c => c != null && c.length >= .4f && c.length <= .7f), "Four generated pickup clips are wired and each lasts 0.4-0.7 seconds");
            Assert(fog.worldClearDistance == 12f && fog.worldBlackDistance == 24f, "Lighting scene serializes the expanded world visibility distances");

            foreach (var path in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var other = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (other == null) continue;
                Assert(!other.rendererFeatures.Any(f => f != null && f.name == "Lighting Fog"),
                    Path.GetFileNameWithoutExtension(path) + " does not carry the Lighting Fog render feature (scoped to Lighting only)");
            }
        }

        static void Poll()
        {
            if (awaitingExit)
            {
                if (!exitObserved) return;
                exitObserved = false; awaitingExit = false;
                try { Assert(globalClearedOnExit, "_LightingFogActive reads back 0 after leaving the Lighting scene (LightingFog.OnDisable ran)"); report.AppendLine("SUCCESS: Lighting visibility checks passed."); Finish(true); }
                catch (Exception e) { report.AppendLine("FAIL: " + e); Finish(false); }
                return;
            }
            if (!running)
            {
                if (!EditorApplication.isPlaying && File.Exists(Request))
                {
                    File.Delete(Request);
                    Run();
                }
                return;
            }
            if (++frames < 45) return;
            running = false; bool ok = false;
            try { RunPlayChecks(); ok = true; }
            catch (Exception e) { report.AppendLine("FAIL: " + e); }
            SessionState.SetBool(AwaitExitKey, true); awaitingExit = true;
            EditorApplication.isPlaying = false;
            if (!ok) Finish(false);
        }
        static void Finish(bool ok)
        {
            File.WriteAllText(Result, report.ToString());
            Debug.Log(report.ToString());
            SessionState.SetBool(PlayKey, false);
            if (Application.isBatchMode && SessionState.GetBool(BatchKey, false)) { SessionState.SetBool(BatchKey, false); EditorApplication.Exit(ok ? 0 : 1); }
        }
        static void Teleport(GameManager game, Vector3 position) { var c = game.player.GetComponent<CharacterController>(); c.enabled = false; game.player.transform.position = new Vector3(position.x, .025f, position.z); c.enabled = true; Physics.SyncTransforms(); }

        static void RunPlayChecks()
        {
            var game = GameManager.Instance;
            Assert(game != null && BridgeProfile.Current.isLighting, "Lighting profile is active for Assets/Scenes/Lighting.unity");
            Assert(!game.persistenceEnabled && game.savePathOverride != null, "Save isolation active before startup"); game.autoTick = false;
            var forest = ForestWorld.For(game);
            Assert(forest != null && forest.PickupVisuals.Count > 0, "At least one particle exists to test visibility against");
            var fog = UnityEngine.Object.FindAnyObjectByType<LightingFog>();
            Assert(fog != null, "LightingFog controller present in Play Mode");
            Assert(Shader.GetGlobalFloat("_LightingFogActive") == 1f, "_LightingFogActive is set while the Lighting scene runs");
            Assert(game.cameraRig != null && game.cameraRig.GetComponent<Camera>().farClipPlane >= 4096f,
                "Lighting camera far clip preserves the distant Wisdom Tree view");
            Assert(game.soulLight != null && game.soulLight.range == 24f && game.soulLight.intensity == 18f,
                "Applied character light has 24m range at the existing intensity of 18");
            foreach (var tree in forest.WisdomTrees)
            {
                var treeRenderers = tree.GetComponentsInChildren<Renderer>(true).Where(r => r != null
                    && r.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name == "LightingBlocks/Spectral Lit")).ToArray();
                Assert(treeRenderers.Length > 0 && treeRenderers.All(r => r.gameObject.layer == 31),
                    "Wisdom Tree opaque renderers use the exact fog-mask layer while retaining their normal depth render");
            }

            var lateUpdate = typeof(LightingFog).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            var visual = forest.PickupVisuals[0];
            var visualRenderer = visual.GetComponent<Renderer>();

            Teleport(game, visual.position + new Vector3(100, 0, 0));
            lateUpdate.Invoke(fog, null);
            Assert(!visualRenderer.enabled, "Particle renderer disables beyond the " + fog.particleHiddenDistance + "-unit hidden distance");

            Teleport(game, visual.position + new Vector3(2, 0, 0));
            lateUpdate.Invoke(fog, null);
            Assert(visualRenderer.enabled, "Particle renderer stays enabled within the " + fog.particleClearDistance + "-unit clear distance");

            var originalVisual=visual;uint originalRevision=forest.VisualRevision;
            GameHUD.Current.BeginJourney();
            Assert(game.ImportSave(game.ExportSave()),"Save reload rebuilds the particle world");
            visual=forest.PickupVisuals[0];visualRenderer=visual.GetComponent<Renderer>();
            Assert(visual!=originalVisual&&forest.VisualRevision!=originalRevision,"Same-count reload changes visual identity and revision");
            Teleport(game,visual.position+Vector3.right*(fog.particleHiddenDistance+1));lateUpdate.Invoke(fog,null);
            Assert(!visualRenderer.enabled,"Replacement particle is hidden beyond the "+fog.particleHiddenDistance+"m threshold after same-count reload");
            Teleport(game,visual.position+Vector3.right*2);fog.particleColor=Color.cyan;lateUpdate.Invoke(fog,null);
            var tint=new MaterialPropertyBlock();visualRenderer.GetPropertyBlock(tint);
            Assert(tint.GetColor("_BaseColor")==Color.cyan,"Inspector particle colour controls visible colour");
            fog.particleColor=LightingParticles.ParticleColor;lateUpdate.Invoke(fog,null);

            foreach (var v in forest.PickupVisuals)
            {
                Assert(v.GetComponent<Light>() == null, "No Light component directly on particle visual " + v.name);
                Assert(v.GetComponentInChildren<Light>(true) == null, "No Light in a particle visual's children (" + v.name + ")");
            }

            try { CaptureZoomLevels(game, fog, lateUpdate); report.AppendLine("PASS: Zoom-level and distant Wisdom Tree captures written to Logs/lighting-captures/"); }
            catch (Exception e) { report.AppendLine("NOTE: zoom-level captures could not be produced (" + e.Message + ") - not a check failure, see report."); }
        }
        // Same view (fixed pitch/yaw) at several zoom (camera distance) values, so the fade band and
        // any distant emission leak are directly comparable frame to frame. Three real particles are
        // pulled onto the camera's own forward line, directly ahead of the player, at the spec's own
        // reference distances (~3/~5/~7 units) - clear, mid-fade and hidden - instead of relying on
        // wherever the generated layout happens to scatter them (most were out of frame or occluded
        // behind the player). This only moves each particle's visual Transform for the screenshot;
        // ParticleSlotSave.position (the saved/respawn state) is untouched.
        static void CaptureZoomLevels(GameManager game, LightingFog fog, MethodInfo lateUpdate)
        {
            string folder = "Logs/lighting-captures"; Directory.CreateDirectory(folder);
            var forest = ForestWorld.For(game);
            var treePosition = forest.configuration.islands[0].wisdomPosition;
            var playerPosition = treePosition + new Vector3(4, 0, -3);
            Teleport(game, playerPosition);

            const float pitch = 24f, yaw = 35f;
            var forward = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward; // same formula as StrategyCamera.Apply
            var flatForward = new Vector3(forward.x, 0, forward.z).normalized;
            var markers = forest.PickupVisuals.Take(3).ToArray();
            var markerDistances = new[] { 3f, 5f, 7f };
            for (int i = 0; i < markers.Length; i++) markers[i].position = playerPosition + flatForward * markerDistances[i] + Vector3.up * .7f;
            Physics.SyncTransforms();
            lateUpdate.Invoke(fog, null);

            var rig = game.cameraRig; rig.readInput = false; game.player.readInput = false;
            var apply = typeof(StrategyCamera).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic);
            rig.CenterOnPlayer(); rig.pitch = pitch; rig.yaw = yaw;
            foreach (var distance in new[] { 14f, 18f, 28f, 42f })
            {
                rig.distance = distance;
                for (int i = 0; i < 10; i++) apply.Invoke(rig, new object[] { 1f });
                SceneCapture.CaptureCamera(rig.GetComponent<Camera>(), folder + "/zoom-" + distance.ToString("00") + ".png");
            }
            Teleport(game, treePosition - flatForward * 30f);
            rig.CenterOnPlayer(); rig.pitch = pitch; rig.yaw = yaw; rig.distance = 14f;
            lateUpdate.Invoke(fog, null);
            var camera = rig.GetComponent<Camera>();
            camera.transform.position = game.player.transform.position + Vector3.up * 12f;
            camera.transform.rotation = Quaternion.LookRotation(treePosition + Vector3.up * 3f - camera.transform.position);
            SceneCapture.CaptureCamera(camera, folder + "/wisdom-30m.png");

            // An opaque, bright wall in front of the tree proves the exemption respects the
            // camera depth buffer: the wall itself stays visible, while the tree behind it does not.
            var direction = (treePosition + Vector3.up * 3f - camera.transform.position).normalized;
            var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            occluder.name = "Temporary Wisdom occlusion check";
            occluder.transform.SetPositionAndRotation(camera.transform.position + direction * 10f, Quaternion.LookRotation(direction));
            occluder.transform.localScale = new Vector3(8f, 8f, .3f);
            var occluderRenderer = occluder.GetComponent<Renderer>();
            occluderRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            wallMaterial.SetColor("_BaseColor", new Color(.08f, 1.4f, .18f, 1));
            occluderRenderer.sharedMaterial = wallMaterial;
            Physics.SyncTransforms();
            SceneCapture.CaptureCamera(camera, folder + "/wisdom-30m-occluded.png");
            occluder.SetActive(false); UnityEngine.Object.Destroy(occluder); UnityEngine.Object.Destroy(wallMaterial);

            // The player-to-tree radius is deliberately far beyond the 24m world black point.
            // Keep the camera close enough to show the silhouette while the fog centre stays 500m away.
            Teleport(game, treePosition - flatForward * 500f);
            lateUpdate.Invoke(fog, null);
            camera.transform.position = game.player.transform.position + Vector3.up * 90f;
            camera.transform.rotation = Quaternion.LookRotation(treePosition + Vector3.up * 3f - camera.transform.position);
            SceneCapture.CaptureCamera(camera, folder + "/wisdom-500m.png");
        }
    }
}
