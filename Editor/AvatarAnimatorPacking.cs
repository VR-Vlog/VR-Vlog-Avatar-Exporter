using System;
using System.Collections.Generic;
using System.IO;
using VRVlog.AvatarAnimator;
using VRVlog.AvatarFormats;

namespace VRVlog.LilToonExporter
{
    // VRVLOG_avatar_animator schema 2 in a GLB: the document (what
    // AvatarAnimatorData.Write returns) in the binary chunk, compressed with
    // AvatarAnimatorData.Pack, and the extension naming its bufferView. The
    // document's clips are most of it and compress well, and the JSON chunk,
    // which the app parses before it shows anything, stays small.
    internal static class AvatarAnimatorPacking
    {
        // Appends the packed document as a new bufferView and places the
        // extension naming it. The uncompressed document is checked by the
        // caller against its own limit.
        internal static void Place(GlbDocument document, Dictionary<string, object> written)
        {
            var packed = AvatarAnimatorData.Pack(JsonDom.Serialize(written));
            var offset = document.AppendBinaryBatch(new[] { packed })[0];
            if (!document.Json.TryGetValue("bufferViews", out var rawViews)) document.Json["bufferViews"] = rawViews = new List<object>();
            var views = (List<object>)rawViews;
            views.Add(new Dictionary<string, object> { ["buffer"] = 0L, ["byteOffset"] = (long)offset, ["byteLength"] = (long)packed.Length });
            document.PlaceExtension(AvatarAnimatorData.Extension, AvatarAnimatorData.Packed(views.Count - 1));
        }

        // The document as AvatarAnimatorData.Read takes it: a packed one
        // unpacked, one written in the JSON as it is; null without the extension.
        internal static object Document(GlbDocument document)
        {
            var extensions = document.Find("extensions");
            if (extensions == null || !extensions.TryGetValue(AvatarAnimatorData.Extension, out var value)) return null;
            if (!AvatarAnimatorData.IsPacked(value)) return value;
            var (offset, length) = AvatarAnimatorData.BufferViewRange(document.Json, AvatarAnimatorData.PackedView(value));
            if ((long)offset + length > document.Binary.Length) throw new InvalidDataException("The packed " + AvatarAnimatorData.Extension + " is outside the binary chunk.");
            var packed = new byte[length];
            Buffer.BlockCopy(document.Binary, offset, packed, 0, length);
            return JsonDom.Parse(AvatarAnimatorData.Unpack(packed));
        }

        // Removes the extension and its name in extensionsUsed, and the
        // bufferView of a packed one with its bytes; whether anything went.
        internal static bool Remove(GlbDocument document)
        {
            object value = null;
            var extensions = document.Find("extensions");
            var removed = extensions != null && extensions.TryGetValue(AvatarAnimatorData.Extension, out value) && extensions.Remove(AvatarAnimatorData.Extension);
            removed |= document.Json.TryGetValue("extensionsUsed", out var used) && used is List<object> names && names.Remove(AvatarAnimatorData.Extension);
            if (value != null && AvatarAnimatorData.IsPacked(value)) DropView(document, AvatarAnimatorData.PackedView(value));
            return removed;
        }

        // Drops a bufferView nothing else names: the later ones move down one
        // index, and its bytes go with their padding when no other view
        // reaches into them, the later bytes moving down by a multiple of four.
        // The binary is cut to the buffer's byteLength: a GLB read back holds
        // the chunk's padding past it.
        static void DropView(GlbDocument document, int view)
        {
            var named = false;
            ForEachView(document.Json, (map, index) => named |= index == view);
            if (named) return;
            var (offset, length) = AvatarAnimatorData.BufferViewRange(document.Json, view);
            var views = (List<object>)document.Json["bufferViews"];
            views.RemoveAt(view);
            ForEachView(document.Json, (map, index) => { if (index > view) map["bufferView"] = (long)(index - 1); });
            if (!(JsonAccess.List(document.Json, "buffers") is List<object> buffers) || buffers.Count != 1 ||
                !(buffers[0] is Dictionary<string, object> buffer) || !(buffer.TryGetValue("byteLength", out var rawTotal) && rawTotal is long declared)) return;
            var total = (int)Math.Min(document.Binary.Length, declared);
            var end = Math.Min(total, (offset + length + 3) & ~3);
            if (offset % 4 != 0 || end <= offset) return;
            var others = new List<(Dictionary<string, object> View, long Offset, long End)>();
            foreach (var raw in views)
            {
                if (!(raw is Dictionary<string, object> other)) return;
                if (Convert.ToInt64(other.TryGetValue("buffer", out var otherBuffer) ? otherBuffer : 0L) != 0) continue;
                var start = other.TryGetValue("byteOffset", out var rawOffset) ? Convert.ToInt64(rawOffset) : 0L;
                var stop = start + Convert.ToInt64(other["byteLength"]);
                if (start < end && stop > offset) return;
                others.Add((other, start, stop));
            }
            var cut = end - offset;
            var binary = new byte[total - cut];
            Buffer.BlockCopy(document.Binary, 0, binary, 0, offset);
            Buffer.BlockCopy(document.Binary, end, binary, offset, total - end);
            foreach (var other in others)
                if (other.Offset >= end) other.View["byteOffset"] = other.Offset - cut;
            document.ReplaceBinary(binary);
        }

        // Each "bufferView" key outside the bufferViews list, with its index.
        static void ForEachView(object value, Action<Dictionary<string, object>, long> visit)
        {
            if (value is List<object> list) foreach (var item in list) ForEachView(item, visit);
            else if (value is Dictionary<string, object> map)
                foreach (var key in new List<string>(map.Keys))
                {
                    if (key == "bufferView") { if (map[key] is long || map[key] is int) visit(map, Convert.ToInt64(map[key])); }
                    else if (key != "bufferViews") ForEachView(map[key], visit);
                }
        }
    }
}
