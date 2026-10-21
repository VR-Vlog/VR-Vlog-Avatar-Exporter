using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    // Work on UniVRM's final nodes/targetNames so no Unity hierarchy index is
    // mistaken for a glTF node or morph target after mesh export.
    internal static class VrmExpressionBindings
    {
        internal const string UnboundVisemeWarning = "口の形（aa・ih・ou・ee・oh）のVRM設定を一部作れませんでした。Avatar DescriptorのLip Syncで指定した変形が書き出したメッシュにありません。";
        internal const string UnexportedVisemeMeshWarning = "口の形（aa・ih・ou・ee・oh）のVRM設定を一部作れませんでした。Avatar DescriptorのLip SyncのFace Meshが書き出したアバターに含まれていません。";

        // VRChat Lip Sync (VisemeBlendShape) read from the processed copy. It
        // names the mouth shapes whatever they are called, so it comes before
        // the alias guess. Node is the output node of VisemeSkinnedMesh.
        internal sealed class DescriptorVisemes
        {
            // VisemeBlendShapes order: sil PP FF TH DD kk CH SS nn RR aa E I O U.
            internal static readonly (int Slot, string Preset)[] Slots = {
                (10, "aa"), (11, "ee"), (12, "ih"), (13, "oh"), (14, "ou"),
            };
            internal int Node = -1;
            // Only slots that named a shape. A null name could not reach the output.
            internal readonly Dictionary<string, string> Shapes = new Dictionary<string, string>();
        }

        // emotionCandidates maps each custom expression this export registered
        // (by its final VRM name) to the text emotion inference reads for it,
        // such as its menu path; only those may fill an empty emotion.
        // emotionShapes lists the original BlendShapes each candidate moves;
        // with shapeNames, a candidate whose label says nothing can still fill
        // an emotion its face clearly shows. Each filled emotion is added to
        // filledEmotions with the message format that explains it.
        // descriptor is the VRChat descriptor's Lip Sync; it fills an empty
        // mouth preset before the known names do.
        public static byte[] AddMissing(byte[] bytes, ICollection<string> warnings = null, bool inferBlink = true,
            bool inferEmotions = false, ICollection<(string Preset, string Source, string Format)> filledEmotions = null,
            IReadOnlyDictionary<string, string> emotionCandidates = null,
            IReadOnlyDictionary<string, IReadOnlyList<(string Shape, float Weight)>> emotionShapes = null, ExpressionShapeNames shapeNames = null,
            DescriptorVisemes descriptor = null)
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
                // A Lip Sync shape the Face Mesh does not have falls back to
                // the known names below.
                if (descriptor != null && descriptor.Shapes.TryGetValue(mapping.Preset, out var shape))
                {
                    var descriptorTarget = DescriptorTarget(nodes, meshes, descriptor.Node, shape);
                    if (descriptorTarget >= 0)
                    {
                        bindings.Add(new Dictionary<string, object> {
                            { "node", (long)descriptor.Node }, { "index", (long)descriptorTarget }, { "weight", 1.0 }
                        });
                        if (expressions == null) vrm["expressions"] = expressions = new Dictionary<string, object>();
                        if (presets == null) expressions["preset"] = presets = new Dictionary<string, object>();
                        presets[mapping.Preset] = new Dictionary<string, object> {
                            { "morphTargetBinds", bindings }, { "isBinary", false }
                        };
                        changed = true;
                        continue;
                    }
                }
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
                        Warn(warnings, string.Format(ExporterLocalization.T(DuplicateExpressionFormat), mapping.Preset, meshIndex));
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
                Warn(warnings, string.Format(ExporterLocalization.T(KnownNameFormat), mapping.Preset));
            }
            // Only slots that named a shape count. Empty slots stay silent as
            // before, and a known alias that bound the preset is enough.
            if (descriptor != null && descriptor.Shapes.Keys.Any(preset => presets == null || !presets.ContainsKey(preset)))
                Warn(warnings, ExporterLocalization.T(descriptor.Node < 0 ? UnexportedVisemeMeshWarning : UnboundVisemeWarning));
            if (inferEmotions && AddEmotions(vrm, nodes, meshes, warnings, filledEmotions, emotionCandidates, emotionShapes, shapeNames))
            {
                changed = true;
                presets = Object(Object(vrm, "expressions"), "preset");
            }
            if (inferBlink && (presets == null || (!presets.ContainsKey("blink") &&
                !(presets.ContainsKey("blinkLeft") && presets.ContainsKey("blinkRight")))))
                Warn(warnings, ExporterLocalization.T(BlinkNotGeneratedWarning));
            return changed ? glb.Write() : bytes;
        }

        internal const string DuplicateExpressionFormat = "{0}: mesh {1} に同名の表情が複数あるため自動設定を省略しました。";
        internal const string BlinkNotGeneratedWarning = "瞬きのVRM設定を自動生成できませんでした。元アバターにVRMのBlink設定を追加してください。";
        internal const string KnownNameFormat = "{0}: 既知の表情名からVRMの割り当てを追加しました。動作を確認してください。";
        internal const string FilledFormat = "{0}: 名前から自動で設定しました（{1}）。書き出したVRMで表情の動きを確認してください。";
        internal const string ShapeFilledFormat = "{0}: 表情が動かすBlendShapeから自動で設定しました（{1}）。書き出したVRMで表情の動きを確認してください。";
        internal const string DuplicateNameFormat = "{0}: mesh {1} に同名のBlendShape（{2}）が複数あるため、この名前は自動設定に使いませんでした。";
        private static readonly string[] BindKeys = { "morphTargetBinds", "materialColorBinds", "textureTransformBinds" };
        private static readonly string[] OverrideKeys = { "overrideBlink", "overrideLookAt", "overrideMouth" };

        // Fill only empty emotion presets. Whole VRChat menu faces come first;
        // then a raw BlendShape whose Latin name names one whole emotion; last,
        // a menu face whose label says nothing, by the shapes it moves.
        // Authored presets that move anything are never changed.
        private static bool AddEmotions(Dictionary<string, object> vrm, List<object> nodes, List<object> meshes,
            ICollection<string> warnings, ICollection<(string Preset, string Source, string Format)> filled, IReadOnlyDictionary<string, string> candidates,
            IReadOnlyDictionary<string, IReadOnlyList<(string Shape, float Weight)>> shapes, ExpressionShapeNames shapeNames)
        {
            var expressions = Object(vrm, "expressions");
            var presets = Object(expressions, "preset");
            var open = new HashSet<UniVRM10.ExpressionPreset>(ExpressionNameInference.Emotions.Select(entry => entry.Preset)
                .Where(preset => IsEmpty(presets, preset.ToString())));
            if (open.Count == 0) return false;
            var changed = false;
            void Fill(UniVRM10.ExpressionPreset preset, Dictionary<string, object> value, string source, string format = FilledFormat)
            {
                if (expressions == null) vrm["expressions"] = expressions = new Dictionary<string, object>();
                if (presets == null) expressions["preset"] = presets = new Dictionary<string, object>();
                presets[preset.ToString()] = value;
                open.Remove(preset);
                changed = true;
                Warn(warnings, string.Format(ExporterLocalization.T(format), preset, source));
                filled?.Add((preset.ToString(), source, format));
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
                    if (open.Contains(candidate.Preset)) Fill(candidate.Preset, CopyFace(candidate.Value), candidate.Name);
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
            if (open.Count == 0 || custom == null || candidates == null || shapes == null || shapeNames == null || shapeNames.Count == 0) return changed;

            // Menu faces labeled "1" or "Face 3" say nothing by name. The
            // shapes they move name the face parts; only a face whose parts
            // agree on one emotion fills it, best supported first. A label
            // that names an emotion, a part or a side is never overruled.
            var byShape = new List<(UniVRM10.ExpressionPreset Preset, int Support, int Order, string Name, Dictionary<string, object> Value)>();
            var shapeOrder = 0;
            foreach (var pair in custom)
            {
                shapeOrder++;
                if (!candidates.TryGetValue(pair.Key, out var path) || !(pair.Value is Dictionary<string, object> value) || !HasBinds(value)) continue;
                if (!shapes.TryGetValue(pair.Key, out var moved) || !ExpressionNameInference.SaysNothing(path ?? pair.Key)) continue;
                var guess = shapeNames.Classify(moved);
                if (guess != null && open.Contains(guess.Value.Preset)) byShape.Add((guess.Value.Preset, guess.Value.Support, shapeOrder, pair.Key, value));
            }
            foreach (var candidate in byShape.OrderByDescending(item => item.Support).ThenBy(item => item.Order))
                if (open.Contains(candidate.Preset)) Fill(candidate.Preset, CopyFace(candidate.Value), candidate.Name, ShapeFilledFormat);
            return changed;
        }

        private static Dictionary<string, object> CopyFace(Dictionary<string, object> face)
        {
            var copy = new Dictionary<string, object>();
            foreach (var key in BindKeys.Concat(OverrideKeys))
                if (face.TryGetValue(key, out var value)) copy[key] = DeepCopy(value);
            copy["isBinary"] = false;
            return copy;
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

        // The descriptor names one exact shape on one renderer. -1 when that
        // renderer or shape did not reach the output.
        private static int DescriptorTarget(List<object> nodes, List<object> meshes, int nodeIndex, string shape)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.Count || string.IsNullOrEmpty(shape)) return -1;
            var node = nodes[nodeIndex] as Dictionary<string, object>;
            if (node == null || !node.TryGetValue("mesh", out var rawMesh)) return -1;
            var mesh = meshes[Index(rawMesh, meshes.Count, "mesh")] as Dictionary<string, object>;
            var names = List(Object(mesh, "extras"), "targetNames");
            var primitives = List(mesh, "primitives");
            if (names == null || primitives == null || primitives.Count == 0) return -1;
            var target = BlinkShapeNames.Unique(names.Select(n => n as string).ToArray(), shape, StringComparison.Ordinal);
            if (target < 0) return -1;
            foreach (var rawPrimitive in primitives)
            {
                var targets = List(rawPrimitive as Dictionary<string, object>, "targets");
                if (targets == null || target >= targets.Count)
                    throw new InvalidOperationException("表情名とmorph targetの対応が不正です。");
            }
            return target;
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
