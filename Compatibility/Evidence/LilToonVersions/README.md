# lilToon 2.0.0-2.3.4 focused tests and export parity

One empty Unity project per lilToon release, made with
`Tools/create-liltoon-version-projects.py`. Every project resolved Unity
2022.3.22f1 (887be4894c44), Built-in RP, UniGLTF/VRM 0.131.2, VRChat SDK
3.10.4, NDMF 1.14.8, Modular Avatar 1.18.7, Test Framework 1.4.6 and the
official lilToon VPM ZIP of that release, embedded. The runner's
`--expect-liltoon` confirmed the installed lilToon before and after each run.
These are Unity Editor results on Windows (batch mode), not device results.
`results.json` has every attempt, its exporter commit and the lilToon ZIP
SHA-256.

## Focused runner profiles

Final attempts, all at exporter `eb85f007a94e2a9bbafbed35b8f024ec090884d7`.
`exporter-behavior` for 2.0.0 and 2.1.0 also passed 2085/2085 earlier at
`237f4c61701d3991cd1f4e06763ad67de09c2591`.

| lilToon | compatibility | exporter-behavior | Unity | UniVRM |
|---|---|---|---|---|
| 2.0.0 | 31/31 Passed | 2085/2085 Passed | 2022.3.22f1 | 0.131.2 |
| 2.1.0 | 31/31 Passed | 2085/2085 Passed | 2022.3.22f1 | 0.131.2 |
| 2.1.10 | 31/31 Passed | 2085/2085 Passed | 2022.3.22f1 | 0.131.2 |
| 2.2.1 | 31/31 Passed | 2085/2085 Passed | 2022.3.22f1 | 0.131.2 |
| 2.3.0 | 31/31 Passed | 2085/2085 Passed | 2022.3.22f1 | 0.131.2 |
| 2.3.4 | 31/31 Passed | 2085/2085 Passed | 2022.3.22f1 | 0.131.2 |

No case was skipped. Earlier attempts that failed:

- main `598783d`, the first run after switching each project to that exporter:
  2.0.0, 2.1.0 and 2.1.10 each failed only
  `DependencyStartupTests.MenuResolvesBackendWithoutInitializationRegistration`
  (30/31). Unity was still importing when the test ran, so the menu correctly
  opened the diagnostics window instead of the exporter.
- `237f4c6` (the test waits while Unity is busy): 2.0.0, 2.1.0, 2.1.10, 2.2.1
  and 2.3.4 passed 31/31; 2.3.0 failed the same test with the 180-second test
  timeout. A "Define symbols changed" recompile was requested as the run started,
  and the test runner keeps assembly reloads locked, so compilation never
  finished while the test waited.
- `eb85f00` yields `WaitForDomainReload` while Unity is compiling: all six passed
  31/31 on their first run after switching to that commit.

The failures came from the test's assumption that the editor is idle, not from
lilToon: the same versions passed once the test waited correctly, and the
failing test does not touch lilToon.

## Export parity after upgrading lilToon

`Tools/run-liltoon-version-parity.py` / `Tests/Editor/LilToonVersionParityHarness.cs`
(harness `90b5d5d`): 63 synthetic materials saved with lilToon X (11 cases for
backlight colour and texture alpha below 1, fur, Outline Lit, MatCap UV1, shadow
colours, rim, rim shade, transparent and cutout, plus the default material of all
52 catalogue shaders), each exported on the test Humanoid. The project copy was
then switched to lilToon 2.3.4, reopened and exported again.

| lilToon X | exports | A vs A-again | A vs B (after 2.3.4) | `VRVLOG_lilToonInstalledVersion` A -> B |
|---|---|---|---|---|
| 2.0.0 | 63 | identical | identical except the version key | 2.0.0 -> 2.3.4 |
| 2.1.0 | 63 | identical | identical except the version key | 2.1.0 -> 2.3.4 |
| 2.1.10 | 63 | identical | identical except the version key | 2.1.10 -> 2.3.4 |
| 2.2.1 | 63 | identical | identical except the version key | 2.2.1 -> 2.3.4 |
| 2.3.0 | 63 | identical | identical except the version key | 2.3.0 -> 2.3.4 |
| 2.3.4 | 63 | identical | identical except the version key | 2.3.4 -> 2.3.4 |

"Identical" means every glTF JSON value is equal (floats compared exactly) and
every bufferView, including the DEFLATE chunks of `VRVLOG_materials_liltoon`
and the images, has the same SHA-256. The one per-export placeholder morph target
name `__VRVlog_BlinkNone_<GUID>` was renumbered in each file before comparing;
it differs between two exports in the same session too. The orchestration
script itself was also run end to end for 2.1.0 (`b9a857c`) and passed.

Observed while checking:

- Every release from 2.0.0 to 2.3.4 has `lilConstants.currentVersionValue` 45,
  and every saved material had `_lilToonVersion` 45 before and after the upgrade.
  lilToon 2.3.4's startup migration ran but had nothing to rewrite.
- `_EnvRimBorder` (default 0.85 in 2.0.0/2.1.0, 3.0 from 2.1.10) is classified
  `float:external` in the catalogue and is not written to the file, so the 0.85
  of old materials does not reach the app.

Not covered: lilToon Shader Setting changes, encrypted materials, custom shaders
and real avatars (compared visually in the acceptance check).
