using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.AvatarAnimator;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    // VRVLOG_avatar_animator: the prepared copy's FX controller, Expression
    // Menu and Expression Parameters, written as they are for the app to play.
    // Only reads: the copy, its assets and its Animator are left as they are.
    // Curves are kept for activeSelf only; the others are counted by kind.
    internal sealed class AvatarAnimatorCapture
    {
        // Why the stage was left out, listed after the stage's name.
        internal const string TooLargeReason = "データが上限の{0}MBを超えています。";
        internal const string TooDeepReason = "BlendTreeの入れ子が上限の{0}階層を超えています。";
        internal const string TooManyMotionsReason = "BlendTreeに含まれるモーションが上限の{0}個を超えています。";

        // The largest extension JSON (UTF-8 bytes) written. Tests lower it.
        internal const int MaximumMegabytes = 4;
        internal static int MaximumBytes = MaximumMegabytes * 1024 * 1024;

        internal static string TooLarge => ExporterLocalization.Format(TooLargeReason, MaximumMegabytes);
        internal static string TooDeep => ExporterLocalization.Format(TooDeepReason, AvatarAnimatorData.MaximumBlendTreeDepth);
        internal static string TooManyMotions => ExporterLocalization.Format(TooManyMotionsReason, AvatarAnimatorData.MaximumExpandedMotions);

        readonly AvatarAnimatorData data = new AvatarAnimatorData();
        // Curve targets stay GameObjects until the output nodes are known.
        readonly List<(GameObject Object, int Default)> targets = new List<(GameObject, int)>();
        readonly Dictionary<GameObject, int> targetIndex = new Dictionary<GameObject, int>();
        readonly List<List<(int Target, ExpressionAnimationData.Curve Curve)>> clipTracks = new List<List<(int, ExpressionAnimationData.Curve)>>();
        // BlendShape curves stay a renderer and a shape name until the output
        // mesh is known; their tracks follow each clip's activeSelf tracks.
        readonly List<(SkinnedMeshRenderer Renderer, string Shape)> morphs = new List<(SkinnedMeshRenderer, string)>();
        readonly Dictionary<(SkinnedMeshRenderer, string), int> morphIndex = new Dictionary<(SkinnedMeshRenderer, string), int>();
        readonly List<List<(int Morph, ExpressionAnimationData.Curve Curve)>> clipMorphTracks = new List<List<(int, ExpressionAnimationData.Curve)>>();
        // The morph targets the last Resolve wrote, each with the
        // {node, index} that UnusedMorphTargets keeps and renumbers.
        readonly List<(AvatarAnimatorData.Target Target, Dictionary<string, object> Bind, bool Soft)> morphBinds = new List<(AvatarAnimatorData.Target, Dictionary<string, object>, bool)>();
        readonly Dictionary<string, int> omitted = new Dictionary<string, int>(StringComparer.Ordinal);

        readonly GameObject root;
        readonly IReadOnlyDictionary<GameObject, bool> rest;
        readonly Dictionary<string, int> parameterIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly HashSet<string> controllerParameters = new HashSet<string>(StringComparer.Ordinal);
        // The names Expression Parameters or the FX controller declare.
        readonly HashSet<string> declared = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<AnimationClip, AnimationClip> overrides;
        readonly Dictionary<Motion, int> motionIndex = new Dictionary<Motion, int>();
        // Blend trees left out because Unity cannot read a parameter they
        // blend by: true when one has another type, false when none is declared.
        readonly Dictionary<Motion, bool> unreadTrees = new Dictionary<Motion, bool>();
        // The uses of a parameter Unity cannot read inside each of
        // data.Motions, counted again wherever the motion is used.
        readonly List<int> unread = new List<int>();
        // Blend trees nested in each of data.Motions, itself included.
        readonly List<int> nesting = new List<int>();
        // The motions each of data.Motions expands to, itself included; it
        // stops counting past the limit.
        readonly List<int> expanded = new List<int>();
        readonly Dictionary<AnimationClip, int> clipIndex = new Dictionary<AnimationClip, int>();
        // Clips an additive layer plays, and whether the layer read now is one.
        readonly HashSet<int> additiveClips = new HashSet<int>();
        bool readingAdditive;

        AvatarAnimatorCapture(GameObject root, IReadOnlyDictionary<GameObject, bool> rest, Dictionary<AnimationClip, AnimationClip> overrides)
        {
            this.root = root; this.rest = rest; this.overrides = overrides;
        }

        // activeSelf of every object of the copy before the export's own
        // steps change it (menu toggles turn their default-off meshes on).
        internal static Dictionary<GameObject, bool> Snapshot(GameObject clone) =>
            clone.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.gameObject, t => t.gameObject.activeSelf);

        // BlendShape weights of every renderer of the copy at the same point,
        // by renderer instance ID and shape name; a repeated name takes the
        // first shape, as Unity's own lookup does.
        internal static Dictionary<(int Renderer, string Shape), float> MorphSnapshot(GameObject clone)
        {
            var weights = new Dictionary<(int, string), float>();
            foreach (var renderer in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                for (var index = 0; index < mesh.blendShapeCount; index++)
                {
                    var key = (renderer.GetInstanceID(), mesh.GetBlendShapeName(index));
                    if (!weights.ContainsKey(key)) weights[key] = renderer.GetBlendShapeWeight(index);
                }
            }
            return weights;
        }

        // The shapes the FX moves on renderers of the copy, ordered by path
        // and name. Read before optimizing, so the optimizer keeps a route
        // to where each one ends up.
        internal static List<(SkinnedMeshRenderer Renderer, string Shape)> DrivenMorphs(GameObject clone)
        {
            var fx = Fx(VrChatExpressionMenu.Descriptor(clone));
            var found = new SortedSet<(string Path, string Shape)>(Comparer<(string, string)>.Create((a, b) =>
            {
                var path = string.CompareOrdinal(a.Item1, b.Item1);
                return path != 0 ? path : string.CompareOrdinal(a.Item2, b.Item2);
            }));
            if (fx == null) return new List<(SkinnedMeshRenderer, string)>();
            foreach (var clip in fx.animationClips.Where(clip => clip != null).Distinct())
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith(ShapePrefix, StringComparison.Ordinal))
                        found.Add((binding.path, binding.propertyName.Substring(ShapePrefix.Length)));
            var result = new List<(SkinnedMeshRenderer, string)>();
            foreach (var (path, shape) in found)
            {
                var target = path.Length == 0 ? clone.transform : clone.transform.Find(path);
                var renderer = target == null ? null : target.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.GetBlendShapeIndex(shape) >= 0) result.Add((renderer, shape));
            }
            return result;
        }

        const string ShapePrefix = "blendShape.";

        // The same FX layer VrChatExpressionMenu.Read takes.
        static RuntimeAnimatorController Fx(object descriptor)
        {
            RuntimeAnimatorController fx = null;
            if (descriptor != null && Member(descriptor, "customizeAnimationLayers") is bool custom && custom)
                foreach (var layer in Items(Member(descriptor, "baseAnimationLayers")))
                {
                    if (Member(layer, "isDefault") is bool isDefault && isDefault) continue;
                    if (Member(layer, "type")?.ToString() == "FX") fx = Member(layer, "animatorController") as RuntimeAnimatorController;
                }
            return fx;
        }

        // Null without a VRChat avatar descriptor or a custom FX controller:
        // the file is then written without the extension.
        internal static AvatarAnimatorCapture Capture(GameObject clone, IReadOnlyDictionary<GameObject, bool> rest)
        {
            var descriptor = VrChatExpressionMenu.Descriptor(clone);
            if (descriptor == null) return null;
            var fx = Fx(descriptor);
            if (fx == null) return null;
            var controller = ExpressionDependencies.Controller(fx);
            var capture = new AvatarAnimatorCapture(clone, rest ?? new Dictionary<GameObject, bool>(), ExpressionDependencies.Overrides(fx));
            var expressions = Member(descriptor, "customExpressions") is bool enabled && enabled;
            capture.ReadParameters(expressions ? Member(descriptor, "expressionParameters") : null, controller);
            capture.data.Controllers.Add(capture.ReadController(controller));
            // Menus name only parameters that Expression Parameters declare.
            if (expressions) capture.ReadMenus(Member(descriptor, "expressionsMenu"));
            return capture;
        }

        // A shape as the export bakes it: the weight in the scene, the weight
        // the base geometry holds and the weight the target shows at 1.
        internal readonly struct MorphSource : IEquatable<MorphSource>
        {
            internal readonly double Rest, Baked, End;
            internal MorphSource(double rest, double baked, double end) { Rest = rest; Baked = baked; End = end; }
            public bool Equals(MorphSource other) => Rest == other.Rest && Baked == other.Baked && End == other.End;
            public override bool Equals(object obj) => obj is MorphSource other && Equals(other);
            public override int GetHashCode() => (Rest, Baked, End).GetHashCode();
        }

        // The {node, index} of each morph target written, for
        // UnusedMorphTargets.Strip to keep and renumber.
        internal IReadOnlyList<Dictionary<string, object>> MorphBinds => morphBinds.Where(bind => !bind.Soft).Select(bind => bind.Bind).ToList();

        // The {node, index} of each target whose curves only hold a weight the
        // base already has, for Strip to renumber without keeping: such a
        // target changes nothing unless something else keeps its shape.
        internal IReadOnlyList<Dictionary<string, object>> SoftMorphBinds => morphBinds.Where(bind => bind.Soft).Select(bind => bind.Bind).ToList();

        // Takes the indices Strip gave the binds back into the targets. A soft
        // target whose shape Strip left out goes, with its tracks.
        internal void RenumberMorphs()
        {
            foreach (var (target, bind, _) in morphBinds) target.Morph = (int)Convert.ToInt64(bind["index"]);
            var gone = new HashSet<AvatarAnimatorData.Target>(morphBinds.Where(bind => bind.Soft && bind.Target.Morph < 0).Select(bind => bind.Target));
            if (gone.Count == 0) return;
            morphBinds.RemoveAll(bind => gone.Contains(bind.Target));
            var map = new int[data.Targets.Count];
            var kept = new List<AvatarAnimatorData.Target>();
            for (var i = 0; i < data.Targets.Count; i++)
            {
                map[i] = gone.Contains(data.Targets[i]) ? -1 : kept.Count;
                if (map[i] >= 0) kept.Add(data.Targets[i]);
            }
            data.Targets.Clear();
            data.Targets.AddRange(kept);
            foreach (var clip in data.Clips)
            {
                clip.Tracks.RemoveAll(track => map[track.Target] < 0);
                foreach (var track in clip.Tracks) track.Target = map[track.Target];
            }
        }

        // The data with glTF node indices. A target the file has no node for
        // is left out with its curves, which are counted as missingNode.
        // sources gives the shapes an output shape was made from; without one,
        // or when they disagree, its curves are counted as blendShape. When the
        // shapes would take the extension past MaximumBytes, all of them are.
        internal AvatarAnimatorData Resolve(Func<GameObject, int> node, Func<SkinnedMeshRenderer, string, IReadOnlyList<MorphSource>> sources = null)
        {
            Build(node, sources, true);
            if (morphBinds.Count > 0 && Encoding.UTF8.GetByteCount(JsonDom.Serialize(AvatarAnimatorData.Write(data))) > MaximumBytes)
                Build(node, sources, false);
            return data;
        }

        const int MissingMorph = -1, OmittedMorph = -2;

        void Build(Func<GameObject, int> node, Func<SkinnedMeshRenderer, string, IReadOnlyList<MorphSource>> sources, bool writeMorphs)
        {
            data.Targets.Clear();
            morphBinds.Clear();
            var map = new int[targets.Count];
            for (var i = 0; i < targets.Count; i++)
            {
                var index = targets[i].Object == null ? -1 : node(targets[i].Object);
                map[i] = index < 0 ? -1 : data.Targets.Count;
                if (index >= 0) data.Targets.Add(new AvatarAnimatorData.Target { Node = index, Property = "active", Rest = targets[i].Default });
            }
            var morphMap = new int[morphs.Count];
            for (var i = 0; i < morphs.Count; i++) morphMap[i] = MorphTarget(i, node, sources, writeMorphs);
            var missing = 0;
            var lost = 0;
            for (var i = 0; i < data.Clips.Count; i++)
            {
                data.Clips[i].Tracks.Clear();
                foreach (var (target, curve) in clipTracks[i])
                {
                    if (map[target] < 0) { missing++; continue; }
                    var track = new AvatarAnimatorData.Track { Target = map[target] };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
                foreach (var (morph, curve) in clipMorphTracks[i])
                {
                    var target = morphMap[morph];
                    if (target == MissingMorph) { missing++; continue; }
                    if (target == OmittedMorph) { lost++; continue; }
                    var track = new AvatarAnimatorData.Track { Target = target };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
            }
            data.Omitted.Clear();
            foreach (var kind in AvatarAnimatorData.OmittedKinds)
            {
                var count = (omitted.TryGetValue(kind, out var value) ? value : 0) + (kind == "missingNode" ? missing : 0) + (kind == "blendShape" ? lost : 0);
                if (count > 0) data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = kind, Count = count });
            }
        }

        // The target index of a moved shape, or why its tracks are not written.
        int MorphTarget(int i, Func<GameObject, int> node, Func<SkinnedMeshRenderer, string, IReadOnlyList<MorphSource>> sources, bool writeMorphs)
        {
            var (renderer, shape) = morphs[i];
            var nodeIndex = renderer == null ? -1 : node(renderer.gameObject);
            if (nodeIndex < 0) return MissingMorph;
            var morph = renderer.sharedMesh != null ? renderer.sharedMesh.GetBlendShapeIndex(shape) : -1;
            var found = morph < 0 || sources == null ? null : sources(renderer, shape);
            if (found == null || found.Count == 0 || found.Distinct().Count() > 1) return OmittedMorph;
            var source = found[0];
            // An additive layer adds the curve less its reference pose, which
            // the extension does not carry.
            if (additiveClips.Any(clip => clipMorphTracks[clip].Any(track => track.Morph == i))) return OmittedMorph;
            // A curve that holds the scene weight is written too: that the FX
            // writes the shape is what the reader weighs against the faces.
            // Past the size limit every shape is counted.
            if (!writeMorphs) return OmittedMorph;
            // A target that shows no change cannot carry the weight (B3).
            if (source.Baked == source.End) return OmittedMorph;
            var target = new AvatarAnimatorData.Target
            {
                Node = nodeIndex, Property = "morph", Morph = morph, Rest = source.Rest, Baked = source.Baked, End = source.End,
            };
            // Curves that only hold rest on a base that already shows it change
            // nothing by themselves: the target stays only if its shape does.
            var soft = source.Baked == source.Rest &&
                       clipMorphTracks.All(tracks => tracks.All(track => track.Morph != i || Holds(track.Curve, source.Rest)));
            morphBinds.Add((target, new Dictionary<string, object> { ["node"] = (long)nodeIndex, ["index"] = (long)morph }, soft));
            data.Targets.Add(target);
            return data.Targets.Count - 1;
        }

        static bool Holds(ExpressionAnimationData.Curve curve, double value) =>
            curve.Keys.All(key => key.Value == value && (key.InTangent ?? 0) == 0 && (key.OutTangent ?? 0) == 0);

        // An extension over MaximumBytes is an error: the caller leaves the
        // stage out and lists it with this reason.
        internal static byte[] Inject(byte[] bytes, AvatarAnimatorData data)
        {
            if (data == null) return bytes;
            var extension = AvatarAnimatorData.Write(data);
            if (Encoding.UTF8.GetByteCount(JsonDom.Serialize(extension)) > MaximumBytes)
                throw new InvalidOperationException(TooLarge);
            var document = GlbDocument.Read(bytes);
            var nodes = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> list ? list : new List<object>();
            var meshes = document.Json.TryGetValue("meshes", out var rawMeshes) && rawMeshes is List<object> meshList ? meshList : new List<object>();
            if (data.Targets.Any(target => target.Node >= nodes.Count))
                throw new InvalidOperationException("Avatar animator target node is out of range.");
            // A morph target names one of its node's mesh's targets.
            foreach (var target in data.Targets.Where(target => target.Property == "morph"))
                if (!(nodes[target.Node] is Dictionary<string, object> node && node.TryGetValue("mesh", out var rawMesh) &&
                      Convert.ToInt32(rawMesh) is var mesh && mesh >= 0 && mesh < meshes.Count &&
                      target.Morph < MorphCount(meshes[mesh] as Dictionary<string, object>)))
                    throw new InvalidOperationException("Avatar animator morph target is out of range.");
            document.PlaceExtension(AvatarAnimatorData.Extension, extension);
            return document.Write();
        }

        // The targets of a mesh's first primitive; glTF requires every
        // primitive to have the same count.
        static int MorphCount(Dictionary<string, object> mesh) =>
            mesh != null && mesh.TryGetValue("primitives", out var raw) && raw is List<object> primitives && primitives.Count > 0 &&
            primitives[0] is Dictionary<string, object> primitive && primitive.TryGetValue("targets", out var rawTargets) && rawTargets is List<object> targets
                ? targets.Count : 0;

        // Expression Parameters first, then the FX parameters they do not name.
        // A name in both takes the FX type; the first of a repeated name counts.
        void ReadParameters(object expressionParameters, AnimatorController controller)
        {
            foreach (var parameter in Items(Member(expressionParameters, "parameters")))
            {
                var name = Member(parameter, "name") as string;
                var type = ExpressionType(Member(parameter, "valueType")?.ToString());
                if (string.IsNullOrEmpty(name) || type == null || parameterIndex.ContainsKey(name)) continue;
                declared.Add(name);
                parameterIndex[name] = data.Parameters.Count;
                data.Parameters.Add(new AvatarAnimatorData.Parameter
                {
                    Name = name, Type = type, Origin = "expression", ExpressionType = type, Default = Number(Member(parameter, "defaultValue")),
                    Saved = Member(parameter, "saved") is bool saved && saved,
                    // SDKs before the networkSynced field synced every parameter.
                    Synced = !(Member(parameter, "networkSynced") is bool synced) || synced
                });
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in controller.parameters)
            {
                if (parameter == null || !seen.Add(parameter.name)) continue;
                controllerParameters.Add(parameter.name);
                declared.Add(parameter.name);
                var type = parameter.type == AnimatorControllerParameterType.Bool ? "bool" : parameter.type == AnimatorControllerParameterType.Int ? "int"
                    : parameter.type == AnimatorControllerParameterType.Trigger ? "trigger" : "float";
                if (parameterIndex.TryGetValue(parameter.name, out var index)) { data.Parameters[index].Type = type; continue; }
                parameterIndex[parameter.name] = data.Parameters.Count;
                data.Parameters.Add(new AvatarAnimatorData.Parameter
                {
                    Name = parameter.name, Type = type, Origin = VrChatParameterDriver.BuiltIn.Contains(parameter.name) ? "builtin" : "animator",
                    Default = type == "float" ? parameter.defaultFloat : type == "int" ? parameter.defaultInt : parameter.defaultBool ? 1 : 0
                });
            }
        }

        static string ExpressionType(string valueType) => valueType == "Bool" ? "bool" : valueType == "Int" ? "int" : valueType == "Float" ? "float" : null;

        // The FX controller's parameter of that name when it is of one of the
        // types given, or -1. Unity reads only what its controller declares,
        // and each use takes only its own types: a name that Expression
        // Parameters alone declare, or nothing does, is no parameter here.
        int ControllerParameter(string name, params string[] types)
        {
            if (name == null || !controllerParameters.Contains(name)) return -1;
            var index = parameterIndex[name];
            return types.Contains(data.Parameters[index].Type) ? index : -1;
        }

        // The parameter a state option reads, or -1. On a name the controller
        // does not declare Unity plays the option as unset, as written here.
        // On a parameter of another type Unity reads some other float instead,
        // which the data cannot say: that is counted.
        int ReadParameter(string name, string type)
        {
            var index = ControllerParameter(name, type);
            if (index < 0 && name != null && controllerParameters.Contains(name)) Omit("unreadParameter");
            return index;
        }

        // A state option on a parameter Unity cannot read is off.
        int? StateParameter(bool active, string name, string type)
        {
            var index = active ? ReadParameter(name, type) : -1;
            return index < 0 ? (int?)null : index;
        }

        AvatarAnimatorData.Controller ReadController(AnimatorController controller)
        {
            var result = new AvatarAnimatorData.Controller { Playable = "fx" };
            var layers = controller.layers;
            for (var index = 0; index < layers.Length; index++)
            {
                var source = MachineOwner(layers, index);
                readingAdditive = index != 0 && layers[index].blendingMode == AnimatorLayerBlendingMode.Additive;
                result.Layers.Add(ReadLayer(layers[index], index, source, source < 0 ? null : layers[source]));
                readingAdditive = false;
            }
            return result;
        }

        // The layer whose machines a synced layer runs, or -1 when the layer is
        // not synced. Unity follows a chain of synced layers to its end. A
        // source outside the layers counts as none, and a chain that comes back
        // to a layer it passed is not synced.
        internal static int MachineOwner(AnimatorControllerLayer[] layers, int index)
        {
            var passed = new HashSet<int> { index };
            var current = index;
            while (true)
            {
                var next = layers[current].syncedLayerIndex;
                if (next < 0 || next >= layers.Length) return current == index ? -1 : current;
                if (!passed.Add(next)) return -1;
                current = next;
            }
        }

        // A synced layer runs the source layer's machines with its own motions
        // and state behaviours. Unity plays the first layer at weight 1 and as an
        // override layer, whatever its default weight and blending mode say.
        AvatarAnimatorData.Layer ReadLayer(AnimatorControllerLayer layer, int layerIndex, int source, AnimatorControllerLayer synced)
        {
            var result = new AvatarAnimatorData.Layer
            {
                Name = layer.name ?? "", Weight = layerIndex == 0 ? 1 : layer.defaultWeight,
                Blending = layerIndex != 0 && layer.blendingMode == AnimatorLayerBlendingMode.Additive ? "additive" : "override",
                SyncedLayer = source, SyncedLayerAffectsTiming = synced != null && layer.syncedLayerAffectsTiming
            };
            var rootMachine = synced != null ? synced.stateMachine : layer.stateMachine;
            if (rootMachine == null) { result.Machines.Add(new AvatarAnimatorData.Machine { Name = result.Name }); return result; }

            // Flatten the machines (root first, each before its children) and
            // their states, so transitions can name any of them. A machine under
            // two parents belongs to the one reached first. The walk keeps its
            // own stack: how deep the machines nest is up to the controller.
            var machines = new List<(AnimatorStateMachine Machine, AnimatorStateMachine Parent)>();
            var machineIndex = new Dictionary<AnimatorStateMachine, int>();
            var states = new List<(AnimatorState State, int Machine)>();
            var stateIndex = new Dictionary<AnimatorState, int>();
            var pending = new Stack<(AnimatorStateMachine Machine, AnimatorStateMachine Parent)>();
            pending.Push((rootMachine, null));
            while (pending.Count > 0)
            {
                var (machine, parent) = pending.Pop();
                if (machine == null || machineIndex.ContainsKey(machine)) continue;
                var index = machines.Count;
                machineIndex[machine] = index;
                machines.Add((machine, parent));
                foreach (var child in machine.states)
                    if (child.state != null && !stateIndex.ContainsKey(child.state)) { stateIndex[child.state] = states.Count; states.Add((child.state, index)); }
                // Pushed last to first, so the first child is the next one out.
                var children = machine.stateMachines;
                for (var next = children.Length - 1; next >= 0; next--) pending.Push((children[next].stateMachine, machine));
            }

            foreach (var (machine, parent) in machines)
            {
                var entry = new AvatarAnimatorData.Machine
                {
                    Name = machine.name ?? "", Parent = parent == null ? -1 : machineIndex[parent],
                    DefaultState = machine.defaultState != null && stateIndex.TryGetValue(machine.defaultState, out var start) ? start : -1
                };
                // Mute and Solo silence the transitions of Entry, of Any State
                // and of a state. Unity takes a transition out of a machine
                // whatever its own Mute and Solo, and looks at Any State
                // transitions on the root alone: it never takes one that a
                // machine inside another holds.
                Transitions(AnimatorConditions.Enabled(machine.entryTransitions), entry.EntryTransitions, stateIndex, machineIndex);
                if (parent == null) Transitions(AnimatorConditions.Enabled(machine.anyStateTransitions), entry.AnyStateTransitions, stateIndex, machineIndex);
                else Transitions(parent.GetStateMachineTransitions(machine), entry.ExitTransitions, stateIndex, machineIndex);
                Behaviours(machine.behaviours, entry.Behaviours);
                result.Machines.Add(entry);
            }
            foreach (var (state, machine) in states)
            {
                var entry = new AvatarAnimatorData.State
                {
                    Name = state.name ?? "", Machine = machine,
                    Motion = StateMotion(synced != null ? layer.GetOverrideMotion(state) : state.motion),
                    WriteDefaults = state.writeDefaultValues, Speed = state.speed, CycleOffset = state.cycleOffset, Mirror = state.mirror,
                    SpeedParameter = StateParameter(state.speedParameterActive, state.speedParameter, "float"),
                    CycleOffsetParameter = StateParameter(state.cycleOffsetParameterActive, state.cycleOffsetParameter, "float"),
                    MirrorParameter = StateParameter(state.mirrorParameterActive, state.mirrorParameter, "bool"),
                    TimeParameter = StateParameter(state.timeParameterActive, state.timeParameter, "float")
                };
                Transitions(AnimatorConditions.Enabled(state.transitions), entry.Transitions, stateIndex, machineIndex);
                Behaviours(synced != null ? layer.GetOverrideBehaviours(state) : state.behaviours, entry.Behaviours);
                result.States.Add(entry);
            }
            return result;
        }

        // Unity's order is kept: an earlier transition wins. A transition
        // Unity never takes is left out: one without a destination, one with
        // a condition Unity cannot read, and, by the caller, one that Mute or
        // Solo silences. A Solo transition silences the others of its list
        // even when Unity never takes it either.
        void Transitions(IEnumerable<AnimatorTransitionBase> transitions, List<AvatarAnimatorData.Transition> into,
            Dictionary<AnimatorState, int> states, Dictionary<AnimatorStateMachine, int> machines)
        {
            if (transitions == null) return;
            foreach (var transition in transitions)
            {
                if (transition == null) continue;
                var result = new AvatarAnimatorData.Transition();
                if (transition.destinationState != null && states.TryGetValue(transition.destinationState, out var state)) result.ToState = state;
                else if (transition.destinationStateMachine != null && machines.TryGetValue(transition.destinationStateMachine, out var machine)) result.ToMachine = machine;
                else if (transition.isExit) result.Exit = true;
                else continue;
                if (transition is AnimatorStateTransition timed)
                {
                    result.Timed = true;
                    result.HasExitTime = timed.hasExitTime; result.ExitTime = timed.exitTime;
                    result.HasFixedDuration = timed.hasFixedDuration; result.Duration = timed.duration; result.Offset = timed.offset;
                    result.InterruptionSource = Interruption(timed.interruptionSource);
                    result.OrderedInterruption = timed.orderedInterruption; result.CanTransitionToSelf = timed.canTransitionToSelf;
                }
                var conditions = transition.conditions.Select(Condition).ToList();
                if (conditions.Contains(null)) continue;
                result.Conditions.AddRange(conditions);
                into.Add(result);
            }
        }

        // Null for a condition Unity cannot read: of a mode it has no name
        // for, on a name its controller does not declare, or of a mode that
        // is not one of the parameter type's.
        AvatarAnimatorData.Condition Condition(AnimatorCondition condition)
        {
            var mode = Mode(condition.mode);
            var parameter = mode == null ? -1 : ControllerParameter(condition.parameter, AvatarAnimatorData.ConditionTypes(mode));
            return parameter < 0 ? null : new AvatarAnimatorData.Condition { Parameter = parameter, Mode = mode, Threshold = condition.threshold };
        }

        static string Interruption(TransitionInterruptionSource source)
        {
            switch (source)
            {
                case TransitionInterruptionSource.Source: return "source";
                case TransitionInterruptionSource.Destination: return "destination";
                case TransitionInterruptionSource.SourceThenDestination: return "sourceThenDestination";
                case TransitionInterruptionSource.DestinationThenSource: return "destinationThenSource";
                default: return "none";
            }
        }

        static string Mode(AnimatorConditionMode mode)
        {
            switch (mode)
            {
                case AnimatorConditionMode.If: return "if";
                case AnimatorConditionMode.IfNot: return "ifNot";
                case AnimatorConditionMode.Greater: return "greater";
                case AnimatorConditionMode.Less: return "less";
                case AnimatorConditionMode.Equals: return "equals";
                case AnimatorConditionMode.NotEqual: return "notEqual";
                default: return null;
            }
        }

        // The VRChat behaviours the app can run; the rest are counted.
        void Behaviours(IEnumerable<StateMachineBehaviour> behaviours, List<AvatarAnimatorData.Behaviour> into)
        {
            if (behaviours == null) return;
            foreach (var behaviour in behaviours)
            {
                var result = behaviour == null ? null : ReadBehaviour(behaviour);
                if (result != null) into.Add(result);
                else Omit("behaviour");
            }
        }

        AvatarAnimatorData.Behaviour ReadBehaviour(StateMachineBehaviour behaviour)
        {
            if (VrChatSdkTypes.Is(behaviour, VrChatSdkTypes.ParameterDriver))
            {
                var result = new AvatarAnimatorData.Behaviour { Type = "parameterDriver", LocalOnly = Member(behaviour, "localOnly") is bool local && local };
                // A driver with an operation of a type this format has no name
                // for is left out whole, and counted as a behaviour.
                var operations = Items(Member(behaviour, "parameters")).ToList();
                if (operations.Any(operation => OperationType(operation) == null)) return null;
                foreach (var operation in operations)
                {
                    var type = OperationType(operation);
                    // Only copy reads its source; the other types may keep a stale name.
                    var source = type == "copy" ? Member(operation, "source") as string : null;
                    // An operation on a name that neither Expression Parameters
                    // nor the FX controller declare, left behind when the
                    // parameter was deleted, has nothing to set: it is counted
                    // and not written.
                    var name = Member(operation, "name") as string;
                    if (name == null || !declared.Contains(name) || type == "copy" && (source == null || !declared.Contains(source))) { Omit("operation"); continue; }
                    result.Operations.Add(new AvatarAnimatorData.Operation
                    {
                        Type = type, Parameter = parameterIndex[name],
                        Source = source == null ? (int?)null : parameterIndex[source],
                        Value = Number(Member(operation, "value")), ValueMin = Number(Member(operation, "valueMin")),
                        ValueMax = Number(Member(operation, "valueMax")), Chance = Number(Member(operation, "chance")),
                        ConvertRange = Member(operation, "convertRange") is bool convert && convert,
                        SourceMin = Number(Member(operation, "sourceMin")), SourceMax = Number(Member(operation, "sourceMax")),
                        DestMin = Number(Member(operation, "destMin")), DestMax = Number(Member(operation, "destMax"))
                    });
                }
                return result;
            }
            if (VrChatSdkTypes.Is(behaviour, VrChatSdkTypes.TrackingControl))
            {
                var result = new AvatarAnimatorData.Behaviour { Type = "trackingControl" };
                // trackingHead -> head, in declaration order.
                foreach (var field in behaviour.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => f.MetadataToken))
                {
                    if (!field.FieldType.IsEnum || !field.Name.StartsWith("tracking", StringComparison.Ordinal) || field.Name.Length <= 8) continue;
                    var value = field.GetValue(behaviour).ToString();
                    value = value == "NoChange" ? "noChange" : value == "Tracking" ? "tracking" : value == "Animation" ? "animation" : null;
                    var part = char.ToLowerInvariant(field.Name[8]) + field.Name.Substring(9);
                    if (value == null || !AvatarAnimatorData.IsTrackingPart(part)) return null;
                    result.Tracking.Add(new KeyValuePair<string, string>(part, value));
                }
                return result.Tracking.Count > 0 ? result : null;
            }
            if (VrChatSdkTypes.Is(behaviour, VrChatSdkTypes.AnimatorLayerControl))
            {
                var playable = Playable(Member(behaviour, "playable"));
                var layer = Convert.ToInt32(Member(behaviour, "layer"));
                if (playable == null || playable == "sitting" || layer < 0) return null;
                return new AvatarAnimatorData.Behaviour
                {
                    Type = "layerControl", Playable = playable, Layer = layer,
                    GoalWeight = Number(Member(behaviour, "goalWeight")), BlendDuration = Number(Member(behaviour, "blendDuration"))
                };
            }
            if (VrChatSdkTypes.Is(behaviour, VrChatSdkTypes.PlayableLayerControl))
            {
                var playable = Playable(Member(behaviour, "layer"));
                if (playable == null) return null;
                return new AvatarAnimatorData.Behaviour
                {
                    Type = "playableLayerControl", Playable = playable,
                    GoalWeight = Number(Member(behaviour, "goalWeight")), BlendDuration = Number(Member(behaviour, "blendDuration"))
                };
            }
            return null;
        }

        static string OperationType(object operation)
        {
            switch (Member(operation, "type")?.ToString())
            {
                case "Set": return "set";
                case "Add": return "add";
                case "Random": return "random";
                case "Copy": return "copy";
                default: return null;
            }
        }

        static string Playable(object value)
        {
            switch (value?.ToString())
            {
                case "Action": return "action";
                case "FX": return "fx";
                case "Gesture": return "gesture";
                case "Additive": return "additive";
                case "Sitting": return "sitting";
                default: return null;
            }
        }

        // Each motion asset is written once; an override controller's clips
        // replace the base controller's. Trees nested deeper than the format
        // allows stop here: depth keeps the nesting from deciding how deep this
        // recurses, and nesting counts a tree two parents share, walked once,
        // at the deeper of its places.
        int MotionIndex(Motion motion, int depth = 1)
        {
            if (motion == null) return -1;
            if (motion is AnimationClip original && overrides.TryGetValue(original, out var replaced)) motion = replaced;
            if (motionIndex.TryGetValue(motion, out var index)) return index;
            if (motion is AnimationClip clip)
            {
                index = data.Motions.Count;
                motionIndex[motion] = index;
                data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = ClipIndex(clip) });
                nesting.Add(0);
                expanded.Add(1);
                unread.Add(0);
                return index;
            }
            if (!(motion is BlendTree tree)) return -1;
            // A 1D or 2D tree blended by a parameter Unity cannot read plays
            // nothing: it is no motion, wherever it is held.
            var direct = tree.blendType == BlendTreeType.Direct;
            var names = direct ? new string[0] : tree.blendType == BlendTreeType.Simple1D ? new[] { tree.blendParameter } : new[] { tree.blendParameter, tree.blendParameterY };
            var blend = names.Select(name => ControllerParameter(name, "float")).ToArray();
            if (blend.Contains(-1))
            {
                unreadTrees[motion] = names.Where((name, i) => blend[i] < 0).Any(name => name != null && controllerParameters.Contains(name));
                motionIndex[motion] = -1;
                return -1;
            }
            if (depth > AvatarAnimatorData.MaximumBlendTreeDepth) throw new InvalidOperationException(TooDeep);
            index = data.Motions.Count;
            motionIndex[motion] = index;
            var result = new AvatarAnimatorData.Motion { Type = "blendTree", BlendType = BlendType(tree.blendType) };
            data.Motions.Add(result);
            nesting.Add(1);
            expanded.Add(1);
            unread.Add(0);
            var inside = 0;
            result.Parameters.AddRange(blend);
            using (var serialized = new SerializedObject(tree))
                result.NormalizedBlendValues = serialized.FindProperty("m_NormalizedBlendValues")?.boolValue ?? false;
            foreach (var child in tree.children)
            {
                // A child of a direct tree weighted by a parameter Unity cannot
                // read has no weight: it is left out. Unity reads some other
                // float for one of another type, and what it does with an
                // undeclared weight was not measured, so both are counted.
                var weight = direct ? ControllerParameter(child.directBlendParameter, "float") : -1;
                if (direct && weight < 0)
                {
                    if (child.directBlendParameter != null) inside++;
                    continue;
                }
                var childIndex = MotionIndex(child.motion, depth + 1);
                inside += Unread(child.motion, childIndex, true);
                if (childIndex >= 0)
                {
                    nesting[index] = Math.Max(nesting[index], nesting[childIndex] + 1);
                    expanded[index] = Math.Min(expanded[index] + expanded[childIndex], AvatarAnimatorData.MaximumExpandedMotions + 1);
                }
                result.Children.Add(new AvatarAnimatorData.Child
                {
                    Motion = childIndex, Threshold = child.threshold, X = child.position.x, Y = child.position.y,
                    TimeScale = child.timeScale, CycleOffset = child.cycleOffset, Mirror = child.mirror,
                    DirectParameter = direct ? weight : (int?)null
                });
            }
            unread[index] = inside;
            if (nesting[index] > AvatarAnimatorData.MaximumBlendTreeDepth) throw new InvalidOperationException(TooDeep);
            if (expanded[index] > AvatarAnimatorData.MaximumExpandedMotions) throw new InvalidOperationException(TooManyMotions);
            return index;
        }

        // The motion a state plays, counting what it leaves out at this use.
        int StateMotion(Motion motion)
        {
            var index = MotionIndex(motion);
            if (readingAdditive) MarkAdditive(index);
            Omit("unreadParameter", Unread(motion, index, false));
            return index;
        }

        // The clips a written motion plays, through its blend trees. A motion
        // is cached, so a clip met first in another layer is marked here too.
        void MarkAdditive(int index)
        {
            var pending = new Stack<int>();
            pending.Push(index);
            var seen = new HashSet<int>();
            while (pending.Count > 0)
            {
                var motion = pending.Pop();
                if (motion < 0 || !seen.Add(motion)) continue;
                var written = data.Motions[motion];
                if (written.Type == "clip") additiveClips.Add(written.Clip);
                foreach (var child in written.Children) pending.Push(child.Motion);
            }
        }

        // The uses of a parameter Unity cannot read that one use of a motion
        // brings: those inside a written motion, or the tree itself when it
        // was left out. On a parameter of another type Unity reads some other
        // float, and a tree on an undeclared name held by another tree plays by
        // what the other children leave; directly under a state that tree
        // plays nothing, as written.
        int Unread(Motion motion, int index, bool held)
        {
            if (index >= 0) return unread[index];
            if (motion is AnimationClip original && overrides.TryGetValue(original, out var replaced)) motion = replaced;
            return motion != null && unreadTrees.TryGetValue(motion, out var otherType) && (otherType || held) ? 1 : 0;
        }

        static string BlendType(BlendTreeType type)
        {
            switch (type)
            {
                case BlendTreeType.Simple1D: return "1d";
                case BlendTreeType.SimpleDirectional2D: return "2dSimpleDirectional";
                case BlendTreeType.FreeformDirectional2D: return "2dFreeformDirectional";
                case BlendTreeType.FreeformCartesian2D: return "2dFreeformCartesian";
                default: return "direct";
            }
        }

        // A clip is written even without a curve the app plays: its length
        // still times the transitions out of its state.
        int ClipIndex(AnimationClip clip)
        {
            if (clipIndex.TryGetValue(clip, out var index)) return index;
            index = data.Clips.Count;
            clipIndex[clip] = index;
            var tracks = new List<(int, ExpressionAnimationData.Curve)>();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            data.Clips.Add(new AvatarAnimatorData.Clip { Name = clip.name ?? "", Duration = clip.length, Loop = settings.loopTime, CycleOffset = settings.cycleOffset });
            clipTracks.Add(tracks);
            var morphTracks = new List<(int, ExpressionAnimationData.Curve)>();
            clipMorphTracks.Add(morphTracks);
            var keys = 0;
            var shapes = new List<EditorCurveBinding>();
            // A curve the contract does not take (more keys or tracks than the
            // clip has room left for, a key past an hour, a value that is not
            // a number) is left out alone, so the rest of the avatar is still
            // written. The keys are counted before the curve is converted,
            // which keeps every key: a curve too long is not converted.
            ExpressionAnimationData.Curve Take(EditorCurveBinding binding)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0 || keys + curve.length > AvatarAnimatorData.MaximumKeysPerClip ||
                    tracks.Count + morphTracks.Count >= AvatarAnimatorData.MaximumTracksPerClip) return null;
                var converted = VrChatGestureExpressions.ConvertCurve(curve);
                try { converted.Validate(); }
                catch (InvalidOperationException) { return null; }
                keys += converted.Keys.Count;
                return converted;
            }
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!(binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive"))
                {
                    // BlendShape curves take what activeSelf leaves of the budget.
                    if (Kind(binding) == "blendShape") shapes.Add(binding);
                    else Omit(Kind(binding));
                    continue;
                }
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                if (target == null) { Omit("missingNode"); continue; }
                var converted = Take(binding);
                if (converted == null) { Omit("other"); continue; }
                tracks.Add((Target(target.gameObject), converted));
            }
            foreach (var binding in shapes)
            {
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                var renderer = target == null ? null : target.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null) { Omit("missingNode"); continue; }
                var converted = Take(binding);
                if (converted == null) { Omit("blendShape"); continue; }
                morphTracks.Add((Morph(renderer, binding.propertyName.Substring(ShapePrefix.Length)), converted));
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                Omit(typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith("m_Materials", StringComparison.Ordinal) ? "material" : "other");
            return index;
        }

        int Target(GameObject gameObject)
        {
            if (targetIndex.TryGetValue(gameObject, out var index)) return index;
            targetIndex[gameObject] = targets.Count;
            targets.Add((gameObject, (rest.TryGetValue(gameObject, out var active) ? active : gameObject.activeSelf) ? 1 : 0));
            return targets.Count - 1;
        }

        int Morph(SkinnedMeshRenderer renderer, string shape)
        {
            if (morphIndex.TryGetValue((renderer, shape), out var index)) return index;
            morphIndex[(renderer, shape)] = morphs.Count;
            morphs.Add((renderer, shape));
            return morphs.Count - 1;
        }

        // An Animator curve on the avatar root drives a parameter when the FX
        // declares the name; otherwise it is a Humanoid muscle or root motion.
        string Kind(EditorCurveBinding binding)
        {
            var property = binding.propertyName ?? "";
            if (binding.type == typeof(Animator) && binding.path.Length == 0) return controllerParameters.Contains(property) ? "parameter" : "muscle";
            if (typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type) && property.StartsWith("blendShape.", StringComparison.Ordinal)) return "blendShape";
            if (typeof(Renderer).IsAssignableFrom(binding.type) && property.StartsWith("material.", StringComparison.Ordinal)) return "material";
            if (typeof(Transform).IsAssignableFrom(binding.type)) return "transform";
            if (property == "m_Enabled") return "enabled";
            return "other";
        }

        void Omit(string kind, int times = 1)
        {
            if (times > 0) omitted[kind] = (omitted.TryGetValue(kind, out var count) ? count : 0) + times;
        }

        // The menus reached from the root menu, each asset once, so a submenu
        // that opens an earlier menu points back to it.
        void ReadMenus(object rootMenu)
        {
            if (!(rootMenu is UnityEngine.Object asset) || asset == null) return;
            var menuIndex = new Dictionary<UnityEngine.Object, int>();
            var pending = new Queue<(UnityEngine.Object Menu, AvatarAnimatorData.Menu Into)>();
            int MenuIndex(UnityEngine.Object menu)
            {
                if (menuIndex.TryGetValue(menu, out var index)) return index;
                var entry = new AvatarAnimatorData.Menu();
                menuIndex[menu] = data.Menus.Count;
                data.Menus.Add(entry);
                pending.Enqueue((menu, entry));
                return data.Menus.Count - 1;
            }
            // A control sets only what Expression Parameters declare, as
            // VRChat's menu does: any other name is no parameter to it.
            int ExpressionParameter(object named) =>
                Member(named, "name") is string name && parameterIndex.TryGetValue(name, out var index) && data.Parameters[index].Origin == "expression" ? index : -1;
            MenuIndex(asset);
            while (pending.Count > 0)
            {
                var (menu, into) = pending.Dequeue();
                foreach (var control in Items(Member(menu, "controls")))
                {
                    var type = ControlType(Member(control, "type")?.ToString());
                    if (type == null) continue;
                    var entry = new AvatarAnimatorData.Control { Name = Member(control, "name") as string ?? "", Type = type, Value = Number(Member(control, "value")) };
                    var parameter = ExpressionParameter(Member(control, "parameter"));
                    if (parameter >= 0) entry.Parameter = parameter;
                    if (type == "subMenu" && Member(control, "subMenu") is UnityEngine.Object subMenu && subMenu != null) entry.Menu = MenuIndex(subMenu);
                    foreach (var sub in Items(Member(control, "subParameters"))) entry.SubParameters.Add(ExpressionParameter(sub));
                    foreach (var label in Items(Member(control, "labels"))) entry.Labels.Add(Member(label, "name") as string ?? "");
                    into.Controls.Add(entry);
                }
            }
        }

        static string ControlType(string type)
        {
            switch (type)
            {
                case "Toggle": return "toggle";
                case "Button": return "button";
                case "RadialPuppet": return "radial";
                case "TwoAxisPuppet": return "twoAxis";
                case "FourAxisPuppet": return "fourAxis";
                case "SubMenu": return "subMenu";
                default: return null;
            }
        }

        static object Member(object value, string name) => VrChatExpressionMenu.Member(value, name);

        static IEnumerable<object> Items(object value) => value is IEnumerable list && !(value is string) ? list.Cast<object>() : Enumerable.Empty<object>();

        static double Number(object value) => value == null ? 0 : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
