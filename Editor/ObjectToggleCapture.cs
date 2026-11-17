using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using VRVlog.ObjectToggles;

namespace VRVlog.LilToonExporter
{
    // VRVLOG_object_toggles: the GameObjects that the Expression Menu's toggles
    // switch on and off, read from the prepared copy after the build passes
    // (Modular Avatar menus and Object Toggles are plain menu entries and FX
    // layers by then). The FX is played once per value of each toggle's
    // parameter with every other parameter at its default; the nodes whose
    // activeSelf differs between the values are the group's targets. Targets
    // that are off by default are turned on in the copy so their meshes are
    // written; the extension's default state hides them again in the app.
    // VRVLOG_object_toggle_transforms: in the same samples, the bones the FX's
    // clips move whose local transform differs between the values (a hat that
    // shrinks the hair under it) get the transform of every value.
    internal sealed class ObjectToggleCapture
    {
        internal const string NonIntegralWarning = "値が整数でないメニュー項目は、表示切り替えに含めませんでした: ";
        internal const string ConflictWarning = "複数のパラメーターが切り替えるオブジェクトは、メニューで先の項目だけに含めました: ";
        internal const string LimitWarning = "表示切り替えの上限を超えたため、次のパラメーターを含めませんでした: ";
        internal const string MissingWarning = "書き出しで無くなったオブジェクトを、表示切り替えから除きました: ";

        // Seconds of FX played per sample, in fixed steps, so transitions and
        // short default states settle before activeSelf is read.
        const int Frames = 40;
        const float FrameStep = .05f;
        const int ValueLimit = 1000000;

        // VRVLOG_object_toggles allows these many targets in a group and in all.
        const int MaximumGroupTargets = 1024, MaximumTargets = 4096;

        sealed class Group
        {
            internal string Parameter, Name;
            internal int DefaultValue;
            internal readonly List<GameObject> Targets = new List<GameObject>();
            internal readonly List<string> Paths = new List<string>();
            internal readonly List<(int Value, bool[] Active)> States = new List<(int, bool[])>();
            // The bones the values move, the properties that differ, and the
            // transforms of every value in the order of States.
            internal readonly List<GameObject> Moved = new List<GameObject>();
            internal readonly List<string> MovedPaths = new List<string>();
            internal readonly List<string[]> MovedProperties = new List<string[]>();
            internal readonly List<Local[]> Poses = new List<Local[]>();
        }

        // A Transform's local values as Unity holds them.
        internal struct Local
        {
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;

            internal static Local Of(Transform transform) =>
                new Local { Position = transform.localPosition, Rotation = transform.localRotation, Scale = transform.localScale };

            internal void ApplyTo(Transform transform)
            {
                transform.localPosition = Position;
                transform.localRotation = Rotation;
                transform.localScale = Scale;
            }
        }

        sealed class Item
        {
            internal int Index;
            internal string Name, Parameter;
            internal int Value;
            internal string[] Path;
        }

        readonly List<Group> groups = new List<Group>();
        readonly List<Item> items = new List<Item>();

        // What the last Resolve mapped of the moved bones, or null.
        internal ObjectToggleTransformData Transforms { get; private set; }

