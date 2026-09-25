using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace LightingBlocks
{
    public sealed class ForestWorld : MonoBehaviour
    {
        public ForestConfiguration configuration;
        [Tooltip("Lighting only. Enable before Play to restore colored pickups, receivers, and canopy fireflies. Saved particle state is retained while disabled.")]
        public bool enableLightingParticles = false;
        public bool ParticlesEnabled => !BridgeProfile.Current.isLighting || enableLightingParticles;
        [field: NonSerialized] public ForestSave State {get;private set;}
        public readonly List<IslandForestLayout> Layouts=new List<IslandForestLayout>();
        public readonly List<WisdomTree> WisdomTrees=new List<WisdomTree>();
        readonly List<Transform> pickupVisuals=new List<Transform>();
        readonly List<ParticleSlotSave> pickupStates=new List<ParticleSlotSave>();
        readonly List<IslandParticleSave> pickupOwners=new List<IslandParticleSave>();
        readonly Collider[] hits=new Collider[64];
        GameManager game;
        float collectionTimer,fxTime,respawnTimer;
        readonly List<IslandForestLayout> pickupLayouts=new List<IslandForestLayout>();
        readonly List<float> pickupAppear=new List<float>();
        [SerializeField,HideInInspector] List<GameObject> sceneryRoots=new List<GameObject>();
        Transform visualRoot;
        void Awake()
        {
            BridgeProfile.Select(gameObject.scene.name);
            game=GetComponent<GameManager>();
            // No island offset is applied here any more. It used to be written into a runtime clone
            // of the configuration, which left every path that reads the asset directly (notably
            // ForestCoverRenderer's [ExecuteAlways] regeneration) pairing a shifted polygon with an
            // unshifted wisdom position: reachability was then seeded outside the island, nothing
            // was reachable and particle placement threw. IslandForestDefinition.wisdomPosition now
            // applies the offset on the read, so every caller gets it.
        }
        public void BeginSession(){EnsureState();State=NewState(Environment.TickCount & int.MaxValue);}
        public static ForestWorld For(GameManager g)=>g==null?null:g.GetComponent<ForestWorld>();
        public void EnsureState()
        {
            game=GetComponent<GameManager>();
            if(configuration==null)throw new InvalidOperationException("Forest configuration is not assigned.");
            if(State==null)State=NewState(Environment.TickCount & int.MaxValue);
        }
        public ForestSave NewState(int seed)
        {
            var state=new ForestSave{seed=seed,particleVersion=BridgeProfile.Current.isLighting?2:0};
            foreach(var d in configuration.islands){var layout=ForestLayoutGenerator.Generate(d,seed,Landscape.Shores[d.polygonIndex],configuration.trees.Length);var island=new IslandParticleSave{id=d.id};for(int i=0;i<d.particleCount;i++)island.slots.Add(new ParticleSlotSave{id=d.id+":"+i,color=i%3,position=layout.particles[i],positioned=true});state.islands.Add(island);}
            return state;
        }
        public bool Valid(ForestSave state,bool legacy=false)
        {
            if(state==null||state.particleVersion<0||state.particleVersion>2||state.seed<0||(legacy?(state.layoutVersion!=1&&state.layoutVersion!=2):state.layoutVersion!=ForestConfiguration.LayoutVersion)||state.islands==null||state.islands.Count!=configuration.islands.Length)return false;
            if(!legacy && (state.suppressedTrees==null||state.suppressedTrees.Count>1000))return false;
            if(!legacy){var unique=new HashSet<string>();foreach(var suppression in state.suppressedTrees){if(string.IsNullOrEmpty(suppression)||!unique.Add(suppression))return false;int slash=suppression.LastIndexOf('/');if(slash<0||configuration.Find(suppression.Substring(0,slash))==null||!int.TryParse(suppression.Substring(slash+1),out int index)||index<0||index>1000)return false;}}
            // Shared inventory (particleVersion 2+) has no upper bound here: a migrated save may
            // legitimately start above InventoryCapacity (see MigrateToV2) and must keep loading.
            if(state.particleVersion>=2&&state.inventory<0)return false;
            var ids=new HashSet<string>();
            foreach(var i in state.islands)
            {
                if(i==null||i.id==null||!ids.Add(i.id))return false;
                if(state.particleVersion>=2)
                {
                    if(i.charge==null||i.charge.Length!=3)return false;
                    foreach(var c in i.charge)if(c<0||c>3)return false;
                    if(i.queue==null||i.queue.Count>300)return false;
                    foreach(var q in i.queue)if(q<-1||q>2)return false;
                }
                else if(i.inventory<0||i.inventory>30||i.treeCharge<0||i.treeCharge>30||(i.hasChargeColor&&(i.chargeColor<0||i.chargeColor>2)))return false;
                var d=configuration.Find(i.id);if(d==null||i.slots==null||i.slots.Count!=d.particleCount)return false;
                var slots=new HashSet<string>();
                foreach(var s in i.slots){if(s==null||!slots.Add(s.id)||float.IsNaN(s.remaining)||float.IsInfinity(s.remaining)||s.remaining<0||s.remaining>30||s.color<0||s.color>2)return false;}
                for(int n=0;n<d.particleCount;n++)if(!slots.Contains(d.id+":"+n))return false;
                if(!legacy){var layout=ForestLayoutGenerator.Generate(d,state.seed,Landscape.Shores[d.polygonIndex],configuration.trees.Length);layout.trees.RemoveAll(t=>state.suppressedTrees.Contains(d.id+"/"+t.id));layout.navigation.BuildReachability(d.wisdomPosition+Vector3.right*2);foreach(var slot in i.slots)if(!slot.positioned||slot.generation<0||slot.generation==int.MaxValue||!float.IsFinite(slot.position.x)||!float.IsFinite(slot.position.y)||!float.IsFinite(slot.position.z)||Mathf.Abs(slot.position.y)>.01f||!layout.navigation.InReachableArea(slot.position))return false;}
            }
            return true;
        }
        public ForestSave Migrate(ForestSave old,int version,List<GameManager.BlockSave> blocks,Vector3 player)
        {
            if(version==4)
            {
                if(!Valid(old))return null;
                if(BridgeProfile.Current.isLighting)
                {
                    if(old.particleVersion==0)
                    {
                        foreach(var island in old.islands)
                        {
                            var d=configuration.Find(island.id);
                            var layout=ForestLayoutGenerator.Generate(d,old.seed,Landscape.Shores[d.polygonIndex],configuration.trees.Length);
                            island.treeCharge=island.inventory;island.inventory=0;
                            foreach(var slot in island.slots){int n=int.Parse(slot.id.Substring(slot.id.LastIndexOf(':')+1));slot.color=n%3;slot.position=layout.particles[n];}
                        }
                        old.particleVersion=1;
                    }
                    if(old.particleVersion==1){MigrateToV2(old);old.particleVersion=2;}
                }
                return old;
            }
            if(version>=2&&!Valid(old,true))return null;
            if(version==3){if(old.layoutVersion!=2)return null;foreach(var island in old.islands)foreach(var slot in island.slots)if(!slot.positioned||slot.generation<0||slot.generation==int.MaxValue||!float.IsFinite(slot.position.x)||!float.IsFinite(slot.position.y)||!float.IsFinite(slot.position.z)||Mathf.Abs(slot.position.y)>.01f||Mathf.Abs(slot.position.x)>60||slot.position.z<-70||slot.position.z>120)return null;}
            var next=NewState(version==1?1907:old.seed);
            foreach(var d in configuration.islands){var layout=ForestLayoutGenerator.Generate(d,next.seed,Landscape.Shores[d.polygonIndex],configuration.trees.Length);foreach(var t in layout.trees){bool blocked=(t.position-new Vector3(player.x,0,player.z)).sqrMagnitude<4;foreach(var b in blocks)if(!b.placed&&(t.position-new Vector3(b.position.x,0,b.position.z)).sqrMagnitude<4)blocked=true;if(blocked)next.suppressedTrees.Add(d.id+"/"+t.id);}}
            if(version>=2)foreach(var target in next.islands){var source=old.islands.Find(i=>i.id==target.id);target.inventory=source.inventory;foreach(var slot in target.slots){var prior=source.slots.Find(p=>p.id==slot.id);slot.remaining=prior.remaining;if(version==3){slot.position=prior.position+(configuration.Find(target.id).polygonIndex==2?Vector3.forward*Landscape.Expansion:Vector3.zero);slot.generation=prior.generation;slot.pending=prior.pending;}}}
            if(version==3)foreach(var d in configuration.islands){var layout=ForestLayoutGenerator.Generate(d,next.seed,Landscape.Shores[d.polygonIndex],configuration.trees.Length);var island=next.islands.Find(i=>i.id==d.id);foreach(var tree in layout.trees)foreach(var slot in island.slots)if((tree.position-slot.position).sqrMagnitude<2.25f){string id=d.id+"/"+tree.id;if(!next.suppressedTrees.Contains(id))next.suppressedTrees.Add(id);break;}layout.trees.RemoveAll(t=>next.suppressedTrees.Contains(d.id+"/"+t.id));layout.navigation.BuildReachability(d.wisdomPosition+Vector3.right*2);foreach(var slot in island.slots)if(!layout.navigation.InReachableArea(slot.position)){int index=island.slots.IndexOf(slot);slot.position=layout.particles[index];}}
            return next;
        }
        // One-time particleVersion 1 -> 2 upgrade: each island's single bank (inventory/treeCharge/
        // chargeColor/hasChargeColor) becomes the shared inventory plus one 0..3 charge per receiver
        // colour, with every complete (multiple-of-3) bank preserved as an ordered queue entry - the
        // stored colour when the bank had one, -1 (uncoloured, resolved by TrySpawnAtWisdomTree's own
        // weighted draw - see WisdomTree.Tick) otherwise. Leftover partial charge follows the same
        // rule: assigned to its recorded colour, or folded into the shared inventory when uncoloured.
        void MigrateToV2(ForestSave old)
        {
            int shared=0;
            foreach(var island in old.islands)
            {
                shared+=Mathf.Max(0,island.inventory);island.inventory=0;
                int complete=island.treeCharge/3,leftover=island.treeCharge%3;
                for(int n=0;n<complete;n++)island.queue.Add(island.hasChargeColor?island.chargeColor:-1);
                if(leftover>0){if(island.hasChargeColor)island.charge[island.chargeColor]=leftover;else shared+=leftover;}
                island.treeCharge=0;island.hasChargeColor=false;
            }
            old.inventory=shared; // may exceed InventoryCapacity; TryAddInventory keeps it from climbing further until spent down
        }
        // ---- Lighting resource transfers ---------------------------------------------------------
        // Every source of shared-inventory or receiver-charge change - walk-over pickup (TryCollect),
        // tile-return refunds (GameManager.ReturnStones) and E-key deposits (Deposit, via
        // LightingParticles.TryDeposit) - routes through these, so inventory/charge/rewards can never
        // be applied twice.
        public const int InventoryCapacity=30;
        public int SharedInventory=>State.inventory;
        // All-or-nothing: a pickup or a tile refund that cannot fully fit is left uncollected/undestroyed.
        public bool TryAddInventory(int amount)
        {
            if(amount<=0||State.inventory+amount>InventoryCapacity)return false;
            State.inventory+=amount;return true;
        }
        public bool TrySpendInventory(int amount)
        {
            if(amount<=0||State.inventory<amount)return false;
            State.inventory-=amount;return true;
        }
        public enum DepositResult{Success,Full,Empty}
        // Spends one shared particle into island's colour receiver, queueing a reward at 3/3.
        // Charge is only ever reduced once the tile it earned is actually created - see WisdomTree.Tick.
        public DepositResult Deposit(string islandId,int color)
        {
            var island=Inventory(islandId);
            if(island==null||color<0||color>2)return DepositResult.Empty;
            if(island.charge[color]>=3)return DepositResult.Full;
            if(!TrySpendInventory(1))return DepositResult.Empty;
            island.charge[color]++;
            if(island.charge[color]==3)island.queue.Add(color);
            return DepositResult.Success;
        }
        // One-time import only: the old save's positions are all invalid once the profile has
        // shifted the islands (particle slots are validated against a regenerated layout, and every
        // island 2/3 slot would now sit out of bounds), so this rebuilds the forest entirely in the
        // current geometry and carries over only the collected-particle counts.
        public ForestSave Adopt(ForestSave old)
        {
            var state=NewState(old!=null&&old.seed>0?old.seed:Environment.TickCount & int.MaxValue);
            if(old!=null&&old.islands!=null)
                foreach(var target in state.islands)
                {
                    var source=old.islands.Find(i=>i.id==target.id);
                    if(source!=null)target.inventory=Mathf.Clamp(source.inventory,0,30);
                }
            return state;
        }
        public void Restore(ForestSave state)
        {
            State=state; // validated by GameManager before any game state is mutated
            foreach(var i in State.islands)i.slots.Sort((a,b)=>int.Parse(a.id.Substring(a.id.LastIndexOf(':')+1)).CompareTo(int.Parse(b.id.Substring(b.id.LastIndexOf(':')+1))));
            if(visualRoot!=null)Build();
        }
        public IslandParticleSave Inventory(string id){foreach(var i in State.islands)if(i.id==id)return i;return null;}
        // Read-only: active/inactive pickup transforms, in the same order as pickupStates. Lighting's
        // dark-floor glow (LightingGlassLights) is the only external reader.
        public IReadOnlyList<Transform> PickupVisuals=>pickupVisuals;
        public IslandForestDefinition IslandAt(Vector3 p)
        {
            // Lighting: hub ownership comes from whichever PlatformSupport the point sits over, by
            // hubId, not the island polygon - see PlatformSupport.cs.
            if(BridgeProfile.Current.platformGround){var hubId=PlatformSupport.At(p)?.hubId;return hubId==null?null:configuration.Find(hubId);}
            foreach(var d in configuration.islands)if(Landscape.Inside(new Vector2(p.x,p.z),Landscape.Shores[d.polygonIndex]))return d;return null;
        }
        public WisdomTree Nearby(Vector3 p)
        {foreach(var t in WisdomTrees)if(t!=null&&(new Vector2(p.x-t.transform.position.x,p.z-t.transform.position.z)).sqrMagnitude<=9)return t;return null;}
        public string InventoryLabel
        {get{if(game==null)game=GetComponent<GameManager>();var d=IslandAt(game.player.transform.position);return d==null?"Particles stay with their island":"Fundamental particles: "+(Inventory(d.id)?.inventory??0)+" / 30";}}
        public void Build()
        {
            EnsureState();
            if(BridgeProfile.Current.isLighting)GetComponent<LightingParticles>()?.ResetWorld();
            if(visualRoot!=null)Remove(visualRoot.gameObject);
            var old=game.blocksRoot.parent.Find("Spectral Forest");if(old!=null)Remove(old.gameObject);
            visualRoot=new GameObject("Spectral Forest").transform;visualRoot.SetParent(game.blocksRoot.parent,false);
            sceneryRoots.Clear();
            Layouts.Clear();WisdomTrees.Clear();pickupVisuals.Clear();pickupStates.Clear();pickupOwners.Clear();pickupLayouts.Clear();pickupAppear.Clear();
            foreach(var d in configuration.islands)
            {
                var layout=ForestLayoutGenerator.Generate(d,State.seed,Landscape.Shores[d.polygonIndex],configuration.trees.Length);layout.trees.RemoveAll(t=>State.suppressedTrees.Contains(d.id+"/"+t.id));layout.navigation.BuildReachability(d.wisdomPosition+Vector3.right*2);Layouts.Add(layout);
                var root=new GameObject(d.id).transform;root.SetParent(visualRoot,false);
                // Lighting: skip tree/undergrowth generation outright - no GameObjects are ever
                // created, not merely hidden after the fact (EnvironmentVisibility.SetForestVisible
                // stays a legacy-only toggle over what this loop built).
                if(!BridgeProfile.Current.skipScenery)
                {
                    foreach(var p in layout.trees)
                    {
                        var t=Instantiate(configuration.trees[p.variant],p.position,Quaternion.Euler(0,p.yaw,0),root);t.transform.localScale*=p.scale;
                        var trunk=t.AddComponent<CapsuleCollider>();trunk.radius=.32f;trunk.height=1.8f;trunk.center=new Vector3(0,.9f,0);
                        sceneryRoots.Add(t);
                    }
                    if(configuration.coverMeshes!=null&&configuration.coverMeshes.Length>0){var cover=root.gameObject.AddComponent<ForestCoverRenderer>();cover.Initialize(configuration,layout,game.player.transform);}
                }
                var wisdom=Instantiate(configuration.wisdomPrefab,d.wisdomPosition,Quaternion.identity,root);wisdom.name="Wisdom Tree - "+d.id;
                var component=wisdom.GetComponent<WisdomTree>();component.Initialize(this,d);WisdomTrees.Add(component);
                var owner=Inventory(d.id);
                var particles=BridgeProfile.Current.isLighting&&enableLightingParticles?(GetComponent<LightingParticles>()??gameObject.AddComponent<LightingParticles>()):null;
                if(particles!=null)particles.AddTree(this,component,root);
                for(int i=0;ParticlesEnabled&&i<layout.particles.Count;i++)
                {
                    var pickup=new GameObject("Particle "+owner.slots[i].id,typeof(MeshFilter),typeof(MeshRenderer));pickup.GetComponent<MeshFilter>().sharedMesh=configuration.particleMesh;
                    pickup.transform.SetParent(root,false);layout.particles[i]=owner.slots[i].position;pickup.transform.position=owner.slots[i].position+Vector3.up*.7f;pickup.transform.localScale=Vector3.one*.3f;pickup.transform.rotation=Quaternion.Euler(0,45,12);
                    var pickupRenderer=pickup.GetComponent<Renderer>();pickupRenderer.sharedMaterial=configuration.particleMaterial;pickupRenderer.shadowCastingMode=ShadowCastingMode.Off;
                    if(particles!=null)
                    {
                        // Every fundamental particle is the same warm yellow now; colour no longer
                        // carries any meaning (receivers are matched by proximity, not by colour).
                        var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",LightingParticles.ParticleColor);block.SetColor("_EmissionColor",LightingParticles.ParticleColor*1.6f);pickupRenderer.SetPropertyBlock(block);
                    }
                    pickupVisuals.Add(pickup.transform);pickupStates.Add(owner.slots[i]);pickupOwners.Add(owner);pickupLayouts.Add(layout);pickupAppear.Add(1);pickup.SetActive(owner.slots[i].remaining<=0&&!owner.slots[i].pending);
                }
                for(int i=0;i<layout.animals.Count;i++)
                {
                    if(i<d.animalCount){int species=ForestLayoutGenerator.Hash(State.seed,d.id+"/species/"+i)%configuration.animals.Length;var animal=Instantiate(configuration.animals[species],layout.animals[i],Quaternion.identity,root);animal.GetComponent<SpectralAnimal>().Initialize(game,new ForestNavigation(Landscape.Shores[d.polygonIndex],layout,true),d,ForestLayoutGenerator.Hash(State.seed,d.id+"/animal/"+i));}
                    else if(configuration.species!=null&&configuration.species.Length>=2){var species=configuration.species[i<d.animalCount+d.rabbitCount?0:1];var animal=Instantiate(species.prefab,layout.animals[i],Quaternion.identity,root);animal.GetComponent<ForestCreature>().Initialize(game,layout,species,ForestLayoutGenerator.Hash(State.seed,d.id+"/creature/"+i));}
                }
            }
            if(!BridgeProfile.Current.skipScenery)
            {
                var random=new System.Random(ForestLayoutGenerator.Hash(State.seed,"valley"));int count=0;
                var bg=new GameObject("Background Forest").transform;bg.SetParent(visualRoot,false);
                sceneryRoots.Add(bg.gameObject);
                for(int a=0;a<4000&&count<120;a++)
                {
                    // Centre and reach follow the islands. Landscape.Span is 217 in the shipped layout,
                    // so this stays the old 40 + 134.5 ring around z=50.5, where the literals sat.
                    float angle=(float)random.NextDouble()*Mathf.PI*2,radius=40+(float)random.NextDouble()*(Landscape.Span*.62f);
                    float x=Mathf.Cos(angle)*radius,z=Landscape.CentreZ+Mathf.Sin(angle)*radius;
                    if(WorldDressing.ShoreDistance(x,z)<12||!WorldDressing.BackgroundHeight(x,z,out float y))continue;
                    var t=Instantiate(configuration.trees[random.Next(configuration.trees.Length)],new Vector3(x,y,z),Quaternion.Euler(0,random.Next(360),0),bg);t.transform.localScale*=1+(float)random.NextDouble();count++;
                }
            }
            if(!BridgeProfile.Current.isLighting){var ambience=visualRoot.gameObject.AddComponent<ForestAmbience>();ambience.Initialize(configuration,this,game);}
            if(game.GetComponent<ForestSoundscape>()==null)game.gameObject.AddComponent<ForestSoundscape>();
            if(Application.isPlaying)SetSceneryVisible(EnvironmentVisibility.ForestVisible);
            Physics.SyncTransforms();
        }
        public void SetSceneryVisible(bool visible)
        {
            // Only decorative trees: Wisdom Trees, pickups, creatures and the terrain stay active.
            // Hide tree colliders with their meshes so invisible trunks cannot trap the player.
            foreach(var root in sceneryRoots)if(root!=null)root.SetActive(visible);
            Physics.SyncTransforms();
        }
        public static void Remove(UnityEngine.Object obj)
        {if(obj==null)return;if(obj is GameObject go)go.SetActive(false);if(obj is Collider collider)collider.enabled=false;if(Application.isPlaying)Destroy(obj);else DestroyImmediate(obj);}
        public bool Occupied(Vector3 p,Vector3 half,bool ignorePlayer=false)
        {
            int count=Physics.OverlapBoxNonAlloc(p,half,hits,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
            if(count==hits.Length)return true;
            for(int i=0;i<count;i++)
            {var c=hits[i];if(!c.enabled||!c.gameObject.activeInHierarchy)continue;if(c.name.StartsWith("ShoreFloor"))continue;if(c.GetComponent<PlatformSupport>()!=null)continue;if(ignorePlayer&&c.GetComponentInParent<BoxPlayer>()!=null)continue;if(c.GetComponentInParent<SpectralAnimal>()!=null)continue;return true;}
            return false;
        }
        public bool FindOutput(IslandForestDefinition d,out Vector3 point)
        {
            point=default;int loose=0;foreach(var b in game.Blocks)if(!b.placed)loose++;if(loose>=35)return false;
            foreach(var offset in d.outputOffsets)
            {
                var p=d.wisdomPosition+offset;bool valid=true;
                // Lighting: an output slot must sit on supported platform ground, corner to corner,
                // same as the legacy island-polygon inset test it replaces.
                foreach(float x in new[]{-.45f,.45f})foreach(float z in new[]{-.45f,.45f})
                {
                    var corner=p+new Vector3(x,0,z);
                    bool grounded=BridgeProfile.Current.platformGround?PlatformSupport.Supports(corner):ForestLayoutGenerator.Inside(corner,Landscape.Shores[d.polygonIndex],.05f);
                    if(!grounded)valid=false;
                }
                if(valid&&!Occupied(p,new Vector3(.46f,.24f,.46f))){point=p;return true;}
            }
            return false;
        }
        public bool TryCollect(int index)
        {
            if(index<0||index>=pickupStates.Count||GameHUD.GameplayBlocked||pickupStates[index].remaining>0||pickupStates[index].pending)return false;
            // Focus loss is gated at the call site (Update(), below), same as BoxPlayer's own
            // Application.isFocused check - not here, so this stays directly callable from checks.
            if(BridgeProfile.Current.isLighting){if(State.inventory>=InventoryCapacity)return false;}
            else if(pickupOwners[index].inventory>=30)return false;
            var t=pickupVisuals[index];var delta=game.player.transform.position-t.position;
            if(new Vector2(delta.x,delta.z).sqrMagnitude>1.21f||Mathf.Abs(delta.y)>1.5f||Occupied(t.position,new Vector3(.15f,.12f,.15f),true))return false;
            // Never consume the world pickup if the shared inventory cannot actually take it.
            if(BridgeProfile.Current.isLighting){if(!TryAddInventory(1))return false;}else pickupOwners[index].inventory++;
            pickupStates[index].remaining=30;ForestSoundscape.Play("collect",t.position,.12f);t.gameObject.SetActive(false);return true;
        }
        public void Tick(float delta,bool collect)
        {
            if(GameHUD.SimulationPaused||delta<=0||State==null)return;
            foreach(var owner in State.islands)foreach(var slot in owner.slots){if(slot.remaining>0){slot.remaining=Mathf.Max(0,slot.remaining-delta);if(slot.remaining==0)slot.pending=true;}}
            collectionTimer-=delta;respawnTimer-=delta;fxTime+=delta;
            for(int i=0;i<pickupVisuals.Count;i++)
            {
                var t=pickupVisuals[i];var slot=pickupStates[i];if(slot.pending&&respawnTimer<=0)Respawn(i);bool active=slot.remaining<=0&&!slot.pending;t.gameObject.SetActive(active);
                if(!active)continue;pickupAppear[i]=Mathf.Min(1,pickupAppear[i]+delta*2);t.localScale=Vector3.one*(.3f*Mathf.SmoothStep(0,1,pickupAppear[i]));t.position=slot.position+Vector3.up*(.7f+Mathf.Sin(fxTime*.9f+i)*.06f);t.Rotate(0,35*delta,0,Space.World);
                if(collect&&collectionTimer<=0)TryCollect(i);
            }
            if(collectionTimer<=0)collectionTimer=.1f;if(respawnTimer<=0)respawnTimer=.5f;
        }
        public bool Respawn(int index)
        {
            var slot=pickupStates[index];var layout=pickupLayouts[index];if(!slot.pending||layout.candidates.Count==0)return false;
            int start=ForestLayoutGenerator.Hash(State.seed,slot.id+"/"+(slot.generation+1))%layout.candidates.Count;
            for(int n=0;n<layout.candidates.Count;n++){
                var p=layout.candidates[(start+n)%layout.candidates.Count];var delta=game.player.transform.position-p;delta.y=0;
                if(delta.sqrMagnitude<9||(p-slot.position).sqrMagnitude<4||Occupied(p+Vector3.up*.7f,new Vector3(.2f,.15f,.2f),true))continue;
                bool blocked=false;foreach(var b in game.Blocks)if((new Vector3(b.transform.position.x,0,b.transform.position.z)-p).sqrMagnitude<1.44f){blocked=true;break;}
                foreach(var other in pickupOwners[index].slots)if(other!=slot&&other.remaining<=0&&!other.pending&&(other.position-p).sqrMagnitude<(BridgeProfile.Current.isLighting?16f:6.25f)){blocked=true;break;}
                if(blocked)continue;slot.position=p;slot.generation++;slot.pending=false;pickupAppear[index]=0;int local=pickupOwners[index].slots.IndexOf(slot);layout.particles[local]=p;return true;
            }return false;
        }
        void Update()
        {
            if(game==null||!game.autoTick)return;
            long before=ForestPerformanceProbe.Measuring?GC.GetAllocatedBytesForCurrentThread():0;
            // Lighting: losing focus stops walk-over collection (same convention as BoxPlayer's own
            // Application.isFocused check), but the rest of the world - respawn timers included -
            // keeps simulating. Legacy is unaffected: it never gated collection on focus.
            bool collect=!BridgeProfile.Current.isLighting||Application.isFocused;
            Tick(Time.deltaTime,collect);
            if(ForestPerformanceProbe.Measuring)ForestPerformanceProbe.ForestBytes+=GC.GetAllocatedBytesForCurrentThread()-before;
        }
    }
}
