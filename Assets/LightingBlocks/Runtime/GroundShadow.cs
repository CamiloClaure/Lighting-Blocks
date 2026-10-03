using UnityEngine;
using UnityEngine.Rendering;
namespace LightingBlocks
{
    // A soft dark disc on the floor under the player or a loose tile. Lighting casts no real shadows
    // (the sun is off and every light is unshadowed), so without it both read as floating. Every
    // walkable top (hub platforms, placed tiles) sits at y 0.
    public sealed class GroundShadow : MonoBehaviour
    {
        public const float FloorY = .006f;
        static Mesh quad; static Material material;
        Transform target; Renderer disc; float size; bool needsGround;
        public bool Shown => disc != null && disc.enabled && gameObject.activeInHierarchy;

        // needsGround hides the disc while the target is off the ground (falling into the void).
        public static GroundShadow Attach(Transform parent, float size, float strength, bool needsGround)
        {
            var go = new GameObject("Ground shadow", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = Shade(); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", new Color(0, 0, 0, strength)); r.SetPropertyBlock(block);
            var shadow = go.AddComponent<GroundShadow>(); shadow.target = parent; shadow.disc = r; shadow.size = size; shadow.needsGround = needsGround;
            shadow.Follow(); return shadow;
        }
        void LateUpdate() => Follow();
        public void Follow()
        {
            var p = target.position;
            if (needsGround) { var game = GameManager.Instance; disc.enabled = p.y > -.05f && (game == null || game.CanStand(p)); }
            transform.SetPositionAndRotation(new Vector3(p.x, FloorY, p.z), Quaternion.identity);
            var s = target.lossyScale; transform.localScale = new Vector3(size / s.x, 1, size / s.z);
        }

        // A unit quad lying flat in XZ, facing up: no rotation, so a non-uniform parent scale cannot skew it.
        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "Ground shadow" };
            quad.SetVertices(new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) });
            quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            quad.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0); quad.RecalculateBounds();
            return quad;
        }
        // WorldDressing's footfall halo, alpha blended in black: transparent, no depth write.
        static Material Shade()
        {
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Ground shadow" };
            material.SetTexture("_BaseMap", WorldDressing.Halo()); material.SetColor("_BaseColor", new Color(0, 0, 0, .5f));
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", 5); material.SetFloat("_DstBlend", 10); material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 2990; // over the opaque floor, under the crystals
            return material;
        }
    }
}
