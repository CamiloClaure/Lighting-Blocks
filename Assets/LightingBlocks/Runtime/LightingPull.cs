using UnityEngine;
using UnityEngine.InputSystem;
namespace LightingBlocks
{
    // Spirit Pull, a Lighting skill: F draws every spirit dot within the Light Grenade's radius into the
    // wallet, over tiles or the open void (walking still needs the tile under a dot). Free, with a cooldown.
    public sealed class LightingPull : MonoBehaviour
    {
        // ponytail: tune. Seconds between pulls.
        public const float Cooldown = 10f;
        // Same radius per level as the Light Grenade, by request.
        public static int Radius(int level) => LightingGrenade.Radius(level);

        GameManager game;
        public float CooldownLeft { get; set; }

        void Awake() { game = GetComponent<GameManager>(); }

        void Update()
        {
            if (game == null || game.player == null || GameHUD.SimulationPaused) return;
            CooldownLeft = Mathf.Max(0, CooldownLeft - Time.deltaTime);
            var keyboard = Keyboard.current;
            if (game.pullLevel > 0 && game.player.readInput && !GameHUD.GameplayBlocked && Application.isFocused
                && keyboard != null && keyboard.fKey.wasPressedThisFrame) TryPull();
        }

        public bool TryPull()
        {
            if (!BridgeProfile.Current.isLighting || game.pullLevel <= 0 || GameHUD.GameplayBlocked) return false;
            if (CooldownLeft > 0) { game.Notify("SPIRIT PULL RECHARGING\nReady in " + Mathf.CeilToInt(CooldownLeft) + " s.", false); return false; }
            var world = ForestWorld.For(game); var at = game.player.transform.position;
            int pulled = world != null && world.Dots != null ? world.Dots.Pull(at, Radius(game.pullLevel) * LightingBridgeGrid.CellSize) : 0;
            if (pulled == 0) { game.Notify("NO PARTICLES IN RANGE\nSpirit Pull reaches " + Radius(game.pullLevel) + " tiles around you.", false); return false; }
            CooldownLeft = Cooldown;
            ForestSoundscape.Play("release", at, .12f);
            var audio = world.GetComponent<LightingPickupAudio>(); if (audio != null) audio.PlayPickup();
            game.Save();
            return true;
        }
    }
}
