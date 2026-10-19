using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    // Work on UniVRM's final nodes/targetNames so no Unity hierarchy index is
    // mistaken for a glTF node or morph target after mesh export.
    internal static class VrmExpressionBindings
    {
        // emotionCandidates maps each custom expression this export registered
        // (by its final VRM name) to the text emotion inference reads for it,
        // such as its menu path; only those may fill an empty emotion.
        public static byte[] AddMissing(byte[] bytes, ICollection<string> warnings = null, bool inferBlink = true,
            bool inferEmotions = false, ICollection<(string Preset, string Source)> filledEmotions = null,
            IReadOnlyDictionary<string, string> emotionCandidates = null)
        {
            var glb = GlbDocument.Read(bytes);
            var vrm = Object(Object(glb.Json, "extensions"), "VRMC_vrm");
            var nodes = List(glb.Json, "nodes");
            var meshes = List(glb.Json, "meshes");
            if (vrm == null || nodes == null || meshes == null)
                throw new InvalidOperationException("VRMの表情を設定するためのnode・mesh情報がありません。");
            var expressions = Object(vrm, "expressions");
            var presets = Object(expressions, "preset");
            var changed = false;
            if (inferBlink && !BlinkShapeNames.Presets.Any(key => presets != null && presets.ContainsKey(key)))
            {
                var blinkBinds = new[] { new List<object>(), new List<object>(), new List<object>() };
                var completePairs = true;
                var completeRenderers = true;
                for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    var node = nodes[nodeIndex] as Dictionary<string, object>;
                    if (node == null || !node.TryGetValue("mesh", out var rawMesh)) continue;
                    var meshIndex = Index(rawMesh, meshes.Count, "mesh");
                    var mesh = meshes[meshIndex] as Dictionary<string, object>;
                    var names = List(Object(mesh, "extras"), "targetNames");
                    if (names == null) continue;
                    var resolved = BlinkShapeNames.Resolve(names.Select(n => n as string).ToArray());
                    if (resolved[0] == -2)
                    {
                        Warn(warnings, "Blink: 同名の閉眼用シェイプが複数あります。確認・調整で指定してください。");
                        completeRenderers = false;
                        continue;
                    }
                    if (resolved[0] < 0 && resolved[1] == BlinkShapeNames.PartialPair)
                    {
                        Warn(warnings, "Blink: 閉眼用の左右がそろっていないメッシュがあります。確認・調整で指定してください。");
                        completeRenderers = false;
                        continue;
                    }
                    if (resolved[0] >= 0 && resolved[1] < 0) completePairs = false;
                    for (var slot = 0; slot < resolved.Length; slot++)
                    {
                        var target = resolved[slot];
                        if (target < 0) continue;
                        var primitives = List(mesh, "primitives");
                        if (primitives == null || primitives.Count == 0) continue;
                        foreach (var rawPrimitive in primitives)
                        {
                            var targets = List(rawPrimitive as Dictionary<string, object>, "targets");
                            if (targets == null || target >= targets.Count)
                                throw new InvalidOperationException("表情名とmorph targetの対応が不正です。");
                        }
                        var bind = new Dictionary<string, object> {
                            { "node", (long)nodeIndex }, { "index", (long)target }, { "weight", 1.0 }
                        };
                        blinkBinds[slot].Add(bind);
                        if (resolved[0] < 0 && slot > 0) blinkBinds[0].Add(bind);
                    }
                }
                for (var slot = 0; slot < blinkBinds.Length; slot++)
                {
                    if (!completeRenderers || blinkBinds[slot].Count == 0 || slot > 0 && !completePairs) continue;
                    if (expressions == null) vrm["expressions"] = expressions = new Dictionary<string, object>();
                    if (presets == null) expressions["preset"] = presets = new Dictionary<string, object>();
                    presets[BlinkShapeNames.Presets[slot]] = new Dictionary<string, object> {
                        { "morphTargetBinds", blinkBinds[slot] }, { "isBinary", false }
                    };
                    changed = true;
                }
            }
            foreach (var mapping in ExpressionNameInference.MouthShapes)
            {
                // An authored preset, including an intentionally empty one,
                // always wins over inferred raw mesh names.
                if (presets != null && presets.ContainsKey(mapping.Preset)) continue;
                var bindings = new List<object>();
                for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    var node = nodes[nodeIndex] as Dictionary<string, object>;
                    if (node == null || !node.TryGetValue("mesh", out var rawMesh)) continue;
                    var meshIndex = Index(rawMesh, meshes.Count, "mesh");
                    var mesh = meshes[meshIndex] as Dictionary<string, object>;
                    var names = List(Object(mesh, "extras"), "targetNames");
                    if (names == null) continue;
                    var target = ExpressionNameInference.FindExact(names, mapping.Aliases);
                    if (target == -2)
                    {
                        Warn(warnings, $"{mapping.Preset}: mesh {meshIndex} に同名の表情が複数あるため自動設定を省略しました。");
                        continue;
                    }
                    if (target < 0) continue;
                    var primitives = List(mesh, "primitives");
                    if (primitives == null || primitives.Count == 0) continue;
                    var valid = true;
                    foreach (var rawPrimitive in primitives)
                    {
                        var targets = List(rawPrimitive as Dictionary<string, object>, "targets");
                        if (targets == null || target >= targets.Count) { valid = false; break; }
                    }
                    if (!valid) throw new InvalidOperationException("表情名とmorph targetの対応が不正です。");
                    bindings.Add(new Dictionary<string, object> {
                        { "node", (long)nodeIndex }, { "index", (long)target }, { "weight", 1.0 }
                    });
                }
                if (bindings.Count == 0) continue;
                if (expressions == null) vrm["expressions"] = expressions = new Dictionary<string, object>();
                if (presets == null) expressions["preset"] = presets = new Dictionary<string, object>();
                presets[mapping.Preset] = new Dictionary<string, object> {
                    { "morphTargetBinds", bindings }, { "isBinary", false }
                };
                changed = true;
                Warn(warnings, $"{mapping.Preset}: 既知の表情名からVRMの割り当てを追加しました。動作を確認してください。");
            }
            if (inferEmotions && AddEmotions(vrm, nodes, meshes, warnings, filledEmotions, emotionCandidates))
            {
                changed = true;
                presets = Object(Object(vrm, "expressions"), "preset");
            }
            if (inferBlink && (presets == null || (!presets.ContainsKey("blink") &&
                !(presets.ContainsKey("blinkLeft") && presets.ContainsKey("blinkRight")))))
                Warn(warnings, "瞬きのVRM設定を自動生成できませんでした。元アバターにVRMのBlink設定を追加してください。");
            return changed ? glb.Write() : bytes;
        }

        internal const string FilledFormat = "{0}: 名前から自動で設定しました（{1}）。動作を確認してください。";
        internal const string DuplicateNameFormat = "{0}: mesh {1} に同名のBlendShape（{2}）が複数あるため、この名前は自動設定に使いませんでした。";
        private static readonly string[] BindKeys = { "morphTargetBinds", "materialColorBinds", "textureTransformBinds" };
        private static readonly string[] OverrideKeys = { "overrideBlink", "overrideLookAt", "overrideMouth" };

        // Fill only empty emotion presets. Whole VRChat menu faces come first;
        // then a raw BlendShape whose Latin name names one whole emotion.
        // Authored presets that move anything are never changed.
        private static bool AddEmotions(Dictionary<string, object> vrm, List<object> nodes, List<object> meshes,
            ICollection<string> warnings, ICollection<(string Preset, string Source)> filled, IReadOnlyDictionary<string, string> candidates)
        {
            var expressions = Object(vrm, "expressions");
            var presets = Object(expressions, "preset");
            var open = new HashSet<UniVRM10.ExpressionPreset>(ExpressionNameInference.Emotions.Select(entry => entry.Preset)
                .Where(preset => IsEmpty(presets, preset.ToString())));
            if (open.Count == 0) return false;
            var changed = false;
            void Fill(UniVRM10.ExpressionPreset preset, Dictionary<string, object> value, string source)
            {
                if (expressions == null) vrm["expressions"] = expressions = new Dictionary<string, object>();
                if (presets == null) expressions["preset"] = presets = new Dictionary<string, object>();
                presets[preset.ToString()] = value;
                open.Remove(preset);
                changed = true;
                Warn(warnings, string.Format(ExporterLocalization.T(FilledFormat), preset, source));
                filled?.Add((preset.ToString(), source));
            }

            // Menu faces are whole faces the avatar author named, so Japanese
            // words count here. One menu face fills at most one emotion.
            // Authored custom expressions are not candidates.
            var custom = Object(expressions, "custom");
            if (custom != null && candidates != null && candidates.Count > 0)
            {
                var menu = new List<(UniVRM10.ExpressionPreset Preset, int Tier, int Order, string Name, Dictionary<string, object> Value)>();
                var order = 0;
                foreach (var pair in custom)
                {
                    order++;
                    if (!candidates.TryGetValue(pair.Key, out var path) || !(pair.Value is Dictionary<string, object> value) || !HasBinds(value)) continue;
                    // The VRM name is the item label alone; the menu path still
                    // decides, so a "Smile" submenu's item "1" stays happy.
                    var match = ExpressionNameInference.MatchMenuPath(path ?? pair.Key);
                    if (match != null) menu.Add((match.Value.Preset, match.Value.Tier, order, pair.Key, value));
                }
                foreach (var candidate in menu.OrderBy(item => item.Tier).ThenBy(item => item.Order))
                {
                    if (!open.Contains(candidate.Preset)) continue;
                    var copy = new Dictionary<string, object>();
                    foreach (var key in BindKeys.Concat(OverrideKeys))
                        if (candidate.Value.TryGetValue(key, out var value)) copy[key] = DeepCopy(value);
                    copy["isBinary"] = false;
                    Fill(candidate.Preset, copy, candidate.Name);
                }
            }
            if (open.Count == 0) return changed;

            // Japanese BlendShape names commonly follow the MMD set, where "怒り",
            // "びっくり" or "にっこり" move only the brows, eyes or mouth. Only
            // Latin whole-face names (e.g. Fcl_ALL_Joy) are used here.
            var raw = new List<(UniVRM10.ExpressionPreset Preset, int Tier, int Order, string Name)>();
            var rawOrder = 0;
            foreach (var names in MeshTargetNames(nodes, meshes).Select(item => item.Names))
                foreach (var name in names.OfType<string>())
                {
                    rawOrder++;
                    var match = ExpressionNameInference.MatchEmotion(name, latinOnly: true);
                    if (match != null && open.Contains(match.Value.Preset)) raw.Add((match.Value.Preset, match.Value.Tier, rawOrder, name));
                }
            foreach (var group in raw.GroupBy(item => item.Preset))
            {
                var preset = group.Key;
                foreach (var name in group.OrderBy(item => item.Tier).ThenBy(item => item.Order).Select(item => item.Name).Distinct())
                {
                    var bindings = new List<object>();
                    var ambiguous = false;
                    foreach (var (nodeIndex, meshIndex, mesh, names) in MeshTargetNames(nodes, meshes))
                    {
                        var found = new List<int>();
                        for (var i = 0; i < names.Count; i++)
                            if (string.Equals(names[i] as string, name, StringComparison.Ordinal)) found.Add(i);
                        if (found.Count == 0) continue;
                        if (found.Count > 1)
                        {
                            // Binding the other meshes alone would move only part of the face.
                            Warn(warnings, string.Format(ExporterLocalization.T(DuplicateNameFormat), preset, meshIndex, name));
                            ambiguous = true;
                            break;
                        }
                        var target = found[0];
                        var primitives = List(mesh, "primitives");
                        if (primitives == null || primitives.Count == 0) continue;
                        foreach (var rawPrimitive in primitives)
                        {
                            var targets = List(rawPrimitive as Dictionary<string, object>, "targets");
                            if (targets == null || target >= targets.Count)
                                throw new InvalidOperationException("表情名とmorph targetの対応が不正です。");
                        }
                        bindings.Add(new Dictionary<string, object> {
                            { "node", (long)nodeIndex }, { "index", (long)target }, { "weight", 1.0 }
                        });
                    }
                    if (ambiguous || bindings.Count == 0) continue;
                    // A whole-face shape already closes or shapes the eyes, so
                    // auto blink on top of it would push the eyelids twice.
                    Fill(preset, new Dictionary<string, object> {
                        { "morphTargetBinds", bindings }, { "isBinary", false }, { "overrideBlink", "block" }
                    }, name);
                    break;
                }
            }
            return changed;
        }

        private static IEnumerable<(int Node, int Mesh, Dictionary<string, object> Value, List<object> Names)> MeshTargetNames(List<object> nodes, List<object> meshes)
        {
            for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                var node = nodes[nodeIndex] as Dictionary<string, object>;
                if (node == null || !node.TryGetValue("mesh", out var rawMesh)) continue;
                var meshIndex = Index(rawMesh, meshes.Count, "mesh");
                var mesh = meshes[meshIndex] as Dictionary<string, object>;
                var names = List(Object(mesh, "extras"), "targetNames");
                if (names != null) yield return (nodeIndex, meshIndex, mesh, names);
            }
        }

        // An absent preset, or one that moves nothing, may be filled.
        private static bool IsEmpty(Dictionary<string, object> presets, string key) =>
            presets == null || !presets.TryGetValue(key, out var value) || value is Dictionary<string, object> expression && !HasBinds(expression);

        private static bool HasBinds(Dictionary<string, object> expression) =>
            BindKeys.Any(key => expression.TryGetValue(key, out var value) && value is List<object> list && list.Count > 0);

        private static object DeepCopy(object value)
        {
            switch (value)
            {
                case Dictionary<string, object> map: return map.ToDictionary(pair => pair.Key, pair => DeepCopy(pair.Value));
                case List<object> list: return list.Select(DeepCopy).ToList();
                default: return value;
            }
        }

        private static int Index(object value, int count, string label)
        {
            if (!(value is long index) || index < 0 || index >= count)
                throw new InvalidOperationException($"Invalid {label} index.");
            return (int)index;
        }
        private static Dictionary<string, object> Object(Dictionary<string, object> parent, string key) =>
            parent != null && parent.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;
        private static List<object> List(Dictionary<string, object> parent, string key) =>
            parent != null && parent.TryGetValue(key, out var value) ? value as List<object> : null;
        private static void Warn(ICollection<string> warnings, string message)
        {
            if (warnings != null && !warnings.Contains(message)) warnings.Add(message);
        }
    }
}
