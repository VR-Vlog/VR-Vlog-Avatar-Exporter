# VRChat SDK 3.5.0 to below 3.11.0 with VRC Animator Play Audio

The exporter checks the VRChat SDK version in one place only: when an expression
file's Animator uses VRC Animator Play Audio, the expression capture copy omits
that behaviour. The accepted range is `vrchatSdkMinimum` (3.5.0) to below
`vrchatSdkBelow` (3.11.0). Base and Avatars must be the same version. A
pre-release counts as its X.Y.Z, so 3.10.6-beta.2 is accepted and 3.11.0-beta.1
is not.

- 3.5.0 is the first VRChat SDK whose `package.json` declares `"unity": "2022.3"`;
  3.4.x declares 2019.4.
- 3.10.5 is the latest release, and 3.10.6-beta.2 the latest pre-release, on
  the official listing as of 2026-10-22.

## Types and members

[members.md](members.md) compares every VRChat SDK type and member the exporter
reads by name (287 checks) in 62 versions, 3.4.2 to 3.10.6-beta.2: 23 releases
and 39 pre-releases from the official VPM listing. Where a type or member
exists, it matches 3.10.5, with these exceptions:

- `VRCAnimatorPlayAudio` → `VRC_AnimatorPlayAudio` → `StateMachineBehaviour`
  first appears in 3.5.2-beta.1. Its fields and methods are the same in every
  version that has it.
- `VRCPhysBone.ignoreOtherPhysBones` first appears in 3.7.7-beta.1 (release
  3.8.0). The exporter treats its absence as `true`.
- VRC Constraints first appear in the preview builds 3.6.2-constraints.1 to .4
  (release 3.7.0). In 3.6.2-constraints.1 and .2, `Sources` is an array, and the
  exporter leaves those constraints out because it finds no sources.

## Unity runs

Four empty Unity projects were made with `Tools/create-liltoon-version-projects.py`.
The VRChat SDK zips come from the official package releases (SHA-256 checked),
UniVRM from its official releases. Each project uses Unity 2022.3.22f1, Built-in
RP, Windows, in batch mode with graphics. These are Unity Editor results, not
device results. `results.json` has every run with its exporter commit.

| Set | VRChat SDK | UniGLTF/VRM | lilToon | NDMF | Modular Avatar | AAO |
|---|---|---|---|---|---|---|
| V1 | 3.5.0 | 0.131.2 | 2.3.4 | not installed | not installed | not installed |
| V2 | 3.5.2 | 0.131.2 | 2.3.4 | not installed | not installed | not installed |
| V3 | 3.10.5 | 0.131.2 | 2.3.4 | not installed | not installed | not installed |
| V4 | 3.10.6-beta.2 | 0.131.2 | 2.3.4 | not installed | not installed | not installed |

The focused run is `ExperimentalExpressionCaptureTests` and
`PhysBoneSpringExportTests` (205 cases).

| Set | VRChat SDK | Passed | Failed | Skipped | audio | audio-reactive |
|---|---|---|---|---|---|---|
| V1 | 3.5.0 | 172 | 19 | 14 | Skipped | Skipped |
| V2 | 3.5.2 | 175 | 18 | 12 | Passed | Passed |
| V3 | 3.10.5 | 181 | 18 | 6 | Passed | Passed |
| V4 | 3.10.6-beta.2 | 181 | 18 | 6 | Passed | Passed |

`audio` and `audio-reactive` are
`ExistingMenuCandidateCanBeDiscoveredPreviewedAndCapturedWithoutChangingItsController`
with an existing menu whose expression state has VRC Animator Play Audio: the
expression is discovered, previewed and captured, and the source controller is
left unchanged. On 3.5.0 they are skipped with the reason "VRCAnimatorPlayAudio
is in VRChat SDK 3.5.2 and later."

The same 18 cases fail in all four sets, and none of them depends on the SDK:

| Cases | Cause |
|---|---|
| 10 blink, auxiliary-exclusion, mouth/gaze and merge-reference cases | The fixtures need the installed Modular Avatar. They passed on E2 below. |
| `PreparingAndExportingDoNotInvokeAvatarEditorCallbacks` ×2 | Modular Avatar type not installed. |
| `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` ×4 | The fixture needs an optional pose library package that is not installed. |
| `OneExplicitClipCanBeDroppedAndPreviewed`, `FourExplicitClipsCanBeDroppedAndRecorded` | No IMGUI drag event reached the window in batch mode. |

V1 also failed `LeavingTheObjectsPageAppliesItsEditsFirst` once ("Expected
<Back> but was empty"). The case does not touch the VRChat SDK; rerun alone on
the same project, it passed (1 case, 1 passed).

The skips other than the audio cases are the NDMF, Modular Avatar and AAO cases
(not installed), and on 3.5.0 and 3.5.2 six `PhysBoneSpringExportTests` cases
that set `ignoreOtherPhysBones`, which needs 3.8.0 or later.

## Earlier runs (3.5.2 floor, 2026-10-18)

These runs were made when the range was 3.5.2 to below 3.11.0 without
pre-releases.

| Set | VRChat SDK | UniGLTF/VRM | lilToon | NDMF | Modular Avatar | AAO |
|---|---|---|---|---|---|---|
| E3 | 3.5.2 | 0.131.2 | 2.3.4 | not installed | not installed | not installed |
| E1 | 3.7.0 | 0.131.0 | 2.0.0 | 1.8.3 | not installed | 1.7.13 |
| E2 | 3.10.5 | 0.131.3 | 2.3.4 | 1.8.3 | 1.13.4 | 1.8.15 |

The `VRC_AnimatorPlayAudio` IL of 3.5.2, 3.6.0 and 3.7.0 matched 3.10.5 once
metadata tokens were masked.

| Set | VRChat SDK | audio | audio-reactive |
|---|---|---|---|
| E3 | 3.5.2 | Passed | Passed |
| E1 | 3.7.0 | Passed | Passed |
| E2 | 3.10.5 | Passed | Passed |

E1 and E2 ran on the commit with the earlier 3.7.0 floor. The only change after
it is the floor constant, so the check that ran is the same. All runs were made
before this branch was rebased onto the UniVRM 0.131 review fixes, which change
only the UniVRM version defines, tooling and documents; the `commit` values in
`results.json` name the rebased commits with the same SDK changes.

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
