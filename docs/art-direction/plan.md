# The Spirit Path — Art Direction Gap Closure Plan

_Last updated: 2026-09-17_

Closes the gap between the current Unity build and the target concept art: a night-time floating-island diorama with a glowing wisdom tree, warm lantern practicals, and a cool volumetric void.

---

## 1. Gap analysis

The target is a night-time floating-island diorama: a glowing wisdom tree, warm lantern practicals, a cool indigo void, and a chain of emissive rune blocks bridging two islands. The current build is a single curved Unity Terrain plane under flat ambient light with no post-processing.

**Roughly half the gap has nothing to do with assets.** Buying every pack in this doc and dropping it in as-is would not produce the target image. Ranked by how much each factor costs us:

| # | Problem in the current build | Fix lives in |
| --- | --- | --- |
| 1 | No post-processing at all: no tonemapping, no bloom, no depth of field, no colour grading | Section 4 |
| 2 | Camera is wide-FOV and near-top-down; target is low-FOV at roughly 40 degrees pitch | Section 7 |
| 3 | Flat ambient lighting, no shadow reading, no emissive materials | Section 5 |
| 4 | Single Unity Terrain plane: no island silhouettes, no undersides, no depth layers | Section 6 |
| 5 | Grass and ground have no normal or AO maps, so props look pasted on rather than seated | Sections 4 and 6 |
| 6 | Prop density is about one detail type across the whole map, against roughly ten types per few square metres in the target | Section 6 |
| 7 | Default Unity labels instead of a designed HUD | Section 8 |
| 8 | Asset quality and style mismatch | Sections 2 and 3 |

Read that order as the work order. Section 9 turns it into a tiered roadmap with effort and impact estimates.

---

## 2. Free assets

### 2.1 Rocks and island bodies

The floating islands are sculpted rock masses with undersides. Unity Terrain cannot produce overhangs, so these are kitbash sources for mesh islands.

| Asset | Notes | Link |
| --- | --- | --- |
| Free Pack – Rocks Stylized (PolyOne) | Cliffs and rocks, stylized, modular | https://assetstore.unity.com/packages/3d/environments/free-pack-rocks-stylized-316250 |
| Stylized Rock Pack (Elijah Cobden) | Pay-what-you-want; ships URP and HDRP unitypackages plus raw FBX | https://elijahcobden.itch.io/stylized-rock-pack/purchase |
| Free Stylized Nature Environment (Ink Phantom) | Older but a solid base kit | https://assetstore.unity.com/packages/3d/environments/free-stylized-nature-environment-96371 |

### 2.2 Vegetation and trees

| Asset | Notes | Link |
| --- | --- | --- |
| Low Poly Trees Pack Lite (Hiroba) | Free stylized trees, oaks and pines, plus plants and rocks | https://assetstore.unity.com/packages/3d/vegetation/trees/low-poly-trees-pack-lite-free-stylized-nature-environment-assets-295464 |
| Foliage Pack Free | General ground cover, 1,724 favourites | https://assetstore.unity.com/packages/3d/vegetation/foliage-pack-free-66155 |
| Free Trees (Ada_King) | Filler background trees | https://assetstore.unity.com/packages/3d/vegetation/trees/free-trees-103208 |
| Stylized Trees Pack (Elijah Cobden) | Pay-what-you-want, URP package included | https://elijahcobden.itch.io/stylized-trees-pack/purchase |
| Stylized Nature Pack (Elijah Cobden) | Pay-what-you-want companion to the above | https://elijahcobden.itch.io/stylized-nature-pack/purchase |
| stylized-vegetation (GitHub, MIT) | URP project with custom toon lighting shader graphs by Cyanilux, LODs for tree, grass and bush. Worth having as a shader reference even if we ship none of the models | https://github.com/ProblematicToucan/stylized-vegetation |

### 2.3 Mushrooms and ground detail

The glowing purple and blue mushroom clusters are a signature detail of the target.

| Asset | Notes | Link |
| --- | --- | --- |
| Mushroom Flora Pack | Free, 137 favourites | https://assetstore.unity.com/packages/3d/environments/mushroom-flora-pack-30024 |
| PBR Mushroom Pack Lite | Free sample, two mushroom types with PBR textures, around 400 tris each | https://assetstore.unity.com/packages/3d/vegetation/pbr-mushroom-pack-lite-56263 |

