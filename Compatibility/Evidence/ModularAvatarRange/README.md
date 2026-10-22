# Modular Avatar 1.10.4 to 1.18.7 with the appearance freeze

The exporter freezes the current appearance of Modular Avatar Shape Changer,
Object Toggle, Material Setter, Material Swap and Mesh Cutter on the export copy.
The accepted range is `modularAvatarMinimum` (1.12.2) to below
`modularAvatarBelow` (1.19.0), stable releases only. Avatars without these
components are exported with any Modular Avatar version.

The Unity projects were made with `Tools/create-liltoon-version-projects.py`.
The package versions come from the public VPM listings, and each downloaded zip
was checked against the listed SHA-256. Each project uses Unity 2022.3.22f1,
Built-in RP, Windows, in batch mode with graphics, on two Windows PCs (PC 1 and
PC 2). These are Unity Editor results, not device results. `results.json` has
every run with its exporter commit.

## Sets

Every set uses VRChat SDK 3.10.5 and lilToon 2.3.4, without AAO. The focused
filter is `AppearancePreparationTests`, `MaReactiveExpressionExportTests`,
`MaDeletionExportTests`, `NdmfPreparationTests` and
`ExperimentalExpressionCaptureTests` (244 cases). Cells are
passed/failed/skipped.

| Set | Modular Avatar | NDMF | UniGLTF/VRM | PC | Commit | AppearancePreparation | MaReactiveExpressionExport | MaDeletionExport | NdmfPreparation | ExperimentalExpressionCapture | Verdict |
|---|---|---|---|---|---|---|---|---|---|---|---|
| L1 | 1.10.4 | 1.8.3 | 0.131.3 | 1 | a0c37f9 | 10/0/2 | 6/6/3 | 3/0/3 | 48/1/0 | 156/6/0 | Outside: menu expressions do not import |
| L9 | 1.11.2 | 1.8.3 | 0.131.3 | 1 | ddbee33 | 10/0/2 | 6/6/3 | 3/0/3 | 48/1/0 | 156/6/0 | Outside: menu expressions do not import |
| M0 | 1.11.7 | 1.8.3 | 0.131.3 | 1 | ddbee33 | 10/0/2 | 6/6/3 | 3/0/3 | 48/1/0 | 156/6/0 | Outside: menu expressions do not import |
| M1 | 1.12.0 | 1.8.3 | 0.131.3 | 1 | ddbee33 | - | - | - | - | - | Outside: MA does not compile |
| M4 | 1.12.1 | 1.8.3 | 0.131.3 | 1 | ddbee33 | - | - | - | - | - | Outside: MA does not compile |
| M5 | 1.12.2 | 1.8.3 | 0.131.2 | 2 | ddbee33 | 10/0/2 | 15/0/0 | 3/0/3 | 48/1/0 | 154/8/0 | In range (lowest) |
| M2 | 1.12.3 | 1.8.3 | 0.131.2 | 2 | ddbee33 | 10/0/2 | 15/0/0 | 3/0/3 | 48/1/0 | 154/8/0 | In range |
| M3 | 1.12.4 | 1.8.3 | 0.131.2 | 2 | ddbee33 | 10/0/2 | 15/0/0 | 3/0/3 | 48/1/0 | 154/8/0 | In range |
| L2 | 1.12.5 | 1.8.3 | 0.131.3 | 1 | ddbee33 | 10/0/2 | 15/0/0 | 3/0/3 | 48/1/0 | 156/6/0 | In range |
| L3 | 1.13.0 | 1.8.3 | 0.131.3 | 1 | ddbee33 | 10/0/2 | 15/0/0 | 3/0/3 | 49/0/0 | 156/6/0 | In range |
| L4 | 1.14.0 | 1.9.0 | 0.131.2 | 2 | ddbee33 | 12/0/0 | 11/4/0 | 6/0/0 | 49/0/0 | 154/8/0 | In range; MA logs its own menu type error |
| L8 | 1.14.2 | 1.9.1 | 0.131.3 | 1 | ddbee33 | 12/0/0 | 15/0/0 | 6/0/0 | 49/0/0 | 156/6/0 | In range |
| L5 | 1.17.1 | 1.11.0 | 0.131.2 | 2 | ddbee33 | 12/0/0 | 15/0/0 | 6/0/0 | 49/0/0 | 153/9/0 | In range |
| L6 | 1.18.0 | 1.11.0 | 0.131.2 | 2 | ddbee33 | 12/0/0 | 15/0/0 | 6/0/0 | 49/0/0 | 153/9/0 | In range |
| L7 | 1.18.7 | 1.14.8 | 0.131.2 | 2 | ddbee33 | 12/0/0 | 15/0/0 | 6/0/0 | 49/0/0 | 156/6/0 | In range |

The sets below 1.12.2 ran on commits whose range still included them
(a0c37f9 started at 1.10.4, ddbee33 at 1.11.2), so the freeze and the export
ran instead of stopping at the range check. ddbee33 and the range commit
97d1ae7 differ only in the lower bound, its tests and documents, and test code
for MA before 1.12.

L3, L8, M5 and L7 were run again on 97d1ae7 with the same projects. Every
count matched the ddbee33 run of the same set:

| Set | Modular Avatar | PC | Commit | AppearancePreparation | MaReactiveExpressionExport | MaDeletionExport | NdmfPreparation | ExperimentalExpressionCapture |
|---|---|---|---|---|---|---|---|---|
| M5 | 1.12.2 | 2 | 97d1ae7 | 10/0/2 | 15/0/0 | 3/0/3 | 48/1/0 | 154/8/0 |
| L3 | 1.13.0 | 1 | 97d1ae7 | 10/0/2 | 15/0/0 | 3/0/3 | 49/0/0 | 156/6/0 |
| L8 | 1.14.2 | 1 | 97d1ae7 | 12/0/0 | 15/0/0 | 6/0/0 | 49/0/0 | 156/6/0 |
| L7 | 1.18.7 | 2 | 97d1ae7 | 12/0/0 | 15/0/0 | 6/0/0 | 49/0/0 | 156/6/0 |

