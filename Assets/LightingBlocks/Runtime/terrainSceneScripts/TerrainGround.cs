using System.Collections.Generic;
using UnityEngine;

// Sandbox only, and additive only. Nothing under Assets/LightingBlocks/Runtime outside this folder
// is modified: every fix here reaches the shipping code through a public method, a public field or
// a component swap. This activates itself solely in a scene that contains a Terrain, so SampleScene
// and the 207-check suite never see it.
namespace LightingBlocks.TerrainScene
{
    // ForestLayoutGenerator has no notion of height. Every position it emits is y=0 and ForestWorld
    // instantiates straight from those, so on a raised terrain the whole world sits buried. This
    // settles it onto the ground afterwards rather than threading a height through the generator.
    //
    // The frame is the load-bearing part, and it has two halves.
    //
    // HALF ONE - wildlife pathfinds in 3D against waypoints that ForestNavigation emits at y=0:
    //
    //     var next = Vector3.MoveTowards(transform.position, path[waypoint], speed * dt);
    //     transform.position = next;
    //     if ((next - path[waypoint]).sqrMagnitude < .01f) waypoint++;
    //
    // Hold an animal at terrain height and its distance to a waypoint can never fall below that
    // height, so the index never advances and it drives into the same waypoint forever while
    // MoveTowards drags it downhill. That is the "diving into the terrain" bug. So wildlife sees
    // itself at y=0 for its own Simulate, and height is applied only for display in LateUpdate.
    //
    // HALF TWO - the same y=0 data is compared against the PLAYER, who really is up on the terrain:
    //
    //     SpectralAnimal.cs:17,22   distance < 16            3D, flee at 4 m
    //     ForestCreature.cs:31,40   distance < fleeRadius^2  3D, flee at 3.5 m
    //     ForestWorld.cs:149        Abs(delta.y) > 1.5       particle held at y=0.7
    //
    // Every one of those is off by exactly the terrain height under the player, so on a 3.6 m hill
    // the creature flee radius is smaller than the error and never fires at any distance, and
    // collection is refused outright. Simulate and Tick are public and the editor suite already
    // drives them from outside (ForestVerification.cs:128,177,229), so rather than fork three
    // scripts this disables their own Update and calls them here with the player lowered into the
    // same flat frame. The player is restored inside the same function, with no physics step and no
    // other script in between, so nothing else ever observes the shifted position.
    [DefaultExecutionOrder(-1000)]
    public sealed class TerrainGround : MonoBehaviour
    {
        static Terrain[] terrains;

        // SampleHeight, not a downward raycast. TerrainCollider is generated from this same
        // heightmap so the two agree exactly, it costs no physics query per animal per frame, and a
        // raycast would also hit blocks, trees and props and settle things on top of those instead
        // of on the ground.
        public static float Height(Vector3 p)
        {
            if (terrains == null) terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            foreach (var t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                var origin = t.transform.position;
                var size = t.terrainData.size;
                float x = p.x - origin.x, z = p.z - origin.z;
                if (x < 0 || z < 0 || x > size.x || z > size.z) continue;
                return t.SampleHeight(p) + origin.y;
            }
            return 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            if (terrains.Length == 0) return;
            var host = new GameObject("Terrain ground");
            host.AddComponent<TerrainGround>();
            host.AddComponent<TerrainCameraGround>();
            host.AddComponent<TerrainHUD>();
        }

        // Pickups are authored at slot.position + up * .7 and ForestWorld.Tick rewrites that every
        // frame, so the offset is re-added each frame rather than accumulated.
        const float PickupLift = .7f;
        // GameManager.ConfigureBlock pins a loose block to y=.26.
        const float BlockLift = .26f;
        // Clearance for the wisdom tree's rune circle once it is bent onto the ground.
        const float RuneLift = .1f;
        // A loose block is scaled .84 (GameManager.ConfigureBlock), so two of them closer than this
        // in plan overlap each other.
        const float BlockSpan = .9f;

