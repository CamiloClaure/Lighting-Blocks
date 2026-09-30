using UnityEngine;

namespace LightingBlocks
{
    // Lighting plays one note per floor cell entered (BridgeSteps), the reference prototype's
    // platform_step - byte-identical to PasosNotas "04LB A#".
    public sealed class LightingFootsteps : MonoBehaviour
    {
        AudioClip clip;
        AudioSource source;
        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.dopplerLevel = 0;
            source.reverbZoneMix = 0;
            source.bypassReverbZones = true;
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            clip = Resources.Load<AudioClip>("Audio/SFX/PasosNotas/04LB A#");
        }
        public void PlayStep()
        {
            if (clip == null || GameHUD.SimulationPaused) return;
            // ForestSoundscape may route sources during Awake. Keep these impacts dry
            // while honoring the same saved volume controls without mixer effects.
            source.outputAudioMixerGroup = null;
            // ponytail: .5 tuned by ear against the pickup chime; the sliders scale it from here.
            source.volume = .5f * ForestSoundscape.Volume("Master")
                * ForestSoundscape.Volume("Effects") * ForestSoundscape.Volume("Footsteps");
            source.clip = clip;
            source.Play();
        }
        void Update() { if (GameHUD.SimulationPaused && source.isPlaying) source.Stop(); }
        void OnApplicationFocus(bool focused) { if (!focused) source.Stop(); }
        void OnDisable() { if (source != null) source.Stop(); }
    }
}
