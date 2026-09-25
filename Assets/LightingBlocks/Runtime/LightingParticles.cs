using System.Collections.Generic;
using UnityEngine;
namespace LightingBlocks
{
    // Owns the shared Space-grab arbitration; particles are not bridge blocks or soul generators.
    public sealed class LightingParticles : MonoBehaviour
    {
        public readonly List<ParticleDrag> Particles=new List<ParticleDrag>();
        public readonly List<ColorReceiver> Receivers=new List<ColorReceiver>();
        public ParticleDrag Held {get;private set;}
        BoxPlayer holder;
        public static readonly Color[] Colors={new Color(.14f,.43f,1),new Color(.69f,.24f,1),new Color(1,.68f,.22f)};
        public void ResetWorld(){Cancel();Particles.Clear();Receivers.Clear();}
        public void AddParticle(ForestWorld forest,GameObject visual,IslandParticleSave owner,ParticleSlotSave slot)
        {
            var particle=visual.AddComponent<ParticleDrag>();particle.World=forest;particle.Owner=owner;particle.Slot=slot;
            particle.Paint();Particles.Add(particle);
        }
        public void AddTree(ForestWorld world,WisdomTree tree,Transform parent)
        {
            for(int color=0;color<3;color++)
            {
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Color receiver "+color;cube.transform.SetParent(parent,false);
                cube.transform.position=tree.Definition.wisdomPosition+new Vector3(-3.2f,.55f,(color-1)*2.1f);cube.transform.localScale=Vector3.one*.8f;
                // Trigger volume accepts particles but never blocks the player or the tree's output.
                cube.GetComponent<Collider>().isTrigger=true;
                var receiver=cube.AddComponent<ColorReceiver>();receiver.Tree=tree;receiver.World=world;receiver.ColorIndex=color;
                receiver.Initialize(world.configuration.particleMaterial);Receivers.Add(receiver);
            }
            if(tree.Definition.polygonIndex==0)
            {
                var lights=new GameObject("Canopy fireflies",typeof(ParticleSystem));lights.transform.SetParent(tree.transform,false);
                var bounds=new Bounds(tree.transform.position,Vector3.zero);foreach(var renderer in tree.GetComponentsInChildren<Renderer>())bounds.Encapsulate(renderer.bounds);
                lights.transform.position=new Vector3(tree.transform.position.x,bounds.max.y+.5f,tree.transform.position.z);
                var ps=lights.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.startLifetime=7;main.startSpeed=.04f;main.startSize=.07f;main.maxParticles=12;main.startColor=new Color(.45f,.4f,.7f,.35f);
                var emission=ps.emission;emission.rateOverTime=1.5f;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=2.5f;
                lights.GetComponent<ParticleSystemRenderer>().sharedMaterial=world.configuration.particleMaterial;ps.Play();
            }
        }
        public bool TryGrab(BoxPlayer player,PullableBox block)
        {
            if(Held!=null||player.HeldBox!=null||GameHUD.GameplayBlocked)return false;
            var origin=player.transform.position+Vector3.up*.7f;
            if(block!=null&&block.GetComponent<BridgeBlock>()?.CanLift==false)block=null;
            float best=block!=null?Vector3.Distance(origin,block.Shape.ClosestPoint(origin)):player.grabReach;
            ParticleDrag chosen=null;
            foreach(var particle in Particles)
            {
                if(particle==null||!particle.gameObject.activeInHierarchy||particle.Slot.remaining>0||particle.Slot.pending)continue;
                float distance=Vector3.Distance(origin,particle.transform.position);
                if(distance>best||!Clear(player,origin,particle.transform.position))continue;
                chosen=particle;best=distance;
            }
            if(chosen==null)return false;
            Held=chosen;holder=player;chosen.Held=true;return true;
        }
        bool Clear(BoxPlayer player,Vector3 from,Vector3 to)
        {
            var delta=to-from;
            foreach(var hit in Physics.RaycastAll(from,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(player.transform)&&hit.collider.GetComponent<PlatformSupport>()==null)return false;
            return true;
        }
        void LateUpdate()
        {
            if(Held==null)return;
            if(holder==null||GameHUD.GameplayBlocked||!Application.isFocused){Cancel();return;}
            var forward=Vector3.ProjectOnPlane(holder.viewCamera!=null?holder.viewCamera.transform.forward:holder.transform.forward,Vector3.up).normalized;
            var target=holder.transform.position+forward*1.05f+Vector3.up*.7f;
            if(Clear(holder,holder.transform.position+Vector3.up*.7f,target))Held.transform.position=target;
            foreach(var receiver in Receivers)receiver.Highlight(receiver.Matches(Held)&&receiver.Contains(Held.transform.position));
        }
        public void Drop()
        {
            if(GameHUD.GameplayBlocked){Cancel();return;}
            if(Held==null)return;
            var particle=Held;Held=null;particle.Held=false;
            bool delivered=false,overReceiver=false;
            foreach(var receiver in Receivers)
            {
                receiver.Highlight(false);
                if(!receiver.Contains(particle.transform.position))continue;
                overReceiver=true;delivered=receiver.Receive(particle);break;
            }
            if(!delivered)
            {
                var p=particle.transform.position;p.y=0;
                // Only commit positions the save validator and respawn system can also accept.
                var layout=particle.World.Layouts.Find(l=>l.definition.id==particle.Owner.id);
                if(!overReceiver&&PlatformSupport.At(p)?.hubId==particle.Owner.id&&layout!=null&&layout.navigation.InReachableArea(p)&&!particle.World.Occupied(p+Vector3.up*.7f,Vector3.one*.15f,true))particle.Slot.position=p;
                particle.transform.position=particle.Slot.position+Vector3.up*.7f;
            }
            holder=null;GetComponent<GameManager>().Save();
        }
        public void Cancel()
        {
            if(Held!=null){Held.Held=false;Held.transform.position=Held.Slot.position+Vector3.up*.7f;Held=null;}
            holder=null;foreach(var receiver in Receivers)if(receiver!=null)receiver.Highlight(false);
        }
        void OnDisable(){Cancel();}
    }
    public sealed class ParticleDrag : MonoBehaviour
    {
        public ForestWorld World;public IslandParticleSave Owner;public ParticleSlotSave Slot;public bool Held;
        public void Paint()
        {
            var block=new MaterialPropertyBlock();var color=LightingParticles.Colors[Slot.color];
            block.SetColor("_BaseColor",color);block.SetColor("_EmissionColor",color*1.6f);GetComponent<Renderer>().SetPropertyBlock(block);
        }
    }
    public sealed class ColorReceiver : MonoBehaviour
    {
        public WisdomTree Tree;public ForestWorld World;public int ColorIndex;
        MaterialPropertyBlock properties;Renderer surface;float rejection,pulse;
        GameObject spark;
        public void Initialize(Material material){surface=GetComponent<Renderer>();surface.sharedMaterial=material;properties=new MaterialPropertyBlock();Highlight(false);}
        public bool Contains(Vector3 p)=>Mathf.Abs(p.x-transform.position.x)<.8f&&Mathf.Abs(p.z-transform.position.z)<.8f&&Mathf.Abs(p.y-transform.position.y)<1;
        public bool Matches(ParticleDrag particle)=>particle!=null&&particle.Slot.color==ColorIndex&&particle.Owner.id==Tree.Definition.id;
        public void Highlight(bool on)
        {
            if(surface==null)return;var c=rejection>0?Color.red:LightingParticles.Colors[ColorIndex];
            properties.SetColor("_BaseColor",c);properties.SetColor("_EmissionColor",c*(on?2.2f:.65f));surface.SetPropertyBlock(properties);
        }
        public bool Receive(ParticleDrag particle)
        {
            if(GameHUD.GameplayBlocked||particle==null||!particle.gameObject.activeInHierarchy||particle.Slot.remaining>0||particle.Slot.pending)return false;
            if(!Matches(particle)||particle.Owner.treeCharge>=3){rejection=.25f;Highlight(false);return false;}
            particle.Owner.chargeColor=ColorIndex;particle.Owner.hasChargeColor=true;particle.Owner.treeCharge++;particle.Slot.remaining=30;particle.Slot.pending=false;particle.gameObject.SetActive(false);
            pulse=.5f;
            if(spark==null){spark=GameObject.CreatePrimitive(PrimitiveType.Sphere);Destroy(spark.GetComponent<Collider>());spark.transform.SetParent(transform.parent,true);spark.transform.localScale=Vector3.one*.18f;spark.GetComponent<Renderer>().sharedMaterial=surface.sharedMaterial;spark.GetComponent<Renderer>().SetPropertyBlock(properties);}
            spark.SetActive(true);ForestSoundscape.Play("collect",transform.position,.12f);return true;
        }
        void Update()
        {
            if(GameHUD.SimulationPaused)return;
            if(rejection>0){rejection-=Time.deltaTime;Highlight(false);}
            if(pulse>0){pulse=Mathf.Max(0,pulse-Time.deltaTime);spark.transform.position=Vector3.Lerp(transform.position,Tree.transform.position+Vector3.up*2,1-pulse/.5f);if(pulse==0)spark.SetActive(false);}
        }
    }
}
