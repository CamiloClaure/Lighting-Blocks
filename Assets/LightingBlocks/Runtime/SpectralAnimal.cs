using System.Collections.Generic;
using UnityEngine;
namespace LightingBlocks
{
    public sealed class SpectralAnimal : MonoBehaviour
    {
        public Animator animator;
        public enum Mode{Idle,Wander,Flee,Recover}
        public Mode State {get;private set;}
        GameManager game;ForestNavigation nav;IslandForestDefinition island;System.Random random;
        readonly List<Vector3> path=new List<Vector3>();int waypoint,lastAnimation=-1;float decision,remaining,fleeTime,walkSpeed;
        public void Initialize(GameManager game,ForestNavigation navigation,IslandForestDefinition island,int seed)
        {this.game=game;nav=navigation;this.island=island;random=new System.Random(seed);remaining=2+(float)random.NextDouble()*4;walkSpeed=.7f+(float)random.NextDouble()*.5f;}
        public void Simulate(float delta)
        {
            if(game==null||GameHUD.SimulationPaused)return;
            float distance=(game.player.transform.position-transform.position).sqrMagnitude;
            bool awake=distance<=3600;if(animator!=null)animator.enabled=awake;if(!awake)return;
            decision-=delta;remaining-=delta;
            if(decision<=0)
            {
                decision=.2f;bool near=distance<16 && Landscape.Inside(new Vector2(game.player.transform.position.x,game.player.transform.position.z),Landscape.Shores[island.polygonIndex]);
                if(near&&State!=Mode.Flee){State=Mode.Flee;fleeTime=4;Pick(true);}
                if(State==Mode.Flee&&(distance>=81||fleeTime<=0)){State=Mode.Recover;remaining=2;path.Clear();}
                if((State==Mode.Idle||State==Mode.Recover)&&remaining<=0){State=Mode.Wander;Pick(false);}
            }
            if(State==Mode.Flee)fleeTime-=delta;
            int animation=State==Mode.Flee?2:State==Mode.Wander?1:0;
            if(animator!=null&&lastAnimation!=animation){animator.CrossFade(animation==2?"Run":animation==1?"Walk":"Idle",.15f);lastAnimation=animation;}
            if(waypoint>=path.Count){if(State==Mode.Wander){State=Mode.Idle;remaining=2+(float)random.NextDouble()*4;}return;}
            var next=Vector3.MoveTowards(transform.position,path[waypoint],(State==Mode.Flee?3.5f:walkSpeed)*delta);
            bool blocked=false;
            foreach(var b in game.Blocks)if(!b.placed&&b.Box.Holder==null&&(b.transform.position-next).sqrMagnitude<1){blocked=true;break;}
            if(blocked||!nav.Segment(transform.position,next)){path.Clear();State=Mode.Recover;remaining=1;return;}
            var direction=next-transform.position;if(direction.sqrMagnitude>.00001f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(direction),delta*8);
            transform.position=next;if((next-path[waypoint]).sqrMagnitude<.01f)waypoint++;
        }
        void Pick(bool fleeing)
        {
            waypoint=0;path.Clear();
            for(int i=0;i<12;i++)
            {
                float angle=(float)random.NextDouble()*Mathf.PI*2;var direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                if(fleeing){var away=transform.position-game.player.transform.position;away.y=0;direction=(away.normalized+direction*.65f).normalized;}
                var p=transform.position+direction*(fleeing?7:3+(float)random.NextDouble()*5);
                if(nav.Path(transform.position,p,path))return;
            }
            State=Mode.Recover;remaining=2;
        }
        void Update(){if(game==null||!game.autoTick)return;long before=ForestPerformanceProbe.Measuring?System.GC.GetAllocatedBytesForCurrentThread():0;Simulate(Time.deltaTime);if(ForestPerformanceProbe.Measuring)ForestPerformanceProbe.WildlifeBytes+=System.GC.GetAllocatedBytesForCurrentThread()-before;}
    }
}
