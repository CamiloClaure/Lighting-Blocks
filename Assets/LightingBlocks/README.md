# Lighting Blocks

Open `Assets/Scenes/SampleScene.unity`, press Play, and click the Game view to give it keyboard focus.

- **WASD:** camera-relative movement. Diagonal movement has the same speed as straight movement.
- **Hold left mouse:** grab a highlighted nearby box. A box under the cursor takes priority when it is reachable; otherwise the nearest reachable box is selected.
- **WASD while holding:** push or pull the box. The character faces the box, and both stop when either encounters an obstacle.
- **Release left mouse:** immediately detach and stop the box. Losing application focus also releases it.
- **Camera:** screen edges or arrow keys to pan, middle mouse drag to pan, scroll to zoom, hold Space to center on the player.

The scene contains five movable teal boxes, a gridded floor, perimeter walls, and two interior obstacles. Nearby boxes highlight mint; the held box turns gold.

The supplied idle, run, and pull FBX clips use the same humanoid avatar, loop, and have root motion baked. The separate `BoxCharacter.controller` is driven by the player script. The original controller is retained. The original scene is backed up at `Assets/Scenes/SampleScene_BeforeBoxControls.unity`.

Tune movement speed, pull speed, and reach on the Player's BoxPlayer component. Tune panning, zoom distance, edge-panning, and bounds on the Main Camera's StrategyCamera component.

Boxes are upright kinematic objects for this flat arena: they do not fall, stack, rotate, or retain momentum. Collision sweeps constrain the held pair; unheld boxes remain stationary. Add another box by duplicating a Pullable Box object.

Use **Tools > Lighting Blocks > Run Gameplay Checks** to enter Play mode, run movement/animation/reach/grab/release/collision/camera checks, and leave Play mode. Results are written to `Library/LightingBlocks.test-result.txt`.

**Tools > Lighting Blocks > Build Playable Scene** recreates the generated scene, replacing current scene contents. The original backup is created only once. Avoid rebuilding after making your own scene edits unless you have saved those edits elsewhere.
