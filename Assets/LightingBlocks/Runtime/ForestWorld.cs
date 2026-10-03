using System;
using System.Collections.Generic;
using System.Linq;
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
        int respawnCursor;
        readonly List<IslandForestLayout> pickupLayouts=new List<IslandForestLayout>();
        readonly List<float> pickupAppear=new List<float>();
        [SerializeField,HideInInspector] List<GameObject> sceneryRoots=new List<GameObject>();
        Transform visualRoot;
        LightingDots dots;
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
            var state=new ForestSave{seed=seed,particleVersion=BridgeProfile.Current.isLighting?5:0};
            foreach(var d in configuration.islands)
            {
                var island=new IslandParticleSave{id=d.id};
                // Lighting has no slots (ParticleCount 0): its dots are the seeded LightingDots field.
                if(d.ParticleCount>0){var initial=ForestLayoutGenerator.Generate(d,seed,Landscape.Shores[d.polygonIndex],configuration.trees.Length).particles;for(int i=0;i<d.ParticleCount;i++)island.slots.Add(new ParticleSlotSave{id=d.id+":"+i,color=i%3,position=initial[i],positioned=true});}
                state.islands.Add(island);
            }
            return state;
        }
        public bool Valid(ForestSave state,bool legacy=false)
        {
            if(state==null||state.particleVersion<0||state.particleVersion>5||state.seed<0||(legacy?(state.layoutVersion!=1&&state.layoutVersion!=2):state.layoutVersion!=ForestConfiguration.LayoutVersion)||state.islands==null||state.islands.Count!=configuration.islands.Length)return false;
            if(!legacy && (state.suppressedTrees==null||state.suppressedTrees.Count>1000))return false;
            if(!legacy){var unique=new HashSet<string>();foreach(var suppression in state.suppressedTrees){if(string.IsNullOrEmpty(suppression)||!unique.Add(suppression))return false;int slash=suppression.LastIndexOf('/');if(slash<0||configuration.Find(suppression.Substring(0,slash))==null||!int.TryParse(suppression.Substring(slash+1),out int index)||index<0||index>1000)return false;}}
            // Shared inventory (particleVersion 2+) has no upper bound here: a migrated save may
            // legitimately start above InventoryCapacity (see MigrateToV2) and must keep loading.
            if(state.particleVersion>=2&&state.inventory<0)return false;
            if(state.dots!=null)
            {
                // Lighting's only saved dot state: dots taken in the last LightingRespawnSeconds. A cell need not
                // hold a dot of the saved seed: the field is salted per world build (LightingDots.Seed).
                var taken=new HashSet<Vector2Int>();
                if(state.dots.Count>4096)return false;
                foreach(var dot in state.dots){var cell=new Vector2Int(dot?.x??0,dot?.z??0);if(dot==null||!float.IsFinite(dot.remaining)||dot.remaining<=0||dot.remaining>LightingRespawnSeconds||!taken.Add(cell))return false;}
            }
            var ids=new HashSet<string>();
            foreach(var i in state.islands)
            {
                if(i==null||i.id==null||!ids.Add(i.id))return false;
                if(i.freeDeliveryColor< -1||i.freeDeliveryColor>2||i.freeDeliveryColors!=null&&(i.freeDeliveryColors.Count>4096||i.freeDeliveryColors.Any(c=>c<0||c>2)))return false;
                if(state.particleVersion>=2)
                {
                    if(i.charge==null||i.charge.Length!=3)return false;
                    foreach(var c in i.charge)if(c<0||c>3)return false;
                    if(state.particleVersion==2)
                    {
                        if(i.queue==null||i.queue.Count>300)return false;
                        foreach(var q in i.queue)if(q<-1||q>2)return false;
                    }
                    else
                    {
                        if(i.rewards==null||i.rewards.Count>303)return false;
                        var backed=new int[3];
                        foreach(var q in i.rewards)
                        {
                            if(q==null||q.color<-1||q.color>2)return false;
                            if(q.consumesCharge){if(q.color<0||++backed[q.color]>1||i.charge[q.color]!=3)return false;}
                        }
                        for(int c=0;c<3;c++)if(i.charge[c]==3&&backed[c]!=1)return false;
                    }
                }
                if(state.particleVersion>=4&&(i.queue.Count!=0||i.rewards.Count!=0||i.charge.Any(c=>c!=0)))return false;
                if(state.particleVersion<2&&(i.inventory<0||i.inventory>30||i.treeCharge<0||i.treeCharge>30||(i.hasChargeColor&&(i.chargeColor<0||i.chargeColor>2))))return false;
                var d=configuration.Find(i.id);if(d==null||i.slots==null||i.slots.Count!=d.ParticleCount)return false;
                var slots=new HashSet<string>();
                foreach(var s in i.slots){if(s==null||!slots.Add(s.id)||float.IsNaN(s.remaining)||float.IsInfinity(s.remaining)||s.remaining<0||s.remaining>30||s.color<0||s.color>2)return false;}
                for(int n=0;n<d.ParticleCount;n++)if(!slots.Contains(d.id+":"+n))return false;
                if(!legacy)
                {
                    // Only the slot reachability test below reads the layout. Lighting has no slots, and
                    // generating three layouts cost ~700 ms per load.
                    if(i.slots.Count>0)
                    {
                        var layout=ForestLayoutGenerator.Generate(d,state.seed,Landscape.Shores[d.polygonIndex],configuration.trees.Length);layout.trees.RemoveAll(t=>state.suppressedTrees.Contains(d.id+"/"+t.id));layout.navigation.BuildReachability(d.wisdomPosition+Vector3.right*2);
                        foreach(var slot in i.slots)
                            if(!slot.positioned||slot.generation<0||slot.generation==int.MaxValue||!float.IsFinite(slot.position.x)||!float.IsFinite(slot.position.y)||!float.IsFinite(slot.position.z)||Mathf.Abs(slot.position.y)>.01f||!layout.navigation.InReachableArea(slot.position))return false;
                    }
                }
            }
            return true;
        }
        public ForestSave Migrate(ForestSave old,int version,List<GameManager.BlockSave> blocks,Vector3 player)
        {
            // Lighting's slots became the seeded dot field (particleVersion 5): their positions mean nothing
            // now. Inventory and every tree record stay; GameManager.ImportSave stamps version 5 after its
            // own v3/v4 inventory conversions.
            if(BridgeProfile.Current.isLighting&&old?.islands!=null)foreach(var island in old.islands)island?.slots?.Clear();
            if(old!=null&&old.dots==null)old.dots=new List<DotSave>();
            if(version>=5)
            {
                if(!Valid(old)||BridgeProfile.Current.isLighting&&old.particleVersion<4)return null;
                foreach(var island in old.islands)if(island.freeDeliveryColors==null)island.freeDeliveryColors=new List<int>();
                return old;
            }
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
                            // Unspent v0 inventory remains available for the player to choose a receiver.
                            foreach(var slot in island.slots){int n=int.Parse(slot.id.Substring(slot.id.LastIndexOf(':')+1));slot.color=n%3;slot.position=layout.particles[n];}
                        }
                        old.particleVersion=1;
                    }
                    if(old.particleVersion==1){MigrateToV2(old);old.particleVersion=2;}
                    if(old.particleVersion==2){MigrateToV3(old);old.particleVersion=3;}
                }
                foreach(var island in old.islands)if(island.freeDeliveryColors==null)island.freeDeliveryColors=new List<int>();
                return old;
            }
            if(version>=2&&!Valid(old,true))return null;
            if(version==3){if(old.layoutVersion!=2)return null;foreach(var island in old.islands)foreach(var slot in island.slots)if(!slot.positioned||slot.generation<0||slot.generation==int.MaxValue||!float.IsFinite(slot.position.x)||!float.IsFinite(slot.position.y)||!float.IsFinite(slot.position.z)||Mathf.Abs(slot.position.y)>.01f||Mathf.Abs(slot.position.x)>60||slot.position.z<-70||slot.position.z>120)return null;}
            var next=NewState(version==1?1907:old.seed);
            foreach(var d in configuration.islands){var layout=ForestLayoutGenerator.Generate(d,next.seed,Landscape.Shores[d.polygonIndex],configuration.trees.Length);foreach(var t in layout.trees){bool blocked=(t.position-new Vector3(player.x,0,player.z)).sqrMagnitude<4;foreach(var b in blocks)if(!b.placed&&(t.position-new Vector3(b.position.x,0,b.position.z)).sqrMagnitude<4)blocked=true;if(blocked)next.suppressedTrees.Add(d.id+"/"+t.id);}}
            if(version>=2)foreach(var target in next.islands){var source=old.islands.Find(i=>i.id==target.id);if(BridgeProfile.Current.isLighting)next.inventory+=source.inventory;else target.inventory=source.inventory;foreach(var slot in target.slots){var prior=source.slots.Find(p=>p.id==slot.id);slot.remaining=prior.remaining;if(version==3){slot.position=prior.position+(configuration.Find(target.id).polygonIndex==2?Vector3.forward*Landscape.Expansion:Vector3.zero);slot.generation=prior.generation;slot.pending=prior.pending;}}}
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
                island.charge=new int[3];island.queue=new List<int>();
                int complete=island.treeCharge/3,leftover=island.treeCharge%3;
                for(int n=0;n<complete;n++)island.queue.Add(island.hasChargeColor?island.chargeColor:-1);
                if(leftover>0){if(island.hasChargeColor)island.charge[island.chargeColor]=leftover;else shared+=leftover;}
                island.treeCharge=0;island.hasChargeColor=false;
            }
            old.inventory=shared; // may exceed InventoryCapacity; TryAddInventory keeps it from climbing further until spent down
        }
        void MigrateToV3(ForestSave old)
        {
            foreach(var island in old.islands)
            {
                island.rewards=new List<ParticleRewardSave>();
                foreach(int color in island.queue)island.rewards.Add(new ParticleRewardSave{color=color});
                for(int color=0;color<3;color++)
                {
                    if(island.charge[color]!=3)continue;
                    var reward=island.rewards.FindLast(q=>q.color==color);
                    // Repair a full receiver lacking a queue entry without losing its earned tile.
                    if(reward==null){reward=new ParticleRewardSave{color=color};island.rewards.Add(reward);}
                    reward.consumesCharge=true;
                }
                island.queue.Clear();
            }
        }
        // ---- Lighting resource transfers ---------------------------------------------------------
        // Every source of shared-inventory change - walk-over pickup (TryCollect) and tile-return
        // refunds (GameManager.ReturnStones) - routes through these, so inventory can never be applied twice.
        public const int ParticleUnitsPerParticle=4;
        // Seconds a collected Lighting particle waits before it respawns (legacy keeps 30).
        public const float LightingRespawnSeconds=15f;
        // Kept for compatibility with older callers; Lighting inventory is now uncapped.
        public const int InventoryCapacity=int.MaxValue;
        public int SharedInventory=>State==null?0:State.inventory;
        public string SharedInventoryLabel=>FormatParticleUnits(SharedInventory);
        public static string FormatParticleUnits(int units)
        {
            int whole=units/ParticleUnitsPerParticle,quarter=units%ParticleUnitsPerParticle;
            return quarter==0?whole.ToString():whole+(quarter==1?".25":quarter==2?".5":".75");
        }
        // All-or-nothing integer quarter-particle accounting. The wallet is uncapped.
        public bool TryAddInventory(int amount)
        {
            if(amount<=0||State==null||State.inventory>int.MaxValue-amount)return false;
            State.inventory+=amount;return true;
        }
        public bool TrySpendInventory(int amount)
        {
            if(amount<=0||State==null||State.inventory<amount)return false;
            State.inventory-=amount;return true;
        }
        public enum DepositResult{Success,Full,Empty}
        // Retained as a compatibility surface for stale editor checks. Lighting no longer deposits
        // or charges particles; loose tiles come from the island stock (LightingParticles).
        public DepositResult Deposit(string islandId,int color)=>DepositResult.Empty;
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
                    if(source!=null){if(BridgeProfile.Current.isLighting)state.inventory+=Mathf.Clamp(source.inventory,0,30);else target.inventory=Mathf.Clamp(source.inventory,0,30);}
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
        // Read-only: active/inactive pickup transforms - Lighting's dot pool, else the legacy slot
        // pickups in pickupStates order. Lighting's visibility fade (LightingFog) is the only external reader.
        public IReadOnlyList<Transform> PickupVisuals=>dots!=null?dots.Visuals:pickupVisuals;
        public LightingDots Dots=>dots;
        public uint VisualRevision {get;private set;}
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
            VisualRevision++;
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
                if(particles!=null)particles.AddTree(this,component,root,layout);
                for(int i=0;ParticlesEnabled&&i<owner.slots.Count;i++)
                {
                    var slot=owner.slots[i]; // legacy only: Lighting has no slots (LightingDots below)
                    RelocateIfReserved(owner,slot,layout);
                    var pickup=new GameObject("Particle "+slot.id,typeof(MeshFilter),typeof(MeshRenderer));pickup.GetComponent<MeshFilter>().sharedMesh=configuration.particleMesh;
                    pickup.transform.SetParent(root,false);if(i<layout.particles.Count)layout.particles[i]=slot.position;else layout.particles.Add(slot.position);pickup.transform.position=slot.position+Vector3.up*.7f;pickup.transform.localScale=Vector3.one*.3f;pickup.transform.rotation=Quaternion.Euler(0,45,12);
                    var pickupRenderer=pickup.GetComponent<Renderer>();pickupRenderer.sharedMaterial=configuration.particleMaterial;pickupRenderer.shadowCastingMode=ShadowCastingMode.Off;
                    pickupVisuals.Add(pickup.transform);pickupStates.Add(slot);pickupOwners.Add(owner);pickupLayouts.Add(layout);pickupAppear.Add(1);pickup.SetActive(slot.remaining<=0&&!slot.pending);
                }
                for(int i=0;i<layout.animals.Count;i++)
                {
                    if(i<d.animalCount){int species=ForestLayoutGenerator.Hash(State.seed,d.id+"/species/"+i)%configuration.animals.Length;var animal=Instantiate(configuration.animals[species],layout.animals[i],Quaternion.identity,root);animal.GetComponent<SpectralAnimal>().Initialize(game,new ForestNavigation(Landscape.Shores[d.polygonIndex],layout,true),d,ForestLayoutGenerator.Hash(State.seed,d.id+"/animal/"+i));}
                    else if(configuration.species!=null&&configuration.species.Length>=2){var species=configuration.species[i<d.animalCount+d.rabbitCount?0:1];var animal=Instantiate(species.prefab,layout.animals[i],Quaternion.identity,root);animal.GetComponent<ForestCreature>().Initialize(game,layout,species,ForestLayoutGenerator.Hash(State.seed,d.id+"/creature/"+i));}
                }
            }
            dots=null;
            if(BridgeProfile.Current.isLighting&&enableLightingParticles){dots=GetComponent<LightingDots>();if(dots==null)dots=gameObject.AddComponent<LightingDots>();dots.Build(this,visualRoot);}
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
            point=default;int loose=0;foreach(var b in game.Blocks)if(!b.placed&&(b.id==null||!b.id.StartsWith("stock-",StringComparison.Ordinal)))loose++;if(loose>=35)return false; // the island stock does not count
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
            // Legacy slot pickups only; Lighting collects through LightingDots.TryCollect.
            if(pickupOwners[index].inventory>=30)return false;
            var t=pickupVisuals[index];var delta=game.player.transform.position-t.position;
            if(new Vector2(delta.x,delta.z).sqrMagnitude>1.21f||Mathf.Abs(delta.y)>1.5f||Occupied(t.position,new Vector3(.15f,.12f,.15f),true))return false;
            pickupOwners[index].inventory++;
            pickupStates[index].remaining=30;t.gameObject.SetActive(false);
            ForestSoundscape.Play("collect",t.position,.12f);
            return true;
        }
        public void Tick(float delta,bool collect)
        {
            if(GameHUD.SimulationPaused||delta<=0||State==null)return;
            if(BridgeProfile.Current.isLighting){if(dots!=null)dots.Tick(delta,collect);return;}
            foreach(var owner in State.islands)foreach(var slot in owner.slots){if(slot.remaining>0){slot.remaining=Mathf.Max(0,slot.remaining-delta);if(slot.remaining==0)slot.pending=true;}}
            collectionTimer-=delta;respawnTimer-=delta;fxTime+=delta;
            int respawnBudget=respawnTimer<=0?2:0;
            if(respawnBudget>0&&pickupStates.Count>0)
            {
                int visited=0;
                for(;visited<pickupStates.Count&&respawnBudget>0;visited++)
                {
                    int index=(respawnCursor+visited)%pickupStates.Count;
                    if(!pickupStates[index].pending)continue;
                    Respawn(index);respawnBudget--;
                }
                respawnCursor=(respawnCursor+Mathf.Max(1,visited))%pickupStates.Count;
            }
            for(int i=0;i<pickupVisuals.Count;i++)
            {
                var t=pickupVisuals[i];var slot=pickupStates[i];bool active=slot.remaining<=0&&!slot.pending;t.gameObject.SetActive(active);
                if(!active)continue;pickupAppear[i]=Mathf.Min(1,pickupAppear[i]+delta*2);t.localScale=Vector3.one*(.3f*Mathf.SmoothStep(0,1,pickupAppear[i]));t.position=slot.position+Vector3.up*(.7f+Mathf.Sin(fxTime*.9f+i)*.06f);t.Rotate(0,35*delta,0,Space.World);
                if(collect&&collectionTimer<=0)TryCollect(i);
            }
            if(collectionTimer<=0)collectionTimer=.1f;if(respawnTimer<=0)respawnTimer=.5f;
        }
        public bool Respawn(int index)
        {
            var slot=pickupStates[index];var layout=pickupLayouts[index];
            var candidates=layout.candidates;
            if(!slot.pending||candidates.Count==0)return false;
            int start=ForestLayoutGenerator.Hash(State.seed,slot.id+"/"+(slot.generation+1))%candidates.Count;
            // ponytail: at most 512 tries (the old candidate cap); a network's frontier can hold thousands.
            for(int n=0;n<Mathf.Min(candidates.Count,512);n++){
                var p=candidates[(start+n)%candidates.Count];var delta=game.player.transform.position-p;delta.y=0;
                if(delta.sqrMagnitude<9||(p-slot.position).sqrMagnitude<4||Occupied(p+Vector3.up*.7f,new Vector3(.2f,.15f,.2f),true))continue;
                bool blocked=false;foreach(var b in game.Blocks)if((new Vector3(b.transform.position.x,0,b.transform.position.z)-p).sqrMagnitude<1.44f){blocked=true;break;}
                foreach(var other in pickupOwners[index].slots)if(other!=slot&&other.remaining<=0&&!other.pending&&(other.position-p).sqrMagnitude<6.25f){blocked=true;break;}
                if(blocked)continue;slot.position=p;slot.generation++;slot.pending=false;pickupAppear[index]=0;int local=pickupOwners[index].slots.IndexOf(slot);layout.particles[local]=p;return true;
            }return false;
        }
        // Migration for the receiver reservation itself: a slot's saved position predates receivers
        // moving (or moving farther apart) and now overlaps one's interaction clearance. layout.candidates
        // is already filtered clear of every receiver (ForestLayoutGenerator.RemoveReceiverClearance),
        // so any candidate found here is a valid new spot - only .position changes; id, colour, timer
        // and pending/generation (respawn state) are left exactly as saved.
        void RelocateIfReserved(IslandParticleSave owner,ParticleSlotSave slot,IslandForestLayout layout)
        {
            float clearance=LightingParticles.ReceiverClearance*LightingParticles.ReceiverClearance;
            bool reserved=false;foreach(var r in layout.receivers)if((slot.position-r).sqrMagnitude<clearance){reserved=true;break;}
            if(!reserved||layout.candidates.Count==0)return;
            int start=ForestLayoutGenerator.Hash(State.seed,slot.id+"/receiver-clear")%layout.candidates.Count;
            for(int n=0;n<layout.candidates.Count;n++)
            {
                var p=layout.candidates[(start+n)%layout.candidates.Count];
                bool clear=true;foreach(var other in owner.slots)if(other!=slot&&(other.position-p).sqrMagnitude<2.25f){clear=false;break;}
                if(clear){slot.position=p;return;}
            }
            Debug.LogError("Lighting: "+owner.id+" could not relocate particle "+slot.id+" clear of a receiver - no free candidate.");
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
