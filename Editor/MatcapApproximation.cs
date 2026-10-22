using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // MToon adds MatcapColor * MatcapTexture over the whole mesh. lilToon 2.3.4
    // also masks the MatCap, can tint it by the main color, and can blend it
    // as screen, normal or multiply. The mask and tint are baked into MToon's
    // rim multiply image (which also scales its MatCap), and the darkening of
    // normal and multiply MatCaps into the main image.
    internal static class MatcapApproximation
    {
        internal const int Normal = 0, Add = 1, Screen = 2, Multiply = 3;

        internal static bool Enabled(Material material) =>
            material.HasProperty("_UseMatCap") && material.GetFloat("_UseMatCap") > 0.5f;

        internal static int Mode(Material material) =>
            Mathf.RoundToInt(material.HasProperty("_MatCapBlendMode") ? material.GetFloat("_MatCapBlendMode") : Add);

        // Weight for MToon's added MatCap. Returns null when MToon already adds
        // it everywhere at full weight, as lilToon does.
        internal static Texture2D Weight(Material material, List<Texture2D> owned)
        {
            if (owned == null || !Enabled(material) || Mode(material) == Multiply) return null;
            var mask = Mask(material);
            var mainStrength = Mathf.Clamp01(Float(material, "_MatCapMainStrength", 0f));
            var screen = Mode(material) == Screen;
            if (mask == null && mainStrength <= 0f && !screen) return null;
            return Bake(material, 0, mask, owned, "_MatCap_", baker => baker.SetFloat("_VrvScreen", screen ? 1f : 0f));
        }

        // Main image darkened by a normal or multiply MatCap; null otherwise.
        internal static Texture2D Darkened(Material material, List<Texture2D> owned)
        {
            if (owned == null || !Enabled(material)) return null;
            var mode = Mode(material);
            if (mode != Normal && mode != Multiply) return null;
            var color = material.HasProperty("_MatCapColor") ? material.GetColor("_MatCapColor") : Color.white;
            var strength = Mathf.Clamp01(Float(material, "_MatCapBlend", 1f)) * Mathf.Clamp01(color.a);
            if (!(strength > 0f)) return null;
            var mean = Mean(material);
            var linear = color.linear;
            // Normal replaces dst by src, so it darkens by a; the added src is
            // MToon's own MatCap. Multiply darkens by a * (1 - src).
            var product = mode == Multiply
                ? new Vector4(linear.r * mean.Product.x, linear.g * mean.Product.y, linear.b * mean.Product.z, 0f) * strength
                : Vector4.zero;
            // A transparent MatCap, or a white one that multiplies, changes nothing.
            var alpha = mean.Alpha * strength;
            if (Float(material, "_MatCapMainStrength", 0f) <= 0f &&
                Math.Max(Math.Abs(alpha - product.x), Math.Max(Math.Abs(alpha - product.y), Math.Abs(alpha - product.z))) < 1f / 512f) return null;
            return Bake(material, 1, Mask(material), owned, "_MatCapBase_", baker =>
            {
                baker.SetFloat("_VrvMeanAlpha", mean.Alpha * strength);
                baker.SetVector("_VrvMeanProduct", product);
            });
        }

        // Mean of a and a * rgb of the MatCap image over the disk lilToon
        // reads, in linear space. lilToon applies _MatCapTex_ST to the view
        // normal in [-1, 1] before mapping it to [0, 1].
        private static (float Alpha, Vector3 Product) Mean(Material material)
        {
            const int size = 32;
            var texture = Texture(material, "_MatCapTex") ?? Texture2D.whiteTexture;
            var scale = material.HasProperty("_MatCapTex") ? material.GetTextureScale("_MatCapTex") : Vector2.one;
            var offset = material.HasProperty("_MatCapTex") ? material.GetTextureOffset("_MatCapTex") : Vector2.zero;
            var previous = RenderTexture.active;
            var previousSrgb = GL.sRGBWrite;
            var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(texture, target, scale, (Vector2.one - scale + offset) * 0.5f);
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                read.Apply(false, false);
                var pixels = read.GetPixels();
                var gamma = QualitySettings.activeColorSpace == ColorSpace.Gamma;
                float alpha = 0f, count = 0f;
                var product = Vector3.zero;
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var u = (x + 0.5f) / size - 0.5f;
                        var v = (y + 0.5f) / size - 0.5f;
                        if (u * u + v * v > 0.25f) continue;
                        var pixel = pixels[y * size + x];
                        if (gamma) pixel = pixel.linear;
                        alpha += pixel.a;
                        product += new Vector3(pixel.r, pixel.g, pixel.b) * pixel.a;
                        count++;
                    }
                return (alpha / count, product / count);
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        private static Texture2D Bake(Material material, int pass, Texture mask, List<Texture2D> owned, string suffix, Action<Material> configure)
        {
            var shader = Shader.Find("Hidden/VRVlog/MatcapApproximation");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("lilToonのベイク用シェーダーを利用できません。");
            var main = Texture(material, "_MainTex") ?? Texture2D.whiteTexture;
            var maskTexture = mask ?? Texture2D.whiteTexture;
            var size = TextureResizePolicy.Size(Math.Max(main.width, maskTexture.width), Math.Max(main.height, maskTexture.height));
            var baker = new Material(shader);
            var previous = RenderTexture.active;
            var previousSrgb = GL.sRGBWrite;
            RenderTexture target = null;
            Texture2D result = null;
            try
            {
                baker.SetColor("_Color", material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white);
                baker.SetTexture("_MatCapBlendMask", maskTexture);
                if (material.HasProperty("_MatCapBlendMask"))
                {
                    baker.SetTextureScale("_MatCapBlendMask", material.GetTextureScale("_MatCapBlendMask"));
                    baker.SetTextureOffset("_MatCapBlendMask", material.GetTextureOffset("_MatCapBlendMask"));
                }
                baker.SetFloat("_MatCapMainStrength", Mathf.Clamp01(Float(material, "_MatCapMainStrength", 0f)));
                configure(baker);
                target = RenderTexture.GetTemporary(size.Item1, size.Item2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(main, target, baker, pass);
                RenderTexture.active = target;
                result = new Texture2D(size.Item1, size.Item2, TextureFormat.RGBA32, false, false)
                {
                    name = material.name + "__VRVlog" + suffix + Guid.NewGuid().ToString("N"),
                    wrapModeU = main.wrapModeU, wrapModeV = main.wrapModeV, filterMode = main.filterMode
                };
                result.ReadPixels(new Rect(0, 0, size.Item1, size.Item2), 0, 0, false);
                result.Apply(false, false);
                owned.Add(result);
                var baked = result;
                result = null; // ownership transferred to the export
                return baked;
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
                if (target != null) RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(baker);
            }
        }

        private static Texture Mask(Material material)
        {
            var mask = Texture(material, "_MatCapBlendMask");
            return mask == Texture2D.whiteTexture ? null : mask;
        }

        private static float Float(Material material, string property, float fallback) => material.HasProperty(property) ? material.GetFloat(property) : fallback;
        private static Texture Texture(Material material, string property) => material.HasProperty(property) ? material.GetTexture(property) : null;
    }
}
