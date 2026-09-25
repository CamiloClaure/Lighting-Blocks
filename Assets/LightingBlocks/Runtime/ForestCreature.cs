using System.Collections.Generic;
using UnityEngine;
namespace LightingBlocks
{
    public sealed class ForestCreature:MonoBehaviour
    {
        public enum Mode{Idle,Forage,Alert,Hop,Flee,Takeoff,Flight,Landing,Recover}
        public Mode State{get;private set;}
        public Animator animator;
        GameManager game;IslandForestLayout layout;ForestSpecies species;ForestNavigation nav;
        System.Random random;readonly List<Vector3> path=new List<Vector3>();int waypoint,lastClip;
        float timer,decision,flightT,flightDuration,flightHeight,flightDelay,cooldown;
        float takeoffLength=1,landingLength=1.25f,hopLength=1,fleeLength=.667f;
        readonly Dictionary<string,int> animationHashes=new Dictionary<string,int>();
        Vector3 flightStart,flightEnd,holdOrigin;bool launching,holding,flightFromAir;float holdTime,retry;int callCount;
        public bool Airborne=>State==Mode.Takeoff||State==Mode.Flight||State==Mode.Landing;
        public void Initialize(GameManager g,IslandForestLayout l,ForestSpecies s,int seed)
        {game=g;layout=l;species=s;nav=new ForestNavigation(Landscape.Shores[l.definition.polygonIndex],l,true,s.clearance);random=new System.Random(seed);timer=Range(2,6);decision=Range(0,.2f);lastClip=0;if(animator!=null){animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;foreach(var clip in animator.runtimeAnimatorController.animationClips){if(clip.name.EndsWith("Takeoff"))takeoffLength=clip.length;if(clip.name.EndsWith("Landing"))landingLength=clip.length;if(clip.name.EndsWith("Hop"))hopLength=clip.length;if(clip.name.EndsWith("Flee"))fleeLength=clip.length;}}}
        float Range(float min,float max)=>Mathf.Lerp(min,max,(float)random.NextDouble());
        void Animate(string name){if(animator==null)return;if(!animationHashes.TryGetValue(name,out int hash)){hash=Animator.StringToHash("Base Layer."+name);animationHashes.Add(name,hash);}animator.speed=name==species.move?hopLength*(species.flying?1.8f:1.6f):name==species.flee&&!species.flying?fleeLength*2.7f:1;if(lastClip!=hash){if(lastClip==0)animator.Play(hash,0,0);else animator.CrossFadeInFixedTime(hash,.1f,0,0);lastClip=hash;}}
        bool ClearGround(Vector3 p,float playerDistance)
        {
            if(!nav.Walkable(p))return false;var delta=game.player.transform.position-p;delta.y=0;if(delta.sqrMagnitude<playerDistance*playerDistance)return false;
            if(species.flying)foreach(var tree in layout.trees){var ground=p;ground.y=0;if((ground-tree.position).sqrMagnitude<12*tree.scale*tree.scale)return false;}
            foreach(var b in game.Blocks){var d=b.transform.position-p;d.y=0;if(d.sqrMagnitude<1.8f)return false;}
            return true;
        }
        public void Simulate(float dt)
        {
            if(game==null||dt<=0)return;
            bool paused=GameHUD.SimulationPaused;float distance=(game.player.transform.position-transform.position).sqrMagnitude;
            if(animator!=null)animator.enabled=!paused&&distance<3600;if(paused||distance>=3600)return;
            timer-=dt;decision-=dt;cooldown=Mathf.Max(0,cooldown-dt);
            if(Airborne){Fly(dt);return;}
            if(launching){flightDelay-=dt;if(flightDelay<=0){launching=false;if(StartFlight())return;State=Mode.Recover;timer=1;}return;}
            if(decision<=0)
            {
                decision=.2f;
                bool sameIsland=ForestLayoutGenerator.Inside(game.player.transform.position,Landscape.Shores[layout.definition.polygonIndex],0);
                if(sameIsland&&distance<species.fleeRadius*species.fleeRadius&&cooldown<=0&&State!=Mode.Flee)
                {
                    if(species.flying){launching=true;flightDelay=Range(.1f,.5f);State=Mode.Alert;Animate("Alert");return;}
                    State=Mode.Flee;timer=4;Pick(true);
                }
                else if(sameIsland&&distance<species.alertRadius*species.alertRadius&&(State==Mode.Idle||State==Mode.Forage)){State=Mode.Alert;timer=2;}
                if(State==Mode.Flee&&(distance>81||timer<=0)){State=Mode.Recover;timer=Range(2,4);cooldown=2;path.Clear();}
                if(State==Mode.Alert){var toward=game.player.transform.position-transform.position;toward.y=0;if(toward.sqrMagnitude>.01f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(toward),.25f);}
                if(!Airborne&&State!=Mode.Flee&&State!=Mode.Hop&&timer<=0){if(random.NextDouble()<.55){State=Mode.Forage;timer=Range(2,5);}else{State=Mode.Hop;timer=Range(3,6);Pick(false);}}
                if(species.flying&&++callCount%100==0&&State==Mode.Forage)ForestSoundscape.Play("bird",transform.position,.035f);
            }
            Animate(State==Mode.Flee?species.flee:State==Mode.Hop?species.move:State==Mode.Alert?"Alert":State==Mode.Forage?species.forage:species.idle);
            if((State!=Mode.Hop&&State!=Mode.Flee)||waypoint>=path.Count)return;
            float gait=1;if(animator!=null&&animator.isActiveAndEnabled){float phase=Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime,1);gait=Mathf.Max(.08f,Mathf.Sin(phase*Mathf.PI*2)*3.05f);}
            var next=Vector3.MoveTowards(transform.position,path[waypoint],(State==Mode.Flee?species.fleeSpeed:species.walkSpeed)*gait*dt);
            if(!ClearGround(next,0)||!nav.Segment(transform.position,next)){State=Mode.Recover;timer=1;path.Clear();return;}
            var direction=next-transform.position;if(direction.sqrMagnitude>.00001f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(direction),dt*6);
            transform.position=next;if((next-path[waypoint]).sqrMagnitude<.01f)waypoint++;
            if(waypoint>=path.Count){State=State==Mode.Flee?Mode.Recover:Mode.Idle;timer=Range(2,5);cooldown=2;Animate(species.idle);}
        }
        void Pick(bool fleeing)
        {
            path.Clear();waypoint=0;
            for(int i=0;i<16;i++){float a=Range(0,Mathf.PI*2);var v=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));if(fleeing){var away=transform.position-game.player.transform.position;away.y=0;v=(away.normalized+v*.6f).normalized;}
                var p=transform.position+v*(fleeing?Range(7,10):Range(2,5));if(ClearGround(p,0)&&nav.Path(transform.position,p,path))return;}
            State=Mode.Recover;timer=2;
        }
        bool StartFlight()
        {
            var polygon=Landscape.Shores[layout.definition.polygonIndex];
            for(int n=0;n<48;n++)
            {
                var p=layout.habitats[random.Next(layout.habitats.Count)];float dist=Vector3.Distance(transform.position,p);
                if(dist<7||dist>30||!ClearGround(p,8))continue;
                bool safe=true;float height=3.5f;
                for(int k=0;k<=24;k++){float t=k/24f;var sample=Vector3.Lerp(transform.position,p,t);
                    // Lighting: the overhead safety corridor must stay above supported crystal, not the
                    // legacy island polygon (see PlatformSupport.cs). Landing itself is already gated via
                    // ClearGround->nav.Walkable; this only covers the arc in between.
                    bool corridorSafe=BridgeProfile.Current.platformGround?PlatformSupport.Supports(sample):ForestLayoutGenerator.Inside(sample,polygon,.6f);
                    if(!corridorSafe){safe=false;break;}
                    foreach(var tree in layout.trees)if((new Vector3(sample.x,0,sample.z)-tree.position).sqrMagnitude<9*tree.scale*tree.scale){if(t<.16f||t>.84f){safe=false;break;}height=Mathf.Max(height,(7.5f*tree.scale)/Mathf.Sin(Mathf.PI*t));}
                    if((sample-layout.definition.wisdomPosition).sqrMagnitude<25){safe=false;break;}
                    if(!safe)break;}
                if(!safe||height>18)continue;bool alreadyFlying=Airborne;flightFromAir=alreadyFlying;holding=false;flightStart=transform.position;flightEnd=p;flightHeight=height;flightT=0;flightDuration=Mathf.Max(takeoffLength+landingLength+1,dist/4+takeoffLength+landingLength);State=alreadyFlying?Mode.Flight:Mode.Takeoff;Animate(alreadyFlying?"Flight":"Takeoff");ForestSoundscape.Play("wing",transform.position,.06f);return true;
            }return false;
        }
        void Fly(float dt)
        {
            if(holding){holdTime+=dt;retry-=dt;var orbitNext=holdOrigin+new Vector3((Mathf.Cos(holdTime)-1)*.25f,Mathf.Sin(holdTime*.7f)*.04f,Mathf.Sin(holdTime)*.25f);var orbitDirection=orbitNext-transform.position;if(orbitDirection.sqrMagnitude>.00001f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(orbitDirection),dt*3);transform.position=orbitNext;Animate("Flight");if(retry<=0){retry=1;StartFlight();}return;}
            // Reject an occupied landing while still high enough to choose another route.
            if(flightT>.6f&&!ClearGround(flightEnd,8))
            {
                holding=true;holdOrigin=transform.position;holdTime=0;retry=0;State=Mode.Flight;return;
            }
            flightT=Mathf.Min(1,flightT+dt/flightDuration);float t=Mathf.SmoothStep(0,1,flightT);
            var next=Vector3.Lerp(flightStart,flightEnd,t)+Vector3.up*(Mathf.Sin(Mathf.PI*t)*flightHeight);
            var direction=next-transform.position;if(direction.sqrMagnitude>.00001f){var horizontal=new Vector3(direction.x,0,direction.z);if(horizontal.sqrMagnitude>.000001f){float pitch=-Mathf.Clamp(Mathf.Atan2(direction.y,horizontal.magnitude)*Mathf.Rad2Deg,-18,18);var rotation=Quaternion.LookRotation(horizontal)*Quaternion.Euler(pitch,0,0);transform.rotation=Quaternion.Slerp(transform.rotation,rotation,dt*5);}}
            transform.position=next;
            State=!flightFromAir&&flightT*flightDuration<takeoffLength?Mode.Takeoff:(1-flightT)*flightDuration<landingLength?Mode.Landing:Mode.Flight;Animate(State==Mode.Takeoff?"Takeoff":State==Mode.Landing?"Landing":"Flight");
            if(flightT>=1){transform.position=flightEnd;transform.rotation=Quaternion.Euler(0,transform.eulerAngles.y,0);State=Mode.Recover;timer=Range(3,6);cooldown=3;Animate(species.idle);}
        }
        void Update(){if(game==null||!game.autoTick)return;long before=ForestPerformanceProbe.Measuring?System.GC.GetAllocatedBytesForCurrentThread():0;Simulate(Time.deltaTime);if(ForestPerformanceProbe.Measuring)ForestPerformanceProbe.WildlifeBytes+=System.GC.GetAllocatedBytesForCurrentThread()-before;}
    }
}
