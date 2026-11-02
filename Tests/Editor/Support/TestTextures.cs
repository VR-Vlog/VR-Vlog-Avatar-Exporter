using System;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // Images the tests make and the colors they compare.
    static class TestTextures
    {
        // A 2x2 linear image with point sampling, pixels from the bottom left.
        public static Texture2D Image(params Color[] pixels)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        public static Texture2D Solid(Color color) => Image(color, color, color, color);

        public static Texture2D Image(int width, int height, bool linear, Func<int, Color32> pixel)
        {
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            image.SetPixels32(Enumerable.Range(0, width * height).Select(pixel).ToArray());
            image.Apply(false, false);
            return image;
        }

        // A color as the project's color space stores it.
        public static Color Linear(Color pixel) => QualitySettings.activeColorSpace == ColorSpace.Linear ? pixel.linear : pixel;
    }
}