        // Returns null when there is nothing to write (no VRChat SDK, no menu,
        // no FX or no toggle that switches an object).
        internal static ObjectToggleCapture Capture(GameObject clone, VrChatMenuImportPolicy policy, ICollection<string> warnings)
        {
            if (clone == null) throw new ArgumentNullException(nameof(clone));
            var menu = VrChatExpressionMenu.Read(clone, policy);
            if (menu.Controller == null) return null;
            var toggles = menu.Entries.Select((entry, index) => (Entry: entry, Index: index))
                .Where(pair => pair.Entry.ControlType == "Toggle" && !string.IsNullOrEmpty(pair.Entry.ControlParameter) &&
                               pair.Entry.Parameters.ContainsKey(pair.Entry.ControlParameter)).ToList();
            if (toggles.Count == 0) return null;
            var transforms = clone.GetComponentsInChildren<Transform>(true);
            // Only nodes that hold a mesh themselves or below can be shown or
            // hidden in the app; the root is never a target.
            var withMesh = new HashSet<Transform>();
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    for (var at = renderer.transform; at != null && at != clone.transform && withMesh.Add(at); at = at.parent) { }
            var candidates = Enumerable.Range(1, transforms.Length - 1).Where(i => withMesh.Contains(transforms[i])).ToArray();
            if (candidates.Length == 0) return null;
            var moving = MovingCandidates(clone, menu.Controller, transforms);
            var movingPaths = new HashSet<string>(moving.Select(i => PathOf(clone, transforms[i])), StringComparer.Ordinal);
            var result = new ObjectToggleCapture();
            // A parameter no object-switching layer reads leaves every object
            // as its default does, so it is not played; an FX without such a
            // layer is never played at all.
            var switching = ParametersThatSwitchObjects(menu.Controller, movingPaths.Contains);
            using var sampler = new Sampler(clone, menu.Controller, moving);
            var samples = new Dictionary<(string, int), (bool[] Active, Local[] Poses)>();
            (bool[] Active, Local[] Poses)? atDefaults = null;
            (bool[] Active, Local[] Poses) Sample(string parameter, int value, int defaultValue)
            {
                // A parameter at its own default leaves every parameter at its
                // default, so all of them share one sample. Each sample costs a
                // new graph played for two seconds, the bulk of this stage.
                if (value == defaultValue && atDefaults != null) return atDefaults.Value;
                if (samples.TryGetValue((parameter, value), out var sample)) return sample;
                var values = new Dictionary<string, float>(menu.Defaults, StringComparer.Ordinal) { [parameter] = value };
                var (all, poses) = sampler.Sample(values);
                sample = samples[(parameter, value)] = (candidates.Select(i => all[i]).ToArray(), poses);
                if (value == defaultValue) atDefaults = sample;
                return sample;
            }
            var claimed = new HashSet<int>();
            var claimedMoves = new HashSet<int>();
            var targetCount = 0;
            var movedCount = 0;
            foreach (var parameterEntries in toggles.GroupBy(pair => pair.Entry.ControlParameter, StringComparer.Ordinal))
            {
                var parameter = parameterEntries.Key;
                if (parameter.Length > 1024)
                {
                    warnings?.Add(ExporterLocalization.T(LimitWarning) + Clip(parameter));
                    continue;
                }
                var rawDefault = menu.Defaults.TryGetValue(parameter, out var known) ? known : sampler.Default(parameter);
                if (!Integral(rawDefault, out var defaultValue))
                {
                    warnings?.Add(ExporterLocalization.T(NonIntegralWarning) + parameter);
                    continue;
                }
                var groupItems = new List<Item>();
                foreach (var (entry, index) in parameterEntries)
                {
                    if (!Integral(entry.Parameters[parameter], out var value))
                    {
                        warnings?.Add(ExporterLocalization.T(NonIntegralWarning) + entry.Name);
                        continue;
                    }
                    if (value == 0) continue;
                    var item = new Item { Index = index, Name = Clip(entry.ExportName), Parameter = parameter, Value = value, Path = PathOf(entry) };
                    // The same submenu read under other parent values repeats its entries.
                    if (!groupItems.Any(other => other.Value == item.Value && other.Name == item.Name && other.Path.SequenceEqual(item.Path)))
                        groupItems.Add(item);
                }
                if (groupItems.Count == 0 || switching != null && !switching.Contains(parameter)) continue;
                var values = new[] { 0, defaultValue }.Concat(groupItems.Select(item => item.Value)).Distinct().OrderBy(value => value).ToList();
                var sampled = values.ToDictionary(value => value, value => Sample(parameter, value, defaultValue));
                var states = sampled.ToDictionary(pair => pair.Key, pair => pair.Value.Active);
                // A node is a target when the values disagree on it; one that
                // an earlier group already switches stays with that group.
                var columns = Enumerable.Range(0, candidates.Length).Where(column => states.Values.Any(state => state[column] != states[0][column])).ToList();
                foreach (var column in columns.Where(claimed.Contains))
                    warnings?.Add(ExporterLocalization.T(ConflictWarning) + PathOf(clone, transforms[candidates[column]]));
                columns.RemoveAll(claimed.Contains);
                // The same for the bones the values move, by property.
                var moves = Enumerable.Range(0, moving.Length)
                    .Select(column => (Column: column, Properties: Differs(transforms[moving[column]], sampled.Values.Select(sample => sample.Poses[column]), sampled[0].Poses[column])))
                    .Where(move => move.Properties.Length > 0).ToList();
                foreach (var move in moves.Where(move => claimedMoves.Contains(move.Column)))
                    warnings?.Add(ExporterLocalization.T(ConflictWarning) + PathOf(clone, transforms[moving[move.Column]]));
                moves.RemoveAll(move => claimedMoves.Contains(move.Column));
                // Turning an item on or off must change something. The bones
                // ride on VRVLOG_object_toggles, so a group must show or hide
                // something as well.
                groupItems.RemoveAll(item => columns.All(column => states[item.Value][column] == states[0][column]) &&
                                             moves.All(move => Differs(transforms[moving[move.Column]], new[] { sampled[item.Value].Poses[move.Column] }, sampled[0].Poses[move.Column]).Length == 0));
                if (groupItems.Count == 0 || columns.Count == 0) continue;
                values.RemoveAll(value => value != 0 && value != defaultValue && groupItems.All(item => item.Value != value));
                if (result.groups.Count >= 256 || columns.Count > MaximumGroupTargets || targetCount + columns.Count > MaximumTargets || values.Count > 256 ||
                    moves.Count > MaximumGroupTargets || movedCount + moves.Count > MaximumTargets || result.items.Count + groupItems.Count > 512)
                {
                    warnings?.Add(ExporterLocalization.T(LimitWarning) + parameter);
                    continue;
                }
                var group = new Group { Parameter = parameter, DefaultValue = defaultValue, Name = NameOf(groupItems, parameter) };
                foreach (var column in columns)
                {
                    group.Targets.Add(transforms[candidates[column]].gameObject);
                    group.Paths.Add(PathOf(clone, transforms[candidates[column]]));
                }
                foreach (var value in values) group.States.Add((value, columns.Select(column => states[value][column]).ToArray()));
                foreach (var move in moves)
                {
                    group.Moved.Add(transforms[moving[move.Column]].gameObject);
                    group.MovedPaths.Add(PathOf(clone, transforms[moving[move.Column]]));
                    group.MovedProperties.Add(move.Properties);
                }
                foreach (var value in values) group.Poses.Add(moves.Select(move => sampled[value].Poses[move.Column]).ToArray());
                claimed.UnionWith(columns);
                claimedMoves.UnionWith(moves.Select(move => move.Column));
                targetCount += columns.Count;
                movedCount += moves.Count;
                result.groups.Add(group);
                result.items.AddRange(groupItems);
            }
            if (result.groups.Count == 0) return null;
            result.items.Sort((left, right) => left.Index.CompareTo(right.Index));
            // Meshes that are off by default are written; the default state
            // in the extension hides them. Only the targets are turned on.
            foreach (var target in result.groups.SelectMany(group => group.Targets))
                if (!target.activeSelf) target.SetActive(true);
            return result;
        }

