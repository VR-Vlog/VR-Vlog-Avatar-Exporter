using System;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Draws into a temporary render texture and reads the pixels back. The
    // active render texture (and the sRGB write state where it is changed) is
    // restored and the temporary released even when drawing or reading fails.
    internal static class GpuReadback
    {
        // A bake material drawn into an sRGB target, read back as an RGBA32
        // image without mipmaps that samples like the given image. The caller
        // owns the result; it is destroyed here only when reading fails.
        internal static Texture2D BakeRgba32(int width, int height, string name, Texture sampling, Action<RenderTexture> blit)
        {
            var previous = RenderTexture.active;
            var previousSrgb = GL.sRGBWrite;
            RenderTexture target = null;
            Texture2D result = null;
            try
            {
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                blit(target);
                RenderTexture.active = target;
                result = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                {
                    name = name,
                    wrapModeU = sampling?.wrapModeU ?? TextureWrapMode.Repeat,
                    wrapModeV = sampling?.wrapModeV ?? TextureWrapMode.Repeat,
                    filterMode = sampling?.filterMode ?? FilterMode.Bilinear
                };
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                result.Apply(false, false);
                var baked = result;
                result = null;
                return baked;
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }

        // The pixels are read back at once: compile the pass now instead of
        // drawing the Editor's placeholder while it compiles.
        internal static void BlitCompiled(Texture source, RenderTexture target, Material material, int pass)
        {
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try { Graphics.Blit(source, target, material, pass); }
            finally { ShaderUtil.allowAsyncCompilation = asyncCompilation; }
        }

#if !EXPORTER_BAKE_TESTS
        // The pixels of a linear target as read without color conversion.
        // GL.sRGBWrite is left to the caller.
        internal static Color[] LinearPixels(int width, int height, RenderTextureFormat targetFormat, TextureFormat format,
            Action<RenderTexture> blit)
        {
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 0, targetFormat, RenderTextureReadWrite.Linear);
            Texture2D readable = null;
            try
            {
                blit(target);
                RenderTexture.active = target;
                readable = new Texture2D(width, height, format, false, true);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
            }
        }
#endif
    }
}
