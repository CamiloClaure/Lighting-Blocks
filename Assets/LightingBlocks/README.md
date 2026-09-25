# Lighting code layout

## Current development

- `Runtime/`: code used by Lighting (gameplay, bridges, player, Souls, UI, audio, Wisdom Trees, particles and creatures).
- `Editor/`: Lighting scene builder, checks, crystal tile authoring, creature asset tools and shared screenshot utility.
- `Legacy/Runtime/Scenery/`: forest ambience and vegetation rendering, skipped by Lighting.
- `Legacy/Runtime/Terrain/`: old terrain ground, cover, HUD and terrain diagnostics.
- `Legacy/Runtime/Diagnostics/`: old forest performance and Twilight capture fixtures.
- `Legacy/Editor/`: old scene builders, forest appearance tools, verification and terrain editors.

Legacy menu commands are under **Tools > Lighting Blocks > Legacy**.

## Shared code is still active code

Do not archive a script just because its name contains Forest. `ForestWorld`,
`ForestConfiguration`, `ForestNavigation`, `ForestCreature`, `ForestSoundscape`,
and `SpectralAnimal` all support Lighting. `GameManager`, `WorldDressing`,
`BridgeProfile`, and `EnvironmentVisibility` also retain compatibility branches
for older scenes. They remain active dependencies, not disposable legacy files.

This is a source-folder separation, not an assembly or behavior rewrite. Legacy
scripts still compile in the existing Unity assemblies because shared code has
references to their scenery types and diagnostic hooks. No types, namespaces,
scene assets, materials, saves, or script GUIDs were renamed or deleted.
Editor scripts remain beneath an Editor folder so they stay editor-only.

The Lighting builder still reads `Assets/Scenes/SampleSceneTerrain.unity` as its
source for player/camera/system wiring. Keep that source scene and its assets.
Building Lighting does not save changes back into it.

See `CodeMoves.json` for exact old-to-new script paths. Unity regenerates IDE
project files on refresh; do not manually maintain generated .csproj files.
