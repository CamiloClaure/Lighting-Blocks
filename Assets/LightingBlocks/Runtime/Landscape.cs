using UnityEngine;

namespace LightingBlocks
{
    // The same polygons define the visible terrain, physical mesh and movement support.
    // They are listed counter-clockwise: the mesh builders fan from the centroid and rely on it.
    // The edges that face a rift are fixed - the bridge cells are measured from them - so the
    // courts only ever grow sideways and away from the gaps.
    public static class Landscape
    {
        public static readonly Vector2[][] Shores = {
            new[] { new Vector2(-18,-57),new Vector2(4,-58),new Vector2(24,-52),new Vector2(38,-41),new Vector2(45,-26),new Vector2(46,-10),new Vector2(33,2),new Vector2(14,7),new Vector2(-14,7),new Vector2(-30,3),new Vector2(-41,-6),new Vector2(-47,-20),new Vector2(-45,-36),new Vector2(-34,-49) },
            new[] { new Vector2(-13,21),new Vector2(13,21),new Vector2(30,22),new Vector2(43,25),new Vector2(46,29),new Vector2(38,32),new Vector2(20,34),new Vector2(13,34),new Vector2(-13,34),new Vector2(-22,34),new Vector2(-38,33),new Vector2(-46,29),new Vector2(-44,25),new Vector2(-30,21) },
            new[] { new Vector2(-13,48),new Vector2(13,48),new Vector2(31,50),new Vector2(43,57),new Vector2(48,70),new Vector2(45,86),new Vector2(34,99),new Vector2(16,106),new Vector2(-6,107),new Vector2(-26,101),new Vector2(-40,89),new Vector2(-47,73),new Vector2(-45,58),new Vector2(-32,50) }
        };
        public static bool Inside(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
                if ((polygon[i].y>point.y)!=(polygon[j].y>point.y) && point.x < (polygon[j].x-polygon[i].x)*(point.y-polygon[i].y)/(polygon[j].y-polygon[i].y)+polygon[i].x) inside=!inside;
            return inside;
        }
        public static bool Supports(Vector3 p)
        {
            foreach(var poly in Shores)
                if(Inside(new Vector2(p.x-.02f,p.z-.02f),poly) && Inside(new Vector2(p.x+.02f,p.z+.02f),poly)) return true;
            return false;
        }
    }
}
