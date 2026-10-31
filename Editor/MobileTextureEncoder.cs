using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using UnityEngine;
using ExportColorSpace = VRVlog.LilToonExporter.Vendor.UniGLTF.ColorSpace;

namespace VRVlog.LilToonExporter
{
    internal static class MobileTextureEncoder
    {
        // Called after UniVRM unpacked normal maps/combined PBR channels and
        // converted texture color space. Never reinterpret packed Unity normals.
        // maximum below the default is 自動で軽量化する's smaller size; only the
        // default's resizing is noted per image, with the size after it alone.
        internal static byte[] EncodeConverted(Texture2D source, bool srgb, ICollection<string> warnings = null,
            int maximum = LilToonMobileProfile.DefaultMaximumTextureSize)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var size = TextureResizePolicy.Size(source.width, source.height, maximum);
            if (size.Item1 == source.width && size.Item2 == source.height) return RequirePng(PngWriter.Encode(source), source.name);
            var pixels = source.GetPixels32();
            var rgba = new byte[checked(pixels.Length * 4)];
            for (var i = 0; i < pixels.Length; i++)
            {
                rgba[i * 4] = pixels[i].r; rgba[i * 4 + 1] = pixels[i].g;
                rgba[i * 4 + 2] = pixels[i].b; rgba[i * 4 + 3] = pixels[i].a;
            }
            var resized = TextureResizePolicy.Resize(rgba, source.width, source.height, srgb, maximum);
            var destination = new Texture2D(size.Item1, size.Item2, TextureFormat.RGBA32, false, !srgb);
            try
            {
                var colors = new Color32[resized.Length / 4];
                for (var i = 0; i < colors.Length; i++) colors[i] = new Color32(resized[i * 4], resized[i * 4 + 1], resized[i * 4 + 2], resized[i * 4 + 3]);
                destination.SetPixels32(colors);
                destination.Apply(false, false);
                var png = RequirePng(PngWriter.Encode(destination), source.name);
                var capped = TextureResizePolicy.Size(source.width, source.height);
                if (capped.Item1 != source.width || capped.Item2 != source.height)
                    Warn(warnings, source.name, source.width, source.height, capped.Item1, capped.Item2);
                return png;
            }
            finally { UnityEngine.Object.DestroyImmediate(destination); }
        }

        internal static void Warn(ICollection<string> warnings, string name, int width, int height, int resizedWidth, int resizedHeight)
        {
            var message = string.Format(ExportNoteText.OutputTextureResized, name, width, height, resizedWidth, resizedHeight);
            if (warnings != null && !warnings.Contains(message)) warnings.Add(message);
        }
        private static byte[] RequirePng(byte[] bytes, string name) => bytes != null && bytes.Length > 0 ? bytes : throw new InvalidOperationException("画像 '" + name + "' をPNGへ変換できませんでした。");
    }

    internal sealed class MobileTextureSerializer : ITextureSerializer
    {
        private readonly ICollection<string> warnings;
        // 自動で軽量化する's maximum long side for a source, given the default; null when off.
        private readonly Func<Texture, int, int> lightweight;
        // The textures UniVRM copies for the images, when lightweight is on.
        private readonly List<Texture> sources = new List<Texture>();
        internal MobileTextureSerializer(ICollection<string> warnings, Func<Texture, int, int> lightweight = null) { this.warnings = warnings; this.lightweight = lightweight; }
        public bool CanExportAsEditorAssetFile(Texture texture, ExportColorSpace exportColorSpace) => false;
        // Deliberately no AssetImporter writes or SaveAndReimport calls. UniVRM
        // creates disposable conversion textures before invoking the serializer.
        public void ModifyTextureAssetBeforeExporting(Texture texture)
        {
            if (lightweight != null && texture != null && !sources.Contains(texture)) sources.Add(texture);
        }
        public (byte[] bytes, string mime) ExportBytesWithMime(Texture2D texture, ExportColorSpace exportColorSpace) =>
            (MobileTextureEncoder.EncodeConverted(texture, exportColorSpace == ExportColorSpace.sRGB, warnings, Maximum(texture)), "image/png");

        // The copy keeps its source's name and size. Only a copy that matches
        // exactly one source can take that source's smaller size; a baked,
        // combined or ambiguous image keeps the default.
        private int Maximum(Texture2D copy)
        {
            if (lightweight == null || copy == null) return LilToonMobileProfile.DefaultMaximumTextureSize;
            var matches = sources.Where(source => source != null && source.name == copy.name && source.width == copy.width && source.height == copy.height).ToArray();
            return matches.Length == 1 ? lightweight(matches[0], LilToonMobileProfile.DefaultMaximumTextureSize) : LilToonMobileProfile.DefaultMaximumTextureSize;
        }
    }
}
