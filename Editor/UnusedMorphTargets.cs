using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.Expressions;
using VRVlog.FaceTracking;

namespace VRVlog.LilToonExporter
{
    // Leaves out the morph targets (BlendShapes) of the finished VRM that the
    // app can never move: no VRM expression or expression animation binds
    // them, the app does not drive their name, and their default weight is 0.
    // Their accessors and bufferViews go with them when nothing else uses
    // them. A file this class cannot fully account for is returned unchanged.
    // Kept free of Unity types so the host tests compile it.
    internal static class UnusedMorphTargets
    {
        // False writes every morph target as before.
        internal static readonly bool Enabled = true;

        // Logged to the Console only: the file is kept unchanged.
        internal const string UnknownExtensionWarning = "Kept every BlendShape because the file has extensions this step has not checked: {0}";
        internal const string StorageWarning = "Kept every BlendShape because the file's binary data is not one GLB chunk this step can pack again";
        internal const string AnimationWarning = "Kept every BlendShape because the expression animations could not be read: {0}";

        // Extensions checked to hold no accessor, bufferView or morph target
        // index other than the ones handled here. Any other extension keeps
        // the file unchanged. VRVLOG_materials_liltoon, the pose and the
        // hand gesture extensions and VRVLOG_object_toggles are added after
        // this stage.
        internal static readonly string[] KnownExtensions =
        {
            "VRMC_vrm", "VRMC_springBone", "VRMC_springBone_extended_collider", "VRMC_springBone_limit",
            "VRMC_node_constraint", "VRMC_materials_mtoon", "KHR_materials_unlit", "KHR_texture_transform",
            "KHR_materials_emissive_strength", ExpressionAnimationData.Extension,
        };

        // Names the app drives without an expression: its fixed mapping
        // (SettingsStore.EnsureMapping), the blink restore names and
        // MediaPipe's "_neutral". Matched ignoring case on the whole name or
        // on the part after the last '.' or '/'.
        static readonly HashSet<string> LegacyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "A", "O", "U", "Joy", "Blink_L", "Blink_R", "Surprised", "Angry", "Sorrow",
            "Blink", "BlinkLeft", "BlinkRight", "EyeBlinkLeft", "EyeBlinkRight", "_neutral",
        };

        // Whether the app moves a morph target of this name by itself: the
        // names above, Perfect Sync (not under "UE/") and Unified Expressions.
        // Names under "VRChat /" are menu expressions, never tracking.
        internal static bool AppDrives(string name)
        {
            if (name == null) return false;
            var value = name.Trim();
            if (value.Length == 0) return false;
            if (LegacyNames.Contains(value) || LegacyNames.Contains(value.Substring(value.LastIndexOf('.') + 1)) ||
                LegacyNames.Contains(value.Substring(value.LastIndexOf('/') + 1))) return true;
            var menu = value.IndexOf('/');
            if (menu >= 0 && string.Equals(value.Substring(0, menu).Trim(), "VRChat", StringComparison.OrdinalIgnoreCase)) return false;
            if (!value.StartsWith("ue/", StringComparison.OrdinalIgnoreCase) && TrackingExpressionNames.IsPerfectSync(value)) return true;
            return UnifiedExpressionRegistry.TryCanonicalize(value, out _);
        }

        sealed class MeshPlan
        {
            internal int Count;
            internal bool Skip;
            internal List<object> Names;
            internal readonly HashSet<int> Bound = new HashSet<int>();
            internal int[] Map;
        }

