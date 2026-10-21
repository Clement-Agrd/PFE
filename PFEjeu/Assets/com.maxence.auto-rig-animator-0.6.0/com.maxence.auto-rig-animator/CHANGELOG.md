# Changelog

All notable changes to this package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses [Semantic Versioning](https://semver.org/).

## [0.6.0] - 2026-10-20

### Added
- `MarkerPlacementWindow`: three "Vue verrouillée" buttons (Face / Profil / Dessus) lock the active
  Scene view to an orthographic camera framed on the target mesh, along a generic world axis. No
  perspective distortion, no accidental rotation while aiming — click "Recréer" (the existing
  collider-warning button) is unrelated; re-click a view button any time to re-snap. These are
  generic world-axis views, NOT aligned to the character's own front: at placement time no markers
  exist yet, so the character's actual facing direction isn't known (that's only inferred later, in
  `SkeletonBuilder`, from the Left/Right leg markers once placed).
- `MarkerPlacementWindow`: new "Accrocher au sommet le plus proche" toggle (on by default). When a
  raycast hit lands within ~5% of the mesh's bounding-box diagonal from an actual mesh vertex, the
  landmark snaps to that vertex instead of the interpolated point on whichever triangle was clicked
  — anchoring to real topology (an edge loop at a joint, say) rather than an arbitrary surface point.
  Each of the front/back hits from the existing double-raycast (0.4.1) is snapped independently
  BEFORE taking their midpoint, so this doesn't undo that fix's interior-centering: snapping the
  final midpoint back to a surface vertex would have pulled wide/round parts (hips, head) back to
  the skewed-toward-camera behavior 0.4.1 fixed in the first place.

### Known limitations
- Vertex snapping can occasionally anchor to a plausible but not-quite-right vertex on a very
  low-poly or unevenly-tessellated mesh near the intended joint; turn the toggle off for that mesh
  if it happens.
- The locked views don't prevent further camera movement (middle-mouse pan, scroll zoom, manual
  rotation) — they just snap once on click; re-click the button to restore the locked framing.

## [0.5.1] - 2026-10-20

### Fixed
- `MeshSkinningConverter.cs` failed to compile (`CS0104: 'Object' is an ambiguous reference between
  'UnityEngine.Object' and 'object'`). Adding `using System;` in 0.5.0 made the bare `Object` call on
  `Object.Instantiate(sourceMesh)` ambiguous between `System.Object` and `UnityEngine.Object`.
  Qualified it explicitly as `UnityEngine.Object.Instantiate(...)`. Checked every other file that
  also gained a `using System;` in 0.5.0 (`AutoRigPipeline.cs`, `AutoSkinner.cs`,
  `SkeletonBuilder.cs`) for the same mistake — none had it.

## [0.5.0] - 2026-10-20

Phase 5a: robustness pass, ahead of batch processing (5c) and large-scale performance work (5b) —
both future phases will call `AutoRigPipeline` directly without going through the UI, so this pass
validates at that boundary, not just in the windows.

### Added
- `AutoRigPipeline.BuildSkeletonAndSkin` / `BuildHumanoidAvatar` now validate every input up front
  (null object, missing/incomplete markers, empty mesh) and wrap the actual build steps in a
  try/catch. On any unexpected exception, or on the user cancelling the new skinning progress bar,
  everything created since the start of that call is automatically rolled back
  (`Undo.RevertAllDownToGroup`) — a failed attempt never leaves half-built GameObjects/components
  behind. New outcomes: `Result.EmptyMesh`, `Result.InvalidInput`, `Result.Cancelled`,
  `Result.UnexpectedError`, and `AvatarResult.InvalidInput` / `AvatarResult.UnexpectedError`.
- **Behavior change**: on `AvatarResult.BuildThrew` or `AvatarResult.InvalidAvatar`, the partially
  built `_Rig` root is now cleaned up automatically. Before 0.5.0 it was left in the scene and
  required a manual Ctrl+Z to remove.
