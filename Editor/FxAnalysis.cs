using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The FX analysis one export reuses for every listed face. Analyze makes
    // one outside its per-face loop and passes it down. Persistent controller
    // and clip assets are not edited while the export analyses them; probes
    // and evaluation copies are HideAndDontSave and are walked afresh. Walks
    // and proofs whose fixed inputs differ per face are kept apart by those
    // inputs. Walked Layers are shared, so a caller must not edit one; the
    // arrays and tables are handed out as copies.
    internal sealed class FxAnalysis
    {
        private sealed class Walk
        {
            internal ExpressionDependencies.Layer[] Layers;
            internal KeyValuePair<StateMachineBehaviour, VrChatParameterDriver.Program>[] Drivers;
            internal string[] Unknown;
        }

        private readonly Dictionary<(string Key, Func<string, bool> Excluded), Walk> walks = new Dictionary<(string Key, Func<string, bool> Excluded), Walk>();
        private readonly Dictionary<int, bool> persistent = new Dictionary<int, bool>();
        private readonly Dictionary<int, Dictionary<string, AnimatorControllerParameterType>> types =
            new Dictionary<int, Dictionary<string, AnimatorControllerParameterType>>();
        private readonly Dictionary<int, EditorCurveBinding[]> bindings = new Dictionary<int, EditorCurveBinding[]>();
        private readonly Dictionary<(int Clip, EditorCurveBinding Binding), bool?> dynamic = new Dictionary<(int Clip, EditorCurveBinding Binding), bool?>();
        private readonly Dictionary<int, (int Index, AnimationClip Clip)[]> stationary = new Dictionary<int, (int Index, AnimationClip Clip)[]>();
        private readonly Dictionary<int, EditorCurveBinding[]> curveBindings = new Dictionary<int, EditorCurveBinding[]>();
        private readonly Dictionary<int, EditorCurveBinding[]> objectBindings = new Dictionary<int, EditorCurveBinding[]>();
        private readonly Dictionary<(int Clip, EditorCurveBinding Binding), bool> constant = new Dictionary<(int Clip, EditorCurveBinding Binding), bool>();
        private readonly Dictionary<int, EditorCurveBinding[]> automatic = new Dictionary<int, EditorCurveBinding[]>();
        private readonly Dictionary<string, (VrChatExpressionMenu.Source Source, int[] Layers)> randomRest =
            new Dictionary<string, (VrChatExpressionMenu.Source Source, int[] Layers)>(StringComparer.Ordinal);

        // The avatar's face geometry, estimated at most once per analysis.
        internal readonly FacialProjectionScope.Session FacialProjection;

        internal int Hits { get; private set; }
        internal int Misses { get; private set; }
        internal int RandomRestHits { get; private set; }
        internal int RandomRestMisses { get; private set; }

        internal FxAnalysis(GameObject avatar = null)
        {
            if (avatar != null) FacialProjection = new FacialProjectionScope.Session(avatar);
        }

        private bool Persistent(UnityEngine.Object value)
        {
            if (value == null) return false;
            var id = value.GetInstanceID();
            if (persistent.TryGetValue(id, out var known)) return known;
            var result = (value.hideFlags & HideFlags.DontSave) == 0 && EditorUtility.IsPersistent(value);
            persistent.Add(id, result);
            return result;
        }

        private bool Saved(RuntimeAnimatorController runtime) =>
            Persistent(runtime) && !(runtime is AnimatorOverrideController overrides && !Persistent(overrides.runtimeAnimatorController));

        // Returns null when this walk must not be kept.
        internal string Signature(RuntimeAnimatorController runtime, bool allowFxControls, IDictionary<string, float> neutralFixed,
            bool typedConditions, int? fxLayerCount, IDictionary<string, ISet<float>> domains = null)
        {
            if (!Saved(runtime)) return null;
            // Another key comparer could make the same entries resolve
            // differently, so only ordinal dictionaries are keyed by content.
            bool Ordinal<T>(IDictionary<string, T> value) => value is Dictionary<string, T> dictionary &&
                (ReferenceEquals(dictionary.Comparer, StringComparer.Ordinal) || ReferenceEquals(dictionary.Comparer, EqualityComparer<string>.Default));
            if (neutralFixed != null && !Ordinal(neutralFixed)) return null;
            // Domains only decide which states and transitions a walk reaches.
            // Each set is keyed by content only when it compares values with
            // the default comparer.
            if (domains != null && (!Ordinal(domains) || domains.Values.Any(set =>
                !(set is HashSet<float> values && ReferenceEquals(values.Comparer, EqualityComparer<float>.Default)))))
                return null;
            var key = new StringBuilder();
            key.Append(runtime.GetInstanceID().ToString(CultureInfo.InvariantCulture)).Append('|').Append(allowFxControls ? '1' : '0')
                .Append(typedConditions ? '1' : '0').Append('|').Append(fxLayerCount?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|');
            AppendValues(key, neutralFixed);
            // Null domains walk every transition; an empty dictionary is kept apart anyway.
            key.Append('|');
            if (domains == null) key.Append('-');
            else
            {
                key.Append('+');
                foreach (var name in domains.Keys.OrderBy(name => name, StringComparer.Ordinal))
                    key.Append(name.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(name).Append('=')
                        .Append(string.Join(",", domains[name].Select(Bits).OrderBy(bits => bits)
                            .Select(bits => bits.ToString(CultureInfo.InvariantCulture)))).Append(';');
            }
            return key.ToString();
        }

        private static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        private static void AppendValues(StringBuilder key, IDictionary<string, float> values)
        {
            if (values == null) { key.Append('-'); return; }
            foreach (var name in values.Keys.OrderBy(name => name, StringComparer.Ordinal))
                key.Append(name.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(name).Append('=')
                    .Append(Bits(values[name]).ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        // A kept walk is reused only with the same path exclusion: its
        // layers' bindings and morphs already left the excluded paths out.
        // The caller gets its own array, and the walk's drivers and unknown
        // callbacks in their original order, as a fresh walk would add them.
        internal ExpressionDependencies.Layer[] Inspect(string key, Func<string, bool> excludedPath,
            Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program> drivers, List<string> unknown,
            Func<Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program>, List<string>, ExpressionDependencies.Layer[]> walk)
        {
            if (walks.TryGetValue((key, excludedPath), out var kept)) Hits++;
            else
            {
                Misses++;
                var walkDrivers = new Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program>();
                var walkUnknown = new List<string>();
                var layers = walk(walkDrivers, walkUnknown);
                walks.Add((key, excludedPath), kept = new Walk { Layers = layers, Drivers = walkDrivers.ToArray(), Unknown = walkUnknown.ToArray() });
            }
            foreach (var pair in kept.Drivers)
                if (!drivers.ContainsKey(pair.Key)) drivers.Add(pair.Key, pair.Value);
            unknown.AddRange(kept.Unknown);
            return (ExpressionDependencies.Layer[])kept.Layers.Clone();
        }

        internal Dictionary<string, AnimatorControllerParameterType> ParameterTypes(RuntimeAnimatorController runtime)
        {
            var controller = ExpressionDependencies.Controller(runtime);
            if (!Persistent(controller)) return ExpressionDependencies.ReadParameterTypes(controller);
            var id = controller.GetInstanceID();
            if (!types.TryGetValue(id, out var result)) types.Add(id, result = ExpressionDependencies.ReadParameterTypes(controller));
            return new Dictionary<string, AnimatorControllerParameterType>(result, StringComparer.Ordinal);
        }

        internal EditorCurveBinding[] Bindings(AnimationClip clip)
        {
            if (!Persistent(clip)) return ExpressionDependencies.ReadBindings(clip);
            var id = clip.GetInstanceID();
            if (!bindings.TryGetValue(id, out var result)) bindings.Add(id, result = ExpressionDependencies.ReadBindings(clip));
            return (EditorCurveBinding[])result.Clone();
        }

        internal bool? Dynamic(AnimationClip clip, EditorCurveBinding binding)
        {
            if (!Persistent(clip)) return ExpressionDependencies.ReadDynamic(clip, binding);
            var key = (clip.GetInstanceID(), binding);
            if (!dynamic.TryGetValue(key, out var result)) dynamic.Add(key, result = ExpressionDependencies.ReadDynamic(clip, binding));
            return result;
        }

        // Every expression probe proves the same stationary FX layers again.
        // Their graph checks only read the persistent controller; the binding
        // checks that depend on the caller's path exclusion run on each call.
        internal (int Index, AnimationClip Clip)[] StationaryCandidates(RuntimeAnimatorController runtime)
        {
            if (!Saved(runtime)) return ExpressionDependencies.StationaryCandidates(runtime);
            var id = runtime.GetInstanceID();
            if (!stationary.TryGetValue(id, out var result)) stationary.Add(id, result = ExpressionDependencies.StationaryCandidates(runtime));
            return ((int Index, AnimationClip Clip)[])result.Clone();
        }

        internal EditorCurveBinding[] CurveBindings(AnimationClip clip)
        {
            if (!Persistent(clip)) return AnimationUtility.GetCurveBindings(clip);
            var id = clip.GetInstanceID();
            if (!curveBindings.TryGetValue(id, out var result)) curveBindings.Add(id, result = AnimationUtility.GetCurveBindings(clip));
            return (EditorCurveBinding[])result.Clone();
        }

        internal EditorCurveBinding[] ObjectBindings(AnimationClip clip)
        {
            if (!Persistent(clip)) return AnimationUtility.GetObjectReferenceCurveBindings(clip);
            var id = clip.GetInstanceID();
            if (!objectBindings.TryGetValue(id, out var result)) objectBindings.Add(id, result = AnimationUtility.GetObjectReferenceCurveBindings(clip));
            return (EditorCurveBinding[])result.Clone();
        }

        internal bool Constant(AnimationClip clip, EditorCurveBinding binding)
        {
            if (!Persistent(clip)) return CurveChecks.Constant(AnimationUtility.GetEditorCurve(clip, binding));
            var key = (clip.GetInstanceID(), binding);
            if (!constant.TryGetValue(key, out var result)) constant.Add(key, result = CurveChecks.Constant(AnimationUtility.GetEditorCurve(clip, binding)));
            return result;
        }

        // Every expression probe asks for the avatar's blink, viseme and VRM
        // blink channels again. The analysis does not change the avatar's
        // meshes or descriptor, and the blink alias check reads whole meshes.
        // Returns a new set, so a caller cannot change the kept entry.
        internal HashSet<EditorCurveBinding> AutomaticChannels(GameObject avatar)
        {
            var id = avatar.GetInstanceID();
            if (!automatic.TryGetValue(id, out var result)) automatic.Add(id, result = NeutralShapeSampler.AutomaticChannels(avatar).ToArray());
            return new HashSet<EditorCurveBinding>(result);
        }

        // Every menu entry proves the same neutral Random rest again. With an
        // empty projection plan the proof reads only the persistent graphs, the
        // menu metadata and the fixed input values: never the selection, the
        // avatar's current pose or a warning list. The fixed inputs differ per
        // face, so they are part of the key. Returns a set the caller owns.
        internal HashSet<int> RandomRest(GameObject avatar, RuntimeAnimatorController runtime,
            VrChatExpressionMenu.Source source, FixedExpressionContext context, Func<HashSet<int>> prove)
        {
            string key = null;
            if (avatar != null && source != null && Saved(runtime) && source.OtherControllers.All(other => other == null || Saved(other)))
            {
                var text = new StringBuilder();
                text.Append(avatar.GetInstanceID().ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(runtime.GetInstanceID().ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(RuntimeHelpers.GetHashCode(source).ToString(CultureInfo.InvariantCulture)).Append('|');
                AppendValues(text, context?.Values);
                key = text.ToString();
                // The hash code is not unique; the entry must name the same Source.
                if (randomRest.TryGetValue(key, out var entry) && ReferenceEquals(entry.Source, source))
                {
                    RandomRestHits++;
                    return new HashSet<int>(entry.Layers);
                }
            }
            RandomRestMisses++;
            var result = prove();
            if (key != null) randomRest[key] = (source, result.ToArray());
            return result;
        }
    }
}
