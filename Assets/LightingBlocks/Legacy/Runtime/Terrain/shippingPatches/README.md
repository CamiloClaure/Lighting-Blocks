# Two fixes that cannot live in this folder

Everything else the terrain scene needs is in `terrainSceneScripts` and reaches the shipping code
through a public method, a public field or a component swap. These two cannot be reached that way,
so they are parked here as an opt-in patch. **Nothing under `Assets/LightingBlocks` outside
`terrainSceneScripts` is modified unless you apply it.**

## What is in the patch

| file | what breaks without it | how bad |
|---|---|---|
| `BoxPlayer.cs:321` | `FindDrop` pins every drop candidate to `y=.26` and then `OverlapBox` hits the terrain solid, so all 160 candidates are rejected and Q reports "DROP AREA BLOCKED" | stones cannot be dropped on raised ground; flat ground and the whole rim band still work |
| `ForestSoundscape.cs:45` | the wisdom-tree audio zone is measured in 3D to `wisdomPosition`, which is layout data at y=0 | radius reads 8.2 m instead of 9 m on a 3.6 m hill |

## Why they cannot be done from `terrainSceneScripts`

- `FindDrop` is **private** and is called from inside `BoxPlayer.Update`. Its public caller
  `DropOrbit` already has the answer by the time it is reachable, and the player must not be moved
  into the flat frame while its `CharacterController` is running, which is the trick
  `TerrainGround.Update` uses for the wildlife.
- `ForestSoundscape` keeps a `static current` that `Play`, `Footstep` and `Route` are called
  through from a dozen places, and its `Awake` builds the single `AudioListener` and 12 voices.
  A second copy would either fight over that static or need every caller rerouted, for 0.8 m of
  crossfade radius.

Renaming a copy does not help either: `GameManager.player` is typed `BoxPlayer`, so a renamed
player script can never be assigned in the scene, and `ForestSoundscape` is added by
`ForestWorld.Build:121` as a hard type reference. Same name in the same assembly is a
duplicate-class error, and this project has no asmdef, so everything is one `Assembly-CSharp`.

## Apply

```
git apply Assets/LightingBlocks/Runtime/terrainSceneScripts/shippingPatches/relief-optional.patch.txt
```

Self-contained: it calls `TerrainScene.TerrainGround.Height`, which finds no Terrain in a flat
scene and returns `0`, so both expressions collapse to the literals they replace and `SampleScene`
behaves identically. Verified to compile clean in both assemblies before being parked here.

The one thing it costs is direction: two shipping files would then name a type in this sandbox
folder. That is the reason it is opt-in rather than applied.

## Revert

```
git checkout -- Assets/LightingBlocks/Runtime/BoxPlayer.cs Assets/LightingBlocks/Runtime/ForestSoundscape.cs
```