- `AutoSkinner.ComputeBoneWeights` shows a cancelable progress bar for meshes above ~2000 vertices.
  Note: the loop itself is fast even at tens of thousands of vertices (a few million simple float
  operations run in milliseconds) — this isn't fixing a real bottleneck, it's here so a large mesh
  never looks frozen with no feedback, and so it can be cancelled instead of waited out.
- `MeshSkinningConverter.Convert` now checks that the computed bone-weight count matches the mesh's
  vertex count before assigning it, failing with a clear message instead of a cryptic Unity error.
- `MarkerPlacementWindow` now rejects two targets it used to accept but couldn't safely work on:
  a mesh with 0 vertices, and — more importantly — a **Prefab asset dragged straight from the
  Project window** (as opposed to a scene instance of it): adding the temporary MeshCollider to the
  asset itself would have modified it for every instance in the project, not just a scene copy.
- `MarkerPlacementWindow` now detects if its temporary collider gets destroyed by something else
  while the window is open (another tool, a script, a manual delete) and shows a one-click
  "Recréer le collider" button instead of clicks silently doing nothing with no explanation.
- `SkeletonBuilder.Build` and `AutoSkinner.ComputeBoneWeights` now throw a clear
  `ArgumentNullException` on null input instead of failing deeper with a less obvious
  `NullReferenceException` — defense in depth for direct (non-UI) callers.

### Not yet implemented
- 5b: performance pass for very large meshes (spatial acceleration if ever actually needed).
- 5c: batch processing (queue several already-marked meshes, or apply/validate a clip across
  several existing rigs, without repeating the UI flow one object at a time).
- 5d: team-oriented documentation (onboarding guide, in-editor tooltips).
- 5e: rig/retargeting quality (pose tolerance beyond strict T-pose, twist bones, automatic skin
  quality report).

## [0.4.3] - 2026-10-20

### Added
- `ClipHumanoidFixer`: when the Preview Animation window flags a clip as not Humanoid, a new
  "Corriger automatiquement (import → Humanoid)" button switches that clip's SOURCE FILE from
  Generic to Humanoid import (`ModelImporter.animationType` + `CreateFromThisModel`) and validates
  the resulting Avatar. If Unity can't build a valid Humanoid Avatar from it (not a recognizable
  biped), the setting is rolled back automatically instead of leaving a broken import behind.
- A Generic clip only retargets correctly when its bone names/paths match the target skeleton
  exactly, which an auto-rigged skeleton (built from your own clicked landmarks) essentially never
  does — this is what caused the clip to apply a broken partial pose (character twisted or lying
  down) even after the 0.4.2 bone-orientation fix. Making the clip Humanoid fixes this because
  Humanoid retargeting works by muscle data, independent of bone names.

### Known limitations
- Only works on a clip that comes from a model file (FBX, etc.) imported through `ModelImporter`. A
  native `.anim` clip authored directly in Unity has no such setting; the button reports this
  instead of doing nothing silently.
- Changing an asset's import settings is **not covered by Unity's Undo system** — this is true of
  the Inspector's own Rig tab too, not something unique to this button. Flip the setting back by
  hand (or re-run the button after a manual edit) if you need to revert.

## [0.4.2] - 2026-10-20

### Fixed
- **Root cause of the "character ends up lying down" / "mesh doesn't stay attached" bug during
  animation preview.** `SkeletonBuilder` used to create every bone with an identity rotation
  (position only). A Humanoid Avatar retargets a clip's muscle data onto each bone's LOCAL axes —
  with every bone sharing the exact same world-aligned axes regardless of which way it actually
  points, the retargeted rotations landed on the wrong local axes, which can look like the mesh
  tearing away from the skeleton and/or the whole character ending up on its side.
- `SkeletonBuilder` now orients each bone to aim down its own limb/spine segment (`AimChildName` on
  `BoneDefinition`), using a character forward/right/up computed from the Left/Right leg (or hand)
  markers. Rotating a bone shifts its children's world positions in Unity, so a third pass restores
  every bone to its exact marker position afterward.
