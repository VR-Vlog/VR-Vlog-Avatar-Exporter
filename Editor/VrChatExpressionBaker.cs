using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    internal static class VrChatExpressionBaker
    {
        internal const string BakedFormat = ExportNoteText.ExpressionsAdded;
        internal const string NoneBakedWarning = ExportNoteText.ExpressionsNone;

        private sealed class Channel
        {
            internal string Path, Shape;
            internal SkinnedMeshRenderer Renderer;
            internal Mesh Mesh;
            internal float[] Rest;
            internal bool HasValue;
            internal float Weight;
            internal ExpressionAnimationData.Curve Curve;
        }

        private sealed class Pose
        {
            internal SkinnedMeshRenderer Renderer;
            internal Mesh Mesh;
            internal float[] Rest;
            internal readonly List<Channel> Channels = new List<Channel>();
        }

        private sealed class Plan
        {
            internal VrChatExpressionMenu.Entry Entry;
            internal readonly List<Pose> Poses = new List<Pose>();
            internal readonly List<Channel> Animation = new List<Channel>();
        }

        internal static bool SameCurve(ExpressionAnimationData.Curve first, ExpressionAnimationData.Curve second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null || first.PreWrap != second.PreWrap || first.PostWrap != second.PostWrap ||
                first.Keys.Count != second.Keys.Count) return false;
            for (var i = 0; i < first.Keys.Count; i++)
            {
                var a = first.Keys[i]; var b = second.Keys[i];
                if (!a.Time.Equals(b.Time) || !a.Value.Equals(b.Value) || !a.InTangent.Equals(b.InTangent) ||
                    !a.OutTangent.Equals(b.OutTangent) || !a.InWeight.Equals(b.InWeight) || !a.OutWeight.Equals(b.OutWeight)) return false;
            }
            return true;
        }

        internal static bool ConstantAt(ExpressionAnimationData.Curve curve, float weight)
        {
            if (curve == null) return true;
            curve.Range(out var minimum, out var maximum);
            return minimum.Equals(maximum) && minimum.Equals((double)weight);
        }

        // Authoring paths retain provenance through NDMF. Baking addresses the
        // final renderer/channel: one pose and one temporal program per channel,
        // with every collision proved compatible before any mesh is changed.
        private static List<Plan> Prepare(GameObject source, GameObject clone, VrChatExpressionMenu.Source menu,
            PreparedExpressionBindings prepared)
        {
            var plans = new List<Plan>();
            foreach (var entry in menu.Entries.Where(value => value.Error == null))
            {
                var origins = new Dictionary<(string Path, string Shape), Channel>();
                Channel Origin(string path, string shape)
                {
                    var key = (path, shape);
                    if (origins.TryGetValue(key, out var found)) return found;
                    var binding = prepared?.Get(path);
                    var original = binding == null ? VrChatExpressionSampler.FindRenderer(source, path) : null;
                    var mesh = binding?.Mesh ?? original.sharedMesh;
                    var renderer = binding?.Renderer ?? VrChatExpressionSampler.FindRenderer(clone, path);
                    var rest = binding?.Weights ?? Enumerable.Range(0, mesh.blendShapeCount).Select(original.GetBlendShapeWeight).ToArray();
                    var channel = new Channel { Path = path, Shape = shape, Renderer = renderer, Mesh = mesh, Rest = rest };
                    origins.Add(key, channel); return channel;
                }
                InvalidOperationException Conflict(Channel channel) => new InvalidOperationException(
                    "Modular Avatar / NDMF の処理で同じ表情の変形に異なる値・アニメーションが統合されました。統合前の表情設定を一致させてください: " +
                    entry.Name + " / " + UnityEditor.AnimationUtility.CalculateTransformPath(channel.Renderer.transform, clone.transform) + " / " + channel.Shape);
                foreach (var value in entry.Values)
                {
                    var channel = Origin(value.Path, value.Shape);
                    if (channel.HasValue && !channel.Weight.Equals(value.Weight)) throw Conflict(channel);
                    channel.HasValue = true; channel.Weight = value.Weight;
                }
                foreach (var value in entry.Animation)
                {
                    var channel = Origin(value.Path, value.Shape);
                    if (channel.Curve != null && !SameCurve(channel.Curve, value.Curve)) throw Conflict(channel);
                    channel.Curve = value.Curve;
                }
                // The readers record an animated channel's static value as the
                // clip's first pose, so a merged value must still match that.
                // The value itself is not baked: the animation alone moves the
                // channel, and the VRM expression takes its last frame.
                foreach (var channel in origins.Values)
                {
                    if (channel.Curve != null)
                    {
                        var initial = (float)channel.Curve.Evaluate(0);
                        if (channel.HasValue && !channel.Weight.Equals(initial)) throw Conflict(channel);
                        channel.Weight = initial;
                    }
                }
                var plan = new Plan { Entry = entry };
                foreach (var group in origins.Values.GroupBy(channel => (channel.Renderer, channel.Shape)))
                {
                    var channel = group.First();
                    foreach (var other in group.Skip(1))
                    {
                        if (!channel.Weight.Equals(other.Weight) ||
                            !(SameCurve(channel.Curve, other.Curve) || ConstantAt(channel.Curve, channel.Weight) && ConstantAt(other.Curve, other.Weight)))
                            throw Conflict(channel);
                        if (!ReferenceEquals(channel.Mesh, other.Mesh) || !channel.Rest.SequenceEqual(other.Rest))
                            throw new InvalidOperationException("処理後の表情の基準形を一意に確定できません: " + entry.Name + " / " + channel.Shape);
                    }
                    var pose = plan.Poses.SingleOrDefault(value => value.Renderer == channel.Renderer);
                    if (pose == null)
                    {
                        pose = new Pose { Renderer = channel.Renderer, Mesh = channel.Mesh, Rest = channel.Rest };
                        plan.Poses.Add(pose);
                    }
                    else if (!ReferenceEquals(pose.Mesh, channel.Mesh) || !pose.Rest.SequenceEqual(channel.Rest))
                        throw new InvalidOperationException("処理後の表情の基準形を一意に確定できません: " + entry.Name + " / " + channel.Shape);
                    pose.Channels.Add(channel);
                    if (channel.Curve != null) plan.Animation.Add(channel);
                }
                plans.Add(plan);
            }
            return plans;
        }

        // The rebased export shape is one frame holding Evaluate(F) - Evaluate(rest).
        // A single positive frame is linear, so raising the channel from rest
        // up to F is that shape at (w - rest) / (F - rest). Anything else, such
        // as lowering below rest or a curved multi-frame shape, returns null
        // and stays a baked residual.
        private static float? DirectWeight(Mesh original, Mesh copy, int shape, float rest, float weight, bool clampToSourceRange)
        {
            if (float.IsNaN(rest) || float.IsInfinity(rest) || float.IsNaN(weight) || float.IsInfinity(weight)) return null;
            if (original.GetBlendShapeFrameCount(shape) != 1) return null;
            var frame = (double)original.GetBlendShapeFrameWeight(shape, 0);
            if (!(frame > 0) || double.IsInfinity(frame)) return null;
            var exported = copy.GetBlendShapeIndex(original.GetBlendShapeName(shape));
            if (exported < 0 || copy.GetBlendShapeFrameCount(exported) != 1) return null;
            var r = clampToSourceRange ? AvatarBaseShape.ClampSourceWeight(original, shape, rest) : rest;
            var w = clampToSourceRange ? AvatarBaseShape.ClampSourceWeight(original, shape, weight) : weight;
            if (r > w || w > frame || frame <= r) return null;
            return (float)((w - r) / (frame - r));
        }

        // An animated channel moves the rebased export shape directly when the
        // whole curve stays between rest and the shape's single frame, where
        // the shape is linear and Unity clamps nothing. The two points are then
        // rest (moves nothing) and the frame (the export shape at full weight).
        private static bool DirectAnimation(Mesh original, Mesh copy, int shape, float rest, double minimum, double maximum,
            bool clampToSourceRange, out double from, out double to)
        {
            from = to = 0;
            if (original.GetBlendShapeFrameCount(shape) != 1) return false;
            var frame = original.GetBlendShapeFrameWeight(shape, 0);
            if (DirectWeight(original, copy, shape, rest, frame, clampToSourceRange) != 1f) return false;
            if (double.IsNaN(minimum) || double.IsInfinity(minimum) || double.IsNaN(maximum) || double.IsInfinity(maximum)) return false;
            if (clampToSourceRange && (AvatarBaseShape.ClampSourceWeight(original, shape, minimum) != minimum ||
                AvatarBaseShape.ClampSourceWeight(original, shape, maximum) != maximum)) return false;
            from = clampToSourceRange ? AvatarBaseShape.ClampSourceWeight(original, shape, rest) : rest;
            to = frame;
            // Points keep the animation schema's bounds; a larger frame is baked.
            return from >= -10000 && to <= 10000 && from <= minimum && maximum <= to && to > from;
        }

        internal static List<VrmMenuExpressions.Expression> Bake(GameObject source, GameObject clone,
            VrChatExpressionMenu.Source menu, ICollection<Mesh> meshes, ICollection<string> warnings)
            => Bake(source, clone, menu, meshes, warnings, null);

        // visemeRenderer and visemeShapes are what AvatarBaseShape.Preserve received.
        internal static List<VrmMenuExpressions.Expression> Bake(GameObject source, GameObject clone,
            VrChatExpressionMenu.Source menu, ICollection<Mesh> meshes, ICollection<string> warnings,
            PreparedExpressionBindings prepared, SkinnedMeshRenderer visemeRenderer = null, IEnumerable<string> visemeShapes = null)
        {
            var result = new List<VrmMenuExpressions.Expression>();
            // A viseme exported without the other visemes' rests is not the
            // linear shape the direct binds assume. Its pose stays baked, or the
            // expression would take those rests off a second time.
            var withoutOtherRests = new Dictionary<SkinnedMeshRenderer, HashSet<int>>();
            HashSet<int> WithoutOtherRests(SkinnedMeshRenderer skin, Mesh mesh, float[] rest)
            {
                if (!withoutOtherRests.TryGetValue(skin, out var shapes))
                {
                    shapes = AvatarBaseShape.ShapesWithoutOtherRests(mesh, rest, AvatarBaseShape.ExclusiveShapes(skin, mesh, visemeRenderer, visemeShapes));
                    withoutOtherRests.Add(skin, shapes);
                }
                return shapes;
            }
            if (prepared == null) new PreparedExpressionBindings(clone, menu, source).Capture(menu);
            var plans = Prepare(source, clone, menu, prepared);
            foreach (var message in menu.Messages) warnings?.Add(message);
            // A controller-wide limitation or a stale shared curve should not
            // print the same explanation once for every menu item.
            var diagnostics = menu.Entries.SelectMany(e => e.Messages.Concat(e.Error == null ? Array.Empty<string>() : new[] { e.Error })
                .Select(message => (e.Name, Message: message)));
            foreach (var group in diagnostics.GroupBy(d => d.Message, StringComparer.Ordinal))
                warnings?.Add(string.Join(", ", group.Select(d => d.Name).Distinct()) + ": " + group.Key);
            // Unique internal names survive UniVRM's renderer/node reordering.
            // Bind against the actual exported names, never guessed mesh indices.
            // Names hash the renderer, entry and source channels, so the same
            // avatar exports the same names; _2, _3 settle repeated inputs.
            var usedNames = DeterministicNames.BlendShapeNames(clone);
            string PathOf(SkinnedMeshRenderer skin) => UnityEditor.AnimationUtility.CalculateTransformPath(skin.transform, clone.transform);
            string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
            var clampToSourceRange = UnityEditor.PlayerSettings.legacyClampBlendShapeWeights;
            long generatedBytes = 0;
            var basis = new Dictionary<(Mesh mesh, int shape, double rest, double value), string>();
            // A zero residual remains zero at every accumulated weight, even
            // with legacy clamping. Only these scalar markers may share an
            // index; nonzero poses retain independent simultaneous targets.
            var zeroScalarTargets = new Dictionary<Mesh, string>();
            bool Zero(AvatarBaseShape.Deltas delta) => new[] { delta.Vertices, delta.Normals, delta.Tangents }
                .All(values => values.All(value => value.x == 0f && value.y == 0f && value.z == 0f));
            // Animation programs are mutually exclusive. Reuse exact basis
            // geometry across those programs only; scalar menu identities and
            // all distinct targets inside one animation remain independent.
            var geometry = new Dictionary<(Mesh mesh, ulong hash), List<string>>();
            Vector3[] compareVertices = null, compareNormals = null, compareTangents = null;
            ulong Hash(AvatarBaseShape.Deltas delta)
            {
                ulong hash = 14695981039346656037UL;
                unchecked
                {
                    foreach (var values in new[] { delta.Vertices, delta.Normals, delta.Tangents })
                        foreach (var value in values)
                        {
                            hash = (hash ^ (uint)value.x.GetHashCode()) * 1099511628211UL;
                            hash = (hash ^ (uint)value.y.GetHashCode()) * 1099511628211UL;
                            hash = (hash ^ (uint)value.z.GetHashCode()) * 1099511628211UL;
                        }
                }
                return hash;
            }
            string EquivalentTarget(Mesh mesh, AvatarBaseShape.Deltas delta, ISet<string> used, ulong hash)
            {
                if (!geometry.TryGetValue((mesh, hash), out var names)) return null;
                if (compareVertices == null || compareVertices.Length != mesh.vertexCount)
                {
                    compareVertices = new Vector3[mesh.vertexCount];
                    compareNormals = new Vector3[mesh.vertexCount];
                    compareTangents = new Vector3[mesh.vertexCount];
                }
                foreach (var name in names)
                {
                    if (used.Contains(name)) continue;
                    mesh.GetBlendShapeFrameVertices(mesh.GetBlendShapeIndex(name), 0, compareVertices, compareNormals, compareTangents);
                    // Hashes only choose candidates. Exact P/N/T equality,
                    // never a distance tolerance, establishes reuse.
                    if (delta.Vertices.SequenceEqual(compareVertices) && delta.Normals.SequenceEqual(compareNormals) &&
                        delta.Tangents.SequenceEqual(compareTangents)) return name;
                }
                return null;
            }
            void Reserve(Mesh mesh)
            {
                generatedBytes += mesh.vertexCount * 36L;
                if (generatedBytes > 128L * 1024 * 1024)
                    throw new InvalidOperationException("表情の追加データが上限の128 MiBを超えます。元のメッシュの頂点数や表情アニメーションを整理してください。");
            }
            foreach (var plan in plans)
            {
                var entry = plan.Entry;
                var expression = new VrmMenuExpressions.Expression { Name = entry.VrmName(), InferenceName = entry.Name, Source = entry.Source, Clip = entry.Clip };
                // The source shapes and end weights, before any of them is
                // baked into a menu target, for emotion inference.
                foreach (var group in plan.Poses)
                    foreach (var value in group.Channels.Where(value => value.Curve == null))
                    {
                        var index = group.Mesh.GetBlendShapeIndex(value.Shape);
                        if (index >= 0 && value.Weight != group.Rest[index]) expression.Shapes.Add((value.Shape, value.Weight));
                    }
                foreach (var animated in plan.Animation)
                {
                    var index = animated.Mesh.GetBlendShapeIndex(animated.Shape);
                    var end = animated.Curve.Evaluate(entry.Duration);
                    if (index >= 0 && end != animated.Rest[index]) expression.Shapes.Add((animated.Shape, (float)end));
                }
                // Binds that change the face at time = duration; a zero marker does not.
                var moved = 0;
                foreach (var group in plan.Poses)
                {
                    var originalMesh = group.Mesh;
                    var copy = group.Renderer;
                    if (!meshes.Contains(copy.sharedMesh) || ReferenceEquals(copy.sharedMesh, originalMesh))
                    {
                        copy.sharedMesh = UnityEngine.Object.Instantiate(copy.sharedMesh);
                        meshes.Add(copy.sharedMesh);
                    }
                    // Animated channels belong to the animation alone; the
                    // static shape keeps them at rest.
                    var fixedChannels = group.Channels.Where(value => value.Curve == null).ToArray();
                    if (fixedChannels.Length == 0) continue;
                    var rest = group.Rest;
                    var pose = (float[])rest.Clone();
                    var residual = new List<Channel>();
                    var direct = 0;
                    foreach (var value in fixedChannels)
                    {
                        var index = originalMesh.GetBlendShapeIndex(value.Shape);
                        if (index < 0) throw new InvalidOperationException("表情の元BlendShapeが見つかりません: " + value.Shape);
                        var weight = WithoutOtherRests(copy, originalMesh, rest).Contains(index) ? null :
                            DirectWeight(originalMesh, copy.sharedMesh, index, rest[index], value.Weight, clampToSourceRange);
                        if (weight == null) { residual.Add(value); pose[index] = value.Weight; continue; }
                        if (weight.Value == 0f) continue;
                        expression.Direct.Add((copy, value.Shape, weight.Value));
                        direct++;
                    }
                    moved += direct;
                    var delta = AvatarBaseShape.ExpressionDeltas(originalMesh, rest, pose, clampToSourceRange);
                    var zero = Zero(delta);
                    if (!zero) moved++;
                    // The direct binds already keep this entry selectable.
                    if (zero && direct > 0) continue;
                    if (!zero || !zeroScalarTargets.TryGetValue(copy.sharedMesh, out var name))
                    {
                        Reserve(originalMesh);
                        var parts = new List<string> { "Menu", PathOf(copy), entry.Name };
                        foreach (var value in residual) { parts.Add(value.Path); parts.Add(value.Shape); parts.Add(Number(value.Weight)); }
                        name = DeterministicNames.Unique("__VRVlog_Menu_" + DeterministicNames.Hash(parts.ToArray()), usedNames);
                        copy.sharedMesh.AddBlendShapeFrame(name, 100f, delta.Vertices, delta.Normals, delta.Tangents);
                        if (zero) zeroScalarTargets.Add(copy.sharedMesh, name);
                    }
                    expression.Targets.Add(name);
                }
                if (plan.Animation.Count > 0)
                {
                    expression.Animation = new ExpressionAnimationData { Duration = entry.Duration, Loop = entry.Loop };
                    var usedTargets = new HashSet<string>(StringComparer.Ordinal);
                    var endsAwayFromRest = false;
                    foreach (var animated in plan.Animation)
                    {
                        var original = animated.Mesh;
                        var copy = animated.Renderer.sharedMesh;
                        var shape = original.GetBlendShapeIndex(animated.Shape);
                        if (shape < 0) throw new InvalidOperationException("表情の元BlendShapeが見つかりません: " + animated.Shape);
                        animated.Curve.Range(out var minimum, out var maximum);
                        if (animated.Curve.Evaluate(entry.Duration) != animated.Rest[shape]) endsAwayFromRest = true;
                        var channel = new ExpressionAnimationData.Channel { Curve = animated.Curve };
                        expression.Animation.Channels.Add(channel);
                        if (!WithoutOtherRests(animated.Renderer, original, animated.Rest).Contains(shape) &&
                            DirectAnimation(original, copy, shape, animated.Rest[shape], minimum, maximum, clampToSourceRange, out var from, out var to))
                        {
                            var full = new ExpressionAnimationData.Point { Value = to };
                            channel.Points.Add(new ExpressionAnimationData.Point { Value = from });
                            channel.Points.Add(full);
                            expression.DirectPoints.Add(full, (animated.Renderer, animated.Shape));
                            continue;
                        }
                        // A constant curve outside the direct range would bake
                        // one point, which Channel.Weights cannot blend.
                        if (!(minimum < maximum)) throw new InvalidOperationException("表情のアニメーションの値が変化しません: " + entry.Name + " / " + animated.Shape);
                        // Morph residuals are affine between source frames.
                        // Enclose varying curves with canonical frame endpoints,
                        // so different extrema can use the same exact basis.
                        // Every residual is measured from rest, the shape a
                        // point without a morph leaves in place.
                        var rest = (double)animated.Rest[shape];
                        var frames = new SortedSet<double> { 0 };
                        for (var frame = 0; frame < original.GetBlendShapeFrameCount(shape); frame++)
                        {
                            var weight = original.GetBlendShapeFrameWeight(shape, frame);
                            // Keep the animation schema's existing point bounds;
                            // a larger source endpoint uses the curve fallback.
                            if (!float.IsNaN(weight) && !float.IsInfinity(weight) && weight >= -10000 && weight <= 10000) frames.Add(weight);
                        }
                        var knots = new SortedSet<double> { minimum, maximum };
                        if (minimum < maximum)
                        {
                            var candidates = new SortedSet<double>(frames) { rest };
                            var lower = candidates.Where(weight => weight <= minimum).DefaultIfEmpty(minimum).Max();
                            var upper = candidates.Where(weight => weight >= maximum).DefaultIfEmpty(maximum).Min();
                            knots = new SortedSet<double> { lower, upper };
                            // Rest selects an endpoint when useful; do not add
                            // an unnecessary interior point for it.
                            foreach (var weight in frames) if (weight > lower && weight < upper) knots.Add(weight);
                        }
                        foreach (var weight in knots)
                        {
                            string target = null;
                            if (weight != rest && (!basis.TryGetValue((copy, shape, rest, weight), out target) || usedTargets.Contains(target)))
                            {
                                var delta = AvatarBaseShape.AnimatedDeltas(original, shape, rest, weight, clampToSourceRange);
                                var hash = Hash(delta);
                                target = EquivalentTarget(copy, delta, usedTargets, hash);
                                if (target == null)
                                {
                                    Reserve(original);
                                    target = DeterministicNames.Unique(ExpressionAnimationData.TargetPrefix + "__VRVlog_Menu_" + DeterministicNames.Hash("MenuAnimation",
                                        PathOf(animated.Renderer), entry.Name, animated.Path, animated.Shape, Number(rest), Number(weight)), usedNames);
                                    copy.AddBlendShapeFrame(target, 100f, delta.Vertices, delta.Normals, delta.Tangents);
                                    if (!geometry.TryGetValue((copy, hash), out var names)) geometry.Add((copy, hash), names = new List<string>());
                                    names.Add(target);
                                }
                                basis[(copy, shape, rest, weight)] = target;
                            }
                            if (target != null) usedTargets.Add(target);
                            channel.Points.Add(new ExpressionAnimationData.Point { Value = weight, Target = target });
                        }
                    }
                    // Every channel ending at rest is the neutral face even
                    // when its final points are two binds that cancel out.
                    if (endsAwayFromRest) moved += expression.FinalPoints().Count();
                    // The loop ends on the neutral face. Keep the expression
                    // selectable with the shared zero marker unless those
                    // binds already do.
                    if (moved == 0)
                    {
                        expression.EndsNeutral = true;
                        var renderer = plan.Animation[0].Renderer;
                        if (expression.Targets.Count == 0 && !expression.FinalPoints().Any())
                        {
                            if (!zeroScalarTargets.TryGetValue(renderer.sharedMesh, out var name))
                            {
                                Reserve(plan.Animation[0].Mesh);
                                name = DeterministicNames.Unique("__VRVlog_Menu_" + DeterministicNames.Hash("Menu", PathOf(renderer), entry.Name), usedNames);
                                var empty = new Vector3[renderer.sharedMesh.vertexCount];
                                renderer.sharedMesh.AddBlendShapeFrame(name, 100f, empty, empty, empty);
                                zeroScalarTargets.Add(renderer.sharedMesh, name);
                            }
                            expression.Targets.Add(name);
                        }
                    }
                }
                if (expression.Targets.Count > 0 || expression.Direct.Count > 0 || expression.FinalPoints().Any()) result.Add(expression);
            }
            if (result.Count > 0) warnings?.Add(string.Format(ExporterLocalization.T(BakedFormat), result.Count));
            else warnings?.Add(ExporterLocalization.T(NoneBakedWarning));
            return result;
        }
    }
}