        // The objects the toggles show and hide.
        internal IEnumerable<GameObject> Targets => groups.SelectMany(group => group.Targets);

        // The bones the toggles move.
        internal IEnumerable<GameObject> MovedTargets => groups.SelectMany(group => group.Moved);

        // The bones of the avatar (indices into its transforms) that a clip of
        // the FX moves: only these are compared between samples, so floating
        // noise of other bones is never read as a toggle. Not the root (no
        // node), not humanoid bones (the app's poses drive them), and not the
        // root's children when the root is scaled (RootScaleBake rewrites
        // their position and scale while exporting).
        internal static int[] MovingCandidates(GameObject clone, RuntimeAnimatorController controller, Transform[] transforms)
        {
            var root = clone.transform;
            var byPath = SelectedExpressionAppearance.TransformsByPath(clone);
            var humanoid = ExpressionTransformCurves.HumanoidTransforms(clone);
            var moved = new HashSet<Transform>();
            foreach (var clip in controller.animationClips.Where(clip => clip != null).Distinct())
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (ExpressionTransformCurves.IsTransformBinding(binding) && byPath.TryGetValue(binding.path, out var matches) && matches.Length == 1)
                        moved.Add(matches[0]);
            moved.RemoveWhere(transform => transform == root || humanoid.Contains(transform) || transform.parent == root && root.localScale != Vector3.one);
            return Enumerable.Range(1, transforms.Length - 1).Where(i => moved.Contains(transforms[i])).ToArray();
        }

