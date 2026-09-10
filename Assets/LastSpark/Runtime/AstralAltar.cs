using UnityEngine;

namespace LightingBlocks
{
    public sealed class AstralAltar : MonoBehaviour
    {
        public string id;
        public bool outerIsland;
        public Light glow;
        public Renderer ring;
        public AstralFragment Occupant { get; set; }
        public Vector3 Seat => transform.position + Vector3.up * .62f;
        public void RefreshVisual()
        {
            bool active = Occupant != null;
            if (glow != null) glow.enabled = active;
            if (ring != null)
            {
                var block = new MaterialPropertyBlock();
                Color color = active ? new Color(.3f, 1f, .82f) : new Color(.28f, .36f, .54f);
                block.SetColor("_BaseColor", color); block.SetColor("_EmissionColor", color * (active ? 2 : .15f));
                ring.SetPropertyBlock(block);
            }
        }
    }
}
