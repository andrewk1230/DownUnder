# Revert notes — camera shake, sinking character, dead swim animations

**Date:** 2026-07-26
**Backup taken before:** the terrain-floor and animator changes below.

This folder holds byte-for-byte copies of every pre-change file, taken before any edit:

- `Assets/Scenes/SampleScene.unity`
- `Assets/Player Related/JoshAnimator.controller`
- `Assets/Editor/FixFirstPersonTurn.cs`
- `Assets/Editor/FixThirdPersonSetup.cs`

It sits outside `Assets/`, so Unity does not import it.

---

## Full revert

Close Unity first — it holds the scene in memory and will overwrite a restored `.unity` file on
save. Then, from the project root:

```
cp "Backups/2026-07-26-swim-and-terrain-fix/Assets/Scenes/SampleScene.unity" "Assets/Scenes/SampleScene.unity"
cp "Backups/2026-07-26-swim-and-terrain-fix/Assets/Player Related/JoshAnimator.controller" "Assets/Player Related/JoshAnimator.controller"
cp "Backups/2026-07-26-swim-and-terrain-fix/Assets/Editor/FixFirstPersonTurn.cs" "Assets/Editor/FixFirstPersonTurn.cs"
rm "Assets/Editor/FixSwimAndTerrain.cs" "Assets/Editor/FixSwimAndTerrain.cs.meta"
rm "Assets/Scripts/SwimAnimatorBridge.cs" "Assets/Scripts/SwimAnimatorBridge.cs.meta"
```

Deleting the two new scripts before reopening matters: `SampleScene.unity` in this backup has no
`SwimAnimatorBridge` component on the player, and `FixSwimAndTerrain` re-applies itself on entering
Edit mode, so leaving it in place would immediately undo the revert.

---

## What changed, and how to undo each piece by hand

### 1. Camera shake when turning (fixed, confirmed by the user)

`Assets/Editor/FixFirstPersonTurn.cs`, applied to the scene:

| Field | Before | After | Why |
|---|---|---|---|
| `UnderwaterSwimController.cameraTransform` | player root `Character.001` | child camera `Camera` | The root was steering off its own axes: holding left/right asked the body to face its own right, which moved the target again next frame, so the root spun continuously. |
| `UnderwaterSwimController.rotationDamping` | 10 | 0 | Makes the `Slerp` at `UnderwaterSwimController.cs:198` return the current rotation, so nothing writes root rotation from `Update`. SUPER pins the camera's world pose in `FixedUpdate`, so an `Update`-time root rotation swung the child camera around a multi-metre lever arm and got snapped back — a judder for as long as the body was turning. |

To undo: set `cameraTransform` back to the root and `rotationDamping` back to 10 on the
UnderwaterSwimController component. Note that body yaw is now owned entirely by SUPER's
`UpdateBodyRotation_3rdPerson`, which only rotates an *ungrounded* character if
`SUPERCharacterAIO.rotateCharacterToCameraForward` is enabled — it is currently off.

### 2. Character sinking into the terrain

`Assets/Editor/FixSwimAndTerrain.cs` → `FixTerrainFloor`, applied to the scene:

| Field | Before | After |
|---|---|---|
| `UnderwaterSwimController.terrainLimit` | unassigned | the scene `Terrain` |
| `UnderwaterSwimController.terrainClearance` | 0.1 | 0.8 (`capsule.height / 2 - 0.075`) |

The swim controller moves the player by assigning `transform.position`, which is a teleport that
ignores colliders. Its only collision substitute, `ApplyMovementLimits`, was inert because
`terrainLimit` was unassigned. Nothing else pushed the capsule out either — SUPER never snaps to the
ground (`playerGroundPosition` is only drawn as a gizmo at `SUPERCharacterAIO.cs:1515`) and it
hard-writes `p_Rigidbody.linearVelocity` every `FixedUpdate`, destroying PhysX's depenetration
velocity.

The clearance is measured from the **root pivot**, which sits at the capsule's centre — half the
capsule height above the feet. It is deliberately set slightly *below* the resting height so it acts
as a floor rather than a constant upward push: a capsule on a slope legitimately rests closer to the
vertically-sampled surface point than it does on flat ground, and clamping at the exact resting
height would make the character hover on inclines.

To undo: clear `terrainLimit` and set `terrainClearance` back to 0.1. Expect the sinking to return.

### 3. Swim and tread-water animations never playing

Root cause was #2. An embedded capsule puts the ground raycast origin (`SUPERCharacterAIO.cs:960`)
*inside* the TerrainCollider, and a downward ray starting below a heightfield does not hit it — so
`Grounded` was permanently `false`, `AnyState -> AirBorn` fired every frame, and the animator was
pinned in the jump clip.

Beyond that, the graph could not reach the swim states on its own. Changes to
`Assets/Player Related/JoshAnimator.controller`:

| Transition | Before | After |
|---|---|---|
| new parameter | — | bool `InWater` |
| AnyState → AirBorn | `Grounded is false` | `Grounded is false AND InWater is false` |
| Idle → Moving | `Idle is false` | `Idle is false AND InWater is false` |
| Idle → Swimming | `SwimSpeed > 0.1 AND IsSwimming is true`, `hasExitTime` at 0.9375 | `InWater is true AND SwimSpeed > 0.1`, no exit time |
| Swimming → Idle | `SwimSpeed < 0.1 AND IsSwimming is true`, `hasExitTime` at 0.9449 | `SwimSpeed < 0.1`, no exit time |
| Swimming → Idle (new) | — | `InWater is false` |

`Idle -> Moving` fires on the first frame of input, while `SwimSpeed` needs a few frames to climb
past 0.1, so without the `InWater` gate the land transition always won the race — and `Moving` has
no path to `Swimming`, so the swim state was unreachable in practice. The old `Swimming -> Idle`
condition was unsatisfiable: `SwimSpeed` and `IsSwimming` are written from the same normalised
speed, so `IsSwimming` is already false by the time `SwimSpeed` drops below 0.1.

Also added: `Assets/Scripts/SwimAnimatorBridge.cs`, a component on the player root that publishes
`InWater` to the Animator each frame from `UnderwaterSwimController.IsInWater`.

### Consequence worth knowing

`requireWaterVolume` is **off** on the UnderwaterSwimController, so `IsInWater` returns `true`
unconditionally. `InWater` is therefore always true, which means:

- Standing still plays **Treading Water** (the clip on the `Idle` state).
- Moving plays **Swimming**.
- The land states `Moving` (Walk) and `Moving_Fast` (Run) are **unreachable**, because
  `Idle -> Moving` now requires `InWater is false`.

To get land locomotion back, tick `requireWaterVolume` on the UnderwaterSwimController and make sure
a `WaterVolume` covers the water. The bridge and the graph then switch between land and water with
no further changes. Watch out for the trap recorded in `HANDOFF.md`: `whatIsGround` is `-1` and
queries hit triggers, so a water trigger on a normal layer registers as walkable ground *and* as
camera obstruction — the previous attempt put the volume on layer 2 (Ignore Raycast) for that
reason.

### Still untouched

- The stray enabled `Camera` component on the player root (`SampleScene.unity:5749`), which is what
  made `GetComponentInChildren<Camera>` in `FixThirdPersonSetup.cs:80` resolve to the root in the
  first place.
- The two locomotion systems both writing position every frame (`HANDOFF.md` open item 2).
