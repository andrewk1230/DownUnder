# Session Handoff — Underwater Proj

**Date:** 2026-07-26, ~16:30 (fifth session; supersedes the 14:40 handoff)
**Branch:** `main`. Worktrees exist and are all irrelevant — see "Critical environment facts".

**Status: the framing complaint is resolved and verified.** `rootVP=(0.500, 0.500)` in the log.

**Sessions so far, in order:** the dead swim/idle animations were traced to Generic clips on a
Humanoid avatar (fixed, verified); the "not aligned" camera was a duplicate depth-0 camera on the
player root (fixed, verified); the residual offset was blamed on unbaked XZ root motion — **that
diagnosis was wrong, has been reverted, and the real cause (a camera pivot aimed at the top of the
character's head) is fixed and verified.** See the correction immediately below.

**Start here:** open items 1 and 2. Item 1 is cleanup — delete the scaffolding, but read which file
is *not* scaffolding first. Item 2 is the real remaining engineering problem.

---

# CORRECTION, ~15:00 — the 14:35 root-motion diagnosis was wrong

**The mesh never slid off its GameObject.** The instrumentation added at 14:35 to prove it in fact
disproves it, and the previous session wrote the handoff without reading its own output back:

- `modelRootOffsetFromCapsule` is exactly `(0.000, -0.875, 0.000)` on every tick — zero in X and Z.
- `headBoneVP.x` sits at **~0.500 in every sample, both before and after the bake** (compare
  `Logs/Editor.log:425998` onward, pre-fix, with the two Play sessions after `:428875`, post-fix).
  Enabling the bake changed nothing measurable.
- The `Character` model child is at local `(0, 0, 0)` (`SampleScene.unity:5104`).

**The bake was also backwards.** Unity's semantics: Bake Into Pose **ON** holds Root Transform
Position (XZ) constant and leaves the horizontal motion *on the pose* — that is the setting that
lets a body walk away from its own transform. **OFF** extracts the motion as a delta, which
`m_ApplyRootMotion: 0` then discards, leaving the pose rendered on the root. **OFF is correct here**
and is what both clips shipped with. `FixHumanoidClipRigs.UnbakeRootPositionXZ` now forces it off;
session key bumped to `.v3`.

**Applied and confirmed on disk at 14:55:** both `.meta` files now read `loopBlendPositionXZ: 0`
(that is the serialised name — the C# property is `lockRootPositionXZ`, which is why grepping the
meta for the API name finds nothing). `Logs/Editor.log:430745` and `:430812` carry the apply log.

## What was actually mis-framed: the vertical, and it is arithmetic

`UpdateCameraPosition_3rdPerson` (`:816-819`) sets position **and** rotation from the same
`quatHeadRot`:

```csharp
currentCameraPos = headPos + (quatHeadRot * (Vector3.forward * currentCameraZ));
playerCamera.transform.position = currentCameraPos;
playerCamera.transform.rotation = quatHeadRot;
```

So `headPos` lands on viewport (0.5, 0.5) **exactly, by construction, every frame**. Two things
follow, and both are worth keeping:

1. **A horizontal framing offset cannot originate from the player's transform.** It is
   geometrically impossible. Do not go looking for one again — if the body reads as off to one
   side, it is the pose (the prone swim pose extends up to ~1.25 m forward of the root while
   `bodyYaw` stays fixed), not the camera.
2. `headPos = transform.position + up * standingEyeHeight` = capsule centre + 0.795, and the
   capsule centre is already 0.875 above the feet. Screen centre therefore sat **1.67 m up a 1.75 m
   character — the top of its head.** The body hung below it, midpoint at viewport y ≈ 0.40. That
   is the `rootVP=(0.500, 0.399)` printed on every single `[SwimDiag/frame]` line, all session.

**Fix:** `Assets/Scripts/ThirdPersonCameraPivot.cs`, attached to the player by
`FixHumanoidClipRigs.AttachCameraPivot`. It gives third person its own pivot height (default `0` =
the capsule centre = the character's own mid-height) and shifts the camera by `pivot - headPos` in
`LateUpdate`, leaving rotation alone. **`standingEyeHeight` is deliberately not touched** — it is
also the first-person eye height (`:733`) and the Q toggle is live.

**This is not scaffolding. It stays.** Unlike the five `Fix*.cs` scripts, deleting it reverts the
framing.

**Why it corrects once per physics step, not once per frame:** the shift is a translation, so
applying it twice to the same pose doubles it. SUPER only rewrites the camera inside `FixedUpdate`
(`:598`), and a frame can contain zero physics steps. The `superRepinnedCamera` flag, set from a
`FixedUpdate` at `[DefaultExecutionOrder(100)]` (after SUPER's), is what distinguishes "SUPER handed
us a fresh pose" from "we are looking at our own output". Rotation is never written, so there is no
path back into SUPER's inputs and none of the rotation-feedback judder documented below can recur.

**VERIFIED, with the before and after inside one log file.** The last `[SwimDiag/frame]` lines run:

```
rootVP=(0.500,0.399) headVP=(0.500,0.500) headBoneVP=(0.498,0.407)   <- before
rootVP=(0.500,0.500) headVP=(0.500,0.601) headBoneVP=(0.499,0.580)   <- after
```

`rootVP` is now dead centre. Note `headVP` moved *off* centre to `0.601` — that is the fix working,
not a regression: `headVP` tracks SUPER's old aim point, which is no longer what the camera centres
on. **`headVP` is `(0.5, 0.5)` only while SUPER owns the framing; it is not a health check.** Use
`rootVP`.

Also worth having: `hipsOffsetFromModelRoot horiz` now sits at **0.075–0.157** while idle, and the
1.25 m excursions seen under the bake are gone from the idle samples.

`lateralOffset` and `heightOffset` were added on top, both defaulting to `0`, for deliberate
over-the-shoulder / high-angle framing. They apply in **camera space**, so an offset stays put on
screen as the camera orbits instead of swinging around the body. Sign conventions are the ones that
trip people up: **negative `lateralOffset` moves the camera left, which slides the character
right**; **positive `heightOffset` moves the camera up, which slides the character down.** At the
current ~7 m boom and 60° FOV, ~1.6 m ≈ 10% of screen width and ~0.8 m ≈ 10% of screen height.

## Trap: the Game view Scale slider crops the frame

The user sent a screenshot of the character sitting up-and-left of centre and asked for the camera
to be nudged left and up — *after* the fix had already verified. Complying would have decentred the
character everywhere except that one panel.
The Game view was set to **Scale 4x**: Unity renders at quarter size and magnifies, so the panel
shows a scrolled crop rather than the whole frame. Solving backwards from the character's position
in the panel gave crop origins of x ≈ 0.437 and y ≈ 0.393 — both valid for a 4x crop, and both
mapping the character back to exactly the `(0.500, 0.500)` the log reported.

The tell that it is a crop and not a camera offset: **the horizontal and vertical discrepancies were
different sizes.** A real camera offset along one axis does not perturb the other; a scrolled crop
does both, independently.

**A screenshot of the Game view is not evidence about framing unless Scale is 1x.** `rootVP` is.
The user's Game view has been left at 4x — ask before trusting the next screenshot.

---

# ~~Third bug, 14:35: the mesh slid off its own GameObject (unbaked root motion)~~ — WRONG, see the correction above

**Everything in this section is superseded.** It is kept only so the next session can see how the
reasoning went wrong: the camera geometry was reconstructed correctly, the conclusion "therefore the
mesh must be displaced" was then asserted without ever checking `headBoneVP` against a pre-fix
baseline, and the Bake Into Pose semantics were stated backwards.

User: *"the way the character is moving is fully fine, however, the camera is relatively to the
right compared to the character."*

**The camera was never wrong.** From `[SwimDiag/cam]` at idle,
`offsetFromPlayer=(0.26, 0.74, -6.79)`, `euler=(359.50, 357.83, 0)`. Reconstructing what
`UpdateCameraPosition_3rdPerson` (`:799`) intends — `headPos + quatHeadRot * (0,0,currentCameraZ)`
with `standingEyeHeight = 0.795` and `currentCameraZ ≈ -6.8`:

| | X | Y | Z |
|---|---|---|---|
| Predicted | 0.257 | 0.736 | -6.795 |
| Measured | 0.26 | 0.74 | -6.79 |

and the angle between the camera's forward and the direction from the camera to `headPos` is
**0.043°**. The capsule is dead centre in frame. So whatever is off-centre on screen **is not at the
capsule** — the rendered body is displaced from its own transform.

**Cause: "Root Transform Position (XZ) → Bake Into Pose" was off on both water clips.** A Humanoid
clip whose XZ root motion is not baked applies that motion to the pose relative to the GameObject,
and `m_ApplyRootMotion` is `0` so nothing consumes it. The mesh slides away while the transform
stays put. Capsule, camera, collision and movement all stay correct — only the visible body moves,
which is exactly why the user reported it as a camera problem and why movement still "feels fine".

**This was introduced by the 13:58 Generic→Humanoid conversion**, and could not have shown up
earlier: as Generic clips they were not being applied at all. Mixamo takes are the standard case —
their "in-place" exports carry residual root translation and default to centre-of-mass rather than
original.

**Fix:** `FixHumanoidClipRigs.BakeRootPositionXZ` sets `lockRootPositionXZ = true` on every clip of
both FBXs. Session key bumped to `.v2` so it re-runs.

Note on `clipAnimations`: it was empty before 13:58 (the FBXs used their default take), but the
Generic→Humanoid conversion **materialised it** — both `.meta` files now carry an explicit entry.
The code still handles the empty case by copying `importer.defaultClipAnimations`, because assigning
to an empty `clipAnimations` array silently does nothing; in that path it also carries `loopTime`
over from the imported `AnimationClip.isLooping`, since `defaultClipAnimations` always reports
`loopTime = false` and silently un-looping the idle would be a nasty regression.

**Y is deliberately untouched.** Vertical placement is already correct (`modelY == feetY`, feet on
the terrain) and the user confirmed movement feels right.

`SwimDiagnostics` now logs `hipsOffsetFromModelRoot` and `modelRootOffsetFromCapsule` every tick —
`horiz` on the first is the number that should now be near zero. **Unverified at time of writing.**

## Do NOT "fix" the body yaw

While chasing this I found `bodyYaw` pinned at 219.2° across every sample while the camera yaw
swung to 357.8° — a constant 139° gap — because `UpdateBodyRotation_3rdPerson` (`:821`) has three
branches and **none fires while idle and grounded**: the first needs `!isIdle`, the second needs
`isSliding`, the third needs `!isGettingGroundInfo && rotateCharacterToCameraForward` (which is
`false`, stock default, no scene override).

I proposed driving body yaw from the camera. **The user rejected it: "the way the character is
moving is fully fine."** It is intended behaviour — you look around without the character spinning.
Leave `rotateCharacterToCameraForward` off and leave that method alone. Asking first is the only
reason this did not become a fourth self-inflicted bug.

---

# RESOLVED at 14:05 — verified from runtime logs

**Both original symptoms are fixed.** `[SwimDiag]` output from the 14:05 Play session, in
`Logs/Editor.log:422891-423100`:

```
capsuleY=44.765 feetY=43.890 (h=1.750 r=0.300 c=(0,0,0)) terrainY=43.856 distAboveGround=0.033
groundRay=True modelY=43.890 state=Idle     clip='mixamo.com' human=True len=3.00 ... avatarHuman=True
capsuleY=39.540 feetY=38.665                terrainY=38.659 distAboveGround=0.006
groundRay=True modelY=38.665 state=Swimming clip='mixamo.com' human=True len=4.53 SwimSpeed=0.993
```

- `human=True` on both clips — the Generic/Humanoid binding failure is gone.
- `Idle` (len 3.00 = Treading Water) when still, `Swimming` (len 4.53) when moving. The graph works.
- `modelY == feetY` exactly, and `feetY - terrainY` stays within ±0.07 m across the whole session.
  **The character was never sunk once the clips bound** — so the sinking really was downstream of
  the same bug, as predicted. No terrain work is needed.

`distAboveGround` reaches 1.6–3.4 m briefly while swimming uphill, with `groundRay=False` and
`Grounded=False` at those moments. That is the swim controller legitimately lifting off the seabed,
not a bug — but it is the mechanism described under "A capsule embedded in a heightfield…", so keep
it in mind if `Grounded`-driven behaviour looks odd.

## Second bug, found 14:10: the stray player-root camera (open item 4 came due)

User: *"the camera is not aligned with the character the whole time"* — identical idle and swimming.
**Constant across both states rules out an animation-pose cause**, which killed the first
hypothesis (that the camera orbits `transform.position + Vector3.up * standingEyeHeight`, `:636`,
with `standingEyeHeight = 0.795` — head height for a *standing* body, wrong for a prone swim pose).
Keep that one on file; it would only ever produce a *swimming-only* offset.

The real cause is the stray camera this file has listed as open item 4 since the first session:

| | Real camera | Stray camera |
|---|---|---|
| Object | `&1042309916`, the **child** | `&1071137440`, on the **player root** `&1071137426` |
| `m_Enabled` | 1 | **1** |
| `m_Depth` | 0 (prefab default) | **0** |
| Viewport / display | full / 0 | **full / 0** |
| Driven by | `SUPERCharacterAIO.playerCamera` (`SampleScene.unity:5356`) | **nothing** |

Unity sorts cameras by depth and leaves **ties unordered**, so which one you see is undefined and
can flip across a domain reload. When the stray one wins you are looking out of the player root —
capsule centre, body yaw, no pitch, no boom distance.

It is also **orphaned**: `m_PrefabInstance: 0` and `m_CorrespondingSourceObject: 0`, yet the player
is a prefab instance and this component is *not* in that instance's `m_AddedComponents` (only the
two MonoBehaviours `&1071137432` and `&1071137433` are). Not prefab content, not a tracked override.

**Fix:** `Assets/Editor/FixStrayPlayerCamera.cs` **disables** it (not deletes — nothing this session
created it, and disabling ends the render conflict while staying reversible). It refuses to act if
`playerCamera` is unassigned or points at the root, and logs every still-rendering camera by name,
depth and viewport. `SwimDiagnostics` now also logs a `[SwimDiag/cam]` line per tick with each
enabled camera's offset from the player — **the stray one is identifiable by `dist≈0`**.

Note the scene `Main Camera` (`&330585545`) is also enabled at depth `-1`. That is harmless — it
renders first and gets overdrawn — but it is a third camera on display 0. Left alone.

**Why the earlier cleanup pass missed it.** `FixThirdPersonSetup.cs:109-115` already hunts duplicate
cameras, but its filter is:

```csharp
if (stray == ownCamera || stray.GetComponentInParent<SUPERCharacterAIO>() != null) continue;
```

`GetComponentInParent` matches the object itself, so **every camera on or under the player is
exempt** — including the offending one. The guard is load-bearing and must stay, because the loop
disables the whole `stray.gameObject`, and without it the pass would deactivate the entire player.
It just over-selects. `FixStrayPlayerCamera` is scoped to `GetComponents<Camera>()` on the root and
disables only the *component*, so the two never collide.

This is the third distinct bug traced to `GetComponentInChildren`/`GetComponentInParent` matching
the object itself on this project. See "Critical environment facts" item 8.

---

# Original diagnosis (13:58)

**The two water clips are imported Generic; Josh's avatar is Humanoid. Unity silently refuses to
bind a Generic clip's transform curves to a Humanoid rig, so neither clip plays at all.**

| Animator state | Source FBX | `animationType` |
|---|---|---|
| `Idle` | `Kevin Iglesias/.../Male/Movement/Treading Water (1).fbx` | **2 = Generic** |
| `Swimming` | `Kevin Iglesias/.../Male/Movement/Swimming (1).fbx` | **2 = Generic** |
| `Moving` | `HumanM@Walk01_Forward [RM].fbx` | 3 = Humanoid |
| `Moving_Fast` | `HumanM@Run01_Forward [RM].fbx` | 3 = Humanoid |
| `AirBorn` | `HumanM@Jump01.fbx` | 3 = Humanoid |
| `Slide` | `Character_Base.fbx` (placeholder) | 2 = Generic |

`Josh.fbx.meta:110` is `animationType: 3`. Because `InWater` is unconditionally true, the only two
reachable states are `Idle` and `Swimming` — i.e. **exactly the two Generic ones**. That accounts
for all three reported symptoms at once.

**The proof was already in the saved scene and had been read past three times.**
`SampleScene.unity:5197` stores the Animator's own `m_WarningMessage`:
*"Binding warning: Some generic clip(s) animate transforms that are already bound by a Humanoid
avatar. These transforms can only be changed by Humanoid clips. Transform 'mixamorig:Hips'…"*

Note the **non-`(1)` originals are Generic too** (`Male/Idles/Treading Water.fbx`,
`Male/Movement/Swimming.fbx`) — these are Mixamo downloads dropped into the Kevin Iglesias folders,
not pack content. Swapping to the originals would not have helped.

**Fix applied:** `Assets/Editor/FixHumanoidClipRigs.cs` sets both to Humanoid /
`CreateFromThisModel` and reimports, then logs a readback of every state's resolved clip and its
`isHumanMotion`. `Assets/Scripts/SwimDiagnostics.cs` (attached to the player by the same script)
logs the live state once a second for 20 s and then disables itself. Backups in
`Backups/2026-07-26-humanoid-clip-rigs/`. **Both are scaffolding — delete once confirmed.**

## Prior hypotheses, resolved

- **H1 (stale code) is dead.** `Logs/Editor.log` shows three clean Play sessions after the 13:41
  fix (lines 421220, 421353, 421487) with no exceptions. The user re-ran Play; symptoms persisted.
- **H2 (Josh.fbx root at the hips) is dead.** Parsed the binary FBX (v7700): the top-level nodes are
  the mesh at the origin and `mixamorig:Hips` at `Lcl Translation` Y = **107.61 cm**. The FBX root
  origin is at the **feet**, which is what `stickRendererToCapsuleBottom` requires. This was the
  most-suspected hypothesis and it was wrong.
- **The sinking is most likely a consequence of the same bug**, not a separate one: with no bound
  curves the humanoid pose is never driven, so the rig collapses toward the avatar default while
  SUPER keeps pinning the FBX root to the capsule bottom. `SwimDiagnostics` settles this — it logs
  `capsuleY`, `feetY`, `terrainY` and `modelY` side by side. If the capsule is correct and only
  `modelY`/the visible mesh is low, it was the clips.

## What is verified applied, on disk

Do not re-apply these; confirm and move on. All confirmed by reading the saved assets at 13:45,
after the user reported the symptoms.

| Where | Field | Value |
|---|---|---|
| `SampleScene.unity:5628` | `terrainLimit` | `{fileID: 1071631721}` = the scene `Terrain` |
| `SampleScene.unity:5629` | `terrainClearance` | `0.8` |
| `SampleScene.unity:5623` | `rotationDamping` | `0` |
| `SampleScene.unity:5632` | `cameraTransform` | `{fileID: 1042309914}` = the child camera's transform |
| `SampleScene.unity:5808-5822` | `SwimAnimatorBridge` | attached to the player root, `m_Enabled: 1`, `inWaterParameter: InWater`, refs null (resolved in `Awake`) |
| `JoshAnimator.controller:353` | `InWater` | bool parameter present |

`Logs/Editor.log:421025-421198` has the full apply transcript, ending in `Saved the scene.` and
`Saved the animator controller.`

## If the sinking survives the clip fix

Read the `[SwimDiag]` lines out of `Logs/Editor.log` first — they give `capsuleY`, `feetY`,
`terrainY`, `distAboveGround`, `groundRay`, `modelY`, the live animator state, the resolved clip and
its `isHumanMotion`, and every parameter. Do not theorise ahead of that output again.

The surviving hypothesis for the position half:

**Two position writers, and the clamp loses.** `UnderwaterSwimController.Update` clamps and assigns
`transform.position`; SUPER then moves the Rigidbody by velocity in `FixedUpdate`, *after* the clamp
has run. Anything physics does can put the capsule back. This is the architectural conflict in
"Open items" item 2 and is the real fix if the diagnostics show `feetY` below `terrainY`.

Also still true and worth re-reading if `Grounded` misbehaves: `Vel` is fed
`p_Rigidbody.linearVelocity.sqrMagnitude` (`:1416`), which stays near zero because the swim
controller moves the player by assigning `transform.position` and never produces rigidbody velocity.

---

## Fixed and confirmed earlier (13:41) — camera shake

**Third-person camera shake when turning.** Two stacked bugs:

1. `UnderwaterSwimController.cameraTransform` pointed at the **player root**, so the controller
   steered off its own axes: holding left/right asked the body to face its own right, which moved
   the target again the next frame — a feedback loop that spun the root at roughly 800°/s.
   `FixThirdPersonSetup.cs:80` caused this via `GetComponentInChildren<Camera>(true)`, which matches
   the object itself, and **the player root carries a stray enabled Camera** (`SampleScene.unity:5749`).
   Fixed by pointing it at the child camera, with a guard in `FixFirstPersonTurn.cs` that now
   rejects the root no matter which path supplies it.
2. Residual judder while the body was mid-turn: the camera is a **child** of the player root, and
   SUPER pins its world pose in `FixedUpdate` (`:816-818`). Any root rotation from `Update` swings
   the child around a multi-metre lever arm on frames with no physics step and gets snapped back on
   frames that have one. Fixed with `rotationDamping = 0`, which makes the `Slerp` at
   `UnderwaterSwimController.cs:198` return the current rotation — the assignment becomes a no-op
   and the pivot compensation below it cancels. Body yaw is now owned solely by SUPER's
   `UpdateBodyRotation_3rdPerson`, which runs immediately before the camera pin.

Position writes, by contrast, are self-correcting: the drag equals the delta the re-pin would apply.
Only rotation writes produce error. Worth remembering if this class of bug reappears.

**Consequence to watch:** SUPER only rotates an *ungrounded* body if
`SUPERCharacterAIO.rotateCharacterToCameraForward` is enabled, and it is **off**. If the character
strafes without facing its direction of travel while off the seabed, that field is the fix.

---

## The animator, fully mapped

This was the biggest misunderstanding in previous sessions. **The controller is a land controller
with `Idle` swapped to a water clip and a `Swimming` state bolted on.**

| State | Clip |
|---|---|
| `Idle` (default) | **Treading Water** — `Male/Movement/Treading Water (1).fbx`, **not** the `Male/Idles/` copy |
| `Moving` | Walk01_Forward [RM] |
| `Moving_Fast` | Run01_Forward [RM] |
| `AirBorn` | Jump01 |
| `Swimming` | Swimming — `Male/Movement/Swimming (1).fbx`, **not** `Male/Movement/Swimming.fbx` |
| `Slide` | Character_Base (placeholder) |

**The `(1)` suffixes are load-bearing.** There are duplicate copies of both water FBXs and the
controller references the duplicates. Both copies were Generic; editing the non-`(1)` one changes
nothing. `FixHumanoidClipRigs.cs` addresses them by GUID for this reason.

**`Idle` playing Treading Water is deliberate, not a bug.** Do not "fix" it.

Transitions, after this session's changes:

| From | To | Condition |
|---|---|---|
| AnyState | Slide | `Sliding is true` |
| AnyState | AirBorn | `Grounded is false AND InWater is false` |
| Idle | Moving | `Idle is false AND InWater is false` |
| Idle | Swimming | `InWater is true AND SwimSpeed > 0.1` (no exit time) |
| Swimming | Idle | `SwimSpeed < 0.1` (no exit time) |
| Swimming | Idle | `InWater is false` |
| Moving | Idle / Moving_Fast | `Idle is true` / `Sprinting is true` |
| Moving_Fast | Moving / Idle | `Sprinting is false AND Idle is false` / `Idle is true` |
| AirBorn | Idle / Moving | `Grounded is true AND Vel < 0.1` / `Vel > 0.1` |
| Slide | Idle / Moving | `Sliding is false AND Vel < 0.1` / `Vel > 0.1` |

What was wrong before, and why the swim states were dead:

- `Idle -> Moving` fires on the **first frame** of input (`Idle is false`, no exit time), while
  `SwimSpeed` needs a few frames to climb past 0.1 — and `Idle -> Swimming` was additionally gated
  behind `hasExitTime` at 0.9375, so it could only fire in the last 6% of the idle clip. `Moving`
  always won, and `Moving` has **no path to `Swimming`**. Hence the `InWater` gate.
- `Swimming -> Idle` required `SwimSpeed < 0.1` **AND** `IsSwimming is true`. Both are written from
  the same normalised speed in `UpdateAnimator`, so `IsSwimming` is already false by the time
  `SwimSpeed` drops that low. The condition was unsatisfiable and `Swimming` was a trap.
- Nothing told the animator it was in water at all. `Assets/Scripts/SwimAnimatorBridge.cs` now
  publishes `InWater` each frame from `UnderwaterSwimController.IsInWater`.

**The controller has seven parameters** — `Vel`, `Idle`, `Sliding`, `Grounded`, `Sprinting`,
`IsSwimming`, `SwimSpeed`, plus `InWater` added this session. Two earlier handoffs claimed six and
that `IsSwimming` was undefined. **That was wrong**, and acting on it would have deleted the only
condition that could reach `Swimming`. `ClearSwimmingBool` in `FixFirstPersonTurn.cs` is
deliberately `false` for this reason — leave it off.

## The always-in-water decision

`requireWaterVolume` is **off** on the swim controller, so `IsInWater` returns `true`
unconditionally, so `InWater` is always true. Therefore:

- Standing still → Treading Water
- Moving → Swimming
- `Moving` (Walk) and `Moving_Fast` (Run) are **unreachable**

This matches the asset setup as the user built it. To restore land locomotion it is one checkbox:
tick `requireWaterVolume` and have a `WaterVolume` cover the water. The bridge and the graph then
switch with no code change. **Trap:** `whatIsGround` is `-1` and queries hit triggers, so a water
trigger on a normal layer registers as walkable ground *and* as camera obstruction — the earlier
attempt put the volume on layer 2 (Ignore Raycast) for exactly this reason.

## Terrain: what the clamp does and why 0.8

`UnderwaterSwimController` moves the player by assigning `transform.position`, which is a teleport
that ignores colliders. Its only substitute for collision is `ApplyMovementLimits`, which was inert
because `terrainLimit` was unassigned.

Nothing else pushes the capsule out either:

- SUPER **never snaps to the ground**. It computes `currentGroundInfo.playerGroundPosition` at
  `:1015` and then only ever draws it as a gizmo at `:1515`.
- SUPER **hard-writes `p_Rigidbody.linearVelocity`** every `FixedUpdate` while grounded (`:852`),
  and while idle adds a force that explicitly cancels gravity and zeroes vertical velocity (`:848`).
  That destroys PhysX's depenetration velocity before it can move the capsule.
- Moving or jumping breaks the lock, which is why the character used to pop out: `Jump()` sets
  `Jumped = true`, failing the `!Jumped` guard at `:844` so SUPER stops clobbering velocity; and
  movement trips the stair-step logic at `:1003-1015`, which reads the terrain slope ahead as a
  riser and lifts the capsule.

**A capsule embedded in a heightfield makes SUPER's ground check fail.**
`isInContactWithGround` is `Physics.Raycast(transform.position, Vector3.down, ...)` at `:960`, and a
downward ray originating below a terrain surface does not hit it. So `Grounded` goes false, which
used to pin the animator in `AirBorn` via the AnyState transition. Remember this if `Grounded`
misbehaves again.

**Clearance geometry.** Capsule is height 1.75, radius 0.3, centre `(0,0,0)`, so the **root pivot
sits at the capsule centre, 0.875 above the feet**, and `terrainClearance` is measured from the
root. `0.8 = capsule.height/2 - 0.075`: deliberately a little *below* the resting height so the
clamp acts as a safety floor rather than a constant upward push. Clamping at exactly 0.875 makes the
character hover on slopes, because a capsule on an incline legitimately rests closer to the
vertically-sampled surface point than it does on the flat.

**Correction (13:58):** an earlier draft claimed the `Character` model child sits at local Y
`-0.875`. It does not — `SampleScene.unity:5108` has its `m_LocalPosition.y` at **0**. It does not
matter either way, because `stickRendererToCapsuleBottom` overwrites the model's world Y every
Update regardless of the authored value. `SwimDiagnostics` logs both `modelY` and `feetY` so this
never has to be inferred from the scene YAML again.

---

## Critical environment facts

1. **The real work is untracked. Always work in `/Users/andrewkao/Underwater Proj`.**
   Only the stock URP template is committed. `git worktree` checkouts of this repo are **empty** —
   editing files there silently edits nothing.

2. **The Unity Editor is normally open.** Never edit `.unity` or `.prefab` YAML directly; the Editor
   holds the scene in memory and overwrites it. Apply scene changes through an **Editor script**.
   `Assets/Editor/FixSwimAndTerrain.cs` is the current working pattern (idempotent, auto-runs on
   entering Edit mode, session-keyed so it applies once, plus a `Tools/` menu item).

3. **Unity only recompiles and runs `[InitializeOnLoad]` scripts when its window regains focus.**
   Writing an Editor script does nothing until the user clicks into Unity. Expect a round trip.
   Related: **creating or editing a script cancels Play mode** via forced synchronous recompile, so
   after writing scripts always have the user re-enter Play rather than trusting a session that was
   already starting.

4. **`Logs/Editor.log` in the project root is the source of truth** — not
   `~/Library/Logs/Unity/Editor.log` (stale), not the saved scene, and **not a screenshot of the
   Game view** (see item 12). Currently ~42.5 MB; check size before grepping, and `tail`/`awk` a
   range rather than dumping it. **Beware `grep | tail`** when looking for whether something ran: it
   hides earlier matches and cost an earlier session a wrong conclusion.
   Corollary learned the hard way at 14:35: **when a fix is meant to change a logged number, diff it
   against a pre-fix baseline in the same log before declaring the diagnosis correct.** The
   root-motion theory survived a whole handoff only because nobody compared `headBoneVP` before and
   after; it had never moved.

5. **Active Input Handling is "Input System Package (New)" only.** Any `UnityEngine.Input` call
   throws at runtime. Use `Keyboard.current` / `Mouse.current`. Note `SUPERCharacterAIO.cs:651`
   (`ViewInputModes.Retro`) still calls `Input.GetAxis` and would throw if that mode were selected.

6. **Inspector edits made during Play mode are discarded on Stop.** Verify against the saved asset
   and the log, not against a description.

7. **`UnderwaterSwimController` is a git-URL package**, `com.budorf.underwater-swim`, from
   `https://github.com/mdj128/yt-underwater-scene.git?path=UnderwaterSwimController`, pinned in
   `packages-lock.json` at `7e330ca6…`. Source lives at
   `Library/PackageCache/com.budorf.underwater-swim@298f1f9390b6/`, `.meta` GUID
   `dfd997346ebaa3e4b8ce1d25b9b3586e`. Its asmdef is `autoReferenced: true`, so scripts in `Assets`
   can reference its types directly.

8. **Hierarchy gotcha.** `GetComponentInChildren<Camera>(true)` on the player returns a camera whose
   GameObject is `Character.001` — the player **root** — because the root carries a stray enabled
   Camera of its own and `GetComponentInChildren` matches the object itself first. This mistake has
   now been made three times. Use `SUPERCharacterAIO.playerCamera`, and still assert the result is
   not the root.

9. **`Animator.parameters` is unreliable in Edit mode** before the controller is bound. Read the
   `AnimatorController` **asset** through `AssetDatabase` instead. (It *is* reliable in `Awake` at
   runtime, which is what `SwimAnimatorBridge` relies on.)

10. **`SUPERCharacterAIO` lives in the `SUPERCharacter` namespace**, so any script referencing it
    needs `using SUPERCharacter;` (or the fully-qualified `SUPERCharacter.SUPERCharacterAIO`, which
    is what `FixFirstPersonTurn.cs:106` does). `UnderwaterSwimController`, by contrast, is in the
    **global** namespace and needs no using. Easy to trip over because the two are used side by side.
    Related: **`PerspectiveModes` is a namespace-level enum, not nested in `SUPERCharacterAIO`** —
    it is `PerspectiveModes._3rdPerson`, never `SUPERCharacterAIO.PerspectiveModes._3rdPerson`
    (`SUPERCharacterAIO.cs:1618`). There are no `.asmdef` files anywhere in `Assets`, so everything
    lands in `Assembly-CSharp` and Editor scripts can reference runtime types freely.

11. **Do not remove the git dependency from `manifest.json` while the package is embedded** — Unity
    deletes its `Library/PackageCache` copy and there is no local backup. Recovery needed a network
    re-fetch. Copy first, leave the manifest alone until the copy is verified.

12. **The user's Game view is set to Scale 4x, so screenshots of it are cropped and scrolled.**
    A centred character can look badly off-centre. Never act on an apparent framing problem from a
    screenshot — ask for Scale 1x, or read the viewport numbers the diagnostics print. The tell that
    you are looking at a crop rather than a real offset: the horizontal and vertical errors are
    *different sizes*. Full write-up under "Trap: the Game view Scale slider crops the frame".

13. **Importer `.meta` field names are not the C# API names.** `lockRootPositionXZ` serialises as
    `loopBlendPositionXZ`, `lockRootHeightY` as `loopBlendPositionY`, `lockRootRotation` as
    `loopBlendOrientation`. Grepping a `.meta` for the API name silently finds nothing and reads as
    "the field is absent" — which is exactly how the 14:35 bake state was first misread.

---

## Backups and revert

`Backups/2026-07-26-swim-and-terrain-fix/` — outside `Assets/`, so Unity does not import it. Holds
pre-change copies of `SampleScene.unity`, `JoshAnimator.controller`, `FixFirstPersonTurn.cs` and
`FixThirdPersonSetup.cs`, plus **`REVERT.md`** with every field change (before → after), the reason,
and a full-revert command block.

**A revert must also delete `Assets/Editor/FixSwimAndTerrain.cs`**, or it re-applies itself on the
next entry into Edit mode and silently undoes the revert.

Nothing is committed. The entire real project is untracked — worth discussing `.gitignore` and Git
LFS for the asset packs. `UnderwaterSwimController.cs` exists **only** in gitignored
`Library/PackageCache` and is pinned to a third-party repo; if that repo disappears, the project's
movement code is gone.

---

## Decisions made

- **Do not use Cinemachine here.** `SUPERCharacterAIO` writes the camera's `localPosition`, FOV and
  Z-distance every frame; a `CinemachineBrain` would fight it. Keep the package for cutscenes.
- **Do not hand-edit `SUPERCharacterAIO.cs`.** The `Start()` crash is sidestepped by turning off
  `autoGenerateCrosshair` and `drawPrimitiveUI`, not by patching third-party code.
- **Keep the `AutoBubbleEmitter`** — the user asked for it explicitly. `UnderwaterVisuals`
  auto-spawns it once a `WaterVolume` exists, and it is baked into the saved scene (~120 KB of
  particle YAML).
- **`Idle` = Treading Water is intentional.** Not a bug.
- Unity's API updater already migrated `PhysicMaterial` → `PhysicsMaterial`; the `error CS0619`
  entries in the log are historical.
- A previous full underwater-mode implementation was **reverted at the user's request** — it broke
  movement and terrain interaction. Do not re-apply it wholesale. Findings worth keeping:
  `SUPERCharacterAIO` has **zero** swim/water support; `enableMovementControl` (`:103`) and
  `enableCameraControl` (`:35`) are separate, and gating movement while leaving the camera on is the
  right split — but it is **not sufficient**, because SUPER writes body rotation from outside that
  gate in both perspectives (`UpdateBodyRotation_3rdPerson` at `:821`, and `RotateView` at `:631`
  with no guard at all), and the guards it does have read `isGettingGroundInfo`, which goes **stale**
  once `GetGroundInfo()` stops running.

## Open items, in priority order

1. **Delete the scaffolding — the framing fix is confirmed, so this is now pure cleanup.**
   Remove `Assets/Scripts/SwimDiagnostics.cs` (and its component on the player) and all five
   `Assets/Editor/Fix*.cs` scripts. **Do not delete `Assets/Scripts/ThirdPersonCameraPivot.cs`** —
   it is the fix, not scaffolding, and removing it puts the character back below centre.
   Read "Backups and revert" first: deleting `FixSwimAndTerrain.cs` is *required* for any revert,
   and the other four have already baked their edits into the saved scene and the importers.
   One wrinkle: `FixHumanoidClipRigs.AttachCameraPivot` is what attaches the pivot component, so
   confirm the component is present and serialised in `SampleScene.unity` **before** deleting that
   script, or a future scene reset will have nothing to re-add it.
2. **The architectural conflict is still unresolved and is the central question for this project.**
   `SUPERCharacterAIO` (rigidbody, gravity, ground checks, owns the camera) and
   `UnderwaterSwimController` (transform writes, no gravity, no collision) both move the same
   GameObject every frame. Every bug in this file is a symptom. The user wants to keep walking on the
   seabed and swap on water volume, with full 3D body pitch while swimming.
3. **Body pitch while swimming does not exist yet.** The swim controller's rotation is disabled
   (`rotationDamping = 0`) and SUPER only ever writes yaw, `Euler(0, y, 0)`.
4. ~~**The stray enabled `Camera` on the player root.**~~ **Done at 14:10** — disabled by
   `FixStrayPlayerCamera.cs`. The component still exists on the root, disabled; delete it by hand
   when convenient.
5. **Both water clips have `loopTime: 0`** (`Treading Water (1).fbx.meta:46`, `Swimming (1).fbx.meta:46`),
   set when the 13:58 Humanoid conversion materialised `clipAnimations`. A non-looping clip in a
   Mecanim state holds its last frame instead of repeating, so Treading Water should freeze after
   3.0 s and Swimming after 4.53 s. **Not changed** — it was out of scope for the reported bug and
   the user has said the animation looks right, so watch for a frozen pose before touching it. If it
   does freeze, set `loopTime = true` on both in `UnbakeRootPositionXZ` (renamed from
   `BakeRootPositionXZ` at ~15:00). Still `loopTime: 0` on disk as of 16:30. (`SwimDiagnostics`
   prints `normalizedTime % 1`, so its wrapping is the modulo, **not** evidence of looping — fix
   that log line before using it to judge this.)

6. **Terrain has no terrain layers**, hence the pink/white checkerboard. Not a bug.
7. **`Assets/CameraSwitcher.cs` is a dead orphan.** Not attached to anything, redundant with SUPER's
   built-in Q toggle, would throw under new-input-only. Safe to delete.
8. **Editor scripts are scaffolding.** `FixThirdPersonSetup.cs`, `FixFirstPersonTurn.cs`,
   `FixSwimAndTerrain.cs`, `FixHumanoidClipRigs.cs`, `FixStrayPlayerCamera.cs` and
   `Assets/Scripts/SwimDiagnostics.cs` can all be deleted once the setup is confirmed good — but
   note item 2 under Backups first. Their edits are already baked into the saved scene and the
   importers; only `SwimDiagnostics` also needs removing from the player GameObject.
   **`Assets/Scripts/ThirdPersonCameraPivot.cs` is NOT in this list** — it runs every frame in the
   built game and is the reason the character is centred. See item 1.

## Key files

| Path | Role |
|---|---|
| `Assets/Scenes/SampleScene.unity` | The only real scene |
| `Assets/Scripts/SwimAnimatorBridge.cs` | Publishes `InWater` to the Animator (new this session) |
| `Assets/Editor/FixSwimAndTerrain.cs` | Terrain floor + animator graph surgery (new this session) |
| `Assets/Editor/FixFirstPersonTurn.cs` | Camera rewire + `rotationDamping` |
| `Assets/Editor/FixThirdPersonSetup.cs` | The 11:44 fix (camera, animator slots, Q toggle) |
| `Assets/Player Related/JoshAnimator.controller` | 8 params now; see the animator tables above |
| `Assets/Editor/FixHumanoidClipRigs.cs` | Generic → Humanoid reimport of the two water clips, clip readback, XZ root-motion **unbake**, and attaches `ThirdPersonCameraPivot` (13:58, extended 14:35, corrected ~15:00 — session key `.v3`) |
| `Assets/Editor/FixStrayPlayerCamera.cs` | Disables the duplicate depth-0 camera on the player root (14:10) |
| `Assets/Scripts/ThirdPersonCameraPivot.cs` | **The framing fix — not scaffolding, do not delete.** Own pivot height for the 3rd-person camera so the character sits mid-frame; leaves `standingEyeHeight` free for 1st person. Plus `lateralOffset` / `heightOffset` for deliberate framing (~15:00) |
| `Assets/Scripts/SwimDiagnostics.cs` | Temporary 1 Hz runtime dump — position, animator, cameras, viewport framing. Self-disables after 75 s (13:58) |
| `Assets/Player Related/Josh.fbx` | Mixamo rig, `animationType: 3` (Humanoid); model GameObject is named `Character`. Root origin is at the **feet** (Hips at Y = 107.6 cm) — verified by parsing the binary FBX |
| `Library/PackageCache/com.budorf.underwater-swim@298f1f9390b6/` | Swim controller source (gitignored, git-pinned) |
| `Assets/SUPER Character Controller/…/SUPERCharacterAIO.cs` | ~2,200 lines. `Start()` 297, `Update()` 422, `FixedUpdate()` 580, `RotateView()` 611, `UpdateCameraPosition_3rdPerson()` 799, `UpdateBodyRotation_3rdPerson()` 821, `MovePlayer()` 835, `GetGroundInfo()` 954, `UpdateAnimationTriggers()` 1399 |
| `Backups/2026-07-26-swim-and-terrain-fix/REVERT.md` | Every change this session, with undo steps |
| `Logs/Editor.log` | Diagnose from here first |
