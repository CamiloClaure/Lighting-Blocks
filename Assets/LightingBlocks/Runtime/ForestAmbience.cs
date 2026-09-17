using UnityEngine;
using UnityEngine.Rendering;
namespace LightingBlocks
{
    public sealed class ForestAmbience:MonoBehaviour
    {
        ParticleSystem[] systems;GameManager game;ForestWorld forest;ForestConfiguration config;
        ParticleSystem.Particle[] buffer;float habitatTimer;
        public void Initialize(ForestConfiguration c,ForestWorld f,GameManager g)
        {
            config=c;forest=f;game=g;systems=new ParticleSystem[4];buffer=new ParticleSystem.Particle[Mathf.Max(16,c.ambientBudget)];
            for(int i=0;i<4;i++){
                var o=new GameObject(i==0?"Golden fireflies":i==1?"Lavender spores":i==2?"Drifting leaves":"Ground mist");o.transform.SetParent(transform,false);
                var ps=o.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);systems[i]=ps;
                var main=ps.main;main.maxParticles=i==3?16:Mathf.Max(1,Mathf.FloorToInt(c.ambientBudget*(i==0?.2f:i==1?.7f:.1f)));main.startLifetime=i==3?18:i==1?15:10;main.startSpeed=0;main.startSize=i==3?new ParticleSystem.MinMaxCurve(4,8):i==2?new ParticleSystem.MinMaxCurve(.06f,.13f):new ParticleSystem.MinMaxCurve(.025f,.065f);main.simulationSpace=ParticleSystemSimulationSpace.World;
                main.startColor=i==0?new Color(1,.75f,.28f,.8f):i==1?new Color(.5f,.72f,1,.35f):i==2?new Color(.5f,.65f,.8f,.35f):new Color(.35f,.43f,.65f,.035f);
                var emission=ps.emission;emission.rateOverTime=i==3?.7f:main.maxParticles/(i==1?15f:10f);
                var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=i==3?new Vector3(22,.1f,22):new Vector3(28,3,28);
                var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.x=c.wind.x;velocity.z=c.wind.z;velocity.y=i==2?-.055f:.012f;
                var noise=ps.noise;noise.enabled=true;noise.strength=i==3?.05f:.12f;noise.frequency=.16f;noise.scrollSpeed=.04f;noise.quality=ParticleSystemNoiseQuality.Low;
                var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(.8f,.7f),new GradientAlphaKey(0,1)});color.color=gradient;
                var renderer=o.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=c.particleMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                var mat=Resources.Load<Material>("Forest/Mote");if(mat!=null)renderer.sharedMaterial=mat;
                ps.useAutoRandomSeed=false;ps.randomSeed=(uint)ForestLayoutGenerator.Hash(f.State.seed,"ambient/"+i);if(Application.isPlaying)ps.Play();
            }
        }
        void Update()
        {
            if(game==null||systems==null)return;bool paused=GameHUD.SimulationPaused;
            var island=forest.IslandAt(game.player.transform.position);
            habitatTimer-=Time.deltaTime;bool inspect=habitatTimer<=0&&!paused;if(inspect)habitatTimer=.25f;
            for(int i=0;i<systems.Length;i++){var ps=systems[i];if(paused){if(ps.isPlaying)ps.Pause();continue;}if(ps.isPaused)ps.Play();var emission=ps.emission;emission.enabled=island!=null;
                ps.transform.position=game.player.transform.position+Vector3.up*(i==3?.15f:i==2?2:1.5f);
                if(inspect){int count=ps.GetParticles(buffer);for(int n=0;n<count;n++){var p=buffer[n].position;var habitat=forest.IslandAt(p);float distance=Vector3.Distance(p,game.player.transform.position);if(habitat==null||distance>60||(ForestHabitat.Green(p,habitat)<.4f&&buffer[n].randomSeed%3!=0))buffer[n].remainingLifetime=0;else {var color=buffer[n].startColor;color.a=(byte)((i==3?9:i==0?204:89)*Mathf.Clamp01((60-distance)/35));buffer[n].startColor=color;}}ps.SetParticles(buffer,count);}}
            Shader.SetGlobalVector("_ForestWind",config.wind);
        }
    }
}
