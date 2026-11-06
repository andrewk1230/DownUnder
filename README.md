# DownUnder

An underwater exploration prototype built in Unity. Swim through an underwater environment, switch between first- and third-person views, and explore alongside fish that spawn and move through the scene.

The repository is named **DownUnder**; the local Unity project folder may be named **Underwater Proj**. The playable development scene is `Assets/Scenes/SampleScene.unity`.

## Demo video

[▶ Open or download the DownUnder gameplay demo](https://github.com/andrewk1230/DownUnder/raw/refs/heads/main/docs/media/downunder-demo.mp4)

*20-second gameplay recording · no audio.*

## What is in the prototype?

- **Camera-relative swimming:** horizontal movement, ascent, descent, sprinting, and vertical movement influenced by camera pitch.
- **Two perspectives:** first-person look and strafing, plus an orbiting third-person camera with adjustable framing.
- **Swimmer animation:** a Humanoid character with swimming and treading-water states, connected to the movement controller through Animator parameters.
- **Camera protection:** third-person obstruction checks and a separate terrain-clearance setting for the camera.
- **Underwater presentation:** water shaders, underwater visuals, and bubbles anchored to the swimmer.
- **Fish population:** configurable spawn groups, per-group model scaling, waypoint or random-target movement, and forward obstacle checks.

This is a development prototype. The controller integration still has limitations; see [Current limitations](#current-limitations) before extending movement or water transitions.

## Open and run

### Requirements

- **Unity Editor 6000.5.0f1**, as recorded in `ProjectSettings/ProjectVersion.txt`.
- **Unity Hub** to select and install the matching editor.
- **Git** available to Unity Package Manager for the Git-based dependencies.
- Internet access for the first package restore.
- The Unity build-support module for your intended target, if you plan to create a build.

### Setup

1. Clone the repository:

   ```sh
   git clone https://github.com/andrewk1230/DownUnder.git
   ```

2. In Unity Hub, choose **Add project from disk** and select the cloned folder containing `Assets`, `Packages`, and `ProjectSettings`.
3. Open it with **6000.5.0f1**. Allow Unity to import assets, resolve packages, and finish compiling.
4. Open `Assets/Scenes/SampleScene.unity`.
5. Check the Console for compilation or missing-reference errors, then enter **Play mode**.
6. Click the Game view to give it input focus.

Opening a third-party demo scene will show that asset's sample setup rather than DownUnder's player and environment.

## Controls

The swim controller uses the **Player** action map in `Assets/InputSystem_Actions.inputactions`.

| Action | Keyboard / mouse | Gamepad binding in the input asset |
| --- | --- | --- |
| Move | W / A / S / D or arrow keys | Left stick |
| Look | Mouse movement | Right stick |
| Ascend | Space | South button |
| Descend | C | East button |
| Sprint | Left Shift | Left-stick press |
| Switch perspective | Q, through SUPER Character Controller | No dedicated perspective action in the shared input asset |

Looking up or down while moving forward also influences vertical swimming. Gamepad bindings are present in the input asset, but the complete gamepad experience needs Play mode verification because SUPER also reads some controls directly.

`Assets/CameraSwitcher.cs` contains an older C-key toggle. It is a legacy helper, not the intended perspective-switching path; C is bound to descent in the swim input map.

## Project layout

| Location | Purpose |
| --- | --- |
| `Assets/Scenes/SampleScene.unity` | Main development and build scene |
| `Assets/Scripts/` | Project-specific swimming, camera, animation, and visual integration |
| `Assets/AISpawners.cs` | Fish spawn groups, spawn volume, and waypoint collection |
| `Assets/AIMove.cs` | Fish target selection, movement, orientation, and obstacle checks |
| `Assets/Player Related/` | Player model and `JoshAnimator.controller` |
| `Assets/InputSystem_Actions.inputactions` | Shared input actions and bindings |
| `Assets/Editor/` | Editor setup and repair utilities |
| `Assets/SUPER Character Controller/` | Imported character-controller source and demos |
| Other asset folders under `Assets/` | Environment models, water shaders, fish, animations, and imported content |
| `Packages/manifest.json` | Direct Unity and Git package dependencies |
| `Packages/packages-lock.json` | Resolved dependency versions and revisions |
| `ProjectSettings/` | Editor, rendering, input, and build settings |
| `HANDOFF.md` | Detailed historical debugging notes and previous session decisions |
| `Backups/` | Previous repair backups and revert notes |

## How the player systems fit together

The player combines **SUPERCharacterAIO** with the package-provided **UnderwaterSwimController**. Project scripts bridge the differences between those controllers, the camera, and the Animator.

### Movement and facing

`UnderwaterSwimController` reads Move, Jump, Crouch, and Sprint actions. It calculates camera-relative movement, applies terrain clearance, and publishes swimming animation values.

`SwimBodyRotation` turns the body toward camera-relative movement in third person. It runs before the swim controller so the controller's facing-alignment speed calculation sees the updated heading. The scene sets the package controller's `rotationDamping` to **0**, leaving this rotation path to the project script.

First person uses two additional components:

- `FirstPersonLookYaw` applies horizontal look in `LateUpdate`, avoiding competing Rigidbody and transform rotation writes. Vertical camera look remains with SUPER.
- `FirstPersonSwimStrafe` restores sideways motion that the package's facing-alignment gate would otherwise suppress. It reapplies terrain and height limits after adding the strafe movement.

Both first-person helpers check the active perspective. Keep those checks when modifying them.

### Camera

`ThirdPersonCameraPivot` maintains an independent camera orbit, positions the camera behind a configurable pivot, and checks for obstructions and terrain clearance. Its main tuning fields include:

| Field | Effect |
| --- | --- |
| `boomLength` / `minBoomLength` | Normal follow distance and closest obstruction-adjusted distance |
| `thirdPersonPivotHeight` | Aim-point height relative to the player capsule centre |
| `lateralOffset` / `heightOffset` | Camera-relative framing offsets |
| `lookSensitivity`, `minPitch`, `maxPitch` | Orbit response and vertical look limits |
| `guardAgainstClipping` / `obstructionMask` | Obstruction protection and layers checked |
| `terrainCameraClearance` | Minimum camera clearance above terrain |

Assign the same camera to the pivot and swim controller so movement follows the view the player actually sees. Keep a single intended gameplay camera active. Adding another camera driver to this rig can introduce competing transform writes.

### Animation and bubbles

- `SwimAnimatorBridge` copies the swim controller's water state into the Animator's `InWater` bool.
- The swim package publishes `SwimSpeed` and `IsSwimming`.
- `Assets/Player Related/JoshAnimator.controller` contains the player animation graph. Treading water is the intended underwater idle animation.
- `BubbleEmitAnchor` redirects the underwater visual system's bubble origin to the swimmer so bubbles do not originate behind the player at the third-person camera.

The first-person strafe and bubble integration read private package fields through reflection. Recheck these components after upgrading the swim package; renamed fields can change their behavior.

## Tune swimming

The main scene serializes these values on `UnderwaterSwimController`:

| Setting | Saved value | Meaning |
| --- | --- | --- |
| Swim speed | `4` | Base swim speed |
| Sprint multiplier | `1.5` | Speed multiplier while sprinting |
| Acceleration | `8` | Velocity response |
| Vertical swim speed | `3` | Vertical movement tuning |
| Camera pitch influence | `0.75` | Contribution of view pitch to vertical movement |
| Terrain clearance | `0.8` | Body clearance from the terrain surface |
| Require water volume | Off | Swimming is treated as active without a water-volume check |
| Clamp to world height | Off | No upper world-height clamp is enabled |

Treat these as starting values from the saved scene. Inspector overrides determine runtime behavior. Terrain clearance is separate from full collision handling against arbitrary rocks, props, or walls.

## Configure fish

1. Select the scene object with `AISpawners`, or add that component to a new spawner object.
2. Set the spawn-area dimensions. The volume follows the spawner's position, rotation, and scale.
3. Configure each entry in **AI Groups Setting**: prefab, group name, maximum population, spawn amount, scale multiplier, and whether spawning is enabled.
4. Set the global **Spawn Timer**. The code clamps intervals below `0.1` seconds to avoid continuous spawning.
5. For waypoint navigation, add child transforms tagged **`waypoint`**. The spawner discovers them at startup.
6. Enter Play mode and check model size, facing direction, population, and movement bounds.

Waypoint renderers and colliders are hidden at runtime when **Hide Waypoints** is enabled. Select the spawner in the Scene view to see its volume and waypoint gizmos.

`AIMove` is added to spawned fish when missing. It supports random-position or waypoint targets, a model yaw correction, and a look-ahead raycast. The default yaw correction is intended for fish authored along their X axis; check new models for sideways or tail-first movement. This is lightweight target-based movement, with no flocking or NavMesh pathfinding implemented in `AIMove`.

## Dependencies and imported assets

The main rendering stack is **Universal Render Pipeline (URP) 17.5.0**. Other direct dependencies include Input System **1.19.0**, Cinemachine **3.1.7**, AI Navigation **2.0.13**, and glTFast **6.19.0**. Package installation alone does not imply a feature is used by gameplay; the player camera is driven by the scripts described above.

Two packages are restored from the `mdj128/yt-underwater-scene` repository:

- `com.budorf.underwater-swim` — `UnderwaterSwimController`
- `com.plant.detailtoolkit` — `PlantDetailToolkit`

Imported content includes SUPER Character Controller, Kevin Iglesias animations, FishAlive, Alstra Infinite Fish PolyPack, and URP water shaders. Consult the individual assets' license and attribution files before redistributing them. This README does not grant rights to third-party assets.

Do not edit resolved package files inside `Library/PackageCache`: Unity can replace them on restore. Make durable integration changes in project scripts, or use an explicitly maintained embedded package or fork.

## Current limitations

- **Movement ownership is shared.** SUPER uses Rigidbody-based movement while the swim package writes transforms. Project helpers resolve specific camera and input problems, but a unified walking/swimming state system remains an architectural task.
- **The main scene uses always-on swimming.** `requireWaterVolume` is disabled. Water-entry and exit behavior, including seabed walking transitions, should be developed and tested explicitly.
- **Body turning is horizontal.** `SwimBodyRotation` steers yaw; it does not implement full 3D body pitch while swimming.
- **Historical notes may lag behind code.** `HANDOFF.md` records earlier diagnoses and fixes, including some superseded approaches. Compare it with current scripts and Inspector values before applying an old recommendation.
- **Repair tools are still present.** Inspect `Assets/Editor/Fix*.cs` before using them; they can change scene or importer settings. Do not run every repair tool as a routine setup step.
- **No project-specific automated test suite or command-line build script is tracked.** Gameplay verification currently relies on Unity compilation and Play mode checks.

## Troubleshooting

| Symptom | Start here |
| --- | --- |
| Git package fails to restore | Confirm Git is available, network access works, and the URLs in `Packages/manifest.json` can be reached. Check Package Manager's error details. |
| Input does nothing | Focus the Game view; inspect the player's `PlayerInput` action asset and action map. Check the Console for missing-action warnings. |
| First-person turning or strafing fails | Check `FirstPersonLookYaw`, `FirstPersonSwimStrafe`, and their Look / Move / Sprint action names. |
| Third-person movement does not match the view | Confirm the swim controller and `ThirdPersonCameraPivot` reference the same camera. |
| Camera is offset, clips, or shows the wrong view | Check active cameras, pivot offsets, terrain clearance, obstruction layers, and competing camera components. |
| Swimming animation is missing or freezes | Check Humanoid avatar and clip imports, Animator parameters, transition conditions, and Loop Time on swimming and treading-water clips. |
| Fish do not appear | Check the group prefab, enable flag, maximum population, spawn amount, and nonzero scale. |
| Fish swim sideways or tail-first | Adjust `AIMove`'s model yaw offset to match the asset's forward axis. |
| Fish remain near the spawner | Check waypoint tags and target mode. With no waypoints, waypoint selection falls back to the spawner position. |

## Build and verify changes

Open **File → Build Profiles**, choose the target platform, and confirm `Assets/Scenes/SampleScene.unity` is enabled in the scene list. Install the required platform module through Unity Hub if needed, then build to a separate output folder.

Before sharing a gameplay change:

- Let Unity finish compiling and resolve Console errors.
- Test movement, ascent/descent, sprinting, and looking in both perspectives.
- Switch perspectives while moving and while idle; watch for camera snaps or lost input.
- Swim near the seabed and props; check body clearance and camera obstruction behavior separately.
- Watch animation transitions and allow idle/swim clips to run long enough to detect frozen poses.
- Confirm bubbles follow the swimmer and fish spawn with sensible scale and orientation.
- For a release, repeat the relevant checks in a standalone build.

Keep Unity `.meta` files with their assets. Use the Unity Editor for scene and prefab changes to preserve serialized references. Exclude generated `Library`, `Logs`, temporary files, and local build output from commits.
