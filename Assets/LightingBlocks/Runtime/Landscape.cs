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
            new[] { new Vector2(-13,21),new Vector2(13,21),new Vector2(30,22),new Vector2(43,25),new Vector2(46,29),new Vector2(46,66),new Vector2(38,78),new Vector2(14,86),new Vector2(-14,86),new Vector2(-34,80),new Vector2(-46,67),new Vector2(-46,29),new Vector2(-44,25),new Vector2(-30,21) },
            new[] { new Vector2(-13,48),new Vector2(13,48),new Vector2(31,50),new Vector2(43,57),new Vector2(48,70),new Vector2(45,86),new Vector2(34,99),new Vector2(16,106),new Vector2(-6,107),new Vector2(-26,101),new Vector2(-40,89),new Vector2(-47,73),new Vector2(-45,58),new Vector2(-32,50) }
        };
        public const float Expansion = 52f;
        // Terraced relief. A plateau is low==high; a ramp lerps low->high along from->to. One struct
        // covers both, so the floor mesh and Height can never disagree about which is which.
        // Authored in the same space as the Shores literals above: island 1 x is scaled and island 2
        // z is shifted by the same static constructor, so read these next to the polygon they sit on.
        // Rules the suite asserts: area convex, every vertex at least 5 m inside its shore, clear of
        // the wisdom courts and the bridge landing bands. The +1.8 m ceiling is load-bearing - the
        // camera has no collision pass and relies on nothing standing taller (see StrategyCamera).
        public struct Terrace{public int island;public Vector2[] area;public float low,high;public Vector2 from,to;}
        public static readonly Terrace[] Terraces = {
            new Terrace{island=0,area=new[]{new Vector2(14,-38),new Vector2(30,-38),new Vector2(30,-16),new Vector2(14,-16)},low=.9f,high=.9f},
            new Terrace{island=0,area=new[]{new Vector2(10,-30),new Vector2(14,-30),new Vector2(14,-24),new Vector2(10,-24)},low=0,high=.9f,from=new Vector2(10,-27),to=new Vector2(14,-27)},
            new Terrace{island=1,area=new[]{new Vector2(16,28),new Vector2(32,28),new Vector2(32,44),new Vector2(16,44)},low=.9f,high=.9f},
            new Terrace{island=1,area=new[]{new Vector2(11,34),new Vector2(16,34),new Vector2(16,40),new Vector2(11,40)},low=0,high=.9f,from=new Vector2(11,37),to=new Vector2(16,37)},
            new Terrace{island=2,area=new[]{new Vector2(12,66),new Vector2(32,66),new Vector2(32,88),new Vector2(12,88)},low=.9f,high=.9f},
            new Terrace{island=2,area=new[]{new Vector2(16,72),new Vector2(28,72),new Vector2(28,84),new Vector2(16,84)},low=1.8f,high=1.8f},
            new Terrace{island=2,area=new[]{new Vector2(8,74),new Vector2(12,74),new Vector2(12,80),new Vector2(8,80)},low=0,high=.9f,from=new Vector2(8,77),to=new Vector2(12,77)},
            new Terrace{island=2,area=new[]{new Vector2(13,76),new Vector2(16,76),new Vector2(16,81),new Vector2(13,81)},low=.9f,high=1.8f,from=new Vector2(13,78.5f),to=new Vector2(16,78.5f)}
        };
        public static float BridgeStart(int gap) => gap == 0 ? 8 : 35 + Expansion;
        public static float Area(Vector2[] polygon){float area=0;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)area+=polygon[j].x*polygon[i].y-polygon[i].x*polygon[j].y;return Mathf.Abs(area)*.5f;}
        static Landscape()
        {
            float scale=Area(Shores[0])/Area(Shores[1]);for(int i=0;i<Shores[1].Length;i++)Shores[1][i].x*=scale;for(int i=0;i<Shores[2].Length;i++)Shores[2][i].y+=Expansion;
            // The terraces ride the same two transforms as the polygons they sit on, or they drift
            // off their island the moment either literal above is retuned.
            for(int t=0;t<Terraces.Length;t++)
            {
                var terrace=Terraces[t];if(terrace.island!=1&&terrace.island!=2)continue;
                for(int i=0;i<terrace.area.Length;i++)terrace.area[i]=Shift(terrace.area[i],terrace.island,scale);
                Terraces[t].from=Shift(terrace.from,terrace.island,scale);Terraces[t].to=Shift(terrace.to,terrace.island,scale);
            }
            Bound();
        }
        static Vector2 Shift(Vector2 p,int island,float scale)=>island==1?new Vector2(p.x*scale,p.y):new Vector2(p.x,p.y+Expansion);
        // Height sits in the wildlife pathfinder's inner loop, which samples every .3 m for ~48
        // animals, so it gets a bounding box per terrace plus one over all of them. Cheap insurance
        // rather than a measured win: the frame cost that prompted it turned out to be the editor
        // hogging the machine, not this. Keep it anyway, the polygon tests are not free.
        static Vector2[] low,high;static Vector2 extentLow,extentHigh;
        static void Bound()
        {
            low=new Vector2[Terraces.Length];high=new Vector2[Terraces.Length];
            for(int t=0;t<Terraces.Length;t++)
            {
                Vector2 lo=Terraces[t].area[0],hi=lo;
                foreach(var v in Terraces[t].area){lo=Vector2.Min(lo,v);hi=Vector2.Max(hi,v);}
                low[t]=lo;high[t]=hi;
            }
            extentLow=low[0];extentHigh=high[0];
            for(int t=1;t<Terraces.Length;t++){extentLow=Vector2.Min(extentLow,low[t]);extentHigh=Vector2.Max(extentHigh,high[t]);}
        }
        public static Vector3 UpgradePosition(Vector3 p){if(p.z>=34)p.z+=Expansion;return p;}
        public static bool Inside(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
                if ((polygon[i].y>point.y)!=(polygon[j].y>point.y) && point.x < (polygon[j].x-polygon[i].x)*(point.y-polygon[i].y)/(polygon[j].y-polygon[i].y)+polygon[i].x) inside=!inside;
            return inside;
        }
        // Ground level at an XZ. The one source of truth: the floor mesh is built from it, every
        // spawn is lifted by it, and a verification check raycasts the collider to prove they agree.
        // Deliberately linear, never smoothed: a lerp over a convex quad is affine, which is what
        // lets the mesh fan match this exactly. Terrace edges step hard, and the mesh puts a wall
        // there. Off every island, and inside the 5 m rim band, it is zero - the keel, the bridge
        // landings and all the rim dressing are pinned to that plane.
        public static float Height(float x,float z)
        {
            // Off every terrace is the overwhelmingly common answer, so the box test settles it in two
            // compares. No shore test is needed: terraces are authored at least 5 m inside their own
            // shore and the suite asserts it, so being inside one means being on that island.
            if(x<extentLow.x||x>extentHigh.x||z<extentLow.y||z>extentHigh.y)return 0;
            var p=new Vector2(x,z);float best=0;
            for(int t=0;t<Terraces.Length;t++)
            {
                if(x<low[t].x||x>high[t].x||z<low[t].y||z>high[t].y)continue;
                if(!Inside(p,Terraces[t].area))continue;
                float h=Level(Terraces[t],p);if(h>best)best=h;
            }
            return best;
        }
        public static float Height(Vector3 p)=>Height(p.x,p.z);
        // One terrace's own surface, with no containment test. The mesh evaluates its corner and
        // centroid vertices through this, because Inside is ambiguous exactly on a boundary and a
        // corner that came back zero would tilt the whole plateau. Height calls it after Inside.
        public static float Level(Terrace t,Vector2 p)
        {
            float h=t.low;
            if(t.high!=t.low){var e=t.to-t.from;h=Mathf.Lerp(t.low,t.high,Mathf.Clamp01(Vector2.Dot(p-t.from,e)/Mathf.Max(.0001f,e.sqrMagnitude)));}
            return h<=0?0:h*Mathf.Clamp01((ForestLayoutGenerator.Edge(p,Shores[t.island])-2)/3);
        }
        public static bool Supports(Vector3 p)
        {
            foreach(var poly in Shores)
                if(Inside(new Vector2(p.x-.02f,p.z-.02f),poly) && Inside(new Vector2(p.x+.02f,p.z+.02f),poly)) return true;
            return false;
        }
    }
}
