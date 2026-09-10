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
        public float moveSpeed = 4.5f;
        public float pullSpeed = 2.3f;
        [Tooltip("Distance from the player's center to the closest box surface.")]
        public float grabReach = 1.25f;
        public bool readInput = true;
        public PullableBox HeldBox { get; private set; }
        public readonly List<PullableBox> HeldBoxes = new List<PullableBox>();
        public PullableBox NearbyBox { get; private set; }
        public float MovementSpeed { get; private set; }
        CharacterController controller;
        float fallingSpeed;
        float nextEdgeNotice;
        int animationState;
        static readonly int Idle = Animator.StringToHash("Base Layer.Idle");
        static readonly int Run = Animator.StringToHash("Base Layer.Run");
        static readonly int Pull = Animator.StringToHash("Base Layer.Pull");

        void Awake() { controller = GetComponent<CharacterController>(); }

        void Update()
        {
            if (!readInput || LastSparkHUD.GameplayBlocked) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (!Application.isFocused) { Release(); return; }
            UpdateNearby();
            if (mouse == null || !mouse.leftButton.isPressed) Release();
            else if (mouse.leftButton.wasPressedThisFrame && !LastSparkHUD.PointerOverUI()) TryGrab(NearbyBox);
            Vector2 input = keyboard == null ? Vector2.zero : new Vector2(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            StepMovement(input, Time.deltaTime);
        }

        public void UpdateNearby()
        {
            if (NearbyBox != null) NearbyBox.Highlight(false);
            NearbyBox = null;
            float best = grabReach;
            Vector3 origin = transform.position + Vector3.up * .7f;
            PullableBox pointed = null;
            if (viewCamera != null && Mouse.current != null && Physics.Raycast(viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue()), out var cursorHit, 150f))
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
            if (LastSparkHUD.GameplayBlocked) return false;
            var soul = SoulBridgeGame.Instance;
            var stone = box != null ? box.GetComponent<SoulBridgeBlock>() : null;
            if (stone != null && !stone.CanLift)
            {
                soul.Notify(stone.placed ? "BRIDGE BLOCK ANCHORED\nConnected blocks become the walkable bridge. Grab a loose block to extend it." : stone.Title.ToUpper() + " IS TOO HEAVY\nRequires Strength " + stone.RequiredStrength + "; yours is " + soul.strength + ". Upgrade in Skills [Tab].", false);
                return false;
            }
            var fragment = box != null ? box.GetComponent<AstralFragment>() : null;
            if (fragment != null && !fragment.CanLift)
            {
                LastSparkGame.Instance.Notify("This memory is too heavy. Awaken Astral Strength first.", false);
                return false;
            }
            if (box == null || HeldBox != null || !CanReach(box) || !box.Grab(this)) return false;
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
            HeldBox.Highlight(true);
            if (LastSparkGame.Instance != null) LastSparkGame.Instance.Recalculate();
            return true;
        }

        public void Release()
        {
            if (HeldBox == null) return;
            var released = HeldBoxes.Count > 0 ? HeldBoxes.ToArray() : new[] { HeldBox };
            HeldBox = null;
            HeldBoxes.Clear();
            foreach (var box in released)
            {
                box.Release();
                var fragment = box.GetComponent<AstralFragment>(); if (fragment != null) fragment.Released();
                var stone = box.GetComponent<SoulBridgeBlock>(); if (stone != null) stone.Released();
            }
        }

        public void StepMovement(Vector2 input, float deltaTime)
        {
            if (LastSparkHUD.GameplayBlocked || deltaTime <= 0) return;
            input = Vector2.ClampMagnitude(input, 1);
            Vector3 forward = viewCamera != null ? Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 motion = (forward * input.y + right * input.x) * (HeldBox != null ? pullSpeed : moveSpeed) * deltaTime;
            Vector3 before = transform.position;
            var soul = SoulBridgeGame.Instance;
            if (soul != null)
            {
                // Stop at an unsupported edge, including while hauling a group.
                string edgeReason = null;
                if (motion.sqrMagnitude > .00001f && !soul.CanStand(before + motion)) edgeReason = "THE PATH ENDS HERE\nRelease your held block at the glowing edge to build the next tile.";
                foreach (var held in HeldBoxes) if (motion.sqrMagnitude > .00001f && !soul.CanDragAt(held.transform.position + motion)) edgeReason = "YOUR HAUL NEEDS MORE ROOM\nExtend or widen the connected bridge, or move fewer blocks together.";
                if (edgeReason != null)
                {
                    motion = Vector3.zero;
                    if (Time.unscaledTime >= nextEdgeNotice) { nextEdgeNotice = Time.unscaledTime + 3; soul.Notify(edgeReason, false); }
                }
            }
            if (HeldBox != null)
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
            else controller.Move(motion);
            Vector3 planar = transform.position - before;
            planar.y = 0;
            MovementSpeed = planar.magnitude / deltaTime;
            fallingSpeed = controller.isGrounded ? -2f : fallingSpeed - 20f * deltaTime;
            controller.Move(Vector3.up * (fallingSpeed * deltaTime));

            Vector3 facing = HeldBox != null ? HeldBox.transform.position - transform.position : planar;
            facing.y = 0;
            if (visual != null && facing.sqrMagnitude > .00001f)
                visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(facing), 720f * deltaTime);
            int next = HeldBox != null ? Pull : MovementSpeed > .08f ? Run : Idle;
            if (animator != null)
            {
                if (animationState != next) { animator.CrossFadeInFixedTime(next, .14f); animationState = next; }
                animator.speed = HeldBox != null && MovementSpeed < .08f ? 0f : 1f;
            }
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
