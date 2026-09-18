using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace LightingBlocks
{
    [ExecuteAlways] public sealed class ForestCoverRenderer:MonoBehaviour
    {
        sealed class Batch{public Matrix4x4[] transforms;public Mesh mesh;public RenderParams parameters;public Vector3 centre;public int variant;}
        readonly List<Batch> batches=new List<Batch>();Transform viewer;float time;
        [SerializeField] ForestConfiguration settings;[SerializeField] string islandId;[SerializeField] int seed;
        void OnEnable(){if(Application.isPlaying||settings==null)return;var g=FindAnyObjectByType<GameManager>();var d=settings.Find(islandId);if(g!=null&&d!=null)Initialize(settings,ForestLayoutGenerator.Generate(d,seed,Landscape.Shores[d.polygonIndex],settings.trees.Length),g.player.transform);}
        public int InstanceCount{get;private set;}
        public void Initialize(ForestConfiguration config,IslandForestLayout layout,Transform player)
        {
            viewer=player;settings=config;islandId=layout.definition.id;var world=FindAnyObjectByType<ForestWorld>();seed=world!=null&&world.State!=null?world.State.seed:1907;batches.Clear();InstanceCount=layout.cover.Count;
            var groups=new Dictionary<Vector3Int,List<Matrix4x4>>();
            foreach(var p in layout.cover){int variant=p.variant%config.coverMeshes.Length;var key=new Vector3Int(Mathf.FloorToInt(p.position.x/12),Mathf.FloorToInt(p.position.z/12),variant);if(!groups.TryGetValue(key,out var list)){list=new List<Matrix4x4>();groups.Add(key,list);}float tx=((p.id*7919)%9)-4f,tz=((p.id*104729)%9)-4f;list.Add(Matrix4x4.TRS(p.position+Vector3.up*Landscape.Height(p.position),Quaternion.Euler(tx,p.yaw,tz),Vector3.one*p.scale));}
            foreach(var entry in groups){var key=entry.Key;var centre=new Vector3(key.x*12+6,1,key.y*12+6);var material=config.coverMaterials[key.z%config.coverMaterials.Length];
                batches.Add(new Batch{transforms=entry.Value.ToArray(),mesh=config.coverMeshes[key.z],centre=centre,variant=key.z,// Tall enough to hold a terrace: the old 4 m box asserted every blade sat in y -1..3, which
                // culls visible grass the moment the ground rises under it. Over-sizing only culls less.
                parameters=new RenderParams(material){worldBounds=new Bounds(centre,new Vector3(16,12,16)),shadowCastingMode=ShadowCastingMode.Off,receiveShadows=true,lightProbeUsage=LightProbeUsage.Off}});}
        }
        void Update()
        {
            if(viewer==null)return;if(!GameHUD.SimulationPaused)time+=Time.deltaTime;
            Shader.SetGlobalVector("_ForestPlayer",viewer.position);Shader.SetGlobalFloat("_ForestTime",time);
            // LOD measured on the ground plane, so walking up a terrace does not pop a tile of grass.
            foreach(var b in batches){float distance=Vector2.Distance(new Vector2(viewer.position.x,viewer.position.z),new Vector2(b.centre.x,b.centre.z));if(distance>66)continue;int count=b.transforms.Length;if(distance>42)count=Mathf.CeilToInt(count*.5f);if(distance>54)count=Mathf.CeilToInt(count*.5f);if(count>0)Graphics.RenderMeshInstanced(b.parameters,b.mesh,0,b.transforms,count);}
        }
    }
}
