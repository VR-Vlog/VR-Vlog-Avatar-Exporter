# PhysBone to VRM SpringBone: how close the swing is

These runs compare the conversion before and after
`Editor/PhysBoneMotionMapping.cs`. The formulas are in
[PhysBone.md](../../PhysBone.md). Every number is the source PhysBone against a
VRM SpringBone copy of the same chain in the same Unity process. They are Unity
Editor results (Unity 2022.3.22f1, batch mode with graphics), not device results.
`results.json` has the aggregate numbers. It has no model or bone names.

## What was run

Each run plays one timeline at 60 fps, 13 s long:

| Section | Time (s) | Motion |
|---|---|---|
| idle | 0-1.5 | Still, after one second of settling |
| walk | 1.5-4.8 | Walk forward |
| turn | 4.8-5.8 | Turn 180 degrees |
| head | 5.8-9 | Shake and nod the head |
| jump | 9-10 | Jump |
| rest | 10-13 | Still, to see how the swing settles |

The motion drives Hips and the model root stays put, because the app moves a
model that way. Colliders are on.

Four bodies run next to the source:

- `old_raw`: the previous conversion with the plain VRM solver
- `new_raw`: this conversion with the plain VRM solver
- `old_app`: the previous conversion with the app's load settings (Center on the
  model root, drag raised by the default stability)
- `new_app`: this conversion with the app's load settings

## How the numbers are made

- Direction error: the angle between each bone's direction on the source and on
  the VRM copy, averaged over the frames. A chain's weight is bone length times
  bone count. Models count equally.
- Tip distance: RMS distance between the chain tips, in percent of chain length.
- Swing size: size of the tip's movement against the source. 1.00 means the same.
- Settling time difference: how far the time to come to rest is from the source.

Parts come from keywords in the bone names, so the part table is rough.

## Results on four models

Four avatars with many kinds of chains, 119 chains:

| | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|
| Direction error, mean (degrees) | 10.07 | 5.33 | 7.86 | 6.51 |
| Tip distance RMS (% of chain length) | 10.40 | 5.54 | 8.15 | 7.33 |
| Last bone rotation error (degrees) | 20.78 | 11.88 | 17.48 | 13.97 |
| Swing size against the source | 0.97 | 1.07 | 1.13 | 1.15 |
| Settling time difference (s) | 0.26 | 0.15 | 0.27 | 0.14 |

| Section | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|
| idle | 1.24 | 1.06 | 1.24 | 1.06 |
| walk | 13.80 | 5.63 | 10.08 | 7.68 |
| turn | 16.55 | 7.36 | 12.89 | 9.48 |
| head | 11.98 | 7.74 | 9.64 | 8.82 |
| jump | 18.21 | 10.29 | 13.24 | 12.10 |
| rest | 3.49 | 2.23 | 3.37 | 2.61 |

| Part | Chains | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|---|
| hair | 51 | 11.94 | 6.00 | 9.98 | 7.48 |
| skirt | 28 | 4.42 | 4.17 | 4.17 | 4.57 |
| tail | 5 | 12.14 | 4.44 | 7.78 | 7.81 |
| ear | 6 | 8.94 | 4.10 | 6.03 | 5.03 |
| breast | 4 | 0.54 | 0.48 | 0.55 | 2.25 |
| other | 25 | 9.95 | 4.68 | 7.00 | 5.12 |

The plain VRM improved on all four models. With the app's load settings, tail did
not improve, and breast and skirt got worse: the raised drag slows a swing that
now matches the source without it. One model went from 1.13 degrees on the plain
VRM to 5.81 with the app's settings. Those settings live in the app, so the
exporter does not compensate for them.

## Results on generated chains

Generated chains with no colliders, exported to a real VRM file and read back. Each
set covers combinations of pull, spring, stiffness, gravity, falloff, immobile,
integration type, version, length and starting direction.

| Set | Chains | Previous mean / median / worst | New mean / median / worst |
|---|---|---|---|
| 1 | 407 | 17.45 / 7.08 / 154.3 | 5.85 / 0.57 / 136.0 |
| 2 | 229 | 14.51 / 6.95 / 114.8 | 3.96 / 0.68 / 104.2 |
| 3 | 100 | 7.41 / 3.66 / 41.4 | 1.57 / 0.59 / 14.5 |

The worst chains point straight up with gravity 1. That rest pose is an unstable
balance on the source, so a small difference decides which way the chain falls.

## Results on the whole list

429 models, 13179 chains, out of the 465 combinations on the test list. The
same timeline and the same four bodies as above. The first 300 models were measured
before the last review fixes and the other 129 after them. Those fixes only touch a
force at its ceiling, a bone pointing straight up and a last bone scaled to zero. On
the four models above they moved the means by less than 0.01 degrees.

| | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|
| Direction error, mean (degrees) | 10.55 | 5.09 | 9.07 | 6.45 |
| Direction error, median model | 9.72 | 5.69 | 9.01 | 6.10 |
| Direction error, 90th percentile model | 12.97 | 7.68 | 13.38 | 9.38 |
| Direction error, worst model | 47.70 | 47.52 | 51.00 | 47.65 |
| Tip distance RMS (% of chain length) | 10.82 | 6.05 | 9.22 | 7.58 |
| Last bone rotation error (degrees) | 22.78 | 10.51 | 20.73 | 12.66 |
| Swing size against the source | 2.75 | 1.08 | 2.91 | 1.15 |
| Settling time difference (s) | 0.22 | 0.11 | 0.23 | 0.13 |

| Section | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|
| idle | 2.03 | 1.43 | 2.04 | 1.45 |
| walk | 14.99 | 5.84 | 11.98 | 7.78 |
| turn | 15.78 | 6.50 | 13.74 | 9.16 |
| head | 11.87 | 6.90 | 10.81 | 8.42 |
| jump | 18.09 | 8.88 | 14.26 | 10.61 |
| rest | 4.23 | 2.43 | 4.26 | 3.10 |

| Part | chains | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|---|
| hair | 5331 | 10.91 | 4.93 | 8.35 | 6.57 |
| skirt | 2228 | 5.67 | 5.11 | 5.73 | 5.55 |
| tail | 670 | 11.25 | 4.66 | 7.82 | 7.46 |
| ear | 850 | 9.12 | 4.16 | 6.69 | 5.59 |
| breast | 272 | 0.72 | 0.56 | 0.83 | 1.64 |
| sleeve | 198 | 17.19 | 4.87 | 16.25 | 5.90 |
| other | 3630 | 10.78 | 4.07 | 9.31 | 5.15 |

change {"raw": {"improved": 427, "same": 2, "worse": 0, "medianChange": -4.337257653179041, "worstIncrease": 0.23852893307358336}, "app": {"improved": 371, "same": 19, "worse": 39, "medianChange": -2.030707606274648, "worstIncrease": 9.824483602076128}}

Against the previous conversion, the plain VRM improved on 427 models, stayed the
same on 2 and got worse on none. The largest rise that still counts as the same
is 0.24 degrees. With the app's load settings, 371 models improved, 19
stayed the same and 39 got worse. The largest rise is 9.82 degrees, on a model
with 3 chains that measures 5.74 degrees on the plain VRM; the next two are 1.97 and
1.92 degrees.

The other 36 combinations gave no comparison:

| Reason | Combinations |
|---|---|
| No PhysBone on the model | 25 |
| PhysBones only on a camera gadget, so no chain to compare | 4 |
| Assets for the base or an outfit were not found | 3 |
| The model is not Humanoid, so the motion cannot play | 2 |
| Assembly left no prefab | 2 |

The worst model stays at 47 degrees in both versions. A PhysBone rooted at Hips
covers its whole body, arms, legs and fingers included, and follows the animation
through Is Animated. A VRM chain has no animated rest pose, which is the likely
cause, though it was not isolated by a separate run. Going by bone names, it is the
only model among the 429 with chains starting on body bones.

High Stiffness is where this conversion gains least. On Advanced chains with
Stiffness 0.5 or more, the two most common settings stay near where they were: 10.94
to 11.81 degrees at Stiffness 0.85 (288 chains) and 13.15 to 11.86 at Stiffness 0.7
(192 chains). One model with long chains stays at 30 degrees, because 20 of its
chains (Stiffness 0.7) differ by 75 degrees in both versions. Its cause was not
isolated.

## Exported files

The numbers above convert inside one Unity process. To check that a file gives the
same result, five models were exported through the normal export and read back:

| Model | Inside the process | Exported file |
|---|---|---|
| 1 | 7.99 | 7.99 |
| 2 | 6.43 | 6.47 |
| 3 | 1.13 | 1.13 |
| 4 | 5.77 | 5.77 |
| 5 | 5.31 | 5.44 |

## Frame rate

VRM drag is applied per frame and the mapping is stated at 60 Hz. Stepping the VRM
solver at 30 Hz on the four models, compared on the frames it ran:

| | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|
| 60 Hz, direction error (degrees) | 10.07 | 5.33 | 7.86 | 6.51 |
| 30 Hz, direction error (degrees) | 10.35 | 5.67 | 7.82 | 6.53 |

## What stays different

- Colliders: PhysBone tests the bone as a capsule and the VRM solver tests only the
  tip as a sphere. This is most of the 1.06 degrees at idle. With colliders off, the
  idle error is 0.03 degrees.
- Advanced integration with high Stiffness: the VRM solver has no matching term.
  The timing can differ by several frames on those chains.
- The gravity of Gravity Falloff changes during a swing on the source. The VRM
  solver has one constant gravity.
- Stretch, squish, a changing Is Animated, Grab and Pose.

Models moved by their root were also run. They changed little, since Center on the
model root removes that motion in both versions.
