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

        // The largest extension JSON (UTF-8 bytes) written. Tests lower it.
        internal const int MaximumMegabytes = 4;
        internal static int MaximumBytes = MaximumMegabytes * 1024 * 1024;

        internal static string TooLarge => ExporterLocalization.Format(TooLargeReason, MaximumMegabytes);
        internal static string TooDeep => ExporterLocalization.Format(TooDeepReason, AvatarAnimatorData.MaximumBlendTreeDepth);

        readonly AvatarAnimatorData data = new AvatarAnimatorData();
        // Curve targets stay GameObjects until the output nodes are known.
        readonly List<(GameObject Object, int Default)> targets = new List<(GameObject, int)>();
        readonly Dictionary<GameObject, int> targetIndex = new Dictionary<GameObject, int>();
        readonly List<List<(int Target, ExpressionAnimationData.Curve Curve)>> clipTracks = new List<List<(int, ExpressionAnimationData.Curve)>>();
        readonly Dictionary<string, int> omitted = new Dictionary<string, int>(StringComparer.Ordinal);

        readonly GameObject root;
        readonly IReadOnlyDictionary<GameObject, bool> rest;
        readonly Dictionary<string, int> parameterIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly HashSet<string> controllerParameters = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<AnimationClip, AnimationClip> overrides;
        readonly Dictionary<Motion, int> motionIndex = new Dictionary<Motion, int>();
        // Blend trees nested in each of data.Motions, itself included.
        readonly List<int> nesting = new List<int>();
        readonly Dictionary<AnimationClip, int> clipIndex = new Dictionary<AnimationClip, int>();

        AvatarAnimatorCapture(GameObject root, IReadOnlyDictionary<GameObject, bool> rest, Dictionary<AnimationClip, AnimationClip> overrides)
        {
            this.root = root; this.rest = rest; this.overrides = overrides;
        }

        // activeSelf of every object of the copy before the export's own
        // steps change it (menu toggles turn their default-off meshes on).
        internal static Dictionary<GameObject, bool> Snapshot(GameObject clone) =>
            clone.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.gameObject, t => t.gameObject.activeSelf);

        // Null without a VRChat avatar descriptor or a custom FX controller:
        // the file is then written without the extension.
        internal static AvatarAnimatorCapture Capture(GameObject clone, IReadOnlyDictionary<GameObject, bool> rest)
        {
            var descriptor = VrChatExpressionMenu.Descriptor(clone);
            if (descriptor == null) return null;
            RuntimeAnimatorController fx = null;
            // The same FX layer VrChatExpressionMenu.Read takes.
            if (Member(descriptor, "customizeAnimationLayers") is bool custom && custom)
                foreach (var layer in Items(Member(descriptor, "baseAnimationLayers")))
                {
                    if (Member(layer, "isDefault") is bool isDefault && isDefault) continue;
                    if (Member(layer, "type")?.ToString() == "FX") fx = Member(layer, "animatorController") as RuntimeAnimatorController;
                }
            if (fx == null) return null;
            var controller = ExpressionDependencies.Controller(fx);
            var capture = new AvatarAnimatorCapture(clone, rest ?? new Dictionary<GameObject, bool>(), ExpressionDependencies.Overrides(fx));
            var expressions = Member(descriptor, "customExpressions") is bool enabled && enabled;
            capture.ReadParameters(expressions ? Member(descriptor, "expressionParameters") : null, controller);
            capture.data.Controllers.Add(capture.ReadController(controller));
            // Menus last: they name only parameters the table already has.
            if (expressions) capture.ReadMenus(Member(descriptor, "expressionsMenu"));
            return capture;
        }

        // The data with glTF node indices. A target the file has no node for
        // is left out with its curves, which are counted as missingNode.
        internal AvatarAnimatorData Resolve(Func<GameObject, int> node)
        {
            data.Targets.Clear();
            var map = new int[targets.Count];
            for (var i = 0; i < targets.Count; i++)
            {
                var index = targets[i].Object == null ? -1 : node(targets[i].Object);
                map[i] = index < 0 ? -1 : data.Targets.Count;
                if (index >= 0) data.Targets.Add(new AvatarAnimatorData.Target { Node = index, Property = "active", Rest = targets[i].Default });
            }
            var missing = 0;
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
            }
            data.Omitted.Clear();
            foreach (var kind in AvatarAnimatorData.OmittedKinds)
            {
                var count = (omitted.TryGetValue(kind, out var value) ? value : 0) + (kind == "missingNode" ? missing : 0);
                if (count > 0) data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = kind, Count = count });
            }
            return data;
        }

        // An extension over MaximumBytes is an error: the caller leaves the
        // stage out and lists it with this reason.
        internal static byte[] Inject(byte[] bytes, AvatarAnimatorData data)
        {
            if (data == null) return bytes;
            var extension = AvatarAnimatorData.Write(data);
            if (Encoding.UTF8.GetByteCount(JsonDom.Serialize(extension)) > MaximumBytes)
                throw new InvalidOperationException(TooLarge);
            var document = GlbDocument.Read(bytes);
            var nodeCount = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> nodes ? nodes.Count : 0;
            if (data.Targets.Any(target => target.Node >= nodeCount))
                throw new InvalidOperationException("Avatar animator target node is out of range.");
            document.PlaceExtension(AvatarAnimatorData.Extension, extension);
            return document.Write();
        }

        // Expression Parameters first, then the FX parameters they do not name.
        // A name in both takes the FX type; the first of a repeated name counts.
        void ReadParameters(object expressionParameters, AnimatorController controller)
        {
            foreach (var parameter in Items(Member(expressionParameters, "parameters")))
            {
                var name = Member(parameter, "name") as string;
                var type = ExpressionType(Member(parameter, "valueType")?.ToString());
                if (string.IsNullOrEmpty(name) || type == null || parameterIndex.ContainsKey(name)) continue;
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

        // A name that no table declares reads as Unity reads a missing
        // parameter: a float at 0.
        int Parameter(string name)
        {
            name = name ?? "";
            if (parameterIndex.TryGetValue(name, out var index)) return index;
            parameterIndex[name] = data.Parameters.Count;
            data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = name, Type = "float", Origin = "animator", Default = 0 });
            return data.Parameters.Count - 1;
        }

        AvatarAnimatorData.Controller ReadController(AnimatorController controller)
        {
            var result = new AvatarAnimatorData.Controller { Playable = "fx" };
            var layers = controller.layers;
            for (var index = 0; index < layers.Length; index++)
            {
                var source = MachineOwner(layers, index);
                result.Layers.Add(ReadLayer(layers[index], index, source, source < 0 ? null : layers[source]));
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
                Transitions(machine.entryTransitions, entry.EntryTransitions, stateIndex, machineIndex);
                Transitions(machine.anyStateTransitions, entry.AnyStateTransitions, stateIndex, machineIndex);
                if (parent != null) Transitions(parent.GetStateMachineTransitions(machine), entry.ExitTransitions, stateIndex, machineIndex);
                Behaviours(machine.behaviours, entry.Behaviours);
                result.Machines.Add(entry);
            }
            foreach (var (state, machine) in states)
            {
                var entry = new AvatarAnimatorData.State
                {
                    Name = state.name ?? "", Machine = machine,
                    Motion = MotionIndex(synced != null ? layer.GetOverrideMotion(state) : state.motion),
                    WriteDefaults = state.writeDefaultValues, Speed = state.speed, CycleOffset = state.cycleOffset, Mirror = state.mirror,
                    SpeedParameter = state.speedParameterActive ? Parameter(state.speedParameter) : (int?)null,
                    CycleOffsetParameter = state.cycleOffsetParameterActive ? Parameter(state.cycleOffsetParameter) : (int?)null,
                    MirrorParameter = state.mirrorParameterActive ? Parameter(state.mirrorParameter) : (int?)null,
                    TimeParameter = state.timeParameterActive ? Parameter(state.timeParameter) : (int?)null
                };
                Transitions(state.transitions, entry.Transitions, stateIndex, machineIndex);
                Behaviours(synced != null ? layer.GetOverrideBehaviours(state) : state.behaviours, entry.Behaviours);
                result.States.Add(entry);
            }
            return result;
        }

        // Unity's order is kept: an earlier transition wins. A transition
        // without a destination, which Unity never takes, is left out.
        void Transitions(IEnumerable<AnimatorTransitionBase> transitions, List<AvatarAnimatorData.Transition> into,
            Dictionary<AnimatorState, int> states, Dictionary<AnimatorStateMachine, int> machines)
        {
            if (transitions == null) return;
            foreach (var transition in transitions)
            {
                if (transition == null) continue;
                var result = new AvatarAnimatorData.Transition { Mute = transition.mute, Solo = transition.solo };
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
                foreach (var condition in transition.conditions)
                {
                    var mode = Mode(condition.mode);
                    if (mode == null) continue;
                    result.Conditions.Add(new AvatarAnimatorData.Condition { Parameter = Parameter(condition.parameter), Mode = mode, Threshold = condition.threshold });
                }
                into.Add(result);
            }
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
                foreach (var operation in Items(Member(behaviour, "parameters")))
                {
                    var type = Member(operation, "type")?.ToString();
                    type = type == "Set" ? "set" : type == "Add" ? "add" : type == "Random" ? "random" : type == "Copy" ? "copy" : null;
                    if (type == null) return null;
                    // Only copy reads its source; the other types may keep a stale name.
                    var source = type == "copy" ? Member(operation, "source") as string : null;
                    result.Operations.Add(new AvatarAnimatorData.Operation
                    {
                        Type = type, Parameter = Parameter(Member(operation, "name") as string),
                        Source = string.IsNullOrEmpty(source) ? (int?)null : Parameter(source),
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
                return index;
            }
            if (!(motion is BlendTree tree)) return -1;
            if (depth > AvatarAnimatorData.MaximumBlendTreeDepth) throw new InvalidOperationException(TooDeep);
            index = data.Motions.Count;
            motionIndex[motion] = index;
            var result = new AvatarAnimatorData.Motion { Type = "blendTree", BlendType = BlendType(tree.blendType) };
            data.Motions.Add(result);
            nesting.Add(1);
            if (tree.blendType == BlendTreeType.Simple1D) result.Parameters.Add(Parameter(tree.blendParameter));
            else if (tree.blendType != BlendTreeType.Direct) { result.Parameters.Add(Parameter(tree.blendParameter)); result.Parameters.Add(Parameter(tree.blendParameterY)); }
            using (var serialized = new SerializedObject(tree))
                result.NormalizedBlendValues = serialized.FindProperty("m_NormalizedBlendValues")?.boolValue ?? false;
            foreach (var child in tree.children)
            {
                var childIndex = MotionIndex(child.motion, depth + 1);
                if (childIndex >= 0) nesting[index] = Math.Max(nesting[index], nesting[childIndex] + 1);
                result.Children.Add(new AvatarAnimatorData.Child
                {
                    Motion = childIndex, Threshold = child.threshold, X = child.position.x, Y = child.position.y,
                    TimeScale = child.timeScale, CycleOffset = child.cycleOffset, Mirror = child.mirror,
                    DirectParameter = tree.blendType == BlendTreeType.Direct ? Parameter(child.directBlendParameter) : (int?)null
                });
            }
            if (nesting[index] > AvatarAnimatorData.MaximumBlendTreeDepth) throw new InvalidOperationException(TooDeep);
            return index;
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
            var keys = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!(binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")) { Omit(Kind(binding)); continue; }
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                if (target == null) { Omit("missingNode"); continue; }
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) { Omit("other"); continue; }
                // A curve the contract does not take (a key past an hour, a
                // value that is not a number, more keys than a clip may hold)
                // is left out alone, so the rest of the avatar is still written.
                var converted = VrChatGestureExpressions.ConvertCurve(curve);
                try { converted.Validate(); }
                catch (InvalidOperationException) { Omit("other"); continue; }
                if (keys + converted.Keys.Count > AvatarAnimatorData.MaximumKeysPerClip) { Omit("other"); continue; }
                keys += converted.Keys.Count;
                tracks.Add((Target(target.gameObject), converted));
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

        void Omit(string kind) => omitted[kind] = (omitted.TryGetValue(kind, out var count) ? count : 0) + 1;

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
            MenuIndex(asset);
            while (pending.Count > 0)
            {
                var (menu, into) = pending.Dequeue();
                foreach (var control in Items(Member(menu, "controls")))
                {
                    var type = ControlType(Member(control, "type")?.ToString());
                    if (type == null) continue;
                    var entry = new AvatarAnimatorData.Control { Name = Member(control, "name") as string ?? "", Type = type, Value = Number(Member(control, "value")) };
                    var parameter = Member(Member(control, "parameter"), "name") as string;
                    if (!string.IsNullOrEmpty(parameter) && parameterIndex.TryGetValue(parameter, out var index)) entry.Parameter = index;
                    if (type == "subMenu" && Member(control, "subMenu") is UnityEngine.Object subMenu && subMenu != null) entry.Menu = MenuIndex(subMenu);
                    foreach (var sub in Items(Member(control, "subParameters")))
                        entry.SubParameters.Add(Member(sub, "name") is string name && name.Length > 0 && parameterIndex.TryGetValue(name, out var subIndex) ? subIndex : -1);
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
