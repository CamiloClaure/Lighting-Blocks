using System.Collections.Generic;
using UnityEngine;
namespace LightingBlocks
{
    // One metre grid; the same footprint checks validate waypoints AND movement segments.
    public sealed class ForestNavigation
    {
        readonly Vector2[] polygon; readonly IslandForestLayout layout; readonly bool wildlife;
        readonly Dictionary<Vector2Int,Vector2Int> came=new Dictionary<Vector2Int,Vector2Int>();
        readonly Dictionary<Vector2Int,float> costs=new Dictionary<Vector2Int,float>();
        readonly List<Vector2Int> open=new List<Vector2Int>();
        readonly List<Vector3> scratch=new List<Vector3>();
        readonly HashSet<Vector2Int> reachable=new HashSet<Vector2Int>();
        readonly float clearance;
        public ForestNavigation(Vector2[] polygon,IslandForestLayout layout,bool wildlife,float clearance=.35f){this.polygon=polygon;this.layout=layout;this.wildlife=wildlife;this.clearance=clearance;}
        public void BuildReachability(Vector3 origin)
        {
            reachable.Clear();var queue=new Queue<Vector2Int>();var start=Cell(origin);if(!Walkable(Point(start)))return;queue.Enqueue(start);reachable.Add(start);
            while(queue.Count>0){var c=queue.Dequeue();for(int i=0;i<4;i++){var n=c+(i==0?Vector2Int.left:i==1?Vector2Int.right:i==2?Vector2Int.up:Vector2Int.down);if(!reachable.Contains(n)&&Segment(Point(c),Point(n))){reachable.Add(n);queue.Enqueue(n);}}}
        }
        public bool InReachableArea(Vector3 p)=>reachable.Contains(Cell(p))&&Segment(p,Point(Cell(p)));
        public bool Walkable(Vector3 p)
        {
            // Flatten first: the tree clearance below is a 3D distance against y=0 tree positions,
            // so an animal standing on a terrace would otherwise measure itself clear of a trunk
            // it is walking straight through. A no-op for the layout nav, which is already flat.
            p.y=0;
            if(!ForestLayoutGenerator.Inside(p,polygon,Mathf.Max(.8f,clearance)))return false;
            if((p-layout.definition.wisdomPosition).sqrMagnitude<1.5f*1.5f)return false;
            foreach(var t in layout.trees)if((p-t.position).sqrMagnitude<Mathf.Pow(.65f+clearance,2))return false;
            if(wildlife && ForestLayoutGenerator.Reserved(p,layout.definition,4))return false;
            return true;
        }
        // The cliff gate is wildlife-only on purpose. The layout nav runs with wildlife:false and its
        // reachability is re-checked against saved particle slots in ForestWorld.Valid, so adding a
        // height clause there would reject a save the moment a terrace moved. Animals pay it instead.
        public bool Segment(Vector3 a,Vector3 b)
        {int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/.3f));float last=Landscape.Height(a);
         for(int i=0;i<=steps;i++){var p=Vector3.Lerp(a,b,(float)i/steps);if(!Walkable(p))return false;
            // Same .4 the CharacterController steps over: an authored ramp passes, a cliff face does not.
            if(wildlife){float h=Landscape.Height(p);if(Mathf.Abs(h-last)>.4f)return false;last=h;}}
         return true;}
        static Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.RoundToInt(p.x),Mathf.RoundToInt(p.z));
        static Vector3 Point(Vector2Int p)=>new Vector3(p.x,0,p.y);
        public bool Reachable(Vector3 a,Vector3 b)=>Path(a,b,scratch);
        public bool Path(Vector3 a,Vector3 b,List<Vector3> output)
        {
            output.Clear();if(!Walkable(a)||!Walkable(b))return false;
            if(Segment(a,b)){output.Add(b);return true;}
            var start=Cell(a);var target=Cell(b);if(!Segment(a,Point(start))||!Segment(Point(target),b))return false;
            open.Clear();came.Clear();costs.Clear();open.Add(start);costs[start]=0;
            int attempts=0;
            while(open.Count>0 && attempts++<18000)
            {
                int best=0;float score=float.MaxValue;
                for(int i=0;i<open.Count;i++){float f=costs[open[i]]+Vector2Int.Distance(open[i],target);if(f<score){score=f;best=i;}}
                var current=open[best];open.RemoveAt(best);
                if(current==target){var c=target;output.Add(b);while(c!=start){output.Add(Point(c));c=came[c];}output.Reverse();return true;}
                for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                {
                    if(x==0&&z==0)continue;var next=current+new Vector2Int(x,z);
                    if(!Segment(Point(current),Point(next)))continue;
                    if(x!=0&&z!=0&&(!Walkable(Point(current+new Vector2Int(x,0)))||!Walkable(Point(current+new Vector2Int(0,z)))))continue;
                    float cost=costs[current]+(x!=0&&z!=0?1.414214f:1);
                    if(costs.TryGetValue(next,out float old)&&old<=cost)continue;
                    costs[next]=cost;came[next]=current;if(!open.Contains(next))open.Add(next);
                }
            }
            return false;
        }
    }
}
