using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LightingBlocks
{
    [RequireComponent(typeof(Camera))]
    public sealed class StrategyCamera : MonoBehaviour
    {
        public Transform player;
        public Vector2 bounds = new Vector2(16, 16);
        public Vector2 minimumFocus = new Vector2(-16, -16);
        public float panSpeed = 12f;
        public float edgePixels = 12f;
        // degrees per pixel of mouse movement; ~180 degree turn per 1100 px, suits a 1080p desktop.
        public float orbitSpeed = .16f;
        public float distance = 18f;
        public float pitch = 42f;
        public float yaw = 45f;
        // the camera FOV drops from 60 to 26 degrees, and tan(30)/tan(13) is 2.5, so every existing distance keeps its framing
        public float lensScale = 2.5f;
        [Min(120)] public float lightingFarClip = 4096f;
        public float smoothing = 4f;
        public bool edgePan = true;
        public bool readInput = true;
        public Vector3 Focus { get; private set; }
        Vector3 smoothedFocus;
        DepthOfField dof;
        bool dofResolved;
        bool cursorLocked;
        public void CenterOnPlayer()
        {
            if (player == null) return;
            Focus = new Vector3(player.position.x, 0, player.position.z);
        }
        void Start() { CenterOnPlayer(); smoothedFocus = Focus; Apply(1f); }
        void LateUpdate()
        {
            UpdateCursorLock();
            MaintainLightingFarClip();
            if (GameHUD.GameplayBlocked) return;
            if (BridgeProfile.Current.playerCamera)
            {
                // Player-centred camera: Focus tracks the player unconditionally (no free pan target),
                // the mouse orbits instead of panning, and the scroll wheel still zooms.
                CenterOnPlayer();
                if (readInput && Application.isFocused)
                {
                    var mouse = Mouse.current;
                    if (mouse != null)
                    {
                        Vector2 delta = mouse.delta.ReadValue();
                        yaw = Mathf.Repeat(yaw + delta.x * orbitSpeed, 360f);
                        pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, 12f, 78f);
                        Vector2 position = mouse.position.ReadValue();
                        bool inside = position.x >= 0 && position.x <= Screen.width && position.y >= 0 && position.y <= Screen.height && !GameHUD.PointerOverUI();
                        if (inside) distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * .012f, BridgeProfile.Current.isLighting ? 10f : 14f, 42f);
                    }
                }
                Apply(1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));
                return;
            }
            if (readInput && Application.isFocused)
            {
                var mouse = Mouse.current;
                var keys = Keyboard.current;
                Vector2 pan = Vector2.zero;
                if (keys != null)
                {
                    pan = new Vector2((keys.rightArrowKey.isPressed ? 1 : 0) - (keys.leftArrowKey.isPressed ? 1 : 0),
                        (keys.upArrowKey.isPressed ? 1 : 0) - (keys.downArrowKey.isPressed ? 1 : 0));
                    if (keys.spaceKey.isPressed) { CenterOnPlayer(); Apply(1f - Mathf.Exp(-12f * Time.unscaledDeltaTime)); return; }
                }
                if (mouse != null)
                {
                    Vector2 position = mouse.position.ReadValue();
                    bool inside = position.x >= 0 && position.x <= Screen.width && position.y >= 0 && position.y <= Screen.height && !GameHUD.PointerOverUI();
                    if (edgePan && inside && !mouse.middleButton.isPressed)
                    {
                        if (position.x < edgePixels) pan.x -= 1;
                        if (position.x > Screen.width - edgePixels) pan.x += 1;
                        if (position.y < edgePixels) pan.y -= 1;
                        if (position.y > Screen.height - edgePixels) pan.y += 1;
                    }
                    if (inside && mouse.middleButton.isPressed) Pan(-mouse.delta.ReadValue() * (distance * lensScale / Screen.height));
                    if (inside) distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * .012f, 14f, 42f);
                }
                Pan(Vector2.ClampMagnitude(pan, 1) * panSpeed * Time.unscaledDeltaTime);
            }
            Apply(1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));
        }
        // Locked+hidden only while the player camera is active and gameplay is actually live; a
        // locked cursor still reports Mouse.current.delta, which is what the orbit above reads.
        // Only touches the Cursor API when the state actually changes, never once per frame.
        void UpdateCursorLock()
        {
            bool shouldLock = BridgeProfile.Current.playerCamera && !GameHUD.GameplayBlocked && Application.isFocused;
            if (shouldLock == cursorLocked) return;
            cursorLocked = shouldLock;
            Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !shouldLock;
        }
        void OnDisable()
        {
            // Leaving Play mode must never leave the editor cursor locked or hidden.
            if (!cursorLocked) return;
            cursorLocked = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        public void Pan(Vector2 amount)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 target = Focus + right * amount.x + forward * amount.y;
            Focus = new Vector3(Mathf.Clamp(target.x, minimumFocus.x, bounds.x), 0, Mathf.Clamp(target.z, minimumFocus.y, bounds.y));
        }
        void MaintainLightingFarClip()
        {
            if (!BridgeProfile.Current.isLighting) return;
            var camera = GetComponent<Camera>();
            if (camera != null && camera.farClipPlane < lightingFarClip) camera.farClipPlane = lightingFarClip;
        }
        void Apply(float blend)
        {
            MaintainLightingFarClip();
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            smoothedFocus = Vector3.Lerp(smoothedFocus, Focus, blend);
            transform.position = smoothedFocus - transform.forward * distance * lensScale;
            // Gaussian depth of field is far-only and metre based: keep the focus plane on the smoothed
            // focus point and let everything more than ~8 m behind it soften toward the void.
            if (!BridgeProfile.Current.isLighting && !dofResolved && Application.isPlaying)
            {
                dofResolved = true;
                var volume = FindAnyObjectByType<Volume>();
                // volume.profile is the runtime instance, so this never dirties the shared asset.
                if (volume != null && volume.profile != null) volume.profile.TryGet(out dof);
            }
            if (dof != null)
            {
                float span = distance * lensScale;
                dof.gaussianStart.value = span + 8f;
                dof.gaussianEnd.value = span + 45f;
            }
        }
    }
}