Make them glow in-engine rather than buying a glowing pack: duplicate the material, enable Emission, set an HDR colour at roughly +2 to +3 EV.

### 2.4 Lanterns and light sources

The hanging warm orbs and lamp posts carry an enormous amount of the target's mood.

| Asset | Notes | Link |
| --- | --- | --- |
| Modular Medieval Lanterns | **Best free pickup on this list.** Five lamps, seven lamp posts, three candle types with three glow variants, sprite flame animation, chandelier, candlesticks, prefabs included | https://marketplace.unity.com/packages/3d/environments/historic/modular-medieval-lanterns-85527 |
| Lantern Pack 01 (monafaye) | Six detailed, cleanly UV-mapped hanging lanterns, pay-what-you-want | https://monafaye.itch.io/lantern-pack-01/purchase |
| STYLIZED – Fantasy Armory | Free, 230+ low-poly models including torches, chains, lamps, fences, rocks and plants, two demo scenes | https://assetstore.unity.com/packages/3d/environments/fantasy/stylized-fantasy-armory-low-poly-3d-art-249203 |

### 2.5 VFX and particles

| Asset | Notes | Link |
| --- | --- | --- |
| Magic Effects FREE | Sparks, magic circles, portals, stylized particles | https://assetstore.unity.com/packages/vfx/particles/spells/magic-effects-free-247933 |
| Free Game VFX – Magic Circle (URP) | For the stone circle under the tree | https://assetstore.unity.com/packages/vfx/particles/free-game-vfx-magic-circle-urp-344984 |
| Free Pack – Fire Effects (PolyOne) | Lantern flames, sparks, embers | https://assetstore.unity.com/packages/vfx/particles/free-pack-fire-effects-318184 |
| Free Fire VFX – URP (Vefects) | Higher-quality flames | https://assetstore.unity.com/packages/vfx/particles/fire-explosions/free-fire-vfx-urp-266226 |

### 2.6 Shaders for the glowing path blocks

The translucent rune cubes are a Fresnel plus emission plus scrolling-texture shader, not a model.

| Asset | Notes | Link |
| --- | --- | --- |
| Free Hologram Shader for URP (VOiD1) | Scanline presets, custom scanline support, colour control. Needs URP and Shader Graph | https://void1gaming.itch.io/free-hologram-shader-for-unity-urp/purchase |
| Hologram Shaders for URP (ereneker) | Two shaders, adjustable colours, scanline and glitch amount | https://ereneker.itch.io/hologram-shaders-for-unity-urp |
| Hologram URP Shader | PBR Shader Graph with exposed base colour, Fresnel rim colour, texture, scroll speed and tiling | https://assetstore.unity.com/packages/vfx/shaders/hologram-urp-shader-181628 |
| Free Stylized Fire Shader for URP | Alternative lantern flame | https://void1gaming.itch.io/free-stylized-fire-shader-for-unity-urp/purchase |

### 2.7 Volumetric lighting (free)

This is the mist under the islands and the haloes around the lanterns.

| Asset | Notes | Link |
| --- | --- | --- |
| Unity-URP-Volumetric-Light (CristianQiu) | Unity 2022.3 and Unity 6+, main and additional lights, compatible with URP Render Graph. Install via Git URL, add the renderer feature, add a Volume override at Custom → Volumetric Fog, and add a VolumetricAdditionalLight component to contributing spot and point lights. No WebGL or multipass VR | https://github.com/CristianQiu/Unity-URP-Volumetric-Light |
| MicroAVL | Performance-focused volumetric light and fog shader, pay-what-you-want | https://akidevcat.itch.io/microavl/purchase |
| Volumetric Fog URP Shader Graph | Cheap local fog volumes | https://aetuts.itch.io/volumetric-fog-unity-lwrpurp-shader-graph/purchase |

### 2.8 Sky

| Asset | Notes | Link |
| --- | --- | --- |
| Fantasy Skybox FREE (Render Knight) | Includes night variants | https://assetstore.unity.com/packages/2d/textures-materials/sky/fantasy-skybox-free-18353 |
| Free HDR Skyboxes Pack (BG Studio) | HDR, which matters for correct ambient | https://assetstore.unity.com/packages/2d/textures-materials/sky/free-hdr-skyboxes-pack-175525 |

### 2.9 Character

| Asset | Notes | Link |
| --- | --- | --- |
| FREE Low Poly Human – RPG Character | Placeholder rigged humanoid | https://assetstore.unity.com/packages/3d/characters/humanoids/fantasy/free-low-poly-human-rpg-character-219979 |

