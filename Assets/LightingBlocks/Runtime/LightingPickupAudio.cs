using UnityEngine;
namespace LightingBlocks
{
    // Lighting-only feedback. Inventory commits call this once; deposits use their own sound.
    [DisallowMultipleComponent]
    public sealed class LightingPickupAudio : MonoBehaviour
    {
        public AudioClip[] clips = new AudioClip[4];
        [Range(0, .5f)] public float volume = .22f;
        [Range(0, .03f)] public float pitchVariation = .012f;
        [Min(.1f)] public float sequenceResetTime = 1.2f;
        readonly AudioSource[] voices = new AudioSource[4];
        static readonly int[] semitones = {0, 2, 4, 7};
        int lastClip = -1, voice, sequence;
        float lastPickup = float.NegativeInfinity;
        public int PlaybackCount { get; private set; }
        public int SequenceStep => sequence;
        void Awake()
        {
            if(!BridgeProfile.Current.isLighting){enabled=false;return;}
            if(clips==null||clips.Length==0)clips=new AudioClip[4];
            for(int i=0;i<clips.Length;i++)if(clips[i]==null)clips[i]=Resources.Load<AudioClip>("Audio/LightingPickups/pickup_"+(i%4+1).ToString("00"));
            for(int i=0;i<voices.Length;i++)
            {
                var host=new GameObject("Particle pickup voice "+i);host.transform.SetParent(transform,false);
                var source=host.AddComponent<AudioSource>();voices[i]=source;
                source.playOnAwake=false;source.spatialBlend=0;source.dopplerLevel=0;
                source.reverbZoneMix=0;source.bypassReverbZones=true;
                ForestSoundscape.Route(source,"Effects");
            }
        }
        public void PlayPickup()
        {
            if(!isActiveAndEnabled||!BridgeProfile.Current.isLighting||GameHUD.GameplayBlocked)return;
            int count=0;for(int i=0;i<clips.Length;i++)if(clips[i]!=null&&i!=lastClip)count++;
            int selected=-1;
            if(count>0){int choice=Random.Range(0,count);for(int i=0;i<clips.Length;i++)if(clips[i]!=null&&i!=lastClip&&choice--==0){selected=i;break;}}
            else if(lastClip>=0&&lastClip<clips.Length&&clips[lastClip]!=null)selected=lastClip;
            if(selected<0)return;
            float now=Time.unscaledTime;
            sequence=now-lastPickup<=sequenceResetTime?Mathf.Min(sequence+1,3):0;
            lastPickup=now;lastClip=selected;
            var source=voices[voice++%voices.Length];source.Stop();
            source.clip=clips[selected];source.volume=Mathf.Clamp(volume,0,.5f);
            // .36 peak assets x .5 maximum gain x four voices = .72, leaving burst headroom.
            source.pitch=Mathf.Pow(2,semitones[sequence]/12f)*(1+Random.Range(-pitchVariation,pitchVariation));
            source.Play();PlaybackCount++;
        }
        public void ResetSequence(){lastPickup=float.NegativeInfinity;sequence=0;}
        void Update(){if(GameHUD.SimulationPaused)ResetSequence();}
        void OnApplicationFocus(bool focused){if(!focused)ResetSequence();}
        void OnDisable(){ResetSequence();foreach(var source in voices)if(source!=null)source.Stop();}
    }
}
