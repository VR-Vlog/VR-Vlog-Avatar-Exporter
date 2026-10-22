# Release ZIP startup and export, lilToon 2.0.0-2.3.4

The package ZIP built from main `3f3fa4524be7d1ddc20119fcdd1d4b0364e87387` was
installed as an embedded package, as VCC/ALCOM install it, into one project per
supported lilToon version. In every project the production menu opened, the
diagnostics report reached Ready, and a generated avatar with one lilToon
material was exported with the same internal methods as the export window.

This is an unreleased candidate. Its `package.json` still says 0.11.15, but it
is not the published 0.11.15 ZIP (that ZIP's SHA-256 is `fd3a8610...`). This
departs from the rule in `../../README.md` to assign the next development
version first: the label is fixed in `Tools/validate.py` and changing it is
release preparation. The ZIP was not distributed. Identify it by the commit and
the SHA-256 below, not by the version label.

## Package

- `Tools/build-package.py` at the commit above: `com.vrvlog.liltoon-vrm-exporter-0.11.15.zip`,
  303 files, 8,830,460 bytes, no `Tests` folder.
- SHA-256 `59ec11c2eb0eb5348c303db28a0b4c287352c67bbf6d93de6daa1463787267e1`.
- The relative-link check and the 20 MiB limit of `Tools/build-release-candidate.py`
  were run on this ZIP with the same functions. The complete host-validation
  receipt that `build-release-candidate.py` requires was not produced: it needs
  Linux (`os.O_NOFOLLOW`), and the run was on Windows. The `Validate` workflow
  passed on the same commit.

## Projects

- Unity 2022.3.22f1 (887be4894c44), Built-in RP, created with
  `Tools/create-liltoon-version-projects.py` (official lilToon VPM ZIPs checked
  against the script's SHA-256 list).
- Embedded packages: lilToon (the version below), UniVRM/UniGLTF 0.131.2,
  VRChat SDK Base/Avatars 3.10.4, NDMF 1.14.8, Modular Avatar 1.18.7.
- The script's `file:` reference to an exporter checkout and its `testables`
  entry were removed. The ZIP was extracted to
  `Packages/com.vrvlog.liltoon-vrm-exporter`.
- `Tests/Startup/ExporterStartupProbe.cs` and `ZipProbe.cs.txt` (rename to
  `.cs`) were copied to `Assets/Editor`. One batch-mode run per project, without
  `-nographics` (as the focused runner): `-batchmode -projectPath PROJECT
  -executeMethod E145ZipProbe.Run -logFile LOG`. This differs from the command
  in `../../README.md` (`-quit -executeMethod ExporterStartupProbe.Run`): the
  wrapper runs that probe and then the export in the same run, and ends Unity
  itself with the result as the exit code. The first open of each project
  includes the full import.

`ZipProbe.cs.txt` first runs `ExporterStartupProbe.Run` (no exporter test
assemblies, no `testables`, exactly one `VR Vlog/` menu entry, the menu opens the
exporter window, refresh and reload keep the backend, the report says
`Status: Ready`). It then builds a Humanoid skeleton in code with one skinned box
using `new Material(Shader.Find("lilToon"))`, and calls the same internal methods
as the export window through reflection: `LilToonExporterWindow.PackageVersion`,
`LilToonExporterWindow.RequireSupportedLilToon` (version range and shader
structure check) and `UniVrmOneClickExporter.Export`. Options the window can
set (gimmicks, blink, poses, recovery, license) keep their defaults. It reads
the GLB JSON chunk of the result.

Since 2026-10-22 the method is `RequireLilToon(GameObject, ICollection<string>)`
and it stops only without lilToon or with a changed baker shader
([decision](../../../Documentation~/decisions/2026-10-22-liltoon-no-version-gate.md)).
`ZipProbe.cs.txt` is kept as it ran; running it again needs that name and a
warnings list.

## Results

| lilToon | Unity | UniVRM / UniGLTF | Menu | Ready | Export | `asset.extras.VRVLOG_lilToonInstalledVersion` | `sourceLilToonVersion` | Editor run (s) |
|---|---|---|---|---|---|---|---|---|
| 2.0.0 | 2022.3.22f1 | 0.131.2 / 0.131.2 | opened | Ready | passed | 2.0.0 | 2.3.4 | 251 |
| 2.1.0 | 2022.3.22f1 | 0.131.2 / 0.131.2 | opened | Ready | passed | 2.1.0 | 2.3.4 | 639 |
| 2.1.10 | 2022.3.22f1 | 0.131.2 / 0.131.2 | opened | Ready | passed | 2.1.10 | 2.3.4 | 262 |
| 2.2.1 | 2022.3.22f1 | 0.131.2 / 0.131.2 | opened | Ready | passed | 2.2.1 | 2.3.4 | 410 |
| 2.3.0 | 2022.3.22f1 | 0.131.2 / 0.131.2 | opened | Ready | passed | 2.3.0 | 2.3.4 | 286 |
| 2.3.4 | 2022.3.22f1 | 0.131.2 / 0.131.2 | opened | Ready | passed | 2.3.4 | 2.3.4 | 753 |

All six Unity runs exited 0 with no compiler error. `results.json` has the same
values per version. The editor time includes the first import; another Unity
project was building on the same PC during these runs.

The six exported files have the same size (35,576 bytes). Their glTF JSON is
identical except for the installed-version value and the random suffix of the
placeholder blend shape name that the exporter adds when no blink is exported.
This only covers a default lilToon material; the comparison of each version's
real export with the export after upgrading to 2.3.4 is a separate check (#149).

## Limits

- A clean first open does not reproduce every user project's import and reload
  history.
- The avatar is generated and has one default lilToon material. Appearance is
  covered by `../LilToonVersionVisual/`.
- Loading these files in the app on an Android device was not done in this run
  (the device was not reachable).