### 2.10 Built-in, install from Package Manager

- **Cinemachine** — the target's framing is not achievable by hand-placing a Transform
- **Shader Graph** — required by most shaders above
- **ProBuilder** — for blocking out island shapes before committing to sculpting
- The Volume post-processing system already ships with URP

---

## 3. Paid assets

### 3.1 Highest impact per dollar

**RPG – Stylized Fantasy Environment** (Rhinox Studio) is the closest single pack to the target art. 200 unique prefabs, 38 leaves and bushes, 4 particle prefabs, 48 plant prefabs, 24 rocks, 52 ruin prefabs, 27 tree types, one skybox and five custom shaders. Compatible with Built-in, URP and HDRP; requires the ShaderGraph package. The ruins and the glowing forest cover both the stone circle and the tree area.
https://assetstore.unity.com/packages/3d/environments/fantasy/rpg-stylized-fantasy-environment-262345

**Stylized Rocks Pack | Floating Islands** (Meanwhile On The Moon) is sculpted rock masses built explicitly for floating islands.
https://assetstore.unity.com/packages/3d/environments/stylized-rocks-pack-floating-islands-set-of-sculpted-stones-247767

**Stylized Grass Shader** (Staggart Creations). Grass is currently the weakest material in the build. Gives wind animation, per-object and per-vertex colour variation to break up repetition, bending and flattening from trails, objects and particles, translucency rendering, terrain colour blending, and grass models designed for mass placement. SRP Batcher and GPU Resident Drawer compatible.

- Unity 6 version: https://assetstore.unity.com/packages/vfx/shaders/stylized-grass-shader-for-unity-6-357954
- Unity 2021–2023 version: https://assetstore.unity.com/packages/vfx/shaders/stylized-grass-shader-for-unity-2021-2023-143830

### 3.2 Volumetrics and atmosphere

> **Do not buy Aura 2.** It is the most-recommended volumetric asset online, but it does not support SRPs, LWRP, URP or HDRP, and was developed and tested on Windows and DirectX11 only. It will not work in this project.

**Buto – Volumetric Fog and Volumetric Lighting for URP** (OccaSoftware), $44. Physically based scattering; volumetric lighting and shadow so objects occlude the fog; roughly 0.2 ms on a GTX 1080 equivalent with depth-aware upscaling; five configurable volumetric noise styles; atmospheric height fog; stylized fog options with colour ramps and light and shadow intensity sliders; volumetric point lighting for additional lights; fog density masks; integrated with Unity's Volume Component system. Version 7.12.7, updated March 2026.
https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/buto-2022-volumetric-fog-volumetric-lighting-height-fog-urp-258881

**Volumetric Fog & Mist 2** (Kronnect) is the main alternative. Fog areas of any size placed like gameobjects, fog voids, native directional light support with shadow cascades, a fast point lights option casting light on fog from 16 or more point lights, terrain fit, depth blur and light scattering, and Adaptive Probe Volumes support in Unity 2023.1+ that injects GI into the volumetric fog. Supports Built-in and URP across forward, forward+ and deferred, is recommended by Unity as a volumetric solution in URP, and supports Render Graph.
https://assetstore.unity.com/packages/slug/162694

**Atmospheric Height Fog** (BOXOPHOBIC), around $20–25. Height fog with directional light and animated noise, giving a volumetric-like effect at lower cost; supports Standard, URP and HDRP; built with Amplify Shader Editor so the shaders are editable. Caveats: needs Depth Texture plus one of Opaque Texture, HDR, MSAA, Post Processing, or a Render Scale other than 1; and it is not a true volumetric solution. The budget choice.
https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/atmospheric-height-fog-optimized-fog-for-consoles-mobile-and-vr-143825

### 3.3 Post-processing

**SC Post Effects Pack** (Staggart Creations) adds 30+ screen effects on top of URP's stack, including light streaks, edge detection and colour grading helpers.
https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/sc-post-effects-pack-108753

**Beautify 3** (Kronnect), around $30. Twenty integrated effects sharing a single render pass, with improved bloom, anamorphic flares, outline, chromatic aberration and depth of field; ships separate Built-in and URP packages. Its bloom can be applied to specific objects and attenuated by depth, which is exactly what we want so the path blocks bloom hard while the grass does not.
https://www.fab.com/listings/0ecc9052-1a61-4cdf-9892-196b21bdebe0 — also on the Unity Asset Store under Kronnect

