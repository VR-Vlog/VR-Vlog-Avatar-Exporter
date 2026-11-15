using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using VRVlog.AvatarAnimator;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // After the export: plays the copy's FX controller in Unity's Animator and
    // the written VRVLOG_avatar_animator in AvatarAnimatorEvaluator, the app's
    // player, from the same inputs for the same frames, and compares what they
    // show: the activeSelf of each active target, the BlendShape weight of
    // each morph target, the enabled flag of each renderer an enabled target
    // names, the float of each material target (materialFloat,
    // materialColor, materialTextureST) as the renderer it was read from
    // shows it, the material of each materialSlot target's slot and the
    // local channel of each translation, rotation and scale target. The inputs are the start alone, each menu control and
    // each hand gesture. Each input starts from the rest: SettleFrames without
    // input, then InputFrames with it.
    //
    // Unity runs VRChat's behaviours only through the SDK's hooks; these are
    // hooked for the run: a Parameter Driver applies its operations as the
    // evaluator does, from the same random sequence, and an Animator Layer
    // Control on the FX blends that layer's weight from the next frame (M10).
    // An input that starts a Playable Layer Control on the FX is not compared:
    // plain Unity has no FX weight to blend.
    internal static class AvatarAnimatorCrossCheck
    {
        // Off skips the check; the file is the same either way.
        internal static bool Enabled = true;
        internal const int SettleFrames = 30, InputFrames = 150;
        internal const double FrameSeconds = (float)(1.0 / 60);
        // Unity blends BlendShape weights (0 to 100) in single precision.
        internal const double WeightTolerance = 0.05;
        // Material floats and texture scales and offsets, also blended in
        // single precision, per unit of size.
        internal const double FloatTolerance = 1e-4;
        // Material color channels: one step of an 8-bit channel.
        internal const double ColorTolerance = 1.0 / 255;
        // Transform channels: 1 mm of a position or scale, 0.1 degree of an
        // angle, and the quaternion channel of a turn of 0.1 degree.
        internal const double PositionTolerance = 0.001, AngleTolerance = 0.1;
        internal static readonly double QuaternionTolerance = Math.Sin(AngleTolerance / 2 * Math.PI / 180);
        // A face the evaluator shows (FaceMismatch): a BlendShape weight within
        // half a step of 0 to 100; the other kinds as above.
        internal const double FaceWeightTolerance = 0.5;
        // The time the inputs may take together; the rest are not compared.
        internal static double BudgetSeconds = 60;
        // Off leaves VRChat's behaviours to plain Unity, which runs none (tests).
        internal static bool HookBehaviours = true;
        // Names listed in the result line; the others are counted.
        internal const int ListedInputs = 8;

        // The result line: {0} the inputs that differ, {1} how many differ, {2} how many were compared.
        internal const string MismatchNote = "アプリの再生がUnityと合わないメニューの項目・手の形（{2}項目中{1}項目）: {0}";
        internal const string MoreInputs = "ほか{0}件";
        internal const string StartInput = "入力なし";
        internal const string MenuInput = "メニュー: {0}";
        internal const string GestureInput = "{0}の手の形: {1}";
        internal const string LeftHand = "左手", RightHand = "右手";
        // The start of the log line that the regression runner keeps.
        internal const string LogPrefix = "VR Vlog animator check: ";

        static readonly string[] Gestures = { "Neutral", "Fist", "HandOpen", "FingerPoint", "Victory", "RockNRoll", "HandGun", "ThumbsUp" };

        // What shows a target in Unity: the object for an active target, the
        // renderer and shape for a morph target, the renderer (Component) for
        // an enabled target, a renderer that draws with the material
        // (Component, Material) and the property Name for a material target,
        // the renderer (Component), its Slot and the glTF index of each
        // material the slot may hold (Slots) for a materialSlot target, and
        // the object for a translation, rotation or scale target. Null for a
        // target that is not compared (a parameter, or what the copy no
        // longer has).
        internal sealed class Binding
        {
            internal GameObject Object;
            internal SkinnedMeshRenderer Renderer;
            internal string Shape;
            internal Renderer Component;
            internal Material Material;
            internal string Name;
            internal int Slot = -1;
            internal Dictionary<Material, int> Slots;
            internal GameObject Owner => Object != null ? Object : Renderer != null ? Renderer.gameObject : Component != null ? Component.gameObject : null;
            // The object's path under the root when the clips were read (null
            // when unknown). Unity binds curves by path, so an object the
            // export renamed or moved no longer moves with the controller.
            internal string Path;
        }

        // Values by parameter index, set after SettleFrames.
        internal sealed class Input
        {
            internal string Name;
            internal readonly List<(int Parameter, double Value)> Values = new List<(int, double)>();
        }

        internal sealed class Mismatch
        {
            internal string Input;
            internal int Frame, Target;
            internal double Unity, Evaluator;
            // The layers whose state or weight differs at that frame, for the log.
            internal string Layers;
        }

        internal sealed class Result
        {
            // Why the evaluator cannot play the data; nothing is compared then.
            internal string Unsupported;
            internal int Inputs, Compared, Targets, Unbound, Moved;
            // Animators under the root besides the one that plays, for the log.
            internal int OtherAnimators;
            // Inputs left out for a Playable Layer Control, and for time.
            internal readonly List<string> Skipped = new List<string>();
            internal int OutOfTime;
            // The first difference of each input that differs.
            internal readonly List<Mismatch> Mismatches = new List<Mismatch>();
            internal double Seconds;
        }

        // The start, every menu control that sets a parameter (in menu order,
        // submenus where they open) and each gesture of either hand. A toggle
        // or button sets its value (a toggle on at the start goes to 0), a
        // radial its parameter to 1 (0 from 0.5 up), an axis puppet each of its
        // parameters to 1. Inputs that set the same values are listed once.
        internal static List<Input> Inputs(AvatarAnimatorData data)
        {
            var inputs = new List<Input> { new Input { Name = ExporterLocalization.T(StartInput) } };
            var seen = new HashSet<string> { "" };
            void Add(Input input)
            {
                if (input.Values.Count == 0) return;
                var key = string.Join(";", input.Values.Select(v => v.Parameter + "=" + v.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                if (seen.Add(key)) inputs.Add(input);
            }
            double Default(int parameter) => data.Parameters[parameter].Default;
            var walked = new HashSet<int>();
            void Walk(int menu, string path)
            {
                if (menu < 0 || menu >= data.Menus.Count || !walked.Add(menu)) return;
                foreach (var control in data.Menus[menu].Controls)
                {
                    var name = path.Length == 0 ? control.Name : path + "/" + control.Name;
                    var input = new Input { Name = ExporterLocalization.Format(MenuInput, name) };
                    if (control.Parameter is int parameter)
                        input.Values.Add((parameter, control.Type == "toggle" && Default(parameter) == control.Value ? 0 : control.Value));
                    var subs = control.SubParameters.Where(p => p >= 0 && p < data.Parameters.Count).ToList();
                    if (control.Type == "radial" && subs.Count > 0) input.Values.Add((subs[0], Default(subs[0]) < .5 ? 1 : 0));
                    if (control.Type == "twoAxis" || control.Type == "fourAxis") foreach (var sub in subs) input.Values.Add((sub, 1));
                    Add(input);
                    if (control.Type == "subMenu" && control.Menu is int child) Walk(child, name);
                }
            }
            Walk(0, "");
            foreach (var (side, label) in new[] { ("Left", LeftHand), ("Right", RightHand) })
            {
                var gesture = Index(data, "Gesture" + side, "int");
                if (gesture < 0) continue;
                var weight = Index(data, "Gesture" + side + "Weight", "float");
                for (var g = 1; g < Gestures.Length; g++)
                {
                    var input = new Input { Name = ExporterLocalization.Format(GestureInput, ExporterLocalization.T(label), Gestures[g]) };
                    input.Values.Add((gesture, g));
                    if (weight >= 0) input.Values.Add((weight, 1));
                    Add(input);
                }
            }
            return inputs;
        }

        static int Index(AvatarAnimatorData data, string name, string type)
        {
            for (var i = 0; i < data.Parameters.Count; i++)
                if (data.Parameters[i].Name == name) return data.Parameters[i].Type == type ? i : -1;
            return -1;
        }

        // Plays both from each input. root holds the Animator (added when
        // missing) and must be active; controller is the FX controller the
        // data was read from; bindings follow data.Targets. readBehaviour reads
        // a behaviour Unity runs into the data's form (null to ignore it).
        // Leaves root's objects and Animator as the last input left them.
        internal static Result Run(GameObject root, RuntimeAnimatorController controller, AvatarAnimatorData data,
            IReadOnlyList<Binding> bindings, Func<StateMachineBehaviour, AvatarAnimatorData.Behaviour> readBehaviour)
        {
            var result = new Result();
            if (!AvatarAnimatorEvaluator.Supports(data, out var reason)) { result.Unsupported = reason; return result; }
            var clock = Stopwatch.StartNew();
            var shown = new List<int>();
            var shapes = new int[data.Targets.Count];
            var quaternions = Quaternions(data);
            for (var t = 0; t < data.Targets.Count; t++)
            {
                var target = data.Targets[t];
                var binding = t < bindings.Count ? bindings[t] : null;
                // A muscle is compared through the pose, which this check does not read yet.
                if (target.Property == "parameter" || target.Property == "muscle") continue;
                result.Targets++;
                shapes[t] = -1;
                if (target.Property == "active" && binding?.Object != null) shown.Add(t);
                else if (target.Property == "morph" && binding?.Renderer != null && binding.Renderer.sharedMesh != null &&
                         (shapes[t] = binding.Renderer.sharedMesh.GetBlendShapeIndex(binding.Shape)) >= 0) shown.Add(t);
                else if (target.Property == "enabled" && binding?.Component != null) shown.Add(t);
                else if (IsMaterial(target.Property) && binding?.Component != null && binding.Material != null && binding.Name != null) shown.Add(t);
                else if (target.Property == "materialSlot" && binding?.Component != null && binding.Slots != null &&
                         binding.Slot >= 0 && binding.Slot < binding.Component.sharedMaterials.Length) shown.Add(t);
                else if (IsTransform(target.Property) && binding?.Object != null) shown.Add(t);
                else { result.Unbound++; continue; }
                if (binding.Path != null && Find(root, binding.Path) != binding.Owner)
                {
                    shown.RemoveAt(shown.Count - 1);
                    result.Moved++;
                }
            }
            var inputs = Inputs(data);
            result.Inputs = inputs.Count;

            if (!root.TryGetComponent<Animator>(out var animator)) animator = root.AddComponent<Animator>();
            result.OtherAnimators = root.GetComponentsInChildren<Animator>(true).Count(other => other != animator);
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var player = new UnityPlayer(animator, data, readBehaviour);
            using (HookBehaviours ? new Hooks(player) : null)
            {
                // Unity takes the values Write Defaults brings back when the
                // controller binds, and Rebind restores those: the rest goes in
                // first, as the export may have left the copy elsewhere.
                animator.runtimeAnimatorController = null;
                ToRest(data, bindings, shown, shapes);
                animator.runtimeAnimatorController = controller;
                player.Read();
                foreach (var input in inputs)
                {
                    // The first input always runs, so a slow avatar still shows something.
                    if (input != inputs[0] && clock.Elapsed.TotalSeconds > BudgetSeconds) { result.OutOfTime++; continue; }
                    var mismatch = Play(input, data, bindings, shown, shapes, quaternions, animator, player, out var skipped);
                    if (skipped) { result.Skipped.Add(input.Name); continue; }
                    result.Compared++;
                    if (mismatch != null) result.Mismatches.Add(mismatch);
                }
            }
            result.Seconds = clock.Elapsed.TotalSeconds;
            return result;
        }

        // The object Unity's Animator binds a curve path to, as the capture read it.
        static GameObject Find(GameObject root, string path)
        {
            var found = path.Length == 0 ? root.transform : root.transform.Find(path);
            return found == null ? null : found.gameObject;
        }

        static void ToRest(AvatarAnimatorData data, IReadOnlyList<Binding> bindings, List<int> shown, int[] shapes)
        {
            // The channels of one Transform go in together, so a rotation is
            // never set from a half-changed quaternion.
            var poses = new Dictionary<(Transform, string, bool), double[]>();
            foreach (var t in shown)
            {
                var rest = data.Targets[t].Rest;
                switch (data.Targets[t].Property)
                {
                    case "translation":
                    case "rotation":
                    case "scale":
                        var target = data.Targets[t];
                        var key = (bindings[t].Object.transform, target.Property, target.Euler);
                        if (!poses.TryGetValue(key, out var channels)) poses[key] = channels = TransformChannels(key.Item1, target.Property, target.Euler);
                        channels[Array.IndexOf(Channels(target), target.Channel)] = rest;
                        break;
                    case "materialSlot":
                        var materials = bindings[t].Component.sharedMaterials;
                        materials[bindings[t].Slot] = bindings[t].Slots.FirstOrDefault(pair => pair.Value == (int)rest).Key;
                        bindings[t].Component.sharedMaterials = materials;
                        break;
                    case "active": bindings[t].Object.SetActive(AvatarAnimatorEvaluator.Active(rest)); break;
                    case "morph": bindings[t].Renderer.SetBlendShapeWeight(shapes[t], (float)rest); break;
                    case "enabled": bindings[t].Component.enabled = AvatarAnimatorEvaluator.Active(rest); break;
                    case "materialFloat":
                    case "materialColor":
                    case "materialTextureST":
                        // The animated value lives in the renderer's property block.
                        var block = new MaterialPropertyBlock();
                        bindings[t].Component.GetPropertyBlock(block);
                        SetChannel(block, data.Targets[t], bindings[t], (float)rest);
                        bindings[t].Component.SetPropertyBlock(block);
                        break;
                }
            }
            foreach (var pair in poses)
            {
                var (transform, property, euler) = pair.Key;
                var c = pair.Value;
                if (property == "translation") transform.localPosition = new Vector3((float)c[0], (float)c[1], (float)c[2]);
                else if (property == "scale") transform.localScale = new Vector3((float)c[0], (float)c[1], (float)c[2]);
                else if (euler) UnityEditor.TransformUtils.SetInspectorRotation(transform, new Vector3((float)c[0], (float)c[1], (float)c[2]));
                else transform.localRotation = new Quaternion((float)c[0], (float)c[1], (float)c[2], (float)c[3]);
            }
        }

        static bool IsTransform(string property) => property == "translation" || property == "rotation" || property == "scale";

        static string[] Channels(AvatarAnimatorData.Target target) =>
            target.Property == "rotation" && !target.Euler ? AvatarAnimatorData.QuaternionChannels : AvatarAnimatorData.VectorChannels;

        // The local channels of a Transform in Unity's space, as the capture
        // reads a rest: x, y, z, or x, y, z, w of a quaternion.
        static double[] TransformChannels(Transform transform, string property, bool euler)
        {
            if (property == "rotation" && !euler)
            {
                var q = transform.localRotation;
                return new double[] { q.x, q.y, q.z, q.w };
            }
            var v = property == "translation" ? transform.localPosition : property == "scale" ? transform.localScale
                : UnityEditor.TransformUtils.GetInspectorRotation(transform);
            return new double[] { v.x, v.y, v.z };
        }

        static bool IsMaterial(string property) => property == "materialFloat" || property == "materialColor" || property == "materialTextureST";

        // Unity animates a color channel as the whole color and a texture's
        // scale and offset as the vector Name_ST (scale in x, y, offset in z,
        // w) in the property block; the other channels come from the block, or
        // from the material when the block has none.
        static void SetChannel(MaterialPropertyBlock block, AvatarAnimatorData.Target target, Binding binding, float value)
        {
            switch (target.Property)
            {
                case "materialFloat": block.SetFloat(binding.Name, value); break;
                case "materialColor":
                    var color = BlockColor(block, binding);
                    color[Array.IndexOf(AvatarAnimatorData.ColorChannels, target.Channel)] = value;
                    block.SetColor(binding.Name, color);
                    break;
                default:
                    var st = BlockTextureST(block, binding);
                    st[Array.IndexOf(AvatarAnimatorData.TextureChannels, target.Channel)] = value;
                    block.SetVector(binding.Name + "_ST", st);
                    break;
            }
        }

        static Color BlockColor(MaterialPropertyBlock block, Binding binding) =>
            block.HasColor(binding.Name) ? block.GetColor(binding.Name) : binding.Material.GetColor(binding.Name);

        static Vector4 BlockTextureST(MaterialPropertyBlock block, Binding binding)
        {
            if (block.HasVector(binding.Name + "_ST")) return block.GetVector(binding.Name + "_ST");
            var scale = binding.Material.GetTextureScale(binding.Name);
            var offset = binding.Material.GetTextureOffset(binding.Name);
            return new Vector4(scale.x, scale.y, offset.x, offset.y);
        }

        // What Unity shows of a target, in the evaluator's units.
        static double Shown(AvatarAnimatorData.Target target, Binding binding, int shape)
        {
            switch (target.Property)
            {
                case "active": return binding.Object.activeSelf ? 1 : 0;
                case "enabled": return binding.Component.enabled ? 1 : 0;
                case "materialFloat":
                    var block = new MaterialPropertyBlock();
                    binding.Component.GetPropertyBlock(block);
                    return block.HasFloat(binding.Name) ? block.GetFloat(binding.Name) : binding.Material.GetFloat(binding.Name);
                case "materialColor":
                    var colors = new MaterialPropertyBlock();
                    binding.Component.GetPropertyBlock(colors);
                    return BlockColor(colors, binding)[Array.IndexOf(AvatarAnimatorData.ColorChannels, target.Channel)];
                case "materialTextureST":
                    var transforms = new MaterialPropertyBlock();
                    binding.Component.GetPropertyBlock(transforms);
                    return BlockTextureST(transforms, binding)[Array.IndexOf(AvatarAnimatorData.TextureChannels, target.Channel)];
                case "materialSlot":
                    // A material the slot may not hold reads -2, which no value is.
                    var held = binding.Component.sharedMaterials[binding.Slot];
                    return held == null ? -1 : binding.Slots.TryGetValue(held, out var index) ? index : -2;
                case "translation":
                case "rotation":
                case "scale":
                    return TransformChannels(binding.Object.transform, target.Property, target.Euler)[Array.IndexOf(Channels(target), target.Channel)];
                default: return binding.Renderer.GetBlendShapeWeight(shape);
            }
        }

        static bool Differs(AvatarAnimatorData.Target target, double evaluator, double unity) => Differs(target, evaluator, unity, WeightTolerance);

        // weight is the tolerance of a BlendShape weight.
        static bool Differs(AvatarAnimatorData.Target target, double evaluator, double unity, double weight) =>
            target.Property == "active" || target.Property == "enabled" ? AvatarAnimatorEvaluator.Active(evaluator) != (unity != 0)
            : target.Property == "materialColor" ? !(Math.Abs(evaluator - unity) <= ColorTolerance)
            : target.Property == "materialFloat" || target.Property == "materialTextureST" ? !(Math.Abs(evaluator - unity) <= FloatTolerance * Math.Max(1, Math.Abs(unity)))
            : target.Property == "materialSlot" ? evaluator != unity
            : target.Property == "translation" || target.Property == "scale" ? !(Math.Abs(evaluator - unity) <= PositionTolerance)
            : target.Property == "rotation" && target.Euler ? !(Math.Abs(evaluator - unity) <= AngleTolerance)
            : target.Property == "rotation" ? !(Math.Abs(evaluator - unity) <= QuaternionTolerance)
            : !(Math.Abs(evaluator - unity) <= weight);

        // The x, y, z and w targets of each quaternion rotation whose four
        // channels are all targets, by each of the four.
        static Dictionary<int, int[]> Quaternions(AvatarAnimatorData data)
        {
            var result = new Dictionary<int, int[]>();
            for (var t = 0; t < data.Targets.Count; t++)
            {
                var target = data.Targets[t];
                if (target.Property != "rotation" || target.Euler || target.Channel != AvatarAnimatorData.QuaternionChannels[0]) continue;
                var four = AvatarAnimatorData.QuaternionChannels.Select(channel => data.Targets.FindIndex(other =>
                    other.Node == target.Node && other.Property == "rotation" && !other.Euler && other.Channel == channel)).ToArray();
                if (four.Any(index => index < 0)) continue;
                foreach (var index in four) result[index] = four;
            }
            return result;
        }

        // A quaternion channel of the evaluator as a Transform takes it:
        // normalized (a blend of two rotations is shorter than 1), on the side
        // of Unity's quaternion, as q and -q are one rotation.
        static double Normalized(IReadOnlyList<double> values, int[] four, int channel, double[] unity)
        {
            double length = 0, dot = 0;
            for (var c = 0; c < 4; c++)
            {
                length += values[four[c]] * values[four[c]];
                dot += values[four[c]] * unity[c];
            }
            return length == 0 ? values[four[channel]] : (dot < 0 ? -1 : 1) * values[four[channel]] / Math.Sqrt(length);
        }

        // Whether the evaluator shows a face of data.Expressions as its VRM
        // expression does (B of the design's conditions for moving a face to
        // the evaluator): from every parameter at its default, SettleFrames,
        // then the values AvatarAnimatorFaces picks for the face (as the app
        // does) and InputFrames, after which each of face, a target and the
        // value the VRM expression gives it, must match within the
        // tolerances (a BlendShape weight within FaceWeightTolerance). Null
        // when it matches, else why not, for the log.
        internal static string FaceMismatch(AvatarAnimatorData data, string name, IReadOnlyList<(int Target, double Value)> face)
        {
            if (!AvatarAnimatorEvaluator.Supports(data, out var reason)) return "unsupported " + reason;
            var random = new System.Random(name.Aggregate(17, (hash, c) => unchecked(hash * 31 + c)));
            var evaluator = new AvatarAnimatorEvaluator(data, () => random.NextDouble());
            for (var frame = 0; frame < SettleFrames; frame++) evaluator.Advance(FrameSeconds);
            var values = AvatarAnimatorFaces.Solve(data, name, evaluator.Get);
            if (values == null) return "no way in";
            foreach (var pair in values) evaluator.Set(pair.Key, pair.Value);
            for (var frame = 0; frame < InputFrames; frame++) evaluator.Advance(FrameSeconds);
            foreach (var (target, value) in face)
                if (Differs(data.Targets[target], evaluator.Values[target], value, FaceWeightTolerance))
                    return "T" + target + " face " + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                           " evaluator " + evaluator.Values[target].ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                           " with " + string.Join(", ", values.Select(pair => data.Parameters[pair.Key].Name + " " + pair.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            return null;
        }

        static Mismatch Play(Input input, AvatarAnimatorData data, IReadOnlyList<Binding> bindings, List<int> shown, int[] shapes,
            Dictionary<int, int[]> quaternions, Animator animator, UnityPlayer player, out bool skipped)
        {
            // Both draw the same sequence; a different order of behaviours shows as a difference.
            var seed = input.Name.Aggregate(17, (hash, c) => unchecked(hash * 31 + c));
            var evaluatorRandom = new System.Random(seed);
            var evaluator = new AvatarAnimatorEvaluator(data, () => evaluatorRandom.NextDouble());
            ToRest(data, bindings, shown, shapes);
            player.Start(new System.Random(seed));
            Mismatch first = null;
            for (var frame = 0; frame < SettleFrames + InputFrames; frame++)
            {
                if (frame == SettleFrames)
                    foreach (var (parameter, value) in input.Values)
                    {
                        evaluator.Set(parameter, value);
                        player.Set(parameter, value);
                    }
                player.Advance(FrameSeconds);
                evaluator.Advance(FrameSeconds);
                if (player.PlayableLayerControlled) { skipped = true; return null; }
                if (first != null) continue;
                foreach (var t in shown)
                {
                    var unity = Shown(data.Targets[t], bindings[t], shapes[t]);
                    var value = quaternions.TryGetValue(t, out var four)
                        ? Normalized(evaluator.Values, four, Array.IndexOf(AvatarAnimatorData.QuaternionChannels, data.Targets[t].Channel),
                            TransformChannels(bindings[t].Object.transform, "rotation", false))
                        : evaluator.Values[t];
                    if (!Differs(data.Targets[t], value, unity)) continue;
                    first = new Mismatch { Input = input.Name, Frame = frame, Target = t, Unity = unity, Evaluator = value, Layers = Layers(data, animator, evaluator) };
                    break;
                }
            }
            skipped = false;
            return first;
        }

        // Each layer where Unity and the evaluator are in a different state or
        // at a different weight: "index name: unity state weight / evaluator
        // state weight". A state in a transition reads "from>to".
        static string Layers(AvatarAnimatorData data, Animator animator, AvatarAnimatorEvaluator evaluator)
        {
            if (data.Controllers.Count == 0) return "";
            var layers = data.Controllers[0].Layers;
            var parts = new List<string>();
            for (var l = 0; l < layers.Count && l < animator.layerCount && l < evaluator.LayerCount; l++)
            {
                var states = layers[l].States;
                if (states.Count == 0) continue;
                string Named(int hash) => states.FirstOrDefault(s => Animator.StringToHash(s.Name) == hash)?.Name ?? "?";
                string At(int index) => index >= 0 && index < states.Count ? states[index].Name : "-";
                var unity = Named(animator.GetCurrentAnimatorStateInfo(l).shortNameHash);
                if (animator.IsInTransition(l)) unity += ">" + Named(animator.GetNextAnimatorStateInfo(l).shortNameHash);
                var app = At(evaluator.State(l));
                if (evaluator.NextState(l) >= 0) app += ">" + At(evaluator.NextState(l));
                var unityWeight = l == 0 ? 1 : animator.GetLayerWeight(l);
                if (unity == app && Math.Abs(unityWeight - evaluator.LayerWeight(l)) < 0.001) continue;
                parts.Add($"{l} {layers[l].Name}: unity {unity} {unityWeight:0.###} / evaluator {app} {evaluator.LayerWeight(l):0.###}");
            }
            return string.Join("; ", parts);
        }

        // The result line, or null when every input compared matches.
        internal static string ResultLine(Result result)
        {
            if (result == null || result.Mismatches.Count == 0) return null;
            var names = result.Mismatches.Take(ListedInputs).Select(m => m.Input).ToList();
            if (result.Mismatches.Count > ListedInputs) names.Add(ExporterLocalization.Format(MoreInputs, result.Mismatches.Count - ListedInputs));
            return ExporterLocalization.Format(MismatchNote, string.Join(ExporterLocalization.T("、"), names), result.Mismatches.Count, result.Compared);
        }

        // The log lines: counts first, then each input's first difference.
        // describe names a target (its path and shape).
        internal static IEnumerable<string> LogLines(Result result, Func<int, string> describe)
        {
            if (result.Unsupported != null)
            {
                yield return LogPrefix + "unsupported " + result.Unsupported;
                yield break;
            }
            yield return LogPrefix + $"inputs {result.Inputs} compared {result.Compared} mismatched {result.Mismatches.Count} " +
                         $"skipped {result.Skipped.Count} outOfTime {result.OutOfTime} targets {result.Targets} unbound {result.Unbound} moved {result.Moved} " +
                         $"otherAnimators {result.OtherAnimators} " +
                         $"seconds {result.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
            foreach (var m in result.Mismatches)
                yield return LogPrefix + $"mismatch \"{m.Input}\" frame {m.Frame} {describe(m.Target)} unity {m.Unity.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} " +
                             $"evaluator {m.Evaluator.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}" +
                             (string.IsNullOrEmpty(m.Layers) ? "" : " layers " + m.Layers);
            foreach (var name in result.Skipped) yield return LogPrefix + $"skipped \"{name}\" (Playable Layer Control on the FX)";
        }

        // Runs the export's check on the copy and lists a difference in
        // warnings. Never throws: a failure is logged and lists nothing.
        internal static void Check(GameObject clone, AvatarAnimatorCapture capture, AvatarAnimatorData data, ICollection<string> warnings)
        {
            if (!Enabled || clone == null || capture?.Controller == null || data == null) return;
            try
            {
                if (!clone.activeSelf) clone.SetActive(true);
                var bindings = capture.Bindings(data);
                var result = Run(clone, capture.Controller, data, bindings, capture.ReadRunningBehaviour);
                foreach (var line in LogLines(result, t => Describe(clone, data, bindings, t))) Debug.Log(line);
                if (ResultLine(result) is string line2) warnings?.Add(line2);
            }
            catch (Exception error) when (!ExportStageGuard.IsCancellation(error))
            {
                Debug.LogWarning(LogPrefix + "failed " + error.GetType().Name + ": " + error.Message);
            }
        }

        internal static string Describe(GameObject root, AvatarAnimatorData data, IReadOnlyList<Binding> bindings, int t)
        {
            var binding = bindings[t];
            var target = data.Targets[t];
            var go = binding.Owner;
            var path = go == null ? "?" : Path(root.transform, go.transform);
            return target.Property == "morph" ? $"{path}:{binding.Shape}" : target.Property == "enabled" ? $"{path}:enabled"
                : target.Property == "materialFloat" ? $"{path}:{binding.Name}"
                : target.Property == "materialColor" ? $"{path}:{binding.Name}.{target.Channel}"
                : target.Property == "materialTextureST" ? $"{path}:{binding.Name}_ST.{target.Channel}"
                : target.Property == "materialSlot" ? $"{path}:slot{binding.Slot}"
                : IsTransform(target.Property) ? $"{path}:{target.Property}{(target.Euler ? "Euler" : "")}.{target.Channel}" : path;
        }

        static string Path(Transform root, Transform transform)
        {
            var parts = new List<string>();
            for (var t = transform; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // Unity's side: the Animator with the parameters set by name, and the
        // behaviours VRChat would run.
        sealed class UnityPlayer
        {
            readonly Animator animator;
            readonly AvatarAnimatorData data;
            readonly Func<StateMachineBehaviour, AvatarAnimatorData.Behaviour> readBehaviour;
            // Unity's type of each data parameter the controller has, or null.
            AnimatorControllerParameterType?[] types;
            // The values of the parameters the controller does not have, which a copy may read.
            double[] others;
            readonly List<(int Layer, double From, double Goal, double Duration, double Elapsed)> blends = new List<(int, double, double, double, double)>();
            System.Random random;
            internal bool PlayableLayerControlled;
            internal Action<Animator> Rebound;

            internal UnityPlayer(Animator animator, AvatarAnimatorData data, Func<StateMachineBehaviour, AvatarAnimatorData.Behaviour> readBehaviour)
            {
                this.animator = animator; this.data = data; this.readBehaviour = readBehaviour;
            }

            internal bool Owns(Animator other) => other == animator;

            internal void Read()
            {
                var unity = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
                foreach (var parameter in animator.parameters)
                    if (!unity.ContainsKey(parameter.name)) unity.Add(parameter.name, parameter.type);
                types = data.Parameters.Select(p => unity.TryGetValue(p.Name, out var type) ? type : (AnimatorControllerParameterType?)null).ToArray();
                others = new double[data.Parameters.Count];
            }

            // Back to the rest, with every parameter at its default.
            internal void Start(System.Random random)
            {
                this.random = random;
                blends.Clear();
                PlayableLayerControlled = false;
                animator.Rebind();
                Rebound?.Invoke(animator);
                var layers = data.Controllers.Count > 0 ? data.Controllers[0].Layers : new List<AvatarAnimatorData.Layer>();
                for (var l = 1; l < layers.Count && l < animator.layerCount; l++) animator.SetLayerWeight(l, (float)layers[l].Weight);
                for (var p = 0; p < data.Parameters.Count; p++) Set(p, data.Parameters[p].Default);
            }

            internal double Get(int parameter)
            {
                var name = data.Parameters[parameter].Name;
                switch (types[parameter])
                {
                    case AnimatorControllerParameterType.Float: return animator.GetFloat(name);
                    case AnimatorControllerParameterType.Int: return animator.GetInteger(name);
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger: return animator.GetBool(name) ? 1 : 0;
                    default: return others[parameter];
                }
            }

            internal void Set(int parameter, double value)
            {
                var name = data.Parameters[parameter].Name;
                switch (types[parameter])
                {
                    case AnimatorControllerParameterType.Float: animator.SetFloat(name, (float)value); break;
                    case AnimatorControllerParameterType.Int: animator.SetInteger(name, (int)Math.Truncate(Math.Max(int.MinValue, Math.Min(int.MaxValue, value)))); break;
                    case AnimatorControllerParameterType.Bool: animator.SetBool(name, value != 0); break;
                    case AnimatorControllerParameterType.Trigger: if (value != 0) animator.SetTrigger(name); else animator.ResetTrigger(name); break;
                    default:
                        var type = data.Parameters[parameter].Type;
                        others[parameter] = type == "float" ? (float)value : type == "int" ? Math.Truncate(value) : value != 0 ? 1 : 0;
                        break;
                }
            }

            // M10: a blend started by a behaviour moves from the next frame on.
            internal void Advance(double seconds)
            {
                for (var i = 0; i < blends.Count; i++)
                {
                    var (layer, from, goal, duration, elapsed) = blends[i];
                    elapsed += seconds;
                    var t = duration > 0 ? Math.Min(1, elapsed / duration) : 1;
                    animator.SetLayerWeight(layer, (float)(from + (goal - from) * t));
                    blends[i] = (layer, from, goal, duration, elapsed);
                }
                blends.RemoveAll(blend => blend.Duration <= 0 || blend.Elapsed >= blend.Duration);
                animator.Update((float)seconds);
            }

            internal void Behaviour(StateMachineBehaviour behaviour)
            {
                var read = behaviour == null ? null : readBehaviour?.Invoke(behaviour);
                if (read == null) return;
                switch (read.Type)
                {
                    case "parameterDriver":
                        foreach (var operation in read.Operations) Drive(operation);
                        break;
                    case "layerControl":
                        if (read.Playable != "fx" || read.Layer <= 0 || read.Layer >= animator.layerCount) break;
                        blends.RemoveAll(blend => blend.Layer == read.Layer);
                        if (read.BlendDuration > 0) blends.Add((read.Layer, animator.GetLayerWeight(read.Layer), read.GoalWeight, read.BlendDuration, 0));
                        else animator.SetLayerWeight(read.Layer, (float)read.GoalWeight);
                        break;
                    case "playableLayerControl":
                        if (read.Playable == "fx") PlayableLayerControlled = true;
                        break;
                }
            }

            // As AvatarAnimatorEvaluator drives a parameter.
            void Drive(AvatarAnimatorData.Operation operation)
            {
                var type = data.Parameters[operation.Parameter].Type;
                double value;
                switch (operation.Type)
                {
                    case "set": value = operation.Value; break;
                    case "add": value = Get(operation.Parameter) + operation.Value; break;
                    case "random":
                        if (type == "bool" || type == "trigger") value = random.NextDouble() < operation.Chance ? 1 : 0;
                        else if (type == "int")
                        {
                            var low = (int)operation.ValueMin;
                            var high = (int)operation.ValueMax;
                            value = high < low ? low : low + Math.Min(high - low, (int)Math.Floor(random.NextDouble() * (high - low + 1.0)));
                        }
                        else value = operation.ValueMin + random.NextDouble() * (operation.ValueMax - operation.ValueMin);
                        break;
                    default:
                        value = operation.Source is int source ? Get(source) : 0;
                        if (operation.ConvertRange)
                        {
                            var span = operation.SourceMax - operation.SourceMin;
                            var t = span != 0 ? Math.Max(0, Math.Min(1, (value - operation.SourceMin) / span)) : 0;
                            value = operation.DestMin + (operation.DestMax - operation.DestMin) * t;
                        }
                        break;
                }
                if (!double.IsNaN(value) && !double.IsInfinity(value)) Set(operation.Parameter, value);
            }
        }

        // The SDK's hooks for the behaviours (found by name; none without the
        // SDK): VRC_AvatarParameterDriver.OnApplySettings, and the
        // ApplySettings of each Animator Layer Control and Playable Layer
        // Control, which the static Initialize hands over.
        sealed class Hooks : IDisposable
        {
            static UnityPlayer current;
            readonly List<Action> undo = new List<Action>();
            readonly HashSet<object> subscribed = new HashSet<object>();
            readonly List<Type> controls = new List<Type>();

            internal Hooks(UnityPlayer player)
            {
                current = player;
                player.Rebound = Adopt;
                var driver = Find("VRC.SDKBase.VRC_AvatarParameterDriver");
                if (driver != null) Attach(driver, null, "OnApplySettings", Delegate(driver, "OnApplySettings", null, nameof(Applied)));
                foreach (var name in new[] { "VRC.SDKBase.VRC_AnimatorLayerControl", "VRC.SDKBase.VRC_PlayableLayerControl" })
                {
                    var type = Find(name);
                    if (type == null) continue;
                    controls.Add(type);
                    Attach(type, null, "Initialize", Delegate(type, "Initialize", null, nameof(Initialized)));
                }
            }

            public void Dispose()
            {
                foreach (var action in undo) action();
                if (current != null) current.Rebound = null;
                current = null;
            }

            static Type Find(string name) =>
                AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name, false)).FirstOrDefault(type => type != null);

            static Type HookType(Type owner, string member)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                return owner.GetEvent(member, flags)?.EventHandlerType ?? owner.GetField(member, flags)?.FieldType;
            }

            // A delegate of the hook's type that calls handler with each argument as object.
            System.Delegate Delegate(Type owner, string member, object target, string handler)
            {
                var type = HookType(owner, member);
                var invoke = type?.GetMethod("Invoke");
                if (invoke == null || invoke.ReturnType != typeof(void)) return null;
                var parameters = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
                var method = typeof(Hooks).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null || method.GetParameters().Length != parameters.Length) return null;
                var call = Expression.Call(Expression.Constant(this), method, parameters.Select(p => (System.Linq.Expressions.Expression)Expression.Convert(p, typeof(object))));
                return Expression.Lambda(type, call, parameters).Compile();
            }

            // Adds the delegate to a static or instance event or delegate field, and undoes it on Dispose.
            void Attach(Type owner, object instance, string member, System.Delegate handler)
            {
                if (handler == null) return;
                var flags = BindingFlags.Public | BindingFlags.NonPublic | (instance == null ? BindingFlags.Static : BindingFlags.Instance);
                var hook = owner.GetEvent(member, flags);
                if (hook != null && hook.GetAddMethod(true) != null)
                {
                    hook.GetAddMethod(true).Invoke(instance, new object[] { handler });
                    undo.Add(() => hook.GetRemoveMethod(true)?.Invoke(instance, new object[] { handler }));
                    return;
                }
                var field = owner.GetField(member, flags);
                if (field == null || !typeof(System.Delegate).IsAssignableFrom(field.FieldType)) return;
                field.SetValue(instance, System.Delegate.Combine((System.Delegate)field.GetValue(instance), handler));
                undo.Add(() => field.SetValue(instance, System.Delegate.Remove((System.Delegate)field.GetValue(instance), handler)));
            }

            void Applied(object behaviour, object animator)
            {
                if (current != null && animator is Animator owner && current.Owns(owner)) current.Behaviour(behaviour as StateMachineBehaviour);
            }

            // The Layer Controls hand over their hook from Awake, which never
            // reached these hooks from the Animator's copies of the behaviours in
            // the editor: the copies are taken from the Animator after each rebind.
            void Adopt(Animator animator)
            {
                foreach (var behaviour in animator.GetBehaviours<StateMachineBehaviour>())
                    if (controls.Any(type => type.IsInstanceOfType(behaviour))) Initialized(behaviour);
            }

            void Initialized(object control)
            {
                if (control == null || !subscribed.Add(control)) return;
                Attach(control.GetType(), control, "ApplySettings", Delegate(control.GetType(), "ApplySettings", control, nameof(Applied)));
            }
        }
    }
}
