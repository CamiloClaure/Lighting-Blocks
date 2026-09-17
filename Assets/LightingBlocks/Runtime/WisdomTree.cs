using UnityEngine;
namespace LightingBlocks
{
    public sealed class WisdomTree : MonoBehaviour
    {
        public Renderer[] glowRenderers;
        public Light glowLight;
        public ParticleSystem motes,release;
        public LineRenderer[] chargeLines;
        public IslandForestDefinition Definition {get;private set;}
        public bool Charging=>remaining>0;
        public float Remaining=>remaining;
        ForestWorld forest;GameManager game;float remaining;
        MaterialPropertyBlock properties;
        void Awake(){properties=new MaterialPropertyBlock();}
        public void Initialize(ForestWorld world,IslandForestDefinition definition){forest=world;game=world.GetComponent<GameManager>();Definition=definition;}
        public bool BeginCharge()
        {
            if(GameHUD.GameplayBlocked||Charging)return false;
            if(forest.Nearby(game.player.transform.position)!=this)return false;
            if(forest.Inventory(Definition.id).inventory<3){game.Notify("Collect 3 particles on this island",false);return false;}
            if(!forest.FindOutput(Definition,out _)){game.Notify("Move blocks away from the Wisdom Tree",false);return false;}
            remaining=.6f;ForestSoundscape.Play("charge",transform.position,.1f);game.Notify("Charging the Wisdom Tree...",false);return true;
        }
        public void Tick(float delta)
        {
            if(GameHUD.SimulationPaused||remaining<=0||delta<=0)return;
            remaining=Mathf.Max(0,remaining-delta);
            if(remaining==0)
            {
                var inventory=forest.Inventory(Definition.id);
                if(inventory.inventory>=3&&game.TrySpawnAtWisdomTree(Definition.id,out var block))
                {
                    inventory.inventory-=3;ForestSoundscape.Play("release",block.transform.position,.12f);
                    if(release!=null){release.transform.position=block.transform.position+Vector3.up*.4f;release.Play();}
                    game.Notify("A BLOCK ANSWERS\nIt waits beside the Wisdom Tree.");game.Save();
                }
                else game.Notify("Move blocks away from the Wisdom Tree",false);
            }
        }
        void Update()
        {
            if(forest==null)return;
            if(game.autoTick)Tick(Time.deltaTime);
            float strength=Charging?1+Mathf.Sin((.6f-remaining)/.6f*Mathf.PI)*2:1;
            if(glowLight!=null)glowLight.intensity=.7f*strength;
            if(glowRenderers!=null)foreach(var r in glowRenderers){properties.SetFloat("_Charge",strength);r.SetPropertyBlock(properties);}
            if(chargeLines!=null)for(int i=0;i<chargeLines.Length;i++)
            {
                var line=chargeLines[i];line.enabled=Charging;
                if(Charging){float t=1-remaining/.6f;var from=game.player.transform.position+Vector3.up*(.6f+i*.15f);var to=transform.position+Vector3.up*(1.3f+i*.3f);line.SetPosition(0,Vector3.Lerp(from,to,Mathf.Clamp01(t-.22f)));line.SetPosition(1,Vector3.Lerp(from,to,t));}
            }
        }
    }
}
