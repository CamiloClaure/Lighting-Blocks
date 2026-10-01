using UnityEngine;
using UnityEngine.InputSystem;
namespace LightingBlocks
{
    // Daybreak, a Lighting skill: V lifts the fog out to RevealChunks chunks (128 m) for 1.5/3/5/8 s by
    // level. LightingFog reads Lift; past the load radius the tiles and dots are drawn from data
    // (LightingTiles.DrawFar, LightingDots.DrawFar). Free; the cooldown starts once the fog is back.
    public sealed class LightingReveal : MonoBehaviour
    {
        // ponytail: tune. Seconds to lift and to return, and between casts.
        public const float Cooldown = 30f, RiseSeconds = .4f, FallSeconds = .8f;
        public const int RevealChunks = 16;
        public static readonly float[] Seconds = { 1.5f, 3f, 5f, 8f };
        public static float Reach => RevealChunks * LightingTiles.ChunkSize * LightingBridgeGrid.CellSize;

        GameManager game;
        float phase; // 0 fog in place, 1 fully lifted
        public float CooldownLeft { get; set; }
        // Seconds the fog stays fully lifted; counts down once it is.
        public float Remaining { get; private set; }
        public bool Active => Remaining > 0 || phase > 0;
        public float Lift => Mathf.SmoothStep(0, 1, phase);

        void Awake() { game = GetComponent<GameManager>(); }

        void Update()
        {
            if (game == null || game.player == null || GameHUD.SimulationPaused) return;
            Step(Time.deltaTime);
            var keyboard = Keyboard.current;
            if (game.revealLevel > 0 && game.player.readInput && !GameHUD.GameplayBlocked && Application.isFocused
                && keyboard != null && keyboard.vKey.wasPressedThisFrame) TryReveal();
        }

        // Rise, hold, fall, then the cooldown.
        public void Step(float delta)
        {
            if (Remaining > 0)
            {
                if (phase < 1) phase = Mathf.Min(1, phase + delta / RiseSeconds);
                else Remaining = Mathf.Max(0, Remaining - delta);
            }
            else if (phase > 0) { phase = Mathf.Max(0, phase - delta / FallSeconds); if (phase == 0) CooldownLeft = Cooldown; }
            else CooldownLeft = Mathf.Max(0, CooldownLeft - delta);
        }

        public bool TryReveal()
        {
            if (!BridgeProfile.Current.isLighting || game.revealLevel <= 0 || GameHUD.GameplayBlocked || Active) return false;
            if (CooldownLeft > 0) { game.Notify("DAYBREAK RECHARGING\nReady in " + Mathf.CeilToInt(CooldownLeft) + " s.", false); return false; }
            Remaining = Seconds[Mathf.Clamp(game.revealLevel, 1, Seconds.Length) - 1];
            ForestSoundscape.Play("release", game.player.transform.position, .12f);
            return true;
        }
    }
}
