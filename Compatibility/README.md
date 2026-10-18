# Dependency compatibility maintenance

`dependencies.json` is the reviewed dependency policy. Run
`python3 Tools/generate-compatibility.py --write` after changing it, and run the
same command without `--write` to detect drift. Generated source is included in
the installable package; development manifests and test tools are excluded.

The independent Editor compatibility assembly references only Unity. It owns
the menu and environment diagnostics. The UniVRM backend and its tests compile
only when both VRM10 and UniGLTF are in the accepted 0.131 patch line. The runtime
guard also rejects mixed versions. Missing/unsupported packages therefore do
not make this exporter's UniVRM references break compilation before diagnostics.
Compilation errors inside another package can still prevent Unity from loading
any new editor code; this cannot repair those packages.

The menu resolves the loaded backend's public `Open()` method when it is used;
there is no startup registration callback. Missing lookups are not cached.
Explicit **Reload** reimports this exporter's assembly definition and requests a
clean script compilation only when dependencies are supported and the backend
is absent. It never changes dependencies or avatar assets. The diagnostics copy
includes package versions, compilation state, loaded backend and version defines,
without absolute paths or avatar data. A missing backend alone is not evidence
of a compile error in another tool.

The VPM requirement stays at `0.131.x` to avoid forcing changes to an existing
creator project. Every release of that patch line can export when UniGLTF and
VRM have the same version (`[0.131.0,0.131.999999999]` version defines; prereleases,
build metadata and other minor lines stop). `uniVrm.versions` lists the patches
actually verified with their official source commit; a new 0.131 patch is
accepted before it is listed, and VCC / ALCOM can install it into a creator
project without anyone touching the exporter.

Accepting unverified patches has a cost. When the version defines named each
verified patch, an unlisted patch only left the backend out and diagnostics
explained why. Now the backend compiles against every 0.131.Z. If a later patch
changes the signature of a UniVRM API the exporter calls, the result is a
compile error inside this package in the creator's project, not a diagnostics
message: in an open editor the script reload stops, on the next project open
Unity offers Safe Mode, and the VRChat SDK refuses to build while compile errors
exist. Patches in this line do change public API: 0.131.2 to 0.131.3 changed
the type of `GlbChunk.Bytes` from `ArraySegment` to `ReadOnlyMemory`, which the
exporter does not use. A behavior change behind an unchanged signature compiles
and is only found when that patch is verified. 0.131.3 is such a case:
`NativeArrayManager.CreateNativeArray(T[])` wraps the managed array instead of
copying it, so each morph's normal array must be a separate allocation.

The mitigation is to verify each new 0.131 patch promptly, before or as soon as
creators can install it: run the `compatibility`, `exporter-behavior` and
`exporter-integration` profiles below with that patch (the runner accepts an
unlisted 0.131 release), then add it to `uniVrm.versions` with its source
commit (evidence: `Evidence/UniVrm0131x/README.md`). If it breaks, ship an
exporter fix, or an exporter release whose accepted range excludes that patch.
`Tools/check-upstream-dependencies.py` reports an upstream UniVRM release newer
than the newest listed patch. A new minor line (0.132 and later) still requires
review and a new adapter.
Diagnostics never install, upgrade or downgrade packages. MA and NDMF remain optional; existing component,
API and ownership checks run during preparation. Reference versions are not a
claim that every avatar or every combination is verified.

The VRChat SDK is optional as well. It matters only when an expression file uses
VRC Animator Play Audio: the capture copy omits that behaviour, and the exporter
accepts it when VRChat SDK Base and Avatars are the same release with
`vrchatSdkMinimum <= version < vrchatSdkBelow` (3.5.2 to below 3.11.0). 3.5.2 is
the first stable release with `VRCAnimatorPlayAudio`; its fields and members are
identical through 3.10.5. Results:
[Evidence/VrchatSdkRange](Evidence/VrchatSdkRange/README.md).

