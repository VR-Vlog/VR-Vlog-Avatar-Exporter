# Avatar Optimizer 1.7.13, 1.8.15 and 1.9.20

The accepted Avatar Optimizer (AAO) range is unchanged: 1.7.0 or later, below
2.0.0. These runs check that range at its low end, middle and newest stable
release after two changes:

- After NDMF and the optimizers have run on the export copy, the exporter checks
  that a humanoid source still has a humanoid root Animator and the 15 bones
  Unity requires for a humanoid. If one is missing, it stops before UniVRM saves
  and names the missing bones. The source avatar is not changed.
- The AAO test fixtures now match real avatars: a VRChat Avatar Descriptor with
  the default playable layers, a root humanoid Animator, and a body mesh weighted
  to every mapped bone.

The Unity projects were made with `Tools/create-liltoon-version-projects.py`.
The package versions come from the public VPM listings. Each project uses Unity
2022.3.22f1, Built-in RP, Windows, in batch mode with graphics. These are Unity
Editor results, not device results. `results.json` has every run with its
exporter commit.

| Set | AAO | NDMF | Modular Avatar | VRChat SDK | UniGLTF/VRM | lilToon |
|---|---|---|---|---|---|---|
| E1 | 1.7.13 | 1.8.3 | not installed | 3.7.0 | 0.131.0 | 2.0.0 |
| E2 | 1.8.15 | 1.8.3 | 1.13.4 | 3.10.5 | 0.131.3 | 2.3.4 |
| U2 | 1.9.20 | 1.14.8 | 1.18.7 | 3.10.5 | 0.131.2 | 2.3.4 |

## Results

`PhysBoneSpringExportTests`, `NdmfBlinkPreparationTests`,
`InstalledAaoNeutralEndpointExportTests` and `AvatarOptimizerBoxIntegrationTests`:

| Set | AAO | Passed | Failed | Skipped |
|---|---|---|---|---|
| E1 | 1.7.13 | 27 | 0 | 27 |
| E2 | 1.8.15 | 36 | 0 | 18 |
| U2 | 1.9.20 | 54 | 0 | 0 |

The skips:

| Cases | Sets | Reason |
|---|---|---|
| 15 box-removal cases | E1, E2 | The fixtures have no Descriptor, so NDMF picks the generic platform. AAO runs there only from 1.9.0. |
| 2 blink merge and 3 endpoint rename cases | E1 | The fixtures use the configuration API added in AAO 1.8. |
| 3 endpoint rename cases | E2 | The fixtures use a configuration API that AAO 1.8.15 does not have. |
| 6 `ignoreOtherPhysBones` cases | E1 | The field exists from VRChat SDK 3.8.0. |
| 1 Modular Avatar case | E1 | Modular Avatar is not installed. |

`InstalledAvatarOptimizerPreservesSpringMotionAndCollisionsAfterFullExport`
passes on all three sets. Before the fixture change it failed on all three:

- On 1.8.15 and 1.9.20, AAO removed the arm and leg bones, which only the Animator
  used. The VRM was then saved without them, and reimporting it failed for lack of
  `LeftUpperLeg`. With the weighted body the bones are used and kept, as on
  real avatars. If they are removed anyway, the new check stops before saving.
- On 1.7.13, AAO threw a `NullReferenceException` because the Descriptor had no
  playable layers.

The new case `PreparedCopyWithoutRequiredHumanBonesOrRootAnimatorStopsAndNamesThem`
removes bones and the Animator from a prepared copy. It passes on all three sets.
