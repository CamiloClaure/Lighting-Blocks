using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
namespace LightingBlocks
{
    // Light Grenade, a Lighting skill: hold right mouse to charge, release to throw a big spirit along the
    // camera's facing. It arcs down to tile level and bursts there: GameManager.GrenadeBlast lays tiles over
    // the void within the blast radius and relights the tiles already there. The charge sets the reach, at
    // most the blast radius, so the blast holds the thrower's side of the network and every tile connects.
    public sealed class LightingGrenade : MonoBehaviour
    {
        // ponytail: tune. Blast radius and farthest throw in tiles, per level; particles per throw.
        public static readonly int[] GrenadeTiles = { 5, 6, 7 };
        public const int ThrowCost = 10;
        public const float ChargeSeconds = 1f, Size = .6f;
        const int RingPoints = 64;

        GameManager game; Material spirit, ink; System.Collections.Generic.IEnumerator<int> blast;
        Transform ball; LineRenderer ring;
        float charge = -1, flight, flightTime, arc; Vector3 from, to; int radius;
        public bool Flying { get; private set; }
        // In flight or still laying its blast (GameManager.GrenadeBlast runs a few tiles a frame).
        public bool Busy => Flying || blast != null;
        public bool Charging => charge >= 0;
        public Vector3 Landing => to;
        public static int Radius(int level) => GrenadeTiles[Mathf.Clamp(level, 1, GrenadeTiles.Length) - 1];

        void Awake() { game = GetComponent<GameManager>(); }

        void Update()
        {
            if (game == null || game.player == null) return;
            if (Busy && !GameHUD.SimulationPaused) Tick(Time.deltaTime);
            var mouse = Mouse.current;
            bool live = game.grenadeLevel > 0 && game.player.readInput && !GameHUD.GameplayBlocked && Application.isFocused
                && mouse != null && game.player.HeldBox == null && !Busy;
            if (!live) { Cancel(); return; }
            if (mouse.rightButton.wasPressedThisFrame) charge = 0;
            if (charge < 0) return;
            if (mouse.rightButton.isPressed) { charge = Mathf.Min(1, charge + Time.deltaTime / ChargeSeconds); ShowRing(Target(charge)); return; }
            float released = charge; Cancel(); TryThrow(released);
        }
        void Cancel() { charge = -1; if (ring != null) ring.enabled = false; }

        // Where a throw at this charge lands: along the camera's flat forward (the body's facing as a
        // fallback, like a standing dash), on the tile tops at y 0.
        public Vector3 Target(float amount)
        {
            var player = game.player;
            Vector3 forward = player.viewCamera != null ? Vector3.ProjectOnPlane(player.viewCamera.transform.forward, Vector3.up) : Vector3.zero;
            if (forward.sqrMagnitude < .0001f && player.visual != null) forward = Vector3.ProjectOnPlane(player.visual.forward, Vector3.up);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
            var target = player.transform.position + forward.normalized * (Mathf.Clamp01(amount) * Radius(game.grenadeLevel) * LightingBridgeGrid.CellSize);
            target.y = 0; return target;
        }

        public bool TryThrow(float amount)
        {
            if (!BridgeProfile.Current.isLighting || Busy || game.grenadeLevel <= 0 || GameHUD.GameplayBlocked
                || game.player.HeldBox != null || !game.CanStand(game.player.transform.position)) return false;
            var world = ForestWorld.For(game);
            if (world == null || !world.TrySpendInventory(ThrowCost * ForestWorld.ParticleUnitsPerParticle))
            {
                game.Notify("NOT ENOUGH PARTICLES\nA Light Grenade costs " + ThrowCost + " particles. Have " + (world == null ? "0" : world.SharedInventoryLabel) + ".", false);
                return false;
            }
            radius = Radius(game.grenadeLevel);
            to = Target(amount); from = game.player.transform.position + Vector3.up * 1.3f;
            float distance = new Vector2(to.x - from.x, to.z - from.z).magnitude;
            flightTime = .25f + distance / 20f; arc = 1 + .25f * distance; flight = 0; Flying = true;
            if (ball == null) BuildBall();
            ball.position = from; ball.gameObject.SetActive(true);
            return true;
        }

        // The flight is a parametric arc, no physics: it cannot hit anything on the way and always lands
        // on the target. Then the blast lays its tiles a few per call, and one save covers the whole throw.
        public void Tick(float delta)
        {
            if (Flying)
            {
                flight += Mathf.Max(0, delta); float t = Mathf.Clamp01(flight / flightTime);
                ball.position = Vector3.Lerp(from, to, t) + Vector3.up * (arc * 4 * t * (1 - t));
                if (t < 1) return;
                Flying = false; ball.gameObject.SetActive(false);
                ForestSoundscape.Play("crystal", to, .12f);
                blast = game.GrenadeBlast(LightingBridgeGrid.CellOf(to), radius);
            }
            if (blast != null && !blast.MoveNext()) { blast = null; game.Save(); }
        }

        void BuildBall()
        {
            spirit = LightingDots.SpiritMaterial();
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name = "Light grenade";
            ForestWorld.Remove(sphere.GetComponent<Collider>());
            sphere.transform.localScale = Vector3.one * Size;
            var r = sphere.GetComponent<Renderer>(); r.sharedMaterial = spirit; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            // Its own light, so the black glossy floor shows the ball going over.
            var glow = new GameObject("Light grenade glow").AddComponent<Light>(); glow.transform.SetParent(sphere.transform, false);
            glow.type = LightType.Point; glow.color = new Color(1f, .8f, .15f); glow.range = 4; glow.intensity = 1.5f; glow.shadows = LightShadows.None;
            ball = sphere.transform;
        }

        // The blast outline around the landing point while charging.
        void ShowRing(Vector3 center)
        {
            if (ring == null)
            {
                ring = new GameObject("Light grenade aim", typeof(LineRenderer)).GetComponent<LineRenderer>();
                ring.useWorldSpace = true; ring.loop = true; ring.positionCount = RingPoints; ring.widthMultiplier = .08f;
                ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false;
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null) { ink = new Material(shader) { name = "Light grenade aim" }; ink.SetColor("_BaseColor", LightingParticles.ParticleColor); ring.sharedMaterial = ink; }
            }
            float r = Radius(game.grenadeLevel) * LightingBridgeGrid.CellSize;
            for (int i = 0; i < RingPoints; i++) { float a = i * Mathf.PI * 2 / RingPoints; ring.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, .05f, Mathf.Sin(a) * r)); }
            ring.enabled = true;
        }

        void OnDestroy()
        {
            if (ball != null) ForestWorld.Remove(ball.gameObject);
            if (ring != null) ForestWorld.Remove(ring.gameObject);
            ForestWorld.Remove(spirit); ForestWorld.Remove(ink);
        }
    }
}
