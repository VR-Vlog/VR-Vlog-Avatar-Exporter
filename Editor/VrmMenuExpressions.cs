using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.Expressions;
using VRVlog.FaceTracking;

namespace VRVlog.LilToonExporter
{
    internal static class VrmMenuExpressions
    {
        internal sealed class Expression
        {
            internal string Name;
            // The text emotion inference reads (the menu path, e.g. "Smile / 1");
            // null reads Name.
            internal string InferenceName;
            // The name Add registered, with any duplicate-name suffix.
            internal string RegisteredName;
            // Where the expression came from (ExpressionSource), listed in the
            // expression animation extension.
            internal string Source = ExpressionSource.Menu;
            // The gesture clip this face plays, if any (VrChatExpressionMenu.Entry.Clip).
            internal UnityEngine.Object Clip = null;
            // Original BlendShape names this face moves away from rest, with
            // the weight (0-100) it ends on. Emotion inference reads them when
            // the menu label says nothing; the final binds may use baked or
            // renamed targets instead.
            internal readonly List<(string Shape, float Weight)> Shapes = new List<(string Shape, float Weight)>();
            internal readonly List<string> Targets = new List<string>();
            // Shapes on the export copies that reproduce a channel by
            // themselves: the final morph target scaled by Weight.
            internal readonly List<(UnityEngine.SkinnedMeshRenderer Renderer, string Shape, float Weight)> Direct =
                new List<(UnityEngine.SkinnedMeshRenderer Renderer, string Shape, float Weight)>();
            internal ExpressionAnimationData Animation = null;
            // Animation points that move an original shape of the export copy.
            // Other points name a baked morph (Point.Target) or move nothing.
            internal readonly Dictionary<ExpressionAnimationData.Point, (UnityEngine.SkinnedMeshRenderer Renderer, string Shape)> DirectPoints =
                new Dictionary<ExpressionAnimationData.Point, (UnityEngine.SkinnedMeshRenderer Renderer, string Shape)>();
            // The animation ends on the neutral face; the VRM expression only
            // holds a zero marker that keeps it selectable.
            internal bool EndsNeutral = false;

            // The animation's shape at time = duration: per channel the two
            // points Channel.Weights picks, at 1 - blend and blend. Points that
            // move nothing and zero weights are left out.
            internal IEnumerable<(ExpressionAnimationData.Point Point, float Weight)> FinalPoints()
            {
                if (Animation == null) yield break;
                foreach (var channel in Animation.Channels)
                {
                    channel.Weights(Animation.Duration, out var left, out var right, out var blend);
                    if (1f - blend != 0f && Moves(channel.Points[left])) yield return (channel.Points[left], 1f - blend);
                    if (blend != 0f && Moves(channel.Points[right])) yield return (channel.Points[right], blend);
                }
            }

            bool Moves(ExpressionAnimationData.Point point) => point.Target != null || point.Morph != null || DirectPoints.ContainsKey(point);
        }

        // Resolves a direct reference through the optimizer route captured
        // before export, so renamed or merged shapes keep their binding.
        internal interface IDirectBindings
        {
            Dictionary<string, object> MenuBinding(Dictionary<string, object> json, UnityEngine.SkinnedMeshRenderer renderer, string shape, float weight);
        }

        // Counts the expressions this exporter registered: those listed in the
        // expression animation extension that bind a morph. Authored custom
        // expressions and unbound names are not counted.
        internal static int CountRegistered(byte[] bytes)
        {
            var json = GlbDocument.Read(bytes).Json;
            var custom = Path(json, "extensions", "VRMC_vrm", "expressions", "custom");
            if (custom == null) return 0;
            return RegisteredNames(json).Count(name => custom.TryGetValue(name, out var value) &&
                value is Dictionary<string, object> expression && expression.TryGetValue("morphTargetBinds", out var binds) &&
                binds is List<object> list && list.Count > 0);
        }

        // Names the expression animation extension lists as registered by this
        // exporter, with their final duplicate-name suffix. Empty without it.
        internal static IReadOnlyCollection<string> RegisteredNames(byte[] bytes) => RegisteredNames(GlbDocument.Read(bytes).Json);

        // The expressions Add registered, by final name, with the text emotion
        // inference reads for each: the same text as before names lost their
        // menu path, so a "Smile" submenu still makes its item "1" happy.
        internal static Dictionary<string, string> EmotionCandidates(IEnumerable<Expression> expressions) =>
            (expressions ?? Enumerable.Empty<Expression>()).Where(e => e.RegisteredName != null)
                .ToDictionary(e => e.RegisteredName, e => e.InferenceName ?? e.Name, StringComparer.Ordinal);

