using UnityEngine;

namespace LightingBlocks
{
    // Occupancy lighting: a bridge block lights up while the player stands on it and fades to
    // nothing over three seconds after they leave. Only reaches the scene when
    // BridgeProfile.Current.occupancyLight is set (attached by WorldDressing.Build); the default
    // profile never adds this component, so SampleScene is unaffected.
    public sealed class BridgeSteps : MonoBehaviour
    {
        static readonly string[] NoteFiles = { "07LB D#" };
        const float NoteCooldown = .12f;
        GameManager game;
        BridgeBlock current;
        AudioClip[] notes;
        AudioSource source;
        float nextNote;

        void Awake()
        {
            game = GetComponent<GameManager>();
            notes = new AudioClip[NoteFiles.Length];
            for (int i = 0; i < NoteFiles.Length; i++)
            {
                var clip = Resources.Load<AudioClip>("Audio/SFX/PasosNotas/" + NoteFiles[i]);
                notes[i] = clip != null ? clip : Resources.Load<AudioClip>("Audio/SFX/PasosEnBloque");
            }
            // Its own GameObject: an ASMR cue for the player, not routed through any world source.
            var host = new GameObject("Bridge step notes");
            host.transform.SetParent(transform, false);
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.volume = .5f;
            ForestSoundscape.Route(source, "Effects");
        }

        void Update()
        {
            if (game == null || game.player == null || GameHUD.SimulationPaused) return;
            // Real seconds, clamped like Footlights.Update already documents: one slow frame
            // (loading, alt-tab, a breakpoint) must not retire a three second fade in one step.
            float step = Mathf.Min(.1f, Time.unscaledDeltaTime);
            var tile = game.BlockUnder(game.player.transform.position);
            if (tile != current)
            {
                current = tile;
                // Every entry sounds, including stepping right back onto the tile you just left.
                // ponytail: cooldown ceiling is BlockUnder's box test (.99 x 1.02 half-extents on a
                // 2m pitch) flickering across a seam and machine-gunning the note; top speed
                // (moveSpeed caps at 9.9 m/s) still takes .2s to cross one tile, so .12s never eats
                // a real step. A future speed upgrade under .12s/tile needs a per-tile debounce instead.
                if (tile != null && Time.unscaledTime >= nextNote)
                {
                    PlayNote(tile);
                    nextNote = Time.unscaledTime + NoteCooldown;
                }
            }
            // Plain foreach, no LINQ, no closures: this runs over every block every frame.
            foreach (var block in game.Blocks)
            {
                float value = block == current ? 1f : Mathf.Max(0f, block.lit - step / 3f);
                if (!block.placed) value = 0f;
                if (value == block.lit) continue; // already settled, costs nothing
                block.lit = value;
                WorldDressing.StepGlow(block, value);
            }
        }

        // Position, not chance, picks the note: a crossing plays a rising line as you walk it.
        void PlayNote(BridgeBlock block)
        {
            // Lighting already plays a dry impact on each physical stride.
            if (BridgeProfile.Current.isLighting) return;
            var clip = notes[(block.gap * GameManager.Rows + block.row) % notes.Length];
            if (clip == null) return;
            source.PlayOneShot(clip);
        }
    }
}
