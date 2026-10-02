using UnityEngine;

namespace LightingBlocks
{
    // Lighting plays one note per floor cell entered (BridgeSteps), the reference prototype's
    // platform_step - byte-identical to PasosNotas "04LB A#" - and a soft ASMR footstep per stride
    // (BoxPlayer.PlayFootstep): close-mic crystal steps from Tools/audio/build_asmr_steps.py, odd
    // clips the left foot, even the right.
    public sealed class LightingFootsteps : MonoBehaviour
    {
        public const float NoteVolume = .3f, StrideVolume = .22f;
        AudioClip clip;
        AudioSource source, stride;
        readonly AudioClip[] steps = new AudioClip[8];
        readonly int[] lastStep = { -1, -1 }; bool rightFoot; // per foot: left, right
        public AudioSource Stride => stride;
        // Strides played and the last clip: PlayOneShot leaves no clip on the source to read back.
        public int StrideCount { get; private set; }
        public string LastStride { get; private set; }
        void Awake()
        {
            source = Dry(); stride = Dry();
            clip = Resources.Load<AudioClip>("Audio/SFX/PasosNotas/04LB A#");
            for (int i = 0; i < steps.Length; i++) steps[i] = Resources.Load<AudioClip>("Audio/LightingSteps/soft_step_" + (i + 1).ToString("00"));
        }
        AudioSource Dry()
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false; a.spatialBlend = 0; a.dopplerLevel = 0; a.reverbZoneMix = 0;
            a.bypassReverbZones = true; a.bypassEffects = true; a.bypassListenerEffects = true;
            return a;
        }
        static float Sliders => ForestSoundscape.Volume("Master") * ForestSoundscape.Volume("Effects") * ForestSoundscape.Volume("Footsteps");
        public void PlayStep()
        {
            if (clip == null || GameHUD.SimulationPaused) return;
            // ForestSoundscape may route sources during Awake. Keep these impacts dry
            // while honoring the same saved volume controls without mixer effects.
            source.outputAudioMixerGroup = null;
            // ponytail: .3 tuned by ear, a little under the old .5 now that strides sound too; the sliders scale it from here.
            source.volume = NoteVolume * Sliders;
            source.clip = clip;
            source.Play();
        }
        // One stride: the other foot from last time, a variant other than that foot's last, a touch of pitch and level drift.
        // PlayOneShot, so a step never cuts off the floor note.
        public void PlayStride()
        {
            if (GameHUD.SimulationPaused) return;
            rightFoot = !rightFoot;
            int foot = rightFoot ? 1 : 0, pick = Random.Range(0, steps.Length / 2) * 2 + foot; // index 0 = soft_step_01, a left foot
            if (pick == lastStep[foot]) pick = (pick + 2) % steps.Length;
            lastStep[foot] = pick; var step = steps[pick]; if (step == null) return;
            stride.outputAudioMixerGroup = null;
            stride.pitch = Random.Range(.95f, 1.05f);
            // ponytail: .22 tuned by ear under the .3 note.
            stride.PlayOneShot(step, StrideVolume * Random.Range(.85f, 1f) * Sliders);
            StrideCount++; LastStride = step.name;
        }
        void Update() { if (GameHUD.SimulationPaused) { if (source.isPlaying) source.Stop(); if (stride.isPlaying) stride.Stop(); } }
        void OnApplicationFocus(bool focused) { if (!focused) { source.Stop(); stride.Stop(); } }
        void OnDisable() { if (source != null) source.Stop(); if (stride != null) stride.Stop(); }
    }
}
