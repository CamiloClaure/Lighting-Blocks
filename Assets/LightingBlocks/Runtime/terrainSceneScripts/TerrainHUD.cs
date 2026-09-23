using UnityEngine;
using UnityEngine.UI;

namespace LightingBlocks.TerrainScene
{
    // Sandbox only, and additive only: GameHUD is not edited. It builds its canvas from code in
    // BuildPlay (GameHUD.cs:207-254) with stable object names, so trimming it by name afterwards
    // costs one pass and leaves the shipping HUD untouched for SampleScene.
    //
    // Kept, all at the top of the screen: the location title and the action feedback line as bare
    // text, and the Souls and particle panels in their authored style with the wording cut back to
    // their data lines. Removed: the three tutorial panels (objective, interaction hint, key hints)
    // and the world-space labels that WorldLabels.Adopt hangs over the map.
    //
    // Nothing here calls SetActive on a panel GameHUD itself toggles - the feedback line and the
    // place prompt are stripped of their frame instead, so GameHUD keeps full control of when they
    // are seen.
    [DefaultExecutionOrder(2000)]
    public sealed class TerrainHUD : MonoBehaviour
    {
        RectTransform canvas;

        // Polled rather than done once at startup, because GameHUD builds its canvas on its own
        // Awake and rebuilds it on a new journey. A destroyed canvas reads null and re-trims.
        void LateUpdate()
        {
            if (canvas != null) return;
            var hud = GameHUD.Current;
            if (hud == null) return;
            canvas = hud.transform.Find("Last Spark Canvas UI") as RectTransform;
            if (canvas != null) Trim(canvas);
        }

        void Trim(Transform root)
        {
            var play = root.Find("Gameplay HUD");
            Hide(play, "Current Objective");
            Hide(play, "Interaction Hint");
            Hide(play, "Key Hints");
            Hide(play, "World Labels");
            Bare(root.Find("Action Feedback"));
            // GameHUD.cs:410 owns the place prompt's active state and flips it back on every time
            // a block can be placed, so hiding the chip itself would not hold. Emptying it does:
            // Bare kills the frame, and the caption is a child GameHUD never touches.
            var chip = root.Find("Place Prompt") ?? Child(play, "Place Prompt");
            Bare(chip);
            Hide(chip, "Place Prompt Text");

            // Souls and particles keep their authored frame, inset and type sizes. Only the words
            // go: the heading over a number that already says what it is, and the two-line help.
            // What is left is each panel's own data line, so the boxes close up around it.
            //
            // The wallet lives on the canvas rather than the play root so it survives the menus,
            // which is why it is found from there.
            var wallet = root.Find("Souls Counter");
            Hide(wallet, "Souls Label");
            Place(wallet, 22, 20, 350, 96);
            Place(Child(wallet, "Souls Amount"), 20, 12, 310, 52);
            Place(Child(wallet, "Souls Income"), 20, 68, 310, 26);

            // Particles slide up into the gap the shortened wallet leaves, same 10 px stack gap.
            var calls = play == null ? null : play.Find("Fragment Calls");
            Hide(calls, "Call Heading");
            Hide(calls, "Particle Help");
            Place(calls, 22, 126, 350, 80);
            Place(Child(calls, "Particle Inventory"), 18, 12, 314, 58);
        }

        static Transform Child(Transform parent, string name)
        {
            return parent == null ? null : parent.Find(name);
        }

        static void Hide(Transform parent, string name)
        {
            var child = Child(parent, name);
            if (child != null) child.gameObject.SetActive(false);
        }

        // Panel() paints a filled Image and stretches an "Edge" child over it for the gold ring, so
        // a box is only gone when both are off.
        static void Bare(Transform panel)
        {
            if (panel == null) return;
            var image = panel.GetComponent<Image>();
            if (image != null) image.enabled = false;
            Hide(panel, "Edge");
        }

        // Rect() anchors everything to the top left with anchoredPosition = (x, -y), so these are
        // the same coordinates the HUD was authored in. Font sizes are left alone: the panels are
        // losing lines, not changing type.
        static void Place(Transform t, float x, float y, float width, float height)
        {
            var rect = t as RectTransform;
            if (rect == null) return;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
