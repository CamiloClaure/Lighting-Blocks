using UnityEngine;

namespace LightingBlocks
{
    // The same polygons define the visible terrain, physical mesh and movement support.
    public static class SoulBridgeLandscape
    {
        public static readonly Vector2[][] Shores = {
            new[] { new Vector2(-14,-24),new Vector2(12,-24),new Vector2(21,-16),new Vector2(20,-3),new Vector2(14,7),new Vector2(-14,7),new Vector2(-21,0),new Vector2(-22,-15) },
            new[] { new Vector2(-13,21),new Vector2(13,21),new Vector2(22,25),new Vector2(21,30),new Vector2(13,34),new Vector2(-13,34),new Vector2(-23,29),new Vector2(-20,24) },
            new[] { new Vector2(-13,48),new Vector2(13,48),new Vector2(22,56),new Vector2(19,71),new Vector2(9,78),new Vector2(-12,76),new Vector2(-21,67),new Vector2(-22,56) }
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
