using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // One expression analysis inspects the same prepared FX graphs thousands
    // of times with a few hundred distinct argument sets. Persistent controller
    // and clip assets are not edited while the scope is open: probes and
    // evaluation copies are HideAndDontSave and never enter the cache. A hit
    // replays the recorded graph walk into fresh objects, so callers still own
    // every returned set, program and side effect exactly as before.
    internal sealed class ExpressionInspectCache : IDisposable
    {
        private static ExpressionInspectCache current;
        private readonly ExpressionInspectCache previous;
        private readonly Dictionary<string, Trace> traces = new Dictionary<string, Trace>(StringComparer.Ordinal);
        private readonly Dictionary<int, bool> persistent = new Dictionary<int, bool>();
        private readonly Dictionary<int, EditorCurveBinding[]> bindings = new Dictionary<int, EditorCurveBinding[]>();
        private readonly Dictionary<(int Clip, EditorCurveBinding Binding), bool?> dynamic = new Dictionary<(int Clip, EditorCurveBinding Binding), bool?>();
        private bool disposed;

        internal int Hits { get; private set; }
        internal int Misses { get; private set; }

        private ExpressionInspectCache(ExpressionInspectCache previous) { this.previous = previous; }

        internal static ExpressionInspectCache Current => current;

        internal static ExpressionInspectCache Begin() => current = new ExpressionInspectCache(current);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (current == this) current = previous;
        }

        internal sealed class Trace
        {
            internal ExpressionDependencies.Layer[] Layers;
            internal readonly List<AnimationClip>[] Clips;
            internal readonly List<KeyValuePair<StateMachineBehaviour, VrChatParameterDriver.Program>> Drivers =
                new List<KeyValuePair<StateMachineBehaviour, VrChatParameterDriver.Program>>();
            internal readonly List<string> Unknown = new List<string>();
            internal Trace(int layers) { Clips = Enumerable.Range(0, layers).Select(_ => new List<AnimationClip>()).ToArray(); }
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

        // Returns null when this call must not use the cache.
        internal string Signature(RuntimeAnimatorController runtime, bool allowFxControls, IDictionary<string, float> neutralFixed,
            bool typedConditions, int? fxLayerCount)
        {
            if (!Persistent(runtime)) return null;
            if (runtime is AnimatorOverrideController overrides && !Persistent(overrides.runtimeAnimatorController)) return null;
            // Another key comparer could make the same entries resolve
            // differently, so only ordinal dictionaries are keyed by content.
            if (neutralFixed != null && !(neutralFixed is Dictionary<string, float> dictionary &&
                (ReferenceEquals(dictionary.Comparer, StringComparer.Ordinal) || ReferenceEquals(dictionary.Comparer, EqualityComparer<string>.Default))))
                return null;
            var key = new StringBuilder();
            key.Append(runtime.GetInstanceID().ToString(CultureInfo.InvariantCulture)).Append('|').Append(allowFxControls ? '1' : '0')
                .Append(typedConditions ? '1' : '0').Append('|').Append(fxLayerCount?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|');
            if (neutralFixed == null) key.Append('-');
            else
                foreach (var name in neutralFixed.Keys.OrderBy(name => name, StringComparer.Ordinal))
                    key.Append(name.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(name).Append('=')
                        .Append(BitConverter.ToInt32(BitConverter.GetBytes(neutralFixed[name]), 0).ToString(CultureInfo.InvariantCulture)).Append(';');
            return key.ToString();
        }

        internal EditorCurveBinding[] Bindings(AnimationClip clip)
        {
            if (!Persistent(clip)) return ExpressionDependencies.ReadBindings(clip);
            var id = clip.GetInstanceID();
            if (!bindings.TryGetValue(id, out var result)) bindings.Add(id, result = ExpressionDependencies.ReadBindings(clip));
            return result;
        }

        internal bool? Dynamic(AnimationClip clip, EditorCurveBinding binding)
        {
            if (!Persistent(clip)) return ExpressionDependencies.ReadDynamic(clip, binding);
            var key = (clip.GetInstanceID(), binding);
            if (!dynamic.TryGetValue(key, out var result)) dynamic.Add(key, result = ExpressionDependencies.ReadDynamic(clip, binding));
            return result;
        }

        internal Trace Record(int layers) { Misses++; return new Trace(layers); }

        internal void Store(string key, Trace trace, ExpressionDependencies.Layer[] result)
        {
            // Keep a private copy: the caller owns and may edit the result.
            var map = new Dictionary<object, object>();
            trace.Layers = result.Select(layer => Copy(layer, map)).ToArray();
            for (var index = 0; index < trace.Drivers.Count; index++)
                trace.Drivers[index] = new KeyValuePair<StateMachineBehaviour, VrChatParameterDriver.Program>(
                    trace.Drivers[index].Key, Copy(trace.Drivers[index].Value, map));
            traces[key] = trace;
        }

        internal bool TryReplay(string key, Func<string, bool> excludedPath, bool readRanges,
            Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program> drivers, List<string> unknown, out ExpressionDependencies.Layer[] result)
        {
            result = null;
            if (!traces.TryGetValue(key, out var trace)) return false;
            var map = new Dictionary<object, object>();
            var layers = trace.Layers.Select(layer => Copy(layer, map)).ToArray();
            var excludedPaths = excludedPath == null ? null : new Dictionary<string, bool>(StringComparer.Ordinal);
            for (var index = 0; index < layers.Length; index++)
            {
                var seen = new HashSet<AnimationClip>();
                foreach (var clip in trace.Clips[index])
                    if (seen.Add(clip)) ExpressionDependencies.InspectClipBindings(layers[index], clip, excludedPath, excludedPaths, readRanges, this);
            }
            foreach (var pair in trace.Drivers)
                if (!drivers.ContainsKey(pair.Key)) drivers.Add(pair.Key, Copy(pair.Value, map));
            unknown.AddRange(trace.Unknown);
            Hits++;
            result = layers;
            return true;
        }

        // Binding-derived members (Morphs, HasBindings, NonMorphBindings,
        // DynamicMorph) depend on the caller's path exclusion. Copies leave
        // them unset; a replay rebuilds them from the recorded clips.
        private static ExpressionDependencies.Layer Copy(ExpressionDependencies.Layer source, Dictionary<object, object> map)
        {
            var copy = new ExpressionDependencies.Layer
            {
                WriteDefaults = source.WriteDefaults, EmptyMotion = source.EmptyMotion, FxControl = source.FxControl, Timed = source.Timed,
            };
            copy.Reads.UnionWith(source.Reads);
            copy.ControlReads.UnionWith(source.ControlReads);
            copy.CopyInputs.UnionWith(source.CopyInputs);
            copy.Writes.UnionWith(source.Writes);
            copy.DriverWrites.UnionWith(source.DriverWrites);
            copy.CurveWrites.UnionWith(source.CurveWrites);
            copy.Clips.UnionWith(source.Clips);
            foreach (var program in source.DriverPrograms) copy.DriverPrograms.Add(Copy(program, map));
            foreach (var pair in source.FxCommands) copy.FxCommands.Add(pair.Key, Copy(pair.Value, map));
            foreach (var pair in source.WeightControls) copy.WeightControls.Add(pair.Key, Copy(pair.Value, map));
            return copy;
        }

        private static VrChatParameterDriver.Program Copy(VrChatParameterDriver.Program source, Dictionary<object, object> map)
        {
            if (source == null) return null;
            if (map.TryGetValue(source, out var known)) return (VrChatParameterDriver.Program)known;
            var copy = new VrChatParameterDriver.Program
            {
                Location = source.Location, Error = source.Error, LocalOnly = source.LocalOnly, FxControl = source.FxControl,
                FxWeight = source.FxWeight, LayerWeight = Copy(source.LayerWeight, map),
            };
            foreach (var operation in source.Operations)
                copy.Operations.Add(new VrChatParameterDriver.Operation
                {
                    Kind = operation.Kind, Destination = operation.Destination, Source = operation.Source, Value = operation.Value,
                    SourceMin = operation.SourceMin, SourceMax = operation.SourceMax, DestinationMin = operation.DestinationMin,
                    DestinationMax = operation.DestinationMax, ConvertRange = operation.ConvertRange,
                    UnresolvedRandom = operation.UnresolvedRandom, Error = operation.Error,
                });
            map.Add(source, copy);
            return copy;
        }

        private static SdkLayerWeightControl Copy(SdkLayerWeightControl source, Dictionary<object, object> map)
        {
            if (source == null) return null;
            if (map.TryGetValue(source, out var known)) return (SdkLayerWeightControl)known;
            var copy = new SdkLayerWeightControl
            {
                Location = source.Location, AnimatorLayer = source.AnimatorLayer, Playable = source.Playable, LayerIndex = source.LayerIndex,
                GoalWeight = source.GoalWeight, BlendDuration = source.BlendDuration, SourceState = source.SourceState,
                FixedBaseLayer = source.FixedBaseLayer,
            };
            map.Add(source, copy);
            return copy;
        }
    }
}
