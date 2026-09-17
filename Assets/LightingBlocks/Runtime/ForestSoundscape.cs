using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
namespace LightingBlocks
{
    public sealed class ForestSoundscape:MonoBehaviour
    {
        static ForestSoundscape current;static AudioMixer mixer;
        readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        readonly AudioClip[][] steps=new AudioClip[3][];readonly int[] lastStep={-1,-1,-1};
        readonly AudioSource[] voices=new AudioSource[12];AudioSource wind,leaves;int voice;GameManager game;Transform listener;
        public static float Volume(string group)=>PlayerPrefs.GetFloat("forest.audio."+group,group=="Music"?.65f:.8f);
        public static void SetVolume(string group,float value){value=Mathf.Clamp01(value);PlayerPrefs.SetFloat("forest.audio."+group,value);PlayerPrefs.Save();if(mixer==null)mixer=Resources.Load<AudioMixer>("Forest/ForestMix");if(mixer!=null)mixer.SetFloat(group,value<=.001f?-80:Mathf.Log10(value)*20);}
        public static void Route(AudioSource source,string group){if(source==null)return;if(mixer==null)mixer=Resources.Load<AudioMixer>("Forest/ForestMix");if(mixer!=null){var groups=mixer.FindMatchingGroups(group);if(groups.Length>0)source.outputAudioMixerGroup=groups[0];}}
        void Awake()
        {
            current=this;game=GetComponent<GameManager>();mixer=Resources.Load<AudioMixer>("Forest/ForestMix");
            foreach(var group in new[]{"Master","Music","Ambience","Effects"})SetVolume(group,Volume(group));
            foreach(var name in new[]{"wind","leaves","collect","charge","release","crystal","wing","bird"})clips[name]=Resources.Load<AudioClip>("Audio/Forest/"+name);
            for(int surface=0;surface<3;surface++){steps[surface]=new AudioClip[6];for(int i=0;i<6;i++)steps[surface][i]=Resources.Load<AudioClip>("Audio/Forest/step_"+surface+"_"+i);}
            for(int i=0;i<voices.Length;i++){var o=new GameObject("Forest sound "+i);o.transform.SetParent(transform,false);var a=o.AddComponent<AudioSource>();a.playOnAwake=false;a.spatialBlend=1;a.rolloffMode=AudioRolloffMode.Linear;a.minDistance=2;a.maxDistance=24;a.dopplerLevel=0;Route(a,"Wildlife");voices[i]=a;}
            wind=Loop("wind",.24f);leaves=Loop("leaves",.1f);
            foreach(var existing in FindObjectsByType<AudioListener>())existing.enabled=false;
            var host=new GameObject("Forest listener",typeof(AudioListener));host.transform.SetParent(transform,false);listener=host.transform;
            foreach(var source in FindObjectsByType<AudioSource>()){if(source.outputAudioMixerGroup!=null)continue;Route(source,source.name=="Music"?"Music":"UI");}
        }
        AudioSource Loop(string name,float volume){var o=new GameObject(name+" ambience",typeof(AudioSource));o.transform.SetParent(transform,false);var a=o.GetComponent<AudioSource>();a.clip=clips[name];a.loop=true;a.spatialBlend=0;a.volume=volume;Route(a,"Ambience");a.Play();return a;}
        public static void Play(string name,Vector3 position,float volume)
        {
            if(current==null||!current.clips.TryGetValue(name,out var clip)||clip==null)return;
            var a=current.voices[current.voice++%current.voices.Length];a.transform.position=position;a.volume=volume;a.pitch=1; // recording timbre stays stable
            // Routing is assigned once except when a voice changes category.
            var target=name=="bird"||name=="wing"?current.wildlifeGroup:current.magicGroup;if(target!=null)a.outputAudioMixerGroup=target;
            a.clip=clip;a.Play();
        }
        AudioMixerGroup wildlifeGroup,magicGroup;AudioMixerSnapshot forestSnapshot,wisdomSnapshot;bool nearWisdom;AudioSource music;
        void Start(){if(mixer!=null){var w=mixer.FindMatchingGroups("Wildlife");var m=mixer.FindMatchingGroups("Magic");if(w.Length>0)wildlifeGroup=w[0];if(m.Length>0)magicGroup=m[0];forestSnapshot=mixer.FindSnapshot("Forest");wisdomSnapshot=mixer.FindSnapshot("Wisdom");}foreach(var source in FindObjectsByType<AudioSource>())if(source.name=="Music")music=source;}
        public static AudioClip Footstep(Vector3 position)
        {
            if(current==null)return null;int surface=0;var f=ForestWorld.For(current.game);var d=f.IslandAt(position);
            if(d==null)surface=2;else if(ForestHabitat.Green(position,d)<.5f)surface=1;
            int n=Random.Range(0,5);if(n>=current.lastStep[surface])n++;n%=6;current.lastStep[surface]=n;return current.steps[surface][n];
        }
        void LateUpdate(){if(game==null||listener==null)return;listener.position=game.player.transform.position+Vector3.up*1.2f;listener.rotation=game.cameraRig.transform.rotation;
            var forest=ForestWorld.For(game);var island=forest.IslandAt(game.player.transform.position);bool near=island!=null&&Vector3.Distance(game.player.transform.position,island.wisdomPosition)<9;
            if(near!=nearWisdom){nearWisdom=near;(near?wisdomSnapshot:forestSnapshot)?.TransitionTo(3);}
            if(music!=null)music.volume=Mathf.MoveTowards(music.volume,GameAudio.MusicVolume*(near?.55f:.8f),Time.unscaledDeltaTime*.012f);
            float amount=island==null?.06f:.10f;if(leaves!=null)leaves.volume=Mathf.MoveTowards(leaves.volume,amount,Time.unscaledDeltaTime*.02f);}
        void OnDestroy(){if(current==this)current=null;}
    }
}
