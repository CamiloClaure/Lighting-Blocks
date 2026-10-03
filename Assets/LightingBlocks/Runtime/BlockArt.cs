using UnityEngine;

namespace LightingBlocks
{
    [DisallowMultipleComponent]
    public sealed class BlockArt : MonoBehaviour
    {
        public Transform looseVisual, placedVisual;
        public Renderer[] bodies, seams;
        public Renderer[] lightDetails;
        // ponytail: tuned by eye. Lighting's stepped-on tiles shine twice as bright; legacy keeps 1.
        public const float LightingLitGain = 2f;
        BridgeBlock block;
        MaterialPropertyBlock properties;
        GroundShadow shadow;
        bool nearby;
        void Awake() { block=GetComponent<BridgeBlock>(); properties=new MaterialPropertyBlock(); Refresh(); }
        public void Highlight(bool value) { nearby=value; Refresh(); }
        public void Refresh()
        {
            if(block==null) block=GetComponent<BridgeBlock>();
            if(block==null || looseVisual==null || placedVisual==null) return;
            if(properties==null)properties=new MaterialPropertyBlock();
            // Art prefabs use physical metres. Cancel the logical root scale, then apply loose shrink.
            var s=transform.localScale;
            var inverse=new Vector3(1/s.x,1/s.y,1/s.z);
            looseVisual.localScale=Vector3.Scale(inverse,new Vector3(.6f,.7142857f,.6f));
            placedVisual.localScale=inverse;
            looseVisual.gameObject.SetActive(!block.placed); placedVisual.gameObject.SetActive(block.placed);
            // Lighting: a soft disc under the loose visual, so it hides with it once placed. Play Mode only:
            // CrystalTiles.Build refreshes prefab contents. ponytail: size and strength tuned by eye.
            if(shadow==null && !block.placed && Application.isPlaying && BridgeProfile.Current.isLighting) shadow=GroundShadow.Attach(looseVisual,1.5f,.5f,false);
            foreach(var seam in seams) if(seam!=null)seam.enabled=block.Connected;
            bool held=block.Box!=null && block.Box.Holder!=null;
            foreach(var body in bodies)
            {
                if(body==null)continue;
                body.GetPropertyBlock(properties);
                properties.SetFloat("_Highlight",held?1:nearby?.5f:0);
                properties.SetFloat("_Connected",block.Connected?1:0);
                body.SetPropertyBlock(properties);
            }
            SetGlow(block.placed && BridgeProfile.Current.occupancyLight ? block.lit : 1f);
        }
        public void SetGlow(float value)
        {
            if(properties==null) properties=new MaterialPropertyBlock();
            ApplyGlow(bodies,value); ApplyGlow(lightDetails,value);
        }
        void ApplyGlow(Renderer[] renderers,float value)
        {
            if(renderers==null) return;
            foreach(var renderer in renderers)
            {
                if(renderer==null) continue;
                renderer.GetPropertyBlock(properties);
                properties.SetFloat("_Glow",Mathf.Clamp01(value));
                properties.SetFloat("_LitGain",BridgeProfile.Current.isLighting?LightingLitGain:1f);
                renderer.SetPropertyBlock(properties);
            }
        }
    }

}
