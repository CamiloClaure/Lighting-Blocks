# The Last Spark — MVP and placeholder replacement report

Prepared for Lighting Blocks • 9 September 2026

## Delivered experience

The current `SampleScene` is now the opening chapter of a mystical incremental adventure. The player discovers fragments in a dark remnant of the universe, physically moves them onto altars, grows Essence production, awakens powers, restores a bridge, and reaches a final revelation.

The existing animated character, WASD movement, hold-to-grab interaction, and strategy camera were retained. The earlier box sandbox is backed up as `SampleScene_BeforeMysticalMVP.unity`; the still-earlier original scene backup also remains.

The MVP contains two islands, a walkable unlockable bridge, 16 altars, four free Starlight fragments, one free Life fragment, two initially locked Ancient Hearts, purchasable fragments, three upgrade levels of Astral Strength, ten Resonance levels, a recall wisp, three memories, and a chapter ending. Completing the chapter doubles production and allows continued play.

## Engagement approach

The opening is designed around fast, understandable rewards rather than repeated clicking:

1. **Immediate discovery:** the first fragment starts within reach; placing it produces visible light, income, a memory, and a one-time 25 Essence reward.
2. **An achievable next action:** that reward can immediately buy another fragment. Free fragments around the island encourage exploration as an alternative.
3. **Placement decisions:** Life fragments boost nearby producers. Their visible influence radius and placement preview make arrangements understandable.
4. **Multiplicative growth:** Resonance increases the value of the whole constellation.
5. **A change of role:** the wisp handles repeated placement while the player explores and improves the arrangement.
6. **A visible destination:** restoring the bridge reveals stronger fragments and the far shrine.
7. **Closure:** the final memory answers the opening mystery; the chapter ends while the economy remains playable.

This is an engagement hypothesis, not a claim of proven retention. The MVP needs human playtests before expanding its economy or content. There are no daily streak penalties, paid purchases, random paid rewards, or forced advertising.

## Playthrough and controls

1. Hold left click near the first blue fragment. Move it onto the nearest ring with WASD and release.
2. Gather more free fragments, or call Starlight from the upgrade panel.
3. Place the green Life fragment close to blue producers; unlock Resonance II and attune four fragments.
4. Restore the bridge for 300 Essence. Buy Astral Strength so you can handle violet Ancient Hearts.
5. Attune both Ancient Hearts on the far island. The wisp can recall them after Strength is unlocked.
6. Stand near the far shrine with 1,200 Essence and press E or use the final upgrade-panel action.

| Input | Action |
|---|---|
| WASD | Camera-relative movement |
| Hold left mouse near a fragment | Grab and move it |
| Release left mouse | Release; snap to a nearby free altar when clear |
| Arrows / middle drag / screen edges | Pan camera |
| Scroll | Zoom |
| Hold Space | Center camera on player |
| 1 / 2 / 3 / 4 | Call Starlight / Resonance / Strength / Call Life |
| E near the shrine | Complete the chapter when requirements are met |

Essence remains visible in the HUD and menus. The Skill Tree button and Tab/K shortcuts open all powers with prices and prerequisites. Esc opens pause with a working Controls view; Music and SFX remain disabled placeholders. Opening pause saves, and Save & Return to Title preserves the current journey. New Journey is available in the title menu and Memories with a second confirmation click.

## Economy and progression

These are the implemented starting values, editable through `Assets/LastSpark/MVP Balance.asset`.

| System | Implemented behavior |
|---|---|
| Starlight | 1 Essence/sec while attuned |
| Ancient Heart | 8 Essence/sec while attuned; requires Astral Strength to move |
| Life | No direct production; +50% to each generator within 4.5m; bonuses from multiple Life fragments add |
| Resonance | Total production × 1.4 per level; ten levels |
| First activation | One-time 25 Essence reward and first memory |
| Call Starlight | Starts at 20 Essence; price increases by ×1.38 per purchase, rounded up |
| Call Life | Starts at 90 Essence; price increases by ×1.8 per purchase |
| Resonance purchase | Starts at 35 Essence; price increases by ×1.85 per level, rounded up |
| Strength purchase | 60, 120, 240 Essence; increases normal and pulling speed |
| Wisp | 220 Essence; requires three attuned fragments; recalls one eligible loose fragment every 12 seconds |
| Bridge | 300 Essence; four active altars and Resonance II required |
| Final shrine | 1,200 Essence; player nearby and two Ancient Hearts attuned on the far island |
| Chapter completion | Total production ×2; final memory |