        GameManager game;
        ForestWorld forest;
        Transform built, dressed;
        readonly List<SpectralAnimal> animals = new List<SpectralAnimal>();
        readonly List<ForestCreature> creatures = new List<ForestCreature>();
        readonly List<Transform> pickups = new List<Transform>();
        readonly HashSet<BridgeBlock> seated = new HashSet<BridgeBlock>();

        bool Ready()
        {
            if (game == null)
            {
                game = GameManager.Instance;
                if (game == null) return false;
                forest = ForestWorld.For(game);
            }
            if (forest == null || game.blocksRoot == null) return false;
            var world = game.blocksRoot.parent;
            // Build() makes a fresh "Spectral Forest" root every time, including on Restore, so a
            // changed identity is the one reliable signal that everything tracked here is stale.
            var root = world.Find("Spectral Forest");
            if (root != built) { built = root; Settle(); }
            var deeps = world.Find("Immersive Deeps");
            if (deeps != dressed) { dressed = deeps; Raise(deeps); }
            return built != null;
        }

        void Update()
        {
            if (!Ready() || !game.autoTick) return;
            float delta = Time.deltaTime;
            Flatten();
            var player = game.player.transform;
            var stood = player.position;
            player.position = new Vector3(stood.x, stood.y - Height(stood), stood.z);
            try
            {
                for (int i = animals.Count - 1; i >= 0; i--)
                {
                    if (animals[i] == null) { animals.RemoveAt(i); continue; }
                    animals[i].Simulate(delta);
                }
                for (int i = creatures.Count - 1; i >= 0; i--)
                {
                    if (creatures[i] == null) { creatures.RemoveAt(i); continue; }
                    creatures[i].Simulate(delta);
                }
                forest.Tick(delta, true);
            }
            finally { player.position = stood; }
        }

        void LateUpdate()
        {
            if (forest == null || built == null) return;
            foreach (var a in animals) if (a != null) Place(a.transform, 0);
            foreach (var c in creatures) if (c != null && !c.Airborne) Place(c.transform, 0);
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                if (pickups[i] == null) { pickups.RemoveAt(i); continue; }
                Place(pickups[i], PickupLift);
            }
            Blocks();
        }

        // GameManager.ConfigureBlock slams every loose block to y=.26, so a stone made at the
        // Wisdom Tree or restored from a save lands inside a hill. The body is kinematic
        // (PullableBox.cs:19), so moving it is the same teleport ConfigureBlock itself does.
        // A block with a Holder is being dragged or orbiting: the player owns its position.
        void Blocks()
        {
            foreach (var b in game.Blocks)
            {
                if (b == null || b.placed) continue;
                var box = b.Box;
                if (box == null || box.Holder != null) continue;
                if (seated.Add(b)) Seat(b);
                var p = b.transform.position;
                float y = Height(p) + BlockLift;
                if (Mathf.Abs(p.y - y) < .001f) continue;
                b.transform.position = new Vector3(p.x, y, p.z);
                if (box.Body != null) box.Body.position = b.transform.position;
            }
        }

        // ForestWorld.FindOutput:137 walks the nine output offsets around a Wisdom Tree and takes the
        // first one where Occupied() reports nothing. Those offsets are layout data at y=.26, but
        // every loose block on this terrain lives at Height+.26, metres above that probe, so the
        // probe finds empty air every time and hands the same first offset to every block made.
        // They end up inside one another, and then BoxPlayer.CanReach:152 rejects all of them at
        // once: it raycasts from the player to the block and fails on any collider that is neither
        // the player nor that block, which is exactly what the rest of the stack is. That is both
        // halves of the report - they overlap, and none can be grabbed.
        //
        // Re-seat a block the first time it is seen and never again, so a block the player carries
        // and drops beside another one is left where they put it.
        void Seat(BridgeBlock block)
        {
            if (!Crowded(block.transform.position, block)) return;
            IslandForestDefinition island = null;
            float best = float.MaxValue;
            var here = block.transform.position;
            foreach (var layout in forest.Layouts)
            {
                var d = layout.definition;
                float distance = new Vector2(d.wisdomPosition.x - here.x, d.wisdomPosition.z - here.z).sqrMagnitude;
                if (distance < best) { best = distance; island = d; }
            }
            if (island == null) return;
            foreach (var offset in island.outputOffsets)
            {
                var spot = island.wisdomPosition + offset;
                if (Crowded(spot, block)) continue;
                block.transform.position = new Vector3(spot.x, Height(spot) + BlockLift, spot.z);
                if (block.Box != null && block.Box.Body != null) block.Box.Body.position = block.transform.position;
                return;
            }
            // Every offset taken. Leave it: FindOutput is the thing that should have refused the
            // spawn, and stacking one block is better than throwing it somewhere unreachable.
        }

