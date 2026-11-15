using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    // The materials only slot curves set (Vrm10AppearanceExporter's
    // addedMaterials) count toward the app's material and texture limits like
    // the meshes' own. Over a limit the whole lilToon extension would go, so
    // they are added in the order the curves name them only while the file
    // stays within both; the first that does not fit and all after it are left
    // out. A file the meshes' own materials already put over a limit gets none.
    // See Documentation~/decisions/2026-11-15-liltoon-unbound-limit.md.
    internal static class AddedMaterialLimit
    {
        // How many of the materials from firstAdded on (the added ones, which
        // no primitive draws with) fit within the limits, in order.
        internal static int Fitting(Dictionary<string, object> json, int firstAdded,
            int maximumMaterials = LilToonMobileProfile.MaximumMaterials, int maximumTextures = LilToonMobileProfile.MaximumTextures)
        {
            var materials = Items(json, "materials");
            var textureCount = Items(json, "textures").Count;
            if (firstAdded < 0 || firstAdded > materials.Count) throw new ArgumentOutOfRangeException(nameof(firstAdded));
            var uses = materials.Select(TexturesOf).ToList();
            // Every texture but those only the added materials use: the
            // meshes' materials need them whatever is added.
            var addedOnly = new HashSet<int>(uses.Skip(firstAdded).SelectMany(each => each));
            addedOnly.ExceptWith(uses.Take(firstAdded).SelectMany(each => each));
            var counted = new HashSet<int>(Enumerable.Range(0, textureCount).Where(texture => !addedOnly.Contains(texture)));
            var fitting = 0;
            for (var i = firstAdded; i < materials.Count; i++)
            {
                var more = uses[i].Where(texture => !counted.Contains(texture)).Distinct().Count();
                if (i + 1 > maximumMaterials || counted.Count + more > maximumTextures) break;
                counted.UnionWith(uses[i]);
                fitting++;
            }
            return fitting;
        }

        // Leaves the materials from keep on out of the file, with the textures
        // only they used, the images only those textures showed and the
        // bufferViews only those images were stored in. None of them may be
        // drawn with. Gives back the bytes unchanged when there is nothing past
        // keep.
        internal static byte[] Trim(byte[] bytes, int keep)
        {
            var glb = GlbDocument.Read(bytes);
            var materials = Items(glb.Json, "materials");
            if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep));
            if (keep >= materials.Count) return bytes;
            foreach (var mesh in Items(glb.Json, "meshes"))
                foreach (var primitive in Items(mesh as Dictionary<string, object>, "primitives"))
                    if (primitive is Dictionary<string, object> drawn && drawn.TryGetValue("material", out var used) && Convert.ToInt32(used) >= keep)
                        throw new InvalidOperationException("A material left out is drawn with.");
            var dropped = new HashSet<int>(materials.Skip(keep).SelectMany(TexturesOf));
            materials.RemoveRange(keep, materials.Count - keep);
            var kept = new HashSet<int>();
            Walk(glb.Json, null, (key, map) => { if (IsTextureInfo(key, map)) kept.Add(Convert.ToInt32(map["index"])); });
            dropped.ExceptWith(kept);
            if (dropped.Count == 0) return glb.Write();

            var textures = Items(glb.Json, "textures");
            var droppedImages = new HashSet<int>(dropped.Select(texture => Source(textures[texture])).Where(image => image >= 0));
            var textureMap = Remove(textures, dropped);
            Walk(glb.Json, null, (key, map) => { if (IsTextureInfo(key, map)) map["index"] = (long)textureMap[Convert.ToInt32(map["index"])]; });
            foreach (var texture in textures) droppedImages.Remove(Source(texture));
            droppedImages.ExceptWith(Thumbnails(glb.Json));
            if (droppedImages.Count > 0)
            {
                var images = Items(glb.Json, "images");
                var droppedViews = new HashSet<int>(droppedImages.Select(image => Integer(images[image], "bufferView")).Where(view => view >= 0));
                var imageMap = Remove(images, droppedImages);
                foreach (var texture in textures)
                    if (texture is Dictionary<string, object> map && map.TryGetValue("source", out var source)) map["source"] = (long)imageMap[Convert.ToInt32(source)];
                Walk(glb.Json, null, (key, map) => { if (map.TryGetValue("thumbnailImage", out var thumbnail)) map["thumbnailImage"] = (long)imageMap[Convert.ToInt32(thumbnail)]; });
                Walk(glb.Json, null, (key, map) => { if (map.TryGetValue("bufferView", out var view)) droppedViews.Remove(Convert.ToInt32(view)); });
                if (droppedViews.Count > 0)
                {
                    var viewMap = Remove(Items(glb.Json, "bufferViews"), droppedViews);
                    Walk(glb.Json, null, (key, map) => { if (map.TryGetValue("bufferView", out var view)) map["bufferView"] = (long)viewMap[Convert.ToInt32(view)]; });
                    // The bytes no view covers any more are left out of BIN.
                    GlbBinaryOptimizer.Compact(glb);
                }
            }
            return glb.Write();
        }

        // glTF textureInfo objects ({"index": texture}) sit under keys ending in
        // Texture, in the core material and in VRMC_materials_mtoon alike.
        static bool IsTextureInfo(string key, Dictionary<string, object> map) =>
            key != null && key.EndsWith("Texture", StringComparison.Ordinal) && map.ContainsKey("index");

        static HashSet<int> TexturesOf(object material)
        {
            var result = new HashSet<int>();
            Walk(material, null, (key, map) => { if (IsTextureInfo(key, map)) result.Add(Convert.ToInt32(map["index"])); });
            return result;
        }

        static IEnumerable<int> Thumbnails(Dictionary<string, object> json)
        {
            var result = new List<int>();
            Walk(json, null, (key, map) => { if (map.TryGetValue("thumbnailImage", out var image)) result.Add(Convert.ToInt32(image)); });
            return result;
        }

        static void Walk(object value, string key, Action<string, Dictionary<string, object>> visit)
        {
            if (value is Dictionary<string, object> map)
            {
                visit(key, map);
                foreach (var pair in map.ToList()) Walk(pair.Value, pair.Key, visit);
            }
            else if (value is List<object> list)
                foreach (var item in list) Walk(item, key, visit);
        }

        // Removes the given positions and maps each old position kept to its new one.
        static Dictionary<int, int> Remove(List<object> list, HashSet<int> removed)
        {
            var map = new Dictionary<int, int>();
            var kept = new List<object>();
            for (var i = 0; i < list.Count; i++)
            {
                if (removed.Contains(i)) continue;
                map.Add(i, kept.Count);
                kept.Add(list[i]);
            }
            list.Clear();
            list.AddRange(kept);
            return map;
        }

        static int Source(object texture) => Integer(texture, "source");

        static int Integer(object value, string key) =>
            value is Dictionary<string, object> map && map.TryGetValue(key, out var raw) ? Convert.ToInt32(raw) : -1;

        static List<object> Items(Dictionary<string, object> json, string key) =>
            json != null && json.TryGetValue(key, out var raw) && raw is List<object> list ? list : new List<object>();
    }
}
