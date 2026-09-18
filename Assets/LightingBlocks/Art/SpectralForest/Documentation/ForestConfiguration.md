# Spectral Forest configuration and maintenance

## Open and play

Use Unity 6000.6.0f1 and the existing URP 17.6.0 packages. Open `Assets/Scenes/SampleScene.unity`. Collect the cyan crystals by walking over them, approach the violet Wisdom Tree, and press E. Three particles from that island produce one existing bridge block after a 0.6-second charge. Souls still buy the five existing skills.

The current scope is three islands. **The five-stage/four-bridge expansion remains future work.** Forest definitions and their deterministic placement code accept additional islands; the existing chapter and bridge progression still intentionally contains two bridges.

## Assets

`Assets/LightingBlocks/Art/SpectralForest/Configuration/Forest.asset` contains the island definitions, prefabs, floor/cliff materials and atmosphere distances. IDs are stable save identifiers; do not rename them in a shipped save without migration. `Assets/Models/Tree.obj` remains the canonical, unmodified Wisdom Tree source. Its reference is stored in the configuration. The processed FBX uses authored bark, leaf and pod face selections, with source-derived LODs and surface-edge filaments.

`ArtSources/SpectralForest/WisdomTree-authored.blend` is the editable Wisdom Tree. The original Quaternius nature blend is retained there, outside Unity's runtime import tree. The ordinary short/full birch prefabs use source BirchTree_2 and BirchTree_3; source BirchTree_1 has no foliage and was excluded after inspection. Maple and pine use their first two numbered variants. All assets use shared materials except the three Wisdom Trees' charge property blocks.

The spectral material is an editable URP HLSL shader, rather than a Shader Graph asset. It supplies the planned normal/roughness/AO blend, cliff triplanar color, Fresnel edge emission, wind, and shaped pod/crystal emission. Animal animations use the original Generic FBX rigs and verified `AnimalArmature|Idle`, `AnimalArmature|Walk`, and `AnimalArmature|Gallop` clips, mapped to Idle/Walk/Run controller states. Root motion is disabled.

## Authoring and regeneration

Use `Tools > Lighting Blocks > Apply Spectral Forest` to regenerate the materials, prefabs and scene preview. The older art integration and scene-building commands route through the same forest builder. This is a generated-art setup command: it resets its authored defaults and removes scene-authored loose starter blocks. Save bespoke prefab/material edits elsewhere or change the builder defaults before reapplying. Runtime saved blocks are not removed by this editor command.

Editor preview seed: 1907. New games create their own seed before saving. Runtime rebuilding preserves inventories and particle cooldowns. Changing placement rules requires incrementing `ForestConfiguration.LayoutVersion` and supplying a matching save migration; the current loader rejects an unsupported layout version.

Fixed Wisdom Tree anchors are (-12,0,-3), (-12,0,27.5), and (-12,0,59), producing Wandering, Grave, and Obsidian blocks. Each has nine outputs at local X 3.5/5.5/7.5 and Z -2/0/2. Output footprints and occupancy are rechecked when the charge finishes.

Ordinary trunk counts are 70/18/85, wildlife 6/3/6, collectible slots 24 per island, and background trees up to 150. Bounded rejection sampling preserves the central hauling lane, output pads, important props, and island-edge setbacks. Wildlife decisions run at 5 Hz, use A* with segment and corner checks, and suspend beyond 60 metres. Animals have no physical colliders and grant no rewards.

## Verification

`Tools > Lighting Blocks > Run Spectral Forest Checks` runs gameplay, deterministic placement, five-island configuration, save, inventory, charging and hauling regression checks with an isolated save. The older Run Gameplay Checks command routes to this suite. Results are written to `Library/SpectralForest.tests.txt`; actual gameplay camera captures are in `Library/SpectralForestCaptures`.

`Tools > Lighting Blocks > Build Forest Verification Player` creates the art-inspection scene and a Windows development player in `Builds/SpectralForestPerformance`. Run that executable with `-forest-profile` to run the deterministic 60-second profiling fixture, which never loads or writes the player's actual save. Results and full-resolution gameplay screenshots are written beside the executable in `Profile`. The fixture exits when complete. Normal launches do not run the fixture.

`ForestArtInspection.unity` displays the ground, six ordinary tree variants, four animals and one Wisdom Tree for art review. SampleScene remains the playable integrated scene.

## Spirit Path dressing

`Tools > Lighting Blocks > Apply Spirit Path Look` runs the whole Spirit Path art direction pass on the open `SampleScene` (`Editor/SpiritPathLook.cs`): ACES tonemapping and the rest of the post-processing stack, trilight ambient and moon retint from `Forest.asset`, rim colour and soft fill on the cliff keels and every forest tree material, Wisdom Tree bark/leaves and luminous-plant emission, and the `M_PlayerFog` void-mist parameters. Play mode has to be stopped first. It also polls for `Library/SpiritPath.apply-request`: create that file with any content (the poll only checks that it exists) and the next non-compiling editor frame reruns the same `Apply()`, writing `SUCCESS: spirit path look applied.`, or the exception text on failure, to `Library/SpiritPath.apply-result.txt`.

The rest of the dressing runs at runtime out of `WorldDressing.Build` (called from `GameManager.Start`), so it survives a scene rebuild instead of depending on a baked scene. Hero cover is `ForestConfiguration`'s dense "cover-hero" placement pass (`Runtime/ForestConfiguration.cs:121-131`): the same undergrowth scatter as the rest of an island, but gridded at 0.45 m instead of 1.05 m within 10 m of a Wisdom Tree, rendered through the same `ForestCoverRenderer` and checked by `Run Spectral Forest Checks` to hold at least 300 cover instances in that ring per island. Rim rocks (`WorldDressing.RimRocks`) walk every shore edge and drop one of four faceted icosahedron shapes (`WorldDressing.RockMesh`) every 3.5-5 m, using a brightened clone of that shore's own cliff material and skipping both bridge lanes so a boulder never blocks a crossing. Lanterns (`WorldDressing.Lanterns`, `WorldDressing.Lantern`) clone the scene's existing "Courtyard lantern glow" fixture roughly every 26 m of shore perimeter and retune each clone to a warm, unshadowed ember (`#FFB661`, intensity 4, range 6).

Paving, ruins and light shafts are further shore dressing landing alongside this pass, under the object names `Spirit paving` and `Rim step` (rim paving), `Spirit ruin` (ruin silhouettes) and `Void shaft` (light shafts through the void mist) - look for them by name under "Immersive Deeps" in the hierarchy `WorldDressing.Build` creates.

The capture set: `Run Spectral Forest Checks` finishes its pass by running `ForestVerification`'s capture coroutine, which writes per-island and gameplay-vista screenshots (and, outside batch mode, `hud.png`) to `Library/SpectralForestCaptures`. Curated copies of these, alongside `-forest-profile`'s `Profile/render-counters.txt` and `Profile/performance.json`, are archived per art-direction milestone under `docs/art-direction/` (`00-baseline-*`, `01-tier1`, `02-tier2`, `03-tier3-6`).

The build poller: `Editor/ForestDelivery.cs` also polls `Library/LightingBlocks.build-request` the same way as the checks and look-pass requests above. Create that file and the next non-compiling, non-Play editor frame builds the Windows development player (`BuildPipeline.BuildPlayer`, target `StandaloneWindows64`, `BuildOptions.Development`) into `Builds/SpectralForestPerformance/LightingBlocks.exe`, writing `SUCCESS: <full path>`, or the exception text on failure, to `Library/LightingBlocks.build-result.txt`.

