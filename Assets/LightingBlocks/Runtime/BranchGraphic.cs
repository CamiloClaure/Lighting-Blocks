using UnityEngine;
using UnityEngine.UI;
namespace LightingBlocks
{
    // One tapered, curved skill-tree branch: a quadratic Bezier a -> control -> b drawn as a quad strip that
    // narrows from baseWidth to tipWidth. Points are local to the RectTransform's pivot (y up). Grown 0..1
    // draws only the first part of the curve, so a branch can grow out from its parent.
    // Unity does not carry Graphic's RequireComponent over to a subclass added at runtime.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BranchGraphic : MaskableGraphic
    {
        const int Segments = 24;
        public Vector2 a, control, b;
        public float baseWidth = 14, tipWidth = 7;
        float grown = 1;

        public float Grown => grown;
        public void SetGrown(float value) { value = Mathf.Clamp01(value); if (value == grown) return; grown = value; SetVerticesDirty(); }
        public Vector2 Point(float t) { float u = 1 - t; return u * u * a + 2 * u * t * control + t * t * b; }
        Vector2 Tangent(float t) { var d = 2 * (1 - t) * (control - a) + 2 * t * (b - control); return d.sqrMagnitude > .0001f ? d.normalized : (b - a).normalized; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (grown <= 0) return;
            int n = Mathf.Max(1, Mathf.CeilToInt(Segments * grown));
            for (int i = 0; i <= n; i++)
            {
                float t = grown * i / n;
                var tangent = Tangent(t); var normal = new Vector2(-tangent.y, tangent.x) * (Mathf.Lerp(baseWidth, tipWidth, t) * .5f);
                var p = Point(t);
                vh.AddVert(p + normal, color, new Vector2(t, 1)); vh.AddVert(p - normal, color, new Vector2(t, 0));
                if (i > 0) { int v = i * 2; vh.AddTriangle(v - 2, v - 1, v); vh.AddTriangle(v - 1, v + 1, v); }
            }
        }
    }
}