        // Returns the input array itself when nothing is removed. warnings
        // receives why the file was kept unchanged; removed receives
        // "mesh/shape" (or the shape alone for an unnamed mesh) for each
        // target left out.
        internal static byte[] Strip(byte[] bytes, ICollection<string> warnings, ICollection<string> removed = null)
        {
            if (!Enabled || bytes == null) return bytes;
            var glb = GlbDocument.Read(bytes);
            var json = glb.Json;
            var unknown = new SortedSet<string>(StringComparer.Ordinal);
            CollectUnknownExtensions(json, unknown);
            if (unknown.Count > 0)
            {
                warnings?.Add(string.Format(UnknownExtensionWarning, string.Join(", ", unknown)));
                return bytes;
            }
            var meshes = List(json, "meshes");
            if (meshes == null || meshes.Count == 0) return bytes;
            if (!StorageIsPlain(glb))
            {
                warnings?.Add(StorageWarning);
                return bytes;
            }
            var nodes = List(json, "nodes") ?? new List<object>();
            var accessors = List(json, "accessors") ?? new List<object>();

            var plans = new MeshPlan[meshes.Count];
            for (var m = 0; m < meshes.Count; m++) plans[m] = Plan(meshes[m] as Dictionary<string, object>, accessors.Count);
            foreach (var raw in nodes)
            {
                // A node may override the mesh's default weights.
                if (!(raw is Dictionary<string, object> node) || !node.ContainsKey("weights")) continue;
                var mesh = MeshOf(node, meshes.Count);
                if (mesh < 0) continue;
                if (!(node["weights"] is List<object> weights) || weights.Count != plans[mesh].Count || !weights.All(IsNumber)) plans[mesh].Skip = true;
            }
            // A glTF animation of the weights writes one value per target.
            foreach (var animation in Objects(List(json, "animations")))
                foreach (var channel in Objects(List(animation, "channels")))
                {
                    var target = Object(channel, "target");
                    if (target == null || !(target.TryGetValue("path", out var path) && path as string == "weights")) continue;
                    var mesh = target.TryGetValue("node", out var rawNode) && Index(rawNode) is int n && n < nodes.Count ? MeshOf(nodes[n] as Dictionary<string, object>, meshes.Count) : -1;
                    if (mesh >= 0) plans[mesh].Skip = true;
                }

            // Every {node, index} that binds a morph target: VRM expressions and
            // the expression animations (schema 2 points).
            var binds = new List<Dictionary<string, object>>();
            var expressions = Object(Object(Object(json, "extensions"), "VRMC_vrm"), "expressions");
            foreach (var group in new[] { "preset", "custom" })
                foreach (var expression in (Object(expressions, group) ?? new Dictionary<string, object>()).Values.OfType<Dictionary<string, object>>())
                    binds.AddRange(Objects(List(expression, "morphTargetBinds")));
            var animationNames = new HashSet<string>(StringComparer.Ordinal);
            if (Object(json, "extensions") is Dictionary<string, object> rootExtensions &&
                rootExtensions.TryGetValue(ExpressionAnimationData.Extension, out var rawAnimations))
            {
                ExpressionAnimationDocument document;
                try { document = ExpressionAnimationData.ReadDocument(rawAnimations); }
                catch (Exception exception)
                {
                    warnings?.Add(string.Format(AnimationWarning, exception.Message));
                    return bytes;
                }
                // Schema 1 points name their morph target instead.
                foreach (var point in document.Animations.SelectMany(a => a.Channels).SelectMany(c => c.Points))
                    if (point.Target != null) animationNames.Add(point.Target);
                CollectBinds(rawAnimations, binds);
            }
            foreach (var bind in binds)
            {
                var mesh = bind.TryGetValue("node", out var rawNode) && Index(rawNode) is int n && n < nodes.Count ? MeshOf(nodes[n] as Dictionary<string, object>, meshes.Count) : -1;
                if (mesh < 0) continue;
                // A bind past the targets cannot be renumbered.
                if (Index(bind.TryGetValue("index", out var rawIndex) ? rawIndex : null) is int index && index < plans[mesh].Count) plans[mesh].Bound.Add(index);
                else plans[mesh].Skip = true;
            }

            var removedAccessors = new HashSet<int>();
            var any = false;
            for (var m = 0; m < meshes.Count; m++)
            {
                var plan = plans[m];
                if (plan.Skip || plan.Count == 0) continue;
                var mesh = (Dictionary<string, object>)meshes[m];
                var weights = List(mesh, "weights");
                var nodeWeights = nodes.OfType<Dictionary<string, object>>().Where(node => MeshOf(node, meshes.Count) == m)
                    .Select(node => List(node, "weights")).Where(list => list != null).ToList();
                var drop = new List<int>();
                for (var t = 0; t < plan.Count; t++)
                {
                    var name = plan.Names != null && t < plan.Names.Count ? plan.Names[t] as string : null;
                    var keep = plan.Bound.Contains(t) || string.IsNullOrEmpty(name) || animationNames.Contains(name) || AppDrives(name) ||
                               weights != null && Convert.ToDouble(weights[t]) != 0 || nodeWeights.Any(list => Convert.ToDouble(list[t]) != 0);
                    if (!keep) drop.Add(t);
                }
                if (drop.Count == 0) continue;
                any = true;
                plan.Map = new int[plan.Count];
                for (int t = 0, next = 0; t < plan.Count; t++) plan.Map[t] = drop.Contains(t) ? -1 : next++;
                var prefix = mesh.TryGetValue("name", out var rawName) && rawName is string text && text.Length > 0 ? text + "/" : "";
                foreach (var t in drop) removed?.Add(prefix + (string)plan.Names[t]);
                foreach (var primitive in Objects(List(mesh, "primitives")))
                {
                    var targets = List(primitive, "targets");
                    foreach (var t in drop)
                        foreach (var value in ((Dictionary<string, object>)targets[t]).Values) removedAccessors.Add((int)Index(value));
                    Filter(primitive, "targets", plan.Map);
                    if (Object(primitive, "extras") is Dictionary<string, object> extras) Filter(extras, "targetNames", plan.Map, primitive, "extras");
                }
                Filter(mesh, "weights", plan.Map);
                if (Object(mesh, "extras") is Dictionary<string, object> meshExtras) Filter(meshExtras, "targetNames", plan.Map, mesh, "extras");
                foreach (var node in nodes.OfType<Dictionary<string, object>>().Where(node => MeshOf(node, meshes.Count) == m))
                    if (node.ContainsKey("weights")) Filter(node, "weights", plan.Map);
            }
            if (!any) return bytes;
            foreach (var bind in binds)
            {
                var mesh = bind.TryGetValue("node", out var rawNode) && Index(rawNode) is int n && n < nodes.Count ? MeshOf(nodes[n] as Dictionary<string, object>, meshes.Count) : -1;
                if (mesh >= 0 && plans[mesh].Map != null) bind["index"] = (long)plans[mesh].Map[(int)Index(bind["index"])];
            }

            RemoveAccessors(glb, removedAccessors);
            return glb.Write();
        }