Modular Avatar has its own range only for the appearance freeze, which runs
when the avatar has Shape Changer, Object Toggle, Material Setter, Material Swap
or Mesh Cutter: `modularAvatarMinimum <= version < modularAvatarBelow` (1.18.0
to below 1.19.0, stable releases only). The freeze reflects MA's preview API;
`IMeshSelector` first appears in 1.18.0 (1.17.x and earlier use
`IVertexFilter`), and 1.19.0-alpha.1 replaces the `InitialStates` analysis. Other
versions stop before that API is touched, naming the installed version and the
range. Avatars without those components export with any MA version. MA 1.18.x
itself requires NDMF 1.11.0 or later; the exporter's own NDMF floor stays
`ndmfMinimum` (1.8.3).

## Supported lilToon versions

The exporter accepts lilToon 2.0.0-2.3.4 (`lilToon.supported` in
`dependencies.json`). This is the range where lilToon's own material migration
alone gives the 2.3.4 structure. On Unity 2022.3.22f1 (Built-in RP), the
migrated shader properties, types, dimensions, pass names and LightMode of
2.0.0, 2.1.0, 2.1.10, 2.2.0, 2.2.1, 2.3.0 and 2.3.3 matched 2.3.4 exactly, and
the structure hash of every tag from 2.0.0 to 2.3.4 matches (see
[lilToon shader structure](#liltoon-shader-structure)). 1.x keeps 35 properties that 2.3.4 does not
have and lacks 3 that it has. Shader Setting changes, encrypted materials and
custom shaders were not measured.

As of 2026-10-18, 2.3.4 is the newest lilToon release and the official VPM
listing has no pre-release, so the range covers every stable 2.x release. 1.x
is not accepted: the exporter requires the 2.3.4 property set exactly, 2.0.0
changed the fur, lighting and Light Volumes shader code, and 1.x packages
declare Unity 2018.3. Updating the project to lilToon 2.0.0-2.3.4 migrates 1.x
materials so they export. See
`Documentation~/decisions/2026-10-18-liltoon-range-review.md`.

Before exporting, these versions stop with the reason and the next step:

| Installed lilToon | What to do |
| --- | --- |
| Not installed | Add lilToon 2.0.0-2.3.4 in VCC / ALCOM |
| 1.x or older | Update to 2.0.0-2.3.4; lilToon migrates materials |
| Newer than 2.3.4 | Return to 2.3.4 or wait for a supporting exporter |
| Pre-release or unreadable | Select a stable 2.0.0-2.3.4 release |

Within the range, the exporter also checks before each export that the
installed lilToon shaders have the same structure as 2.3.4 and stops if they
differ, for example when a pass was removed in lilToon's Shader Setting.

Exported files keep `sourceLilToonVersion` at `2.3.4`, meaning the lilToon
catalogue that the materials follow, so existing VR Vlog versions read them
unchanged. The version installed in the source project is written to
`asset.extras.VRVLOG_lilToonInstalledVersion`; the app does not use it.

The exporter does not convert values per lilToon version. The app renders
materials with 2.3.4 rules, so a material made in an older version looks the
same as after updating that project to lilToon 2.3.4. Known differences from
the version the material was made in:

- 2.3.0 and later multiply backlight strength by the alpha of
  `_BacklightColor` and `_BacklightColorTex`. Before 2.3.0 the alpha was
  ignored, so a material with alpha below 1 becomes darker.
- 2.1.0 clamps the UV1 used by `_MatCapBlendUV1` to 0-1. In lilToonLite it
  also changed how shadow color mixes with environment light; this shows only
  when the main light color is below 1.
- Outline Lit is computed per vertex up to 2.1.9 and per pixel from 2.1.10.

Rendered comparisons of these cases are in
[Evidence/LilToonVersionVisual](Evidence/LilToonVersionVisual/README.md).

`_EnvRimBorder` (the VRC Light Volumes rim) defaults to 0.85 in 2.0.0-2.1.9
and 3.0 from 2.1.10, and migration keeps the stored value. The exporter does
not write this property, so the difference does not reach the exported file.

These are lilToon's own changes. Correcting them in the exporter would add
per-version conversions that this project does not maintain.

## lilToon shader structure

`Formats/Runtime/LilToon234Catalogue.cs` is generated from the Built-in RP shader
declarations of the lilToon 2.3.4 tag by `Tools/generate-liltoon-catalogue.py`
(`catalogue --fetch 2.3.4 --check` confirms it byte for byte). The same tool
hashes the shader structure of each lilToon tag into `liltoon-structure.json`
(`structure --fetch --write`): shader names, property names, types, runtime
texture dimensions, named passes and LightMode, without defaults or declaration
order. `Tools/check-liltoon-structure.py` runs offline in the validation receipt
and fails when the catalogue no longer matches the table or a tag in the
supported lilToon range (default 2.0.0-2.3.4) has a different structure. The
definition and the per-tag results are in
[the decision record](../Documentation~/decisions/2026-10-14-liltoon-structure-hash.md).
A matching hash is not proof of visual equivalence.

## Upgrade procedure

1. Run `python3 Tools/check-upstream-dependencies.py --output upstream.json`
   or the manual **Check upstream dependency releases** Actions workflow. This only
   reports stable upstream releases; it neither changes dependencies nor starts
   a Unity build. For a newer lilToon release it also downloads that tag's
   source and reports whether its shader structure matches the 2.3.4 catalogue. No scheduled CI or paid build was introduced.
2. Review the official changes. Add a version and immutable source commit to the
   policy in a branch. For a different UniVRM API family, add a separately gated
   backend; keep third-party types out of the compatibility assembly. For a
   different lilToon property/keyword catalogue, review exporter and app readers,
   shader specialization and format version together. Do not merely relax the
   version/commit check.
3. In separate empty Unity projects, install the candidate's actual UniGLTF and
   VRM10 packages from the same source commit, lilToon 2.3.4, this exporter as a
   local development package and Unity Test Framework 1.4.6. Include
   `"testables": ["com.vrvlog.liltoon-vrm-exporter"]` in each manifest. Use
   `com.unity.collections` 2.1.4 for the existing mesh tests. Keep lilToon's Shader
   Setting at its defaults: `LilToonStructureCheckTests` compares every installed
   official lilToon shader with the 2.3.4 catalogue. Never test by changing
   only a supported package's version label and claim a real upstream result.
4. Run the focused test runner for every version in `uniVrm.versions`, and for
   a new 0.131 patch before it is listed:

   ```text
   python3 Tools/run-unity-compatibility.py --unity UNITY_EXECUTABLE --project TEST_PROJECT --expect-univrm 0.131.1 --expect-unity 2022.3.22f1 --output TEST_RESULTS
   ```

   Use 2022.3.22f1 for the existing creator environment. Also test the app's
   separate release baseline with `--expect-unity 2022.3.62f3` (the runner default).
   This does not require creators to upgrade their editor. The runner
   rejects missing/old results, skipped required tests and unexpected installed
   versions. It tests ordinary and full-lilToon export/reimport, meshes, morphs,
   material bindings, source preservation, renderer selection and skin weights.
   Like the exporter, the runner treats every release of `uniVrm.vpmRange`
   (0.131.x without prerelease or build labels) as supported, listed or not; its
   JSON receipt records `uniVrmSupported` and `uniVrmListed`.
   `create-liltoon-version-projects.py` accepts the same range.
5. Repeat with no UniVRM (`--expect-univrm missing`) and an unsupported-package
   fixture to verify the independent diagnostics. A synthetic unsupported
   fixture proves exclusion/diagnostics only, never future API compatibility.
   Run the existing MA/NDMF tests with the actual optional packages when those
   integrations change. Use the app's loading and full-lilToon behavior tests and
   compare transparent materials, expressions and representative avatars on a
   device before release. Synthetic mesh tests do not prove visual equivalence.
6. Record exact versions, editor, source commits, results and remaining limits.
   Review the PR and required checks before changing the supported matrix.

## lilToon version projects

To repeat the focused tests for each lilToon release, create one empty project
per version:

```text
python3 Tools/create-liltoon-version-projects.py --output VERSION_PROJECTS --dependencies PINNED_PACKAGES --unity UNITY_EXECUTABLE
```

`PINNED_PACKAGES` contains the unpacked official UniGLTF and VRM packages of one
supported version and, for the exporter profiles, the VRChat SDK, NDMF and
Modular Avatar packages. The script embeds the official lilToon VPM release ZIP
(default 2.0.0, 2.1.0, 2.1.10, 2.2.1, 2.3.0 and 2.3.4; `--versions` selects others),
references this checkout with `file:` and `testables`, and records each ZIP's
source URL and SHA-256 after checking it against the reviewed SHA-256 listed in
the script (`--sha256 VERSION=SHA256` for an unlisted release). With `--unity`,
it opens every project once in batch mode
and records the Unity, lilToon and dependency versions Unity actually resolved in
`liltoon-version-project.json`; a compile error or a different resolved version
stops it. Existing folders are never reused. lilToon is not committed here.

Pass `--expect-liltoon VERSION` to the runner. It reads the project's
`packages-lock.json` and the installed lilToon `package.json` before launching
Unity and stops on a different, missing or unreadable version. The JSON receipt
records the expected and installed lilToon version, with or without this option.

### Export parity after upgrading lilToon

The supported range rests on one claim: exporting with lilToon X gives the same
file as upgrading the project to 2.3.4 and exporting again, apart from
`asset.extras.VRVLOG_lilToonInstalledVersion`. Check it per version project:

```text
python3 Tools/run-liltoon-version-parity.py --unity UNITY_EXECUTABLE --project VERSION_PROJECTS/liltoon-X --upgrade-package VERSION_PROJECTS/liltoon-2.3.4/Packages/jp.lilxyzw.liltoon --work WORK --output OUTPUT
```

The script copies the version project (the original stays as it is), points the
copy at this checkout and runs
`Tests/Editor/LilToonVersionParityHarness.cs` in batch mode. The harness saves
synthetic materials made with lilToon X (backlight colour and texture alpha below
1, fur, Outline Lit, MatCap UV1 blend, shadow colours, rim, rim shade,
transparent and cutout main textures, and the default material of every catalogue
shader) and exports each on the synthetic Humanoid used by the tests (`A`, then
`A-again` in the same session). It then replaces the embedded lilToon with 2.3.4,
reopens the copy so lilToon's own startup runs, and exports the saved materials
again (`B`). `Tools/compare-liltoon-version-exports.py` compares every glTF JSON
value exactly and every bufferView by SHA-256, including the DEFLATE chunks of
`VRVLOG_materials_liltoon`. The exporter's per-export placeholder morph target
names (`__VRVlog_BlinkNone_<GUID>`) are renumbered first because two exports in
the same session already differ there. `OUTPUT/parity.json` records the
versions, the commit and both comparisons. Results:
[Evidence/LilToonVersions](Evidence/LilToonVersions/README.md).

## Exporter behavior and integration regression profiles

Dependency compatibility alone does not prove the one-click export pipeline.
Before releasing changes to neutral sampling, expression endpoints or preparation,
run the generated-fixture behavior profile in a separate, explicitly pinned project:

```text
python3 Tools/run-unity-compatibility.py --profile exporter-behavior --unity UNITY_EXECUTABLE --project INTEGRATION_TEST_PROJECT --expect-univrm 0.131.0 --expect-unity 2022.3.22f1 --output TEST_RESULTS
```

The default `compatibility` profile requires 31 real-package cases
in a supported environment, or two diagnostic cases in a missing/unsupported one.
Both exporter profiles require a supported real UniVRM environment, the packages
listed in step 3, and the actual VRChat SDK, Modular Avatar and NDMF. Use the
reviewed authoring reference versions in `dependencies.json`; pin and record the
actual SDK version as well. These test prerequisites do not make MA/NDMF mandatory
dependencies for ordinary exporter users. The runner installs or changes nothing.

The behavior profile selects these complete classes in addition to compatibility:

- `NeutralShapeSamplerTests`, `ParameterDriverExpressionTests`, `MergedFxDefaultsTests`
- `DirectExpressionNativeLayersTests`, `MergedFixedNeutralSamplingTests`, `NeutralCurveConditionTests`
- `NeutralShapeEndpointTests`, `PreparedNeutralEligibilityTests`, `PreparedNeutralExportTests`
- `NeutralShapePipelineTests`, `NeutralShapeExportTests`, `UnifiedExpressionExportTests`
- `MaSceneReferencePreparationTests`, `NdmfPreparationTests`
- `FaceEmoPreparedFxIntegrationTests`
- `AdditionalPlayableCallbackTests`, `NeutralLayerControlTests`
- `TemporalNeutralShapeTests`

They cover fixed external inputs and dormant Action branches; whole prepared
appearance versus independent neutral morphs; native Additive/Override and Write
Defaults behavior; native source-frame clamping versus unlimited explicit endpoint
arithmetic; scalar sampling with independent Transform support on influencing
skin bones; absolute endpoints;
blink eligibility after preparation; renderer mapping; source-reference ownership;
phase ordering and cleanup; and real export/reimport. Native Animator evaluation
and source geometry provide independent expectations. Required cases also cover
optional missing/unexported roots, normal-external ownership pruning, direct or
reachable Copy-linked appearance controls (including transitive range conversion),
and source tracking obligations after preparation removes an authoring marker.
Direct gesture probes also require normal VRChat inputs and authored contact
defaults to match native sampling, while unresolved external inputs remain rejected.
Registration tests retain the selected state's effective clip and deterministic
callbacks, reject invalid or ambiguous callback provenance, and keep different
native outcomes from a shared clip. Identical evaluated registrations remain
deduplicated. The generated FaceEmo fixtures exercise exact or proved retargeted
clip identity and reject missing/changed state, motion or callback context.
An original registered clip identity cannot select a state when its effective
override supplies different motion data.
An existing NDMF object-registry origin may prove a uniquely matching augmented
prepared clip; the actual effective prepared motion and its callbacks then define
the entry. Missing/wrong/ambiguous origins and deferred clip or override changes
remain rejected.
Direct scalar support must keep prepared bone/wardrobe appearance, protect
captured Renderer activation, and defer unknown authored channels until prepared
renderer/mesh resolution.
An active base-state probe must retain its callbacks and dynamic upper graph;
an inactive slot must retain its authored clip without being force-enabled, and
reject callbacks that could change the retained graph.
An exactly identified moving gesture must retain its native lower, Additive and
Write Defaults support without an unrelated upper expression's reset. A proved
permanent upper override and a configured single-state BlendTree's native
contribution remain authoritative. Disconnected unsupported clips must not affect neutral sampling. Native Unity
and VRChat constraint settings keep their original WD/additive support without
committing the probe's pose. Unsupported appearance support retains the affected
prepared neutral weights with a warning; selected expression endpoints still
require independent validation. Coupled required morphs retain the whole prepared
appearance as their neutral instead of rejecting export. Bad curves, ambiguous
bindings, events and unknown callbacks remain errors and are checked before any
recoverable neutral refusal. A last, stationary, unmasked full Override layer
that explicitly writes every captured morph can prove those scalars independent
of lower state inputs and timing. The full native evaluation graph stays intact;
partial weights and masks do not authorize this exception.

In `0.11.11-beta.9`, documented SDK Animator/Playable layer-weight commands are
classified separately from unknown callbacks. An unsupported neutral effect
retains the affected prepared appearance with a warning; it does not authorize a
selected endpoint. A full final scalar override can remain independent only of
commands targeting lower FX layers, never the final layer or the whole FX playable.
SDK callbacks are not executed or emulated, and a goal weight of one is not a
no-op proof. Other playable data and reachable parameter writers are checked before
neutral fallback, including parameters read across controller boundaries. Original
controllers and avatar settings remain unchanged. See [avatar preservation](AvatarPreservation.md)
for the body-playable metadata boundary and the difference from running VRChat.
Copy ownership does not expand through Set/Add/Random or generic reset morphs.
The transient-input matrix requires an unsaved declared signal with compatible
types/defaults, no menu input, a raw producer in another playable, no raw FX
producer, and no producer reachable under that playable's normal external inputs.
Saved/exposed, unwritten, FX-written, external, unknown, explicitly selected or
remotely synchronized inputs, and inputs with reachable random writers, must
retain their appearance alternatives.
The actual SDK serialized menu inventory covers shared/cyclic pages and null
optional inputs; missing required submenus or unknown data cannot prove absence.
The influencing-bone loop regression compares native scalar sampling with real
export/reimport geometry at the retained prepared pose and explicit authored
endpoints. Probe Transform animation must remain on the native graph, but its
pose must not be copied into the prepared avatar. Bone or vertex overlap alone
does not prove that a Transform curve changes a BlendShape weight.
The unclamped source-range case preserves the explicit arithmetic contract; it
does not claim that Editor BakeMesh is a native oracle for an unclamped player.
Run the native clamped-range and negative-rest geometry cases through the
GPU-backed batch behavior or integration profile above. An interactive EditMode
runner can keep the native skinning policy stale after PlayerSettings readback
changes, even while Editor updates and Camera.Render complete. The shared test
helper queues four updates and verifies the declared true policy on a fresh
100-frame BakeMesh sentinel before the full geometry oracle proceeds. A failed
precondition must not be skipped or replaced with relaxed geometry expectations.
The required one-click
additive geometry regression must pass even if it failed on an earlier development
commit. A known failure is not a release exemption.
Missing ordinary blink can use only a surviving moving Unified Expression route.
Disabled, missing, inert and fully closed routes remain rejected. A negative source
rest normalized by the native clamp policy is covered separately by real reimport
and native geometry at coefficients 0, 0.5 and 1; the source weight stays intact.

Temporal rest is a separate contract from a selectable animated expression.
Inspect complete curves on active effective clips after the normal-context,
dependency and callback checks. Preserve a genuinely varying morph channel's
prepared authored weight, rather than baking a sampled intermediate phase.
Reconstruct independent stationary channels through the same native graph,
including Write Defaults and Additive support. Keep the complete graph and binding
safety checks even when no stationary scalar remains to capture. This policy
does not make a varying curve constant, authorize animated Animator parameters or
unknown callbacks, or add automatic idle playback to the app. Existing selectable
programs retain their authored curves, duration and loop setting.

The synthetic regression matrix must include a delayed loop whose movement begins
beyond the sample window, weighted tangent variation, a nonzero prepared rest and
stationary siblings, partial Override/Additive composition, and source preservation.
History captured before temporal classification must not restore an excluded
temporal channel later. Prepared rest and explicit blink/tracking endpoints must
also survive real export/reimport and optimization. A varying lower writer has a
bounded dominance proof only when a later included Override layer has exact native
weight one, no mask or transition, exactly one current effective clip at weight
one, no next clip and an explicit constant curve for that same binding. Only that
lower layer/clip/binding is exempt from the curve check; timed state, parameter and
callback checks still run. Fractional, near-one, Additive, masked or moving upper
writers do not establish this proof, and finite sample equality alone cannot
provide it. Conservative
prepared-rest preservation must be reported, and cannot excuse a missing required
route, an altered authored endpoint or an inert required blink. A zero residual
that independently matches an explicitly authored endpoint is not itself a failure.

Additional playable inspection distinguishes structurally valid known SDK effects
from unknown or malformed callbacks. A known layer-control command without a
parameter write may be excluded only when typed fixed-input reachability proves
its branch dormant. Every surviving unsupported command remains a diagnostic.
Raw writers and cross-controller parameter type conflicts still prevent invariant
pruning. Test default/entry/timed paths, selected inputs, authored writers, malformed
command data and unknown callbacks as well as the ordinary dormant AFK branch.
Neither temporal rest nor additional playable handling may use an avatar or clip
name as a compatibility exception.

For AAO/NDMF optimization changes, run the integration profile with a pinned actual
AAO installation too:

```text
python3 Tools/run-unity-compatibility.py --profile exporter-integration --unity UNITY_EXECUTABLE --project INTEGRATION_TEST_PROJECT --expect-univrm 0.131.0 --expect-unity 2022.3.22f1 --output TEST_RESULTS
```

It additionally requires `InstalledNdmfNeutralExportTests`,
`InstalledAaoNeutralEndpointExportTests`, `NdmfBlinkPreparationTests` and
`PhysBoneSpringExportTests`. These
exercise the installed processors, moved/replaced renderers, mesh deletion and
merge, endpoint remapping, and the difference between callback-free preview and
export optimization, including spring motion and collisions after real export.
The optional AAO spring fixture checks real package registration before reflection;
an absent package is a skip, while an installed but unloaded component is a failure.
Record the exact AAO version and any explicitly applied
dependency patch; use the fixture's supported configuration API. Missing or
unsupported integration packages cause required skips and therefore a failed run.
The AAO spring fixtures carry a VRChat Avatar Descriptor and a weighted body, as
real avatars do. The box-removal fixtures have no Descriptor, so NDMF picks the
generic platform, where AAO runs only from 1.9.0; they skip on older AAO.
`Compatibility/Evidence/AaoRange/` has the 1.7.13, 1.8.15 and 1.9.20 results.

The runner runs each selected class and checks named critical regressions and their
parameter counts. A missing class/case, duplicate identity, failed/skipped case,
unexpected class or unsuccessful root result cannot satisfy the gate. Extra tests
added to a selected class must also pass. XML/log names are fresh per invocation;
the JSON report records the profile, counts, expected environment and, when
available, the runner checkout's package version, Git commit and dirty state.
`runnerSource` identifies the checkout that supplies the runner; it does not prove
that a different exporter copy installed in the test project has the same source.
Point the project's local development package at that checkout, or verify the
installed candidate against it, and record the production ZIP hash separately.
A dirty checkout's commit alone cannot identify its uncommitted fixes.

These fixtures create their own meshes, controllers and avatars; they require no
private or purchased avatar. Obtain actual dependency packages from their official
sources and preserve their pins and licenses. Do not replace SDKs/processors with
stubs or relabel unsupported packages to obtain a passing integration report.
`Tools/test-compatibility-tooling.py` uses artificial NUnit XML to test the result
gate itself; its success is not evidence that Unity behavior passed.

Use an available, appropriately licensed Editor for these runs. No cloud Unity CI
or license provisioning is introduced by these profiles. Host CI and the release
workflow run the same AAO patch, LAN and pose checks, but do not launch Unity.
Keep actual Unity XML and exact tested versions/commits in release review evidence;
do not substitute a skipped Unity job with a successful source-only check. Repeat
the relevant profile on the separate 2022.3.62f3 baseline and each supported UniVRM
version when the change affects that matrix. Existing real-avatar/app/device visual
checks still apply; generated fixtures do not prove every avatar's appearance.

Development packages must be distinguishable from an already published stable
version. Assign the next development version before distributing a changed main
checkout, and prepare the final stable version/changelog only after review and
required checks. Preserve published tags; the release workflow refuses to replace
an existing tag. Retest the final version's actual package, and never use an older
version label or XML to claim that a new candidate was verified. Private avatar
assets, local paths, logs and screenshots remain outside public packages.

For startup changes, also validate the release ZIP as embedded packages, as used
by VCC/ALCOM, with the actual VRChat/MA/NDMF packages from a report. The focused
runner includes a menu test that removes the old startup registration to verify
the menu cannot become permanently unavailable. Test the production menu once
without exporter test assemblies or `testables`, so test-only references cannot hide
an assembly-loading problem. For the production probe, copy
`Tests/Startup/ExporterStartupProbe.cs` into the isolated project's `Assets/Editor`
and run Unity with `-batchmode -quit -projectPath TEST_PROJECT -executeMethod
ExporterStartupProbe.Run -logFile STARTUP_LOG`. The probe rejects exporter test assemblies and `testables`, and checks the real
menu, refresh and diagnostic report. UniGLTF itself depends on Test Framework;
that upstream dependency is preserved.
A successful clean startup does not reproduce every
user project's import/reload history; record this limit explicitly.

The file format remains schema 2.0 for full lilToon (legacy 1.x remains readable).
The app's embedded 2.3.4 catalogue and shaders are unchanged. More versions mean
more validation work; exact gates deliberately trade immediate adoption of an
unknown update for predictable exports and actionable errors.
