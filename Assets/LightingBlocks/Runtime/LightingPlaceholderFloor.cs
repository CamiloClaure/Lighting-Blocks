using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightingBlocks
{
    // Uses the live placement preview as the visual source, including its exact
    // mesh, filament pattern, material settings and camera-facing dashed outline.
    public sealed class LightingPlaceholderFloor : MonoBehaviour
    {
        readonly List<Object> owned = new List<Object>();
        readonly List<GameObject> roots = new List<GameObject>();
        readonly List<Renderer> hidden = new List<Renderer>();

        public void Build(Transform preview)
        {
            var filters = preview.GetComponentsInChildren<MeshFilter>(true);
            var outline = preview.GetComponentInChildren<LineRenderer>(true);
            var materials = new Dictionary<Renderer, Material>();
            Bounds bounds = new Bounds();
            bool foundBody = false;
            foreach (var filter in filters)
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null) continue;
                var material = new Material(renderer.sharedMaterial);
                // These are the placement preview's property overrides (GameManager).
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.white * 5f);
                if (material.HasProperty("_Highlight")) material.SetFloat("_Highlight", 1f);
                if (material.HasProperty("_Opacity")) material.SetFloat("_Opacity", .10f);
                if (material.HasProperty("_Glow")) material.SetFloat("_Glow", .25f);
                materials.Add(renderer, material); owned.Add(material);
                if (renderer.name == "Crystal body") { bounds = filter.sharedMesh.bounds; foundBody = true; }
            }
            if (!foundBody || outline == null) return;
            var edgeMaterial = new Material(outline.sharedMaterial);
            edgeMaterial.SetColor("_Color", Color.white);
            if (edgeMaterial.HasProperty("_BaseColor")) edgeMaterial.SetColor("_BaseColor", Color.white);
            owned.Add(edgeMaterial);
            var positions = new Vector3[outline.positionCount];
            outline.GetPositions(positions);
            foreach (var support in FindObjectsByType<PlatformSupport>(FindObjectsSortMode.None))
            {
                var collider = support.GetComponent<BoxCollider>();
                var old = support.GetComponent<MeshRenderer>();
                if (collider == null || old == null) continue;
                var root = new GameObject("White placement crystals");
                root.transform.SetParent(support.transform, false); roots.Add(root);
                int count = Mathf.RoundToInt(collider.size.x / 2f);
                float top = collider.center.y + collider.size.y * .5f;
                float firstX = collider.center.x - collider.size.x * .5f + 1f;
                // Batch crystal bodies and filaments per existing platform row.
                foreach (var filter in filters)
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null || !materials.TryGetValue(renderer, out var material)) continue;
                    var instances = new CombineInstance[count];
                    for (int i = 0; i < count; i++)
                        instances[i] = new CombineInstance { mesh = filter.sharedMesh,
                            transform = Matrix4x4.Translate(new Vector3(firstX + i * 2f, top - bounds.max.y, collider.center.z))
                                * preview.worldToLocalMatrix * filter.transform.localToWorldMatrix };
                    var mesh = new Mesh { name = "Preview floor " + renderer.name, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(instances, true, true); owned.Add(mesh);
                    var host = new GameObject(renderer.name, typeof(MeshFilter), typeof(MeshRenderer));
                    host.transform.SetParent(root.transform, false);
                    host.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var output = host.GetComponent<MeshRenderer>(); output.sharedMaterial = material;
                    output.shadowCastingMode = ShadowCastingMode.Off; output.receiveShadows = false;
                }
                // Keep the actual LineRenderer so its width and dashes respond to
                // the moving camera exactly as the placement placeholder does.
                for (int i = 0; i < count; i++)
                {
                    var host = new GameObject("Preview dashed edge", typeof(LineRenderer));
                    host.transform.SetParent(root.transform, false);
                    host.transform.localPosition = new Vector3(firstX + i * 2f, top - bounds.max.y, collider.center.z);
                    var line = host.GetComponent<LineRenderer>();
                    line.useWorldSpace = false; line.loop = outline.loop;
                    line.positionCount = positions.Length; line.SetPositions(positions);
                    line.widthMultiplier = outline.widthMultiplier;
                    line.widthCurve = outline.widthCurve; line.colorGradient = outline.colorGradient;
                    line.textureMode = outline.textureMode; line.alignment = outline.alignment;
                    line.sharedMaterial = edgeMaterial;
                    line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                }
                old.enabled = false; hidden.Add(old);
            }
        }
        void OnDestroy()
        {
            foreach (var renderer in hidden) if (renderer != null) renderer.enabled = true;
            foreach (var root in roots) if (root != null) Destroy(root);
            foreach (var asset in owned) if (asset != null) Destroy(asset);
        }
    }
}