        // The properties in which any of the poses differs from first, within
        // the tolerances of the expression transforms.
        static string[] Differs(Transform target, IEnumerable<Local> poses, Local first)
        {
            var reach = ExpressionTransformCurves.PositionTolerance / ExpressionTransformCurves.ParentScale(target);
            bool Far(Vector3 a, Vector3 b, double tolerance) => Math.Abs(a.x - b.x) > tolerance || Math.Abs(a.y - b.y) > tolerance || Math.Abs(a.z - b.z) > tolerance;
            bool translation = false, rotation = false, scale = false;
            foreach (var pose in poses)
            {
                translation |= Far(pose.Position, first.Position, reach);
                rotation |= ExpressionTransformCurves.Angle(pose.Rotation, first.Rotation) > ExpressionTransformCurves.AngleTolerance;
                scale |= Far(pose.Scale, first.Scale, ExpressionTransformCurves.PositionTolerance);
            }
            var result = new List<string>();
            if (translation) result.Add(ObjectToggleTransformData.Translation);
            if (rotation) result.Add(ObjectToggleTransformData.Rotation);
            if (scale) result.Add(ObjectToggleTransformData.Scale);
            return result.ToArray();
        }

        // Whether the groups of these leaders, one target each, stay within
        // the limits of the extension.
        internal bool HasRoom(IReadOnlyCollection<GameObject> leaders) =>
            groups.Sum(group => group.Targets.Count) + leaders.Count <= MaximumTargets &&
            groups.All(group => group.Targets.Count + leaders.Count(group.Targets.Contains) <= MaximumGroupTargets);

        // A later step moved `moved` out from under `leader`, a target: it
        // takes the leader's state in every value, so the toggle still shows
        // and hides it. `path` names it in the user's avatar for warnings.
        internal void Follow(GameObject moved, GameObject leader, string path)
        {
            var group = groups.First(candidate => candidate.Targets.Contains(leader));
            var column = group.Targets.IndexOf(leader);
            group.Targets.Add(moved);
            group.Paths.Add(path);
            for (var i = 0; i < group.States.Count; i++)
            {
                var (value, active) = group.States[i];
                group.States[i] = (value, active.Append(active[column]).ToArray());
            }
        }

        // Maps the targets to output nodes. A target the build passes or the
        // export removed is left out with a warning; an item that no longer
        // changes any kept target is left out, and so is a group left without
        // targets or items.
        // The moved bones are mapped the same way into Transforms; a bone left
        // out only drops its transforms.
        internal ObjectToggleData Resolve(Func<GameObject, int> node, ICollection<string> warnings)
        {
            var data = new ObjectToggleData();
            var moved = new ObjectToggleTransformData();
            var kept = new List<Item>();
            foreach (var group in groups)
            {
                var output = new ObjectToggleData.Group { Parameter = group.Parameter, Name = group.Name, DefaultValue = group.DefaultValue };
                var columns = new List<int>();
                for (var i = 0; i < group.Targets.Count; i++)
                {
                    var index = group.Targets[i] != null ? node(group.Targets[i]) : -1;
                    if (index < 0) { warnings?.Add(ExporterLocalization.T(MissingWarning) + group.Paths[i]); continue; }
                    output.Targets.Add(index);
                    columns.Add(i);
                }
                if (output.Targets.Count == 0) continue;
                var transforms = new ObjectToggleTransformData.Group { Parameter = group.Parameter };
                var movedColumns = new List<int>();
                for (var i = 0; i < group.Moved.Count; i++)
                {
                    var index = group.Moved[i] != null ? node(group.Moved[i]) : -1;
                    if (index < 0) { warnings?.Add(ExporterLocalization.T(MissingWarning) + group.MovedPaths[i]); continue; }
                    var target = new ObjectToggleTransformData.Target { Node = index };
                    target.Properties.AddRange(group.MovedProperties[i]);
                    transforms.Targets.Add(target);
                    movedColumns.Add(i);
                }
                bool[] ActiveAt(int value) => group.States.First(state => state.Value == value).Active;
                Local[] PosesAt(int value) => group.Poses[group.States.FindIndex(state => state.Value == value)];
                bool Moves(int value) => movedColumns.Any(column =>
                    Differs(group.Moved[column].transform, new[] { PosesAt(value)[column] }, PosesAt(0)[column]).Intersect(group.MovedProperties[column]).Any());
                // Turning an item on or off must still change a kept target.
                var groupItems = items.Where(item => item.Parameter == group.Parameter &&
                                                     (columns.Any(column => ActiveAt(item.Value)[column] != ActiveAt(0)[column]) || Moves(item.Value))).ToList();
                if (groupItems.Count == 0) continue;
                for (var s = 0; s < group.States.Count; s++)
                {
                    var (value, active) = group.States[s];
                    if (value != 0 && value != group.DefaultValue && groupItems.All(item => item.Value != value)) continue;
                    var state = new ObjectToggleData.State { Value = value };
                    state.Active.AddRange(columns.Select(column => active[column]));
                    output.States.Add(state);
                    var pose = new ObjectToggleTransformData.State { Value = value };
                    foreach (var column in movedColumns) pose.Transforms.Add(Written(group.Poses[s][column]));
                    transforms.States.Add(pose);
                }
                data.Groups.Add(output);
                if (transforms.Targets.Count > 0) moved.Groups.Add(transforms);
                kept.AddRange(groupItems);
            }
            foreach (var item in items.Where(kept.Contains))
            {
                var output = new ObjectToggleData.Item { Id = "m" + item.Index, Name = item.Name, Parameter = item.Parameter, Value = item.Value };
                output.Path.AddRange(item.Path);
                data.Items.Add(output);
            }
            Transforms = data.Groups.Count == 0 || moved.Groups.Count == 0 ? null : moved;
            return data.Groups.Count == 0 ? null : data;
        }

