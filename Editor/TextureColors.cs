using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Reads an image through the GPU, so images that are not readable on the
    // CPU still give their colors. The values are linear, as shaders see them.
    internal static class TextureColors
    {
        // Resamples the image to width x height with bilinear filtering; a
        // height of 1 gives the row a shader samples at v = 0.5.
        internal static Color[] ReadLinear(Texture texture, int width, int height)
        {
            var pixels = GpuReadback.LinearPixels(width, height, RenderTextureFormat.ARGBHalf, TextureFormat.RGBAHalf,
                target => Graphics.Blit(texture, target));
            // A Gamma project does not decode sRGB images when sampling.
            if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
                for (var i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].linear;
            return pixels;
        }
    }
}