        // Target counts, names and weights of one mesh, or Skip when they do
        // not line up.
        static MeshPlan Plan(Dictionary<string, object> mesh, int accessorCount)
        {
            var plan = new MeshPlan();
            var primitives = Objects(List(mesh, "primitives")).ToList();
            if (mesh == null || primitives.Count == 0 || primitives.Count != List(mesh, "primitives").Count) { plan.Skip = true; return plan; }
            var counts = primitives.Select(p => p.ContainsKey("targets") ? (p["targets"] as List<object>)?.Count ?? -1 : 0).Distinct().ToList();
            if (counts.Count != 1 || counts[0] < 0) { plan.Skip = true; return plan; }
            plan.Count = counts[0];
            if (plan.Count == 0) return plan;
            foreach (var primitive in primitives)
                foreach (var target in List(primitive, "targets"))
                    if (!(target is Dictionary<string, object> attributes) || attributes.Values.Any(value => !(Index(value) is int a) || a >= accessorCount))
                    { plan.Skip = true; return plan; }
            if (mesh.ContainsKey("weights") && (!(mesh["weights"] is List<object> weights) || weights.Count != plan.Count || !weights.All(IsNumber)))
            { plan.Skip = true; return plan; }
            // UniGLTF reads the mesh's names first, then the first primitive's.
            var lists = new[] { Object(mesh, "extras") }.Concat(primitives.Select(p => Object(p, "extras"))).Where(extras => extras != null && extras.ContainsKey("targetNames"));
            foreach (var extras in lists)
                if (!(extras["targetNames"] is List<object> names) || names.Count > plan.Count) { plan.Skip = true; return plan; }
            plan.Names = List(Object(mesh, "extras"), "targetNames") ?? List(Object(primitives[0], "extras"), "targetNames");
            return plan;
        }

        // Keeps the items whose map entry is not -1. An emptied list is
        // removed, and so is the owner's emptied extras.
        static void Filter(Dictionary<string, object> owner, string key, int[] map, Dictionary<string, object> parent = null, string parentKey = null)
        {
            if (!(owner.TryGetValue(key, out var raw) && raw is List<object> list)) return;
            var kept = list.Where((item, index) => index >= map.Length || map[index] >= 0).ToList();
            if (kept.Count > 0) { owner[key] = kept; return; }
            owner.Remove(key);
            if (parent != null && owner.Count == 0) parent.Remove(parentKey);
        }

