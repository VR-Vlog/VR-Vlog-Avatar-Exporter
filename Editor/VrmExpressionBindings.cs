using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    // Work on UniVRM's final nodes/targetNames so no Unity hierarchy index is
    // mistaken for a glTF node or morph target after mesh export.
    internal static class VrmExpressionBindings
    {
        private static readonly (string Preset, string[] Aliases)[] Mappings = {
            ("aa", new[] { "vrc.v.aa", "vrc.v_aa", "mouth_a", "Aa", "A", "Fcl_MTH_A" }),
            ("ih", new[] { "vrc.v.ih", "vrc.v_ih", "mouth_i", "Ih", "I", "Fcl_MTH_I" }),
            ("ou", new[] { "vrc.v.ou", "vrc.v_ou", "vrc.v.u", "vrc.v_u", "mouth_u", "Ou", "U", "Fcl_MTH_U" }),
            ("ee", new[] { "vrc.v.e", "vrc.v_e", "mouth_e", "Ee", "E", "Fcl_MTH_E" }),
            ("oh", new[] { "vrc.v.oh", "vrc.v_oh", "mouth_o", "Oh", "O", "Fcl_MTH_O" }),
        };

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

        public static byte[] AddMissing(byte[] bytes, ICollection<string> warnings = null, bool inferBlink = true,
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
            foreach (var mapping in Mappings)
            {
                // An authored preset, including an intentionally empty one,
                // always wins over inferred raw mesh names.
                if (presets != null && presets.ContainsKey(mapping.Preset)) continue;
                var bindings = new List<object>();
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
                    var target = FindTarget(names, mapping.Aliases);
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
            // Only slots that named a shape count. Empty slots stay silent as
            // before, and a known alias that bound the preset is enough.
            if (descriptor != null && descriptor.Shapes.Keys.Any(preset => presets == null || !presets.ContainsKey(preset)))
                Warn(warnings, descriptor.Node < 0 ? UnexportedVisemeMeshWarning : UnboundVisemeWarning);
            if (inferBlink && (presets == null || (!presets.ContainsKey("blink") &&
                !(presets.ContainsKey("blinkLeft") && presets.ContainsKey("blinkRight")))))
                Warn(warnings, "瞬きのVRM設定を自動生成できませんでした。元アバターにVRMのBlink設定を追加してください。");
            return changed ? glb.Write() : bytes;
        }

        // Alias order expresses a documented preference (e.g. authored VRChat
        // visemes before a generic mouth shape). Never use substring matching.
        private static int FindTarget(List<object> names, string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var found = -1;
                for (var i = 0; i < names.Count; i++)
                    if (names[i] is string name && string.Equals(name, alias, StringComparison.OrdinalIgnoreCase))
                    {
                        if (found >= 0) return -2;
                        found = i;
                    }
                if (found >= 0) return found;
            }
            return -1;
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
