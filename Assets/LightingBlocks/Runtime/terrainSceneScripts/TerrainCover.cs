using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightingBlocks.TerrainScene
{
    // Copy of ForestCoverRenderer with the two lines relief needs changed. TerrainGround disables
    // the shipping component and adds this one instead, so ForestCoverRenderer.cs itself is never
    // touched and SampleScene keeps the component ForestWorld.Build:94 gave it.
    //
    // Two differences from the original, both marked below:
    //   worldBounds  a 4 m box centred at y=1 asserts all cover lives between y=-1 and y=3, so on
    //                a raised island Unity culls the whole batch and the grass simply vanishes.
    //   LOD eye      distance is measured to a batch centre at y=1, so climbing a slope counts as
    //                walking away and pops the grass down a LOD.
    //
    // The [ExecuteAlways] edit-mode preview and the serialized fields are dropped: this only ever
    // exists at runtime, added by TerrainGround.
    public sealed class TerrainCover : MonoBehaviour
    {
        sealed class Batch{public Matrix4x4[] transforms;public Mesh mesh;public RenderParams parameters;public Vector3 centre;public int variant;}
        readonly List<Batch> batches=new List<Batch>();Transform viewer;float time;
        public int InstanceCount{get;private set;}
        public void Initialize(ForestConfiguration config,IslandForestLayout layout,Transform player)
        {
            viewer=player;batches.Clear();InstanceCount=layout.cover.Count;
            var groups=new Dictionary<Vector3Int,List<Matrix4x4>>();
            foreach(var p in layout.cover){int variant=p.variant%config.coverMeshes.Length;var key=new Vector3Int(Mathf.FloorToInt(p.position.x/12),Mathf.FloorToInt(p.position.z/12),variant);if(!groups.TryGetValue(key,out var list)){list=new List<Matrix4x4>();groups.Add(key,list);}float tx=((p.id*7919)%9)-4f,tz=((p.id*104729)%9)-4f;list.Add(Matrix4x4.TRS(p.position,Quaternion.Euler(tx,p.yaw,tz),Vector3.one*p.scale));}
            foreach(var entry in groups){var key=entry.Key;var centre=new Vector3(key.x*12+6,1,key.y*12+6);var material=config.coverMaterials[key.z%config.coverMaterials.Length];
                // CHANGED: height 4 -> 20, so a batch sitting on relief is not culled outright.
                batches.Add(new Batch{transforms=entry.Value.ToArray(),mesh=config.coverMeshes[key.z],centre=centre,variant=key.z,parameters=new RenderParams(material){worldBounds=new Bounds(centre,new Vector3(16,20,16)),shadowCastingMode=ShadowCastingMode.Off,receiveShadows=true,lightProbeUsage=LightProbeUsage.Off}});}
        }
        void Update()
        {
            if(viewer==null)return;if(!GameHUD.SimulationPaused)time+=Time.deltaTime;
            // The real position, not the flattened one: this is what makes grass bend around the player.
            Shader.SetGlobalVector("_ForestPlayer",viewer.position);Shader.SetGlobalFloat("_ForestTime",time);
            // CHANGED: batch centres are layout data at y=1, so measure the LOD from where the
            // player would stand on a flat world or climbing a slope pops the grass.
            var eye=viewer.position;eye.y-=TerrainGround.Height(eye);
            foreach(var b in batches){float distance=Vector3.Distance(eye,b.centre);if(distance>66)continue;int count=b.transforms.Length;if(distance>42)count=Mathf.CeilToInt(count*.5f);if(distance>54)count=Mathf.CeilToInt(count*.5f);if(count>0)Graphics.RenderMeshInstanced(b.parameters,b.mesh,0,b.transforms,count);}
        }
    }
}