        // Removes the accessors that only the removed targets used, then the
        // bufferViews that only those accessors used, renumbers what follows
        // and packs the binary chunk again. extraViews adds bufferViews to remove
        // when nothing uses them (VertexDataLightweight's replaced weights).
        internal static void RemoveAccessors(GlbDocument glb, HashSet<int> candidates, IEnumerable<int> extraViews = null)
        {
            var json = glb.Json;
            var accessors = List(json, "accessors");
            var used = new HashSet<int>();
            ForEachAccessorReference(json, (owner, key) => { used.Add((int)Index(owner[key])); });
            var gone = candidates.Where(index => !used.Contains(index)).ToList();
            var candidateViews = new HashSet<int>(extraViews ?? Enumerable.Empty<int>());
            if (gone.Count == 0 && candidateViews.Count == 0) return;
            foreach (var index in gone) foreach (var view in AccessorViews(accessors[index] as Dictionary<string, object>)) candidateViews.Add(view);
            var accessorMap = Renumber(accessors.Count, gone);
            json["accessors"] = accessors.Where((item, index) => accessorMap[index] >= 0).ToList();
            ForEachAccessorReference(json, (owner, key) => owner[key] = (long)accessorMap[(int)Index(owner[key])]);

            var views = List(json, "bufferViews");
            var usedViews = new HashSet<int>();
            ForEachViewReference(json, (owner, key) => usedViews.Add((int)Index(owner[key])));
            var goneViews = candidateViews.Where(index => !usedViews.Contains(index)).ToList();
            if (goneViews.Count == 0) return;
            var viewMap = Renumber(views.Count, goneViews);
            var keptViews = views.Where((item, index) => viewMap[index] >= 0).Cast<Dictionary<string, object>>().ToList();
            json["bufferViews"] = keptViews.Cast<object>().ToList();
            ForEachViewReference(json, (owner, key) => owner[key] = (long)viewMap[(int)Index(owner[key])]);
            glb.ReplaceBinary(Pack(glb.Binary, keptViews));
        }

        // Copies the byte ranges the remaining views cover, in order. A range
        // keeps its offset modulo 4 so every accessor stays aligned, and views
        // that shared or overlapped bytes still do.
        static byte[] Pack(byte[] binary, List<Dictionary<string, object>> views)
        {
            var ranges = views.Select(view => (Start: Offset(view), End: Offset(view) + (long)Index(view["byteLength"])))
                .Where(range => range.End > range.Start).OrderBy(range => range.Start).ToList();
            var merged = new List<(long Start, long End)>();
            foreach (var range in ranges)
            {
                if (merged.Count > 0 && range.Start <= merged[merged.Count - 1].End)
                    merged[merged.Count - 1] = (merged[merged.Count - 1].Start, Math.Max(merged[merged.Count - 1].End, range.End));
                else merged.Add(range);
            }
            var moved = new List<(long Start, long End, long To)>();
            long length = 0;
            foreach (var range in merged)
            {
                var to = ((length + 3) & ~3L) + (range.Start & 3);
                moved.Add((range.Start, range.End, to));
                length = to + range.End - range.Start;
            }
            var result = new byte[checked((int)((length + 3) & ~3L))];
            foreach (var range in moved) Buffer.BlockCopy(binary, (int)range.Start, result, (int)range.To, (int)(range.End - range.Start));
            foreach (var view in views)
            {
                var start = Offset(view);
                var range = moved.FirstOrDefault(r => r.Start <= start && start < r.End);
                if (range.End == 0) continue; // Empty view: nothing to move.
                var offset = range.To + start - range.Start;
                if (offset != 0 || view.ContainsKey("byteOffset")) view["byteOffset"] = offset;
            }
            return result;
        }

        // The binary chunk is the only buffer and every view lies inside it,
        // so the views can be moved without touching anything else.
        internal static bool StorageIsPlain(GlbDocument glb)
        {
            if (!(List(glb.Json, "buffers") is List<object> buffers) || buffers.Count != 1 || !(buffers[0] is Dictionary<string, object> buffer) || buffer.ContainsKey("uri"))
                return false;
            foreach (var raw in List(glb.Json, "bufferViews") ?? new List<object>())
            {
                if (!(raw is Dictionary<string, object> view) || view.ContainsKey("extensions")) return false;
                if (!(view.TryGetValue("buffer", out var b) && Index(b) == 0) || !(view.TryGetValue("byteLength", out var l) && Index(l) is int length)) return false;
                if (view.ContainsKey("byteOffset") && !(Index(view["byteOffset"]) is int)) return false;
                if (Offset(view) + length > glb.Binary.Length) return false;
            }
            var accessorCount = List(glb.Json, "accessors")?.Count ?? 0;
            var viewCount = List(glb.Json, "bufferViews")?.Count ?? 0;
            var valid = true;
            ForEachAccessorReference(glb.Json, (owner, key) => valid &= Index(owner[key]) is int index && index < accessorCount);
            ForEachViewReference(glb.Json, (owner, key) => valid &= Index(owner[key]) is int index && index < viewCount);
            return valid;
        }

