using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using VRVlog.Expressions;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // VRVLOG_expression_gestures: the board's faces that the FX shows for each
    // pair of hand gestures, read from the prepared copy after the build
    // passes. Every pair is played on a new graph from the parameter defaults,
    // with the FX layer controls and Parameter Drivers of the entered states
    // applied as VRChat applies them. The face clips playing then that do not
    // play at (0, 0) are the cell's references, in layer order. A fist hand's
    // trigger is also played at 0; a reference whose face moves with it
    // follows that trigger. A cell that cannot be settled is null.
    internal sealed class ExpressionGestureCapture
    {
        internal const string FailureWarning = "VR Vlog: ハンドジェスチャーの表情を書き出しませんでした: ";

        // Seconds of FX played per pair, in steps of one VRChat frame, so
        // transitions and pass-through states settle before the faces are read.
        const int Frames = 120;
        const float FrameStep = 1f / 60f;
        // The playing faces are read at this frame and at the end; a cell
        // whose faces differ between the two is not settled.
        const int SettleFrame = 100;
        const float ClipWeight = .01f, ShapeTolerance = .5f;
        const int EntryLimit = 4096;

        // VRChat's values for a local, standing avatar in VR.
        static readonly Dictionary<string, float> BuiltIns = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["IsLocal"] = 1, ["Grounded"] = 1, ["Upright"] = 1, ["VRMode"] = 1, ["TrackingType"] = 6, ["IsAnimatorEnabled"] = 1,
            ["ScaleFactor"] = 1, ["ScaleFactorInverse"] = 1, ["EyeHeightAsMeters"] = 1.6f, ["EyeHeightAsPercent"] = .5f,
        };

        // A playing face clip and the board faces it comes from; none when the
        // clip is not on the board.
        internal sealed class Reference
        {
            internal int[] Faces;
            internal string Drive;
        }

        internal readonly List<Reference>[] Cells = new List<Reference>[ExpressionGestureData.CellCount];
        // The clip of each board face as the exporter received it, which the
        // baked expressions keep as their Clip.
        readonly Object[] faceClips;

        ExpressionGestureCapture(Object[] faceClips) { this.faceClips = faceClips; }

        // faces lists the board's faces in order: the clip passed to the
        // exporter and the board's own clip it was copied from (or null).
        // Returns null when there is nothing to write; a failure is logged and
        // also returns null, so the export goes on without the extension.
        internal static ExpressionGestureCapture Capture(GameObject clone, IReadOnlyList<(Object Clip, Object Source)> faces,
            Func<Object, Object> isolatedCopyOf = null, object objectRegistry = null)
        {
            if (clone == null) throw new ArgumentNullException(nameof(clone));
            if (faces == null || faces.Count == 0) return null;
            try { return CaptureCore(clone, faces, isolatedCopyOf, objectRegistry); }
            catch (Exception error) when (!ExportStageGuard.IsCancellation(error))
            {
                Debug.LogWarning(FailureWarning + error.Message);
                return null;
            }
        }

        static ExpressionGestureCapture CaptureCore(GameObject clone, IReadOnlyList<(Object Clip, Object Source)> faces,
            Func<Object, Object> isolatedCopyOf, object objectRegistry)
        {
            var source = VrChatExpressionMenu.Read(clone, new VrChatMenuImportPolicy { SkipPlayback = true });
            var fx = source.Controller;
            if (fx == null) return null;
            var shapeBindings = new Dictionary<AnimationClip, HashSet<(string Path, string Property)>>();
            foreach (var clip in fx.animationClips.Where(clip => clip != null).Distinct())
            {
                var bindings = new HashSet<(string, string)>(AnimationUtility.GetCurveBindings(clip).Where(IsShape).Select(binding => (binding.path, binding.propertyName)));
                if (bindings.Count > 0) shapeBindings[clip] = bindings;
            }
            // A face clip comes from a board face when it is that face's clip,
            // the build's copy of it, or a clip NDMF records as made from either.
            var origins = new Dictionary<Object, HashSet<int>>();
            void Origin(Object value, int face)
            {
                if (value == null) return;
                if (!origins.TryGetValue(value, out var set)) origins[value] = set = new HashSet<int>();
                set.Add(face);
            }
            for (var face = 0; face < faces.Count; face++)
                foreach (var value in new[] { faces[face].Clip, faces[face].Source })
                {
                    Origin(value, face);
                    if (value != null) Origin(isolatedCopyOf?.Invoke(value), face);
                }
            var facesOf = new Dictionary<AnimationClip, int[]>();
            foreach (var clip in shapeBindings.Keys)
            {
                var found = new HashSet<int>();
                if (origins.TryGetValue(clip, out var direct)) found.UnionWith(direct);
                var origin = RegisteredOrigin(objectRegistry, clip);
                if (origin != null && origins.TryGetValue(origin, out var registered)) found.UnionWith(registered);
                facesOf[clip] = found.OrderBy(face => face).ToArray();
            }
            if (!facesOf.Values.Any(found => found.Length > 0)) return null;
            using var sampler = new Sampler(clone, fx, source, shapeBindings);
            if (!sampler.Types.ContainsKey("GestureLeft") && !sampler.Types.ContainsKey("GestureRight")) return null;

            var baseline = sampler.Play(0, 0, 0, 0);
            // Without a settled neutral nothing can be compared.
            if (baseline.Undetermined) return null;
            var neutral = new HashSet<(int, AnimationClip)>(baseline.Settling.Concat(baseline.Final).Select(playing => (playing.Layer, playing.Clip)));
            var result = new ExpressionGestureCapture(faces.Select(face => face.Clip).ToArray());
            for (var left = 0; left < ExpressionGestureData.Gestures; left++)
                for (var right = 0; right < ExpressionGestureData.Gestures; right++)
                {
                    // Only a fist (1) reads its trigger; a held fist is pressed fully.
                    var run = left == 0 && right == 0 ? baseline : sampler.Play(left, right, left == 1 ? 1 : 0, right == 1 ? 1 : 0);
                    result.Cells[ExpressionGestureData.Cell(left, right)] = Cell(sampler, run, baseline, neutral, facesOf, left, right);
                }
            return result.Cells.Any(cell => cell != null && cell.Any(reference => reference.Faces.Length > 0)) ? result : null;
        }

        static List<Reference> Cell(Sampler sampler, Sampler.Run run, Sampler.Run baseline, HashSet<(int, AnimationClip)> neutral,
            Dictionary<AnimationClip, int[]> facesOf, int left, int right)
        {
            if (run.Undetermined) return null;
            var settling = sampler.Candidates(run.Settling, neutral);
            var final = sampler.Candidates(run.Final, neutral);
            if (!settling.SequenceEqual(final)) return null;
            // A face that leaves every BlendShape as at (0, 0) shows nothing.
            var shown = final.Where(playing => sampler.Moves(playing.Clip, run, baseline)).ToList();
            if (shown.Count == 0) return new List<Reference>();
            var references = shown.Select(playing => new Reference { Faces = facesOf[playing.Clip] }).ToList();
            if (references.All(reference => reference.Faces.Length == 0)) return references;
            foreach (var (hand, gesture) in new[] { (ExpressionGestureData.DriveLeft, left), (ExpressionGestureData.DriveRight, right) })
            {
                if (gesture != 1) continue;
                var released = hand == ExpressionGestureData.DriveLeft ? sampler.Play(left, right, 0, right == 1 ? 1 : 0) : sampler.Play(left, right, left == 1 ? 1 : 0, 0);
                if (released.Undetermined) return null;
                for (var index = 0; index < shown.Count; index++)
                {
                    if (!sampler.Moves(shown[index].Clip, run, released)) continue;
                    // A face that follows both triggers has no single weight.
                    if (references[index].Drive != null) return null;
                    references[index].Drive = hand;
                }
            }
            return references;
        }

        // The table with the names the file gives the board's faces. A face
        // the file does not carry is not referenced; a cell whose faces are
        // all such faces, or whose clip names two different expressions, is
        // null. Returns null when no cell names an expression.
        internal ExpressionGestureData Resolve(Func<int, IEnumerable<string>> namesOf)
        {
            var data = new ExpressionGestureData();
            for (var index = 0; index < Cells.Length; index++)
            {
                var cell = Cells[index];
                if (cell == null) continue;
                var references = new List<ExpressionGestureData.Reference>();
                var dropped = false;
                var settled = true;
                foreach (var reference in cell)
                {
                    var names = reference.Faces.SelectMany(face => namesOf(face) ?? Enumerable.Empty<string>())
                        .Where(name => !string.IsNullOrEmpty(name) && name.Length <= 1024).Distinct(StringComparer.Ordinal).ToArray();
                    if (names.Length == 0) { dropped = true; continue; }
                    if (names.Length > 1) { settled = false; break; }
                    // A later reference overwrites an earlier one's BlendShapes.
                    references.RemoveAll(other => string.Equals(other.Expression, names[0], StringComparison.Ordinal));
                    references.Add(new ExpressionGestureData.Reference { Expression = names[0], Drive = reference.Drive });
                }
                if (!settled || dropped && references.Count == 0 || references.Count > ExpressionGestureData.ReferenceLimit) continue;
                data.Cells[index] = references;
            }
            return data.Cells.Any(cell => cell != null && cell.Count > 0) ? data : null;
        }

        // Writes the table into the finished file, naming each face as the
        // file's custom expressions do. A face merged into an identical one
        // takes that face's name.
        internal byte[] Inject(byte[] bytes, IEnumerable<VrmMenuExpressions.Expression> expressions)
        {
            var document = GlbDocument.Read(bytes);
            var written = new HashSet<string>(Member(document.Json, "extensions", "VRMC_vrm", "expressions", "custom")?.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var listed = (expressions ?? Enumerable.Empty<VrmMenuExpressions.Expression>()).Where(expression => expression?.Clip != null).ToList();
            var data = Resolve(face => listed.Where(expression => ReferenceEquals(expression.Clip, faceClips[face]))
                .Select(expression => expression.RegisteredName ?? expression.MergedInto).Where(name => name != null && written.Contains(name)));
            if (data == null) return bytes;
            var extension = ExpressionGestureData.Write(data);
            if (!document.Json.TryGetValue("extensions", out var rawExtensions)) document.Json["extensions"] = rawExtensions = new Dictionary<string, object>();
            ((Dictionary<string, object>)rawExtensions)[ExpressionGestureData.Extension] = extension;
            if (!document.Json.TryGetValue("extensionsUsed", out var rawUsed)) document.Json["extensionsUsed"] = rawUsed = new List<object>();
            var used = (List<object>)rawUsed;
            if (!used.Contains(ExpressionGestureData.Extension)) used.Add(ExpressionGestureData.Extension);
            return document.Write();
        }

        static Dictionary<string, object> Member(Dictionary<string, object> current, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (current == null || !current.TryGetValue(key, out var value)) return null;
                current = value as Dictionary<string, object>;
            }
            return current;
        }

        static bool IsShape(EditorCurveBinding binding) =>
            binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);

        // The asset NDMF records the clip as made from, read without creating a reference.
        static Object RegisteredOrigin(object registry, AnimationClip clip)
        {
            if (registry == null) return null;
            var contract = registry.GetType().GetInterfaces().FirstOrDefault(type => type.FullName == "nadena.dev.ndmf.IObjectRegistry");
            var method = contract?.GetMethod("GetReference", new[] { typeof(Object), typeof(bool) });
            if (method == null) return null;
            try
            {
                var reference = method.Invoke(registry, new object[] { clip, false });
                return reference?.GetType().GetProperty("Object", BindingFlags.Public | BindingFlags.Instance)?.GetValue(reference) as Object;
            }
            catch (TargetInvocationException) { return null; }
            catch (ArgumentException) { return null; }
        }

        // Plays the FX on one preview copy of the avatar. Every pair starts
        // from the copy's own objects and BlendShapes with a new graph, so
        // write-defaults-off states of an earlier pair do not leak into the next.
        internal sealed class Sampler : IDisposable
        {
            internal sealed class Run
            {
                internal bool Undetermined;
                internal List<(int Layer, AnimationClip Clip, float Weight)> Settling, Final;
                internal float[] Shapes;
            }

            readonly Scene scene;
            readonly GameObject copy;
            readonly Animator animator;
            readonly Transform[] transforms;
            readonly bool[] rest;
            readonly RuntimeAnimatorController fx;
            readonly AnimatorControllerLayer[] layers;
            readonly Dictionary<AnimationClip, HashSet<(string Path, string Property)>> shapeBindings;
            readonly List<(SkinnedMeshRenderer Renderer, int Index, float Weight)> shapes = new List<(SkinnedMeshRenderer, int, float)>();
            readonly Dictionary<(string Path, string Property), int> shapeIndex = new Dictionary<(string, string), int>();
            readonly Dictionary<(int Layer, int Hash), (StateMachineBehaviour[] Behaviours, string Location)> states = new Dictionary<(int, int), (StateMachineBehaviour[], string)>();
            readonly Dictionary<string, float> defaults = new Dictionary<string, float>(StringComparer.Ordinal);
            readonly ISet<string> expressionParameters;
            readonly HashSet<string> writable = new HashSet<string>(StringComparer.Ordinal);
            readonly HashSet<string> supplied;
            // Each behaviour read once: a layer control, a driver program, or the reason it cannot run.
            readonly Dictionary<StateMachineBehaviour, object> programs = new Dictionary<StateMachineBehaviour, object>();
            internal readonly Dictionary<string, AnimatorControllerParameterType> Types = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);

            internal Sampler(GameObject avatar, RuntimeAnimatorController fx, VrChatExpressionMenu.Source source,
                Dictionary<AnimationClip, HashSet<(string Path, string Property)>> shapeBindings)
            {
                this.fx = fx;
                this.shapeBindings = shapeBindings;
                layers = ExpressionDependencies.Controller(fx).layers;
                expressionParameters = source.ExpressionParameters;
                foreach (var pair in BuiltIns) defaults[pair.Key] = pair.Value;
                foreach (var pair in source.Defaults) defaults[pair.Key] = pair.Value;
                supplied = new HashSet<string>(defaults.Keys.Concat(new[] { "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight" }), StringComparer.Ordinal);
                for (var layer = 0; layer < layers.Length; layer++)
                {
                    var index = layer;
                    var synced = layers[layer].syncedLayerIndex;
                    var machine = synced >= 0 && synced < layers.Length ? layers[synced].stateMachine : layers[layer].stateMachine;
                    // Unity names a layer's root state machine after the layer.
                    void Walk(AnimatorStateMachine current, string path)
                    {
                        if (current == null) return;
                        foreach (var child in current.states)
                        {
                            if (child.state == null) continue;
                            var behaviours = synced >= 0 ? layers[index].GetOverrideBehaviours(child.state) : child.state.behaviours;
                            states[(index, Animator.StringToHash(path + "." + child.state.name))] = (behaviours ?? Array.Empty<StateMachineBehaviour>(), layers[index].name + " / " + child.state.name);
                        }
                        foreach (var child in current.stateMachines) Walk(child.stateMachine, path + "." + child.stateMachine?.name);
                    }
                    Walk(machine, layers[layer].name);
                }
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    copy = Object.Instantiate(avatar);
                    copy.name = avatar.name;
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    SceneManager.MoveGameObjectToScene(copy, scene);
                    foreach (var behaviour in copy.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                    animator = copy.GetComponent<Animator>();
                    if (animator == null) animator = copy.AddComponent<Animator>();
                    animator.runtimeAnimatorController = null;
                    animator.enabled = true;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false;
                    animator.fireEvents = false;
                    copy.SetActive(true);
                    transforms = copy.GetComponentsInChildren<Transform>(true);
                    rest = transforms.Select(transform => transform.gameObject.activeSelf).ToArray();
                    foreach (var key in shapeBindings.Values.SelectMany(bindings => bindings).Distinct())
                    {
                        var target = key.Path.Length == 0 ? copy.transform : copy.transform.Find(key.Path);
                        var renderer = target == null ? null : target.GetComponent<SkinnedMeshRenderer>();
                        var index = renderer == null || renderer.sharedMesh == null ? -1 : renderer.sharedMesh.GetBlendShapeIndex(key.Property.Substring("blendShape.".Length));
                        if (index < 0) continue;
                        shapeIndex[key] = shapes.Count;
                        shapes.Add((renderer, index, renderer.GetBlendShapeWeight(index)));
                    }
                    var graph = PlayableGraph.Create("VR Vlog hand gesture parameters");
                    try
                    {
                        var playable = AnimatorControllerPlayable.Create(graph, fx);
                        for (var i = 0; i < playable.GetParameterCount(); i++)
                        {
                            var parameter = playable.GetParameter(i);
                            if (!Types.ContainsKey(parameter.name)) Types[parameter.name] = parameter.type;
                        }
                    }
                    finally { graph.Destroy(); }
                    foreach (var pair in Types)
                        if (pair.Value != AnimatorControllerParameterType.Trigger && !VrChatParameterDriver.BuiltIn.Contains(pair.Key)) writable.Add(pair.Key);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal Run Play(int left, int right, float leftWeight, float rightWeight)
            {
                for (var i = 0; i < transforms.Length; i++) transforms[i].gameObject.SetActive(rest[i]);
                foreach (var shape in shapes) shape.Renderer.SetBlendShapeWeight(shape.Index, shape.Weight);
                var graph = PlayableGraph.Create("VR Vlog hand gesture sampling");
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimatorControllerPlayable.Create(graph, fx);
                    AnimationPlayableOutput.Create(graph, "Hand gestures", animator).SetSourcePlayable(playable);
                    foreach (var pair in defaults) Write(playable, pair.Key, pair.Value);
                    // VRChat sends the hands every frame.
                    void Gestures()
                    {
                        Write(playable, "GestureLeft", left);
                        Write(playable, "GestureRight", right);
                        Write(playable, "GestureLeftWeight", leftWeight);
                        Write(playable, "GestureRightWeight", rightWeight);
                    }
                    Gestures();
                    graph.Play();
                    graph.Evaluate(0f);
                    var run = new Run();
                    var entered = new int[Math.Min(layers.Length, playable.GetLayerCount())];
                    var entries = 0;
                    // The behaviours of a state run when the layer enters it,
                    // at the start of the transition into it.
                    void Enter()
                    {
                        for (var layer = 0; layer < entered.Length && !run.Undetermined; layer++)
                        {
                            var info = playable.IsInTransition(layer) ? playable.GetNextAnimatorStateInfo(layer) : playable.GetCurrentAnimatorStateInfo(layer);
                            if (info.fullPathHash == entered[layer]) continue;
                            entered[layer] = info.fullPathHash;
                            if (!states.TryGetValue((layer, info.fullPathHash), out var state)) continue;
                            foreach (var behaviour in state.Behaviours)
                                if (behaviour != null && (++entries > EntryLimit || !Apply(playable, behaviour, state.Location))) { run.Undetermined = true; return; }
                        }
                    }
                    Enter();
                    for (var frame = 1; frame <= Frames && !run.Undetermined; frame++)
                    {
                        Gestures();
                        graph.Evaluate(FrameStep);
                        Enter();
                        if (frame == SettleFrame) run.Settling = Playing(playable);
                    }
                    if (run.Undetermined) return run;
                    run.Final = Playing(playable);
                    run.Shapes = shapes.Select(shape => shape.Renderer.GetBlendShapeWeight(shape.Index)).ToArray();
                    return run;
                }
                finally { graph.Destroy(); }
            }

            // Applies an FX layer control or a Parameter Driver. False when
            // the result cannot be known: a Random operation, or a setting
            // the SDK would reject.
            bool Apply(AnimatorControllerPlayable playable, StateMachineBehaviour behaviour, string location)
            {
                if (!programs.TryGetValue(behaviour, out var program))
                {
                    try
                    {
                        program = (object)SdkLayerWeightControl.Read(behaviour, location, layers.Length) ??
                            (VrChatParameterDriver.IsDriver(behaviour) ? VrChatParameterDriver.Read(behaviour, location) : null);
                    }
                    catch (InvalidOperationException error) { program = error; }
                    programs[behaviour] = program;
                }
                switch (program)
                {
                    case SdkLayerWeightControl control:
                        // The base layer stays at full weight.
                        if (control.AnimatorLayer && control.Playable == "FX" && control.LayerIndex > 0 && control.LayerIndex < layers.Length)
                            playable.SetLayerWeight(control.LayerIndex, control.GoalWeight);
                        return true;
                    case VrChatParameterDriver.Program driver:
                        try
                        {
                            VrChatParameterDriver.Execute(driver, Types, expressionParameters, writable, true,
                                name => Read(playable, name), (name, value) => Write(playable, name, (float)value), supplied);
                            return true;
                        }
                        catch (InvalidOperationException) { return false; }
                    case Exception _:
                        return false;
                    default:
                        return true;
                }
            }

            double Read(AnimatorControllerPlayable playable, string name)
            {
                switch (Types[name])
                {
                    case AnimatorControllerParameterType.Bool: return playable.GetBool(name) ? 1 : 0;
                    case AnimatorControllerParameterType.Int: return playable.GetInteger(name);
                    case AnimatorControllerParameterType.Float: return playable.GetFloat(name);
                    default: return 0;
                }
            }

            void Write(AnimatorControllerPlayable playable, string name, float value)
            {
                if (!Types.TryGetValue(name, out var type)) return;
                switch (type)
                {
                    case AnimatorControllerParameterType.Bool: playable.SetBool(name, value != 0f); break;
                    case AnimatorControllerParameterType.Int: playable.SetInteger(name, Mathf.RoundToInt(value)); break;
                    case AnimatorControllerParameterType.Float: playable.SetFloat(name, value); break;
                }
            }

            // The face clips playing on each layer with weight, in layer order.
            List<(int Layer, AnimationClip Clip, float Weight)> Playing(AnimatorControllerPlayable playable)
            {
                var result = new List<(int, AnimationClip, float)>();
                for (var layer = 0; layer < Math.Min(layers.Length, playable.GetLayerCount()); layer++)
                {
                    var weight = layer == 0 ? 1f : playable.GetLayerWeight(layer);
                    if (weight <= 0f) continue;
                    var clips = playable.GetCurrentAnimatorClipInfo(layer).AsEnumerable();
                    if (playable.IsInTransition(layer)) clips = clips.Concat(playable.GetNextAnimatorClipInfo(layer));
                    foreach (var info in clips)
                        if (info.clip != null && info.weight > ClipWeight && shapeBindings.ContainsKey(info.clip) &&
                            !result.Any(other => other.Item1 == layer && other.Item2 == info.clip))
                            result.Add((layer, info.clip, weight));
                }
                return result;
            }

            // The playing face clips that do not play at (0, 0) and that no
            // later layer hides: a later override layer at full weight whose
            // clips set every BlendShape of this one.
            internal List<(int Layer, AnimationClip Clip)> Candidates(List<(int Layer, AnimationClip Clip, float Weight)> playing, HashSet<(int, AnimationClip)> neutral)
            {
                var result = new List<(int, AnimationClip)>();
                foreach (var (layer, clip, _) in playing)
                {
                    if (neutral.Contains((layer, clip))) continue;
                    var later = playing.Where(other => other.Layer > layer && other.Weight >= .999f &&
                        layers[other.Layer].blendingMode == AnimatorLayerBlendingMode.Override).SelectMany(other => shapeBindings[other.Clip]);
                    if (shapeBindings[clip].IsSubsetOf(later)) continue;
                    result.Add((layer, clip));
                }
                return result;
            }

            // Whether a BlendShape the clip sets ends differently in the two runs.
            internal bool Moves(AnimationClip clip, Run first, Run second) =>
                shapeBindings[clip].Any(key => shapeIndex.TryGetValue(key, out var index) && Math.Abs(first.Shapes[index] - second.Shapes[index]) > ShapeTolerance);

            public void Dispose()
            {
                if (copy != null) Object.DestroyImmediate(copy);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
