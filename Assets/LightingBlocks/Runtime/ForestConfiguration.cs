using System;
using System.Collections.Generic;
using UnityEngine;
namespace LightingBlocks
{
    [Serializable] public sealed class IslandForestDefinition
    {
        public string id;
        public int polygonIndex, blockTier, treeCount=120, animalCount=6, particleCount=24, rabbitCount=4, birdCount=6;
        public Vector3 wisdomPosition;
        public Vector3[] outputOffsets=Outputs(), pathPoints;
        public static Vector3[] Outputs(){var p=new List<Vector3>();foreach(float x in new[]{3.5f,5.5f,7.5f})foreach(float z in new[]{-2f,0f,2f})p.Add(new Vector3(x,.26f,z));return p.ToArray();}
    }
    [Serializable] public sealed class ForestSpecies
    {
        public string id;public GameObject prefab;
        public float walkSpeed=.8f,fleeSpeed=3.5f,alertRadius=5,fleeRadius=3.5f,clearance=.35f;
        public bool flying;public string idle="Idle",forage="Sniff",move="Hop",flee="Flee";
    }
    [CreateAssetMenu(menuName="Lighting Blocks/Forest configuration")]
    public sealed class ForestConfiguration:ScriptableObject
    {
        public const int LayoutVersion=2;
        public int artVersion;public IslandForestDefinition[] islands;
        public GameObject wisdomPrefab;public UnityEngine.Object wisdomSource;
        public GameObject[] trees,undergrowth,animals;public ForestSpecies[] species;
        public Mesh[] coverMeshes;public Material[] coverMaterials;
        public Material ground,cliff,particleMaterial;public Mesh particleMesh;
        public int ambientBudget=600;public Vector3 wind=new Vector3(.16f,0,.09f);
        public float moonIntensity=.42f;public Color moonColor=new Color(.62f,.74f,.92f),ambientSky=new Color(.22f,.27f,.35f),ambientEquator=new Color(.12f,.19f,.20f),ambientGround=new Color(.075f,.105f,.125f);
        public Color haze=new Color(.055f,.073f,.13f);public float fogStart=48,fogEnd=150;
        public IslandForestDefinition Find(string id){if(islands!=null)foreach(var d in islands)if(d.id==id)return d;return null;}
    }
    [Serializable] public sealed class ForestSave
    {
        public int seed,layoutVersion=ForestConfiguration.LayoutVersion;
        public List<IslandParticleSave> islands=new List<IslandParticleSave>();
        public List<string> suppressedTrees=new List<string>();
    }
    [Serializable] public sealed class IslandParticleSave{public string id;public int inventory;public List<ParticleSlotSave> slots=new List<ParticleSlotSave>();}
    [Serializable] public sealed class ParticleSlotSave{public string id;public float remaining;public Vector3 position;public int generation;public bool positioned,pending;}
    public struct ForestPlacement{public Vector3 position;public float yaw,scale;public int variant,id;}
    public sealed class IslandForestLayout
    {
        public IslandForestDefinition definition;
        public readonly List<ForestPlacement> trees=new List<ForestPlacement>(),cover=new List<ForestPlacement>();
        public readonly List<Vector3> particles=new List<Vector3>(),animals=new List<Vector3>(),candidates=new List<Vector3>(),habitats=new List<Vector3>();
        public ForestNavigation navigation;
    }
    public static class ForestHabitat
    {
        public static float SegmentDistance(Vector3 p,Vector3 a,Vector3 b){p.y=a.y=b.y=0;var e=b-a;return Vector3.Distance(p,a+e*Mathf.Clamp01(Vector3.Dot(p-a,e)/Mathf.Max(.001f,e.sqrMagnitude)));}
        public static float PathDistance(Vector3 p,IslandForestDefinition d)
        {
            float centre=d.wisdomPosition.x+12,result=Mathf.Abs(p.x-centre);
            result=Mathf.Min(result,SegmentDistance(p,d.wisdomPosition+Vector3.right*2,new Vector3(centre,0,d.wisdomPosition.z)));
            if(d.pathPoints!=null)for(int i=1;i<d.pathPoints.Length;i+=2)result=Mathf.Min(result,SegmentDistance(p,d.pathPoints[i-1],d.pathPoints[i]));
            return result;
        }
        public static float Green(Vector3 p,IslandForestDefinition d){float path=Mathf.SmoothStep(0,1,Mathf.Clamp01((PathDistance(p,d)-1.1f)/2.8f));return Mathf.Clamp01(.2f+path*(.5f+Mathf.PerlinNoise(p.x*.065f+31,p.z*.065f+17)*.3f));}
        public static bool MainPath(Vector3 p,IslandForestDefinition d)=>PathDistance(p,d)<3;
        public static bool Landing(Vector3 p,IslandForestDefinition d){if(Mathf.Abs(p.x-(d.wisdomPosition.x+12))>6)return false;return d.polygonIndex==0?p.z>3&&p.z<9:d.polygonIndex==1?(p.z<25||p.z>30):p.z<52;}
    }
    public static class ForestLayoutGenerator
    {
        public static int Hash(int seed,string value){unchecked{uint h=(uint)seed^2166136261;foreach(char c in value){h^=c;h*=16777619;}return(int)(h&0x7fffffff);}}
        public static float Edge(Vector2 p,Vector2[] poly){float best=float.MaxValue;for(int i=0,j=poly.Length-1;i<poly.Length;j=i++){var e=poly[i]-poly[j];float t=Mathf.Clamp01(Vector2.Dot(p-poly[j],e)/e.sqrMagnitude);best=Mathf.Min(best,Vector2.Distance(p,poly[j]+e*t));}return best;}
        public static bool Inside(Vector3 p,Vector2[] poly,float inset=.65f)=>Landscape.Inside(new Vector2(p.x,p.z),poly)&&Edge(new Vector2(p.x,p.z),poly)>=inset;
        public static bool Reserved(Vector3 p,IslandForestDefinition d,float radius)
        {
            p.y=0;if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(d.wisdomPosition.x,d.wisdomPosition.z))<radius)return true;
            foreach(var offset in d.outputOffsets)if((p-(d.wisdomPosition+offset)).sqrMagnitude<6.25f)return true;
            if(d.polygonIndex==0&&(p-new Vector3(-6,0,-9)).sqrMagnitude<9)return true;
            if(d.polygonIndex==2&&(p-new Vector3(0,0,55)).sqrMagnitude<9)return true;return false;
        }
        public static IslandForestLayout Generate(IslandForestDefinition d,int seed,Vector2[] polygon,int variants)
        {
            var layout=new IslandForestLayout{definition=d};var min=polygon[0];var max=min;foreach(var p in polygon){min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            Vector3 Sample(System.Random r)=>new Vector3(Mathf.Lerp(min.x,max.x,(float)r.NextDouble()),0,Mathf.Lerp(min.y,max.y,(float)r.NextDouble()));
            var rng=new System.Random(Hash(seed,d.id+"/trees-v2"));var cells=new List<Vector3>();
            for(float x=min.x+1;x<max.x;x+=2)for(float z=min.y+1;z<max.y;z+=2){var p=new Vector3(x,0,z);if(Inside(p,polygon,3)&&!Reserved(p,d,5)&&!ForestHabitat.MainPath(p,d)&&!ForestHabitat.Landing(p,d))cells.Add(p);}
            int count=Mathf.Min(d.treeCount,Mathf.RoundToInt(cells.Count*4/45f));
            int eligibleInterior=0;foreach(var cell in cells)if(Mathf.Abs(cell.x)<17)eligibleInterior++;
            int interiorQuota=eligibleInterior==0?0:Mathf.Max(1,Mathf.RoundToInt(count*eligibleInterior/(float)Mathf.Max(1,cells.Count))),interiorCount=0;
            for(int i=cells.Count-1;i>0;i--){int j=rng.Next(i+1);var temp=cells[i];cells[i]=cells[j];cells[j]=temp;}
            for(int phase=0;phase<3;phase++)for(int a=0;a<cells.Count&&layout.trees.Count<count;a++)
            {
                var p=cells[a]+new Vector3((float)rng.NextDouble()-.5f,0,(float)rng.NextDouble()-.5f);
                bool interior=Mathf.Abs(p.x)<17;if(phase==0&&(!interior||interiorCount>=interiorQuota))continue;if(phase==1&&interior)continue;
                if(!Inside(p,polygon,3)||Reserved(p,d,5)||ForestHabitat.MainPath(p,d)||ForestHabitat.Landing(p,d))continue;
                bool clear=true;foreach(var t in layout.trees)if((t.position-p).sqrMagnitude<12.25f){clear=false;break;}
                if(clear){layout.trees.Add(new ForestPlacement{id=layout.trees.Count,position=p,yaw=(float)rng.NextDouble()*360,scale=.8f+(float)rng.NextDouble()*.4f,variant=rng.Next(Math.Max(1,variants))});if(interior)interiorCount++;}
            }
            var nav=new ForestNavigation(polygon,layout,false);layout.navigation=nav;nav.BuildReachability(d.wisdomPosition+Vector3.right*2);
            rng=new System.Random(Hash(seed,d.id+"/particles-v2"));
            if(d.polygonIndex==0)foreach(var p in new[]{new Vector3(-2,0,1),new Vector3(-4,0,0),new Vector3(-6,0,-1)})if(nav.Walkable(p))layout.particles.Add(p);
            for(int a=0;a<12000&&layout.candidates.Count<192;a++)
            {
                var p=Sample(rng);if(!nav.Walkable(p)||Reserved(p,d,6)||!nav.InReachableArea(p))continue;
                bool clear=true;foreach(var prior in layout.candidates)if((prior-p).sqrMagnitude<2.25f){clear=false;break;}
                if(!clear)continue;layout.candidates.Add(p);clear=true;foreach(var prior in layout.particles)if((prior-p).sqrMagnitude<6.25f){clear=false;break;}
                if(clear&&layout.particles.Count<d.particleCount)layout.particles.Add(p);
            }
            rng=new System.Random(Hash(seed,d.id+"/animals-v2"));int total=d.animalCount+d.rabbitCount+d.birdCount;
            for(int a=0;a<8000&&layout.animals.Count<total;a++)
            {
                var p=Sample(rng);if(!nav.Walkable(p)||Reserved(p,d,6)||ForestHabitat.PathDistance(p,d)<3.5f)continue;
                bool clear=true;foreach(var prior in layout.animals)if((prior-p).sqrMagnitude<4){clear=false;break;}
                if(layout.animals.Count>=d.animalCount+d.rabbitCount)foreach(var tree in layout.trees)if((tree.position-p).sqrMagnitude<12*tree.scale*tree.scale){clear=false;break;}
                if(clear){layout.animals.Add(p);layout.habitats.Add(p);}
            }
            rng=new System.Random(Hash(seed,d.id+"/cover-v2"));
            for(float x=min.x;x<max.x;x+=1.05f)for(float z=min.y;z<max.y;z+=1.05f)
            {
                var p=new Vector3(x+(float)rng.NextDouble(),0,z+(float)rng.NextDouble());if(!Inside(p,polygon,.5f)||Reserved(p,d,2.3f))continue;
                if(rng.NextDouble()>ForestHabitat.Green(p,d)*.93f)continue;
                int v=rng.Next(100);v=v<42?0:v<68?1:v<80?2:v<85?3:v<90?4:v<93?5:v<96?6:v<98?7:8;
                if(ForestHabitat.PathDistance(p,d)<3.2f)v=0;foreach(var habitat in layout.habitats)if((p-habitat).sqrMagnitude<2.25f){v=0;break;}
                layout.cover.Add(new ForestPlacement{id=layout.cover.Count,position=p,yaw=rng.Next(360),scale=.8f+(float)rng.NextDouble()*.4f,variant=v});
            }
            if(layout.particles.Count!=d.particleCount)throw new InvalidOperationException("Cannot safely place all particles on "+d.id);
            if(layout.animals.Count!=total)throw new InvalidOperationException("Cannot safely place wildlife on "+d.id);return layout;
        }
    }
}
