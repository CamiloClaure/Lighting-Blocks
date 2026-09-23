using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LightingBlocks.TerrainScene
{
    // Sandbox only, and inert unless the marker file exists, so pressing Play normally never runs it.
    //
    // Why this exists: the terrain shows broad diagonal bands, and at least three things in this
    // scene make concentric world-space rings that read as diagonal bands under a 45 degree camera -
    // terrain patch LOD, the grass instance-count LOD, and per-cascade shadow bias. Guessing costs a
    // round trip each. This captures one frame per suspect with that suspect switched off, so the
    // comparison answers all of them at once.
    //
    // One variable at a time, always back to baseline in between: two changes in one frame cannot be
    // told apart afterwards.
    public sealed class TerrainDiagnostic : MonoBehaviour
    {
        public const string Marker = "Library/LightingBlocks.terrain-diagnose";
        const string Folder = "Library/TerrainDiagnose";

        static string mode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (!File.Exists(Marker)) return;
            mode = File.ReadAllText(Marker).Trim();
            File.Delete(Marker);   // one run per request, so a crash cannot leave the editor looping
            new GameObject("Terrain diagnostic").AddComponent<TerrainDiagnostic>();
        }

        // GameHUD.cs:74 sets Time.timeScale = 0 while a menu or a blocking message is up, and this
        // scene opens with one. Every wait below is therefore realtime: a scaled WaitForSeconds
        // never completes there, which is exactly how the first run hung in play mode.
        static WaitForSecondsRealtime Wait(float seconds) { return new WaitForSecondsRealtime(seconds); }

        IEnumerator Start()
        {
            Directory.CreateDirectory(Folder);
            // Nothing below is allowed to leave the editor stuck in play mode, whatever throws.
            StartCoroutine(Watchdog());
            var log = "";
            GameManager game = null;
            // GameManager builds the world over its first frames. A capture taken before the ground
            // exists proves nothing, so wait for the root rather than for a fixed delay.
            for (float waited = 0; waited < 25; waited += Time.unscaledDeltaTime)
            {
                game = GameManager.Instance;
                if (game != null && game.blocksRoot != null && game.blocksRoot.parent != null
                    && game.blocksRoot.parent.Find("Spectral Forest") != null) break;
                yield return null;
            }
            var camera = game == null || game.cameraRig == null ? Camera.main : game.cameraRig.GetComponent<Camera>();
            if (camera == null) { File.WriteAllText(Folder + "/done.txt", "FAIL: no camera"); Quit(); yield break; }
            if (game != null && game.cameraRig != null)
            {
                // Pull out to 42 m: the grass LOD rings sit at 42 and 54 m, so a close shot would
                // frame the one region where every suspect looks identical.
                game.cameraRig.distance = 42;
                game.cameraRig.CenterOnPlayer();
            }
            yield return Wait(2);

            var terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            var covers = new List<Behaviour>();
            foreach (var c in FindObjectsByType<TerrainCover>(FindObjectsSortMode.None)) covers.Add(c);
            foreach (var c in FindObjectsByType<ForestCoverRenderer>(FindObjectsSortMode.None)) if (c.enabled) covers.Add(c);
            var lights = new List<Light>();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.shadows != LightShadows.None) lights.Add(l);
            log += "terrains " + terrains.Length + ", cover renderers " + covers.Count + ", shadowed lights " + lights.Count + "\n";
            foreach (var t in terrains) log += "  " + t.name + " pixelError " + t.heightmapPixelError + " basemap " + t.basemapDistance
                                            + " shadows " + t.shadowCastingMode
                                            + " material " + (t.materialTemplate == null ? "pipeline default" : t.materialTemplate.name) + "\n";
            foreach (var l in lights) log += "  light " + l.name + " " + l.type + " shadows " + l.shadows
                                          + " intensity " + l.intensity.ToString("0.00") + " range " + l.range.ToString("0.0")
                                          + " bias " + l.shadowBias.ToString("0.000") + "/" + l.shadowNormalBias.ToString("0.000")
                                          + " pipelineBias " + Pipeline(l)
                                          + " euler " + l.transform.eulerAngles + "\n";

            yield return Shot(camera, "0-baseline");

            // Round one proved the bands survive grass-off and terrain LOD 0 and die with shadows
            // off, so they are shadows. Grass stays off for the whole of round two: it hides them.
            foreach (var c in covers) c.enabled = false;
            yield return Shot(camera, "1-no-grass");

            // Rounds two and three ruled out every caster, both bias dials (2/3 and 10/10 changed the
            // mean by 0.2 of 255) and the light's direction, and proved URP's own terrain shader
            // renders the same ground flat at mean 198 std 7. So the fault is inside
            // SpectralLitTerrain. Round four reads its terms one at a time instead of inferring them.
            //
            // Clones, never the material asset: a material edited in play mode keeps the edit.
            var materials = new Material[terrains.Length];
            var probes = new Material[terrains.Length];
            for (int i = 0; i < terrains.Length; i++)
            {
                materials[i] = terrains[i].materialTemplate;
                probes[i] = new Material(materials[i]);
                terrains[i].materialTemplate = probes[i];
            }
            if (mode == "palette")
            {
                // Boost multiplies every channel alike, and Moss averages linear (.041,.100,.029),
                // so at 12 the green channel passes 1.0 and clips while red and blue do not. That
                // clipping is the "too green". These are the candidates, rendered rather than argued.
                var options = new (string name, float boost, Color tint)[]
                {
                    ("a-boost12-current", 12, new Color(.627f, .694f, .690f)),
                    ("b-boost8",           8, new Color(.627f, .694f, .690f)),
                    ("c-boost6",           6, new Color(.627f, .694f, .690f)),
                    ("d-boost4",           4, new Color(.627f, .694f, .690f)),
                    ("e-boost6-lavender",  6, new Color(.72f,  .62f,  .78f)),
                    ("f-boost6-ashgrey",   6, new Color(.70f,  .66f,  .72f)),
                };
                foreach (var o in options)
                {
                    foreach (var m in probes) { m.SetFloat("_DetailBoost", o.boost); m.SetColor("_BaseColor", o.tint); }
                    yield return Shot(camera, o.name);
                }
            }
            else
            {
                string[] views = { "2-shadow-attenuation", "3-world-normal", "4-albedo", "5-ndotl",
                                   "6-vertex-normal", "7-tangent-space-normal" };
                for (int v = 0; v < views.Length; v++)
                {
                    foreach (var m in probes) m.SetFloat("_Debug", v + 1);
                    yield return Shot(camera, views[v]);
                }
                foreach (var m in probes) m.SetFloat("_Debug", 0);
            }
            for (int i = 0; i < terrains.Length; i++) terrains[i].materialTemplate = materials[i];

            foreach (var c in covers) c.enabled = true;
            yield return Shot(camera, "8-baseline-again");

            File.WriteAllText(Folder + "/done.txt", "SUCCESS\n" + log);
            Quit();
        }

        // "True" means the light takes its bias from the URP asset (depth .1, normal .5 on PC)
        // rather than from its own sliders.
        static string Pipeline(Light light)
        {
            var data = light.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            return data == null ? "no-data" : data.usePipelineSettings.ToString();
        }

        IEnumerator Shot(Camera camera, string name)
        {
            // Two frames, not one: a toggle applied this frame is only drawn on the next, and
            // TwilightEvidence.Capture renders the camera itself rather than reading the back buffer.
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            TwilightEvidence.Capture(camera, Folder + "/" + name + ".png", false);
        }

        // A capture run is 6 frames of work. If it is not finished in 90 realtime seconds something
        // threw, and leaving someone's editor parked in play mode is worse than a partial answer.
        IEnumerator Watchdog()
        {
            yield return Wait(90);
            if (!File.Exists(Folder + "/done.txt"))
                File.WriteAllText(Folder + "/done.txt", "TIMEOUT: watchdog fired, see whichever shots exist");
            Quit();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
