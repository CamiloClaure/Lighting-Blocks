using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Linq;

namespace LightingBlocks
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class BoxPlayer : MonoBehaviour
    {
        public Transform visual;
        public Animator animator;
        public Camera viewCamera;
        public float moveSpeed = 6f;
        public float pullSpeed = 3.2f;
        [Tooltip("Distance from the player's center to the closest box surface.")]
        public float grabReach = 1.25f;
        public bool readInput = true;
        public PullableBox HeldBox { get; private set; }
        public readonly List<PullableBox> HeldBoxes = new List<PullableBox>();
        public PullableBox NearbyBox { get; private set; }
        public float MovementSpeed { get; private set; }
        public bool Orbiting { get; private set; }
        // Raised at the moment a stride completes, with the foot position. Anything that wants to
        // react to walking hangs off this rather than re-deriving the cadence from MovementSpeed.
        public event System.Action<Vector3> Stepped;
        readonly Dictionary<PullableBox, Vector3> pickupPositions = new Dictionary<PullableBox, Vector3>();
        float orbitAngle;
        CharacterController controller;
        float fallingSpeed;
        float nextEdgeNotice;
        AudioSource sfx;
        float strideDistance;
        float airborneTime;
        float dashTimeRemaining, dashCooldownRemaining;
        Vector3 dashDirection;
        const float StrideLength = 1.7f;
        public const float DashCooldown = .3f;
        public const float DashSpeed = 40f;
        // Tiles covered per Dash level; a dash that runs off the edge lays these as bridge tiles.
        public static readonly int[] DashTiles = { 3, 5, 7, 9, 12 };
        public static float DashDistance(int level) => DashTiles[Mathf.Clamp(level, 1, DashTiles.Length) - 1] * LightingBridgeGrid.CellSize;
        public static float DashDuration(int level) => DashDistance(level) / DashSpeed;
        const float DashSupportStep = .2f;
        public bool IsDashing => dashTimeRemaining > 0;
        // Why the last dash ended before its full length, null when it did not: for the checks and
        // for bug reports.
        public string DashStop { get; private set; }
        // The last side contact (not floor) of a controller move, named in DashStop.
        Collider sideHit; Vector3 sideNormal;
        void OnControllerColliderHit(ControllerColliderHit hit) { if (hit.normal.y < .7f) { sideHit = hit.collider; sideNormal = hit.normal; } }
        static string PathOf(Transform t) => t == null ? "?" : (t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name) + (t.GetComponent<BridgeBlock>() is BridgeBlock b ? " [" + b.id + (b.placed ? "" : " loose") + "]" : "");
        public float DashCooldownRemaining => dashCooldownRemaining;
        int animationState;
        static readonly int Idle = Animator.StringToHash("Base Layer.Idle");
        static readonly int Run = Animator.StringToHash("Base Layer.Run");
        static readonly int Pull = Animator.StringToHash("Base Layer.Pull");

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            sfx = gameObject.AddComponent<AudioSource>();
            ForestSoundscape.Route(sfx,"Footsteps"); sfx.spatialBlend = .8f; sfx.playOnAwake = false; sfx.volume = GameAudio.SfxVolume;
        }

        void Start()
        {
            BuildWizardHat();
            if(BridgeProfile.Current.isLighting)
            {
                // ponytail: size and strength tuned by eye.
                Shadow=GroundShadow.Attach(transform,1.1f,.55f,true);
                if(visual!=null) visual.localPosition=Vector3.down*(controller.skinWidth+SoleLift);
            }
        }
        // The rig's idle soles stand this far over its origin (measured: toe bones .21 m up, soles 1.3 cm
        // under them), so the character hovered over the floor. ponytail: one offset for every clip;
        // the Run plant reaches 7 cm lower than idle, so a running foot dips about 6 cm into the floor.
        const float SoleLift = .19f;
        public GroundShadow Shadow { get; private set; }

        // ponytail: material fallback is a global scan, only hit when the leather hat child is missing from the scene entirely.
        void BuildWizardHat()
        {
            if(animator==null)return;
            var head=animator.GetBoneTransform(HumanBodyBones.Head);
            if(head==null)return;
            Material material=null;
            for(int i=0;i<head.childCount;i++)
            {
                var child=head.GetChild(i);
                if(!child.name.StartsWith("Traveler leather hat"))continue;
                var renderer=child.GetComponent<Renderer>();
                if(renderer!=null)material=renderer.sharedMaterial;
                child.gameObject.SetActive(false);
            }
            if(material==null)foreach(var m in Resources.FindObjectsOfTypeAll<Material>())if(m.name=="M_CHR_Leather"){material=m;break;}
            var hat=new GameObject("Traveler wizard hat",typeof(MeshFilter),typeof(MeshRenderer));
            hat.transform.SetParent(head,false);
            // The rig's bones do not run at world scale, so a mesh authored in metres inherits the
            // bone's scale and grows to cover the island. Normalise against it and put the offset
            // in the same world units, so the hat is .84 m across whatever the rig does.
            var scale=head.lossyScale;var inverse=new Vector3(scale.x==0?1:1/scale.x,scale.y==0?1:1/scale.y,scale.z==0?1:1/scale.z);
            hat.transform.localScale=inverse;
            hat.transform.localPosition=Vector3.up*.12f*inverse.y;hat.transform.localRotation=Quaternion.identity;
            hat.GetComponent<MeshFilter>().sharedMesh=WizardHatMesh();
            hat.GetComponent<Renderer>().sharedMaterial=material;
        }

        static Mesh WizardHatMesh()
        {
            int sides=16;float coneRadius=.30f,coneHeight=.62f,bendRad=25f*Mathf.Deg2Rad,brimRadius=.42f,half=.015f;
            var vertices=new List<Vector3>();var triangles=new List<int>();
            int baseStart=vertices.Count;
            for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;vertices.Add(new Vector3(Mathf.Cos(a)*coneRadius,0,Mathf.Sin(a)*coneRadius));}
            int apex=vertices.Count;vertices.Add(new Vector3(0,coneHeight*Mathf.Cos(bendRad),-coneHeight*Mathf.Sin(bendRad)));
            for(int i=0;i<sides;i++){int a=baseStart+i,b=baseStart+(i+1)%sides;triangles.AddRange(new[]{apex,a,b,apex,b,a});}
            int topCenter=vertices.Count;vertices.Add(new Vector3(0,half,0));
            int topStart=vertices.Count;
            for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;vertices.Add(new Vector3(Mathf.Cos(a)*brimRadius,half,Mathf.Sin(a)*brimRadius));}
            int bottomCenter=vertices.Count;vertices.Add(new Vector3(0,-half,0));
            int bottomStart=vertices.Count;
            for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;vertices.Add(new Vector3(Mathf.Cos(a)*brimRadius,-half,Mathf.Sin(a)*brimRadius));}
            for(int i=0;i<sides;i++)
            {
                int t0=topStart+i,t1=topStart+(i+1)%sides,b0=bottomStart+i,b1=bottomStart+(i+1)%sides;
                triangles.AddRange(new[]{topCenter,t1,t0,topCenter,t0,t1});
                triangles.AddRange(new[]{bottomCenter,b0,b1,bottomCenter,b1,b0});
                triangles.AddRange(new[]{t0,t1,b0,t1,b1,b0,b0,b1,t0,b1,t1,t0});
            }
            var mesh=new Mesh{name="Traveler wizard hat"};
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            return mesh;
        }

        void Update()
        {
            if (!readInput || GameHUD.GameplayBlocked) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (!Application.isFocused) { Release(); CancelDash(); return; }
            UpdateNearby();
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame) {
                if (Orbiting) DropOrbit(true); else if(HeldBox!=null) Release();
                else if(GameManager.Instance!=null) GameManager.Instance.Notify("NO STONES TO DROP\nClick near a loose stone. Soul Links unlocks floating carry.",false);
            }
            else if (Orbiting) { /* Floating carry persists until Q, a menu or focus loss. */ }
            else if (BridgeProfile.Current.spaceGrab)
            {
                // Space replaces the left mouse button with the same hold-to-carry semantics. The
                // cursor is locked to screen centre under this profile, so PointerOverUI cannot be
                // hit by a key press and the check the mouse path needs is skipped here.
                if (keyboard == null || !keyboard.spaceKey.isPressed) Release();
                else if (keyboard.spaceKey.wasPressedThisFrame) TryGrab(NearbyBox);
            }
            else if (mouse == null || !mouse.leftButton.isPressed) Release();
            else if (mouse.leftButton.wasPressedThisFrame && !GameHUD.PointerOverUI()) TryGrab(NearbyBox);
            Vector2 input = keyboard == null ? Vector2.zero : new Vector2(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            if (BridgeProfile.Current.isLighting && keyboard != null && (keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame))
                TryStartDash(input);
            StepMovement(input, Time.deltaTime);
        }

        public bool TryStartDash(Vector2 input)
        {
            var game = GameManager.Instance;
            if (!BridgeProfile.Current.isLighting || game == null || game.dashLevel <= 0 || GameHUD.GameplayBlocked ||
                dashCooldownRemaining > 0 || HeldBox != null || !game.CanStand(transform.position)) return false;
            Vector3 forward = viewCamera != null ? Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 direction = forward * input.y + right * input.x;
            // Standing still, the burst goes the way the character faces; the camera is only the fallback.
            if (direction.sqrMagnitude < .0001f && visual != null) direction = Vector3.ProjectOnPlane(visual.forward, Vector3.up);
            if (direction.sqrMagnitude < .0001f) direction = forward;
            dashDirection = direction.normalized; DashStop = null;
            dashTimeRemaining = DashDuration(game.dashLevel);
            dashCooldownRemaining = DashCooldown;
            return true;
        }

        public void CancelDash() => dashTimeRemaining = 0;
        public void ResetDash() { dashTimeRemaining = 0; dashCooldownRemaining = 0; }

        public void UpdateNearby()
        {
            if (NearbyBox != null) NearbyBox.Highlight(false);
            NearbyBox = null;
            float best = grabReach;
            Vector3 origin = transform.position + Vector3.up * .7f;
            PullableBox pointed = null;
            // Under spaceGrab the cursor is locked to screen centre, so the screen-point ray is not a
            // pointing device any more; skip it and fall back to the proximity search alone.
            if (!BridgeProfile.Current.spaceGrab && viewCamera != null && Mouse.current != null && Physics.Raycast(viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue()), out var cursorHit, 260f))
                pointed = cursorHit.collider.GetComponentInParent<PullableBox>();
            foreach (var hit in Physics.OverlapSphere(origin, grabReach, ~0, QueryTriggerInteraction.Ignore))
            {
            var box = hit.GetComponentInParent<PullableBox>();
                if (box == null || (box.Holder != null && box.Holder != this)) continue;
                float distance = Vector3.Distance(origin, hit.ClosestPoint(origin));
                if (distance > grabReach || !CanReach(box)) continue;
                if (box == pointed) { NearbyBox = box; break; }
                if (distance < best) { best = distance; NearbyBox = box; }
            }
            if (NearbyBox != null) NearbyBox.Highlight(true);
            if (HeldBox != null) HeldBox.Highlight(true);
        }

        bool CanReach(PullableBox box)
        {
            Vector3 origin = transform.position + Vector3.up * .7f;
            if (Vector3.Distance(origin, box.Shape.ClosestPoint(origin)) > grabReach) return false;
            Vector3 ray = box.Shape.bounds.center - origin;
            foreach (var hit in Physics.RaycastAll(origin, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != controller && hit.collider != box.Shape && !hit.collider.transform.IsChildOf(transform)) return false;
            return true;
        }

        public bool TryGrab(PullableBox box)
        {
            if (GameHUD.GameplayBlocked) return false;
            var soul = GameManager.Instance;
            var stone = box != null ? box.GetComponent<BridgeBlock>() : null;
            if(stone!=null&&stone.placed&&BridgeProfile.Current.isLighting&&soul.TryReclaimTerminalTile(stone))
            {
                // ReclaimTerminalTile moves the now-loose terminal crystal beside the player so the
                // same click can lift it; connected branches and hub links have already been checked.
            }
            if (stone != null && !stone.CanLift)
            {
                soul.Notify(stone.placed ? "BRIDGE BLOCK ANCHORED\nConnected blocks become the walkable bridge. Grab a loose block to extend it." : stone.Title.ToUpper() + " IS TOO HEAVY\nRequires Strength " + stone.RequiredStrength + "; yours is " + soul.strength + ". Upgrade in Skills [Tab].", false);
                return false;
            }
            if (box == null || HeldBox != null || !CanReach(box) || !box.Grab(this)) return false;
            CancelDash();
            HeldBox = box;
            HeldBoxes.Clear(); HeldBoxes.Add(box);
            if (soul != null)
            {
                foreach (var other in soul.Blocks.Where(b => b.CanLift && b.Box != box && b.Box.Holder == null && Vector3.Distance(b.transform.position, box.transform.position) < 2.8f).OrderBy(b => Vector3.Distance(b.transform.position, box.transform.position)))
                {
                    if (HeldBoxes.Count >= soul.Capacity) break;
                    if (Vector3.Distance(transform.position, other.transform.position) > 3.6f) continue;
                    bool blocked = Physics.Linecast(box.transform.position, other.transform.position, out var obstacle, ~0, QueryTriggerInteraction.Ignore) && obstacle.collider != other.Box.Shape && !HeldBoxes.Any(b => b.Shape == obstacle.collider);
                    if (!blocked && other.Box.Grab(this)) { HeldBoxes.Add(other.Box); other.Box.Highlight(true); }
                }
                soul.Notify("HAULING " + HeldBoxes.Count + " / " + soul.Capacity + " BLOCKS\nWASD to drag · Release left mouse at a glowing path edge.", false);
            }
            if(soul!=null && soul.links>0) BeginOrbit();
            HeldBox.Highlight(true);
            if (sfx != null && GameAudio.Grab != null)
            {
                sfx.pitch = GameAudio.GrabPitch(box);
                sfx.PlayOneShot(GameAudio.Grab);
            }
            return true;
        }

        public void Release()
        {
            if (HeldBox == null) return;
            if (Orbiting) { DropOrbit(false); return; }
            var released = HeldBoxes.Count > 0 ? HeldBoxes.ToArray() : new[] { HeldBox };
            HeldBox = null;
            HeldBoxes.Clear();
            foreach (var box in released)
            {
                box.Release();
                var stone = box.GetComponent<BridgeBlock>(); if (stone != null) stone.Released();
            }
        }

        public void StepMovement(Vector2 input, float deltaTime)
        {
            if (GameHUD.GameplayBlocked || deltaTime <= 0) return;
            input = Vector2.ClampMagnitude(input, 1);
            Vector3 forward = viewCamera != null ? Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 before = transform.position;
            var soul = GameManager.Instance;
            // The cooldown runs from the end of the burst, so every level gets the same full second between dashes.
            if (dashTimeRemaining <= 0) dashCooldownRemaining = Mathf.Max(0, dashCooldownRemaining - deltaTime);
            if (dashTimeRemaining > 0 && HeldBox != null) CancelDash();
            bool dashFrame = dashTimeRemaining > 0 && BridgeProfile.Current.isLighting && HeldBox == null;
            float dashSlice = dashFrame ? Mathf.Min(deltaTime, dashTimeRemaining) : 0;
            float normalSlice = deltaTime - dashSlice;
            if (dashFrame)
            {
                MoveSampledPlanar(dashDirection * (DashSpeed * dashSlice), soul, true);
                dashTimeRemaining = Mathf.Max(0, dashTimeRemaining - dashSlice);
            }
            else normalSlice = deltaTime;
            Vector3 motion = (forward * input.y + right * input.x) * (HeldBox != null ? pullSpeed : moveSpeed) * normalSlice;
            if (soul != null)
            {
                // Stop at an unsupported edge, including while hauling a group.
                string edgeReason = null;
                if (!dashFrame && motion.sqrMagnitude > .00001f && !soul.CanStand(transform.position + motion))
                {
                    // Bridge borders are support limits, not walls. Keep the supported component
                    // of a diagonal input so running along an edge never cancels forward movement.
                    Vector3 alongX = new Vector3(motion.x, 0, 0);
                    Vector3 alongZ = new Vector3(0, 0, motion.z);
                    bool canX = alongX.sqrMagnitude > .00001f && soul.CanStand(transform.position + alongX);
                    bool canZ = alongZ.sqrMagnitude > .00001f && soul.CanStand(transform.position + alongZ);
                    motion = canX && (!canZ || alongX.sqrMagnitude > alongZ.sqrMagnitude) ? alongX
                        : canZ ? alongZ : Vector3.zero;
                    if (motion.sqrMagnitude <= .00001f)
                        edgeReason = "THE PATH ENDS HERE\nBuild at the glowing edge: release a dragged stone, or press Q with orbiting stones.";
                }
                foreach (var held in HeldBoxes) if (!Orbiting && motion.sqrMagnitude > .00001f && !soul.CanDragAt(held.transform.position + motion)) edgeReason = "YOUR HAUL NEEDS MORE ROOM\nExtend or widen the connected bridge, or move fewer blocks together.";
                if (edgeReason != null)
                {
                    motion = Vector3.zero;
                    if (Time.unscaledTime >= nextEdgeNotice) { nextEdgeNotice = Time.unscaledTime + 3; soul.Notify(edgeReason, false); }
                }
            }
            if (dashFrame && motion.sqrMagnitude > .00001f)
            {
                // A frame that straddles the dash's expiry can contain regular walking too. Sample
                // that remainder as well, so a large dt cannot skip a hole after the burst ends.
                MoveSampledPlanar(motion, soul);
            }
            else if (HeldBox != null && !Orbiting)
            {
                // Sweep both bodies before translating the pair. Neither can pass through a wall or another box.
                float distance = motion.magnitude;
                if (distance > .00001f)
                {
                    Vector3 direction = motion / distance;
                    float allowed = distance;
                    Vector3 center = transform.position + controller.center;
                    float segment = controller.height * .5f - controller.radius;
                    foreach (var hit in Physics.CapsuleCastAll(center + Vector3.up * segment, center - Vector3.up * segment,
                        controller.radius, direction, distance + .03f, ~0, QueryTriggerInteraction.Ignore))
                        Limit(hit, ref allowed);
                    foreach (var held in HeldBoxes)
                    {
                    var shape = held.Shape;
                    Vector3 extents = Vector3.Scale(shape.size * .5f, shape.transform.lossyScale) - Vector3.one * .015f;
                    Vector3 boxCenter = held.Body.position + held.Body.rotation * Vector3.Scale(shape.center, shape.transform.lossyScale);
                    foreach (var hit in Physics.BoxCastAll(boxCenter, extents, direction,
                        shape.transform.rotation, distance + .03f, ~0, QueryTriggerInteraction.Ignore)) Limit(hit, ref allowed);
                    }
                    motion = direction * allowed;
                }
                // The paired sweep already checked collisions with the environment. Ignore only the held box during the controller move.
                foreach (var held in HeldBoxes) Physics.IgnoreCollision(controller, held.Shape, true);
                controller.Move(motion);
                foreach (var held in HeldBoxes) Physics.IgnoreCollision(controller, held.Shape, false);
                Vector3 actual = transform.position - before;
                actual.y = 0;
                foreach (var held in HeldBoxes) held.Translate(actual);
                Physics.SyncTransforms();
            }
            else if (!dashFrame) controller.Move(motion);
            Vector3 planar = transform.position - before;
            planar.y = 0;
            MovementSpeed = planar.magnitude / deltaTime;
            fallingSpeed = controller.isGrounded ? -2f : fallingSpeed - 20f * deltaTime;
            controller.Move(Vector3.up * (fallingSpeed * deltaTime));

            // Footstep cadence comes from distance walked, not a timer, so it slows
            // by itself when pullSpeed replaces moveSpeed while dragging a block.
            // isGrounded only reflects the LAST Move(), so it has to be read here,
            // after the gravity step - reading it after the horizontal move alone
            // reports false on flat ground and silently wipes the accumulator.
            if (controller.isGrounded) airborneTime = 0;
            else airborneTime += deltaTime;
            // One flickering frame must not reset the stride; a real fall does.
            if (airborneTime > .18f) strideDistance = 0;
            else if (planar.magnitude > 0)
            {
                strideDistance += planar.magnitude;
                if (strideDistance >= StrideLength) { strideDistance %= StrideLength; PlayFootstep(); }
            }

            if(Orbiting) UpdateOrbit(deltaTime);
            Vector3 facing = HeldBox != null && !Orbiting ? HeldBox.transform.position - transform.position : planar;
            facing.y = 0;
            if (visual != null && facing.sqrMagnitude > .00001f)
                visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(facing), 720f * deltaTime);
            int next = HeldBox != null && !Orbiting ? Pull : MovementSpeed > .08f ? Run : Idle;
            if (animator != null)
            {
                if (animationState != next) { animator.CrossFadeInFixedTime(next, .14f); animationState = next; }
                animator.speed = HeldBox != null && !Orbiting && MovementSpeed < .08f ? 0f : 1f;
            }
        }

        // Moves in short supported steps so a large dt cannot skip a hole. With buildTiles, a step
        // into the void first lays a bridge tile there; the dash stops where no tile can go.
        void MoveSampledPlanar(Vector3 desired, GameManager soul, bool buildTiles = false)
        {
            float distance = desired.magnitude;
            if (distance <= .000001f) return;
            if (HeldBox != null && !Orbiting) return; // dashing is disabled while hauling
            Vector3 direction = desired / distance;
            float moved = 0;
            while (moved < distance - .0001f)
            {
                float segment = Mathf.Min(DashSupportStep, distance - moved);
                Vector3 next = transform.position + direction * segment;
                if (soul != null && !soul.CanStand(next) && !(buildTiles && soul.TryBuildDashTile(next))) { if (buildTiles) DashStop = "no tile: " + (soul.DashRefusal ?? "?") + " at " + LightingBridgeGrid.CellOf(next); break; }
                Vector3 safePosition = transform.position;
                sideHit = null;
                controller.Move(direction * segment);
                Vector3 actual = transform.position - safePosition; actual.y = 0;
                if (soul != null && !soul.CanStand(transform.position))
                {
                    controller.enabled = false; transform.position = safePosition; controller.enabled = true;
                    Physics.SyncTransforms();
                    if (buildTiles) DashStop = "left support at " + LightingBridgeGrid.CellOf(transform.position + direction * segment);
                    break;
                }
                moved += segment;
                // Stop after a wall or a collision slide; each subsequent sample starts from the
                // actual character position and may not cut diagonally across unsupported ground.
                if (actual.magnitude < segment - .025f || Vector3.Dot(actual, direction) < actual.magnitude * .85f)
                {
                    if (buildTiles) DashStop = (actual.magnitude < segment - .025f ? "blocked by " : "slid by ") + (sideHit != null ? PathOf(sideHit.transform) + " normal " + sideNormal.ToString("F2") : "no side contact")
                        + " at " + transform.position.ToString("F2") + " y " + transform.position.y.ToString("F3");
                    break;
                }
            }
        }


        void BeginOrbit()
        {
            Orbiting=true; pickupPositions.Clear();
            foreach(var b in HeldBoxes) { pickupPositions[b]=b.transform.position; b.Shape.enabled=false; }
            UpdateOrbit(0);
            GameManager.Instance.Notify("SOUL ORBIT · "+HeldBoxes.Count+" / "+GameManager.Instance.Capacity+"\nMouse released safely · WASD to carry · Q to drop or build.",false);
        }
        public Vector3 SavedBlockPosition(BridgeBlock block)
        {
            return pickupPositions.TryGetValue(block.Box,out var grounded) ? grounded : block.transform.position;
        }
        void UpdateOrbit(float dt)
        {
            orbitAngle+=dt*.8f;
            for(int i=0;i<HeldBoxes.Count;i++)
            {
                float a=orbitAngle+i*Mathf.PI*2/HeldBoxes.Count;
                Vector3 target=transform.position+new Vector3(Mathf.Cos(a)*1.35f,1.2f+Mathf.Sin(a*2+Time.time)*.12f,Mathf.Sin(a)*1.35f);
                var box=HeldBoxes[i]; box.Translate(target-box.transform.position);
                box.transform.rotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);
            }
        }
        // Safe ground is required for loose drops. A carried stone never becomes hidden void-floor.
        bool FindDrop(PullableBox box, Vector3 origin, out Vector3 position)
        {
            for(int ring=1;ring<=10;ring++) for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI*2/16; var candidate=origin+new Vector3(Mathf.Cos(a)*ring*.9f,0,Mathf.Sin(a)*ring*.9f);candidate.y=.26f;
                var game=GameManager.Instance;
                if(!game.CanStand(candidate) || !game.CanStand(candidate+new Vector3(.43f,0,.43f)) || !game.CanStand(candidate-new Vector3(.43f,0,.43f)))continue;
                bool occupied=false;
                foreach(var hit in Physics.OverlapBox(candidate,new Vector3(.44f,.23f,.44f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore))
                    if(hit!=box.Shape){occupied=true;break;}
                if(occupied)continue;
                position=candidate;return true;
            }
            position=default; return false;
        }
        public void DropOrbit(bool build)
        {
            if(!Orbiting)return;
            var game=GameManager.Instance;
            int built=0,dropped=0;
            foreach(var box in HeldBoxes.ToArray())
            {
                var block=box.GetComponent<BridgeBlock>();
                bool place=build && game.FindOrbitPlacement(block,out _);
                Vector3 target;
                if(place)game.FindOrbitPlacement(block,out target);
                else if(!FindDrop(box,transform.position,out target) && !FindDrop(box,pickupPositions[box],out target)) continue;
                box.transform.rotation=Quaternion.identity; box.Translate(target-box.transform.position);
                box.Shape.enabled=true; box.Release(); HeldBoxes.Remove(box); pickupPositions.Remove(box);
                Physics.SyncTransforms();
                if(place && game.TryPlace(block))built++;else dropped++;
            }
            HeldBox=HeldBoxes.Count>0?HeldBoxes[0]:null; Orbiting=HeldBox!=null;
            if(!Orbiting)pickupPositions.Clear();
            game.Notify(built>0?(BridgeProfile.Current.isLighting?"NETWORK EXTENDED · "+built+" TILES\n"+dropped+" blocks dropped nearby.":"BRIDGE EXTENDED · "+built+" STONES\n+"+GameManager.Format(game.Rate)+" Souls/sec total · "+dropped+" dropped nearby.")
                : Orbiting?"DROP AREA BLOCKED\nMove to an open shore and press Q. Remaining stones stay safe in orbit."
                :"STONES GROUNDED\n"+dropped+" stones dropped safely nearby. Click to gather again.",false);
            game.Save();
        }

        void PlayFootstep()
        {
            // Fire before the audio guard: a missing clip must not also swallow the visual.
            if (Stepped != null) Stepped(transform.position);
            // Lighting: a soft footstep per stride (none while dashing) on top of the note per floor cell
            // entered (BridgeSteps.StepNote).
            if (BridgeProfile.Current.isLighting)
            {
                if (IsDashing) return;
                var steps = GetComponent<LightingFootsteps>(); if (steps == null) steps = gameObject.AddComponent<LightingFootsteps>(); // not ??: Unity's fake null
                steps.PlayStride();
                return;
            }
            var clip = ForestSoundscape.Footstep(transform.position);
            if (sfx == null || clip == null) return;
            sfx.pitch = 1; sfx.PlayOneShot(clip);
        }

        void Limit(RaycastHit hit, ref float allowed)
        {
            if (hit.collider == controller || HeldBoxes.Any(b => b.Shape == hit.collider) || hit.collider.transform.IsChildOf(transform)) return;
            allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - .025f));
        }

        void OnApplicationFocus(bool focused) { if (!focused) Release(); }
        void OnApplicationPause(bool paused) { if (paused) Release(); }
        void OnDisable() { Release(); if (NearbyBox != null) NearbyBox.Highlight(false); }
    }
}
