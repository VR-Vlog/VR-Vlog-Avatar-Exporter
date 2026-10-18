# VRChat SDK 3.5.2, 3.7.0 and 3.10.5 with VRC Animator Play Audio

The exporter checks the VRChat SDK version in one place only: when an expression
file's Animator uses VRC Animator Play Audio, the expression capture copy omits
that behaviour. The accepted range is `vrchatSdkMinimum` (3.5.2) to below
`vrchatSdkBelow` (3.11.0). Base and Avatars must be the same release.

Three empty Unity projects were made with `Tools/create-liltoon-version-projects.py`.
The package versions come from the public VPM listings: the VRChat SDK zips from
the official package releases, UniVRM from its official releases. Each project
uses Unity 2022.3.22f1, Built-in RP, Windows, in batch mode with graphics. These
are Unity Editor results, not device results. `results.json` has every run with
its exporter commit.

| Set | VRChat SDK | UniGLTF/VRM | lilToon | NDMF | Modular Avatar | AAO |
|---|---|---|---|---|---|---|
| E3 | 3.5.2 | 0.131.2 | 2.3.4 | not installed | not installed | not installed |
| E1 | 3.7.0 | 0.131.0 | 2.0.0 | 1.8.3 | not installed | 1.7.13 |
| E2 | 3.10.5 | 0.131.3 | 2.3.4 | 1.8.3 | 1.13.4 | 1.8.15 |

3.5.2 is the first stable release with `VRCAnimatorPlayAudio`. Its DLLs were
compared with 3.6.0, 3.7.0, 3.8.0, 3.9.0, 3.10.4 and 3.10.5 from the official
package listing:

- The inheritance is the same in every version: `VRCAnimatorPlayAudio` → `VRC_AnimatorPlayAudio` → `StateMachineBehaviour`.
- The fields and methods are the same.
- The `VRC_AnimatorPlayAudio` IL of 3.5.2, 3.6.0 and 3.7.0 matches 3.10.5 once metadata tokens are masked.
- 3.5.1 and earlier do not contain the type.
- 3.5.0, 3.5.2, 3.6.1 and 3.7.0 all declare `"unity": "2022.3"` in `package.json`.

## Play Audio cases

`ExperimentalExpressionCaptureTests.ExistingMenuCandidateCanBeDiscoveredPreviewedAndCapturedWithoutChangingItsController`
with the `audio` and `audio-reactive` fixtures: an existing menu whose expression
state has VRC Animator Play Audio is discovered, previewed and captured, and the
source controller is left unchanged.

| Set | VRChat SDK | audio | audio-reactive |
|---|---|---|---|
| E3 | 3.5.2 | Passed | Passed |
| E1 | 3.7.0 | Passed | Passed |
| E2 | 3.10.5 | Passed | Passed |

E1 and E2 ran on the commit with the earlier 3.7.0 floor. The only change after
it is the floor constant, so the check that ran is the same.

## Other results

E3 also ran `Tools/run-unity-compatibility.py --profile exporter-integration`:
2131 cases, 2002 passed, 0 failed, 129 skipped. The skips are the cases that
need MA, NDMF or AAO (not installed in E3), and 14 cases that need VRChat's
`VRCParentConstraint`. That component was added in 3.7.0, and those cases skip
with their reason. The runner exits non-zero only because one required
blink case needs MA/NDMF.

The focused runs (`ExperimentalExpressionCaptureTests` and
`PhysBoneSpringExportTests`, 152 cases) had failures unrelated to the SDK range:

| Cases | Sets | Cause |
|---|---|---|
| 10 blink, auxiliary-exclusion, mouth/gaze and merge-reference cases | E1, E3 | The fixtures need the installed Modular Avatar. They pass on E2. |
| `PreparingAndExportingDoNotInvokeAvatarEditorCallbacks` ×2 | E1, E2, E3 | E1/E3: Modular Avatar type not installed. E2: the MA 1.13.4 appearance freeze stops with its message (MA evidence). |
| `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` ×4 | E1, E2, E3 | The fixture needs an optional pose library package that is not installed. |
| `InstalledNdmfReportRemainsSafeAfterItsOwnedCopyIsDestroyed` ×2 | E1, E2 | NDMF 1.8.3 notification navigation; skipped on E3 without NDMF. |
| `InstalledAvatarOptimizerPreservesSpringMotionAndCollisionsAfterFullExport` ×2 | E1, E2 | The AAO fixture had no Avatar Descriptor; see the AAO evidence. |
| 4 window interaction cases (`CandidateSelectionBulkAdd…`, `CorrectedRecommendedAsset…`, `OneExplicitClip…`, `FourExplicitClips…`) | E3 | Failed once with no drag or click reaching the window. Rerun of these 4 alone on the same project: 4 passed. |

`PhysBoneSpringExportTests` cases that set `ignoreOtherPhysBones` are ignored on
3.5.2 and 3.7.0 with a reason; the field exists from 3.8.0 and the exporter
treats its absence as `true`.
