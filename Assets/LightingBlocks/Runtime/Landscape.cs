using UnityEngine;

namespace LightingBlocks
{
    // The same polygons define the visible terrain, physical mesh and movement support.
    // They are listed counter-clockwise: the mesh builders fan from the centroid and rely on it.
    // The edges that face a rift are fixed - the bridge cells are measured from them - so the
    // courts only ever grow sideways and away from the gaps.
    public static class Landscape
    {
        // The pristine shipped polygons. Never mutated: Reload always deep-copies from here, so a
        // profile switch (or several) can never drift the source data.
        static readonly Vector2[][] Source = {
            new[] { new Vector2(-18,-57),new Vector2(4,-58),new Vector2(24,-52),new Vector2(38,-41),new Vector2(45,-26),new Vector2(46,-10),new Vector2(33,2),new Vector2(14,7),new Vector2(-14,7),new Vector2(-30,3),new Vector2(-41,-6),new Vector2(-47,-20),new Vector2(-45,-36),new Vector2(-34,-49) },
            new[] { new Vector2(-13,21),new Vector2(13,21),new Vector2(30,22),new Vector2(43,25),new Vector2(46,29),new Vector2(46,66),new Vector2(38,78),new Vector2(14,86),new Vector2(-14,86),new Vector2(-34,80),new Vector2(-46,67),new Vector2(-46,29),new Vector2(-44,25),new Vector2(-30,21) },
            new[] { new Vector2(-13,48),new Vector2(13,48),new Vector2(31,50),new Vector2(43,57),new Vector2(48,70),new Vector2(45,86),new Vector2(34,99),new Vector2(16,106),new Vector2(-6,107),new Vector2(-26,101),new Vector2(-40,89),new Vector2(-47,73),new Vector2(-45,58),new Vector2(-32,50) }
        };
        public static Vector2[][] Shores;
        // The historical, shipped Expansion value. UpgradePosition migrates pre-v4 saves that were
        // written against the shipped layout, so it always uses this fixed number, never the live
        // profile: that migration is history and must not follow whatever scene is currently active.
        public const float LegacyExpansion = 52f;
        public static float Expansion => BridgeProfile.Current.expansion;
        public static float BridgeStart(int gap) => gap == 0 ? 8 : 87 + BridgeProfile.Current.islandShift;
        public static float Area(Vector2[] polygon){float area=0;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)area+=polygon[j].x*polygon[i].y-polygon[i].x*polygon[j].y;return Mathf.Abs(area)*.5f;}
        static Landscape(){Reload();}
        // Rebuilds Shores from the pristine Source under the active profile. Called once at startup
        // and again every time BridgeProfile.Select switches to a different profile.
        public static void Reload()
        {
            Shores = new Vector2[Source.Length][];
            for (int i = 0; i < Source.Length; i++) Shores[i] = (Vector2[])Source[i].Clone();
            float scale = Area(Source[0]) / Area(Source[1]);
            for (int i = 0; i < Shores[1].Length; i++) { Shores[1][i].x *= scale; Shores[1][i].y += BridgeProfile.Current.islandShift; }
            for (int i = 0; i < Shores[2].Length; i++) Shores[2][i].y += BridgeProfile.Current.expansion;
            float south = float.MaxValue, north = float.MinValue;
            foreach (var point in Shores[0]) south = Mathf.Min(south, point.y);
            foreach (var point in Shores[2]) north = Mathf.Max(north, point.y);
            CentreZ = (south + north) * .5f; Span = north - south;
        }
        // The world's middle and its length along z, from the first island's south shore to the
        // last island's north shore. Background scenery is scattered against these instead of the
        // shipped literals (50.5 and 217), which stop describing the world the moment a profile
        // pushes the islands apart.
        public static float CentreZ { get; private set; }
        public static float Span { get; private set; }
        public static Vector3 UpgradePosition(Vector3 p){if(p.z>=34)p.z+=LegacyExpansion;return p;}
        // Metres of +z a shipped-layout point on this island has moved under the active profile.
        // 0 for island 1 (never moves), islandShift for island 2, and island 3's TOTAL offset less the
        // LegacyExpansion it already carried in the shipped layout.
        public static float IslandOffset(int polygonIndex) => polygonIndex == 1 ? BridgeProfile.Current.islandShift : polygonIndex == 2 ? BridgeProfile.Current.expansion - LegacyExpansion : 0f;
        public static bool Shifted => IslandOffset(1) != 0 || IslandOffset(2) != 0;
        // Maps a point authored or saved in the SHIPPED layout into the current one. 21 and 100 are
        // the shipped island 2 / island 3 start edges.
        public static Vector3 ImportPosition(Vector3 p)
        {
            if (!Shifted) return p;
            if (p.z >= 100) p.z += IslandOffset(2);
            else if (p.z >= 21) p.z += IslandOffset(1);
            return p;
        }
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
