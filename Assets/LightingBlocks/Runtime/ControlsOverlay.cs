using UnityEngine;

namespace LightingBlocks
{
    public sealed class ControlsOverlay : MonoBehaviour
    {
        public BoxPlayer player;
        GUIStyle title, body, status;
        void OnGUI()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                status = new GUIStyle(body) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            title.normal.textColor = title.hover.textColor = title.active.textColor = title.focused.textColor = new Color(.45f, 1f, .87f);
            body.normal.textColor = body.hover.textColor = body.active.textColor = body.focused.textColor = Color.white;
            status.normal.textColor = status.hover.textColor = status.active.textColor = status.focused.textColor = Color.white;
            float width = Mathf.Min(480, Screen.width - 24);
            GUI.Box(new Rect(12, 12, width, 120), GUIContent.none);
            GUI.Label(new Rect(26, 20, width - 28, 30), "LIGHTING BLOCKS", title);
            GUI.Label(new Rect(26, 55, width - 28, 68), "WASD  Move  |  Hold left click near a box to grab\nMove while holding to push / pull. Release to let go.\nCamera: screen edges / arrows / middle drag\nScroll: zoom  |  Hold Space: center on player", body);
            string message = player != null && player.HeldBox != null ? "GRABBED  -  Move with WASD. Release left click to let go."
                : player != null && player.NearbyBox != null ? "BOX IN REACH  -  Hold left mouse button to grab" : "Walk up to a colored box";
            GUI.Box(new Rect((Screen.width - width) / 2, Screen.height - 54, width, 38), GUIContent.none);
            GUI.Label(new Rect((Screen.width - width) / 2, Screen.height - 54, width, 38), message, status);
        }
    }
}
