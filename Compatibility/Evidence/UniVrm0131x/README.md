# UniVRM 0.131.0, 0.131.2 and 0.131.3 in real projects

Four empty Unity projects made with `Tools/create-liltoon-version-projects.py`,
each with the package versions below resolved from the public VPM listings and
the official UniVRM releases (not bundled with the exporter). Unity 2022.3.22f1,
Built-in RP, Windows, batch mode. These are Unity Editor results, not device
results. `results.json` has every run with its exporter commit.

| Set | UniGLTF/VRM | lilToon | VRChat SDK | NDMF | Modular Avatar | AAO |
|---|---|---|---|---|---|---|
| U2 | 0.131.2 | 2.3.4 | 3.10.5 | 1.14.8 | 1.18.7 | 1.9.20 |
| U3 | 0.131.3 | 2.3.4 | 3.10.5 | 1.14.8 | 1.18.7 | 1.9.20 |
| E1 | 0.131.0 | 2.0.0 | 3.7.0 | 1.8.3 | not installed | 1.7.13 |
| E2 | 0.131.3 | 2.3.4 | 3.10.5 | 1.8.3 | 1.13.4 | 1.8.15 |

U2 and U3 differ only in UniVRM. E1 and E2 put the other dependencies at the
low end of their ranges; their non-UniVRM results are covered by the evidence
of those dependencies.

## exporter-integration

`Tools/run-unity-compatibility.py --profile exporter-integration` (includes the
`compatibility` and `exporter-behavior` cases).

| Set | Exporter | Total | Passed | Failed | Skipped |
|---|---|---|---|---|---|
| U2 | `fe1c336` (before the fix) | 2128 | 2124 | 2 | 2 |
| U3 | `fe1c336` (before the fix) | 2128 | 2118 | 8 | 2 |
| U2 | `5d0f4fb` (fix) | 2131 | 2126 | 3 | 2 |
| U3 | `5d0f4fb` (fix) | 2131 | 2126 | 3 | 2 |
| E1 | `5d0f4fb` + VRChat SDK range change | 2131 | 2022 | 9 | 100 |
| E2 | `5d0f4fb` + VRChat SDK range change | 2131 | 2091 | 27 | 13 |

Failures by cause:

- `MissingMeshAttributesTests.NormalExporterPreservesMissingAttributeMeshesThroughRealVrmRoundTrip`,
  6 cases, U3 before the fix only. UniVRM 0.131.3 changed
  `NativeArrayManager.CreateNativeArray(T[])` to wrap the managed array without
  copying it. The exporter reused one normals array for every blend shape of a
  mesh, so every morph target pointed at the last shape's normals, and targets
  whose normals then looked unchanged lost `NORMAL`. The same input exported
  with 0.131.2 had `NORMAL` on every target; with 0.131.3 the first target had
  none (checked in the GLB). With one array per shape (`5d0f4fb`) these cases
  pass on U3 and on E2 (also 0.131.3).
- `MorphNormalExportTests.EveryMorphOfOneMeshKeepsItsOwnAuthoredNormals`, the
  new regression test, failed at `5d0f4fb` on all four sets because the test
  gave two renderers the same `Mesh`. `441aff8` gives each renderer its own
  mesh. A focused run of `MorphNormalExportTests` at `441aff8` passed 3/3 on
  U2, U3, E1 and E2.
- `PhysBoneSpringExportTests`, 2 cases on every set: the AAO integration
  fixture has no avatar descriptor. Not related to UniVRM (same on 0.131.0,
  0.131.2 and 0.131.3); handled with the AAO range.
- E1 and E2 only: PhysBone `ignoreOtherPhysBones` (VRChat SDK 3.7.0),
  Modular Avatar 1.13.4 appearance freezing, AAO 1.7/1.8 mapping. Handled with
  those dependencies.
- Skipped: `MeshDeleterIntegrationTests` (2) everywhere because that package
  is not installed; E1/E2 skip the cases whose packages are missing or older
  than the fixture needs.

No UniVRM-only failure remains after `5d0f4fb`.

## lilToon export parity, 0.131.2 vs 0.131.3

`LilToonVersionParityHarness.CreateAndExport` on U2 and U3, compared with
`Tools/compare-liltoon-version-exports.py` (63 exports per side, the 11 lilToon
feature cases plus the default material of all 52 catalogue shaders).

| Compared | Exporter | Pairs | JSON differences | Binary differences |
|---|---|---|---|---|
| U2 vs U3 | `fe1c336` | 63 | `asset.generator` only | none |
| U2 vs U3 | `441aff8` | 63 | `asset.generator` only | none |
| U2 `fe1c336` vs U2 `441aff8` | | 63 | none | none |
| U3 `fe1c336` vs U3 `441aff8` | | 63 | none | none |

`asset.generator` is `UniVRM-0.131.2` / `UniVRM-0.131.3`. The parity exports
are identical before and after the fix and between the two UniVRM versions, so
these materials do not exercise several blend shapes with different normals on
one mesh; `MorphNormalExportTests` covers that case.
