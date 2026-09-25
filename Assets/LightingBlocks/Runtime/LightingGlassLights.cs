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
            var particles=game!=null?game.GetComponent<LightingParticles>():null;
            int count=0;
            if(game!=null&&BridgeProfile.Current.isLighting&&particles!=null)
            {
                foreach(var particle in particles.Particles)
                {
                    if(particle==null||!particle.gameObject.activeInHierarchy)continue;
                    var p=particle.transform.position;
                    float distance=(p-game.player.transform.position).sqrMagnitude;
                    int at=count;
                    while(at>0&&distance<distances[at-1])at--;
                    if(at>=MaxLights)continue;
                    for(int j=Mathf.Min(count,MaxLights-1);j>at;j--){distances[j]=distances[j-1];positions[j]=positions[j-1];colors[j]=colors[j-1];}
                    distances[at]=distance;positions[at]=new Vector4(p.x,p.y,p.z,7);
                    colors[at]=LightingParticles.Colors[particle.Slot.color].linear*4;
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
