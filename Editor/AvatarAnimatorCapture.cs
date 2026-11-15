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
using VRVlog.LilToon;

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

        // The result line of the curves left out, {0} the kinds with their counts.
        internal const string OmittedCurvesNote = "アプリに渡すFXの動きに入れなかったアニメーションの曲線: {0}";
        // {0} the kind, {1} the number of curves.
        internal const string OmittedCurveCount = "{0} {1}本";
        // The kinds of omitted that count curves, in OmittedKinds order, with their labels.
        internal static readonly (string Kind, string Label)[] CurveKinds =
        {
            ("blendShape", "BlendShape"), ("material", "マテリアル"), ("transform", "Transform"), ("enabled", "コンポーネントのオン・オフ"),
            ("parameter", "パラメーター"), ("muscle", "Humanoidのマッスル"), ("other", "その他"), ("missingNode", "VRMに無いオブジェクト"),
            ("materialProperty", "アプリで扱わないマテリアルのプロパティ"), ("materialSwitch", "マテリアルの機能のオン・オフ"),
            ("materialSlotShader", "lilToon以外のマテリアルへの差し替え"),
            ("humanoidTransform", "Humanoidのボーンを動かすTransform"), ("rotationMixed", "角度と四元数が混ざる回転"),
            ("humanoidRoot", "Humanoidのルートの位置と回転・手足のIK"),
        };
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
        // Renderer enabled curves stay renderers until the output nodes are
        // known, each renderer with its enabled flag when its first curve was
        // read, or the one enabledRest gives it.
        readonly List<(Renderer Renderer, int Default)> renderers = new List<(Renderer, int)>();
        readonly Dictionary<Renderer, int> rendererIndex = new Dictionary<Renderer, int>();
        readonly List<List<(int Renderer, ExpressionAnimationData.Curve Curve)>> clipEnabledTracks = new List<List<(int, ExpressionAnimationData.Curve)>>();
        // Constraint enabled curves stay components until the constraints
        // VRVLOG_avatar_constraints writes are known, each with its enabled
        // flag when its first curve was read.
        readonly List<(Behaviour Constraint, int Default)> constraints = new List<(Behaviour, int)>();
        readonly Dictionary<Behaviour, int> constraintIndex = new Dictionary<Behaviour, int>();
        readonly List<List<(int Constraint, ExpressionAnimationData.Curve Curve)>> clipConstraintTracks = new List<List<(int, ExpressionAnimationData.Curve)>>();
        // Material curves stay a renderer and what they move (MaterialCurve)
        // until the output materials are known.
        readonly List<List<(Renderer Renderer, (string Property, string Name, string Channel) Moves, ExpressionAnimationData.Curve Curve)>> clipMaterialTracks =
            new List<List<(Renderer, (string, string, string), ExpressionAnimationData.Curve)>>();
        // Each slot of a renderer when its first curve on a property channel
        // was read, with whether the slot's material has the property and its
        // value there: the export later swaps lilToon slots for fallback
        // materials, which the output indices are taken from, but which may
        // lack the property or hold another value.
        readonly Dictionary<(Renderer, (string, string, string)), (Material Material, bool Has, float Value)[]> slotsAtRead =
            new Dictionary<(Renderer, (string, string, string)), (Material, bool, float)[]>();
        // Material slot curves stay a renderer, the slot they set when read
        // and their keys until the output materials are known.
        readonly List<List<(Renderer Renderer, int Slot, ObjectReferenceKeyframe[] Keys)>> clipSlotTracks = new List<List<(Renderer, int, ObjectReferenceKeyframe[])>>();
        // Every renderer's slots when the first slot curve was read, each
        // with the slot it was then: a slot curve names materials, which the
        // file writes under the slots that held them; the export later swaps
        // lilToon slots for fallback copies and drops empty ones (KeepSlots).
        Dictionary<Renderer, (Material Material, int Slot)[]> rendererSlotsAtRead;
        // Transform curves stay an object and the channel they move
        // (TransformCurve) until the output nodes are known.
        readonly List<(GameObject Object, (string Property, string Channel, bool Euler) Moves)> transforms = new List<(GameObject, (string, string, bool))>();
        readonly Dictionary<(GameObject, (string, string, bool)), int> transformIndex = new Dictionary<(GameObject, (string, string, bool)), int>();
        readonly List<List<(int Transform, ExpressionAnimationData.Curve Curve)>> clipTransformTracks = new List<List<(int, ExpressionAnimationData.Curve)>>();
        // Each clip's muscle curves, by the muscle's index (Muscle).
        readonly List<List<(int Muscle, ExpressionAnimationData.Curve Curve)>> clipMuscleTracks = new List<List<(int, ExpressionAnimationData.Curve)>>();
        // The layers' masks with the paths their transform masks let move,
        // which become nodes when the output nodes are known (Build).
        readonly List<(AvatarAnimatorData.Mask Mask, List<string> Paths)> masks = new List<(AvatarAnimatorData.Mask, List<string>)>();
        // The Humanoid bones of the copy, read with the first Transform curve.
        HashSet<Transform> humanoidBones;

        // The export drops empty submeshes with their slots after the curves
        // were read (UniVrmOneClickExporter.DropEmptySubmeshes): kept holds,
        // for each slot the renderer has now, the slot it had before.
        internal void KeepSlots(Renderer renderer, IReadOnlyList<int> kept)
        {
            foreach (var key in slotsAtRead.Keys.Where(key => key.Item1 == renderer).ToList())
            {
                var read = slotsAtRead[key];
                slotsAtRead[key] = kept.Select(slot => slot < read.Length ? read[slot] : ((Material)null, false, 0f)).ToArray();
            }
            if (rendererSlotsAtRead != null && rendererSlotsAtRead.TryGetValue(renderer, out var slots))
                rendererSlotsAtRead[renderer] = kept.Select(slot => slot < slots.Length ? slots[slot] : ((Material)null, -1)).ToArray();
        }
        // The morph targets the last Resolve wrote, each with the
        // {node, index} that UnusedMorphTargets keeps and renumbers.
        readonly List<(AvatarAnimatorData.Target Target, Dictionary<string, object> Bind, bool Soft)> morphBinds = new List<(AvatarAnimatorData.Target, Dictionary<string, object>, bool)>();
        // What shows each target the last Resolve wrote in the copy, for
        // AvatarAnimatorCrossCheck: the object of an active target, the
        // renderer and shape a morph target was read from (not its reverse),
        // the renderer of an enabled target, and the first renderer and the
        // material a material target was read from.
        readonly Dictionary<AvatarAnimatorData.Target, AvatarAnimatorCrossCheck.Binding> shownBy = new Dictionary<AvatarAnimatorData.Target, AvatarAnimatorCrossCheck.Binding>();
        // The path under the root each curve's object had when the clips were
        // read; the export may rename or move it before the cross-check.
        readonly Dictionary<GameObject, string> pathAtRead = new Dictionary<GameObject, string>();
        // The float parameter curves of each clip, by parameter index.
        readonly List<List<(int Parameter, ExpressionAnimationData.Curve Curve)>> clipParameterTracks = new List<List<(int, ExpressionAnimationData.Curve)>>();
        // The FX parameters a clip of the controller animates. Unity holds each
        // of them: SetFloat and the like no longer change it (see Driven).
        readonly SortedSet<int> driven = new SortedSet<int>();
        readonly HashSet<Motion> drivenWalked = new HashSet<Motion>();
        // The value the FX controller gives each parameter it declares.
        readonly Dictionary<int, double> controllerDefaults = new Dictionary<int, double>();
        readonly Dictionary<string, int> omitted = new Dictionary<string, int>(StringComparer.Ordinal);

        readonly GameObject root;
        readonly IReadOnlyDictionary<GameObject, bool> rest;
        readonly IReadOnlyDictionary<Renderer, bool> enabledRest;
        readonly Dictionary<string, int> parameterIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        // The parameters the controller read now declares, with their types.
        Dictionary<string, string> controllerParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        // The parameters whose type and default a controller gave: the first
        // controller read (the FX) wins.
        readonly HashSet<int> controllerTyped = new HashSet<int>();
        // The names Expression Parameters or a controller declare.
        readonly HashSet<string> declared = new HashSet<string>(StringComparer.Ordinal);
        // The clips the controller read now overrides.
        Dictionary<AnimationClip, AnimationClip> overrides;
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
        // The faces ReadFaces matched, each with the clips it was read from.
        readonly List<(VrmMenuExpressions.Expression Face, SortedSet<int> Clips)> faces = new List<(VrmMenuExpressions.Expression, SortedSet<int>)>();
        bool readingAdditive;

        AvatarAnimatorCapture(GameObject root, IReadOnlyDictionary<GameObject, bool> rest, IReadOnlyDictionary<Renderer, bool> enabledRest,
            Dictionary<AnimationClip, AnimationClip> overrides)
        {
            this.root = root; this.rest = rest; this.enabledRest = enabledRest; this.overrides = overrides;
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
        // to where each one ends up. held gets, for each, the weight every
        // curve holds, or null when a curve moves it.
        internal static List<(SkinnedMeshRenderer Renderer, string Shape)> DrivenMorphs(GameObject clone,
            IDictionary<(SkinnedMeshRenderer Renderer, string Shape), double?> held = null)
        {
            var fx = Fx(VrChatExpressionMenu.Descriptor(clone));
            var found = new SortedSet<(string Path, string Shape)>(Comparer<(string, string)>.Create((a, b) =>
            {
                var path = string.CompareOrdinal(a.Item1, b.Item1);
                return path != 0 ? path : string.CompareOrdinal(a.Item2, b.Item2);
            }));
            var values = new Dictionary<(string, string), double?>();
            if (fx == null) return new List<(SkinnedMeshRenderer, string)>();
            foreach (var clip in fx.animationClips.Where(clip => clip != null).Distinct())
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith(ShapePrefix, StringComparison.Ordinal))
                    {
                        var key = (binding.path, binding.propertyName.Substring(ShapePrefix.Length));
                        found.Add(key);
                        if (held == null) continue;
                        var value = HeldValue(AnimationUtility.GetEditorCurve(clip, binding));
                        values[key] = values.TryGetValue(key, out var before) && before != value ? null : value;
                    }
            var result = new List<(SkinnedMeshRenderer, string)>();
            foreach (var (path, shape) in found)
            {
                var target = path.Length == 0 ? clone.transform : clone.transform.Find(path);
                var renderer = target == null ? null : target.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.GetBlendShapeIndex(shape) < 0) continue;
                result.Add((renderer, shape));
                if (held != null) held[(renderer, shape)] = values[(path, shape)];
            }
            return result;
        }

        // The value a curve stays at, or null when it moves.
        static double? HeldValue(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return null;
            var keys = curve.keys;
            foreach (var key in keys)
                if (key.value != keys[0].value || !Flat(key.inTangent) || !Flat(key.outTangent)) return null;
            return keys[0].value;
        }

        static bool Flat(float tangent) => tangent == 0f || float.IsInfinity(tangent);

        const string ShapePrefix = "blendShape.";

        // The same FX layer VrChatExpressionMenu.Read takes.
        static RuntimeAnimatorController Fx(object descriptor) => Playable(descriptor, "FX");

        // The custom controller of a playable layer of the descriptor (FX,
        // Gesture or Action), or null when it keeps the SDK's default.
        static RuntimeAnimatorController Playable(object descriptor, string type)
        {
            RuntimeAnimatorController found = null;
            if (descriptor != null && Member(descriptor, "customizeAnimationLayers") is bool custom && custom)
                foreach (var layer in Items(Member(descriptor, "baseAnimationLayers")))
                {
                    if (Member(layer, "isDefault") is bool isDefault && isDefault) continue;
                    if (Member(layer, "type")?.ToString() == type) found = Member(layer, "animatorController") as RuntimeAnimatorController;
                }
            return found;
        }

        // Null without a VRChat avatar descriptor or a custom FX controller:
        // the file is then written without the extension.
        // The FX controller read, as the descriptor names it (an override controller stays one).
        internal RuntimeAnimatorController Controller { get; private set; }

        // What shows each of data.Targets in the copy; null for a parameter target.
        internal IReadOnlyList<AvatarAnimatorCrossCheck.Binding> Bindings(AvatarAnimatorData written) =>
            written.Targets.Select(target => shownBy.TryGetValue(target, out var binding) ? binding : null).ToList();

        // A behaviour the Animator runs, read as the extension writes it, or
        // null when the extension leaves it out. Counts nothing.
        internal AvatarAnimatorData.Behaviour ReadRunningBehaviour(StateMachineBehaviour behaviour)
        {
            var counts = new Dictionary<string, int>(omitted, StringComparer.Ordinal);
            try { return ReadBehaviour(behaviour); }
            finally
            {
                omitted.Clear();
                foreach (var pair in counts) omitted.Add(pair.Key, pair.Value);
            }
        }

        // enabledRest gives renderers whose enabled the export's own steps
        // changed the value they had (ObjectToggleCapture.CopiedEnablementRest).
        internal static AvatarAnimatorCapture Capture(GameObject clone, IReadOnlyDictionary<GameObject, bool> rest,
            IReadOnlyDictionary<Renderer, bool> enabledRest = null)
        {
            var descriptor = VrChatExpressionMenu.Descriptor(clone);
            if (descriptor == null) return null;
            var fx = Fx(descriptor);
            if (fx == null) return null;
            var controller = ExpressionDependencies.Controller(fx);
            var capture = new AvatarAnimatorCapture(clone, rest ?? new Dictionary<GameObject, bool>(), enabledRest ?? new Dictionary<Renderer, bool>(),
                ExpressionDependencies.Overrides(fx)) { Controller = fx };
            var expressions = Member(descriptor, "customExpressions") is bool enabled && enabled;
            capture.ReadParameters(expressions ? Member(descriptor, "expressionParameters") : null, controller);
            capture.data.Controllers.Add(capture.ReadController(controller, "fx"));
            // Then the Gesture and the Action, each with its own parameters and overrides.
            var fxParameters = capture.controllerParameters;
            var fxOverrides = capture.overrides;
            foreach (var (type, playable) in new[] { ("Gesture", "gesture"), ("Action", "action") })
            {
                var runtime = Playable(descriptor, type);
                if (!((runtime is AnimatorOverrideController overridden ? overridden.runtimeAnimatorController : runtime) is AnimatorController other)) continue;
                capture.controllerParameters = new Dictionary<string, string>(StringComparer.Ordinal);
                capture.overrides = ExpressionDependencies.Overrides(runtime);
                capture.ReadControllerParameters(other);
                capture.data.Controllers.Add(capture.ReadController(other, playable));
            }
            capture.controllerParameters = fxParameters;
            capture.overrides = fxOverrides;
            // Menus name only parameters that Expression Parameters declare.
            if (expressions) capture.ReadMenus(Member(descriptor, "expressionsMenu"));
            return capture;
        }

        // Whether an FX curve turns this renderer on and off: an enabled target
        // when the renderer is also a mesh in the file (see Resolve).
        internal bool Switches(Renderer renderer) =>
            renderer != null && rendererIndex.ContainsKey(renderer) && renderer.enabled && renderer.gameObject.activeInHierarchy;

        // Matches the faces to the clips read above. The faces were read from
        // the build's copy before the optimizer, which may replace a clip
        // again: a face matches a clip that is its clip, the clip it was copied
        // from (sourceOf), the build's copy of either (isolatedCopyOf), the clip
        // an override controller replaces, or a clip the build records as made
        // from any of these (registeredOriginal). Returns this capture.
        internal AvatarAnimatorCapture ReadFaces(IEnumerable<VrmMenuExpressions.Expression> expressions,
            Func<UnityEngine.Object, UnityEngine.Object> sourceOf = null, Func<UnityEngine.Object, UnityEngine.Object> isolatedCopyOf = null,
            Func<UnityEngine.Object, UnityEngine.Object> registeredOriginal = null)
        {
            faces.Clear();
            var clipsOf = new Dictionary<UnityEngine.Object, HashSet<int>>();
            void Add(UnityEngine.Object value, int index)
            {
                // A chain of copies, each recorded as made from the one before.
                for (var step = 0; value != null && step < 8; step++)
                {
                    if (!clipsOf.TryGetValue(value, out var set)) clipsOf[value] = set = new HashSet<int>();
                    if (!set.Add(index)) return;
                    value = registeredOriginal?.Invoke(value);
                }
            }
            foreach (var pair in clipIndex) Add(pair.Key, pair.Value);
            foreach (var pair in overrides)
                if (pair.Value != null && clipIndex.TryGetValue(pair.Value, out var replaced)) Add(pair.Key, replaced);
            foreach (var face in expressions ?? Enumerable.Empty<VrmMenuExpressions.Expression>())
            {
                if (face?.Clip == null) continue;
                var found = new SortedSet<int>();
                var source = sourceOf?.Invoke(face.Clip);
                foreach (var value in new[] { face.Clip, source, isolatedCopyOf?.Invoke(face.Clip), source == null ? null : isolatedCopyOf?.Invoke(source) })
                    if (value != null && clipsOf.TryGetValue(value, out var set)) found.UnionWith(set);
                if (found.Count > 0) faces.Add((face, found));
            }
            return this;
        }

        // The faces of the file's custom expressions, by registered name, with
        // the clips each was read from. A face written as an identical face's
        // name adds its clips to that name. Read when the file is written, after
        // the names are registered.
        internal IReadOnlyList<(string Name, IReadOnlyCollection<int> Clips)> FaceClips
        {
            get
            {
                var result = new List<(string Name, IReadOnlyCollection<int> Clips)>();
                var byName = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
                foreach (var (face, clips) in faces)
                {
                    var name = face.RegisteredName ?? face.MergedInto;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!byName.TryGetValue(name, out var set)) { byName[name] = set = new SortedSet<int>(); result.Add((name, set)); }
                    set.UnionWith(clips);
                }
                return result;
            }
        }

        // The ways into the states that play one of the clips: the layer's
        // first state, and the entry, Any State and state transitions that go
        // straight to such a state. A transition to a machine is not followed.
        // The same way with the same conditions is listed once.
        internal static List<AvatarAnimatorData.Entrance> Entrances(AvatarAnimatorData data, ICollection<int> clips)
        {
            var result = new List<AvatarAnimatorData.Entrance>();
            if (data.Controllers.Count == 0) return result;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var layers = data.Controllers[0].Layers;
            for (var l = 0; l < layers.Count; l++)
            {
                var layer = layers[l];
                var playing = new HashSet<int>();
                for (var s = 0; s < layer.States.Count; s++)
                    if (AvatarAnimatorData.Plays(data, layer.States[s].Motion, clips)) playing.Add(s);
                if (playing.Count == 0) continue;
                void Add(int state, string from, int machine, int fromState, IEnumerable<AvatarAnimatorData.Condition> conditions)
                {
                    var entrance = new AvatarAnimatorData.Entrance { Layer = l, State = state, From = from, Machine = machine, FromState = fromState };
                    entrance.Conditions.AddRange(conditions);
                    var key = string.Join("|", new[] { l, state, machine, fromState }.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "|" + from + "|"
                        + string.Join(";", entrance.Conditions.Select(c => c.Parameter.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + c.Mode + ","
                            + c.Threshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                    if (seen.Add(key)) result.Add(entrance);
                }
                var start = layer.Machines[0].DefaultState;
                if (playing.Contains(start)) Add(start, "default", -1, -1, Enumerable.Empty<AvatarAnimatorData.Condition>());
                for (var m = 0; m < layer.Machines.Count; m++)
                {
                    foreach (var transition in layer.Machines[m].EntryTransitions)
                        if (transition.ToState is int to && playing.Contains(to)) Add(to, "entry", m, -1, transition.Conditions);
                    foreach (var transition in layer.Machines[m].AnyStateTransitions)
                        if (transition.ToState is int to && playing.Contains(to)) Add(to, "any", -1, -1, transition.Conditions);
                }
                for (var s = 0; s < layer.States.Count; s++)
                    foreach (var transition in layer.States[s].Transitions)
                        if (transition.ToState is int to && playing.Contains(to)) Add(to, "state", -1, s, transition.Conditions);
            }
            return result;
        }

        // A shape as the export bakes it: the weight in the scene, the weight
        // the base geometry holds and the weight the target shows at 1. When
        // the base holds the shape at its end, the target shows nothing, and
        // the export adds a reverse shape that takes the base back to 0: its
        // output renderer and name.
        internal readonly struct MorphSource : IEquatable<MorphSource>
        {
            internal readonly double Rest, Baked, End;
            internal readonly SkinnedMeshRenderer ReverseRenderer;
            internal readonly string ReverseShape;
            internal MorphSource(double rest, double baked, double end, SkinnedMeshRenderer reverseRenderer = null, string reverseShape = null)
            { Rest = rest; Baked = baked; End = end; ReverseRenderer = reverseRenderer; ReverseShape = reverseShape; }
            public bool Equals(MorphSource other) => Rest == other.Rest && Baked == other.Baked && End == other.End &&
                ReverseRenderer == other.ReverseRenderer && ReverseShape == other.ReverseShape;
            public override bool Equals(object obj) => obj is MorphSource other && Equals(other);
            public override int GetHashCode() => (Rest, Baked, End, ReverseShape).GetHashCode();
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
            RemoveMorphTargets(new HashSet<AvatarAnimatorData.Target>(morphBinds.Where(bind => bind.Soft && bind.Target.Morph < 0).Select(bind => bind.Target)), false);
        }

        // Leaves out the morph targets the file cannot carry: the node has no
        // mesh, or its mesh has no such morph target, as when the exporter
        // writes no geometry for a renderer that is off or empty. Their tracks
        // are counted as missingNode. Run on the bytes before Strip.
        internal void DropMorphsNotIn(byte[] bytes)
        {
            if (!morphBinds.Any()) return;
            var document = GlbDocument.Read(bytes);
            var nodes = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> list ? list : new List<object>();
            var meshes = document.Json.TryGetValue("meshes", out var rawMeshes) && rawMeshes is List<object> meshList ? meshList : new List<object>();
            RemoveMorphTargets(new HashSet<AvatarAnimatorData.Target>(morphBinds.Select(bind => bind.Target).Where(target => !Fits(target, nodes, meshes))), true);
        }

        static bool Fits(AvatarAnimatorData.Target target, List<object> nodes, List<object> meshes) =>
            target.Node >= 0 && target.Node < nodes.Count && nodes[target.Node] is Dictionary<string, object> node &&
            node.TryGetValue("mesh", out var rawMesh) && Convert.ToInt32(rawMesh) is var mesh && mesh >= 0 && mesh < meshes.Count &&
            target.Morph >= 0 && target.Morph < MorphCount(meshes[mesh] as Dictionary<string, object>);

        void RemoveMorphTargets(HashSet<AvatarAnimatorData.Target> gone, bool count)
        {
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
            var removed = 0;
            foreach (var clip in data.Clips)
            {
                removed += clip.Tracks.RemoveAll(track => map[track.Target] < 0);
                foreach (var track in clip.Tracks) track.Target = map[track.Target];
            }
            if (!count || removed == 0) return;
            var missing = data.Omitted.FirstOrDefault(omission => omission.Kind == "missingNode");
            if (missing != null) missing.Count += removed;
            else
            {
                // Omitted stays in OmittedKinds order.
                data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "missingNode", Count = removed });
                var order = AvatarAnimatorData.OmittedKinds;
                data.Omitted.Sort((a, b) => Array.IndexOf(order, a.Kind).CompareTo(Array.IndexOf(order, b.Kind)));
            }
        }

        // The data with glTF node indices. A target the file has no node for
        // is left out with its curves, which are counted as missingNode.
        // sources gives the shapes an output shape was made from; without one,
        // or when they disagree, its curves are counted as blendShape. When the
        // shapes would take the extension past MaximumBytes, all of them are.
        // material gives the glTF index of a material the file writes, or -1;
        // without it, material curves are counted as material.
        // constraint gives the VRVLOG_avatar_constraints entry of a
        // constraint component, or -1; without it, or for -1, constraint
        // enabled curves are counted as enabled.
        internal AvatarAnimatorData Resolve(Func<GameObject, int> node, Func<SkinnedMeshRenderer, string, IReadOnlyList<MorphSource>> sources = null,
            Func<Material, int> material = null, Func<Component, int> constraint = null)
        {
            Build(node, sources, material, constraint, true);
            if (morphBinds.Count > 0 && Encoding.UTF8.GetByteCount(JsonDom.Serialize(AvatarAnimatorData.Write(data))) > MaximumBytes)
                Build(node, sources, material, constraint, false);
            return data;
        }

        const int MissingMorph = -1, OmittedMorph = -2;

        void Build(Func<GameObject, int> node, Func<SkinnedMeshRenderer, string, IReadOnlyList<MorphSource>> sources, Func<Material, int> material,
            Func<Component, int> constraint, bool writeMorphs)
        {
            data.Targets.Clear();
            morphBinds.Clear();
            shownBy.Clear();
            var map = new int[targets.Count];
            for (var i = 0; i < targets.Count; i++)
            {
                var index = targets[i].Object == null ? -1 : node(targets[i].Object);
                map[i] = index < 0 ? -1 : data.Targets.Count;
                if (index < 0) continue;
                var target = new AvatarAnimatorData.Target { Node = index, Property = "active", Rest = targets[i].Default };
                data.Targets.Add(target);
                shownBy[target] = new AvatarAnimatorCrossCheck.Binding { Object = targets[i].Object, Path = PathAtRead(targets[i].Object) };
            }
            var morphMap = new int[morphs.Count];
            for (var i = 0; i < morphs.Count; i++) morphMap[i] = MorphTarget(i, node, sources, writeMorphs);
            // Every parameter a clip animates is a target, with or without a
            // track: Unity holds it at the controller's default when no layer
            // writes it, and holds an int, bool or trigger there always. The
            // node targets (active, then morph) come first.
            var parameterTarget = new Dictionary<int, int>();
            foreach (var parameter in driven)
            {
                parameterTarget[parameter] = data.Targets.Count;
                data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "parameter", Parameter = parameter, Rest = controllerDefaults[parameter] });
            }
            // Then the renderer enabled targets, then the constraint enabled
            // targets, then the material targets.
            // A renderer that is off or under an inactive object when the
            // file is written is not a mesh there (ModelExporter), so nothing
            // shows it: its curves are counted as enabled (switchedOff).
            var rendererMap = new int[renderers.Count];
            var switchedOff = new bool[renderers.Count];
            for (var i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i].Renderer;
                var index = renderer == null ? -1 : node(renderer.gameObject);
                switchedOff[i] = index >= 0 && !(renderer.enabled && renderer.gameObject.activeInHierarchy);
                rendererMap[i] = index < 0 || switchedOff[i] ? -1 : data.Targets.Count;
                if (rendererMap[i] < 0) continue;
                var target = new AvatarAnimatorData.Target { Node = index, Property = "enabled", Component = "renderer", Rest = renderers[i].Default };
                data.Targets.Add(target);
                shownBy[target] = new AvatarAnimatorCrossCheck.Binding { Component = renderer, Path = PathAtRead(renderer.gameObject) };
            }
            // Then the constraint enabled targets: component "constraint", the
            // node the constraint moves and index its entry. A reader that
            // does not know the component keeps them unwritten (Target.Known).
            // A constraint the extension does not write (converted to VRM or
            // left out) has no entry: its curves are counted as enabled.
            var constraintMap = new int[constraints.Count];
            for (var i = 0; i < constraints.Count; i++)
            {
                var entry = constraints[i].Constraint == null || constraint == null ? -1 : constraint(constraints[i].Constraint);
                var entryNode = entry < 0 ? -1 : ConstraintNode(constraints[i].Constraint, node);
                constraintMap[i] = entryNode < 0 ? -1 : data.Targets.Count;
                if (constraintMap[i] < 0) continue;
                data.Targets.Add(new AvatarAnimatorData.Target
                {
                    Node = -1, Property = "enabled", Component = "constraint", Rest = constraints[i].Default,
                    Fields = new Dictionary<string, object>
                    {
                        ["node"] = (long)entryNode, ["property"] = "enabled", ["component"] = "constraint", ["index"] = (long)entry, ["rest"] = (long)constraints[i].Default
                    }
                });
            }
            // Material curves take the track room the other kinds leave.
            var room = new int[data.Clips.Count];
            var constraintTracks = new int[data.Clips.Count];
            for (var i = 0; i < data.Clips.Count; i++)
            {
                room[i] = AvatarAnimatorData.MaximumTracksPerClip - clipTracks[i].Count(t => map[t.Target] >= 0) - clipMorphTracks[i].Count(t => morphMap[t.Morph] >= 0) -
                          clipParameterTracks[i].Count - clipEnabledTracks[i].Count(t => rendererMap[t.Renderer] >= 0);
                constraintTracks[i] = clipConstraintTracks[i].Count(t => constraintMap[t.Constraint] >= 0);
            }
            var (floatMap, channelMap) = MaterialTargets(node, material, room, constraintTracks, out var materialMissing, out var materialLost);
            var slotMap = SlotTargets(node, material, out var slotMissing, out var slotLost, out var slotShader);
            var transformMap = TransformTargets(node, out var mixedRotation);
            // Then the muscles a clip of a layer that is not additive moves,
            // by index, each at the T-pose's 0.
            var muscleTarget = new Dictionary<int, int>();
            foreach (var muscle in clipMuscleTracks.Where((_, clip) => !additiveClips.Contains(clip)).SelectMany(tracks => tracks.Select(track => track.Muscle)).Distinct().OrderBy(m => m))
            {
                muscleTarget[muscle] = data.Targets.Count;
                data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "muscle", Muscle = muscle, Rest = 0 });
            }
            // A mask names the nodes of the objects its transform mask lets move.
            foreach (var (mask, paths) in masks)
                mask.Nodes = paths?.Select(path => path.Length == 0 ? root.transform : root.transform.Find(path))
                    .Select(found => found == null ? -1 : node(found.gameObject)).Where(index => index >= 0).Distinct().OrderBy(index => index).ToList();
            var muscleLost = 0;
            var missing = materialMissing + slotMissing;
            var transformLost = 0;
            var mixed = 0;
            var lost = 0;
            var off = 0;
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
                foreach (var (parameter, curve) in clipParameterTracks[i].OrderBy(t => t.Parameter))
                {
                    var track = new AvatarAnimatorData.Track { Target = parameterTarget[parameter] };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
                foreach (var (renderer, curve) in clipEnabledTracks[i])
                {
                    if (switchedOff[renderer]) { off++; continue; }
                    if (rendererMap[renderer] < 0) { missing++; continue; }
                    var track = new AvatarAnimatorData.Track { Target = rendererMap[renderer] };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
                foreach (var (target, curve) in floatMap[i])
                {
                    var track = new AvatarAnimatorData.Track { Target = target };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
                // Constraint enabled curves come after the material floats and
                // take the room they leave, so they never leave out a track the
                // file wrote before the extension.
                foreach (var (index, curve) in clipConstraintTracks[i])
                {
                    if (constraintMap[index] < 0 || data.Clips[i].Tracks.Count >= AvatarAnimatorData.MaximumTracksPerClip) { off++; continue; }
                    var track = new AvatarAnimatorData.Track { Target = constraintMap[index] };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
                // The color and texture channels, added after them, come next.
                foreach (var (target, curve) in channelMap[i])
                {
                    var track = new AvatarAnimatorData.Track { Target = target };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
                // The material slots, added after them, come last.
                foreach (var (target, curve) in slotMap[i])
                {
                    if (data.Clips[i].Tracks.Count >= AvatarAnimatorData.MaximumTracksPerClip) { slotLost++; continue; }
                    var track = new AvatarAnimatorData.Track { Target = target };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
                // The Transform channels, added after them, take the room left.
                foreach (var (index, curve) in clipTransformTracks[i])
                {
                    // Unity adds an additive layer's Transform curves to the pose,
                    // which a player blending floats does not do.
                    if (additiveClips.Contains(i)) { transformLost++; continue; }
                    if (mixedRotation[index]) { mixed++; continue; }
                    if (transformMap[index] < 0) { missing++; continue; }
                    if (data.Clips[i].Tracks.Count >= AvatarAnimatorData.MaximumTracksPerClip) { transformLost++; continue; }
                    var track = new AvatarAnimatorData.Track { Target = transformMap[index] };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
                // The muscles, added after them, take the room left. Unity adds
                // an additive layer's muscles to the pose, which a player
                // blending floats does not do.
                foreach (var (muscle, curve) in clipMuscleTracks[i])
                {
                    if (additiveClips.Contains(i) || data.Clips[i].Tracks.Count >= AvatarAnimatorData.MaximumTracksPerClip) { muscleLost++; continue; }
                    var track = new AvatarAnimatorData.Track { Target = muscleTarget[muscle] };
                    track.Curves.Add(curve);
                    data.Clips[i].Tracks.Add(track);
                }
            }
            data.Omitted.Clear();
            foreach (var kind in AvatarAnimatorData.OmittedKinds)
            {
                var count = (omitted.TryGetValue(kind, out var value) ? value : 0) + (kind == "missingNode" ? missing : 0) + (kind == "blendShape" ? lost : 0) +
                            (kind == "material" ? materialLost + slotLost : 0) + (kind == "enabled" ? off : 0) + (kind == "materialSlotShader" ? slotShader : 0) +
                            (kind == "transform" ? transformLost : 0) + (kind == "rotationMixed" ? mixed : 0) + (kind == "muscle" ? muscleLost : 0);
                if (count > 0) data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = kind, Count = count });
            }
        }

        // Adds the translation, rotation and scale targets that a clip of a
        // layer that is not additive moves, ordered by kind, node and channel,
        // and returns each Transform curve's target, or -1
        // when the file has no node for its object. Rest is the object's local
        // value when the file is written, which its glTF node holds; that of
        // an Euler rotation is the angles the inspector shows, those Unity's
        // Animator starts the channels a clip leaves from. A node that Euler
        // and quaternion rotation curves both move, in any clips of the
        // controller, has its rotation curves left out: mixed is true for
        // them, and they are counted as rotationMixed.
        int[] TransformTargets(Func<GameObject, int> node, out bool[] mixed)
        {
            var map = new int[transforms.Count];
            mixed = new bool[transforms.Count];
            var eulerOf = new Dictionary<GameObject, bool>();
            var mixedObjects = new HashSet<GameObject>();
            var played = new HashSet<int>(clipTransformTracks.Where((_, clip) => !additiveClips.Contains(clip)).SelectMany(tracks => tracks.Select(track => track.Item1)));
            foreach (var i in played)
            {
                var (gameObject, moves) = transforms[i];
                if (moves.Property != "rotation" || gameObject == null) continue;
                if (eulerOf.TryGetValue(gameObject, out var euler) && euler != moves.Euler) mixedObjects.Add(gameObject);
                eulerOf[gameObject] = moves.Euler;
            }
            var order = new List<(int Index, int Node)>();
            for (var i = 0; i < transforms.Count; i++)
            {
                var (gameObject, moves) = transforms[i];
                mixed[i] = moves.Property == "rotation" && mixedObjects.Contains(gameObject);
                var index = gameObject == null || mixed[i] || !played.Contains(i) ? -1 : node(gameObject);
                map[i] = -1;
                if (index >= 0) order.Add((i, index));
            }
            int Channel(string property, string channel) =>
                Array.IndexOf(property == "rotation" ? AvatarAnimatorData.QuaternionChannels : AvatarAnimatorData.VectorChannels, channel);
            foreach (var (i, index) in order.OrderBy(entry => Array.IndexOf(AvatarAnimatorData.Properties, transforms[entry.Index].Moves.Property)).ThenBy(entry => entry.Node)
                         .ThenBy(entry => Channel(transforms[entry.Index].Moves.Property, transforms[entry.Index].Moves.Channel)))
            {
                var (gameObject, moves) = transforms[i];
                map[i] = data.Targets.Count;
                var target = new AvatarAnimatorData.Target
                {
                    Node = index, Property = moves.Property, Channel = moves.Channel, Euler = moves.Euler, Rest = TransformValue(gameObject.transform, moves)
                };
                data.Targets.Add(target);
                shownBy[target] = new AvatarAnimatorCrossCheck.Binding { Object = gameObject, Path = PathAtRead(gameObject) };
            }
            return map;
        }

        // The local value of a Transform channel, in Unity's space.
        static double TransformValue(Transform transform, (string Property, string Channel, bool Euler) moves)
        {
            var vector = moves.Property == "translation" ? transform.localPosition : moves.Property == "scale" ? transform.localScale
                : moves.Euler ? TransformUtils.GetInspectorRotation(transform) : Vector3.zero;
            if (moves.Property == "rotation" && !moves.Euler)
            {
                var rotation = transform.localRotation;
                return new[] { rotation.x, rotation.y, rotation.z, rotation.w }[Array.IndexOf(AvatarAnimatorData.QuaternionChannels, moves.Channel)];
            }
            return vector[Array.IndexOf(AvatarAnimatorData.VectorChannels, moves.Channel)];
        }

        // Adds the material targets, the materialFloat ones first, each kind
        // ordered by material, name and channel, and returns each clip's
        // tracks on them: those of the materialFloat targets, and those of the
        // materialColor and materialTextureST ones. Unity moves a renderer's
        // material property on every material of the renderer, so a curve
        // becomes a track of each material the renderer draws with that the
        // file writes and that has the property; renderers that share a
        // material share its target. A curve on a renderer the file has no
        // node for is counted as missingNode (missing). One that reaches no
        // target is counted as material (lost): no material has the
        // property, the file writes none of them, a clip of an additive layer
        // moves the target (its reference pose is not carried), or another
        // curve of the same clip moved each of its targets first.
        // Material floats take the room the other kinds leave; the color and
        // texture channels, added later, the room the constraint tracks
        // (constraintTracks for each clip) leave after them, so they never
        // leave out a track the file wrote before.
        (List<List<(int Target, ExpressionAnimationData.Curve Curve)>> Floats, List<List<(int Target, ExpressionAnimationData.Curve Curve)>> Channels) MaterialTargets(
            Func<GameObject, int> node, Func<Material, int> material, int[] room, int[] constraintTracks, out int missing, out int lost)
        {
            var missed = 0;
            var left = 0;
            var first = new Dictionary<(int, (string, string, string)), (Renderer Renderer, Material Material, float Value)>();
            var additive = new HashSet<(int, (string, string, string))>();
            List<List<((int, (string, string, string)) Key, ExpressionAnimationData.Curve Curve)>> Expand(bool floats, int[] free)
            {
                var found = new List<List<((int, (string, string, string)), ExpressionAnimationData.Curve)>>();
                for (var i = 0; i < clipMaterialTracks.Count; i++)
                {
                    var tracks = new List<((int, (string, string, string)), ExpressionAnimationData.Curve)>();
                    found.Add(tracks);
                    var taken = new HashSet<(int, (string, string, string))>();
                    foreach (var (renderer, moves, curve) in clipMaterialTracks[i])
                    {
                        if ((moves.Property == "materialFloat") != floats) continue;
                        if (renderer == null || node(renderer.gameObject) < 0) { missed++; continue; }
                        // Whether a slot has the property, and its rest, come from
                        // the material read with the curve; the index from the
                        // material the slot holds now, which the file writes.
                        var read = slotsAtRead[(renderer, moves)];
                        var now = renderer.sharedMaterials;
                        var keys = new List<((int, (string, string, string)) Key, int Slot)>();
                        for (var s = 0; s < read.Length; s++)
                        {
                            var index = !read[s].Has || material == null || s >= now.Length || now[s] == null ? -1 : material(now[s]);
                            if (index >= 0 && !taken.Contains((index, moves)) && !keys.Any(k => k.Key == (index, moves))) keys.Add(((index, moves), s));
                        }
                        // A curve that would take the clip past its track room is
                        // left out with all its tracks.
                        if (keys.Count == 0 || tracks.Count + keys.Count > free[i]) { left++; continue; }
                        foreach (var (key, slot) in keys)
                        {
                            taken.Add(key);
                            tracks.Add((key, curve));
                            if (!first.ContainsKey(key)) first[key] = (renderer, read[slot].Material, read[slot].Value);
                            if (additiveClips.Contains(i)) additive.Add(key);
                        }
                    }
                }
                return found;
            }
            var floatFound = Expand(true, room);
            var later = new int[room.Length];
            for (var i = 0; i < room.Length; i++)
            {
                var free = room[i] - floatFound[i].Count;
                later[i] = free - Math.Min(constraintTracks[i], free);
            }
            var channelFound = Expand(false, later);
            var targetOf = new Dictionary<(int, (string, string, string)), int>();
            foreach (var key in first.Keys.Where(key => !additive.Contains(key)).OrderBy(key => Array.IndexOf(MaterialProperties, key.Item2.Item1)).ThenBy(key => key.Item1)
                         .ThenBy(key => key.Item2.Item2, StringComparer.Ordinal).ThenBy(key => key.Item2.Item3, StringComparer.Ordinal))
            {
                var (renderer, slot, value) = first[key];
                var (property, name, channel) = key.Item2;
                var target = new AvatarAnimatorData.Target { Node = -1, Property = property, Material = key.Item1, Name = name, Channel = channel, Rest = value };
                targetOf[key] = data.Targets.Count;
                data.Targets.Add(target);
                shownBy[target] = new AvatarAnimatorCrossCheck.Binding { Component = renderer, Material = slot, Name = name, Path = PathAtRead(renderer.gameObject) };
            }
            List<List<(int, ExpressionAnimationData.Curve)>> Tracks(List<List<((int, (string, string, string)) Key, ExpressionAnimationData.Curve Curve)>> found)
            {
                var result = new List<List<(int, ExpressionAnimationData.Curve)>>();
                for (var i = 0; i < found.Count; i++)
                {
                    var tracks = new List<(int, ExpressionAnimationData.Curve)>();
                    result.Add(tracks);
                    // A curve whose every track went is counted once.
                    var curves = new HashSet<ExpressionAnimationData.Curve>();
                    var kept = new HashSet<ExpressionAnimationData.Curve>();
                    foreach (var (key, curve) in found[i])
                    {
                        curves.Add(curve);
                        if (!targetOf.TryGetValue(key, out var target)) continue;
                        kept.Add(curve);
                        tracks.Add((target, curve));
                    }
                    left += curves.Count(curve => !kept.Contains(curve));
                    tracks.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                }
                return result;
            }
            var floatTracks = Tracks(floatFound);
            var channelTracks = Tracks(channelFound);
            missing = missed;
            lost = left;
            return (floatTracks, channelTracks);
        }

        // Adds the materialSlot targets, ordered by node and slot, and returns
        // each clip's tracks on them, each key the glTF index of the material
        // it sets (-1 for none). A slot curve names the material, which the
        // file writes as the material of each slot that held it when the
        // curves were read, or else as the copy the export added at the end of
        // the glTF materials (AddedMaterials). One on a renderer the file has no mesh for, or
        // whose slot it dropped, is counted as missingNode (missing). One
        // that sets a material the file does not write, or that a clip of an
        // additive layer plays, is counted as material (lost), and one that
        // sets a material of a shader other than official lilToon as
        // materialSlotShader (shader): the app shows a swapped slot with
        // VRVLOG_materials_liltoon or VRVLOG_materials_liltoon_unbound.
        List<List<(int Target, ExpressionAnimationData.Curve Curve)>> SlotTargets(Func<GameObject, int> node, Func<Material, int> material,
            out int missing, out int lost, out int shader)
        {
            missing = lost = shader = 0;
            var result = new List<List<(int, ExpressionAnimationData.Curve)>>();
            var found = new List<(int Clip, (int Node, int Slot) Key, ExpressionAnimationData.Curve Curve)>();
            var rests = new Dictionary<(int, int), (Renderer Renderer, int Material)>();
            // The glTF index of each material a slot holds or a curve sets, for the check.
            var held = new Dictionary<(int, int), Dictionary<Material, int>>();
            // A slot that moved down with a dropped empty submesh: the copy's
            // Animator writes the curve's own slot, so the check leaves it out.
            var renumbered = new HashSet<(int, int)>();
            for (var i = 0; i < clipSlotTracks.Count; i++)
            {
                result.Add(new List<(int, ExpressionAnimationData.Curve)>());
                foreach (var (renderer, slot, frames) in clipSlotTracks[i])
                {
                    var nodeIndex = renderer == null ? -1 : node(renderer.gameObject);
                    var now = nodeIndex < 0 || !rendererSlotsAtRead.TryGetValue(renderer, out var slots) ? -1 : Array.FindIndex(slots, each => each.Slot == slot);
                    // A renderer that is off when the file is written is no mesh there.
                    if (now < 0 || !(renderer.enabled && renderer.gameObject.activeInHierarchy) || now >= SubmeshCount(renderer) || now >= renderer.sharedMaterials.Length) { missing++; continue; }
                    if (frames.Any(frame => frame.value is Material swapped && !IsOfficialLilToon(swapped)))
                    {
                        shader++;
                        continue;
                    }
                    var curve = new ExpressionAnimationData.Curve();
                    foreach (var frame in frames)
                        curve.Keys.Add(new ExpressionAnimationData.Key { Time = frame.time, Value = SlotMaterial(frame.value as Material, node, material) });
                    if (additiveClips.Contains(i) || curve.Keys.Any(key => key.Value < -1) || !Valid(curve)) { lost++; continue; }
                    var holds = renderer.sharedMaterials[now];
                    var rest = holds == null || material == null ? -1 : material(holds);
                    // -1 is an empty slot: a held material the file does not write is no rest.
                    if (holds != null && rest < 0) { lost++; continue; }
                    rests[(nodeIndex, now)] = (renderer, rest);
                    found.Add((i, (nodeIndex, now), curve));
                    if (slot != now) renumbered.Add((nodeIndex, now));
                    if (!held.TryGetValue((nodeIndex, now), out var materials)) held[(nodeIndex, now)] = materials = new Dictionary<Material, int>();
                    if (holds != null) materials[holds] = rest;
                    for (var k = 0; k < frames.Length; k++)
                        if (frames[k].value is Material set) materials[set] = (int)curve.Keys[k].Value;
                }
            }
            var targetOf = new Dictionary<(int, int), int>();
            foreach (var key in rests.Keys.OrderBy(key => key.Item1).ThenBy(key => key.Item2))
            {
                var target = new AvatarAnimatorData.Target { Node = key.Item1, Property = "materialSlot", Slot = key.Item2, Rest = rests[key].Material };
                targetOf[key] = data.Targets.Count;
                data.Targets.Add(target);
                var renderer = rests[key].Renderer;
                if (!renumbered.Contains(key))
                    shownBy[target] = new AvatarAnimatorCrossCheck.Binding { Component = renderer, Slot = key.Item2, Slots = held[key], Path = PathAtRead(renderer.gameObject) };
            }
            foreach (var (clip, key, curve) in found) result[clip].Add((targetOf[key], curve));
            foreach (var tracks in result) tracks.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return result;
        }

        // The glTF index of a material a slot curve sets: that of a slot that
        // held it when the curves were read, else that of its added copy; -1
        // for none, -2 when the file writes neither.
        int SlotMaterial(Material swapped, Func<GameObject, int> node, Func<Material, int> material)
        {
            if (swapped == null) return -1;
            if (material == null) return -2;
            foreach (var pair in rendererSlotsAtRead)
            {
                var renderer = pair.Key;
                if (renderer == null || node(renderer.gameObject) < 0) continue;
                var now = renderer.sharedMaterials;
                for (var s = 0; s < pair.Value.Length && s < now.Length; s++)
                {
                    if (!Holds(pair.Value[s].Material, swapped) || now[s] == null) continue;
                    var index = material(now[s]);
                    if (index >= 0) return index;
                }
            }
            // A material no slot held is written at the end of the glTF
            // materials as the copy the export made of it (UnboundSwaps).
            if (AddedMaterials != null && AddedMaterials.TryGetValue(swapped, out var added) && added != null)
            {
                var index = material(added);
                if (index >= 0) return index;
            }
            return -2;
        }

        // The official lilToon materials that slot curves on the renderers the
        // file writes as meshes (shown) set and that no slot of those held when
        // the curves were read, in the order the curves name them. No mesh
        // draws with them, so the export adds a copy of each at the end of the
        // glTF materials and gives the copies back in AddedMaterials;
        // VRVLOG_materials_liltoon_unbound carries their lilToon records.
        // Curves an additive layer plays, and curves that also set another
        // shader's material, are left out as SlotTargets leaves them out.
        internal IReadOnlyList<Material> UnboundSwaps(IEnumerable<Renderer> shown)
        {
            var result = new List<Material>();
            if (rendererSlotsAtRead == null || shown == null) return result;
            var written = new HashSet<Renderer>(shown.Where(renderer => renderer != null));
            var held = new HashSet<Material>(rendererSlotsAtRead.Where(pair => written.Contains(pair.Key))
                .SelectMany(pair => pair.Value.Select(each => each.Material)).Where(each => each != null)
                .SelectMany(each => new[] { each, SourceOf?.Invoke(each) }).Where(each => each != null));
            for (var i = 0; i < clipSlotTracks.Count; i++)
            {
                if (additiveClips.Contains(i)) continue;
                foreach (var (renderer, _, frames) in clipSlotTracks[i])
                {
                    if (renderer == null || !written.Contains(renderer)) continue;
                    if (frames.Any(frame => frame.value is Material swapped && !IsOfficialLilToon(swapped))) continue;
                    foreach (var frame in frames)
                        if (frame.value is Material swapped && !held.Contains(swapped) && !result.Contains(swapped)) result.Add(swapped);
                }
            }
            return result;
        }

        // The export copies materials before the curves are read (AudioLink
        // off, recovery); the curves still name the original. SourceOf gives
        // the original of such a copy; null leaves each material as it is.
        internal Func<Material, Material> SourceOf { get; set; }

        bool Holds(Material held, Material swapped) =>
            held == swapped || held != null && SourceOf != null && SourceOf(held) == swapped;

        static bool IsOfficialLilToon(Material material) => material.shader != null && LilToon234Catalogue.Shaders.ContainsKey(material.shader.name);

        // The output copy of each material UnboundSwaps returned, set by the
        // export before Resolve; null when it adds none.
        internal IReadOnlyDictionary<Material, Material> AddedMaterials { get; set; }

        static int SubmeshCount(Renderer renderer)
        {
            var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            return mesh == null ? 0 : mesh.subMeshCount;
        }

        static bool Valid(ExpressionAnimationData.Curve curve)
        {
            try { curve.Validate(); }
            catch (InvalidOperationException) { return false; }
            return true;
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
            // A target that shows no change cannot carry the weight: the
            // reverse shape does, from the base (baked) to 0 (end).
            var end = source.End;
            if (source.Baked == source.End)
            {
                var reverse = source.ReverseRenderer;
                nodeIndex = reverse == null || reverse.sharedMesh == null ? -1 : node(reverse.gameObject);
                morph = nodeIndex < 0 ? -1 : reverse.sharedMesh.GetBlendShapeIndex(source.ReverseShape);
                if (morph < 0) return OmittedMorph;
                end = 0;
            }
            var target = new AvatarAnimatorData.Target
            {
                Node = nodeIndex, Property = "morph", Morph = morph, Rest = source.Rest, Baked = source.Baked, End = end,
            };
            // Curves that only hold rest on a base that already shows it change
            // nothing by themselves: the target stays only if its shape does.
            var soft = source.Baked == source.Rest &&
                       clipMorphTracks.All(tracks => tracks.All(track => track.Morph != i || Holds(track.Curve, source.Rest)));
            morphBinds.Add((target, new Dictionary<string, object> { ["node"] = (long)nodeIndex, ["index"] = (long)morph }, soft));
            shownBy[target] = new AvatarAnimatorCrossCheck.Binding { Renderer = renderer, Shape = shape, Path = PathAtRead(renderer.gameObject) };
            data.Targets.Add(target);
            return data.Targets.Count - 1;
        }

        static bool Holds(ExpressionAnimationData.Curve curve, double value) =>
            curve.Keys.All(key => key.Value == value && (key.InTangent ?? 0) == 0 && (key.OutTangent ?? 0) == 0);

        // An extension over MaximumBytes is an error: the caller leaves the
        // stage out and lists it with this reason.
        // faces (FaceClips) lists the faces by name; those of the file's
        // custom expressions are written in the file's order, each with the
        // ways into the states that play its clips, when the evaluator can
        // show the face as its VRM expression does (FaceValues and
        // AvatarAnimatorCrossCheck.FaceMismatch). The others stay out, and
        // the app shows them with their VRM expression; each is logged.
        internal static byte[] Inject(byte[] bytes, AvatarAnimatorData data, IReadOnlyList<(string Name, IReadOnlyCollection<int> Clips)> faces = null)
        {
            if (data == null) return bytes;
            var document = GlbDocument.Read(bytes);
            data.Expressions.Clear();
            var custom = document.Find("extensions", "VRMC_vrm", "expressions", "custom");
            if (custom != null && faces != null)
                foreach (var name in custom.Keys)
                {
                    var clips = faces.FirstOrDefault(face => face.Name == name).Clips;
                    if (clips == null || clips.Count == 0) continue;
                    var expression = new AvatarAnimatorData.Expression { Name = name };
                    expression.Clips.AddRange(clips.OrderBy(clip => clip));
                    expression.When.AddRange(Entrances(data, new HashSet<int>(clips)));
                    data.Expressions.Add(expression);
                    string reason;
                    try
                    {
                        var values = FaceValues(document, data, name, out reason);
                        if (values != null) reason = AvatarAnimatorCrossCheck.FaceMismatch(data, name, values);
                    }
                    // A face the check cannot read stays out; the others are still written.
                    catch (Exception error) when (!(error is OutOfMemoryException)) { reason = error.GetType().Name + " " + error.Message; }
                    if (reason == null) continue;
                    data.Expressions.RemoveAt(data.Expressions.Count - 1);
                    Debug.Log(AvatarAnimatorCrossCheck.LogPrefix + $"face \"{name}\" left out: {reason}");
                }
            var extension = AvatarAnimatorData.Write(data);
            if (Encoding.UTF8.GetByteCount(JsonDom.Serialize(extension)) > MaximumBytes)
                throw new InvalidOperationException(TooLarge);
            var nodes = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> list ? list : new List<object>();
            var meshes = document.Json.TryGetValue("meshes", out var rawMeshes) && rawMeshes is List<object> meshList ? meshList : new List<object>();
            if (data.Targets.Any(target => target.Property != "parameter" && target.Node >= nodes.Count))
                throw new InvalidOperationException("Avatar animator target node is out of range.");
            // A morph target names one of its node's mesh's targets.
            if (data.Targets.Any(target => target.Property == "morph" && !Fits(target, nodes, meshes)))
                throw new InvalidOperationException("Avatar animator morph target is out of range.");
            // A material target names one of the file's materials.
            var materials = document.Json.TryGetValue("materials", out var rawMaterials) && rawMaterials is List<object> materialList ? materialList.Count : 0;
            if (data.Targets.Any(target => Array.IndexOf(MaterialProperties, target.Property) >= 0 && target.Material >= materials))
                throw new InvalidOperationException("Avatar animator material is out of range.");
            // So does each value of a materialSlot target, past -1 (none).
            var slotTargets = new HashSet<int>(Enumerable.Range(0, data.Targets.Count).Where(t => data.Targets[t].Stepped));
            if (data.Targets.Any(target => target.Stepped && target.Rest >= materials) ||
                data.Clips.SelectMany(clip => clip.Tracks).Where(track => slotTargets.Contains(track.Target))
                    .Any(track => track.Curves.Any(curve => curve.Keys.Any(key => key.Value >= materials))))
                throw new InvalidOperationException("Avatar animator material slot is out of range.");
            document.PlaceExtension(AvatarAnimatorData.Extension, extension);
            return document.Write();
        }

        // The value each target of the extension takes when the VRM custom
        // expression name shows (conditions A and C of the design for moving a
        // face to the evaluator), or null with the reason when it cannot be
        // told from the targets. Each of its morphTargetBinds is the morph
        // target on the same node and morph (the glTF weight as a Unity weight
        // with Baked and End, binds of one morph added up). A material color
        // or texture bind is not one: the menu's faces have none. In
        // VRVLOG_expression_animations, a BlendShape animation, or a bone or
        // visibility track that moves in time, is not one either (C); a track
        // held still is the value of the active target of its node, or each
        // channel the translation, rotation or scale target of its node and
        // channel, from glTF's space to Unity's (position -x, rotation -y and
        // -z), the rotation as Euler or quaternion as the target is.
        internal static List<(int Target, double Value)> FaceValues(GlbDocument document, AvatarAnimatorData data, string name, out string reason)
        {
            reason = null;
            var result = new Dictionary<int, double>();
            var custom = document.Find("extensions", "VRMC_vrm", "expressions", "custom", name);
            if (custom == null) { reason = "no expression"; return null; }
            foreach (var key in new[] { "materialColorBinds", "textureTransformBinds" })
                if (custom.TryGetValue(key, out var binds) && binds is List<object> list && list.Count > 0) { reason = key; return null; }
            if (custom.TryGetValue("morphTargetBinds", out var rawMorphs) && rawMorphs is List<object> morphs)
                foreach (var raw in morphs)
                {
                    if (!(raw is Dictionary<string, object> bind)) { reason = "morphTargetBinds"; return null; }
                    if (!bind.TryGetValue("node", out var rawNode) || !bind.TryGetValue("index", out var rawIndex)) { reason = "morphTargetBinds"; return null; }
                    var node = (int)Number(rawNode);
                    var index = (int)Number(rawIndex);
                    var weight = Number(bind.TryGetValue("weight", out var rawWeight) ? rawWeight : null);
                    var t = data.Targets.FindIndex(target => target.Property == "morph" && target.Node == node && target.Morph == index);
                    if (t < 0) { reason = $"morph {node}:{index} is no target"; return null; }
                    var target = data.Targets[t];
                    result[t] = (result.TryGetValue(t, out var sum) ? sum : target.Baked) + weight * (target.End - target.Baked);
                }
            var animations = document.Find("extensions", ExpressionAnimationData.Extension);
            if (animations != null)
            {
                ExpressionAnimationDocument read;
                List<ExpressionTransformAnimation> transforms;
                try
                {
                    read = ExpressionAnimationData.ReadDocument(animations);
                    transforms = ExpressionAnimationData.ReadTransforms(animations, read);
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    reason = ExpressionAnimationData.Extension + " unreadable";
                    return null;
                }
                if (read.Animations.Any(animation => animation.Expression == name)) { reason = "blendShape animation"; return null; }
                foreach (var animation in transforms.Where(animation => animation.Expression == name))
                    foreach (var track in animation.Tracks)
                    {
                        if (animation.Duration > 0 && track.Curves.Any(curve => curve.Keys.Any(key => key.Value != curve.Keys[0].Value))) { reason = "transform animation"; return null; }
                        if (!TrackValues(data, track, result, out reason)) return null;
                    }
            }
            return result.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)).ToList();
        }

        // The values of a track held still, by target; false with the reason
        // when a target is missing.
        static bool TrackValues(AvatarAnimatorData data, ExpressionTransformAnimation.Track track, Dictionary<int, double> result, out string reason)
        {
            reason = null;
            double Held(int component) => track.Curves[component].Keys.Count == 0 ? 0 : track.Curves[component].Keys[0].Value;
            int Find(string property, string channel, bool? euler) => data.Targets.FindIndex(target => target.Node == track.Node && target.Property == property &&
                                                                                         target.Channel == channel && (euler == null || target.Euler == euler));
            if (track.Property == ExpressionTransformAnimation.Active)
            {
                var t = data.Targets.FindIndex(target => target.Property == "active" && target.Node == track.Node);
                if (t < 0) { reason = $"active {track.Node} is no target"; return false; }
                result[t] = Held(0) >= 0.5 ? 1 : 0;
                return true;
            }
            string property; bool? isEuler = null; string[] channels; double[] sign;
            switch (track.Property)
            {
                case ExpressionTransformAnimation.Translation: property = "translation"; channels = AvatarAnimatorData.VectorChannels; sign = new[] { -1d, 1, 1 }; break;
                case ExpressionTransformAnimation.Scale: property = "scale"; channels = AvatarAnimatorData.VectorChannels; sign = new[] { 1d, 1, 1 }; break;
                case ExpressionTransformAnimation.Euler: property = "rotation"; isEuler = true; channels = AvatarAnimatorData.VectorChannels; sign = new[] { 1d, -1, -1 }; break;
                case ExpressionTransformAnimation.Rotation: property = "rotation"; isEuler = false; channels = AvatarAnimatorData.QuaternionChannels; sign = new[] { 1d, -1, -1, 1 }; break;
                default: reason = "transform " + track.Property; return false;
            }
            if (track.Curves.Count != channels.Length) { reason = "transform " + track.Property; return false; }
            for (var c = 0; c < channels.Length; c++)
            {
                var t = Find(property, channels[c], isEuler);
                if (t < 0) { reason = $"{track.Property} {track.Node}.{channels[c]} is no target"; return false; }
                result[t] = sign[c] * Held(c);
            }
            return true;
        }

        // The curves the written extension left out, by kind, for the result;
        // null when it left none out. The other kinds stay in omitted only.
        internal static string OmittedCurvesLine(AvatarAnimatorData data)
        {
            if (data == null) return null;
            var parts = CurveKinds.Select(kind => (kind.Label, Count: data.Omitted.FirstOrDefault(omission => omission.Kind == kind.Kind)?.Count ?? 0))
                .Where(part => part.Count > 0)
                .Select(part => ExporterLocalization.Format(OmittedCurveCount, ExporterLocalization.T(part.Label), part.Count)).ToList();
            return parts.Count == 0 ? null : ExporterLocalization.Format(OmittedCurvesNote, string.Join(ExporterLocalization.T("、"), parts));
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
            ReadExpressionParameters(expressionParameters);
            ReadControllerParameters(controller);
        }

        void ReadExpressionParameters(object expressionParameters)
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
        }

        // The parameters of the controller read next. A name an earlier
        // controller declared keeps that controller's type and default: the
        // data has one parameter of a name, as VRChat has one value.
        void ReadControllerParameters(AnimatorController controller)
        {
            foreach (var parameter in controller.parameters)
            {
                if (parameter == null || controllerParameters.ContainsKey(parameter.name)) continue;
                declared.Add(parameter.name);
                var type = parameter.type == AnimatorControllerParameterType.Bool ? "bool" : parameter.type == AnimatorControllerParameterType.Int ? "int"
                    : parameter.type == AnimatorControllerParameterType.Trigger ? "trigger" : "float";
                controllerParameters[parameter.name] = type;
                // A double, so an int default is not rounded through a float.
                var value = type == "float" ? (double)parameter.defaultFloat : type == "int" ? parameter.defaultInt : parameter.defaultBool ? 1 : 0;
                if (parameterIndex.TryGetValue(parameter.name, out var index))
                {
                    if (!controllerTyped.Add(index)) continue;
                    data.Parameters[index].Type = type;
                    controllerDefaults[index] = value;
                    continue;
                }
                controllerTyped.Add(data.Parameters.Count);
                parameterIndex[parameter.name] = data.Parameters.Count;
                controllerDefaults[data.Parameters.Count] = value;
                data.Parameters.Add(new AvatarAnimatorData.Parameter
                {
                    Name = parameter.name, Type = type, Origin = VrChatParameterDriver.BuiltIn.Contains(parameter.name) ? "builtin" : "animator",
                    Default = value
                });
            }
        }

        static string ExpressionType(string valueType) => valueType == "Bool" ? "bool" : valueType == "Int" ? "int" : valueType == "Float" ? "float" : null;

        // The read controller's parameter of that name when it is of one of the
        // types given, or -1. Unity reads only what its controller declares,
        // and each use takes only its own types: a name that Expression
        // Parameters alone declare, or nothing does, is no parameter here,
        // nor is one the controller declares with another type than the data's.
        int ControllerParameter(string name, params string[] types)
        {
            if (name == null || !controllerParameters.TryGetValue(name, out var own)) return -1;
            var index = parameterIndex[name];
            return types.Contains(own) && data.Parameters[index].Type == own ? index : -1;
        }

        // The parameter a state option reads, or -1. On a name the controller
        // does not declare Unity plays the option as unset, as written here.
        // On a parameter of another type Unity reads some other float instead,
        // which the data cannot say: that is counted.
        int ReadParameter(string name, string type)
        {
            var index = ControllerParameter(name, type);
            if (index < 0 && name != null && controllerParameters.ContainsKey(name)) Omit("unreadParameter");
            return index;
        }

        // A state option on a parameter Unity cannot read is off.
        int? StateParameter(bool active, string name, string type)
        {
            var index = active ? ReadParameter(name, type) : -1;
            return index < 0 ? (int?)null : index;
        }

        AvatarAnimatorData.Controller ReadController(AnimatorController controller, string playable)
        {
            var result = new AvatarAnimatorData.Controller { Playable = playable };
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
                SyncedLayer = source, SyncedLayerAffectsTiming = synced != null && layer.syncedLayerAffectsTiming,
                Mask = ReadMask(layer.avatarMask)
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

        // A mask that lets every body part and every node move is none.
        // Otherwise the body parts it lets move, and, when it names
        // transforms, the paths of those it lets move, which Build turns into
        // nodes (a path the file has no node for is dropped). A mask without
        // transforms lets every node move.
        AvatarAnimatorData.Mask ReadMask(AvatarMask mask)
        {
            if (mask == null) return null;
            var result = new AvatarAnimatorData.Mask();
            for (var part = 0; part < AvatarAnimatorData.BodyPartCount; part++)
                if (mask.GetHumanoidBodyPartActive((AvatarMaskBodyPart)part)) result.Humanoid.Add(part);
            if (mask.transformCount == 0 && result.Humanoid.Count == AvatarAnimatorData.BodyPartCount) return null;
            var paths = new List<string>();
            for (var i = 0; i < mask.transformCount; i++)
                if (mask.GetTransformActive(i)) paths.Add(mask.GetTransformPath(i) ?? "");
            masks.Add((result, mask.transformCount == 0 ? null : paths));
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
                        DestMin = Number(Member(operation, "destMin")), DestMax = Number(Member(operation, "destMax")),
                        PreventRepeats = PreventRepeats(operation, type, data.Parameters[parameterIndex[name]].Type)
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

        // Prevent Repeats (VRChat SDK 3.10.2 and later; an SDK without the
        // field never prevents) is offered only on a random into an int; set
        // anywhere else it is not shown and is read as off.
        internal static bool PreventRepeats(object operation, string type, string parameterType) =>
            type == "random" && parameterType == "int" && Member(operation, "preventRepeats") is bool prevent && prevent;

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
                unreadTrees[motion] = names.Where((name, i) => blend[i] < 0).Any(name => name != null && controllerParameters.ContainsKey(name));
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
            Driven(motion);
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

        // Unity binds the curves of every clip a state holds, under any blend
        // tree, whether or not the tree can be read or a direct child has a
        // weight: each FX parameter such a curve names is then held by the
        // animator. The walk keeps its own stack and visits each motion once.
        void Driven(Motion motion)
        {
            var pending = new Stack<Motion>();
            pending.Push(motion);
            while (pending.Count > 0)
            {
                var next = pending.Pop();
                if (next is AnimationClip original && overrides.TryGetValue(original, out var replaced)) next = replaced;
                if (next == null || !drivenWalked.Add(next)) continue;
                if (next is BlendTree tree)
                {
                    foreach (var child in tree.children) pending.Push(child.motion);
                    continue;
                }
                if (!(next is AnimationClip clip)) continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var parameter = AnimatedParameter(binding);
                    if (parameter >= 0) driven.Add(parameter);
                }
            }
        }

        // The FX parameter an Animator curve on the avatar root animates, or
        // -1. Unity takes such a curve only on a name the controller declares.
        int AnimatedParameter(EditorCurveBinding binding) =>
            binding.type == typeof(Animator) && binding.path.Length == 0 && binding.propertyName != null && controllerParameters.ContainsKey(binding.propertyName)
                ? parameterIndex[binding.propertyName] : -1;

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
            var parameterTracks = new List<(int, ExpressionAnimationData.Curve)>();
            clipParameterTracks.Add(parameterTracks);
            var morphTracks = new List<(int, ExpressionAnimationData.Curve)>();
            clipMorphTracks.Add(morphTracks);
            var enabledTracks = new List<(int, ExpressionAnimationData.Curve)>();
            clipEnabledTracks.Add(enabledTracks);
            var constraintTracks = new List<(int, ExpressionAnimationData.Curve)>();
            clipConstraintTracks.Add(constraintTracks);
            var materialTracks = new List<(Renderer, (string, string, string), ExpressionAnimationData.Curve)>();
            clipMaterialTracks.Add(materialTracks);
            var slotTracks = new List<(Renderer, int, ObjectReferenceKeyframe[])>();
            clipSlotTracks.Add(slotTracks);
            var transformTracks = new List<(int, ExpressionAnimationData.Curve)>();
            clipTransformTracks.Add(transformTracks);
            var muscleTracks = new List<(int, ExpressionAnimationData.Curve)>();
            clipMuscleTracks.Add(muscleTracks);
            var muscleCurves = new List<(EditorCurveBinding Binding, int Muscle)>();
            var keys = 0;
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var shapes = new List<EditorCurveBinding>();
            var switches = new List<EditorCurveBinding>();
            var constraintSwitches = new List<EditorCurveBinding>();
            var floats = new List<EditorCurveBinding>();
            var transformCurves = new List<EditorCurveBinding>();
            // The clip's key and track room goes to activeSelf curves first, then
            // to parameter curves, then to BlendShape curves, then to renderer
            // enabled curves, then to material curves, then to
            // constraint enabled curves, so a kind written
            // later never leaves out one written before. A curve past the room
            // is left out alone (see Curve). A material curve counts as
            // one track here; Resolve leaves it out when the materials it
            // becomes take the clip past the room.
            bool Full() => tracks.Count + parameterTracks.Count + morphTracks.Count + enabledTracks.Count + constraintTracks.Count + materialTracks.Count >= AvatarAnimatorData.MaximumTracksPerClip;
            foreach (var binding in bindings)
            {
                if (AnimatedParameter(binding) >= 0) continue;
                if (!(binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive"))
                {
                    // BlendShape, enabled and material curves take what
                    // activeSelf and parameters leave of the room.
                    if (binding.type == typeof(Animator) && binding.path.Length == 0)
                    {
                        var muscle = Muscle(binding.propertyName);
                        if (muscle >= 0) muscleCurves.Add((binding, muscle));
                        else Omit(HumanoidRoot(binding.propertyName) ? "humanoidRoot" : "muscle");
                    }
                    else if (Kind(binding) == "blendShape") shapes.Add(binding);
                    else if (RendererSwitch(binding)) switches.Add(binding);
                    else if (binding.propertyName == "m_Enabled" && AvatarConstraintsExport.IsConstraint(binding.type)) constraintSwitches.Add(binding);
                    else if (MaterialCurve(binding, out var materialOmitted) != null) floats.Add(binding);
                    else if (TransformCurve(binding) != null) transformCurves.Add(binding);
                    else Omit(materialOmitted ?? Kind(binding));
                    continue;
                }
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                if (target == null) { Omit("missingNode"); continue; }
                var converted = Full() ? null : Curve(clip, binding, ref keys);
                if (converted == null) { Omit("other"); continue; }
                tracks.Add((Target(target.gameObject), converted));
            }
            // On an Additive layer Unity adds a parameter curve's value less its
            // value at the reference pose, which is the clip's first frame unless
            // the clip names another. Another reference pose is not written, so
            // such a clip leaves its parameter curves out.
            var ownReferencePose = AnimationUtility.GetAnimationClipSettings(clip).hasAdditiveReferencePose;
            foreach (var binding in bindings)
            {
                var parameter = AnimatedParameter(binding);
                // Unity moves a float parameter only; on an int, bool or
                // trigger the curve holds the default, which its target shows.
                if (parameter < 0 || data.Parameters[parameter].Type != "float") continue;
                var animated = ownReferencePose || Full() ? null : Curve(clip, binding, ref keys);
                if (animated == null) Omit("parameter");
                else parameterTracks.Add((parameter, animated));
            }
            foreach (var binding in shapes)
            {
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                var renderer = target == null ? null : target.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null) { Omit("missingNode"); continue; }
                var converted = Full() ? null : Curve(clip, binding, ref keys);
                if (converted == null) { Omit("blendShape"); continue; }
                morphTracks.Add((Morph(renderer, binding.propertyName.Substring(ShapePrefix.Length)), converted));
            }
            foreach (var binding in switches)
            {
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                var renderer = target == null ? null : target.GetComponent(binding.type) as Renderer;
                if (renderer == null) { Omit("missingNode"); continue; }
                var converted = Full() ? null : Curve(clip, binding, ref keys);
                if (converted == null) { Omit("enabled"); continue; }
                enabledTracks.Add((RendererTarget(renderer), converted));
            }
            foreach (var binding in floats)
            {
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                var renderer = target == null ? null : target.GetComponent(binding.type) as Renderer;
                if (renderer == null) { Omit("missingNode"); continue; }
                var converted = Full() ? null : Curve(clip, binding, ref keys);
                if (converted == null) { Omit("material"); continue; }
                pathAtRead[renderer.gameObject] = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                var moves = MaterialCurve(binding, out _).Value;
                if (!slotsAtRead.ContainsKey((renderer, moves)))
                    slotsAtRead[(renderer, moves)] = renderer.sharedMaterials.Select(slot => SlotValue(slot, moves)).ToArray();
                materialTracks.Add((renderer, moves, converted));
            }
            // A curve on a constraint the avatar does not have is counted as
            // enabled, as before the extension.
            foreach (var binding in constraintSwitches)
            {
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                var constraint = target == null ? null : target.GetComponent(binding.type) as Behaviour;
                if (constraint == null) { Omit("enabled"); continue; }
                var converted = Full() ? null : Curve(clip, binding, ref keys);
                if (converted == null) { Omit("enabled"); continue; }
                constraintTracks.Add((ConstraintTarget(constraint), converted));
            }
            // Material slot curves come last; Resolve gives them the track
            // room the other kinds leave.
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var slot = MaterialSlot(binding);
                if (slot < 0)
                {
                    Omit(typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith("m_Materials", StringComparison.Ordinal) ? "material" : "other");
                    continue;
                }
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                var renderer = target == null ? null : target.GetComponent(binding.type) as Renderer;
                if (renderer == null) { Omit("missingNode"); continue; }
                var frames = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (frames == null || frames.Length == 0 || keys + frames.Length > AvatarAnimatorData.MaximumKeysPerClip ||
                    frames.Any(frame => frame.value != null && !(frame.value is Material))) { Omit("material"); continue; }
                keys += frames.Length;
                if (rendererSlotsAtRead == null)
                    rendererSlotsAtRead = root.GetComponentsInChildren<Renderer>(true)
                        .ToDictionary(each => each, each => each.sharedMaterials.Select((material, index) => (material, index)).ToArray());
                pathAtRead[renderer.gameObject] = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                slotTracks.Add((renderer, slot, frames));
            }
            // Transform curves come after them all and take the room left.
            // A curve on a Humanoid bone is counted as humanoidTransform: the
            // app's pose and hand tracking write those bones every frame. One
            // on the avatar root, which the app places, stays counted as
            // transform, as before the extension.
            foreach (var binding in transformCurves)
            {
                var target = binding.path.Length == 0 ? root.transform : root.transform.Find(binding.path);
                if (target == null) { Omit("missingNode"); continue; }
                if (target == root.transform) { Omit("transform"); continue; }
                if (HumanoidBoneSet().Contains(target)) { Omit("humanoidTransform"); continue; }
                var converted = Full() || tracks.Count + parameterTracks.Count + morphTracks.Count + enabledTracks.Count + constraintTracks.Count +
                    materialTracks.Count + slotTracks.Count + transformTracks.Count >= AvatarAnimatorData.MaximumTracksPerClip ? null : Curve(clip, binding, ref keys);
                if (converted == null) { Omit("transform"); continue; }
                transformTracks.Add((TransformTarget(target.gameObject, TransformCurve(binding).Value), converted));
            }
            // Muscle curves come last of all and take the room left.
            foreach (var (binding, muscle) in muscleCurves)
            {
                var converted = Full() || tracks.Count + parameterTracks.Count + morphTracks.Count + enabledTracks.Count + constraintTracks.Count +
                    materialTracks.Count + slotTracks.Count + transformTracks.Count + muscleTracks.Count >= AvatarAnimatorData.MaximumTracksPerClip ? null : Curve(clip, binding, ref keys);
                if (converted == null) { Omit("muscle"); continue; }
                muscleTracks.Add((muscle, converted));
            }
            return index;
        }

        // The curve name of each Humanoid muscle (an Animator curve on the
        // avatar root), to its index of HumanTrait.MuscleName. A clip names a
        // finger muscle as LeftHand.Thumb.1 Stretched where HumanTrait says
        // Left Thumb 1 Stretched; every other muscle by the same name.
        static Dictionary<string, int> muscleNames;
        static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };

        internal static int Muscle(string curveName)
        {
            if (muscleNames == null)
            {
                var names = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < HumanTrait.MuscleCount && i < AvatarAnimatorData.MuscleCount; i++)
                {
                    var name = HumanTrait.MuscleName[i];
                    foreach (var side in new[] { "Left", "Right" })
                        foreach (var finger in Fingers)
                            if (name.StartsWith(side + " " + finger + " ", StringComparison.Ordinal))
                                name = side + "Hand." + finger + "." + name.Substring(side.Length + finger.Length + 2);
                    names[name] = i;
                }
                muscleNames = names;
            }
            return curveName != null && muscleNames.TryGetValue(curveName, out var index) ? index : -1;
        }

        // The root's position and rotation (RootT, RootQ and the root motion
        // MotionT, MotionQ) and the hands' and feet's IK goals: the app's
        // tracking and walking place the hips, so these are left out.
        static readonly string[] HumanoidRootCurves = { "RootT", "RootQ", "MotionT", "MotionQ", "LeftFootT", "LeftFootQ", "RightFootT", "RightFootQ", "LeftHandT", "LeftHandQ", "RightHandT", "RightHandQ" };

        static bool HumanoidRoot(string curveName)
        {
            var dot = curveName == null ? -1 : curveName.LastIndexOf('.');
            return dot > 0 && curveName.Length == dot + 2 && "xyzw".IndexOf(curveName[dot + 1]) >= 0 && Array.IndexOf(HumanoidRootCurves, curveName.Substring(0, dot)) >= 0;
        }

        // The curve as written, or null when the contract does not take it
        // (more keys than the clip has room left for, or a curve the shared
        // curve contract rejects, such as a key past an hour, a value that is
        // not a number or out of range): it is then left out alone, so the rest
        // of the avatar is still written. The keys are counted before the
        // curve is converted, which keeps every key: a curve too long is not
        // converted. Unity never reads the first key's in-tangent or the last
        // key's out-tangent, which some tools leave as not a number: such a
        // tangent is written as null, as an infinite one is.
        static ExpressionAnimationData.Curve Curve(AnimationClip clip, EditorCurveBinding binding, ref int keys)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0 || keys + curve.length > AvatarAnimatorData.MaximumKeysPerClip) return null;
            var converted = VrChatGestureExpressions.ConvertCurve(curve);
            var first = converted.Keys[0];
            var last = converted.Keys[converted.Keys.Count - 1];
            if (first.InTangent is double inTangent && double.IsNaN(inTangent)) first.InTangent = null;
            if (last.OutTangent is double outTangent && double.IsNaN(outTangent)) last.OutTangent = null;
            try { converted.Validate(); }
            catch (InvalidOperationException) { return null; }
            keys += converted.Keys.Count;
            return converted;
        }

        int TransformTarget(GameObject gameObject, (string, string, bool) moves)
        {
            if (transformIndex.TryGetValue((gameObject, moves), out var index)) return index;
            transformIndex[(gameObject, moves)] = transforms.Count;
            pathAtRead[gameObject] = AnimationUtility.CalculateTransformPath(gameObject.transform, root.transform);
            transforms.Add((gameObject, moves));
            return transforms.Count - 1;
        }

        HashSet<Transform> HumanoidBoneSet()
        {
            if (humanoidBones != null) return humanoidBones;
            humanoidBones = new HashSet<Transform>();
            var animator = root.GetComponent<Animator>();
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                var transform = HumanoidBones.Resolve(animator, bone);
                if (transform != null) humanoidBones.Add(transform);
            }
            return humanoidBones;
        }

        // What a Transform curve moves: m_LocalPosition.x a translation
        // channel, m_LocalScale.x a scale channel, m_LocalRotation.x a
        // quaternion rotation channel and localEulerAnglesRaw.x an Euler one,
        // which Unity blends as angles (the Euler Angles interpolation). Null
        // for another curve, counted as transform: an Euler Angles
        // (Quaternion) curve, whose angles Unity plays as baked quaternions,
        // is among them.
        internal static (string Property, string Channel, bool Euler)? TransformCurve(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Transform)) return null;
            var property = binding.propertyName ?? "";
            var dot = property.LastIndexOf('.');
            if (dot < 0) return null;
            var channel = property.Substring(dot + 1);
            switch (property.Substring(0, dot))
            {
                case "m_LocalPosition": return Array.IndexOf(AvatarAnimatorData.VectorChannels, channel) >= 0 ? ("translation", channel, false) : ((string, string, bool)?)null;
                case "m_LocalScale": return Array.IndexOf(AvatarAnimatorData.VectorChannels, channel) >= 0 ? ("scale", channel, false) : ((string, string, bool)?)null;
                case "m_LocalRotation": return Array.IndexOf(AvatarAnimatorData.QuaternionChannels, channel) >= 0 ? ("rotation", channel, false) : ((string, string, bool)?)null;
                case "localEulerAnglesRaw": return Array.IndexOf(AvatarAnimatorData.VectorChannels, channel) >= 0 ? ("rotation", channel, true) : ((string, string, bool)?)null;
                default: return null;
            }
        }

        int Target(GameObject gameObject)
        {
            if (targetIndex.TryGetValue(gameObject, out var index)) return index;
            targetIndex[gameObject] = targets.Count;
            pathAtRead[gameObject] = AnimationUtility.CalculateTransformPath(gameObject.transform, root.transform);
            targets.Add((gameObject, (rest.TryGetValue(gameObject, out var active) ? active : gameObject.activeSelf) ? 1 : 0));
            return targets.Count - 1;
        }

        int RendererTarget(Renderer renderer)
        {
            if (rendererIndex.TryGetValue(renderer, out var index)) return index;
            rendererIndex[renderer] = renderers.Count;
            pathAtRead[renderer.gameObject] = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
            renderers.Add((renderer, (enabledRest.TryGetValue(renderer, out var enabled) ? enabled : renderer.enabled) ? 1 : 0));
            return renderers.Count - 1;
        }

        static int ConstraintNode(Behaviour constraint, Func<GameObject, int> node)
        {
            var target = AvatarConstraintsExport.TargetOf(constraint);
            return target == null ? -1 : node(target.gameObject);
        }

        int ConstraintTarget(Behaviour constraint)
        {
            if (constraintIndex.TryGetValue(constraint, out var index)) return index;
            constraintIndex[constraint] = constraints.Count;
            constraints.Add((constraint, constraint.enabled ? 1 : 0));
            return constraints.Count - 1;
        }

        // The renderers an enabled target turns on and off: those the export
        // writes as glTF meshes. Particle, trail and line renderers are not.
        static bool RendererSwitch(EditorCurveBinding binding) =>
            binding.propertyName == "m_Enabled" && (binding.type == typeof(SkinnedMeshRenderer) || binding.type == typeof(MeshRenderer));

        const string SlotPrefix = "m_Materials.Array.data[";

        // The slot a mesh renderer's material slot curve
        // (m_Materials.Array.data[n]) sets, or -1 for another curve.
        internal static int MaterialSlot(EditorCurveBinding binding)
        {
            var property = binding.propertyName ?? "";
            if (!(binding.type == typeof(SkinnedMeshRenderer) || binding.type == typeof(MeshRenderer)) ||
                !property.StartsWith(SlotPrefix, StringComparison.Ordinal) || !property.EndsWith("]", StringComparison.Ordinal)) return -1;
            var digits = property.Substring(SlotPrefix.Length, property.Length - SlotPrefix.Length - 1);
            return digits.Length > 0 && digits.All(c => c >= '0' && c <= '9') &&
                   int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var slot) ? slot : -1;
        }

        const string MaterialPrefix = "material.";

        // The material target kinds, in the order their targets are written.
        static readonly string[] MaterialProperties = { "materialFloat", "materialColor", "materialTextureST" };

        const string TextureTransformSuffix = "_ST";

        // What a mesh renderer's material curve moves: material.X the float
        // X (materialFloat), material.X.r a channel of the color X
        // (materialColor) and material.X_ST.x a channel of the scale and
        // offset of the texture X (materialTextureST). Null for another curve,
        // with the kind it is counted as in omitted, or null when it is not a
        // mesh renderer's material curve. The app's lilToon material keeps
        // the contract's properties under the same names: a name it does not
        // keep with that type is counted as materialProperty, and a feature
        // switch the app fixes on loading as materialSwitch. A vector channel
        // (material._Vector.x) is counted as material.
        internal static (string Property, string Name, string Channel)? MaterialCurve(EditorCurveBinding binding, out string omitted)
        {
            omitted = null;
            var property = binding.propertyName ?? "";
            if (!(binding.type == typeof(SkinnedMeshRenderer) || binding.type == typeof(MeshRenderer)) ||
                !property.StartsWith(MaterialPrefix, StringComparison.Ordinal)) return null;
            omitted = "material";
            var moved = property.Substring(MaterialPrefix.Length);
            var dot = moved.IndexOf('.');
            var name = dot < 0 ? moved : moved.Substring(0, dot);
            var channel = dot < 0 ? null : moved.Substring(dot + 1);
            string kind, type;
            if (channel == null) { kind = "materialFloat"; type = "float"; }
            else if (Array.IndexOf(AvatarAnimatorData.ColorChannels, channel) >= 0) { kind = "materialColor"; type = "color"; }
            else if (Array.IndexOf(AvatarAnimatorData.TextureChannels, channel) < 0) return null;
            else if (name.EndsWith(TextureTransformSuffix, StringComparison.Ordinal) &&
                     LilToonFullContract.RuntimeType(name.Substring(0, name.Length - TextureTransformSuffix.Length)) == "texture")
            {
                kind = "materialTextureST"; type = "texture";
                name = name.Substring(0, name.Length - TextureTransformSuffix.Length);
            }
            else { kind = null; type = "vector"; }
            if (name.Length == 0) return null;
            if (LilToonFullContract.RuntimeType(name) != type) { omitted = "materialProperty"; return null; }
            if (LilToonFullContract.IsSwitch(name)) { omitted = "materialSwitch"; return null; }
            if (kind == null) return null;
            omitted = null;
            return (kind, name, channel);
        }

        // Whether a slot's material has what a material curve moves, and its value there.
        static (Material Material, bool Has, float Value) SlotValue(Material slot, (string Property, string Name, string Channel) moves)
        {
            if (slot == null) return (null, false, 0f);
            switch (moves.Property)
            {
                case "materialFloat":
                    return slot.HasFloat(moves.Name) ? (slot, true, slot.GetFloat(moves.Name)) : (slot, false, 0f);
                case "materialColor":
                    return slot.HasColor(moves.Name) ? (slot, true, slot.GetColor(moves.Name)[Array.IndexOf(AvatarAnimatorData.ColorChannels, moves.Channel)]) : (slot, false, 0f);
                default:
                    if (!slot.HasTexture(moves.Name)) return (slot, false, 0f);
                    var channel = Array.IndexOf(AvatarAnimatorData.TextureChannels, moves.Channel);
                    var pair = channel < 2 ? slot.GetTextureScale(moves.Name) : slot.GetTextureOffset(moves.Name);
                    return (slot, true, pair[channel % 2]);
            }
        }

        string PathAtRead(GameObject gameObject) => gameObject != null && pathAtRead.TryGetValue(gameObject, out var path) ? path : null;

        int Morph(SkinnedMeshRenderer renderer, string shape)
        {
            if (morphIndex.TryGetValue((renderer, shape), out var index)) return index;
            morphIndex[(renderer, shape)] = morphs.Count;
            pathAtRead[renderer.gameObject] = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
            morphs.Add((renderer, shape));
            return morphs.Count - 1;
        }

        // An Animator curve on the avatar root on a name the controller does not
        // declare (those it declares are parameters, taken before) is a
        // Humanoid muscle, root motion or an IK goal (see ClipIndex).
        string Kind(EditorCurveBinding binding)
        {
            var property = binding.propertyName ?? "";
            if (binding.type == typeof(Animator) && binding.path.Length == 0) return "muscle";
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