Holding a fragment immediately stops its production. Moving a Life fragment changes its neighbors' production. Purchases reject insufficient currency and unmet requirements without spending anything. Fragment calling is limited when too many loose fragments or too little altar capacity would make the island crowded.

The automated route's first measured economy-only waiting was approximately 127 seconds, excluding walking, arranging, reading, and deciding. This is not a measured human completion time. The intended first-session length is roughly 5–10 minutes and should be tuned after observation.

## Placeholder replacement inventory

P0 means replace before a public vertical slice; P1 improves atmosphere and reward clarity; P2 can wait until the loop has been validated.

| Priority | Current placeholder / location | Replace with | Gameplay requirements |
|---|---|---|---|
| P0 | Existing character under `Player/Visual` | A small astral wanderer: readable silhouette, cloak or celestial garments, a visible source of inner light | Approximately 1.8m tall. Preserve idle/run/pull states and humanoid retargeting. Keep gameplay movement on the parent controller. |
| P0 | Blue cube in `Starlight Fragment.prefab` | A fractured luminous starstone with a bright core and a distinct star-shaped rune | Readable at the default camera distance. Keep a roughly 1.2m collision footprint and clear grabbing faces. |
| P0 | Green cube in `Life Fragment.prefab` | An emerald seed-crystal containing growing roots, leaves, or flowing particles | Distinct shape as well as color. Its support role and influence area must remain clear. |
| P0 | Violet cube in `Ancient Fragment.prefab` | A dense, weathered celestial heart with embedded rings or ancient inscriptions | Visually heavier than Starlight. Preserve the current collision footprint until handling is retested. Show locked and movable states. |
| P0 | Two flat cylinders forming each altar | An ancient stone socket with an engraved circular channel and inset constellation markings | Approximately 1.9m outer diameter; no raised collision that blocks placement. Clearly distinguish empty, valid-preview, and occupied states. |
| P0 | Flat island slabs and rim cubes | Floating basalt landforms with broken undersides, eroded stone edges, and sparse ruined architecture | Home walkable area: about 24×20m; far area: about 20×18m. Preserve navigable space, altar locations, and edge protection. |
| P0 | Purple barrier and rectangular bridge | A broken starbridge that assembles into a luminous walkable path | About 5m wide and 9.2m long. Keep the locked barrier solid until the path is ready; retain bridge collision and side protection. |
| P0 | Final shrine cylinder and floating sphere | A ceremonial seed cradle surrounding the unborn universe: layered rings, suspended seed, and an awakening state | Keep a clear interaction area within 3.5m. Add dormant, ready, and completed presentations. |
| P0 | Canvas UI panels, text-only skill cards, title and pause menus | A mystical UI art set: Essence icon, title wordmark, power icons, decorative frames, and constellation connections | Keep the working Canvas scaling, persistent currency, input blocking, pause behavior, shortcuts, prices, prerequisites, save and restart controls. Music/SFX settings remain placeholders until audio integration. Validate small screens before adding ornamentation. |
| P1 | Small green sphere named `Placeholder - Wisp` | A floating spirit with a distinct silhouette, an orbiting trail, and recall gestures | This ability uses magical recall, not ground navigation. Show departure, destination, and arrival so the teleport feels intentional. |
| P1 | Flat cylinder and large `ORIGIN WELL` label | A small summoning basin or rift with an obvious arrival area | Leave all eight arrival positions clear. Calling a fragment must not hide or overlap it behind the shrine. |
| P1 | Six cylinder pillars and flat world labels | Modular ruined pillars, carved inscriptions, and subtle environmental wayfinding | Keep decorative meshes outside hauling lanes; add colliders only where they should actually obstruct movement. |
| P1 | Gold mini-cubes/sphere above fragments | Distinct floating glyphs for star, life, and ancient power | Use shape to supplement the blue/green/violet color system. Avoid making them appear to be separate collectible objects. |
| P1 | Thin Life radius line | A soft runic influence circle with short connections to boosted generators | Preserve the exact 4.5m radius and show it primarily when inspected or held to avoid permanent clutter. |
| P1 | Point-light activation and uniform chime | An attunement pulse, light flowing through carved channels, a brief particle burst, and a short layered activation sound | Trigger on successful attunement. Scale milestone rewards without overwhelming normal interactions. |
| P1 | Text-only memory notices | Illustrated memory panels or short in-world visions with restrained sound and optional narration | Preserve the persistent journal so the player can reread notices. Avoid blocking movement with long, unskippable sequences. |
| P2 | Solid navy camera background | A stylized cosmic void with distant stars, dead constellations, and slow-moving celestial silhouettes | Maintain contrast with fragments and rings. Avoid bright background detail that competes with the play area. |
| P2 | One generated sine-wave chime; no ambience/music | A cohesive audio set: grab/release, stone dragging, fragment discovery, upgrade, bridge restoration, wisp recall, shrine awakening, ambience, and music layers | Include independent volume controls when the audio set is integrated. Milestone sounds should differ from routine placement. |