- **This only takes effect on a skeleton built from scratch.** An already-rigged object still has
  the old identity-rotation bones; Undo the previous "Build Skeleton"/Avatar steps (or start from a
  fresh copy of the mesh) and rebuild before testing playback again.

## [0.4.1] - 2026-10-20

### Fixed
- Landmarks are now placed at the midpoint of the front and back raycast hits instead of right on
  the clicked (front) surface — fixes visibly skewed rigging/skinning on wide, round parts (hips,
  head), where a surface-only point sat biased toward the camera.
- `AutoSkinner` now blends the two nearest bone segments (up to 4 bones) instead of only the single
  nearest one, smoothing the hard seams that could appear at joints where several bones meet (e.g.
  Hips, where Spine and both upper legs start).
- Landmark raycasting now explicitly picks the nearest matching hit instead of the first one found
  in `Physics.RaycastAll`'s unordered result — a latent bug that could pick a farther point on a
  non-convex mesh.

## [0.4.0] - 2026-10-19

### Added
- `Tools > Auto Rig > Preview Animation` window: drag any AnimationClip onto a rig built by this
  tool and scrub/play it immediately via `AnimationMode.SampleAnimationClip`, no AnimatorController
  needed. Warns if the target has no `Animator`/Avatar or if the clip is not marked Humanoid.

### Not yet implemented
- Premade clip library (browsing/picking from a bundled or project set of clips).
- In-editor pose/IK tool to create new animations and bake them into AnimationClips.

## [0.3.0] - 2026-10-19

### Added
- `Générer l'Avatar Humanoid` button: reparents the mesh and the skeleton under a new common root
  (`AvatarRootBuilder`), builds a `HumanDescription` from the bone hierarchy (`HumanoidAvatarBuilder`)
  and calls `AvatarBuilder.BuildHumanAvatar`. On success, the Avatar is saved under
  `Assets/AutoRig/Avatars/` and an `Animator` is added to the new root with that Avatar assigned.
- Validity check (`avatar.isValid` / `avatar.isHuman`) with a clear message instead of a silently
  broken Avatar.

### Known limitations
- The mesh must be roughly in T-pose when the landmarks were placed (Phase 1); AvatarBuilder uses
  each bone's current local transform as its bind pose.
- Saving the `.asset` file is not covered by Unity's Undo system: undoing the rig after this step
  leaves the asset file on disk (safe to delete manually).

### Not yet implemented
- Premade animation library and in-editor pose/IK tool.

## [0.2.0] - 2026-10-19

### Added
- `Construire le squelette` button: generates the bone hierarchy from the placed landmarks
  (`SkeletonBuilder`), computes bone weights automatically (`AutoSkinner`, closest-segment linear
  blend) and swaps the mesh's `MeshFilter`/`MeshRenderer` for a `SkinnedMeshRenderer`
  (`MeshSkinningConverter`) — all in one Undo step.
- Guard against rebuilding on an already-rigged object (must Undo first).

### Known limitations
- Skin weights use a simple nearest-segment linear blend, not heat-diffusion weights: joints can
  look slightly faceted up close. Acceptable for Phase 2; may be revisited later.
- Bones are created with identity rotation (position-only). Fine for skinning; to be checked again
  in Phase 3 against `AvatarBuilder.BuildHumanAvatar`'s requirements.

### Not yet implemented
- Humanoid Avatar generation.
- Premade animation library and in-editor pose/IK tool.

## [0.1.0] - 2026-10-19

### Added
- `Tools > Auto Rig > Place Landmarks` window: click landmarks for a simplified 20-bone humanoid
  skeleton (Hips, Spine, Chest, Neck, Head, arms, legs, optional shoulders/toes) directly on a raw
  mesh in the Scene view, with auto-advance to the next required landmark.
- Movable marker handles and per-bone progress list in the window.
- Temporary `MeshCollider` management for precise raycasting on meshes with no collider yet.

### Not yet implemented
- Skeleton / bone hierarchy generation from the placed markers.
- Automatic skinning (bone weights).
- Humanoid Avatar generation.
- Premade animation library and in-editor pose/IK tool.
