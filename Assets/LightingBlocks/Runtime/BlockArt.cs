using UnityEngine;

namespace LightingBlocks
{
    [DisallowMultipleComponent]
    public sealed class BlockArt : MonoBehaviour
    {
        public Transform looseVisual, placedVisual;
        public Renderer[] bodies, seams;
        BridgeBlock block;
        MaterialPropertyBlock properties;
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
            foreach(var seam in seams) if(seam!=null)seam.enabled=block.Connected;
            bool held=block.Box!=null && block.Box.Holder!=null;
            foreach(var body in bodies)
            {
                if(body==null)continue;
                body.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor",held?new Color(1,.83f,.52f):nearby?new Color(1,.94f,.76f):Color.white);
                body.SetPropertyBlock(properties);
            }
        }
    }

}