## Asset integration contracts

- Keep the **gameplay root and identifiers** of fragment prefabs. `AstralFragment.id` is used for persistence; authored fragment IDs should remain stable across art revisions.
- Keep `PullableBox`, `AstralFragment`, a kinematic `Rigidbody`, and the root `BoxCollider`. Current scripts expect the primary material renderer on the fragment root. For a multi-mesh asset, explicitly update highlight handling to address all intended renderers.
- Use URP-compatible materials. Runtime highlight and emission feedback use `_BaseColor` and `_EmissionColor`.
- Keep the collision footprint independent of decorative shards or glyphs. Decorative child objects should not create unexpected collision obstacles.
- Altar seating places the fragment center at **0.62m above the altar's origin**, matching the current 1.2m box. Adjust that offset alongside collider changes if an art asset changes height.
- Keep `Player`, its CharacterController, and its scripts. Replace the model beneath `Visual`; keep humanoid idle/run/pull clips and root motion disabled. The current imported model uses a normalization scale; a final asset should arrive consistently scaled in meters.
- Keep bridge, barrier, far-fragment group, shrine, and altar references assigned on `LastSparkGame`. Art replacement must preserve their active/inactive behavior.
- Keep the far island's eight altars marked `outerIsland`; that distinction is used by the ending and wisp.
- Check silhouettes and readability from the existing camera's 55-degree pitch and default distance of 20m.

## Persistence and replay

The save stores currency, lifetime production, upgrades, unlocked powers, chapter status, player position, and every fragment's identity/type/position. Altar occupancy and production are recalculated after loading.

The active file is `last-spark-v1.json` in Unity's `Application.persistentDataPath` for Lighting Blocks. A temporary file is written first, with the prior save retained as `.bak`. If the primary save is malformed, loading attempts the backup. Offline income uses the smaller of saved and reconstructed production, capped at two hours at half rate.

Starting a new journey preserves the previous active save with a `.previous` suffix, clears the active save and its backup, and reloads the scene. The confirmation is part of the game UI.

## Validation and limits

The automated check exercises physical grabbing and altar snapping; the first-light reward; time-based production; Life adjacency; progression costs and requirements; wisp recall; bridge state; heavy-fragment strength gating; the shrine ending; JSON save round-tripping; invalid-save rejection; offline limits; UI input blocking; mouse press/release; and camera bounds. Save-file tests use an isolated file in the project's Library folder, not the player's real save.

See the accompanying `Last-Spark-Gameplay-Checks.txt` for the final execution results. The interface update passes 94 checks, including title/continue flow, visible currency updates, pause and controls navigation, disabled audio options, skill purchases, actual Esc/Tab/K/F1/E input, skill-description fit, explanatory failure messages, and successful shrine activation without duplicate charges. Rendered title, gameplay, skills, pause, controls, and feedback previews are included separately. F1 expands/restores the Game view in the editor; a built player uses borderless fullscreen/windowed modes. Testing was performed in the Unity Editor; the standalone fullscreen branch has not been tested in a player build, and no human retention study is claimed.

Known scope limits:

- One complete chapter, with continued production after its ending; no additional chapters or prestige layer yet.
- Flat-ground, upright fragment handling; no physical stacking, fragment rotation, free flight, or terrain traversal.
- The wisp teleports fragments to clear eligible altars. It does not buy fragments, optimize layouts, or navigate around obstacles.
- World labels, the HUD, sounds, lighting feedback, and all new scenery are intentionally placeholders.
- Economy defaults are tuned for a short MVP. Skill prices, bonus radius, and wisp timing labels read the current balance values.
- No localization, controller support, cloud saves, or accessibility settings menu in this MVP.

## Recommended next validation

Watch three to five first-time players without explaining the economy. Record time to first attunement, first self-directed upgrade, bridge restoration, and chapter completion. Note the first moment of confusion or boredom. Confirm that players notice the Life radius and understand why moving it changes income.

Prioritize interaction feel, fragment/altar readability, and the bridge/shrine reward sequence before expanding the asset count. The next content investment should follow what players actually enjoy: constellation arrangement, magical exploration, or automation.