Skips: before 1.14.0 there is no Mesh Cutter, so 2 cases of
`AppearancePreparationTests` and 3 of `MaDeletionExportTests` skip; the Shape
Changer Delete cases run. Before 1.12.0, 3 cases of
`MergeMotionActivationKeepsNonMenuReactionThroughCanonicalBuild` also skip,
because Merge Blend Tree only takes a BlendTree there.

## Below 1.12.2

On 1.10.4, 1.11.2 and 1.11.7 the freeze, the deletion cases and the other
reactive cases pass, but the 6 cases of
`MenuReactiveShapeSurvivesCanonicalBuildAndVrmReload` fail. The case switches a
Shape Changer from a menu item and imports the expression from the FX that MA's
own build makes.

- Bool menu: Unity rejects the merged FX:
  `Controller 'FX (merged)': Transition '' in state '<default>' uses parameter 'Menu' which is not compatible with condition type.`
- Int and Float menus: the import reports that the transition to the
  expression changes something other than BlendShapes, so 0 expressions are
  imported instead of 1. On 1.11.2 and 1.11.7 that is `m_IsActive` of
  the menu item's GameObject. On 1.10.4 it is a `deletedShape.Smile` curve on
  MA's `Reactive Component Defaults`.

1.12.0 and 1.12.1 with NDMF 1.8.3, the exporter's NDMF floor, do not compile.
Unity reports compile errors in MA's own
`Runtime/ModularAvatarMergeBlendTree.cs`: the namespace `API` and the type
`IVirtualizeMotion` are not found. No test ran.

## 1.14.0

On L4, 4 cases of `MenuReactiveShapeSurvivesCanonicalBuildAndVrmReload` (Int
and Bool, each with and without permanent deletion) fail on an error that NDMF
logs during MA's build, which the test runner counts as a failure:
`Parameter Menu changed type from Int to Float in-place. This is not supported and will become an error in future versions. …`
(`from Bool to Float` for Bool). The Float cases and the rest of the fixture
pass. 1.14.2 (L8) passes all 15; its changelog lists the fix for adjusting
parameter types to Float (#1765). 1.14.1 was not run.

## Failures outside the range

| Cases | Sets | Cause |
|---|---|---|
| `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` ×4 | every set | The fixture needs an optional pose library package that is not installed. |
| `InstalledNdmfReportRemainsSafeAfterItsOwnedCopyIsDestroyed` ×2 | every set with NDMF before 1.14.8 | NDMF 1.8.3 to 1.11.0 resolve the report to the source object with the same name instead of the export copy, so selecting an NDMF error shown during export selects the source. It passes on NDMF 1.14.8 (L7). The exported file is the same. |
| `InstalledModularAvatarMergesSleeveRigAndPreservesSourceAndExpression` ×1 | MA before 1.13.0 | The fixture uses NDMF's `NDMFAvatarRoot`, a non-VRChat avatar root used only by the test. MA runs on non-VRChat roots from 1.13.0 ("Enabled support for non-VRC platforms" in its changelog, `[RunsOnAllPlatforms]` on its plugin), so older MA does not merge the sleeve and it does not move. Avatars with a VRChat avatar descriptor are not affected. |
| `OneExplicitClipCanBeDroppedAndPreviewed`, `FourExplicitClipsCanBeDroppedAndRecorded` | every set on PC 2 | The simulated IMGUI drag does not reach the window in batch mode on PC 2. Both pass on PC 1. |
| `LeavingTheObjectsPageAppliesItsEditsFirst` ×1 | L5 | Failed once; passes on every other run in the tables above, including the other sets on PC 2. |
| `SavedExcludedObjectsThatLeaveNoPreviewWaitInTheList` ×1 | L6 | Failed once (the list was empty); passes on every other run in the tables above. |

## Earlier runs

When the range started at 1.18.0 (commit a37c0d2), these sets ran
`AppearancePreparationTests`, `MaReactiveExpressionExportTests`,
`MaDeletionExportTests` and `ExperimentalExpressionCaptureTests` (153 cases):

| Set | Modular Avatar | NDMF | UniGLTF/VRM | AAO | AppearancePreparation | MaReactiveExpressionExport | MaDeletionExport |
|---|---|---|---|---|---|---|---|
| E2 | 1.13.4 | 1.8.3 | 0.131.3 | 1.8.15 | 2 passed, 8 stopped, 2 skipped | 15 stopped | 6 skipped |
| E4 | 1.18.0 | 1.11.0 | 0.131.3 | not installed | 12 passed | 15 passed | 6 passed |
| U2 | 1.18.7 | 1.14.8 | 0.131.2 | 1.9.20 | 12 passed | 15 passed | 6 passed |

"Stopped" means the case failed with the range message before the freeze
touched the copy, because 1.13.4 was outside the range at that commit. 1.13.x is
now inside the range; L3 covers it. The `ExperimentalExpressionCaptureTests`
failures on E2, E4 and U2 are the pose library and NDMF report cases above, and
on E2 also 2 cases stopped by the range message.

E4 also ran `Tools/run-unity-compatibility.py --profile exporter-integration`
on d02a6e8, before the range gate: 2131 cases, 2122 passed, 0 failed, 9
skipped. The skips are 7 cases that need AAO (not installed in E4) and 2 cases
that need an optional mesh deletion package (not installed). The runner exits
non-zero only because some required AAO cases are skipped.