### 3.4 Environment packs worth considering

| Asset | Why | Link |
| --- | --- | --- |
| Forest – Stylized Fantasy Environment (Holotna) | 18 game-ready assets, Shader Graph shaders with RGB and BW textures and fully customizable colours, wind support, 2–4 LODs, Built-in/URP/HDRP. Recolourable is the key feature | https://assetstore.unity.com/packages/slug/294436 |
| URP Stylized Nature Essentials (Purple Sky) | Trees, grass, water, clouds, sun rays, wind; built for URP | https://assetstore.unity.com/packages/3d/environments/urp-stylized-nature-essentials-325327 |
| Stylized Nature & Environment MEGA PACK | Large rock and tree library | https://assetstore.unity.com/packages/3d/environments/stylized-nature-environment-mega-pack-326646 |
| Environment Pack – Stylized Nature (PolyOne) | Matches the free PolyOne rocks pack stylistically | https://assetstore.unity.com/packages/3d/environments/environment-pack-stylized-nature-400868 |
| Stylized Rock & Cliff Mega Pack | 44 cliff variations for island undersides | https://assetstore.unity.com/packages/3d/props/stylized-rock-cliff-mega-pack-44-variations-400134 |
| Stylized Nature – Rocks & Cliffs Pack (Arkerion) | Recent, URP-ready | https://assetstore.unity.com/packages/3d/environments/stylized-nature-rocks-cliffs-pack-364396 |
| Stylized Interactive Grass Kit (URP) | Zelda-style interactive stylized foliage, optimized for URP | https://assetstore.unity.com/packages/3d/environments/landscapes/stylized-interactive-grass-kit-urp-384710 |
| Stylized Ancient Ruins (Leartes), $59.99 | 184 unique meshes, AAA quality, controllable material parameters, example levels. For the stone circle and ruined pillars | https://leartesstudios.gumroad.com/l/stylizedancientruins_unity |
| Glowing Mushroom Forest, $8.50 | 5 giant mushroom trees, 5 ground mushrooms, mycelium ground materials, an overgrown stone light, customizable emissive colours, 2-level LODs | https://assetstore.unity.com/packages/3d/environments/fantasy/glowing-mushroom-forest-82229 |

### 3.5 Sky, lanterns, characters, UI

| Asset | Notes | Link |
| --- | --- | --- |
| HDRI Night Fantasy Skyboxes | | https://assetstore.unity.com/packages/2d/textures-materials/sky/hdri-night-fantasy-skyboxes-307826 |
| Nebula Skyboxes (Road Turtle) | | https://assetstore.unity.com/packages/2d/textures-materials/sky/nebula-skyboxes-219924 |
| Lantern – Mega Pack, $9.80 | 100+ assets: wall lanterns, hand lanterns with spark particles, lampposts with flares, candles, a chandelier with flame effect. Single 1024 texture for one drawcall | https://marketplace.unity.com/packages/3d/props/interior/lantern-mega-pack-18484 |
| Fantasy lamps | 4K emissive maps, ten unique objects with four emissive variants each | https://assetstore.unity.com/packages/3d/props/fantasy-lamps-182238 |
| Fireflies Effect VFX Graph URP | | https://assetstore.unity.com/packages/vfx/fireflies-effect-vfx-graph-urp-197348 |
| Stylized Low Poly Wizard Pack | | https://assetstore.unity.com/packages/3d/characters/stylized-low-poly-wizard-pack-383358 |
| Low Poly Wizard Pack (SKULLVERTEX) | | https://assetstore.unity.com/packages/3d/characters/humanoids/fantasy/low-poly-wizard-pack-167525 |
| Fantasy Trees (TeamQueyras) | | https://assetstore.unity.com/packages/3d/vegetation/trees/fantasy-trees-324646 |
| Fantasy RPG UI Pack | 50+ board bases, 150+ decorative elements, 84 icons, health and mana bars, 4K PNG and PSD, editable | https://marketplace.unity.com/packages/2d/gui/fantasy-rpg-ui-pack-269061 |
| Dark Lands RPG UI Kit | Hand-drawn 4K interface, three HUD types, two interaction panel types, 550+ UI elements, 41 layered PSDs | https://marketplace.unity.com/packages/2d/gui/icons/dark-lands-rpg-ui-kit-229815 |

---

## 4. Rendering pipeline