        // The original shapes each registered expression moves, by final name.
        internal static Dictionary<string, IReadOnlyList<(string Shape, float Weight)>> EmotionShapes(IEnumerable<Expression> expressions) =>
            (expressions ?? Enumerable.Empty<Expression>()).Where(e => e.RegisteredName != null && e.Shapes.Count > 0)
                .ToDictionary(e => e.RegisteredName, e => (IReadOnlyList<(string Shape, float Weight)>)e.Shapes, StringComparer.Ordinal);

        static IReadOnlyCollection<string> RegisteredNames(Dictionary<string, object> json)
        {
            var extensions = Path(json, "extensions");
            if (extensions == null || !extensions.TryGetValue(ExpressionAnimationData.Extension, out var value)) return Array.Empty<string>();
            return ExpressionAnimationData.ReadDocument(value).Expressions.Select(e => e.Expression).ToArray();
        }

        static Dictionary<string, object> Path(Dictionary<string, object> current, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!current.TryGetValue(key, out var value) || !(value is Dictionary<string, object> next)) return null;
                current = next;
            }
            return current;
        }

        static readonly HashSet<string> PresetNames = new HashSet<string>(
            Enum.GetNames(typeof(UniVRM10.ExpressionPreset)).Where(name => name != "custom"), StringComparer.Ordinal);

        // A custom expression must not take a VRM 1.0 preset name (UniVRM keys
        // imported expressions by name, so custom "happy" would collide with
        // the preset) or a face tracking channel name (the app and the
        // tracking output would read it as that channel, and a tracking
        // profile refuses to replace an existing expression). The app drops
        // the whole expression animation extension when a listed name is a
        // preset (exact, case-sensitive), a Perfect Sync or Unified
        // Expressions channel by its own matching, or the rest clip it adds
        // for Unified Expressions; each of those is reserved here.
        internal static bool Reserved(string name) =>
            PresetNames.Contains(name) || TrackingExpressionNames.IsPerfectSync(name) ||
            UnifiedExpressionRegistry.TryCanonicalize(name, out _) || IsUnifiedRest(name);

        // The app names its rest clip RestPrefix, then RestPrefix2, RestPrefix3...
        static bool IsUnifiedRest(string name) =>
            name != null && name.StartsWith(UnifiedExpressionRegistry.RestPrefix, StringComparison.Ordinal) &&
            name.Skip(UnifiedExpressionRegistry.RestPrefix.Length).All(c => c >= '0' && c <= '9');

        // name, or name (2), name (3)... when taken or reserved.
        internal static string UniqueName(string name, Func<string, bool> taken)
        {
            var result = name;
            var suffix = 2;
            while (taken(result) || Reserved(result)) result = name + " (" + suffix++ + ")";
            return result;
        }

        // Register only actual menu entries. Each one binds its composite
        // targets at full weight, the original shapes it moves linearly at
        // their own weight, and its animation's shape at time = duration;
        // raw shapes do not become selectable expressions. The animation
        // extension (schema 2) lists every registered expression with its
        // source and refers to morphs by node and morph target index.
        internal static byte[] Add(byte[] bytes, IList<Expression> expressions, IDirectBindings direct = null, ICollection<string> warnings = null)
        {
            if (expressions.Count == 0) return bytes;
            var glb = GlbDocument.Read(bytes);
            var vrm = (Dictionary<string, object>)((Dictionary<string, object>)glb.Json["extensions"])["VRMC_vrm"];
            var root = Object(vrm, "expressions");
            var custom = Object(root, "custom");
            var nodes = (List<object>)glb.Json["nodes"];
            var meshes = (List<object>)glb.Json["meshes"];
            var document = new ExpressionAnimationDocument();
            var written = new List<(UnityEngine.Object Clip, string Name, string Inference, string Source, List<object> Binds, ExpressionAnimationData Animation)>();
            foreach (var expression in expressions)
            {
                // The name carries no source prefix; the extension lists the
                // source. Recorded and authored custom faces keep their names.
                var name = UniqueName(expression.Name, custom.ContainsKey);
                ExpressionAnimationData.MorphReference Named(string target)
                {
                    var matches = new List<ExpressionAnimationData.MorphReference>();
                    for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                    {
                        var node = (Dictionary<string, object>)nodes[nodeIndex];
                        if (!node.TryGetValue("mesh", out var meshIndex)) continue;
                        var mesh = (Dictionary<string, object>)meshes[Convert.ToInt32(meshIndex)];
                        if (!mesh.TryGetValue("extras", out var extras) || !((Dictionary<string, object>)extras).TryGetValue("targetNames", out var rawNames)) continue;
                        var names = (List<object>)rawNames;
                        for (var i = 0; i < names.Count; i++)
                        {
                            if (!string.Equals(names[i] as string, target, StringComparison.Ordinal)) continue;
                            foreach (Dictionary<string, object> primitive in (List<object>)mesh["primitives"])
                                if (!primitive.TryGetValue("targets", out var targets) || ((List<object>)targets).Count != names.Count)
                                    throw new InvalidOperationException("表情の出力先モーフ数が一致しません: " + expression.Name);
                            matches.Add(new ExpressionAnimationData.MorphReference { Node = nodeIndex, Index = i });
                        }
                    }
                    if (matches.Count != 1) throw new InvalidOperationException("表情の出力先を一意に特定できません: " + expression.Name);
                    return matches.Single();
                }
                if ((expression.Direct.Count > 0 || expression.DirectPoints.Count > 0) && direct == null)
                    throw new InvalidOperationException("表情の元BlendShapeの出力先を解決できません: " + expression.Name);
                var binds = new List<object>();
                void Bind(Dictionary<string, object> bind, string shape)
                {
                    // Merged source shapes keep the first declaration only,
                    // as UniVRM would otherwise apply one target twice. One
                    // merged target cannot hold two weights; say which was kept.
                    var first = binds.Cast<Dictionary<string, object>>().FirstOrDefault(b => Convert.ToInt64(b["node"]) == Convert.ToInt64(bind["node"]) &&
                        Convert.ToInt64(b["index"]) == Convert.ToInt64(bind["index"]));
                    if (first != null)
                    {
                        if (Math.Abs(Convert.ToDouble(first["weight"]) - Convert.ToDouble(bind["weight"])) > 1e-4)
                            warnings?.Add("表情「" + expression.Name + "」で、最適化ツールが1つにまとめたシェイプキー「" + shape +
                                "」に別々の値が指定されていたため、最初の値だけを使いました。");
                        return;
                    }
                    binds.Add(bind);
                }
                Dictionary<string, object> Weighted(ExpressionAnimationData.MorphReference morph, double weight) =>
                    new Dictionary<string, object> { ["node"] = (long)morph.Node, ["index"] = (long)morph.Index, ["weight"] = weight };
                foreach (var target in expression.Targets) Bind(Weighted(Named(target), 1.0), target);
                foreach (var (renderer, shape, weight) in expression.Direct) Bind(direct.MenuBinding(glb.Json, renderer, shape, weight), shape);
                var fixedMorphs = binds.Cast<Dictionary<string, object>>().Select(b => (Convert.ToInt32(b["node"]), Convert.ToInt32(b["index"]))).ToList();
                ExpressionAnimationData animation = null;
                if (expression.Animation != null)
                {
                    // Copy the program with every point resolved to its output
                    // node and morph target index; null points stay null.
                    var resolved = new Dictionary<ExpressionAnimationData.Point, ExpressionAnimationData.MorphReference>();
                    animation = new ExpressionAnimationData { Duration = expression.Animation.Duration, Loop = expression.Animation.Loop };
                    foreach (var channel in expression.Animation.Channels)
                    {
                        var copy = new ExpressionAnimationData.Channel { Curve = channel.Curve };
                        foreach (var point in channel.Points)
                        {
                            ExpressionAnimationData.MorphReference morph = null;
                            if (point.Target != null) morph = Named(point.Target);
                            else if (expression.DirectPoints.TryGetValue(point, out var shape))
                            {
                                var bind = direct.MenuBinding(glb.Json, shape.Renderer, shape.Shape, 1f);
                                morph = new ExpressionAnimationData.MorphReference { Node = Convert.ToInt32(bind["node"]), Index = Convert.ToInt32(bind["index"]) };
                            }
                            else if (point.Morph != null) morph = point.Morph;
                            if (morph != null) resolved.Add(point, morph);
                            copy.Points.Add(new ExpressionAnimationData.Point { Value = point.Value, Morph = morph });
                        }
                        animation.Channels.Add(copy);
                    }
                    foreach (var (point, weight) in expression.FinalPoints())
                        Bind(Weighted(resolved[point], weight), point.Target ?? (expression.DirectPoints.TryGetValue(point, out var source) ? source.Shape : null));
                    var morphs = resolved.Values.Select(morph => (morph.Node, morph.Index)).ToList();
                    if (morphs.Distinct().Count() != morphs.Count || morphs.Any(fixedMorphs.Contains))
                    {
                        // An optimizer merged two animated shapes, or an
                        // animated shape and a fixed one, into one morph; the
                        // app could not move them apart. Keep the VRM
                        // expression (the last frame) without the motion.
                        warnings?.Add(expression.Name + ": 表情のアニメーションが動かす変形が書き出し時にまとめられたため、アニメーションを省いて最後のフレームの顔だけを登録しました。");
                        animation = null;
                    }
                }
                if (binds.Count == 0) throw new InvalidOperationException("表情の出力先がありません: " + expression.Name);
                // One gesture clip registered twice (both hands) writes the
                // same binds and motion under the same name; the app would
                // list it as name and name (2). Keep the first; the other stays
                // unregistered (RegisteredName null). Only the same clip: the
                // board lists each clip once, and separate clips stay separate.
                var inference = expression.InferenceName ?? expression.Name;
                if (expression.Clip != null && written.Any(previous => previous.Clip == expression.Clip && previous.Name == expression.Name &&
                    previous.Inference == inference && previous.Source == expression.Source &&
                    SameBinds(previous.Binds, binds) && SameAnimation(previous.Animation, animation))) continue;
                written.Add((expression.Clip, expression.Name, inference, expression.Source, binds, animation));
                custom.Add(name, new Dictionary<string, object>
                {
                    ["morphTargetBinds"] = binds, ["isBinary"] = true,
                    ["overrideBlink"] = "block", ["overrideMouth"] = "block", ["overrideLookAt"] = "block"
                });
                document.Expressions.Add(new ExpressionSource { Expression = name, Source = expression.Source });
                expression.RegisteredName = name;
                if (animation != null)
                {
                    animation.Expression = name; // Includes any duplicate-name suffix.
                    if (expression.Animation != null) expression.Animation.Expression = name;
                    document.Animations.Add(animation);
                }
            }
            var extensions = (Dictionary<string, object>)glb.Json["extensions"];
            if (extensions.ContainsKey(ExpressionAnimationData.Extension))
                throw new InvalidOperationException("既存の表情アニメーション拡張を上書きできません。");
            extensions.Add(ExpressionAnimationData.Extension, ExpressionAnimationData.WriteDocument(document));
            if (!glb.Json.TryGetValue("extensionsUsed", out var used)) glb.Json["extensionsUsed"] = used = new List<object>();
            ((List<object>)used).Add(ExpressionAnimationData.Extension);
            return glb.Write();
        }

        static bool SameBinds(List<object> first, List<object> second) =>
            first.Count == second.Count && first.Zip(second, SameBind).All(same => same);

        static bool SameBind(object first, object second)
        {
            var a = (Dictionary<string, object>)first;
            var b = (Dictionary<string, object>)second;
            return Convert.ToInt64(a["node"]) == Convert.ToInt64(b["node"]) && Convert.ToInt64(a["index"]) == Convert.ToInt64(b["index"]) &&
                Convert.ToDouble(a["weight"]).Equals(Convert.ToDouble(b["weight"]));
        }

        // Equal as written: duration, loop, every curve key and every point's
        // value and output morph.
        static bool SameAnimation(ExpressionAnimationData first, ExpressionAnimationData second)
        {
            if (first == null || second == null) return first == second;
            if (!first.Duration.Equals(second.Duration) || first.Loop != second.Loop || first.Channels.Count != second.Channels.Count) return false;
            for (var c = 0; c < first.Channels.Count; c++)
            {
                var a = first.Channels[c];
                var b = second.Channels[c];
                if (a.Curve.PreWrap != b.Curve.PreWrap || a.Curve.PostWrap != b.Curve.PostWrap ||
                    a.Curve.Keys.Count != b.Curve.Keys.Count || a.Points.Count != b.Points.Count) return false;
                for (var k = 0; k < a.Curve.Keys.Count; k++)
                {
                    var p = a.Curve.Keys[k];
                    var q = b.Curve.Keys[k];
                    if (!p.Time.Equals(q.Time) || !p.Value.Equals(q.Value) || !Nullable.Equals(p.InTangent, q.InTangent) ||
                        !Nullable.Equals(p.OutTangent, q.OutTangent) || !p.InWeight.Equals(q.InWeight) || !p.OutWeight.Equals(q.OutWeight)) return false;
                }
                for (var i = 0; i < a.Points.Count; i++)
                {
                    var p = a.Points[i];
                    var q = b.Points[i];
                    if (!p.Value.Equals(q.Value) || (p.Morph == null) != (q.Morph == null) ||
                        p.Morph != null && (p.Morph.Node != q.Morph.Node || p.Morph.Index != q.Morph.Index)) return false;
                }
            }
            return true;
        }

        private static Dictionary<string, object> Object(Dictionary<string, object> parent, string key)
        {
            if (!parent.TryGetValue(key, out var value)) parent[key] = value = new Dictionary<string, object>();
            return value as Dictionary<string, object> ?? throw new InvalidOperationException("Invalid VRM expression object: " + key);
        }
    }
}