        // Plan distance only, because the whole point is that the heights disagree.
        bool Crowded(Vector3 p, BridgeBlock self)
        {
            foreach (var b in game.Blocks)
            {
                if (b == null || b == self || b.placed) continue;
                var q = b.transform.position;
                if (new Vector2(q.x - p.x, q.z - p.z).sqrMagnitude < BlockSpan * BlockSpan) return true;
            }
            return false;
        }

        void Flatten()
        {
            foreach (var a in animals)
            {
                if (a == null) continue;
                var p = a.transform.position; a.transform.position = new Vector3(p.x, 0, p.z);
            }
            foreach (var c in creatures)
            {
                // Fly() authors the whole arc from flightStart and flightEnd and never reads the
                // ground, so an airborne bird is left alone in both passes.
                if (c == null || c.Airborne) continue;
                var p = c.transform.position; c.transform.position = new Vector3(p.x, 0, p.z);
            }
        }

        // WisdomTree.BuildRuneCircle:148 builds a flat 6 m disc of rings and ticks at local y=.03.
        // Lifting the tree carries it rigidly, so on relief half the disc sinks into the hill and the
        // other half floats: the player sees three broken purple arcs instead of a circle. The mesh
        // is created per tree at runtime (new Mesh in BuildRuneCircle), so bending its vertices onto
        // the ground touches no shared asset and no shipping script.
        //
        // Runs after WisdomTree.Start because Unity calls Start on everything instantiated last
        // frame before any Update, and this is reached from Update.
        static void Drape(Transform tree)
        {
            var rune = tree.Find("Rune circle");
            if (rune == null) return;
            var filter = rune.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            // .03 was enough against a floor at exactly y=0. The terrain surface is triangles while
            // Height bilinearly interpolates the same heightmap, so between samples the two disagree
            // by a little: this buys clearance for that without reading as floating on a 6 m disc.
            rune.localPosition = new Vector3(0, RuneLift, 0);
            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            float scale = Mathf.Abs(rune.lossyScale.y) < .0001f ? 1 : rune.lossyScale.y;
            float root = Height(rune.position);
            for (int i = 0; i < vertices.Length; i++)
            {
                // Read the world position from the mesh's own frame so a rotated or scaled prefab
                // still lands on the right patch of ground.
                var world = rune.TransformPoint(new Vector3(vertices[i].x, 0, vertices[i].z));
                vertices[i].y = (Height(world) - root) / scale;
            }
            mesh.SetVertices(vertices);
            mesh.RecalculateBounds();
        }

        // Absolute, never additive, so a paused frame where ForestWorld.Tick never ran cannot stack
        // heights on top of each other.
        static void Place(Transform t, float extra)
        {
            var p = t.position;
            t.position = new Vector3(p.x, Height(p) + extra, p.z);
        }

