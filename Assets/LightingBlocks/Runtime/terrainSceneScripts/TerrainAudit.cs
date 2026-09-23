using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LightingBlocks.TerrainScene
{
    // Sandbox only, and inert unless the marker file exists, so pressing Play normally never runs it.
    //
    // Why this exists: something on this terrain draws as large flat black shapes, and a compressed
    // screenshot cannot say whether that is a cover mesh, a piece of WorldDressing left at y=0, or a
    // material sampling the wrong part of the palette atlas. Three suspects, three different fixes,
    // and guessing costs a round trip each.
    //
    // So this reads the scene rather than the picture: every renderer, its height against the
    // terrain underneath it, its size, and the shader and tint it draws with. A thing buried in a
    // hill and a thing that is merely dark look identical in a screenshot and nothing alike here.
    public sealed class TerrainAudit : MonoBehaviour
    {
        public const string Marker = "Library/LightingBlocks.terrain-audit";
        const string Out = "Library/LightingBlocks.terrain-audit.txt";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (!File.Exists(Marker)) return;
            File.Delete(Marker);   // one run per request, so a crash cannot leave the editor looping
            new GameObject("Terrain audit").AddComponent<TerrainAudit>();
        }

        // GameHUD.cs:74 sets Time.timeScale = 0 behind its opening message, so every wait here is
        // realtime. A scaled WaitForSeconds never completes in this scene.
        IEnumerator Start()
        {
            StartCoroutine(Watchdog());
            // The world is assembled over the first frames; auditing before it exists proves nothing.
            GameManager game = null;
            for (float waited = 0; waited < 25; waited += Time.unscaledDeltaTime)
            {
                game = GameManager.Instance;
                if (game != null && game.blocksRoot != null && game.blocksRoot.parent != null
                    && game.blocksRoot.parent.Find("Spectral Forest") != null) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);

            var terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            var report = new StringBuilder();
            report.AppendLine("terrains " + terrains.Length);
            foreach (var t in terrains)
                report.AppendLine("  " + t.name + " origin " + t.transform.position + " size " + t.terrainData.size);

            var rows = new List<Row>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (r.GetComponentInParent<Terrain>() != null) continue;
                var p = r.bounds.center;
                var row = new Row
                {
                    path = Path(r.transform),
                    size = r.bounds.size,
                    foot = r.bounds.min.y,
                    ground = Ground(terrains, p),
                    shader = r.sharedMaterial == null ? "none" : r.sharedMaterial.shader.name,
                    material = r.sharedMaterial == null ? "none" : r.sharedMaterial.name,
                    tint = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")
                         ? r.sharedMaterial.GetColor("_BaseColor") : Color.clear,
                    on = r.enabled && r.gameObject.activeInHierarchy,
                };
                rows.Add(row);
            }

            // Sunk or floating is the whole question, so rank by it rather than by name.
            rows.Sort((a, b) => Mathf.Abs(b.foot - b.ground).CompareTo(Mathf.Abs(a.foot - a.ground)));
            report.AppendLine();
            report.AppendLine("WORST 50 BY DISTANCE FROM THE GROUND (negative = buried)");
            report.AppendLine("  delta  footY groundY   size(x,y,z)          material / shader");
            foreach (var row in rows.Take(50))
                report.AppendLine(string.Format("{0,7:0.00} {1,6:0.0} {2,7:0.0}   {3,-20} {4} / {5}  {6}\n        {7}",
                    row.foot - row.ground, row.foot, row.ground,
                    row.size.x.ToString("0.00") + "," + row.size.y.ToString("0.00") + "," + row.size.z.ToString("0.00"),
                    row.material, row.shader, row.on ? "" : "(OFF)", row.path));

            // The list above is owned by the void: the keels, the background forest and the islets
            // all live tens of metres below y=0 on purpose, and Ground() reports 0 off the terrain.
            // Only a thing standing over actual terrain can be sunk into it or hovering over it.
            report.AppendLine();
            report.AppendLine("WORST 40 STANDING OVER TERRAIN (negative = buried)");
            foreach (var row in rows.Where(r => r.ground > .05f)
                                    .OrderByDescending(r => Mathf.Abs(r.foot - r.ground)).Take(40))
                report.AppendLine(string.Format("{0,7:0.00} {1,6:0.0} {2,7:0.0}   {3,-20} {4}\n        {5}",
                    row.foot - row.ground, row.foot, row.ground,
                    row.size.x.ToString("0.00") + "," + row.size.y.ToString("0.00") + "," + row.size.z.ToString("0.00"),
                    row.material, row.path));

            // A count by family says whether a shape is one landmark or two thousand scattered ones.
            report.AppendLine();
            report.AppendLine("BY FAMILY: count, median |delta|, largest size, material");
            foreach (var group in rows.GroupBy(r => Family(r.path)).OrderByDescending(g => g.Count()))
            {
                var deltas = group.Select(r => Mathf.Abs(r.foot - r.ground)).OrderBy(d => d).ToArray();
                var big = group.OrderByDescending(r => r.size.magnitude).First();
                report.AppendLine(string.Format("{0,5}  {1,6:0.00}  {2,-20} {3,-28} {4}",
                    group.Count(), deltas[deltas.Length / 2],
                    big.size.x.ToString("0.0") + "," + big.size.y.ToString("0.0") + "," + big.size.z.ToString("0.0"),
                    big.material, group.Key));
            }

            File.WriteAllText(Out, "SUCCESS\n" + report);
            Quit();
        }

        struct Row
        {
            public string path, shader, material;
            public Vector3 size; public float foot, ground; public Color tint; public bool on;
        }

        static float Ground(Terrain[] terrains, Vector3 p)
        {
            float best = 0;
            foreach (var t in terrains)
            {
                var origin = t.transform.position;
                var size = t.terrainData.size;
                if (p.x < origin.x || p.z < origin.z || p.x > origin.x + size.x || p.z > origin.z + size.z) continue;
                float h = t.SampleHeight(p) + origin.y;
                if (h > best) best = h;   // terrains overlap at the seams; the highest one is the floor
            }
            return best;
        }

        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (var n = t; n != null; n = n.parent) parts.Add(n.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // Trailing digits are instance numbering, not identity: "Islet 7" and "Islet 12" are one family.
        static string Family(string path)
        {
            var name = path.Substring(path.LastIndexOf('/') + 1);
            name = System.Text.RegularExpressions.Regex.Replace(name, @"[ _-]?\d+$", "");
            int cut = path.LastIndexOf('/');
            return (cut < 0 ? "" : path.Substring(0, cut) + "/") + name;
        }

        IEnumerator Watchdog()
        {
            yield return new WaitForSecondsRealtime(90);
            if (!File.Exists(Out)) File.WriteAllText(Out, "TIMEOUT: the audit did not finish.");
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
