using System.Collections.Generic;
using UnityEngine;
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
            var image = map != null ? map : main != null ? white : null;
            if (image == null) { material.SetColor(colorName, Ratio(color, material.GetColor("_Color"))); return; }
            material.SetTexture(textureName, image);
            material.SetColor(colorName, color);
        }
    }
}
