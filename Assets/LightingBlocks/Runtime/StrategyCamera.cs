using UnityEngine;
using UnityEngine.InputSystem;

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
        public float distance = 18f;
        public bool edgePan = true;
        public bool readInput = true;
        public Vector3 Focus { get; private set; }
        Vector3 smoothedFocus;
        public void CenterOnPlayer()
        {
            if (player == null) return;
            Focus = new Vector3(player.position.x, 0, player.position.z);
        }
        void Start() { CenterOnPlayer(); smoothedFocus = Focus; Apply(1f); }
        void LateUpdate()
        {
            if (LastSparkHUD.GameplayBlocked) return;
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
                    bool inside = position.x >= 0 && position.x <= Screen.width && position.y >= 0 && position.y <= Screen.height && !LastSparkHUD.PointerOverUI();
                    if (edgePan && inside && !mouse.middleButton.isPressed)
                    {
                        if (position.x < edgePixels) pan.x -= 1;
                        if (position.x > Screen.width - edgePixels) pan.x += 1;
                        if (position.y < edgePixels) pan.y -= 1;
                        if (position.y > Screen.height - edgePixels) pan.y += 1;
                    }
                    if (inside && mouse.middleButton.isPressed) Pan(-mouse.delta.ReadValue() * (distance / Screen.height));
                    if (inside) distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * .012f, 14f, 42f);
                }
                Pan(Vector2.ClampMagnitude(pan, 1) * panSpeed * Time.unscaledDeltaTime);
            }
            Apply(1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        }
        public void Pan(Vector2 amount)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 target = Focus + right * amount.x + forward * amount.y;
            Focus = new Vector3(Mathf.Clamp(target.x, minimumFocus.x, bounds.x), 0, Mathf.Clamp(target.z, minimumFocus.y, bounds.y));
        }
        void Apply(float blend)
        {
            transform.rotation = Quaternion.Euler(55, 0, 0);
            smoothedFocus = Vector3.Lerp(smoothedFocus, Focus, blend);
            transform.position = smoothedFocus - transform.forward * distance;
        }
    }
}
