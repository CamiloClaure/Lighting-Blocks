using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace LightingBlocks
{
    public sealed class WisdomTree : MonoBehaviour
    {
        public Renderer[] glowRenderers;
        public Light glowLight;
        public ParticleSystem motes,release;
        public LineRenderer[] chargeLines;
        public IslandForestDefinition Definition {get;private set;}
        public bool Charging=>remaining>0;
        public float Remaining=>remaining;
        ForestWorld forest;GameManager game;float remaining;bool channelled;
        MaterialPropertyBlock properties;
        float baseIntensity=1.1f;
        // ---- inverted-tree tuning knobs --------------------------------------
        // Concept art reads as a glowing white-lavender trunk under a deep blue-violet canopy,
        // the opposite of the authored materials. Everything a tuning pass touches lives here.
        // Private and unserialized like baseIntensity above, so editing a value here actually
        // reaches every tree instead of being shadowed by numbers baked into WisdomTree.prefab.
        Color trunkEmission=new Color(1.6f,1.35f,2.4f,1);   // HDR, bark and filaments
        Color leafEmission=new Color(.05f,.03f,.16f,1);     // canopy, almost out
        Color leafBase=new Color(.16f,.13f,.30f,1);         // canopy albedo, pushed to blue-violet
        Color lanternColour=new Color(2.6f,1.7f,.7f,1);     // HDR warm, unlit glass
        float uplightIntensity=1.2f;
        int stringCount=12;                                 // ForestVerification asserts 12 strings and 12 lanterns: move both together
        float stringMin=1.4f,stringMax=2.4f;
        MaterialPropertyBlock runeProperties;Renderer rune;float yaw;
        // One entry per submesh we repaint: the renderer, its material slot, and the block we keep re-applying.
        (Renderer renderer,int index,MaterialPropertyBlock block)[] repaints;
        static readonly Color Lavender=new Color(.812f,.753f,1f);      // #CFC0FF
        static readonly Color LanternWarm=new Color(1f,.714f,.380f);   // #FFB661
        static readonly Color RuneInk=new Color(.55f,.42f,1.5f,1);     // HDR, so bloom catches the circle
        static readonly Color CordInk=new Color(.12f,.1f,.16f,1);
        void Awake(){properties=new MaterialPropertyBlock();}
        public void Initialize(ForestWorld world,IslandForestDefinition definition){forest=world;game=world.GetComponent<GameManager>();Definition=definition;}
        public bool BeginCharge(bool silent=false)
        {
            if(GameHUD.GameplayBlocked||Charging)return false;
            if(forest.Nearby(game.player.transform.position)!=this)return false;
            if(forest.Inventory(Definition.id).inventory<3){if(!silent)game.Notify("Collect 3 particles on this island",false);return false;}
            if(!forest.FindOutput(Definition,out _)){if(!silent)game.Notify("Move blocks away from the Wisdom Tree",false);return false;}
            remaining=.6f;channelled=silent;ForestSoundscape.Play("charge",transform.position,.1f);if(!silent)game.Notify("Charging the Wisdom Tree...",false);return true;
        }
        public void Tick(float delta)
        {
            if(GameHUD.SimulationPaused||remaining<=0||delta<=0)return;
            remaining=Mathf.Max(0,remaining-delta);
            if(remaining==0)
            {
                var inventory=forest.Inventory(Definition.id);
                if(inventory.inventory>=3&&game.TrySpawnAtWisdomTree(Definition.id,out var block))
                {
                    inventory.inventory-=3;ForestSoundscape.Play("release",block.transform.position,.12f);
                    if(release!=null){release.transform.position=block.transform.position+Vector3.up*.4f;release.Play();}
                    // A held channel completes a charge every .6 s. Chiming and writing the whole
                    // save that often turns the feature into a stutter, so a channelled stone is
                    // announced quietly and left to the ten second autosave.
                    game.Notify("A BLOCK ANSWERS\nIt waits beside the Wisdom Tree.",!channelled);if(!channelled)game.Save();
                }
                else game.Notify("Move blocks away from the Wisdom Tree",false);
            }
        }
        void Update()
        {
            if(forest==null)return;
            if(game.autoTick)Tick(Time.deltaTime);
            float strength=Charging?1+Mathf.Sin((.6f-remaining)/.6f*Mathf.PI)*2:1;
            if(glowLight!=null)glowLight.intensity=baseIntensity*strength;
            if(rune!=null){float glow=Mathf.Max(.35f,strength);runeProperties.SetColor("_BaseColor",new Color(RuneInk.r*glow,RuneInk.g*glow,RuneInk.b*glow,1));rune.SetPropertyBlock(runeProperties);}
            if(glowRenderers!=null)foreach(var r in glowRenderers){properties.SetFloat("_Charge",strength);r.SetPropertyBlock(properties);}
            // Per-slot blocks win over the per-renderer one, so the pulse has to be written into them too.
            if(repaints!=null)foreach(var e in repaints){e.block.SetFloat("_Charge",strength);e.renderer.SetPropertyBlock(e.block,e.index);}
            if(chargeLines!=null)for(int i=0;i<chargeLines.Length;i++)
            {
                var line=chargeLines[i];line.enabled=Charging;
                if(Charging){float t=1-remaining/.6f;var from=game.player.transform.position+Vector3.up*(.6f+i*.15f);var to=transform.position+Vector3.up*(1.3f+i*.3f);line.SetPosition(0,Vector3.Lerp(from,to,Mathf.Clamp01(t-.22f)));line.SetPosition(1,Vector3.Lerp(from,to,t));}
            }
        }

        // ---- hero dressing ---------------------------------------------------
        // Concept art: a big lavender light, a glowing rune circle on the ground and gold
        // lanterns strung off the canopy. All of it is scenery built at runtime, so the prefab
        // stays untouched: no colliders, and every step bails if its child already exists.
        void Start()
        {
            // ponytail: position-derived spin instead of a seeded Random - three trees only need
            // to stop looking copy-pasted, and this stays stable across saves for free.
            yaw=Mathf.Repeat(transform.position.x*.7f+transform.position.z*1.3f,1)*Mathf.PI*.5f;
            if(glowLight!=null){glowLight.color=Lavender;glowLight.range=22;glowLight.shadows=LightShadows.Soft;glowLight.renderingLayerMask=1;}
            Repaint();   // no canopy fill any more: the canopy is the dark half of the read
            if(transform.Find("Trunk uplight")==null)
            {
                // Stands off the trunk and looks back at it. The camera sits at -x,-z (rig yaw 45), so that is the face worth lighting.
                var local=new Vector3(-1,.15f,-1);
                var spot=NewLight("Trunk uplight",transform,local,Lavender,uplightIntensity,10);
                spot.type=LightType.Spot;spot.spotAngle=55;spot.transform.localRotation=Quaternion.LookRotation(new Vector3(0,2.4f,0)-local);
            }
            BuildLanterns();   // before the circle: the fallback bounds must not swallow a 6 m disc
            BuildRuneCircle();
        }
        Light NewLight(string name,Transform parent,Vector3 local,Color colour,float intensity,float range)
        {
            var o=new GameObject(name,typeof(Light));o.transform.SetParent(parent,false);o.transform.localPosition=local;
            var light=o.GetComponent<Light>();
            light.type=LightType.Point;light.color=colour;light.intensity=intensity;light.range=range;
            light.shadows=LightShadows.None;light.renderingLayerMask=1;
            return light;
        }
        // Bark and filaments light up, leaves go dark. Per material slot, because one LOD renderer
        // carries bark, pods and leaves at once. Property blocks only: Wisdom_Bark.mat and
        // Wisdom_Leaves.mat stay as SpiritPathLook authored them, nothing leaks into the shared assets.
        void Repaint()
        {
            var found=new List<(Renderer renderer,int index,MaterialPropertyBlock block)>();
            foreach(var r in GetComponentsInChildren<Renderer>(true))
            {
                var materials=r.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var m=materials[i];if(m==null)continue;
                    // Wisdom_Pods is deliberately left out: those are the HDR gold pods, the one
                    // warm accent in the canopy, and darkening them puts the tree's lights out.
                    bool leaf=m.name.Contains("Leaves");
                    if(!leaf&&!m.name.Contains("Bark")&&!m.name.Contains("Filament"))continue;
                    var block=new MaterialPropertyBlock();
                    block.SetColor("_EmissionColor",leaf?leafEmission:trunkEmission);
                    if(leaf&&m.HasProperty("_BaseColor"))block.SetColor("_BaseColor",leafBase);
                    r.SetPropertyBlock(block,i);found.Add((r,i,block));
                }
            }
            repaints=found.ToArray();
        }
        static Material Unlit(Color colour)
        {
            var shader=Shader.Find("Universal Render Pipeline/Unlit");if(shader==null)return null;
            var material=new Material(shader);material.SetColor("_BaseColor",colour);return material;
        }

        // ---- rune circle -----------------------------------------------------
        void BuildRuneCircle()
        {
            var existing=transform.Find("Rune circle");
            if(existing!=null){rune=existing.GetComponent<Renderer>();runeProperties=new MaterialPropertyBlock();return;}
            var material=Unlit(RuneInk);if(material==null)return;
            material.name="Rune circle";
            material.SetFloat("_Cull",0);   // ponytail: double sided, so a winding slip can never make the hero prop invisible
            var vertices=new List<Vector3>();var triangles=new List<int>();
            foreach(float radius in new[]{1.6f,2.3f,3f})Ring(vertices,triangles,radius,.12f);
            for(int i=0;i<12;i++)Dash(vertices,triangles,yaw+i*Mathf.PI*2/12,2.65f,.5f,.12f);
            var mesh=new Mesh{name="Rune circle"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
            // ponytail: no normals or UVs - Unlit reads neither. Add them if this ever goes lit.
            var o=new GameObject("Rune circle",typeof(MeshFilter),typeof(MeshRenderer));
            o.transform.SetParent(transform,false);o.transform.localPosition=new Vector3(0,.03f,0);
            o.GetComponent<MeshFilter>().sharedMesh=mesh;
            rune=o.GetComponent<Renderer>();rune.sharedMaterial=material;
            rune.shadowCastingMode=ShadowCastingMode.Off;rune.receiveShadows=false;
            runeProperties=new MaterialPropertyBlock();
        }
        // Flat band in XZ, wound clockwise seen from above so the face points up.
        static void Ring(List<Vector3> vertices,List<int> triangles,float radius,float width)
        {
            const int segments=32;float inner=radius-width*.5f,outer=radius+width*.5f;
            for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;int n=vertices.Count;
                vertices.Add(new Vector3(Mathf.Cos(a)*inner,0,Mathf.Sin(a)*inner));
                vertices.Add(new Vector3(Mathf.Cos(a)*outer,0,Mathf.Sin(a)*outer));
                vertices.Add(new Vector3(Mathf.Cos(b)*inner,0,Mathf.Sin(b)*inner));
                vertices.Add(new Vector3(Mathf.Cos(b)*outer,0,Mathf.Sin(b)*outer));
                triangles.AddRange(new[]{n,n+3,n+1,n,n+2,n+3});
            }
        }
        // One radial tick between the middle and outer ring, same winding as Ring.
        static void Dash(List<Vector3> vertices,List<int> triangles,float angle,float centre,float length,float width)
        {
            var radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var side=new Vector3(-radial.z,0,radial.x)*(width*.5f);
            var near=radial*(centre-length*.5f);var far=radial*(centre+length*.5f);int n=vertices.Count;
            vertices.Add(near-side);vertices.Add(near+side);vertices.Add(far+side);vertices.Add(far-side);
            triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }

        // ---- lantern strings -------------------------------------------------
        void BuildLanterns()
        {
            if(transform.Find("Lantern string")!=null)return;
            var source=glowRenderers!=null&&glowRenderers.Length>0?glowRenderers:GetComponentsInChildren<Renderer>();
            var bounds=new Bounds();bool any=false;
            foreach(var r in source){if(r==null)continue;if(any)bounds.Encapsulate(r.bounds);else{bounds=r.bounds;any=true;}}
            if(!any)return;
            var gold=Unlit(lanternColour);var cord=Unlit(CordInk);if(gold==null||cord==null)return;
            gold.name="Lantern glass";cord.name="Lantern cord";
            float hang=bounds.min.y+bounds.size.y*.7f;
            // ponytail: one fixed seed, with yaw spinning the ring per tree. Deterministic enough for the
            // rebuild check and varied enough that three trees do not read as copies. Per-tree seeds if that stops holding.
            var random=new System.Random(20260918);
            for(int i=0;i<stringCount;i++)
            {
                float angle=yaw+i*Mathf.PI*2/stringCount;
                // Between 45 % and 85 % of the way out along the canopy's own footprint, so every cord starts under leaves.
                float reach=.45f+(float)random.NextDouble()*.4f,length=stringMin+(float)random.NextDouble()*(stringMax-stringMin);
                var attach=transform.InverseTransformPoint(new Vector3(bounds.center.x+Mathf.Cos(angle)*bounds.extents.x*reach,hang,bounds.center.z+Mathf.Sin(angle)*bounds.extents.z*reach));
                var tip=attach+Vector3.down*length;   // plumb line, so no sag curve to fake
                var o=new GameObject("Lantern string",typeof(LineRenderer));o.transform.SetParent(transform,false);
                var line=o.GetComponent<LineRenderer>();
                line.useWorldSpace=false;line.positionCount=2;line.widthMultiplier=.02f;line.sharedMaterial=cord;
                line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
                line.SetPosition(0,attach);line.SetPosition(1,tip);
                var lantern=GameObject.CreatePrimitive(PrimitiveType.Sphere);lantern.name="Lantern";
                ForestWorld.Remove(lantern.GetComponent<SphereCollider>());   // disables it this frame, then destroys
                lantern.transform.SetParent(o.transform,false);
                lantern.transform.localPosition=tip;lantern.transform.localScale=Vector3.one*.16f;
                var glass=lantern.GetComponent<Renderer>();
                glass.sharedMaterial=gold;glass.shadowCastingMode=ShadowCastingMode.Off;glass.receiveShadows=false;
                // Every third lantern carries a real light: 12 glows, 4 lamps, so the light count does not grow with the string count.
                if(i%3==0)NewLight("Lantern lamp",o.transform,tip,LanternWarm,.9f,4);
            }
        }
    }
}