        // Every property is written; ObjectToggleTransformData.Write keeps the
        // target's. The rotation is normalized in double.
        static ObjectToggleTransformData.Transform Written(Local local)
        {
            var r = local.Rotation;
            var length = Math.Sqrt((double)r.x * r.x + (double)r.y * r.y + (double)r.z * r.z + (double)r.w * r.w);
            if (length < 1e-6) length = 1;
            return new ObjectToggleTransformData.Transform
            {
                Translation = new double[] { local.Position.x, local.Position.y, local.Position.z },
                Rotation = new[] { r.x / length, r.y / length, r.z / length, r.w / length },
                Scale = new double[] { local.Scale.x, local.Scale.y, local.Scale.z }
            };
        }

        // Adds the extension to the root of the finished GLB. Nothing to write
        // keeps the bytes unchanged.
        // The moved bones go to their own extension, which readers of
        // VRVLOG_object_toggles 1 do not know and leave alone.
        internal static byte[] Inject(byte[] bytes, ObjectToggleData data, ObjectToggleTransformData transforms = null)
        {
            if (data == null) return bytes;
            var extension = ObjectToggleData.Write(data);
            var document = GlbDocument.Read(bytes);
            var nodeCount = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> nodes ? nodes.Count : 0;
            if (data.Groups.Any(group => group.Targets.Any(target => target >= nodeCount)))
                throw new InvalidOperationException("Object toggle target node is out of range.");
            // Bones that cannot be written only lose their transforms; the
            // toggles stay.
            Dictionary<string, object> moved = null;
            try
            {
                if (transforms != null && transforms.Groups.Any(group => group.Targets.Any(target => target.Node >= nodeCount)))
                    throw new InvalidOperationException("Object toggle transform target node is out of range.");
                if (transforms != null) moved = ObjectToggleTransformData.Write(transforms, data);
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                Debug.LogWarning("VR Vlog: left the bones the toggles move out of the VRM: " + error.Message);
            }
            document.PlaceExtension(ObjectToggleData.Extension, extension);
            if (moved != null) document.PlaceExtension(ObjectToggleTransformData.Extension, moved);
            return document.Write();
        }

