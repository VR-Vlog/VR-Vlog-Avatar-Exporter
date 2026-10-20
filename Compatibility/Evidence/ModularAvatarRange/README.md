# Modular Avatar 1.13.4, 1.18.0 and 1.18.7 with the appearance freeze

The exporter freezes the current appearance of Modular Avatar Shape Changer,
Object Toggle, Material Setter, Material Swap and Mesh Cutter on the export copy.
The accepted range is `modularAvatarMinimum` (1.18.0) to below
`modularAvatarBelow` (1.19.0), stable releases only. Avatars without these
components are exported with any Modular Avatar version.

The Unity projects were made with `Tools/create-liltoon-version-projects.py`.
The package versions come from the public VPM listings, and each downloaded zip
was checked against the listed SHA-256. Each project uses Unity 2022.3.22f1,
Built-in RP, Windows, in batch mode with graphics. These are Unity Editor
results, not device results. `results.json` has every run with its exporter
commit.

| Set | Modular Avatar | NDMF | VRChat SDK | UniGLTF/VRM | lilToon | AAO |
|---|---|---|---|---|---|---|
| E2 | 1.13.4 | 1.8.3 | 3.10.5 | 0.131.3 | 2.3.4 | 1.8.15 |
| E4 | 1.18.0 | 1.11.0 | 3.10.5 | 0.131.3 | 2.3.4 | not installed |
| U2 | 1.18.7 | 1.14.8 | 3.10.5 | 0.131.2 | 2.3.4 | 1.9.20 |

1.18.0 is the lowest release with the preview API that the freeze reads, and
1.11.0 is the lowest NDMF that Modular Avatar 1.18.0 accepts. 1.18.7 is the
newest stable release.

## Appearance cases

`AppearancePreparationTests`, `MaReactiveExpressionExportTests` and
`MaDeletionExportTests` on the branch commit:

| Set | Modular Avatar | AppearancePreparation | MaReactiveExpressionExport | MaDeletionExport |
|---|---|---|---|---|
| E2 | 1.13.4 | 2 passed, 8 stopped, 2 skipped | 15 stopped | 6 skipped |
| E4 | 1.18.0 | 12 passed | 15 passed | 6 passed |
| U2 | 1.18.7 | 12 passed | 15 passed | 6 passed |

"Stopped" means the case failed with the new message before the freeze touched
the copy: `Modular Avatar 1.13.4では、…対応範囲は1.18.0以上1.19.0未満です。…`.
Before this change, 1.13.4 also stopped, but with a message that did not name
a supported version. On E2 the deletion cases skip because they need Mesh Cutter,
which Modular Avatar 1.13.4 does not have.

## Other results

E4 also ran `Tools/run-unity-compatibility.py --profile exporter-integration`
on the commit before this change: 2131 cases, 2122 passed, 0 failed, 9 skipped.
The skips are 7 cases that need AAO (not installed in E4) and 2 cases that need
an optional mesh deletion package (not installed). The runner exits non-zero
only because some required AAO cases are skipped.

`ExperimentalExpressionCaptureTests` ran in the same focused runs. The failures
outside the freeze were:

| Cases | Sets | Cause |
|---|---|---|
| `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` ×4 | E2, E4, U2 | The fixture needs an optional pose library package that is not installed. |
| `InstalledNdmfReportRemainsSafeAfterItsOwnedCopyIsDestroyed` ×2 | E2, E4 | NDMF 1.8.3 and 1.11.0 resolve the report to the source object with the same name instead of the export copy, so selecting an NDMF error shown during export selects the source. It passes on NDMF 1.14.8 (U2). The exported file is the same. |
| `PreparingAndExportingDoNotInvokeAvatarEditorCallbacks` ×2 | E2 | Stopped by the new message (the fixture has a Shape Changer). |
