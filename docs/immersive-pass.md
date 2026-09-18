# Immersive pass

Three complaints, three root causes. Every fix runs at **runtime** from `GameManager.Start`
and `GameHUD.BuildUI`, so pressing Play applies them - no editor menu to click, no scene
surgery, and nothing to redo if the scene is rebuilt from `Tools/Lighting Blocks/...`.

## 1. The world did not fill the screen

Cause: the three shores are polygon islands with literally nothing around them. At 55 degrees
of pitch the camera never sees a horizon, so every pixel outside the islands was camera
background.

`WorldDressing` (new):
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

**2026-09-17, Spirit Path Tier 2:** the valley, its rim and the chasm carving are gone -
below the islands there is now only the night-sky void. Each island has a 40 m rock keel
(`WorldDressing.CliffMesh`, six rings plus a cap), and six small non-collidable background
islets with trees (`WorldDressing.BuildBackgroundIslands`, rendering layer 2) give depth.
The reason: the long 26 degree lens plus fog and depth of field make empty background the
composition, so the original "fill the frame with land" rationale no longer applies.

**2026-09-18, Spirit Path Tiers 3-5:** the void read as depth, but everything in it was still
lit flat. `SpiritPathLook.Apply` (`Editor/SpiritPathLook.cs`) now retints the moon and switches
ambient to `AmbientMode.Trilight` from `Forest.asset`'s sky/equator/ground colours, and lifts
the additional-light shadowmap to 4096 px with SSAO at 0.5 intensity
(`Editor/SpiritPathLook.cs:43`, `:49`). The keels and every forest tree material pick up a rim
colour and a little self-fill, so a silhouette against the void reads as rock or foliage
instead of a black cutout (`SpiritPathLook.cs:71-89`), and the three Wisdom Trees' bark and
leaves go emissive (`SpiritPathLook.cs:78`). On the object side, `WorldDressing.RimRocks` and
`WorldDressing.Lanterns` (`WorldDressing.cs:273`, `:356`) line every shore with faceted rock
clones and warm ember lanterns cloned from the scene's own soul-well fixture, luminous plants
get their own emission tint (`SpiritPathLook.cs:80`), and `ForestConfiguration`'s cover pass
adds a dense "hero" ring of undergrowth within 10 m of every Wisdom Tree on top of the existing
scatter (`ForestConfiguration.cs:121-131`). Volumetric fog was the planned Tier 5 and was
dropped on purpose: `M_PlayerFog` carries the void-mist colour, top, depth and strength instead,
written by the same apply pass (`SpiritPathLook.cs:91-100`).

## 2. The skill tree was a card grid

`SkillTree` (new) draws a real tree from procedural circle sprites (no new assets):

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

**2026-09-18, Spirit Path Tier 6:** the HUD's own palette was still the old warm amber-on-brown
scheme against a now navy, gold-accented world. `GameHUD` moves to a deep navy glass with a
single gold accent (`GameHUD.cs:52-56`), and every panel and button now renders through a
shared rounded-rect sprite with a thin gold edge ring instead of a flat-coloured `Image`
(`GameHUD.cs:432-464`). The gameplay HUD gained a "THE SPIRIT PATH" title banner, moved the
resource readout under the Souls wallet, added a compact WASD/E key-hint bar, and condensed the
skills/memory/pause shortcuts into three equal icon buttons (`GameHUD.cs:203-229`, `:240-244`).
A new place-prompt chip tracks the "Next bridge block preview" ghost on screen and shows
"[E] Place block" whenever it is in frame (`GameHUD.RefreshPlacePrompt`, `GameHUD.cs:379-405`).
The pixel-perfect canvas and 1440x900 reference resolution from the original fix above did not
need to change (`GameHUD.cs:191`, `:193`).

## Also

`Save` fell over on `File.Replace` (the SAVE FAILED banner). It now falls back to copy + move
before reporting failure.

## Polish

Tiers 1-6 shipped as one commit and reapply from one place: `Tools/Lighting Blocks/Apply Spirit
Path Look`, or by creating `Library/SpiritPath.apply-request` (any content, the poll only checks
that the file exists) and reading the result back from `Library/SpiritPath.apply-result.txt`
(`Editor/SpiritPathLook.cs:14`, `:37`). Baseline, Tier 1, Tier 2 and Tier 3-6 screenshots and
test logs are archived under `docs/art-direction/` for side-by-side comparison. The gameplay
checks named in the "Verified" note below (dated to the original pass) have since grown
alongside the art pass: `Library/SpectralForest.tests.txt` currently reports 198 PASS, 0 FAIL.

## Verified

`Tools/Lighting Blocks/Run Gameplay Checks` - 100 PASS, 0 FAIL, including new assertions that the
deeps add no collision, that the valley stays under the courts and drops away in the rifts, that
no scenery stands inside a court, that no `TextMesh` labels survive, that the canvas is pixel
perfect, and that branches grow only out of powers that are already awake.