        internal static void ForEachAccessorReference(Dictionary<string, object> json, Action<Dictionary<string, object>, string> visit)
        {
            foreach (var mesh in Objects(List(json, "meshes")))
                foreach (var primitive in Objects(List(mesh, "primitives")))
                {
                    if (Object(primitive, "attributes") is Dictionary<string, object> attributes)
                        foreach (var key in attributes.Keys.ToList()) visit(attributes, key);
                    if (primitive.ContainsKey("indices")) visit(primitive, "indices");
                    foreach (var target in Objects(List(primitive, "targets")))
                        foreach (var key in target.Keys.ToList()) visit(target, key);
                }
            foreach (var skin in Objects(List(json, "skins")))
                if (skin.ContainsKey("inverseBindMatrices")) visit(skin, "inverseBindMatrices");
            foreach (var animation in Objects(List(json, "animations")))
                foreach (var sampler in Objects(List(animation, "samplers")))
                {
                    if (sampler.ContainsKey("input")) visit(sampler, "input");
                    if (sampler.ContainsKey("output")) visit(sampler, "output");
                }
        }

        // Every "bufferView" key outside the bufferViews list: accessors,
        // sparse indices and values, and images.
        internal static void ForEachViewReference(object value, Action<Dictionary<string, object>, string> visit)
        {
            if (value is List<object> list) foreach (var item in list) ForEachViewReference(item, visit);
            else if (value is Dictionary<string, object> obj)
                foreach (var key in obj.Keys.ToList())
                {
                    if (key == "bufferView") visit(obj, key);
                    else if (key != "bufferViews") ForEachViewReference(obj[key], visit);
                }
        }

        static IEnumerable<int> AccessorViews(Dictionary<string, object> accessor)
        {
            if (accessor == null) yield break;
            if (accessor.TryGetValue("bufferView", out var view) && Index(view) is int index) yield return index;
            var sparse = Object(accessor, "sparse");
            foreach (var part in new[] { Object(sparse, "indices"), Object(sparse, "values") })
                if (part != null && part.TryGetValue("bufferView", out var raw) && Index(raw) is int sparseIndex) yield return sparseIndex;
        }

        internal static void CollectUnknownExtensions(object value, ISet<string> unknown, IReadOnlyCollection<string> known = null)
        {
            known = known ?? KnownExtensions;
            if (value is List<object> list) { foreach (var item in list) CollectUnknownExtensions(item, unknown, known); return; }
            if (!(value is Dictionary<string, object> obj)) return;
            foreach (var pair in obj)
            {
                if (pair.Key == "extensions" && pair.Value is Dictionary<string, object> extensions)
                    foreach (var name in extensions.Keys) if (!known.Contains(name)) unknown.Add(name);
                if ((pair.Key == "extensionsUsed" || pair.Key == "extensionsRequired") && pair.Value is List<object> names)
                    foreach (var name in names) if (!(name is string text) || !known.Contains(text)) unknown.Add(name as string ?? "?");
                CollectUnknownExtensions(pair.Value, unknown, known);
            }
        }

        static void CollectBinds(object value, List<Dictionary<string, object>> binds)
        {
            if (value is List<object> list) foreach (var item in list) CollectBinds(item, binds);
            else if (value is Dictionary<string, object> obj)
            {
                if (obj.ContainsKey("node") && obj.ContainsKey("index")) binds.Add(obj);
                foreach (var item in obj.Values) CollectBinds(item, binds);
            }
        }

        static int[] Renumber(int count, IEnumerable<int> removed)
        {
            var gone = new HashSet<int>(removed);
            var map = new int[count];
            for (int i = 0, next = 0; i < count; i++) map[i] = gone.Contains(i) ? -1 : next++;
            return map;
        }

        static int MeshOf(Dictionary<string, object> node, int meshCount) =>
            node != null && node.TryGetValue("mesh", out var raw) && Index(raw) is int mesh && mesh < meshCount ? mesh : -1;

        static long Offset(Dictionary<string, object> view) =>
            view.TryGetValue("byteOffset", out var raw) && Index(raw) is int offset ? offset : 0;

        static int? Index(object value)
        {
            if (value is long l && l >= 0 && l <= int.MaxValue) return (int)l;
            if (value is int i && i >= 0) return i;
            if (value is double d && d >= 0 && d <= int.MaxValue && d == Math.Floor(d)) return (int)d;
            return null;
        }

        static bool IsNumber(object value) => value is long || value is int || value is double d && !double.IsNaN(d) && !double.IsInfinity(d);

        static Dictionary<string, object> Object(Dictionary<string, object> owner, string key) =>
            owner != null && owner.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;

        static List<object> List(Dictionary<string, object> owner, string key) =>
            owner != null && owner.TryGetValue(key, out var value) ? value as List<object> : null;

        static IEnumerable<Dictionary<string, object>> Objects(List<object> list) =>
            list?.OfType<Dictionary<string, object>>() ?? Enumerable.Empty<Dictionary<string, object>>();
    }
}
