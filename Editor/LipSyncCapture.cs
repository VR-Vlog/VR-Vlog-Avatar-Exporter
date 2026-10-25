using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    // VRVLOG_lip_sync: an avatar whose Lip Sync is Viseme Parameter Only shows
    // one mouth shape at a time, switched by FX transitions on VRChat's Viseme
    // parameter. Read from the prepared copy after the build passes; see
    // Documentation~/decisions/2026-10-25-lipsync-dominant-viseme.md.
    internal static class LipSyncCapture
    {
        internal const string FailureWarning = "VR Vlog: リップシンクの切り替えを書き出しませんでした: ";
        internal const string VisemeParameter = "Viseme";

        // Returns null unless the descriptor's Lip Sync is Viseme Parameter
        // Only. A failure adds a warning and also returns null, so the export
        // goes on without the extension.
        internal static LipSyncData Capture(GameObject clone, ICollection<string> warnings)
        {
            if (clone == null) throw new ArgumentNullException(nameof(clone));
            try
            {
                var descriptor = clone.GetComponents<Component>().FirstOrDefault(c => c != null &&
                    c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                if (VrChatExpressionMenu.Member(descriptor, "lipSync")?.ToString() != "VisemeParameterOnly") return null;
                var fx = VrChatExpressionMenu.Read(clone, new VrChatMenuImportPolicy { SkipPlayback = true }).Controller;
                var data = new LipSyncData { DominantViseme = true, TransitionSeconds = fx == null ? 0f : TransitionSeconds(ExpressionDependencies.Controller(fx)) };
                LipSyncData.Write(data); // Out-of-contract values fail here, not in Inject.
                return data;
            }
            catch (Exception error) when (!ExportStageGuard.IsCancellation(error))
            {
                warnings?.Add(ExporterLocalization.T(FailureWarning) + error.Message);
                return null;
            }
        }

        // The most common length in seconds, to the millisecond (the shorter on
        // a tie), of the transitions in every layer, sub state machine and Any
        // State whose conditions use Viseme; 0 without any. A normalized length
        // is scaled, as Animator does, by the clip length of the state it
        // leaves (the destination for Any State, whose source is not known), or
        // by 1 second when that state plays no clip or a clip of length 0.
        internal static float TransitionSeconds(AnimatorController fx)
        {
            if (fx == null) throw new ArgumentNullException(nameof(fx));
            var counts = new Dictionary<long, int>();
            var visited = new HashSet<AnimatorStateMachine>();
            void Count(AnimatorStateTransition transition, AnimatorState source)
            {
                if (transition == null || !transition.conditions.Any(condition => string.Equals(condition.parameter, VisemeParameter, StringComparison.Ordinal))) return;
                var seconds = (double)transition.duration;
                if (!transition.hasFixedDuration)
                    seconds *= (source ?? transition.destinationState)?.motion is AnimationClip clip && clip.length > 0 ? clip.length : 1;
                var milliseconds = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
                counts[milliseconds] = counts.TryGetValue(milliseconds, out var count) ? count + 1 : 1;
            }
            void Machine(AnimatorStateMachine machine)
            {
                // Each layer counts on its own; within a layer a machine reached twice counts once.
                if (machine == null || !visited.Add(machine)) return;
                foreach (var transition in machine.anyStateTransitions) Count(transition, null);
                foreach (var child in machine.states)
                    foreach (var transition in child.state?.transitions ?? Array.Empty<AnimatorStateTransition>()) Count(transition, child.state);
                foreach (var child in machine.stateMachines) Machine(child.stateMachine);
            }
            foreach (var layer in fx.layers) { visited.Clear(); Machine(layer.stateMachine); }
            if (counts.Count == 0) return 0f;
            return counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key / 1000f;
        }

        // Writes the data into the finished file.
        internal static byte[] Inject(byte[] bytes, LipSyncData data)
        {
            if (data == null) return bytes;
            var extension = LipSyncData.Write(data);
            var document = GlbDocument.Read(bytes);
            if (!document.Json.TryGetValue("extensions", out var rawExtensions)) document.Json["extensions"] = rawExtensions = new Dictionary<string, object>();
            ((Dictionary<string, object>)rawExtensions)[LipSyncData.Extension] = extension;
            if (!document.Json.TryGetValue("extensionsUsed", out var rawUsed)) document.Json["extensionsUsed"] = rawUsed = new List<object>();
            var used = (List<object>)rawUsed;
            if (!used.Contains(LipSyncData.Extension)) used.Add(LipSyncData.Extension);
            return document.Write();
        }
    }
}
