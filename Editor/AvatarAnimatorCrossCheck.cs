using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRVlog.AvatarAnimator;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // After the export: plays the copy's controllers in Unity's Animator and
    // the written VRVLOG_avatar_animator in AvatarAnimatorEvaluator, the app's
    // player, from the same inputs for the same frames, and compares what they
    // show: the activeSelf of each active target, the BlendShape weight of
    // each morph target, the enabled flag of each renderer an enabled target
    // names, the float of each material target (materialFloat,
    // materialColor, materialTextureST) as the renderer it was read from
    // shows it, the material of each materialSlot target's slot and the
    // local channel of each translation, rotation and scale target, and the
    // muscle of each muscle target as HumanPoseHandler reads the copy's
    // humanoid pose back, against the evaluator's muscles put on the bones and
    // read back the same way (MuscleReader). The inputs are the start alone, each hand gesture and
    // each menu control. Each input starts from the rest: SettleFrames without
    // input, InputFrames with it, then ReleaseFrames with its parameters back
    // at their defaults, so the way back to the start is compared too.
    //
    // Unity runs VRChat's behaviours only through the SDK's hooks; these are
    // hooked for the run: a Parameter Driver applies its operations as the
    // evaluator does, from the same random sequence, and an Animator Layer
    // Control on the FX blends that layer's weight from the next frame (M10).
    // An input that starts a Playable Layer Control on the FX is not compared:
    // plain Unity has no FX weight to blend. The evaluator plays each input
    // alone first (Controls), so Unity plays only the inputs it can compare.
    //
    // With only the FX, Unity's Animator plays it as the controller. With a
    // Gesture or an Action the evaluator plays too, or a muscle target, a
    // PlayableGraph plays them as VRChat stacks them (M15): an
    // AnimationLayerMixerPlayable with the Gesture, the Action and the FX in
    // that order, at the weights the evaluator starts them at (the Action at
    // 0), each masked with its base layer's body parts, and the FX without
    // the muscles when its base layer has no mask (M16). A Playable Layer Control on the Gesture or the Action blends that
    // input's weight from the next frame, and the parameters go to every
    // controller, as VRChat sets them on each.
    internal static class AvatarAnimatorCrossCheck
    {
        // Off skips the check; the file is the same either way.
        internal static bool Enabled = true;
        internal const int SettleFrames = 30, InputFrames = 150, ReleaseFrames = 60;
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
        // Muscles, read back from the bones in single precision (the design's 1e-3).
        internal const double MuscleTolerance = 1e-3;
        // The time the inputs may take together; the rest are not compared,
        // and one under way when it runs out stops at that frame. Quick and
        // normal exports use the same limit.
        internal static double BudgetSeconds = 30;
        // The seconds since the check started (tests replace it).
        internal static Func<Stopwatch, double> ElapsedSeconds = clock => clock.Elapsed.TotalSeconds;
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

        // Values by parameter index, set after SettleFrames and set back to
        // the parameters' defaults after InputFrames.
        internal sealed class Input
        {
            internal string Name;
            internal readonly List<(int Parameter, double Value)> Values = new List<(int, double)>();
        }

        internal sealed class Mismatch
        {
            internal string Input;
            internal int Frame, Target;
            // For a muscle, both are read back from the bones (MuscleReader), so
            // Evaluator is not the evaluator's own value.
            internal double Unity, Evaluator;
            // The layers whose state or weight differs at that frame, for the log.
            internal string Layers;
        }

        internal sealed class Result
        {
            // Why the evaluator cannot play the data; nothing is compared then.
            internal string Unsupported;
            internal int Inputs, Compared, Targets, Unbound, Moved;
            // Muscle targets compared through the humanoid pose.
            internal int Muscles;
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
            // The hands before the menu: they make the faces, and a long menu
            // would leave them past the time budget.
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
            Walk(0, "");
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
            IReadOnlyList<Binding> bindings, Func<StateMachineBehaviour, AvatarAnimatorData.Behaviour> readBehaviour) =>
            Run(root, new[] { controller }, data, bindings, readBehaviour);

        // controllers follow data.Controllers (the FX first): what each was
        // read from, null for one Unity does not play. Muscle targets are
        // compared when root's Animator has a humanoid Avatar.
        internal static Result Run(GameObject root, IReadOnlyList<RuntimeAnimatorController> controllers, AvatarAnimatorData data,
            IReadOnlyList<Binding> bindings, Func<StateMachineBehaviour, AvatarAnimatorData.Behaviour> readBehaviour)
        {
            var result = new Result();
            if (!AvatarAnimatorEvaluator.Supports(data, out var reason)) { result.Unsupported = reason; return result; }
            var clock = Stopwatch.StartNew();
            var shown = new List<int>();
            var muscles = new List<int>();
            var shapes = new int[data.Targets.Count];
            var quaternions = Quaternions(data);
            for (var t = 0; t < data.Targets.Count; t++)
            {
                var target = data.Targets[t];
                var binding = t < bindings.Count ? bindings[t] : null;
                if (target.Property == "parameter") continue;
                result.Targets++;
                if (target.Property == "muscle") { muscles.Add(t); continue; }
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
            var human = animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
            if (human) result.Muscles = muscles.Count;
            else { result.Unbound += muscles.Count; muscles.Clear(); }
            // The controllers the evaluator plays; one it skips is left out of Unity too.
            var probe = new AvatarAnimatorEvaluator(data, () => 0);
            var played = Enumerable.Range(0, data.Controllers.Count)
                .Select(c => c < controllers.Count && probe.SkipReason(c) == null ? controllers[c] : null).ToList();
            var graph = muscles.Count > 0 || played.Skip(1).Any(c => c != null);
            var player = new UnityPlayer(animator, data, readBehaviour, graph ? played : null);
            var poses = muscles.Count > 0 ? new MuscleReader(animator, root.transform) : null;
            using (HookBehaviours ? new Hooks(player) : null)
            {
                try
                {
                    // Unity takes the values Write Defaults brings back when the
                    // controller binds, and Rebind restores those: the rest goes in
                    // first, as the export may have left the copy elsewhere.
                    animator.runtimeAnimatorController = null;
                    ToRest(data, bindings, shown, shapes);
                    if (!graph) animator.runtimeAnimatorController = played.Count > 0 ? played[0] : null;
                    player.Read();
                    bool Late() => ElapsedSeconds(clock) > BudgetSeconds;
                    foreach (var input in inputs)
                    {
                        // The first input always runs, so a slow avatar still shows something.
                        var first = input == inputs[0];
                        if (!first && Late()) { result.OutOfTime++; continue; }
                        var controls = Controls(input, data, first ? null : (Func<bool>)Late);
                        if (controls == null) { result.OutOfTime++; continue; }
                        if (controls == true) { result.Skipped.Add(input.Name); continue; }
                        var mismatch = Play(input, data, bindings, shown, muscles, poses, shapes, quaternions, player, first ? null : (Func<bool>)Late, out var skipped, out var late);
                        if (late) { result.OutOfTime++; continue; }
                        if (skipped) { result.Skipped.Add(input.Name); continue; }
                        result.Compared++;
                        if (mismatch != null) result.Mismatches.Add(mismatch);
                    }
                }
                finally
                {
                    player.Dispose();
                    poses?.Dispose();
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
            : target.Property == "muscle" ? !(Math.Abs(evaluator - unity) <= MuscleTolerance)
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

        // Each input draws its own random sequence, the same on both sides.
        static int Seed(Input input) => input.Name.Aggregate(17, (hash, c) => unchecked(hash * 31 + c));

        // Whether the evaluator runs a Playable Layer Control on the FX in the
        // frames Play plays the input; null when late stops it part way.
        static bool? Controls(Input input, AvatarAnimatorData data, Func<bool> late)
        {
            var random = new System.Random(Seed(input));
            var evaluator = new AvatarAnimatorEvaluator(data, () => random.NextDouble());
            for (var frame = 0; frame < SettleFrames + InputFrames + ReleaseFrames; frame++)
            {
                if (late != null && late()) return null;
                if (frame == SettleFrames)
                    foreach (var (parameter, value) in input.Values) evaluator.Set(parameter, value);
                if (frame == SettleFrames + InputFrames)
                    foreach (var (parameter, _) in input.Values) evaluator.Set(parameter, data.Parameters[parameter].Default);
                evaluator.Advance(FrameSeconds);
                if (evaluator.FxControlled) return true;
            }
            return false;
        }

        static Mismatch Play(Input input, AvatarAnimatorData data, IReadOnlyList<Binding> bindings, List<int> shown, List<int> muscles,
            MuscleReader poses, int[] shapes, Dictionary<int, int[]> quaternions, UnityPlayer player, Func<bool> late, out bool skipped, out bool outOfTime)
        {
            skipped = outOfTime = false;
            var pose = new HumanPose();
            // Both draw the same sequence; a different order of behaviours shows as a difference.
            var seed = Seed(input);
            var evaluatorRandom = new System.Random(seed);
            var evaluator = new AvatarAnimatorEvaluator(data, () => evaluatorRandom.NextDouble());
            ToRest(data, bindings, shown, shapes);
            player.Start(new System.Random(seed));
            Mismatch first = null;
            for (var frame = 0; frame < SettleFrames + InputFrames + ReleaseFrames; frame++)
            {
                // Stopped part way, the input is not compared.
                if (late != null && late()) { outOfTime = true; return null; }
                if (frame == SettleFrames)
                    foreach (var (parameter, value) in input.Values)
                    {
                        evaluator.Set(parameter, value);
                        player.Set(parameter, value);
                    }
                if (frame == SettleFrames + InputFrames)
                    foreach (var (parameter, _) in input.Values)
                    {
                        evaluator.Set(parameter, data.Parameters[parameter].Default);
                        player.Set(parameter, data.Parameters[parameter].Default);
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
                    first = new Mismatch { Input = input.Name, Frame = frame, Target = t, Unity = unity, Evaluator = value, Layers = Layers(data, player, evaluator) };
                    break;
                }
                if (first != null || muscles.Count == 0) continue;
                poses.Read(ref pose);
                var read = poses.ReadBack(pose, data, muscles, evaluator.Values);
                foreach (var t in muscles)
                {
                    var unity = (double)pose.muscles[data.Targets[t].Muscle];
                    var value = (double)read[data.Targets[t].Muscle];
                    if (!Differs(data.Targets[t], value, unity)) continue;
                    first = new Mismatch { Input = input.Name, Frame = frame, Target = t, Unity = unity, Evaluator = value, Layers = Layers(data, player, evaluator) };
                    break;
                }
            }
            return first;
        }

        // Reads the copy's muscles back from its bones. HumanPoseHandler does not
        // give back exactly the muscles put on the bones: a pose put with
        // SetHumanPose reads back up to about 1.5e-3 off on the twist muscles of
        // the legs, the feet and the arms, as the Animator's pose does, by the same
        // amount (Unity mixes the muscles of its layers linearly; measured in
        // AvatarAnimatorCrossCheckTests.MusclesReadBackAsAPosePutOnTheBones). So
        // the evaluator's muscles are put on the bones and read back the same way
        // before they are compared, and the bones are put back as Unity left them.
        internal sealed class MuscleReader : IDisposable
        {
            readonly HumanPoseHandler handler;
            // The humanoid bones and every transform above them up to the root:
            // what SetHumanPose can move.
            readonly Transform[] skeleton;
            readonly Vector3[] positions, scales;
            readonly Quaternion[] rotations;
            HumanPose evaluated = new HumanPose();

            internal MuscleReader(Animator animator, Transform root)
            {
                handler = new HumanPoseHandler(animator.avatar, root);
                var moved = new HashSet<Transform>();
                for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                    for (var t = animator.GetBoneTransform(bone); t != null && t != root && moved.Add(t); t = t.parent) { }
                skeleton = moved.ToArray();
                positions = new Vector3[skeleton.Length];
                scales = new Vector3[skeleton.Length];
                rotations = new Quaternion[skeleton.Length];
            }

            internal void Read(ref HumanPose pose) => handler.GetHumanPose(ref pose);

            // The muscles HumanPoseHandler reads back with the evaluator's muscle
            // targets on the bones and the other muscles and the body where
            // Unity's pose has them: the read back of a muscle depends on the
            // other muscles of its bone, and the muscles the evaluator does not
            // play (additive clips, clips over the track limit) still move in
            // Unity. The array is reused by the next call.
            internal float[] ReadBack(HumanPose unity, AvatarAnimatorData data, List<int> muscles, IReadOnlyList<double> values)
            {
                for (var i = 0; i < skeleton.Length; i++)
                {
                    positions[i] = skeleton[i].localPosition;
                    rotations[i] = skeleton[i].localRotation;
                    scales[i] = skeleton[i].localScale;
                }
                if (evaluated.muscles == null || evaluated.muscles.Length != unity.muscles.Length) evaluated.muscles = new float[unity.muscles.Length];
                Array.Copy(unity.muscles, evaluated.muscles, unity.muscles.Length);
                evaluated.bodyPosition = unity.bodyPosition;
                evaluated.bodyRotation = unity.bodyRotation;
                foreach (var t in muscles) evaluated.muscles[data.Targets[t].Muscle] = (float)values[t];
                handler.SetHumanPose(ref evaluated);
                handler.GetHumanPose(ref evaluated);
                for (var i = 0; i < skeleton.Length; i++)
                {
                    skeleton[i].localPosition = positions[i];
                    skeleton[i].localRotation = rotations[i];
                    skeleton[i].localScale = scales[i];
                }
                return evaluated.muscles;
            }

            public void Dispose() => handler.Dispose();
        }

        // Each layer where Unity and the evaluator are in a different state or
        // at a different weight: "index name: unity state weight / evaluator
        // state weight". A state in a transition reads "from>to".
        // A layer of a controller other than the FX reads "playable index name".
        static string Layers(AvatarAnimatorData data, UnityPlayer player, AvatarAnimatorEvaluator evaluator)
        {
            var parts = new List<string>();
            for (var c = 0; c < data.Controllers.Count; c++)
            {
                if (evaluator.SkipReason(c) != null) continue;
                var layers = data.Controllers[c].Layers;
                for (var l = 0; l < layers.Count && l < player.LayerCount(c); l++)
                {
                    var states = layers[l].States;
                    var e = evaluator.LayerIndex(c, l);
                    if (states.Count == 0 || e < 0) continue;
                    string Named(int hash) => states.FirstOrDefault(s => Animator.StringToHash(s.Name) == hash)?.Name ?? "?";
                    string At(int index) => index >= 0 && index < states.Count ? states[index].Name : "-";
                    var unity = Named(player.StateHash(c, l, false));
                    if (player.InTransition(c, l)) unity += ">" + Named(player.StateHash(c, l, true));
                    var app = At(evaluator.State(e));
                    if (evaluator.NextState(e) >= 0) app += ">" + At(evaluator.NextState(e));
                    var unityWeight = l == 0 ? 1 : player.LayerWeight(c, l);
                    if (unity == app && Math.Abs(unityWeight - evaluator.LayerWeight(e)) < 0.001) continue;
                    var name = c == 0 ? "" : data.Controllers[c].Playable + " ";
                    parts.Add($"{name}{l} {layers[l].Name}: unity {unity} {unityWeight:0.###} / evaluator {app} {evaluator.LayerWeight(e):0.###}");
                }
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
                         $"otherAnimators {result.OtherAnimators} muscles {result.Muscles} " +
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
                var result = Run(clone, Controllers(clone, capture.Controller, data), data, bindings, capture.ReadRunningBehaviour);
                foreach (var line in LogLines(result, t => Describe(clone, data, bindings, t))) Debug.Log(line);
                if (ResultLine(result) is string line2) warnings?.Add(line2);
            }
            catch (Exception error) when (!ExportStageGuard.IsCancellation(error))
            {
                Debug.LogWarning(LogPrefix + "failed " + error.GetType().Name + ": " + error.Message);
            }
        }

        // The controller each of data.Controllers was read from: the FX the
        // capture read, and the descriptor's Gesture and Action, as
        // AvatarAnimatorCapture reads them (null when it keeps the SDK's default).
        static List<RuntimeAnimatorController> Controllers(GameObject clone, RuntimeAnimatorController fx, AvatarAnimatorData data)
        {
            var descriptor = VrChatExpressionMenu.Descriptor(clone);
            RuntimeAnimatorController Layer(string type)
            {
                RuntimeAnimatorController found = null;
                if (descriptor != null && VrChatExpressionMenu.Member(descriptor, "customizeAnimationLayers") is bool custom && custom &&
                    VrChatExpressionMenu.Member(descriptor, "baseAnimationLayers") is System.Collections.IEnumerable layers)
                    foreach (var layer in layers)
                    {
                        if (VrChatExpressionMenu.Member(layer, "isDefault") is bool isDefault && isDefault) continue;
                        if (VrChatExpressionMenu.Member(layer, "type")?.ToString() == type) found = VrChatExpressionMenu.Member(layer, "animatorController") as RuntimeAnimatorController;
                    }
                return found;
            }
            return data.Controllers.Select(c => c.Playable == "fx" ? fx : Layer(c.Playable == "gesture" ? "Gesture" : "Action")).ToList();
        }

        internal static string Describe(GameObject root, AvatarAnimatorData data, IReadOnlyList<Binding> bindings, int t)
        {
            var binding = bindings[t];
            var target = data.Targets[t];
            if (target.Property == "muscle") return "muscle " + (target.Muscle < HumanTrait.MuscleCount ? HumanTrait.MuscleName[target.Muscle] : target.Muscle.ToString());
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

        // Unity's side: the Animator, or the playables of each controller, with
        // the parameters set by name, and the behaviours VRChat would run.
        sealed class UnityPlayer : IDisposable
        {
            // VRChat's order of the playable layers, from the bottom (M15).
            static readonly string[] PlayOrder = { "gesture", "action", "fx" };

            readonly Animator animator;
            readonly AvatarAnimatorData data;
            readonly Func<StateMachineBehaviour, AvatarAnimatorData.Behaviour> readBehaviour;
            // Null to play the FX as the Animator's controller; else the
            // controller of each of data.Controllers on the graph (null for one
            // that does not play).
            readonly IReadOnlyList<RuntimeAnimatorController> controllers;
            PlayableGraph graph;
            AnimationLayerMixerPlayable mixer;
            // Per data controller: its playable and its input of the mixer (-1 when it does not play).
            AnimatorControllerPlayable[] playables;
            int[] inputs;
            readonly List<AvatarMask> masks = new List<AvatarMask>();
            // Unity's type of each data parameter in each controller (the
            // Animator's in [0] without the graph), or null.
            AnimatorControllerParameterType?[][] types;
            // The values of the parameters no controller has, which a copy may read.
            double[] others;
            readonly List<(int Controller, int Layer, double From, double Goal, double Duration, double Elapsed)> blends =
                new List<(int, int, double, double, double, double)>();
            readonly List<(int Input, double From, double Goal, double Duration, double Elapsed)> playableBlends =
                new List<(int, double, double, double, double)>();
            System.Random random;
            internal bool PlayableLayerControlled;
            internal Action<Animator> Rebound;

            internal UnityPlayer(Animator animator, AvatarAnimatorData data, Func<StateMachineBehaviour, AvatarAnimatorData.Behaviour> readBehaviour,
                IReadOnlyList<RuntimeAnimatorController> controllers)
            {
                this.animator = animator; this.data = data; this.readBehaviour = readBehaviour; this.controllers = controllers;
            }

            internal bool Owns(Animator other) => other == animator;

            bool Graph => controllers != null;

            public void Dispose()
            {
                if (graph.IsValid()) graph.Destroy();
                masks.ForEach(Object.DestroyImmediate);
                masks.Clear();
            }

            internal void Read()
            {
                others = new double[data.Parameters.Count];
                if (!Graph) types = new[] { Types(animator.parameters) };
            }

            AnimatorControllerParameterType?[] Types(IEnumerable<AnimatorControllerParameter> parameters)
            {
                var unity = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
                foreach (var parameter in parameters)
                    if (!unity.ContainsKey(parameter.name)) unity.Add(parameter.name, parameter.type);
                return data.Parameters.Select(p => unity.TryGetValue(p.Name, out var type) ? type : (AnimatorControllerParameterType?)null).ToArray();
            }

            // A new graph of the controllers that play, in VRChat's order.
            void Build()
            {
                if (graph.IsValid()) graph.Destroy();
                masks.ForEach(Object.DestroyImmediate);
                masks.Clear();
                graph = PlayableGraph.Create("VR Vlog animator check");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var order = PlayOrder.SelectMany(playable => Enumerable.Range(0, data.Controllers.Count)
                    .Where(c => data.Controllers[c].Playable == playable && c < controllers.Count && controllers[c] != null)).ToList();
                mixer = AnimationLayerMixerPlayable.Create(graph, order.Count);
                playables = new AnimatorControllerPlayable[data.Controllers.Count];
                inputs = Enumerable.Repeat(-1, data.Controllers.Count).ToArray();
                types = new AnimatorControllerParameterType?[data.Controllers.Count][];
                for (var i = 0; i < order.Count; i++)
                {
                    var c = order[i];
                    var playable = playables[c] = AnimatorControllerPlayable.Create(graph, controllers[c]);
                    inputs[c] = i;
                    graph.Connect(playable, 0, mixer, i);
                    mixer.SetInputWeight(i, data.Controllers[c].Playable == "action" ? 0 : 1);
                    // M16: VRChat masks each playable with its base layer's body
                    // parts, and applies no muscle of an FX whose base layer has no mask.
                    var baseMask = data.Controllers[c].Layers.Count > 0 ? data.Controllers[c].Layers[0].Mask : null;
                    if (baseMask != null || data.Controllers[c].Playable == "fx")
                    {
                        var parts = new AvatarMask { name = "Playable " + i, hideFlags = HideFlags.HideAndDontSave };
                        for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                            parts.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, baseMask != null && baseMask.Humanoid.Contains(part));
                        masks.Add(parts);
                        mixer.SetLayerMaskFromAvatarMask((uint)i, parts);
                    }
                    types[c] = Types(Enumerable.Range(0, playable.GetParameterCount()).Select(playable.GetParameter));
                }
                AnimationPlayableOutput.Create(graph, "Animator", animator).SetSourcePlayable(mixer);
            }

            // Back to the rest, with every parameter at its default.
            internal void Start(System.Random random)
            {
                this.random = random;
                blends.Clear();
                playableBlends.Clear();
                entered = null;
                PlayableLayerControlled = false;
                animator.Rebind();
                if (Graph) Build();
                Rebound?.Invoke(animator);
                for (var c = 0; c < data.Controllers.Count; c++)
                {
                    var layers = data.Controllers[c].Layers;
                    for (var l = 1; l < layers.Count && l < LayerCount(c); l++) SetLayerWeight(c, l, layers[l].Weight);
                }
                for (var p = 0; p < data.Parameters.Count; p++) Set(p, data.Parameters[p].Default);
            }

            // The Animator's layers are the FX's (data controller 0).
            internal int LayerCount(int c) => Graph ? (playables[c].IsValid() ? playables[c].GetLayerCount() : 0) : c == 0 ? animator.layerCount : 0;
            internal double LayerWeight(int c, int l) => Graph ? playables[c].GetLayerWeight(l) : animator.GetLayerWeight(l);
            void SetLayerWeight(int c, int l, double weight)
            {
                if (Graph) playables[c].SetLayerWeight(l, (float)weight);
                else animator.SetLayerWeight(l, (float)weight);
            }
            internal bool InTransition(int c, int l) => Graph ? playables[c].IsInTransition(l) : animator.IsInTransition(l);
            internal int StateHash(int c, int l, bool next) => Graph
                ? (next ? playables[c].GetNextAnimatorStateInfo(l) : playables[c].GetCurrentAnimatorStateInfo(l)).shortNameHash
                : (next ? animator.GetNextAnimatorStateInfo(l) : animator.GetCurrentAnimatorStateInfo(l)).shortNameHash;

            // Each controller the parameter goes to: (index of types, playable or the Animator).
            IEnumerable<int> Holders(int parameter)
            {
                for (var c = 0; c < types.Length; c++)
                    if (types[c] != null && types[c][parameter] != null) yield return c;
            }

            internal double Get(int parameter)
            {
                var name = data.Parameters[parameter].Name;
                foreach (var c in Holders(parameter))
                    switch (types[c][parameter])
                    {
                        case AnimatorControllerParameterType.Float: return Graph ? playables[c].GetFloat(name) : animator.GetFloat(name);
                        case AnimatorControllerParameterType.Int: return Graph ? playables[c].GetInteger(name) : animator.GetInteger(name);
                        default: return (Graph ? playables[c].GetBool(name) : animator.GetBool(name)) ? 1 : 0;
                    }
                return others[parameter];
            }

            internal void Set(int parameter, double value)
            {
                var name = data.Parameters[parameter].Name;
                var held = false;
                foreach (var c in Holders(parameter))
                {
                    held = true;
                    var type = types[c][parameter];
                    var i = (int)Math.Truncate(Math.Max(int.MinValue, Math.Min(int.MaxValue, value)));
                    if (!Graph)
                        switch (type)
                        {
                            case AnimatorControllerParameterType.Float: animator.SetFloat(name, (float)value); break;
                            case AnimatorControllerParameterType.Int: animator.SetInteger(name, i); break;
                            case AnimatorControllerParameterType.Bool: animator.SetBool(name, value != 0); break;
                            default: if (value != 0) animator.SetTrigger(name); else animator.ResetTrigger(name); break;
                        }
                    else
                        switch (type)
                        {
                            case AnimatorControllerParameterType.Float: playables[c].SetFloat(name, (float)value); break;
                            case AnimatorControllerParameterType.Int: playables[c].SetInteger(name, i); break;
                            case AnimatorControllerParameterType.Bool: playables[c].SetBool(name, value != 0); break;
                            default: if (value != 0) playables[c].SetTrigger(name); else playables[c].ResetTrigger(name); break;
                        }
                }
                if (held) return;
                var declared = data.Parameters[parameter].Type;
                others[parameter] = declared == "float" ? (float)value : declared == "int" ? Math.Truncate(value) : value != 0 ? 1 : 0;
            }

            // M10: a blend started by a behaviour moves from the next frame on.
            internal void Advance(double seconds)
            {
                for (var i = 0; i < blends.Count; i++)
                {
                    var (controller, layer, from, goal, duration, elapsed) = blends[i];
                    elapsed += seconds;
                    var t = duration > 0 ? Math.Min(1, elapsed / duration) : 1;
                    SetLayerWeight(controller, layer, from + (goal - from) * t);
                    blends[i] = (controller, layer, from, goal, duration, elapsed);
                }
                blends.RemoveAll(blend => blend.Duration <= 0 || blend.Elapsed >= blend.Duration);
                for (var i = 0; i < playableBlends.Count; i++)
                {
                    var (input, from, goal, duration, elapsed) = playableBlends[i];
                    elapsed += seconds;
                    var t = duration > 0 ? Math.Min(1, elapsed / duration) : 1;
                    mixer.SetInputWeight(input, (float)(from + (goal - from) * t));
                    playableBlends[i] = (input, from, goal, duration, elapsed);
                }
                playableBlends.RemoveAll(blend => blend.Duration <= 0 || blend.Elapsed >= blend.Duration);
                if (Graph) { graph.Evaluate((float)seconds); Enter(); }
                else animator.Update((float)seconds);
            }

            // The data controller of the playable that plays, or -1.
            int Controller(string playable)
            {
                for (var c = 0; c < data.Controllers.Count; c++)
                    if (data.Controllers[c].Playable == playable && LayerCount(c) > 0) return c;
                return -1;
            }

            // The states each layer of each played controller was in or going to.
            HashSet<int>[][] entered;

            // On the graph the SDK's Layer Controls are copies the playables
            // hold, which Hooks cannot reach (Adopt reads the Animator's), so
            // the data's Layer Controls of each state a layer enters this
            // frame play here instead; the Parameter Drivers still come
            // through Hooks. Only the state's own behaviours, not its
            // machines'.
            void Enter()
            {
                if (entered == null)
                    entered = data.Controllers.Select((c, i) => playables[i].IsValid() ? c.Layers.Select(_ => new HashSet<int>()).ToArray() : null).ToArray();
                for (var c = 0; c < entered.Length; c++)
                {
                    if (entered[c] == null) continue;
                    for (var l = 0; l < entered[c].Length && l < LayerCount(c); l++)
                    {
                        var now = new HashSet<int> { StateHash(c, l, false) };
                        if (InTransition(c, l)) now.Add(StateHash(c, l, true));
                        var states = data.Controllers[c].Layers[l].States;
                        foreach (var hash in now)
                        {
                            if (entered[c][l].Contains(hash)) continue;
                            var state = states.FindIndex(s => Animator.StringToHash(s.Name) == hash);
                            if (state < 0) continue;
                            foreach (var behaviour in states[state].Behaviours)
                                if (behaviour.Type != "parameterDriver") Apply(behaviour);
                        }
                        entered[c][l] = now;
                    }
                }
            }

            internal void Behaviour(StateMachineBehaviour behaviour)
            {
                var read = behaviour == null ? null : readBehaviour?.Invoke(behaviour);
                if (read == null) return;
                // On the graph only the drivers come from the SDK (see Enter).
                if (!Graph || read.Type == "parameterDriver") Apply(read);
            }

            void Apply(AvatarAnimatorData.Behaviour read)
            {
                switch (read.Type)
                {
                    case "parameterDriver":
                        foreach (var operation in read.Operations) Drive(operation);
                        break;
                    case "layerControl":
                        var c = Controller(read.Playable);
                        if (c < 0 || read.Layer <= 0 || read.Layer >= LayerCount(c)) break;
                        blends.RemoveAll(blend => blend.Controller == c && blend.Layer == read.Layer);
                        if (read.BlendDuration > 0) blends.Add((c, read.Layer, LayerWeight(c, read.Layer), read.GoalWeight, read.BlendDuration, 0));
                        else SetLayerWeight(c, read.Layer, read.GoalWeight);
                        break;
                    case "playableLayerControl":
                        if (read.Playable == "fx") { PlayableLayerControlled = true; break; }
                        var played = Graph ? Controller(read.Playable) : -1;
                        if (played < 0) break;
                        var input = inputs[played];
                        playableBlends.RemoveAll(blend => blend.Input == input);
                        if (read.BlendDuration > 0) playableBlends.Add((input, mixer.GetInputWeight(input), read.GoalWeight, read.BlendDuration, 0));
                        else mixer.SetInputWeight(input, (float)read.GoalWeight);
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