        // The parameters whose value can change an object's activeSelf: those
        // read by a layer whose clips set m_IsActive, drive animator parameters
        // another layer may read, or whose behaviours run, and while any layer
        // sets m_IsActive, those read by a layer that mixes write defaults (a
        // write-defaults state there resets what the layers below set; the
        // others leave it). Any other layer adds the same to every object in
        // every state. Null stands for every parameter, for controllers this
        // does not read (override controllers and synced layers). A clip that
        // moves a bone whose path `moves` accepts counts as setting m_IsActive.
        internal static HashSet<string> ParametersThatSwitchObjects(RuntimeAnimatorController runtime, Func<string, bool> moves = null)
        {
            var clips = new Dictionary<AnimationClip, (bool SetsActive, bool DrivesParameters)>();
            bool Switches(EditorCurveBinding binding) => IsActiveBinding(binding) ||
                moves != null && ExpressionTransformCurves.IsTransformBinding(binding) && moves(binding.path);
            if (!(runtime is AnimatorController controller) || controller.layers.Any(layer => layer.syncedLayerIndex >= 0))
                return runtime.animationClips.Any(clip => clip != null && AnimationUtility.GetCurveBindings(clip).Any(Switches)) ? null : new HashSet<string>(StringComparer.Ordinal);
            AnimatorNesting.Check(controller);
            var parameterNames = new HashSet<string>(controller.parameters.Select(parameter => parameter.name), StringComparer.Ordinal);
            (bool SetsActive, bool DrivesParameters) Read(AnimationClip clip)
            {
                if (clips.TryGetValue(clip, out var known)) return known;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                return clips[clip] = (bindings.Any(Switches),
                    bindings.Any(binding => binding.type == typeof(Animator) && parameterNames.Contains(binding.propertyName)));
            }
            var layers = controller.layers.Select(layer =>
            {
                var reads = new HashSet<string>(StringComparer.Ordinal);
                var writeDefaults = new HashSet<bool>();
                bool setsActive = false, drives = false;
                var seen = new HashSet<UnityEngine.Object>();
                void Conditions(IEnumerable<AnimatorTransitionBase> transitions)
                {
                    foreach (var transition in transitions)
                        if (transition != null)
                            foreach (var condition in transition.conditions) reads.Add(condition.parameter);
                }
                void Motion(Motion motion)
                {
                    if (motion == null || !seen.Add(motion)) return;
                    if (motion is AnimationClip clip)
                    {
                        var (active, driving) = Read(clip);
                        setsActive |= active;
                        drives |= driving;
                    }
                    else if (motion is BlendTree tree)
                    {
                        reads.Add(tree.blendParameter);
                        reads.Add(tree.blendParameterY);
                        foreach (var child in tree.children)
                        {
                            reads.Add(child.directBlendParameter);
                            Motion(child.motion);
                        }
                    }
                }
                void Machine(AnimatorStateMachine machine)
                {
                    if (machine == null || !seen.Add(machine)) return;
                    drives |= ExpressionDependencies.HasEffectfulBehaviours(machine.behaviours);
                    Conditions(machine.anyStateTransitions);
                    Conditions(machine.entryTransitions);
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        if (state == null) continue;
                        writeDefaults.Add(state.writeDefaultValues);
                        drives |= ExpressionDependencies.HasEffectfulBehaviours(state.behaviours);
                        Conditions(state.transitions);
                        if (state.speedParameterActive) reads.Add(state.speedParameter);
                        if (state.cycleOffsetParameterActive) reads.Add(state.cycleOffsetParameter);
                        if (state.mirrorParameterActive) reads.Add(state.mirrorParameter);
                        if (state.timeParameterActive) reads.Add(state.timeParameter);
                        Motion(state.motion);
                    }
                    foreach (var child in machine.stateMachines)
                    {
                        Conditions(machine.GetStateMachineTransitions(child.stateMachine));
                        Machine(child.stateMachine);
                    }
                }
                Machine(layer.stateMachine);
                return (Reads: reads, SetsActive: setsActive, Drives: drives, MixedWriteDefaults: writeDefaults.Count > 1);
            }).ToList();
            var anySetsActive = layers.Any(layer => layer.SetsActive);
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layer in layers)
                if (layer.SetsActive || layer.Drives || anySetsActive && layer.MixedWriteDefaults)
                    result.UnionWith(layer.Reads);
            return result;
        }

        static bool IsActiveBinding(EditorCurveBinding binding) => binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive";

        static bool Integral(float raw, out int value)
        {
            value = 0;
            if (float.IsNaN(raw) || float.IsInfinity(raw) || raw != Mathf.Round(raw) || Math.Abs(raw) > ValueLimit) return false;
            value = (int)raw;
            return true;
        }

        static string Clip(string text) => text.Length > 1024 ? text.Substring(0, 1024) : text;

        // The entry's name is its submenus and label joined by " / ".
        static string[] PathOf(VrChatExpressionMenu.Entry entry)
        {
            var suffix = " / " + entry.ExportName;
            if (!entry.Name.EndsWith(suffix, StringComparison.Ordinal)) return Array.Empty<string>();
            var parts = entry.Name.Substring(0, entry.Name.Length - suffix.Length)
                .Split(new[] { " / " }, StringSplitOptions.None).Where(part => part.Length > 0).Select(Clip).ToArray();
            return parts.Skip(Math.Max(0, parts.Length - 16)).ToArray();
        }

        // The last submenu the items share, else the parameter.
        static string NameOf(List<Item> groupItems, string parameter)
        {
            var common = groupItems[0].Path.AsEnumerable();
            foreach (var item in groupItems.Skip(1))
                common = common.Zip(item.Path, (left, right) => (left, right)).TakeWhile(pair => pair.left == pair.right).Select(pair => pair.left).ToArray();
            return Clip(common.LastOrDefault() ?? parameter);
        }

        static string PathOf(GameObject root, Transform transform) =>
            UnityEditor.AnimationUtility.CalculateTransformPath(transform, root.transform);

        // Plays the FX on one preview copy of the avatar, made on first use.
        // Every sample starts from the copy's own activeSelf with a new graph,
        // so write-defaults-off states of an earlier sample do not leak into
        // the next.
        sealed class Sampler : IDisposable
        {
            readonly GameObject avatar;
            readonly RuntimeAnimatorController controller;
            readonly Dictionary<string, AnimatorControllerParameter> parameters = new Dictionary<string, AnimatorControllerParameter>(StringComparer.Ordinal);
            readonly int[] moving;
            AnimatorProbe probe;
            Transform[] transforms;
            bool[] rest;
            Local[] restPoses;

            // moving: indices of the bones whose transforms are read.
            internal Sampler(GameObject avatar, RuntimeAnimatorController controller, int[] moving)
            {
                this.avatar = avatar;
                this.controller = controller;
                this.moving = moving;
            }

            void Open()
            {
                if (probe != null) return;
                try
                {
                    probe = new AnimatorProbe(avatar);
                    // Instantiate keeps the hierarchy order, so index i is the
                    // same object in the avatar and in this copy.
                    transforms = probe.Copy.GetComponentsInChildren<Transform>(true);
                    rest = transforms.Select(transform => transform.gameObject.activeSelf).ToArray();
                    restPoses = moving.Select(i => Local.Of(transforms[i])).ToArray();
                    var graph = PlayableGraph.Create("VR Vlog object toggle parameters");
                    try
                    {
                        var playable = AnimatorControllerPlayable.Create(graph, controller);
                        for (var i = 0; i < playable.GetParameterCount(); i++)
                        {
                            var parameter = playable.GetParameter(i);
                            parameters[parameter.name] = parameter;
                        }
                    }
                    finally { graph.Destroy(); }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            // The controller's own default for a parameter the avatar does not list.
            internal float Default(string name)
            {
                Open();
                if (!parameters.TryGetValue(name, out var parameter)) return 0f;
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool: return parameter.defaultBool ? 1f : 0f;
                    case AnimatorControllerParameterType.Int: return parameter.defaultInt;
                    case AnimatorControllerParameterType.Float: return parameter.defaultFloat;
                    default: return 0f;
                }
            }

            // activeSelf of every object of the avatar, in hierarchy order, and
            // the local transforms of the moving bones. The transforms stay
            // in the copy after a graph is gone, so they are reset too.
            internal (bool[] Active, Local[] Poses) Sample(Dictionary<string, float> values)
            {
                Open();
                for (var i = 0; i < transforms.Length; i++) transforms[i].gameObject.SetActive(rest[i]);
                for (var i = 0; i < moving.Length; i++) restPoses[i].ApplyTo(transforms[moving[i]]);
                var graph = probe.Play("VR Vlog object toggle sampling", controller, "Object toggles", out var playable);
                try
                {
                    foreach (var pair in values)
                        if (parameters.TryGetValue(pair.Key, out var parameter)) AnimatorProbe.Write(playable, parameter.type, pair.Key, pair.Value);
                    graph.Play();
                    graph.Evaluate(0f);
                    for (var frame = 0; frame < Frames; frame++) graph.Evaluate(FrameStep);
                    return (transforms.Select(transform => transform.gameObject.activeSelf).ToArray(), moving.Select(i => Local.Of(transforms[i])).ToArray());
                }
                finally { graph.Destroy(); }
            }

            public void Dispose()
            {
                probe?.Dispose();
                probe = null;
            }
        }
    }
}
