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

300 models that could be assembled in one Unity project, 8558 chains. The same
timeline and the same four bodies as above. This run was made before the last
review fixes. Those fixes only touch a force at its ceiling, a bone pointing
straight up and a last bone scaled to zero. On the four models above they moved
the means by less than 0.01 degrees.

| | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|
| Direction error, mean (degrees) | 11.01 | 4.87 | 9.11 | 6.47 |
| Direction error, median model | 9.92 | 5.61 | 7.78 | 6.16 |
| Direction error, 90th percentile model | 13.10 | 7.89 | 14.44 | 9.91 |
| Direction error, worst model | 47.70 | 47.52 | 51.00 | 47.65 |
| Tip distance RMS (% of chain length) | 11.41 | 6.15 | 9.36 | 7.89 |
| Last bone rotation error (degrees) | 24.17 | 9.59 | 21.63 | 12.46 |
| Swing size against the source | 3.49 | 1.07 | 3.66 | 1.16 |
| Settling time difference (s) | 0.21 | 0.09 | 0.23 | 0.13 |

| Section | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|
| idle | 2.09 | 1.42 | 2.10 | 1.44 |
| walk | 15.52 | 5.49 | 11.63 | 7.54 |
| turn | 16.28 | 6.17 | 13.69 | 9.12 |
| head | 12.61 | 6.61 | 11.19 | 8.56 |
| jump | 19.05 | 8.49 | 14.33 | 10.67 |
| rest | 4.36 | 2.44 | 4.35 | 3.29 |

| Part | chains | old_raw | new_raw | old_app | new_app |
|---|---|---|---|---|---|
| hair | 3388 | 12.60 | 4.69 | 9.11 | 6.71 |
| skirt | 1474 | 5.52 | 5.22 | 5.93 | 5.75 |
| tail | 434 | 11.23 | 2.92 | 5.97 | 6.59 |
| ear | 652 | 8.51 | 2.70 | 5.36 | 4.34 |
| breast | 148 | 0.55 | 0.48 | 0.56 | 2.01 |
| sleeve | 182 | 17.86 | 4.99 | 16.83 | 6.07 |
| other | 2280 | 11.35 | 4.41 | 9.71 | 5.70 |

Against the previous conversion, the plain VRM improved on 299 models, stayed the
same on 1 and got worse on none. With the app's load settings, 261 models improved,
3 stayed the same and 36 got worse, by 1.27 degrees at most.

22 more models gave no comparison. 18 have no PhysBone, and on 4 the only PhysBones
are on a camera gadget with no chain to compare.

The worst model stays at 47 degrees in both versions. A PhysBone rooted at Hips
covers its whole body, arms, legs and fingers included, and follows the animation
through Is Animated. A VRM chain has no animated rest pose, which is the likely
cause, though it was not isolated by a separate run. Going by bone names, it is the
only model among the 300 with chains starting on body bones.

Models that could not be measured here: those whose assets are not in this project
or need another Unity project.

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
