using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VRVlog.LilToon;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // 自動で軽量化する, on the finished VRM: writes the MToon images that only
    // the lilToon extension's materials use at a long side of 256 (512 for the
    // face). The app draws those materials from the extension and never
    // decodes these images, so only other VRM apps show them, at a lower
    // resolution. Which materials count follows the app
    // (LilToonLoadSession.FullyBoundMaterials): a material with an extension
    // record that no node without a binding draws. An image anything else
    // uses (another material, the thumbnail) keeps its size. A file this
    // class cannot fully account for is returned unchanged.
    internal static class FallbackImageLightweight
    {
        internal const int Maximum = 256;
        // The images of a material on a mesh the expressions move (the face,
        // with its eyes, brows and lashes) or recolour.
        internal const int FaceMaximum = 512;

        // Logged to the Console only: the file is kept unchanged.
        internal const string UnknownExtensionWarning = "Kept the MToon images because the file has extensions this step has not checked: {0}";
        internal const string StorageWarning = "Kept the MToon images because the file's binary data is not one GLB chunk this step can pack again";

        static readonly string[] KnownExtensions = UnusedMorphTargets.KnownExtensions.Append(LilToonFullContract.ExtensionName).ToArray();

        // The MToon and glTF texture slots and whether each holds colour
        // (sRGB). A slot not listed here keeps its image unchanged.
        static readonly Dictionary<string, bool> Slots = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            {"baseColorTexture", true}, {"emissiveTexture", true}, {"shadeMultiplyTexture", true}, {"matcapTexture", true}, {"rimMultiplyTexture", true},
            {"normalTexture", false}, {"shadingShiftTexture", false}, {"outlineWidthMultiplyTexture", false}, {"uvAnimationMaskTexture", false},
            {"metallicRoughnessTexture", false}, {"occlusionTexture", false},
        };

        // Returns the input array itself when nothing changes. count receives
        // how many images became smaller, saved how many bytes the file lost.
        internal static byte[] Apply(byte[] bytes, ICollection<string> warnings, out int count, out long saved)
        {
            count = 0;
            saved = 0;
            if (bytes == null) return bytes;
            var glb = GlbDocument.Read(bytes);
            var json = glb.Json;
            if (!LilToonFullContract.IsFull(json)) return bytes;
            var unknown = new SortedSet<string>(StringComparer.Ordinal);
            UnusedMorphTargets.CollectUnknownExtensions(json, unknown, KnownExtensions);
            if (unknown.Count > 0)
            {
                warnings?.Add(string.Format(UnknownExtensionWarning, string.Join(", ", unknown)));
                return bytes;
            }
            if (!UnusedMorphTargets.StorageIsPlain(glb))
            {
                warnings?.Add(StorageWarning);
                return bytes;
            }
            var plan = Plan(json);
            if (plan.Count == 0) return bytes;
            var images = List(json, "images");
            var views = List(json, "bufferViews");
            var viewUses = new Dictionary<int, int>();
            UnusedMorphTargets.ForEachViewReference(json, (owner, key) => viewUses[(int)Index(owner[key])] = viewUses.TryGetValue((int)Index(owner[key]), out var n) ? n + 1 : 1);
            var payloads = new List<(Dictionary<string, object> Image, int View, byte[] Data)>();
            foreach (var pair in plan.OrderBy(pair => pair.Key))
            {
                if (!(images[pair.Key] is Dictionary<string, object> image) || !(image.TryGetValue("mimeType", out var mime) && mime as string == "image/png")) continue;
                if (!(image.TryGetValue("bufferView", out var rawView) && Index(rawView) is int view) || view >= views.Count || viewUses[view] != 1) continue;
                var bufferView = (Dictionary<string, object>)views[view];
                var png = new byte[(int)Index(bufferView["byteLength"])];
                Buffer.BlockCopy(glb.Binary, Offset(bufferView), png, 0, png.Length);
                var smaller = Shrink(png, pair.Value.Srgb, pair.Value.Maximum);
                if (smaller != null && smaller.Length < png.Length) payloads.Add((image, view, smaller));
            }
            if (payloads.Count == 0) return bytes;
            var offsets = glb.AppendBinaryBatch(payloads.Select(p => p.Data).ToArray());
            var replaced = new HashSet<int>();
            for (var i = 0; i < payloads.Count; i++)
            {
                views.Add(new Dictionary<string, object> { { "buffer", 0L }, { "byteOffset", (long)offsets[i] }, { "byteLength", (long)payloads[i].Data.Length } });
                payloads[i].Image["bufferView"] = (long)(views.Count - 1);
                replaced.Add(payloads[i].View);
            }
            UnusedMorphTargets.RemoveAccessors(glb, new HashSet<int>(), replaced);
            var result = glb.Write();
            if (result.Length >= bytes.Length) return bytes;
            count = payloads.Count;
            saved = bytes.Length - result.Length;
            return result;
        }

        // The line of the result list; null when nothing was saved.
        internal static string ResultLine(int count, long saved) => count <= 0 || saved <= 0 ? null :
            string.Format(CultureInfo.InvariantCulture, ExporterLocalization.T(ExportNoteText.FallbackImagesLightened), count, TexelDensity.Plan.Megabytes(saved));

        // The images this step may make smaller, each with whether it holds
        // colour and its maximum long side.
        internal static Dictionary<int, (bool Srgb, int Maximum)> Plan(Dictionary<string, object> json)
        {
            var result = new Dictionary<int, (bool Srgb, int Maximum)>();
            var images = List(json, "images");
            var textures = List(json, "textures");
            var materials = List(json, "materials");
            if (images == null || textures == null || materials == null) return result;
            var fully = FullyBoundMaterials(json);
            var face = FaceMaterials(json);
            var kept = new HashSet<int>();
            var vrm = Object(Object(json, "extensions"), "VRMC_vrm");
            if (Object(vrm, "meta") is Dictionary<string, object> meta && meta.TryGetValue("thumbnailImage", out var thumbnail) && Index(thumbnail) is int thumbnailImage)
                kept.Add(thumbnailImage);
            var uses = new Dictionary<int, (bool? Srgb, bool Face)>();
            for (var m = 0; m < materials.Count; m++)
            {
                var references = new List<(int Texture, string Slot)>();
                TextureReferences(materials[m], null, references);
                foreach (var (texture, slot) in references)
                {
                    if (texture >= textures.Count || !(textures[texture] is Dictionary<string, object> record) ||
                        !(record.TryGetValue("source", out var rawSource) && Index(rawSource) is int image) || image >= images.Count)
                        continue;
                    if (!fully.Contains(m) || !Slots.TryGetValue(slot, out var srgb)) { kept.Add(image); continue; }
                    if (uses.TryGetValue(image, out var use))
                    {
                        if (use.Srgb != srgb) kept.Add(image);
                        uses[image] = (srgb, use.Face || face.Contains(m));
                    }
                    else uses[image] = (srgb, face.Contains(m));
                }
            }
            // A texture no material reaches could be used another way; only
            // the material references are known.
            foreach (var pair in uses)
                if (!kept.Contains(pair.Key)) result[pair.Key] = ((bool)pair.Value.Srgb, pair.Value.Face ? FaceMaximum : Maximum);
            return result;
        }

        // Every texture a material refers to, with the key it is under.
        // KHR_texture_transform stays as written: it is in UV space.
        static void TextureReferences(object value, string key, List<(int, string)> references)
        {
            if (value is List<object> list) { foreach (var item in list) TextureReferences(item, null, references); return; }
            if (!(value is Dictionary<string, object> obj)) return;
            if (key != null && key.EndsWith("Texture", StringComparison.Ordinal) && obj.TryGetValue("index", out var raw) && Index(raw) is int index)
            {
                references.Add((index, key));
                return;
            }
            foreach (var pair in obj) TextureReferences(pair.Value, pair.Key, references);
        }

        // The app's LilToonLoadSession.FullyBoundMaterials: the materials with
        // an extension record, less those a node without a binding draws.
        internal static HashSet<int> FullyBoundMaterials(Dictionary<string, object> json)
        {
            var root = Object(Object(json, "extensions"), LilToonFullContract.ExtensionName);
            var result = new HashSet<int>();
            foreach (var record in Objects(List(root, "materials")))
                if (record.TryGetValue("materialIndex", out var raw) && Index(raw) is int index) result.Add(index);
            var bound = new HashSet<int>();
            foreach (var binding in Objects(List(root, "bindings")))
                if (binding.TryGetValue("node", out var raw) && Index(raw) is int node) bound.Add(node);
            var nodes = List(json, "nodes") ?? new List<object>();
            var meshes = List(json, "meshes") ?? new List<object>();
            for (var n = 0; n < nodes.Count; n++)
            {
                if (bound.Contains(n) || !(nodes[n] is Dictionary<string, object> node) || !(node.TryGetValue("mesh", out var rawMesh) && Index(rawMesh) is int mesh)) continue;
                if (mesh >= meshes.Count) { result.Clear(); return result; }
                foreach (var primitive in Objects(List(meshes[mesh] as Dictionary<string, object>, "primitives")))
                    if (primitive.TryGetValue("material", out var rawMaterial) && Index(rawMaterial) is int material) result.Remove(material);
            }
            return result;
        }

        // The materials of the meshes the expressions move, and the ones they
        // recolour or move the UVs of.
        internal static HashSet<int> FaceMaterials(Dictionary<string, object> json)
        {
            var result = new HashSet<int>();
            var expressions = Object(Object(Object(json, "extensions"), "VRMC_vrm"), "expressions");
            var nodes = List(json, "nodes") ?? new List<object>();
            var meshes = List(json, "meshes") ?? new List<object>();
            foreach (var group in new[] { Object(expressions, "preset"), Object(expressions, "custom") })
            {
                if (group == null) continue;
                foreach (var expression in group.Values.OfType<Dictionary<string, object>>())
                {
                    foreach (var bind in Objects(List(expression, "morphTargetBinds")))
                    {
                        if (!(bind.TryGetValue("node", out var rawNode) && Index(rawNode) is int n) || n >= nodes.Count) continue;
                        if (!(nodes[n] is Dictionary<string, object> node) || !(node.TryGetValue("mesh", out var rawMesh) && Index(rawMesh) is int mesh) || mesh >= meshes.Count) continue;
                        foreach (var primitive in Objects(List(meshes[mesh] as Dictionary<string, object>, "primitives")))
                            if (primitive.TryGetValue("material", out var rawMaterial) && Index(rawMaterial) is int material) result.Add(material);
                    }
                    foreach (var key in new[] { "materialColorBinds", "textureTransformBinds" })
                        foreach (var bind in Objects(List(expression, key)))
                            if (bind.TryGetValue("material", out var rawMaterial) && Index(rawMaterial) is int material) result.Add(material);
                }
            }
            return result;
        }

        // The PNG at maximum long side, or null when it already fits or
        // cannot be read.
        internal static byte[] Shrink(byte[] png, bool srgb, int maximum)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, !srgb);
            try
            {
                if (!texture.LoadImage(png, false)) return null;
                int width = texture.width, height = texture.height;
                var size = TextureResizePolicy.Size(width, height, maximum);
                if (size.Item1 == width && size.Item2 == height) return null;
                var pixels = texture.GetPixels32();
                var rgba = new byte[checked(pixels.Length * 4)];
                for (var i = 0; i < pixels.Length; i++)
                {
                    rgba[i * 4] = pixels[i].r; rgba[i * 4 + 1] = pixels[i].g;
                    rgba[i * 4 + 2] = pixels[i].b; rgba[i * 4 + 3] = pixels[i].a;
                }
                var resized = TextureResizePolicy.Resize(rgba, width, height, srgb, maximum);
                var colors = new Color32[resized.Length / 4];
                for (var i = 0; i < colors.Length; i++) colors[i] = new Color32(resized[i * 4], resized[i * 4 + 1], resized[i * 4 + 2], resized[i * 4 + 3]);
                return PngWriter.Encode(colors, size.Item1, size.Item2);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        static int Offset(Dictionary<string, object> view) =>
            view.TryGetValue("byteOffset", out var raw) && Index(raw) is int offset ? offset : 0;

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
