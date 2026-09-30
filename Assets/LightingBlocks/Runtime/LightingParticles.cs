using System.Collections.Generic;
using UnityEngine;
namespace LightingBlocks
{
    // Owns each Wisdom Tree's three free colour selectors and the E-key tile request. Collection
    // requires standing on the particle's supporting platform or bridge tile.
    public sealed class LightingParticles : MonoBehaviour
    {
        // How close a receiver must be, straight-line and unobstructed, for E to reach it.
        public const float DepositRange = 3f;
        // Receiver placement (ForestLayoutGenerator.PlaceReceivers/RemoveReceiverClearance): the
        // minimum distance between receiver sites in legacy layouts. Lighting selectors sit beside their
        // Wisdom Tree and no longer reserve particle placement or charge inventory.
        public const float ReceiverSpacing = 8f;
        public const float ReceiverClearance = 2.2f;
        public readonly List<ColorReceiver> Receivers = new List<ColorReceiver>();
        // Receiver/tile colours: blue, violet, amber - matches BridgeBlock's tier 0/1/2 exactly, so a
        // receiver's ColorIndex can be passed straight through as TrySpawnAtWisdomTree's forced tier.
        public static readonly Color[] Colors = { new Color(.14f, .43f, 1), new Color(.69f, .24f, 1), new Color(1, .68f, .22f) };
        // Every fundamental particle: the reference prototype's soul yellow (1, .86, .18).
        public static readonly Color ParticleColor = new Color(1f, .86f, .18f);
        public void ResetWorld() { Receivers.Clear(); }
        // layout.receivers holds up to 3 farthest-point-selected sites for this island (see
        // ForestLayoutGenerator.PlaceReceivers) - index order matches ColorReceiver.ColorIndex
        // (blue/violet/amber). A site missing here means placement could not clear ReceiverSpacing on
        // this island; PlaceReceivers already logged that generation error, so this just skips the cube
        // rather than placing one somewhere unsupported or overlapping.
        public void AddTree(ForestWorld world, WisdomTree tree, Transform parent, IslandForestLayout layout)
        {
            for (int color = 0; color < 3; color++)
            {
                float angle = (color * 120f - 30f) * Mathf.Deg2Rad;
                var selectorPosition = tree.transform.position + new Vector3(Mathf.Cos(angle) * 2.3f, .55f, Mathf.Sin(angle) * 2.3f);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "Free tile selector " + color; cube.transform.SetParent(parent, false);
                cube.transform.position = selectorPosition; cube.transform.localScale = Vector3.one * .8f;
                // Trigger volume accepts E selection but never blocks the player or the tree's output.
                cube.GetComponent<Collider>().isTrigger = true;
                var receiver = cube.AddComponent<ColorReceiver>(); receiver.Tree = tree; receiver.World = world; receiver.ColorIndex = color;
                receiver.Initialize(world.configuration.particleMaterial); Receivers.Add(receiver);
            }
            if (tree.Definition.polygonIndex == 0)
            {
                var lights = new GameObject("Canopy fireflies", typeof(ParticleSystem)); lights.transform.SetParent(tree.transform, false);
                var bounds = new Bounds(tree.transform.position, Vector3.zero); foreach (var renderer in tree.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                lights.transform.position = new Vector3(tree.transform.position.x, bounds.max.y + .5f, tree.transform.position.z);
                var ps = lights.GetComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.startLifetime = 7; main.startSpeed = .04f; main.startSize = .07f; main.maxParticles = 12; main.startColor = new Color(.45f, .4f, .7f, .35f);
                var emission = ps.emission; emission.rateOverTime = 1.5f; var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 2.5f;
                lights.GetComponent<ParticleSystemRenderer>().sharedMaterial = world.configuration.particleMaterial; ps.Play();
            }
        }
        // Nearest receiver within range with a clear, unobstructed line from origin; null if none.
        // ignore excludes a transform's own hierarchy (the player's body sits at/near origin).
        public ColorReceiver Nearest(Vector3 origin, float range, Transform ignore = null)
        {
            ColorReceiver best = null; float bestDistance = range;
            foreach (var receiver in Receivers)
            {
                if (receiver == null) continue;
                float distance = Vector3.Distance(origin, receiver.transform.position);
                if (distance > bestDistance || !Clear(origin, receiver.transform.position, ignore)) continue;
                bestDistance = distance; best = receiver;
            }
            return best;
        }
        static bool Clear(Vector3 from, Vector3 to, Transform ignore)
        {
            var delta = to - from;
            foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (ignore != null && hit.transform.IsChildOf(ignore)) continue;
                if (hit.collider.GetComponent<PlatformSupport>() != null) continue;
                return false;
            }
            return true;
        }
        // E: one press asks the nearest tree selector for a free matching tile.
        public bool TryDeposit(BoxPlayer player)
        {
            if (GameHUD.GameplayBlocked || player == null) return false;
            var selector = Nearest(player.transform.position + Vector3.up * .7f, DepositRange, player.transform);
            return selector != null && selector.RequestFreeTile();
        }
    }
    public sealed class ColorReceiver : MonoBehaviour
    {
        public WisdomTree Tree; public ForestWorld World; public int ColorIndex;
        MaterialPropertyBlock properties; Renderer surface; float rejection, pulse;
        GameObject spark;
        public int Charge => 0;
        public void Initialize(Material material) { surface = GetComponent<Renderer>(); surface.sharedMaterial = material; properties = new MaterialPropertyBlock(); Refresh(); }
        // Requests one free matching tile and shows brief selection feedback.
        public bool RequestFreeTile()
        {
            if (Tree == null || !Tree.RequestTile(ColorIndex)) { rejection = .25f; Refresh(); return false; }
            pulse = .5f;
            if (spark == null) { spark = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(spark.GetComponent<Collider>()); spark.transform.SetParent(transform.parent, true); spark.transform.localScale = Vector3.one * .18f; spark.GetComponent<Renderer>().sharedMaterial = surface.sharedMaterial; }
            spark.GetComponent<Renderer>().SetPropertyBlock(properties); spark.transform.position = transform.position; spark.SetActive(true);
            ForestSoundscape.Play("collect", transform.position, .12f); Refresh(); return true;
        }
        public bool Deposit() => RequestFreeTile();
        public void Refresh()
        {
            if (surface == null) return;
            var c = rejection > 0 ? Color.red : LightingParticles.Colors[ColorIndex];
            float glow = .9f;
            properties.SetColor("_BaseColor", c); properties.SetColor("_EmissionColor", c * glow); surface.SetPropertyBlock(properties);
        }
        void Update()
        {
            if (GameHUD.SimulationPaused) return;
            if (rejection > 0) { rejection = Mathf.Max(0, rejection - Time.deltaTime); Refresh(); }
            if (pulse > 0) { pulse = Mathf.Max(0, pulse - Time.deltaTime); spark.transform.position = Vector3.Lerp(transform.position, Tree.transform.position + Vector3.up * 2, 1 - pulse / .5f); if (pulse == 0) spark.SetActive(false); }
        }
    }
}
