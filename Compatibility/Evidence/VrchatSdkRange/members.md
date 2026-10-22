# VRChat SDK types and members the exporter reads, 3.4.2 to 3.10.6-beta.2

The DLLs of 62 VRChat SDK Base/Avatars versions (23 releases and 39
pre-releases from the official VPM listing) were read with reflection. Each row
is a type the exporter reaches by name, or a member of it: the field type and
serialization attributes, an enum constant, the base type (`@base`), or the
field and method lists (`@fields`, `@methods`). Each cell compares the version
with 3.10.5.

- `=`: the same as 3.10.5.
- `–`: the type or member does not exist in that version.
- The last column covers the pre-releases: `all =` when every pre-release that
  has the row matches, otherwise the pre-releases that differ or lack it.

287 checks were made. The table lists every type and the members that differ
in some version; members that match 3.10.5 in all 62 versions are left out. The
differences are:

- `VRCAnimatorPlayAudio` and `VRC_AnimatorPlayAudio` first appear in 3.5.2-beta.1.
- `VRCPhysBone.ignoreOtherPhysBones` first appears in 3.7.7-beta.1 (release 3.8.0).
- VRC Constraints first appear in 3.6.2-constraints.1. In 3.6.2-constraints.1
  and .2, `Sources` is a `ConstraintSource[]` array; from 3.6.2-constraints.3 it
  is `VRCConstraintSourceKeyableList`, which the exporter reads.

| Area | Type.member | 3.4.2 | 3.5.0 | 3.5.1 | 3.5.2 | 3.6.0 | 3.6.1 | 3.7.0 | 3.7.1 | 3.7.2 | 3.7.3 | 3.7.4 | 3.7.5 | 3.7.6 | 3.8.0 | 3.8.1 | 3.8.2 | 3.9.0 | 3.10.0 | 3.10.1 | 3.10.2 | 3.10.3 | 3.10.4 | 3.10.5 | prereleases |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Avatar Descriptor | VRCAvatarDescriptor | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Avatar Descriptor | VRCAvatarDescriptor+CustomAnimLayer | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Avatar Descriptor | VRCAvatarDescriptor+AnimLayerType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Avatar Descriptor | VRC_AvatarDescriptor+LipSyncStyle | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Avatar Descriptor | VRCAvatarDescriptor+CustomEyeLookSettings | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Avatar Descriptor | VRCAvatarDescriptor+CustomEyeLookSettings+EyeRotations | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Avatar Descriptor | VRCAvatarDescriptor+EyelidType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Expression Parameters | VRCExpressionParameters | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Expression Parameters | VRCExpressionParameters+Parameter | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Expression Parameters | VRCExpressionParameters+ValueType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Expressions Menu | VRCExpressionsMenu | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Expressions Menu | VRCExpressionsMenu+Control | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Expressions Menu | VRCExpressionsMenu+Control+Parameter | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Expressions Menu | VRCExpressionsMenu+Control+ControlType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| PhysBone | VRCPhysBone | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| PhysBone | VRCPhysBone.ignoreOtherPhysBones | – | – | – | – | – | – | – | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2, 3.6.2-constraints.3, 3.6.2-constraints.4, 3.7.0-beta.1, 3.7.0-beta.2, 3.7.1-beta.1, 3.7.1-beta.2, 3.7.2-beta.1, 3.7.2-beta.2, 3.7.2-beta.3, 3.7.6-beta.1, 3.7.6-beta.2 |
| PhysBone | VRCPhysBoneBase+MultiChildType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| PhysBone | VRCPhysBoneBase+LimitType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| PhysBone | VRCPhysBoneBase+ImmobileType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| PhysBone | VRCPhysBoneBase+Version | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| PhysBone | VRCPhysBoneCollider | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| PhysBone | VRCPhysBoneColliderBase+ShapeType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| VRC Constraint | VRCRotationConstraint | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCRotationConstraint.IsActive | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCRotationConstraint.Locked | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCRotationConstraint.GlobalWeight | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCRotationConstraint.SolveInLocalSpace | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCRotationConstraint.FreezeToWorld | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCRotationConstraint.TargetTransform | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCRotationConstraint.Sources | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCRotationConstraint.AffectsRotationX | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCRotationConstraint.AffectsRotationY | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCRotationConstraint.AffectsRotationZ | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.IsActive | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.Locked | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.GlobalWeight | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.SolveInLocalSpace | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.FreezeToWorld | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.TargetTransform | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.Sources | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCAimConstraint.AffectsRotationX | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.AffectsRotationY | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.AffectsRotationZ | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.AimAxis | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCAimConstraint.WorldUp | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCLookAtConstraint | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCLookAtConstraint.IsActive | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCLookAtConstraint.Locked | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCLookAtConstraint.GlobalWeight | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCLookAtConstraint.SolveInLocalSpace | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCLookAtConstraint.FreezeToWorld | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCLookAtConstraint.TargetTransform | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCLookAtConstraint.Sources | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCParentConstraint | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCParentConstraint.IsActive | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCParentConstraint.Locked | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCParentConstraint.GlobalWeight | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCParentConstraint.SolveInLocalSpace | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCParentConstraint.FreezeToWorld | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCParentConstraint.TargetTransform | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCParentConstraint.Sources | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCPositionConstraint | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCPositionConstraint.IsActive | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCPositionConstraint.Locked | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCPositionConstraint.GlobalWeight | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCPositionConstraint.SolveInLocalSpace | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCPositionConstraint.FreezeToWorld | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCPositionConstraint.TargetTransform | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCPositionConstraint.Sources | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCScaleConstraint | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCScaleConstraint.IsActive | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCScaleConstraint.Locked | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCScaleConstraint.GlobalWeight | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCScaleConstraint.SolveInLocalSpace | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCScaleConstraint.FreezeToWorld | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCScaleConstraint.TargetTransform | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1 |
| VRC Constraint | VRCScaleConstraint.Sources | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.totalLength | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.overflowList | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source0 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source1 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source2 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source3 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source4 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source5 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source6 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source7 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source8 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source9 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source10 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source11 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source12 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source13 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source14 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSourceKeyableList.source15 | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSource | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSource.SourceTransform | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| VRC Constraint | VRCConstraintSource.Weight | – | – | – | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | differ: 3.5.2-beta.1, 3.5.2-beta.2, 3.5.3-beta.2, 3.6.0-beta.1, 3.6.1-beta.2, 3.6.1-beta.3, 3.6.2-beta.1, 3.6.2-constraints.1, 3.6.2-constraints.2 |
| Contact | VRCContactReceiver | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Contact | VRCContactSender | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Avatar components | VRCSpatialAudioSource | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Avatar components | IEditorOnly | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRCAvatarParameterDriver | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRC_AvatarParameterDriver+Parameter | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRC_AvatarParameterDriver+ChangeType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRCAnimatorTrackingControl | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRC_AnimatorTrackingControl+TrackingType | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRCPlayableLayerControl | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRC_PlayableLayerControl+BlendableLayer | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRCAnimatorLayerControl | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRC_AnimatorLayerControl+BlendableLayer | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRCAnimatorLocomotionControl | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Animator behaviours | VRCAnimatorTemporaryPoseSpace | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Play Audio | VRCAnimatorPlayAudio | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Play Audio | VRCAnimatorPlayAudio.@base | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Play Audio | VRC_AnimatorPlayAudio | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Play Audio | VRC_AnimatorPlayAudio.@base | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Play Audio | VRC_AnimatorPlayAudio.@fields | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
| Play Audio | VRC_AnimatorPlayAudio.@methods | – | – | – | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | = | all = |
