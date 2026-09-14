# Immersive pass

Three complaints, three root causes. Every fix runs at **runtime** from `SoulBridgeGame.Start`
and `SoulBridgeHUD.BuildUI`, so pressing Play applies them - no editor menu to click, no scene
surgery, and nothing to redo if the scene is rebuilt from `Tools/Last Spark/...`.

## 1. The world did not fill the screen

Cause: the three shores are polygon islands with literally nothing around them. At 55 degrees
of pitch the camera never sees a horizon, so every pixel outside the islands was camera
background.

`SoulBridgeImmersion` (new):
- A generated "Deep Valley" polar mesh out to radius 135 around the play area, sitting at
  y = -5 with Perlin hills. It has **no collider**: it is scenery, the bridges stay the only
  way across, and the player's y < -3 recovery still catches every fall.
- The valley rises to -3.6 within 10 m of a shore so it meets the authored cliff skirts
  (which hang to -5..-7). Without that you could see straight through the seam to the
  background - that was the leftover void in the first verification screenshot.
- A 14 m chasm is carved under both rifts, so a gap reads as depth rather than a flat hole.
- A rim lifts +12 m from radius 58 outward, so the frame ends in land at any zoom.
- 90 ruin silhouettes cloned from the art-pack instances already in the scene (colliders and
  lights stripped). They are kept 9 m clear of every court and out of both rifts, and their
  size scales with distance from the courts so nothing near the play area blocks the view.
- Haze retuned: linear fog 24 -> 78 in a lighter slate that the camera background matches,
  warmer ambient and directional light so the distant ground reads.
- Camera distance 27 -> 22, and 4x MSAA in `PC_RPAsset` (it was disabled - that was the
  jagged geometry in the screenshot).

## 2. The skill tree was a card grid

`SoulBridgeSkillTree` (new) draws a real tree from procedural circle sprites (no new assets):

    STONE SOURCE --- THE SOUL PATH --- GRAVE SOURCE      (trunk, shows blocks connected)
                    /               \
            SWIFT HAULING        SOUL WELL
                  |                   |
             SOUL LINKS         LIGHTING SOUL
                    \               /
                     ASTRAL STRENGTH

- Dormant branches show as dim ghosts, then **grow** out of a power the moment it awakens; a
  spark travels every live branch.
- Buying fires a punch scale, a white ring burst and a pip pop. Affordable nodes breathe,
  hovering lifts a node, maxed nodes go solid gold, locked ones stay grey.
- Each node carries its level as a roman numeral, level pips, name and cost; the right-hand
  pane follows the hovered node and carries its own AWAKEN button.

## 3. Text was not HD

In order of damage:
- The world labels were `TextMesh` quads seen from 22 m - a 48 px raster smeared over a
  handful of screen pixels. `WorldLabels` retires them and redraws each one on the HUD canvas,
  tracking its world point, with an outline, a distance fade and an edge fade.
- `Canvas.pixelPerfect` was off, so glyph quads landed on fractional pixels.
- The 1600x1000 reference resolution gave a 0.909 canvas scale on a 1914 px window, resampling
  every glyph. Now 1440x900, still Expand, so the UI keeps fitting small game views.
- `resizeTextForBestFit` removed everywhere, the captions that overlapped in the screenshot
  are two-line labels now, and the nav buttons were widened so MEMORIES stops wrapping.

## Also

`Save` fell over on `File.Replace` (the SAVE FAILED banner). It now falls back to copy + move
before reporting failure.

## Verified

`Tools/Last Spark/Run Soul Bridge Checks` - 100 PASS, 0 FAIL, including new assertions that the
deeps add no collision, that the valley stays under the courts and drops away in the rifts, that
no scenery stands inside a court, that no `TextMesh` labels survive, that the canvas is pixel
perfect, and that branches grow only out of powers that are already awake.
