using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using static VRVlog.LilToonExporter.UnsupportedShaderSubstitution;

namespace VRVlog.LilToonExporter
{
    internal static class ShadeLayers
    {
        // A shade drawn as map × color, with nothing of the base color and
        // white when no map is set. lilToon draws albedo × color unless it has
        // an image, so the flat case takes a white image of its own: the
        // exporter reads Unity's built-in white as an empty slot.
        internal static void Absolute(Material material, string colorName, string textureName, Color color, Texture map, List<Texture2D> textures, ref Texture white)
        {
            var main = material.GetTexture("_MainTex");
            if (map == null && main != null && white == null && textures != null)
            {
                var pixels = new Color[16];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
                white = NewImage(main, "__VRVlog_WhiteShade", 4, 4, pixels, textures, false);
            }
            var image = map != null ? WithoutAlpha(map, textures) : main != null ? white : null;
            if (image == null) { material.SetColor(colorName, Ratio(color, material.GetColor("_Color"))); return; }
            material.SetTexture(textureName, image);
            material.SetColor(colorName, color);
        }

        // lilToon blends its shade image over the base color by the image's
        // alpha. These shaders read only its RGB, so an image with alpha is
        // redrawn opaque. An image that cannot be read stays as it is.
        internal static Texture WithoutAlpha(Texture map, List<Texture2D> textures)
        {
            if (map == null || textures == null || !GraphicsFormatUtility.HasAlphaChannel(map.graphicsFormat)) return map;
            var fit = Mathf.Min(1, 2048f / Mathf.Max(map.width, map.height));
            var width = Mathf.Max(1, Mathf.RoundToInt(map.width * fit));
            var height = Mathf.Max(1, Mathf.RoundToInt(map.height * fit));
            var pixels = Sampled(map, width, height);
            if (pixels == null) return map;
            var opaque = true;
            foreach (var pixel in pixels) if (pixel.a < .998f) { opaque = false; break; }
            if (opaque) return map;
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i].gamma;
                color.a = 1;
                pixels[i] = color;
            }
            return NewImage(map, "__VRVlog_OpaqueShade", width, height, pixels, textures, false);
        }

        // lilToon reads the shade images and the border mask in the main
        // image's UV, so tiling of their own has nowhere to go.
        internal static void ReportOwnUv(Material material, Vector2 scale, Vector2 offset, ICollection<string> dropped, string label)
        {
            if (material.GetTextureScale("_MainTex") != scale || material.GetTextureOffset("_MainTex") != offset) Drop(dropped, label);
        }
    }
}