        // WorldDressing.Build authors every prop flat: paving slabs at y=.02, ruin columns at
        // .4*height, the arch root at 0. They are rigid decoration with no gameplay reads, so one
        // additive lift per rebuild puts them on the ground and keeps the authored offset. Additive
        // is safe because WorldDressing.Build always makes a fresh "Immersive Deeps", so a changed
        // root identity is exactly the "this has not been lifted yet" signal.
        //
        // Rim rocks, rim steps, void shafts, lanterns and the background islets go through the same
        // loop and move by zero: the terrain has a flat 6 m band at every shore, and the islets sit
        // outside every terrain where Height returns 0. No name matching needed.
        static void Raise(Transform root)
        {
            if (root == null) return;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var p = child.position;
                child.position = new Vector3(p.x, p.y + Height(p), p.z);
            }
        }

        void Settle()
        {
            animals.Clear(); creatures.Clear(); pickups.Clear(); seated.Clear();
            if (built == null) return;
            // Their own Update calls Simulate in the world frame, where the player is metres above
            // the y=0 data they measure against. Disabling the component only stops that Update:
            // it stays attached, which ForestWorld.Occupied:131 relies on to whitelist animals.
            forest.enabled = false;
            var player = game.player.transform;
            foreach (var layout in forest.Layouts)
            {
                var root = built.Find(layout.definition.id);
                if (root == null) continue;
                for (int c = 0; c < root.childCount; c++)
                {
                    var child = root.GetChild(c);
                    if (child.name.StartsWith("Particle ")) { pickups.Add(child); continue; }
                    var animal = child.GetComponent<SpectralAnimal>();
                    if (animal != null) { animal.enabled = false; animals.Add(animal); continue; }
                    var creature = child.GetComponent<ForestCreature>();
                    if (creature != null) { creature.enabled = false; creatures.Add(creature); continue; }
                    Place(child, 0);
                    Drape(child);
                }
                // Cover is thousands of matrices baked once inside Initialize, with no transforms to
                // move afterwards. Raising the layout it was baked from and baking again is the only
                // way in without forking ForestCoverRenderer, and the shipping renderer additionally
                // culls anything above y=3, so TerrainCover takes over the drawing.
                var cover = root.GetComponent<ForestCoverRenderer>();
                if (cover == null) continue;
                cover.enabled = false;
                for (int i = 0; i < layout.cover.Count; i++)
                {
                    var placement = layout.cover[i];
                    placement.position.y = Height(placement.position);
                    layout.cover[i] = placement;
                }
                var mine = root.GetComponent<TerrainCover>();
                if (mine == null) mine = root.gameObject.AddComponent<TerrainCover>();
                mine.Initialize(forest.configuration, layout, player);
            }
            Surface();
        }

        // GameManager.cs:392 falls back to a hard (0, .025, 3) when a save will not load, and a
        // restored save from a flat session puts the player at y=0 too. Either way they start
        // inside the hill. One nudge per world build, not a per-frame leash that would fight the
        // CharacterController.
        void Surface()
        {
            var t = game.player.transform;
            var p = t.position;
            float ground = Height(p);
            if (p.y >= ground - .05f) return;
            var controller = game.player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            t.position = new Vector3(p.x, ground + .025f, p.z);
            if (controller != null) controller.enabled = true;
        }
    }

    // StrategyCamera pins Focus to y=0 (CenterOnPlayer and Pan both write it), so on relief the
    // player climbs out of frame. Focus has a private setter, so instead of forking the camera this
    // raises the finished rig by the ground under the focus point, which for a fixed pitch and yaw
    // is the same transform. Order 1000 puts this after StrategyCamera.Apply has placed the rig.
    [DefaultExecutionOrder(1000)]
    public sealed class TerrainCameraGround : MonoBehaviour
    {
        StrategyCamera rig;
        float lift, applied;
        void LateUpdate()
        {
            if (rig == null)
            {
                var game = GameManager.Instance;
                if (game == null) return;
                rig = game.cameraRig;
                if (rig == null) return;
            }
            // Matched to the camera's own damping so panning across a slope does not snap.
            lift = Mathf.Lerp(lift, TerrainGround.Height(rig.Focus), 1 - Mathf.Exp(-rig.smoothing * Time.unscaledDeltaTime));
            // Bookkept rather than additive: StrategyCamera.LateUpdate returns early while the HUD
            // blocks gameplay, and a raw += would climb a little further every one of those frames.
            var p = rig.transform.position;
            rig.transform.position = new Vector3(p.x, p.y - applied + lift, p.z);
            applied = lift;
        }
    }
}
