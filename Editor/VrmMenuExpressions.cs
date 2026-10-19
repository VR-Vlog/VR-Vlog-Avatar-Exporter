using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    internal static class VrmMenuExpressions
    {
        internal sealed class Expression
        {
            internal string Name;
            internal readonly List<string> Targets = new List<string>();
            // Shapes on the export copies that reproduce a channel by
            // themselves: the final morph target scaled by Weight.
            internal readonly List<(UnityEngine.SkinnedMeshRenderer Renderer, string Shape, float Weight)> Direct =
                new List<(UnityEngine.SkinnedMeshRenderer Renderer, string Shape, float Weight)>();
            internal ExpressionAnimationData Animation = null;
        }

        // Resolves a direct reference through the optimizer route captured
        // before export, so renamed or merged shapes keep their binding.
        internal interface IDirectBindings
        {
            Dictionary<string, object> MenuBinding(Dictionary<string, object> json, UnityEngine.SkinnedMeshRenderer renderer, string shape, float weight);
        }

        internal static int CountRegistered(byte[] bytes)
        {
            var current = GlbDocument.Read(bytes).Json;
            foreach (var key in new[] { "extensions", "VRMC_vrm", "expressions", "custom" })
            {
                if (!current.TryGetValue(key, out var value) || !(value is Dictionary<string, object> next)) return 0;
                current = next;
            }
            return current.Count(p => p.Key.StartsWith("VRChat / ", StringComparison.Ordinal) &&
                p.Value is Dictionary<string, object> expression && expression.TryGetValue("morphTargetBinds", out var binds) &&
                binds is List<object> list && list.Count > 0);
        }

        // Register only actual menu entries. Each one binds its composite
        // targets at full weight and the original shapes it moves linearly at
        // their own weight; raw shapes do not become selectable expressions.
        internal static byte[] Add(byte[] bytes, IList<Expression> expressions, IDirectBindings direct = null, ICollection<string> warnings = null)
        {
            if (expressions.Count == 0) return bytes;
            var glb = GlbDocument.Read(bytes);
            var vrm = (Dictionary<string, object>)((Dictionary<string, object>)glb.Json["extensions"])["VRMC_vrm"];
            var root = Object(vrm, "expressions");
            var custom = Object(root, "custom");
            var nodes = (List<object>)glb.Json["nodes"];
            var meshes = (List<object>)glb.Json["meshes"];
            var animations = new List<ExpressionAnimationData>();
            foreach (var expression in expressions)
            {
                var name = "VRChat / " + expression.Name;
                var suffix = 2;
                while (custom.ContainsKey(name)) name = "VRChat / " + expression.Name + " (" + suffix++ + ")";
                var binds = new List<object>();
                var allTargets = expression.Targets.Concat(expression.Animation == null ? Enumerable.Empty<string>() :
                    expression.Animation.Channels.SelectMany(c => c.Points).Select(p => p.Target).Where(t => t != null));
                foreach (var target in allTargets)
                {
                    var matches = new List<object>();
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
                            matches.Add(new Dictionary<string, object> { ["node"] = (long)nodeIndex, ["index"] = (long)i, ["weight"] = 1.0 });
                        }
                    }
                    if (matches.Count != 1) throw new InvalidOperationException("表情の出力先を一意に特定できません: " + expression.Name);
                    if (expression.Targets.Contains(target)) binds.Add(matches.Single());
                }
                if (expression.Direct.Count > 0 && direct == null)
                    throw new InvalidOperationException("表情の元BlendShapeの出力先を解決できません: " + expression.Name);
                foreach (var (renderer, shape, weight) in expression.Direct)
                {
                    var bind = direct.MenuBinding(glb.Json, renderer, shape, weight);
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
                        continue;
                    }
                    binds.Add(bind);
                }
                if (binds.Count == 0) throw new InvalidOperationException("表情の出力先がありません: " + expression.Name);
                custom.Add(name, new Dictionary<string, object>
                {
                    ["morphTargetBinds"] = binds, ["isBinary"] = true,
                    ["overrideBlink"] = "block", ["overrideMouth"] = "block", ["overrideLookAt"] = "block"
                });
                if (expression.Animation != null)
                {
                    expression.Animation.Expression = name; // Includes any duplicate-name suffix.
                    animations.Add(expression.Animation);
                }
            }
            if (animations.Count > 0)
            {
                var extensions = (Dictionary<string, object>)glb.Json["extensions"];
                if (extensions.ContainsKey(ExpressionAnimationData.Extension))
                    throw new InvalidOperationException("既存の表情アニメーション拡張を上書きできません。");
                extensions.Add(ExpressionAnimationData.Extension, ExpressionAnimationData.Write(animations));
                if (!glb.Json.TryGetValue("extensionsUsed", out var used)) glb.Json["extensionsUsed"] = used = new List<object>();
                ((List<object>)used).Add(ExpressionAnimationData.Extension);
            }
            return glb.Write();
        }

        private static Dictionary<string, object> Object(Dictionary<string, object> parent, string key)
        {
            if (!parent.TryGetValue(key, out var value)) parent[key] = value = new Dictionary<string, object>();
            return value as Dictionary<string, object> ?? throw new InvalidOperationException("Invalid VRM expression object: " + key);
        }
    }
}