### 4.1 Switch to Forward+ first

The target has roughly 25 to 40 individual light sources visible: lanterns, hanging orbs, mushrooms, path blocks, the tree. In URP's default Forward path this is impossible, because Forward allows a maximum of nine real-time lights per object (one main light plus eight additional lights). Forward+ has no per-object limit and only a per-camera limit applies; it divides the screen into tiles, identifies which lights affect which tiles, and lights each object only from the lights in its tile.

Set this on the Universal Renderer asset: **Rendering Path → Forward+**.

Two things to know:

- With Forward+, the _Additional Lights → Per Object Limit_ property is ignored and reflection probe blending is always on. The per-camera limit is 256 on desktop, 32 on mobile, and 16 on OpenGL ES 3.0 and earlier.
- Transparent objects still render through the Forward pass with the nine-light maximum. The glowing path blocks should therefore use an opaque or alpha-clipped material with emission rather than true transparency — or accept limited lighting on them, which is fine since they are self-lit.

Reference: https://docs.unity3d.com/6000.0/Documentation/Manual/urp/rendering/forward-rendering-paths.html

### 4.2 Post-processing volume

There is currently none. This is the single biggest win in the whole plan. Create a global Volume and set:

| Override | Settings |
| --- | --- |
| **Tonemapping** | ACES. Non-negotiable. Without it, HDR emissives clip to white and the night scene reads muddy grey instead of deep indigo |
| **Bloom** | Threshold 0.9–1.1, Intensity 0.4–0.8, Scatter 0.6–0.75, tint slightly cool-blue, High Quality Filtering on. The halo on every lantern in the target is bloom on HDR-emissive materials, not a glow texture |
| **Color Adjustments** | Post Exposure +0.2 to +0.5, Contrast +10 to +20, Saturation +5 to +15 |
| **Split Toning** | Shadows toward blue-violet (around #3A2E6B), Highlights toward warm amber (around #FFC27A), Balance about -10. This warm/cool split is the defining colour relationship of the target |
| **Shadows Midtones Highlights** | Crush shadows slightly, lift highlights. Keeps the void between islands genuinely dark |
| **Depth of Field** | Bokeh mode. Focus Distance at the character, Aperture f/2.8–f/5.6, Focal Length 80–120. The background islands in the target are noticeably soft; this one effect creates the diorama feel |
| **Vignette** | Intensity 0.25–0.35, Smoothness 0.4 |
| **Film Grain** | 0.15–0.25, very subtle. Hides banding in the dark gradients |
| **Chromatic Aberration** | 0.05–0.1, barely on |

### 4.3 URP asset and camera settings

- **HDR: on** — required for emissive bloom to work correctly
- **Anti-aliasing:** SMAA High on the camera, or TAA on Unity 6
- **Depth Texture: on** and **Opaque Texture: on** — fog assets and DoF need these
- **Soft Shadows: on**, Shadow Distance 60–100 m, 2–4 cascades
- **SSAO renderer feature: add it.** There is no contact shadowing at all right now, which is why props look pasted onto the ground rather than sitting in it. Intensity around 0.5, Radius 0.2–0.3
- **Colour Space: Linear** — verify in Player Settings. If the project is in Gamma, nothing above will look right

---

## 5. Lighting

### 5.1 Directional moonlight

- Colour: cool blue around `#8FB4FF`, or Color Temperature mode at 9000–12000 K
- Intensity: 0.3–0.7. Deliberately weak — it is a moon
- Angle: about 35–45 degrees elevation, coming from behind and slightly left so island edges catch a rim
- Shadows: Soft, Strength 0.6–0.75. Not 1.0; night shadows are not black in a scene full of fill light

### 5.2 Environment lighting

- Source: Skybox, Intensity Multiplier **0.2–0.5**. The current scene reads as if ambient is flat at 1.0
- If using Gradient instead: Sky deep indigo `#1A1B3D`, Equator `#2A2545`, Ground near-black `#0A0A14`
- Generate Lighting after changing this. Ambient is what makes the grass read blue-green rather than flat khaki

### 5.3 Practical lights (the lanterns)

- One point light per lantern, colour 2000–2500 K warm, around `#FFB661`
- Range 4–8 m, Intensity 3–12 depending on the project's unit setup
- **Shadows off on all but two or three hero lanterns.** Dozens of shadow-casting point lights will destroy the framerate and the difference is not visible
- Match every light to an emissive mesh. A light with no visible source looks wrong; an emissive mesh with no light looks flat. Both are needed

### 5.4 The Wisdom Tree

This is the focal point and needs special treatment:

- One large point light at the trunk base, lavender-white, range 20–30 m, shadows on
- Two or three smaller fill lights in the canopy
- Emissive material on the trunk and branch geometry at HDR intensity +3 to +5 EV
- A subtle upward-facing spot light from the ground to underlight the canopy
- Use **Rendering Layers** to stop this light spilling onto distant islands

### 5.5 The path blocks

- Emissive material, HDR colour between `#3A6BFF` and `#7FB0FF`, intensity +2 to +4 EV
- One small point light per two or three blocks, not per block. Range 3 m, no shadows
- Animate emission intensity on a slow sine wave for the breathing feel
- The unbuilt ghost blocks in the target are a wireframe or Fresnel-only material at low alpha with no light attached

### 5.6 Baked GI

Everything static should be lightmapped. Realtime lights only for the ones the player interacts with.

Use **Adaptive Probe Volumes** for dynamic object lighting rather than hand-placed light probes. If we go with Kronnect's fog, APVs are supported in Unity 2023.1+ and inject GI directly into the volumetric fog, which is a meaningful quality gain for free.

---

## 6. Environment topology

This is the part most people skip, and it is why a scene never matches the concept even after all the assets are in.

### 6.1 Retire the Unity Terrain

The current scene is a single curved terrain plane with no silhouette. Unity Terrain cannot produce overhangs or undersides, which is what a floating island fundamentally is. We need mesh islands.

Build method:

1. Block out island footprints with ProBuilder to nail scale and gameplay distances first
2. Export to Blender, sculpt the top surface and a tapering rock keel underneath
3. Or kitbash: a flat top mesh with four to eight rotated and scaled rock meshes stacked underneath, seams hidden with foliage and small rocks at the rim

### 6.2 Composition rules from the target

- **Three depth layers.** Foreground island at full detail and in focus; midground bridge and character as the hero focus; background islands silhouetted, desaturated 40–60%, small, soft from DoF. The current scene is one layer, which is why it reads flat
- **Hard silhouette against void.** The islands end. Nothing is below them. This is what makes them read as floating rather than as a hill
- **Vertical anchors.** The tree, the lamp posts and the hanging light strings pull the eye upward. Everything in the current scene sits at ground level
- **Multiple ground heights.** There are steps, a raised stone circle, a low ledge, a sunken path. Add three to five elevation changes of 0.3–1.0 m on the main island
- **Composed negative space.** The dark gap between islands is deliberate, roughly a third of the frame

### 6.3 Prop density

Count the distinct prop types near the tree in the target: stone tiles, moss patches, ferns, three mushroom types, small purple flowers, pebbles, fallen logs, hanging lanterns, ground lanterns, ivy. That is about ten types within a few square metres. The current scene has one grass detail type over the entire map.

Target roughly **8–15 scattered prop instances per square metre** in hero areas, dropping to 2–3 in traversal areas. Randomise Y rotation, apply plus or minus 20% scale variation, and add slight random tilt on X and Z so nothing reads as grid-placed.

### 6.4 Ground materials

The grass currently has no normal map and no AO, which is why it reads as flat paint. Minimum requirement is albedo plus normal plus smoothness, and a second ground material (dirt or stone) blended by vertex colour or a splat map. The worn dirt path in the target does a lot of readability work and should be replicated.

---

## 7. Camera

The current camera is a high, wide-FOV perspective looking almost straight down. The target is a low-FOV framing at roughly 40 degrees pitch and 45 degrees yaw.

- Install **Cinemachine** from Package Manager
- `CinemachineCamera` with Position Composer, Follow target set to the player
- **Field of View: 20–30 degrees.** This is the big one — the project is most likely at 60
- Pitch 38–45 degrees, Yaw 45 degrees
- Distance 25–40 m
- Enable **Physical Camera**: Focal Length 85–135 mm, Sensor 35 mm, Aperture f/2.8–f/5.6
- Damping 1.0–1.5 on all axes so the camera drifts rather than snaps

Do not use true orthographic. A low-FOV perspective gives the isometric read _and_ real depth of field, which orthographic cannot do properly.

Also: set the Game view to a fixed **16:9** instead of Free Aspect. Composition cannot be judged at Free Aspect, and the current screenshots are a different aspect ratio from the target art.

---

## 8. UI

The current HUD is unstyled Unity labels. The target's HUD is:

- **Dark panels** at about 85% opacity with 8–12 px rounded corners and a 1 px warm-gold border
- **Two type styles:** a wide-tracked serif for headers (the location title), a clean sans for body text
- **Iconography everywhere** — particle glyph, block glyph, map, settings, journal
- **Circular icon buttons** top-right, not text buttons
- **World-space prompt** anchored to the target block, not screen-space
- **A diamond ornament** flanking the location title, and a small icon badge on the objective card

Layout to match: resource panel top-left, location title top-centre with ornament, circular icon buttons top-right, objective card bottom-left, key prompts bottom-centre, interaction prompt in world space near the block.

### Dev artifacts to strip before any screenshot

- The "EXPANDED GAME VIEW / Press F1" overlay
- The Unity editor toolbar — use Maximize On Play
- The Windows activation watermark

---

## 9. Roadmap

| Tier | Work | Cost | Effort | Impact |
| --- | --- | --- | --- | --- |
| **1** | Forward+, ACES tonemapping, full post-processing volume, camera FOV and angle, HDR skybox, Linear colour space, SSAO | Free | ~1 day | **~50% of the gap** |
| **2** | Rebuild terrain as discrete mesh islands with undersides and three depth layers | Free | 3–5 days | ~20% |
| **3** | Lighting pass: moonlight, lantern practicals, emissive materials, baked GI | Free | 2 days | ~10% |
| **4** | Asset swap: rocks, foliage, mushrooms, lanterns, tree | $0–80 | 2 days | ~10% |
| **5** | Volumetric fog | $0–44 | 1 day | ~5% |
| **6** | UI redesign | $0–30 | 2 days | ~5% |

Start with tier 1. It costs nothing, takes a day, and the before/after screenshot will be dramatic enough to tell us whether the rest is worth the time.

**Capture a reference screenshot before touching anything**, from a fixed camera position, at 16:9, and retake it from the same position after every tier. Without that the progress is impossible to judge and it becomes very easy to spend a week on work that changed nothing.

---

## 10. Prompt for Claude Code

Paste everything inside the code block below into Claude Code, in the repo root of the game project. It asks for a plan, not edits, so it is safe to run before deciding anything.

```
I'm closing the gap between how my game currently looks and a target concept art
direction. The target is a night-time floating-island diorama: a glowing wisdom
tree, warm lantern practicals, a deep indigo void, cool volumetric mist under the
islands, and a chain of emissive rune blocks bridging two islands. Current state is
a single curved Unity Terrain plane under flat ambient light with no
post-processing.

I have an art-direction plan already. Your job is NOT to implement it yet. Your job
is to audit this specific project and produce a concrete, ordered implementation
plan that is grounded in what's actually in the repo.

STEP 1 - AUDIT. Read the project and report:
- Unity version, render pipeline in use, and where the pipeline assets live
- Colour space (Linear vs Gamma) from ProjectSettings
- The Universal Renderer asset: current Rendering Path, HDR on/off, Depth Texture,
  Opaque Texture, MSAA, shadow settings, and which Renderer Features are attached
- Whether any Volume / post-processing profile exists anywhere, and what's in it
- Every Camera in the main scene: FOV, projection, position, rotation, and whether
  Cinemachine is installed
- How the world is built: Terrain component vs meshes, and where the ground/island
  geometry comes from
- Every Light in the main scene: type, colour, intensity, range, shadow setting
- Which materials use emission, and whether any use HDR emission intensity
- Lightmapping settings and whether anything is baked
- Installed packages relevant to rendering (URP version, Shader Graph, Cinemachine,
  ProBuilder, Adaptive Probe Volumes availability)
- The HUD: which UI system (IMGUI / UGUI / UI Toolkit), and where the HUD code lives
- Any existing asset packs already imported under Assets/

Report this as a table of "current value" vs "target value" using the targets below.

STEP 2 - PLAN. Produce a tiered plan with these tiers, in this order. For each task
give: exact file or asset to change, the specific value to set, why it matters, and
an estimate. Flag anything that would break existing gameplay code.

TIER 1 (target: ~1 day, ~50% of the visual gap, zero cost)
- Switch Universal Renderer to Forward+. Reason: Forward caps at 9 real-time lights
  per object (1 main + 8 additional); the target scene needs 25-40. Note that
  Forward+ ignores Additional Lights Per Object Limit, always blends reflection
  probes, and still caps transparents at 9 lights - so flag any transparent
  materials that need to be lit and recommend opaque/alpha-clip + emission instead.
- Verify Linear colour space; if Gamma, flag it as a blocking prerequisite.
- Enable HDR, Depth Texture, Opaque Texture, Soft Shadows (distance 60-100m, 2-4
  cascades). Add the SSAO Renderer Feature (intensity ~0.5, radius 0.2-0.3).
- Create a global Volume with: Tonemapping ACES; Bloom threshold 0.9-1.1, intensity
  0.4-0.8, scatter 0.6-0.75, high quality filtering on; Color Adjustments post
  exposure +0.2-0.5, contrast +10-20, saturation +5-15; Split Toning shadows
  blue-violet #3A2E6B / highlights warm amber #FFC27A, balance -10; Shadows
  Midtones Highlights with crushed shadows and lifted highlights; Depth of Field
  Bokeh, aperture f/2.8-5.6, focal length 80-120, focus on the player; Vignette
  0.25-0.35 intensity, 0.4 smoothness; Film Grain 0.15-0.25; Chromatic Aberration
  0.05-0.1.
- Install Cinemachine. Replace the current camera rig with a CinemachineCamera +
  Position Composer following the player: FOV 20-30 (not 60), pitch 38-45 deg, yaw
  45 deg, distance 25-40m, damping 1.0-1.5, Physical Camera on with 85-135mm focal
  length and 35mm sensor. Keep perspective, NOT orthographic, so DoF still works.
- Swap in an HDR night skybox; set Environment Lighting to Skybox with intensity
  multiplier 0.2-0.5.
- Set the Game view to a fixed 16:9.

TIER 2 (~3-5 days) - Replace the Unity Terrain with discrete mesh islands that have
undersides, hard silhouettes against the void, and three depth layers (foreground
in focus / midground hero / background silhouetted and desaturated). Tell me what
in the codebase depends on the Terrain component (navmesh, raycasts, block
placement, spawn logic, height sampling) and what has to change. Propose a
ProBuilder blockout path.

TIER 3 (~2 days) - Lighting pass. Directional moonlight #8FB4FF or 9000-12000K at
0.3-0.7 intensity, 35-45 deg elevation from behind-left, soft shadows at 0.6-0.75
strength. One warm point light (2000-2500K, #FFB661, range 4-8m) per lantern with
shadows OFF on all but 2-3 hero lights, each paired with an emissive mesh. Wisdom
Tree: large lavender point light at the trunk (range 20-30m, shadows on), 2-3
canopy fills, an upward spot, emissive material at +3 to +5 EV, and Rendering
Layers so its light doesn't spill onto distant islands. Path blocks: emissive
#3A6BFF-#7FB0FF at +2 to +4 EV, one small point light per 2-3 blocks, sine-wave
pulsing emission. Bake GI for statics, use Adaptive Probe Volumes for dynamics.

TIER 4 (~2 days) - Asset integration. I'll supply the packs; plan the folder
structure, material conversion to URP, and a scatter/placement approach hitting
8-15 prop instances per square metre in hero areas and 2-3 in traversal areas,
with randomised Y rotation, +/-20% scale and slight X/Z tilt.

TIER 5 (~1 day) - Volumetric fog integration (candidate assets: Buto, or Kronnect
Volumetric Fog & Mist 2, or the free CristianQiu URP volumetric light package).
Check our Unity version against each and recommend one. Note: Aura 2 is NOT an
option, it doesn't support URP.

TIER 6 (~2 days) - HUD redesign: dark panels at ~85% opacity, 8-12px rounded
corners, 1px warm-gold border, serif headers plus sans body, circular icon buttons
top-right, world-space interaction prompts. Tell me whether to stay on the current
UI system or migrate.

STEP 3 - RISKS. List anything in the plan that could break gameplay, hurt
performance, or conflict with existing systems, and what to do about each.

CONSTRAINTS:
- Do not modify any files in this pass. Plan only.
- If a target value conflicts with something the project already does for a good
  reason, say so instead of silently following the spec.
- Where you can't determine something from the repo, say so explicitly rather than
  guessing.
- Before Tier 1, tell me how to capture a fixed-position reference screenshot at
  16:9 so I can measure progress after each tier.
```

When the plan comes back, have Claude Code implement Tier 1 only, then compare screenshots before authorising Tier 2.
