using UnityEngine;
namespace LightingBlocks
{
    // Only nearby, active collectible lights appear in the dark floor. No particle objects are created.
    public sealed class LightingGlassLights : MonoBehaviour
    {
        const int MaxLights=24;
        readonly Vector4[] positions=new Vector4[MaxLights],colors=new Vector4[MaxLights];
        readonly float[] distances=new float[MaxLights];
        void LateUpdate()
        {
            var game=GameManager.Instance;
            var forest=game!=null?ForestWorld.For(game):null;
            int count=0;
            if(game!=null&&BridgeProfile.Current.isLighting&&forest!=null)
            {
                // Every particle is the same warm yellow now (see LightingParticles.ParticleColor);
                // there is no per-particle colour left to read.
                var tint=LightingParticles.ParticleColor.linear*4;
                foreach(var visual in forest.PickupVisuals)
                {
                    if(visual==null||!visual.gameObject.activeInHierarchy)continue;
                    var p=visual.position;
                    float distance=(p-game.player.transform.position).sqrMagnitude;
                    int at=count;
                    while(at>0&&distance<distances[at-1])at--;
                    if(at>=MaxLights)continue;
                    for(int j=Mathf.Min(count,MaxLights-1);j>at;j--){distances[j]=distances[j-1];positions[j]=positions[j-1];colors[j]=colors[j-1];}
                    distances[at]=distance;positions[at]=new Vector4(p.x,p.y,p.z,7);
                    colors[at]=tint;
                    count=Mathf.Min(count+1,MaxLights);
                }
            }
            Shader.SetGlobalInt("_VoidParticleCount",count);
            Shader.SetGlobalVectorArray("_VoidParticlePositions",positions);
            Shader.SetGlobalVectorArray("_VoidParticleColors",colors);
        }
        void OnDisable(){Shader.SetGlobalInt("_VoidParticleCount",0);}
    }
}
