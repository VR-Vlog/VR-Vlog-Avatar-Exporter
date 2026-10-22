# NDMF 1.6.0 and later 1.x

The exporter runs NDMF's build on its own export copy. The accepted range is
`ndmfMinimum` (1.6.0) and later 1.x releases (`ndmfMajor` 1), stable releases
only. NDMF before 1.8.0 has no platform API; there the exporter makes the
`BuildContext` with `(GameObject, string, bool)` and starts at `Resolving`.
1.5.x and older stop before processing, because their `BuildContext` has no
`AssetSaver`. The reasons are in
`Documentation~/decisions/2026-10-22-ndmf-lower-range.md`.

The Unity projects were made with `Tools/create-liltoon-version-projects.py`.
The package versions come from the public VPM listings, and each downloaded zip
was checked against the listed SHA-256 (`packageSha256` in `results.json`).
Each project uses Unity 2022.3.22f1, Built-in RP, Windows, in batch mode with
graphics, on one Windows PC (PC 1). These are Unity Editor results, not device
results. `results.json` has every run with its exporter commit.

## Sets

Every set uses VRChat SDK 3.10.5, lilToon 2.3.4 and UniVRM 0.131.2 in the
project, without AAO. 53f9773 is the branch after merging develop, which
exports through the bundled UniVRM 0.131.4; 9d294c3 is the same NDMF change
before that merge. MA 1.11.2 is the newest MA that accepts NDMF 1.6.0, and
1.12.2 is the lowest MA of the appearance freeze range; it needs NDMF 1.7.2.
The focused filter is the 11 fixtures in `filterFocused` (342 cases). Cells are
passed/failed/skipped. `NdmfBlinkPreparationTests` (2 cases) skips in every set
because AAO is not installed, and is left out of the table.

| Set | NDMF | Modular Avatar | Commit | NdmfPreparation | ExportOptimizationBindings | NdmfLostMorphFollow | InstalledNdmfNeutralExport | MaSceneReferencePreparation | AppearancePreparation | MaReactiveExpressionExport | MaDeletionExport | MaPermanentAppearanceExport | ExperimentalExpressionCapture |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| P4 | 1.6.0 | - | 53f9773 | 38/0/17 | 28/0/15 | 0/0/10 | 0/0/4 | 0/0/29 | 2/0/10 | 0/0/15 | 0/0/6 | 0/0/4 | 142/20/0 |
| P1 | 1.7.2 | 1.12.2 | 53f9773 | 50/0/5 | 28/0/15 | 10/0/0 | 1/3/0 | 0/0/29 | 9/0/3 | 15/0/0 | 3/0/3 | 4/0/0 | 145/8/9 |
| P2 | 1.8.3 | 1.12.2 | 53f9773 | 54/1/0 | 29/0/14 | 10/0/0 | 1/3/0 | 18/11/0 | 10/0/2 | 15/0/0 | 3/0/3 | 4/0/0 | 154/8/0 |
| F4 | 1.6.0 | - | 9d294c3 | 38/0/17 | 28/0/15 | 0/0/10 | 0/0/4 | 0/0/29 | 2/0/10 | 0/0/15 | 0/0/6 | 0/0/4 | 141/21/0 |
| F4b | 1.6.0 | - | 9d294c3 | 38/0/17 | 28/0/15 | 0/0/10 | 0/0/4 | 0/0/29 | 2/0/10 | 0/0/15 | 0/0/6 | 0/0/4 | 142/20/0 |
| G4 | 1.8.3 | - | 9d294c3 | 39/0/16 | 29/0/14 | 0/0/10 | 0/0/4 | 0/0/29 | 2/0/10 | 0/0/15 | 0/0/6 | 0/0/4 | 142/20/0 |
| F1 | 1.6.0 | 1.11.2 | 9d294c3 | 50/0/5 | 28/0/15 | 10/0/0 | 1/3/0 | 0/0/29 | 2/7/3 | 0/15/0 | 0/3/3 | 0/4/0 | 143/10/9 |
| D1 | 1.7.2 | 1.12.2 | 9d294c3 | 50/0/5 | 28/0/15 | 10/0/0 | 1/3/0 | 0/0/29 | 9/0/3 | 15/0/0 | 3/0/3 | 4/0/0 | 144/9/9 |
| D2 | 1.8.3 | 1.12.2 | 9d294c3 | 54/1/0 | 29/0/14 | 10/0/0 | 1/3/0 | 18/11/0 | 10/0/2 | 15/0/0 | 3/0/3 | 4/0/0 | 154/8/0 |

