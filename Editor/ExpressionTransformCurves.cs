using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    // The bone (Transform) and visibility (GameObject m_IsActive) curves of an
    // expression clip, for the expression animation extension's transforms.
    // Read takes them from the clip on the prepared copy, in Unity's local
    // space; Resolve binds each one to its output node after export.
    internal static class ExpressionTransformCurves
    {
        // A component that stays this close to the prepared pose, keys and
        // Bezier handles alike, does not move: authored rest poses carry float
        // noise. Position in metres of the avatar (a bone under a 100x armature
        // compares 0.000005 of its own units), scale as is, Euler angles in degrees.
        internal const double PositionTolerance = 0.0005;
        internal const double AngleTolerance = 0.05;
        // Each quaternion component this close to the prepared one (or to its
        // negation, the same rotation): about AngleTolerance of rotation.
        internal static readonly double QuaternionTolerance = Math.Sin(AngleTolerance * Math.PI / 180 / 2);

        internal const string HumanoidWarning = "表情が動かすHumanoidのボーンは書き出しません: ";
        internal const string UnmappedWarning = "書き出すノードに結び付かない表情のボーンと表示の変化は書き出しません: ";
        internal const string HiddenShownWarning = "書き出さないオブジェクトを表示する表情は表示の変化をまとめて書き出しません: ";

        internal sealed class Reading
        {
            internal readonly List<VrmMenuExpressions.TransformTrack> Tracks = new List<VrmMenuExpressions.TransformTrack>();
            // Bindings Read took or left out on purpose; the BlendShape reader skips them.
            internal readonly HashSet<EditorCurveBinding> Handled = new HashSet<EditorCurveBinding>();
            internal readonly SortedSet<string> Humanoid = new SortedSet<string>(StringComparer.Ordinal);
            internal readonly SortedSet<string> Unmapped = new SortedSet<string>(StringComparer.Ordinal);

            // Some track changes over time, so it needs the clip's clock.
            internal bool Moving => Tracks.Any(track => track.Curves.Any(Moves));

            internal IEnumerable<string> Messages()
            {
                if (Humanoid.Count > 0) yield return ExporterLocalization.T(HumanoidWarning) + string.Join(", ", Humanoid);
                if (Unmapped.Count > 0) yield return ExporterLocalization.T(UnmappedWarning) + string.Join(", ", Unmapped);
            }
        }

        static readonly Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["m_LocalPosition"] = ExpressionTransformAnimation.Translation,
            ["localEulerAnglesRaw"] = ExpressionTransformAnimation.Euler,
            ["m_LocalEulerAngles"] = ExpressionTransformAnimation.Euler,
            ["m_LocalRotation"] = ExpressionTransformAnimation.Rotation,
            ["m_LocalScale"] = ExpressionTransformAnimation.Scale
        };
        static readonly string[] Axes = { "x", "y", "z", "w" };

        internal static Reading Read(GameObject avatar, AnimationClip clip, Func<string, bool> excludedPath = null)
        {
            SelectedExpressionAppearance.ValidateClipData(clip, excludedPath);
            var reading = new Reading();
            var root = avatar.transform;
            var paths = avatar.GetComponentsInChildren<Transform>(true).GroupBy(transform => AnimationUtility.CalculateTransformPath(transform, root))
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var humanoid = HumanoidBones(avatar);
            var bindings = AnimationUtility.GetCurveBindings(clip).Where(binding => !binding.isPPtrCurve && excludedPath?.Invoke(binding.path) != true).ToArray();
            var parts = bindings.Where(binding => binding.type == typeof(Transform)).Select(binding =>
            {
                var dot = binding.propertyName.LastIndexOf('.');
                var prefix = dot > 0 ? binding.propertyName.Substring(0, dot) : null;
                var property = prefix != null && Properties.TryGetValue(prefix, out var value) ? value : null;
                var axis = property == null ? -1 : Array.IndexOf(Axes, binding.propertyName.Substring(dot + 1));
                return (Binding: binding, Prefix: prefix, Property: property, Axis: axis);
            }).Where(part => part.Property != null && part.Axis >= 0 && part.Axis < ExpressionTransformAnimation.Components(part.Property));
            var rotated = new Dictionary<Transform, string>();
            foreach (var group in parts.GroupBy(part => (part.Binding.path, part.Property)))
            {
                var (path, property) = group.Key;
                foreach (var part in group) reading.Handled.Add(part.Binding);
                // Unity cannot bind an absent target, so the curve changes nothing.
                if (!paths.TryGetValue(path, out var matches)) continue;
                // Both Euler spellings on one object would be two tracks of one property.
                if (group.Select(part => part.Prefix).Distinct().Count() > 1 || group.GroupBy(part => part.Axis).Any(axis => axis.Count() > 1))
                    throw new InvalidOperationException("BlendShape以外の変化を含む表情アニメーションです: " + path + " / " + property);
                var target = matches.Length == 1 ? matches[0] : null;
                // The root is no node, and RootScaleBake rewrites its children's
                // position and scale while exporting.
                if (target == null || target == root || target.parent == root && root.localScale != Vector3.one &&
                    (property == ExpressionTransformAnimation.Translation || property == ExpressionTransformAnimation.Scale))
                {
                    reading.Unmapped.Add(path);
                    continue;
                }
                var rest = Rest(target, property);
                var curves = new ExpressionAnimationData.Curve[rest.Length];
                foreach (var part in group) curves[part.Axis] = VrChatGestureExpressions.ReadCurve(AnimationUtility.GetEditorCurve(clip, part.Binding));
                if (!Settle(target, property, curves, rest)) continue;
                // The app's pose, gaze and emotes write these bones every frame.
                if (humanoid.Contains(target))
                {
                    reading.Humanoid.Add(path);
                    continue;
                }
                if (property == ExpressionTransformAnimation.Euler || property == ExpressionTransformAnimation.Rotation)
                {
                    if (rotated.ContainsKey(target))
                        throw new InvalidOperationException("BlendShape以外の変化を含む表情アニメーションです: " + path + " / " + property);
                    rotated.Add(target, property);
                }
                var track = new VrmMenuExpressions.TransformTrack { Target = target.gameObject, Path = path, Property = property };
                track.Curves.AddRange(curves);
                reading.Tracks.Add(track);
            }
            foreach (var binding in bindings.Where(binding => binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive"))
            {
                if (!paths.TryGetValue(binding.path, out var matches)) { reading.Handled.Add(binding); continue; }
                var curve = VrChatGestureExpressions.ReadCurve(AnimationUtility.GetEditorCurve(clip, binding));
                curve.Range(out var minimum, out var maximum);
                // Other values are not a visibility; the BlendShape reader rejects them.
                if (minimum < 0 || maximum > 1) continue;
                reading.Handled.Add(binding);
                if (matches.Length != 1 || matches[0] == root)
                {
                    reading.Unmapped.Add(binding.path);
                    continue;
                }
                var target = matches[0].gameObject;
                if (minimum == maximum && minimum == (target.activeSelf ? 1 : 0)) continue;
                var track = new VrmMenuExpressions.TransformTrack { Target = target, Path = binding.path, Property = ExpressionTransformAnimation.Active };
                track.Curves.Add(curve);
                reading.Tracks.Add(track);
            }
            return reading;
        }

        // The prepared value of each component.
        static double[] Rest(Transform target, string property)
        {
            switch (property)
            {
                case ExpressionTransformAnimation.Translation: { var v = target.localPosition; return new double[] { v.x, v.y, v.z }; }
                case ExpressionTransformAnimation.Scale: { var v = target.localScale; return new double[] { v.x, v.y, v.z }; }
                // The Euler hint the Animation window records against.
                case ExpressionTransformAnimation.Euler: { var v = TransformUtils.GetInspectorRotation(target); return new double[] { v.x, v.y, v.z }; }
                default: { var q = target.localRotation; return new double[] { q.x, q.y, q.z, q.w }; }
            }
        }

        // Replaces each component that does not move from the prepared pose
        // with that value and fills absent ones with it; components that hold
        // one value within the tolerance become that value. False when
        // nothing moves away from the prepared pose.
        static bool Settle(Transform target, string property, ExpressionAnimationData.Curve[] curves, double[] rest)
        {
            if (property == ExpressionTransformAnimation.Rotation)
            {
                // A rotation compares as one pose; q and -q are the same.
                if (new[] { 1.0, -1.0 }.Any(sign => Enumerable.Range(0, 4).All(c => curves[c] == null || Within(curves[c], sign * rest[c], QuaternionTolerance))))
                    return false;
                var still = curves.All(curve => curve == null || Width(curve) <= QuaternionTolerance);
                for (var c = 0; c < 4; c++)
                    curves[c] = curves[c] == null ? Constant(rest[c]) : still ? Constant(curves[c].Keys[0].Value) : curves[c];
                return true;
            }
            var tolerance = property == ExpressionTransformAnimation.Euler ? AngleTolerance
                : property == ExpressionTransformAnimation.Translation ? PositionTolerance / ParentScale(target) : PositionTolerance;
            if (property == ExpressionTransformAnimation.Euler && curves.All(curve => curve == null || Width(curve) <= tolerance))
            {
                // Held angles can spell the prepared rotation another way.
                var held = Enumerable.Range(0, 3).Select(c => (float)(curves[c]?.Keys[0].Value ?? rest[c])).ToArray();
                if (Angle(Quaternion.Euler(held[0], held[1], held[2]), target.localRotation) <= tolerance) return false;
            }
            var moves = false;
            for (var c = 0; c < curves.Length; c++)
            {
                var center = rest[c];
                // Euler components compare modulo a full turn.
                if (curves[c] != null && property == ExpressionTransformAnimation.Euler) center += 360 * Math.Round((curves[c].Keys[0].Value - rest[c]) / 360);
                if (curves[c] == null || Within(curves[c], center, tolerance)) { curves[c] = Constant(rest[c]); continue; }
                moves = true;
                if (Width(curves[c]) <= tolerance) curves[c] = Constant(curves[c].Keys[0].Value);
            }
            return moves;
        }

        // How far one local unit of the target's position reaches in the
        // avatar: the largest axis of the parent's scale.
        static double ParentScale(Transform target)
        {
            if (target.parent == null) return 1;
            var scale = target.parent.lossyScale;
            var largest = Math.Max(Math.Abs(scale.x), Math.Max(Math.Abs(scale.y), Math.Abs(scale.z)));
            return largest > 0 ? largest : 1;
        }

        static bool Within(ExpressionAnimationData.Curve curve, double value, double tolerance)
        {
            curve.Range(out var minimum, out var maximum);
            return minimum >= value - tolerance && maximum <= value + tolerance;
        }

        static double Width(ExpressionAnimationData.Curve curve)
        {
            curve.Range(out var minimum, out var maximum);
            return maximum - minimum;
        }

        static bool Moves(ExpressionAnimationData.Curve curve)
        {
            curve.Range(out var minimum, out var maximum);
            return minimum != maximum;
        }

        static ExpressionAnimationData.Curve Constant(double value)
        {
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value, InTangent = 0, OutTangent = 0 });
            return curve;
        }

        // Degrees between two rotations, in double: Quaternion.Angle reads
        // anything under about 0.16 degrees as 0.
        static double Angle(Quaternion a, Quaternion b)
        {
            double ax = a.x, ay = a.y, az = a.z, aw = a.w, bx = b.x, by = b.y, bz = b.z, bw = b.w;
            var na = Math.Sqrt(ax * ax + ay * ay + az * az + aw * aw);
            var nb = Math.Sqrt(bx * bx + by * by + bz * bz + bw * bw);
            if (na < 1e-6 || nb < 1e-6) return double.PositiveInfinity;
            // conj(a) * b
            var w = aw * bw + ax * bx + ay * by + az * bz;
            var x = aw * bx - ax * bw - ay * bz + az * by;
            var y = aw * by + ax * bz - ay * bw - az * bx;
            var z = aw * bz - ax * by + ay * bx - az * bw;
            return 2 * Math.Atan2(Math.Sqrt(x * x + y * y + z * z), Math.Abs(w)) * 180 / Math.PI;
        }

        static HashSet<Transform> HumanoidBones(GameObject avatar)
        {
            var result = new HashSet<Transform>();
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return result;
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                var transform = animator.GetBoneTransform(bone);
                if (transform != null) result.Add(transform);
            }
            return result;
        }

        // Binds every track to its output node. Tracks without one are left
        // out with a warning. A face that shows an object the VRM leaves out
        // loses all its visibility tracks, so it never shows half of a switch.
        internal static void Resolve(IEnumerable<VrmMenuExpressions.Expression> expressions, Func<GameObject, int> outputNode, Transform root,
            ICollection<string> warnings)
        {
            var messages = new List<(string Name, string Message)>();
            foreach (var expression in expressions ?? Enumerable.Empty<VrmMenuExpressions.Expression>())
            {
                if (expression.Transforms.Count == 0) continue;
                bool Alive(VrmMenuExpressions.TransformTrack track) => track.Target is GameObject go && go != null;
                bool Exported(VrmMenuExpressions.TransformTrack track) => ExportRendererSelection.ActiveUnder((GameObject)track.Target, root);
                bool Shows(VrmMenuExpressions.TransformTrack track)
                {
                    track.Curves[0].Range(out _, out var maximum);
                    return maximum > 0;
                }
                var hidden = expression.Transforms.Where(track => track.Property == ExpressionTransformAnimation.Active && Alive(track) && !Exported(track) && Shows(track))
                    .Select(track => track.Path).Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray();
                var unmapped = new SortedSet<string>(StringComparer.Ordinal);
                var kept = new List<VrmMenuExpressions.TransformTrack>();
                foreach (var track in expression.Transforms)
                {
                    if (track.Property == ExpressionTransformAnimation.Active)
                    {
                        if (hidden.Length > 0) continue;
                        // Hiding an object the VRM leaves out changes nothing.
                        if (Alive(track) && !Exported(track)) continue;
                    }
                    var node = Alive(track) ? outputNode((GameObject)track.Target) : -1;
                    if (node < 0) { unmapped.Add(track.Path); continue; }
                    track.Node = node;
                    kept.Add(track);
                }
                expression.Transforms.Clear();
                expression.Transforms.AddRange(kept);
                if (hidden.Length > 0) messages.Add((expression.Name, ExporterLocalization.T(HiddenShownWarning) + string.Join(", ", hidden)));
                if (unmapped.Count > 0) messages.Add((expression.Name, ExporterLocalization.T(UnmappedWarning) + string.Join(", ", unmapped)));
            }
            foreach (var group in messages.GroupBy(message => message.Message, StringComparer.Ordinal))
                warnings?.Add(string.Join(", ", group.Select(message => message.Name).Distinct()) + ": " + group.Key);
        }
    }
}
