using UnityEngine;

namespace LightingBlocks
{
    // Lighting uses one dry impact per stride, including on bridge tiles.
    public sealed class LightingFootsteps : MonoBehaviour
    {
        readonly AudioClip[] clips = new AudioClip[4];
        AudioSource source;
        int next;
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
            for (int i = 0; i < clips.Length; i++)
                clips[i] = Resources.Load<AudioClip>("Audio/LightingSteps/step_" + i);
        }
        public void PlayStep()
        {
            var clip = clips[next++ % clips.Length];
            if (clip == null || GameHUD.SimulationPaused) return;
            // ForestSoundscape may route sources during Awake. Keep these impacts dry
            // while honoring the same saved volume controls without mixer effects.
            source.outputAudioMixerGroup = null;
            source.volume = .32f * ForestSoundscape.Volume("Master")
                * ForestSoundscape.Volume("Effects") * ForestSoundscape.Volume("Footsteps");
            source.clip = clip;
            source.Play();
        }
        void Update() { if (GameHUD.SimulationPaused && source.isPlaying) source.Stop(); }
        void OnApplicationFocus(bool focused) { if (!focused) source.Stop(); }
        void OnDisable() { if (source != null) source.Stop(); }
    }
}