P4, P1 and P2 used the projects of F4, D1 and D2 with the exporter moved to
53f9773. F4b is a second run of the F4 project.

The pairs that keep MA and change only NDMF are P4 and G4 (no MA, 1.6.0 and
1.8.3) and P1 and P2 (MA 1.12.2, 1.7.2 and 1.8.3). No case fails only before
1.8.0. Every failure in these sets is in the table below; none of them depends
on NDMF being older than 1.8.0.

## Skips before 1.8.0

NDMF adds `NDMFAvatarRoot`, the avatar root marker for non-VRChat avatars, in
1.8.0. Cases that build their avatar on it skip before 1.8.0 instead of
failing:

- `MaSceneReferencePreparationTests` (29 cases), 4 cases of
  `NdmfPreparationTests` (the root marker, MA's authored references) and 1 of
  `AppearancePreparationTests`.
- 9 cases of `ExperimentalExpressionCaptureTests` whose fixture has neither the
  marker nor a VRChat Avatar Descriptor, so NDMF before 1.8.0 cannot identify
  the avatar.
- `InstalledNdmfRootUsesItsPublicEditorOnlyInterface` in
  `ExportOptimizationBindingsTests`, which checks `INDMFEditorOnly` through
  `NDMFAvatarRoot`. `INDMFEditorOnly` exists from 1.7.0; on 1.6.x MA and AAO
  components are found through their base class instead.
- `InstalledModularAvatarMergesSleeveRigAndPreservesSourceAndExpression` in
  `NdmfPreparationTests`, which fails on 1.8.3 with MA 1.12.2 (below).

These cases are therefore not covered on NDMF 1.6.0 to 1.7.x. The cases with a
VRChat Avatar Descriptor (`MaReactiveExpressionExportTests`,
`MaDeletionExportTests`, `MaPermanentAppearanceExportTests`,
`NdmfLostMorphFollowTests`, `InstalledNdmfNeutralExportTests` and most of
`AppearancePreparationTests`) run and give the same result on 1.7.2 and 1.8.3.

The other skips need a package that the set does not have: MA (P4, F4, F4b,
G4), AAO, or MA 1.14.0 for Mesh Cutter (3 cases of `MaDeletionExportTests`).

## Avatars without a VRChat Avatar Descriptor on NDMF before 1.8.0

The first runs on NDMF 1.7.x (C2 below) failed 2 cases whose fixture had MA
components on an avatar with neither a VRChat Avatar Descriptor nor
`NDMFAvatarRoot`:

- `UnresolvedFollowingTargetCanReachCanonicalResolutionOnTheOwnedCopy`: MA
  could not resolve the reference on the copy, and the export stopped with
  "Modular Avatar の追従先を取得できません".
- `RecommendationsFollowActualMergeAnimatorReferencesInsteadOfScanningByName`:
  NDMF 1.7's animator services build the FX that Merge Animator merges into
  from the VRChat Avatar Descriptor, so MA 1.12.2 threw `KeyNotFoundException`
  inside its Merge Animator pass.

NDMF before 1.8.0 and MA before 1.13.0 find the avatar by its VRChat Avatar
Descriptor. The exporter puts `NDMFAvatarRoot` on the copy only when NDMF has
it (1.8.0 and later). From 9d294c3 these 2 cases add a VRChat Avatar Descriptor
on NDMF before 1.8.0 and pass (D1, P1).

The same happens to a user who exports a non-VRChat avatar (no VRChat Avatar
Descriptor) with MA 1.11.x or 1.12.x on NDMF 1.6.0 to 1.7.x: the export stops
with an error and the source avatar is not changed. Those MA versions do not
support non-VRChat avatars (MA supports them from 1.13.0); on NDMF 1.8.0 and
later they skip such an avatar instead. Avatars with a VRChat Avatar Descriptor
are not affected.

## Failures

| Cases | Sets | Cause |
|---|---|---|
| `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` ×4 | every set | The fixture needs an optional pose library package that is not installed. |
| `OneExplicitClipCanBeDroppedAndPreviewed`, `FourExplicitClipsCanBeDroppedAndRecorded` | every set | The simulated IMGUI drag does not reach the window in batch mode on PC 1. |
| `InstalledNdmfReportRemainsSafeAfterItsOwnedCopyIsDestroyed` ×2 | every set | NDMF before 1.14.8 resolves the report to the source object with the same name instead of the export copy (see `ModularAvatarRange`). It fails on 1.8.3 as well. |
| `InstalledNdmfNeutralExportTests` ×3 | every set with MA | Fails the same way on MA 1.11.2, 1.12.2, 1.12.5 and 1.13.0 with NDMF 1.6.0 to 1.8.3. The result does not change with the NDMF version. Fixed on 2026-10-23: the tests expected a BlendShape that the export now drops because no expression uses it. |
| `MaSceneReferencePreparationTests` ×11, `InstalledModularAvatarMergesSleeveRigAndPreservesSourceAndExpression` ×1 | P2, D2 (MA 1.12.2, NDMF 1.8.3) | The fixtures use `NDMFAvatarRoot` without a VRChat Avatar Descriptor, and MA before 1.13.0 skips non-VRChat avatars (see `ModularAvatarRange`). They pass with MA 1.13.0 (N3). On NDMF before 1.8.0 they skip. |
| 10 cases of `ExperimentalExpressionCaptureTests` (`AutomaticAuxiliaryExclusion…` ×2, `ExplicitBlinkMetadata…` ×4, `ManualBlinkConfiguration…` ×2, `RecordedFacesRetain…` ×1, `RecommendationsFollowActualMergeAnimatorReferences…` ×1) and `PreparingAndExportingDoNotInvokeAvatarEditorCallbacks` ×2 | sets without MA (P4, F4, F4b, G4) | These cases use MA components and fail when MA is not installed, on 1.6.0 and 1.8.3 alike. |
| MA range message (`AppearancePreparationTests` ×7, `MaReactiveExpressionExportTests` ×15, `MaDeletionExportTests` ×3, `PreparingAndExportingDoNotInvokeAvatarEditorCallbacks` ×2) and `MaPermanentAppearanceExportTests` ×4 | F1 (MA 1.11.2) | MA 1.11.2 is below the appearance freeze range (1.12.2), so the freeze stops with the range message; 1.11.2 also has no permanent deletion. Avatars without those MA components export. The NDMF cases of F1 pass. |
| `ExcludedObjectsBrokenByAnAvatarEditWaitInTheList` ×1 | F4 | Failed once (the list was empty); it passed in F4b with the same project and commit, and in every other set. |
| `SavedExcludedObjectsThatLeaveNoPreviewWaitInTheList` ×1 | D1 | Failed once (the list was empty); it passed in P1 with the same project, and in every other set. The same case failed once in `ModularAvatarRange` too. |

## Earlier runs

These ran before the fixture changes in 9d294c3, with the same filter. At
38d29c1 and 62d19f0 the appearance freeze range still started at MA 1.18.0, so
every MA set stopped the freeze with the range message.

| Set | NDMF | Modular Avatar | Commit | Total | Passed | Failed | Skipped |
|---|---|---|---|---|---|---|---|
| N1 | 1.6.0 | 1.11.2 | 38d29c1 | 341 | 226 | 34 | 81 |
| N2 | 1.7.10 | 1.12.5 | 38d29c1 | 341 | 229 | 31 | 81 |
| N3 | 1.8.0 | 1.13.0 | 38d29c1 | 341 | 281 | 36 | 24 |
| N4 | 1.6.0 | - | 38d29c1 | 341 | 209 | 20 | 112 |
| B1 | 1.8.3 | 1.11.2 | 62d19f0 | 342 | 266 | 52 | 24 |
| B2 | 1.7.10 | 1.11.2 | 62d19f0 | 342 | 233 | 19 | 90 |
| C2 | 1.7.2 | 1.12.2 | d6f720f | 342 | 239 | 13 | 90 |

- N1 and N2 failed 7 cases of `NdmfPreparationTests` whose fixtures were not
  ready for NDMF before 1.8.0 (MA inactive-authoring fixtures without a VRChat
  Avatar Descriptor, and the copy-resolution case above). 62d19f0 and 9d294c3
  changed those fixtures; F1 passes them.
- N3 (MA 1.13.0 on NDMF 1.8.0) passed all 29 cases of
  `MaSceneReferencePreparationTests` and all of `NdmfPreparationTests`.
- C2 is the run that found the 2 failures described above.
- 2 more sets (MA 1.11.2 with NDMF 1.7.2 and 1.8.0) were planned and dropped;
  B1, B2 and the 9d294c3 sets cover the same versions.
