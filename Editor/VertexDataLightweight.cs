using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    // 自動で軽量化する, on the finished VRM: leaves out the vertex colours
    // (COLOR_0) of a mesh none of whose materials reads them, and writes float
    // bone weights (WEIGHTS_0) as normalized unsigned shorts. What the app
    // draws does not change. The accessors and bufferViews nothing uses any
    // more go with UnusedMorphTargets.RemoveAccessors. A file this class
    // cannot fully account for is returned unchanged.
    // Kept free of Unity types like UnusedMorphTargets.
    internal static class VertexDataLightweight
    {
        // Logged to the Console only: the file is kept unchanged.
        internal const string UnknownExtensionWarning = "Kept the vertex data because the file has extensions this step has not checked: {0}";
        internal const string StorageWarning = "Kept the vertex data because the file's binary data is not one GLB chunk this step can pack again";

        const long Float = 5126, UnsignedShort = 5123;

        // The extensions UnusedMorphTargets checked, and the lilToon one: it
        // points to bufferViews (moved like any other) and to meshes and
        // materials by index, never to an accessor.
        static readonly string[] KnownExtensions = UnusedMorphTargets.KnownExtensions.Append(LilToon.LilToonFullContract.ExtensionName).ToArray();

        // Returns the input array itself when nothing changes. saved receives
        // how many bytes the file lost.
        internal static byte[] Apply(byte[] bytes, ICollection<string> warnings, out long saved)
        {
            saved = 0;
            if (bytes == null) return bytes;
            var glb = GlbDocument.Read(bytes);
            var json = glb.Json;
            var unknown = new SortedSet<string>(StringComparer.Ordinal);
            UnusedMorphTargets.CollectUnknownExtensions(json, unknown, KnownExtensions);
            if (unknown.Count > 0)
            {
                warnings?.Add(string.Format(UnknownExtensionWarning, string.Join(", ", unknown)));
                return bytes;
            }
            if (!(json.TryGetValue("meshes", out var rawMeshes) && rawMeshes is List<object> meshes) || meshes.Count == 0) return bytes;
            if (!UnusedMorphTargets.StorageIsPlain(glb))
            {
                warnings?.Add(StorageWarning);
                return bytes;
            }
            var removed = RemoveColors(json, meshes.OfType<Dictionary<string, object>>());
            var replacedViews = PackWeights(glb);
            if (removed.Count == 0 && replacedViews.Count == 0) return bytes;
            UnusedMorphTargets.RemoveAccessors(glb, removed, replacedViews);
            var result = glb.Write();
            if (result.Length >= bytes.Length) return bytes;
            saved = bytes.Length - result.Length;
            return result;
        }

        // The line of the result list; null when nothing was saved.
        internal static string ResultLine(long saved) => saved <= 0 ? null :
            string.Format(CultureInfo.InvariantCulture, ExporterLocalization.T(ExportNoteText.VertexDataLightened), TexelDensity.Plan.Megabytes(saved));

        // Takes COLOR_0 off every primitive of a mesh whose materials all leave
        // it unread; the primitives of a mesh share their attribute accessors.
        // Returns the accessors taken off.
        static HashSet<int> RemoveColors(Dictionary<string, object> json, IEnumerable<Dictionary<string, object>> meshes)
        {
            var materials = List(json, "materials") ?? new List<object>();
            var records = new Dictionary<int, Dictionary<string, object>>();
            var extension = Object(Object(json, "extensions"), LilToon.LilToonFullContract.ExtensionName);
            foreach (var record in Objects(List(extension, "materials")))
                if (record.TryGetValue("materialIndex", out var raw) && Index(raw) is int index) records[index] = record;
            var removed = new HashSet<int>();
            foreach (var mesh in meshes)
            {
                var primitives = Objects(List(mesh, "primitives")).ToList();
                if (!primitives.Any(p => Object(p, "attributes")?.ContainsKey("COLOR_0") == true)) continue;
                // A morph target of the colours would be left without them.
                if (primitives.Any(p => Objects(List(p, "targets")).Any(t => t.ContainsKey("COLOR_0")))) continue;
                if (primitives.Any(p => !(p.TryGetValue("material", out var raw) && Index(raw) is int index && index < materials.Count) ||
                                        ReadsVertexColor(materials[index] as Dictionary<string, object>, records.TryGetValue(index, out var record) ? record : null)))
                    continue;
                foreach (var primitive in primitives)
                {
                    var attributes = Object(primitive, "attributes");
                    if (attributes == null || !attributes.TryGetValue("COLOR_0", out var color)) continue;
                    removed.Add((int)Index(color));
                    attributes.Remove("COLOR_0");
                }
            }
            return removed;
        }

        // Whether the app reads COLOR_0 for a material. lilToon 2.3.4 reads it
        // for the outline width and direction (_OutlineVertexR2Width 1 or 2:
        // the outline passes, and the forward pass that hides a vertex of zero
        // width with _OutlineDeleteMesh) and for the fur direction
        // (_VertexColor2FurVector). lilToonMulti chooses its outline by
        // keyword, so any _OutlineVertexR2Width counts. A property the shader
        // does not declare is 0. The app's MToon never reads COLOR_0; a
        // material with neither (unlit, PBR) counts as reading it, as does a
        // primitive with no material.
        internal static bool ReadsVertexColor(Dictionary<string, object> material, Dictionary<string, object> record)
        {
            if (material == null) return true;
            if (record == null) return Object(Object(material, "extensions"), "VRMC_materials_mtoon") == null;
            if (Value(record, "_VertexColor2FurVector") != 0) return true;
            if (Value(record, "_OutlineVertexR2Width") == 0) return false;
            if (Value(record, "_OutlineDeleteMesh") != 0) return true;
            if (!(record.TryGetValue("sourceShader", out var shader) && shader is string name) || name.IndexOf("lilToonMulti", StringComparison.Ordinal) >= 0) return true;
            if (!(record.TryGetValue("passes", out var raw) && raw is List<object> passes)) return true;
            return Objects(passes).Any(pass => pass.TryGetValue("name", out var passName) && passName is string text &&
                                                text.IndexOf("OUTLINE", StringComparison.Ordinal) >= 0 &&
                                                !(pass.TryGetValue("enabled", out var enabled) && enabled is bool on && !on));
        }

        // The first component of a recorded value: 0 when the record has no
        // such property, NaN when it cannot be read.
        static double Value(Dictionary<string, object> record, string name)
        {
            var entry = Objects(List(record, "values")).FirstOrDefault(value => value.TryGetValue("name", out var n) && n as string == name);
            if (entry == null) return 0;
            var components = List(entry, "value");
            return components != null && components.Count > 0 && Number(components[0]) is double number ? number : double.NaN;
        }

        // Rewrites each float VEC4 accessor that only WEIGHTS_0 uses, and
        // whose bufferView only it uses, into a new bufferView of normalized
        // unsigned shorts. Returns the bufferViews it no longer uses.
        static HashSet<int> PackWeights(GlbDocument glb)
        {
            var json = glb.Json;
            var accessors = List(json, "accessors");
            var views = List(json, "bufferViews");
            var replaced = new HashSet<int>();
            if (accessors == null || views == null) return replaced;
            // An accessor counts only when every reference to it is a WEIGHTS_0.
            var weightUses = new Dictionary<int, int>();
            foreach (var mesh in Objects(List(json, "meshes")))
                foreach (var primitive in Objects(List(mesh, "primitives")))
                    if (Object(primitive, "attributes") is Dictionary<string, object> attributes && attributes.TryGetValue("WEIGHTS_0", out var raw))
                        Count(weightUses, (int)Index(raw));
            var uses = new Dictionary<int, int>();
            UnusedMorphTargets.ForEachAccessorReference(json, (owner, key) => Count(uses, (int)Index(owner[key])));
            var viewUses = new Dictionary<int, int>();
            UnusedMorphTargets.ForEachViewReference(json, (owner, key) => Count(viewUses, (int)Index(owner[key])));
            var payloads = new List<(Dictionary<string, object> Accessor, int View, int Data)>();
            // Weights the export already shares between meshes keep sharing
            // their bytes, so the file never grows.
            var unique = new List<byte[]>();
            var seen = new Dictionary<byte[], int>(new SameBytes());
            foreach (var pair in weightUses.OrderBy(pair => pair.Key))
            {
                var index = pair.Key;
                if (index >= accessors.Count || uses[index] != pair.Value || !(accessors[index] is Dictionary<string, object> accessor)) continue;
                if (!(accessor.TryGetValue("bufferView", out var rawView) && Index(rawView) is int view) || view >= views.Count || viewUses[view] != 1) continue;
                var data = Quantize(accessor, (Dictionary<string, object>)views[view], glb.Binary);
                if (data == null) continue;
                if (!seen.TryGetValue(data, out var at))
                {
                    at = unique.Count;
                    unique.Add(data);
                    seen.Add(data, at);
                }
                payloads.Add((accessor, view, at));
            }
            if (payloads.Count == 0) return replaced;
            var offsets = glb.AppendBinaryBatch(unique.ToArray());
            for (var i = 0; i < payloads.Count; i++)
            {
                var (accessor, view, at) = payloads[i];
                var old = (Dictionary<string, object>)views[view];
                var added = new Dictionary<string, object> { { "buffer", 0L }, { "byteOffset", (long)offsets[at] }, { "byteLength", (long)unique[at].Length } };
                if (old.TryGetValue("target", out var target)) added["target"] = target;
                views.Add(added);
                accessor["bufferView"] = (long)(views.Count - 1);
                if (accessor.ContainsKey("byteOffset")) accessor["byteOffset"] = 0L;
                accessor["componentType"] = UnsignedShort;
                accessor["normalized"] = true;
                replaced.Add(view);
            }
            return replaced;
        }

        // The weights as unsigned shorts, or null when the accessor is not a
        // plain float VEC4 one or a vertex's weights are not ones that add up
        // to 1. Each weight is rounded to 1/65535 and what is left over goes to
        // the largest, so a vertex adds up to exactly 65535. A zero stays zero.
        internal static byte[] Quantize(Dictionary<string, object> accessor, Dictionary<string, object> view, byte[] binary)
        {
            if (!(accessor.TryGetValue("componentType", out var type) && Number(type) == Float) ||
                !(accessor.TryGetValue("type", out var shape) && shape as string == "VEC4")) return null;
            if (accessor.ContainsKey("sparse") || accessor.ContainsKey("min") || accessor.ContainsKey("max")) return null;
            if (accessor.TryGetValue("normalized", out var normalized) && !(normalized is bool flag && !flag)) return null;
            if (!(accessor.TryGetValue("count", out var rawCount) && Index(rawCount) is int count) || count == 0) return null;
            var stride = view.TryGetValue("byteStride", out var rawStride) ? Index(rawStride) ?? 0 : 16;
            var offset = accessor.TryGetValue("byteOffset", out var rawOffset) ? Index(rawOffset) ?? -1 : 0;
            // StorageIsPlain checked that the view lies inside the binary chunk.
            if (stride < 16 || offset < 0 || offset + (long)stride * (count - 1) + 16 > Index(view["byteLength"])) return null;
            var start = Offset(view) + offset;
            var result = new byte[checked(count * 8)];
            var weights = new double[4];
            var values = new int[4];
            for (var v = 0; v < count; v++)
            {
                var sum = 0;
                var largest = 0;
                for (var c = 0; c < 4; c++)
                {
                    var weight = (double)BitConverter.ToSingle(binary, start + v * stride + c * 4);
                    if (double.IsNaN(weight) || weight < 0 || weight > 1.0001) return null;
                    weights[c] = weight;
                    values[c] = (int)Math.Round(Math.Min(weight, 1) * ushort.MaxValue, MidpointRounding.AwayFromZero);
                    sum += values[c];
                    if (weight > weights[largest]) largest = c;
                }
                if (weights[largest] > 0)
                {
                    // Rounding four weights is off by 2 at most; more means
                    // they do not add up to 1.
                    var rest = ushort.MaxValue - sum;
                    if (Math.Abs(rest) > 2) return null;
                    values[largest] += rest;
                }
                for (var c = 0; c < 4; c++)
                {
                    result[v * 8 + c * 2] = (byte)values[c];
                    result[v * 8 + c * 2 + 1] = (byte)(values[c] >> 8);
                }
            }
            return result;
        }

        sealed class SameBytes : IEqualityComparer<byte[]>
        {
            public bool Equals(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

            public int GetHashCode(byte[] bytes)
            {
                var hash = unchecked((int)2166136261);
                foreach (var b in bytes) hash = unchecked((hash ^ b) * 16777619);
                return hash;
            }
        }

        static void Count(Dictionary<int, int> counts, int index) => counts[index] = counts.TryGetValue(index, out var count) ? count + 1 : 1;

        static int Offset(Dictionary<string, object> view) =>
            view.TryGetValue("byteOffset", out var raw) && Index(raw) is int offset ? offset : 0;

        static double? Number(object value)
        {
            if (value is long l) return l;
            if (value is int i) return i;
            if (value is double d && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
            return null;
        }

        static int? Index(object value)
        {
            if (value is long l && l >= 0 && l <= int.MaxValue) return (int)l;
            if (value is int i && i >= 0) return i;
            if (value is double d && d >= 0 && d <= int.MaxValue && d == Math.Floor(d)) return (int)d;
            return null;
        }

        static Dictionary<string, object> Object(Dictionary<string, object> owner, string key) =>
            owner != null && owner.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;

        static List<object> List(Dictionary<string, object> owner, string key) =>
            owner != null && owner.TryGetValue(key, out var value) ? value as List<object> : null;

        static IEnumerable<Dictionary<string, object>> Objects(List<object> list) =>
            list?.OfType<Dictionary<string, object>>() ?? Enumerable.Empty<Dictionary<string, object>>();
    }
}
