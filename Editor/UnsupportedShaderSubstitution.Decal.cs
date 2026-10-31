using System;
using System.Collections.Generic;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;

namespace VRVlog.LilToonExporter
{
    // The sectioned toon shader's four decals. Each is an image placed on the
    // mesh UV by position, rotation and scale and blended over the main color
    // before lighting, like lilToon's second and third main layers. The last
    // decals that lilToon's layers draw the same way become those layers; the
    // earlier ones are baked into the main image in their order, so the
    // layers still blend over them. A decal that neither can draw is reported
    // by name.
    internal static partial class UnsupportedShaderSubstitution
    {
        sealed class ToonDecal
        {
            internal int Index;
            internal string Label;
            internal Texture Texture;
            internal Vector2 TextureScale, TextureOffset;
            internal Color Color;
            internal float BlendAlpha, AlphaIntensity;
            internal int Blend;
            internal bool Tiled;
            internal bool Mirrored;
            // Center of rotation and placement on the mesh UV, the angle in
            // radians and the size of the placed image.
            internal Vector2 Center, Size;
            internal float Angle;
            internal Texture Mask;
            internal Vector2 MaskScale, MaskOffset;
            internal int MaskChannel;

            // lilToon's layers multiply the image by one alpha; the source
            // saturates the product with its intensity.
            internal float LayerAlpha => AlphaIntensity * Mathf.Clamp01(BlendAlpha);
        }

        // The source's blend modes and lilToon's layer blend mode for those
        // drawn the same way: replace, multiply, screen and add.
        static readonly int[] ToonDecalBlends = { 0, 1, 2, 5, 6, 7, 8, 9, 20 };

        static int LayerBlendMode(int blend) => blend == 0 ? 0 : blend == 8 ? 1 : blend == 6 ? 2 : blend == 2 ? 3 : -1;

        static void ToonDecals(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            var decals = new List<ToonDecal>();
            for (var i = 0; i < 4; i++)
            {
                var decal = ReadToonDecal(values, i, effects);
                if (decal != null) decals.Add(decal);
            }
            if (decals.Count == 0) return;

            var mainScale = material.GetTextureScale("_MainTex");
            var mainOffset = material.GetTextureOffset("_MainTex");
            // The longest run of layer-drawable decals at the end, two at most.
            var layered = 0;
            while (layered < 2 && layered < decals.Count && Layerable(decals[decals.Count - 1 - layered], material, mainScale, mainOffset)) layered++;
            var baked = decals.GetRange(0, decals.Count - layered);
            if (baked.Count > 0 && !BakeToonDecals(material, baked, textures))
                foreach (var decal in baked) Drop(effects, decal.Label);
            var layers = new[] { "2nd", "3rd" };
            for (var k = 0; k < layered; k++)
                ToonDecalLayer(values, material, decals[baked.Count + k], layers[k], textures, effects);
        }

        // Null when the decal is off, or cannot be drawn and is reported.
        static ToonDecal ReadToonDecal(SourceValues values, int index, List<string> effects)
        {
            var s = index == 0 ? "" : index.ToString();
            var n = "_Decal" + index;
            if (values.Float("_DecalEnabled" + s, 0) <= .5f) return null;
            var label = "Decal " + index;
            if (values.Float("_DecalEmissionStrength" + s, 0) > 0) Drop(effects, label + " Emission");

            var blend = Mathf.RoundToInt(values.Float("_DecalBlendType" + s, 0));
            var symmetry = Mathf.RoundToInt(values.Float("_DecalSymmetryMode" + s, 0));
            bool Moving(string name) => values.TryVector(name, out var pan) && (!Finite(pan) || pan.x != 0 || pan.y != 0);
            // Placement by another UV set, handedness, view parallax or time, a
            // flipped half (whose meaning differs between versions), colors
            // changed by hue, adjustment, theme or video, and alpha taken from
            // the faces, global masks or the decal itself are not drawn. Older
            // versions, without the alpha intensity, number their blend modes
            // differently and are not drawn either.
            if (!values.TryFloat("_DecalAlphaIntensity" + s, out _) || Mathf.RoundToInt(values.Float("_DecalTexture" + s + "UV", 0)) != 0 || Mathf.RoundToInt(values.Float("_DecalMirroredUVMode" + s, 0)) != 0 ||
                (symmetry != 0 && symmetry != 1) || values.Float(n + "Depth", 0) != 0 || values.Float("_DecalRotationSpeed" + s, 0) != 0 ||
                Moving("_DecalTexture" + s + "Pan") || Array.IndexOf(ToonDecalBlends, blend) < 0 ||
                values.Float("_DecalHueShiftEnabled" + s, 0) > .5f || values.Float("_DecalColorAdjustEnabled" + s, 0) > .5f ||
                values.Float(index == 0 ? "_DecalColorThemeIndex" : "_DecalColor" + s + "ThemeIndex", 0) != 0 ||
                values.Float(n + "VideoEnabled", 0) > .5f || values.Float(n + "ChannelSeparationEnable", 0) > .5f ||
                values.Float(n + "FaceMask", 0) != 0 || values.Float(n + "GlobalMask", 0) != 0 || values.Float("_DecalOverrideAlpha" + s, 0) != 0)
            {
                Drop(effects, label);
                return null;
            }

            var decal = new ToonDecal { Index = index, Label = label, Blend = blend, Mirrored = symmetry == 1 };
            if (values.Texture("_DecalTexture" + s, out var texture, out var scale, out var offset) && texture != null)
            {
                if (!Flat(texture) || !Finite((Vector4)scale) || !Finite((Vector4)offset)) { Drop(effects, label); return null; }
                decal.Texture = texture; decal.TextureScale = scale; decal.TextureOffset = offset;
            }
            else { decal.TextureScale = Vector2.one; decal.TextureOffset = Vector2.zero; }
            decal.Color = values.TryVector("_DecalColor" + s, out var color) && Finite(color) ? (Color)color : Color.white;
            decal.BlendAlpha = values.Float("_DecalBlendAlpha" + s, 1);
            decal.AlphaIntensity = values.Float("_DecalAlphaIntensity" + s, 1);
            decal.Tiled = values.Float("_DecalTiled" + s, 0) > .5f;

            // The placed area spans position ± scale / 2 widened by the side
            // offsets (left, right, down, up), and turns around its center.
            var position = values.TryVector("_DecalPosition" + s, out var p) && Finite(p) ? new Vector2(p.x, p.y) : new Vector2(.5f, .5f);
            var size = values.TryVector("_DecalScale" + s, out var sc) && Finite(sc) ? new Vector2(sc.x, sc.y) : Vector2.one;
            var aspect = Mathf.RoundToInt(values.Float(n + "AspectRatioMode", 0));
            // Shrink to Fit narrows the shorter side of the image, Grow to Fit
            // widens the longer one; other modes leave the scale as it is.
            if ((aspect == 1 || aspect == 2) && decal.Texture != null && decal.Texture.width > 0 && decal.Texture.height > 0)
            {
                float w = decal.Texture.width, h = decal.Texture.height;
                var fit = aspect == 1
                    ? (w > h ? new Vector2(1, h / w) : new Vector2(w / h, 1))
                    : (w > h ? new Vector2(w / h, 1) : new Vector2(1, h / w));
                size = Vector2.Scale(size, fit);
            }
            var side = values.TryVector("_DecalSideOffset" + s, out var so) && Finite(so) ? so : Vector4.zero;
            decal.Size = size + new Vector2(side.x + side.y, side.z + side.w);
            decal.Center = position + new Vector2((side.y - side.x) / 2, (side.w - side.z) / 2);
            decal.Angle = values.Float("_DecalRotation" + s, 0) * Mathf.Deg2Rad;
            if (!Finite(decal.Angle) || !Finite(decal.BlendAlpha) || !Finite(decal.AlphaIntensity) ||
                Mathf.Abs(decal.Size.x) < 1e-5f || Mathf.Abs(decal.Size.y) < 1e-5f) { Drop(effects, label); return null; }

            // One RGBA mask for the four decals, each reading its channel; a
            // channel past A reads no mask.
            decal.MaskChannel = Mathf.RoundToInt(values.Float(n + "MaskChannel", index));
            if (decal.MaskChannel >= 0 && decal.MaskChannel <= 3 && values.Texture("_DecalMask", out var mask, out var maskScale, out var maskOffset) && mask != null)
            {
                if (!Flat(mask) || Mathf.RoundToInt(values.Float("_DecalMaskUV", 0)) != 0 || Moving("_DecalMaskPan")) { Drop(effects, label); return null; }
                decal.Mask = mask; decal.MaskScale = maskScale; decal.MaskOffset = maskOffset;
            }
            return decal;
        }

        // lilToon's layer clips the decal after its image's tiling and offset
        // and reads its mask through the main image's tiling and offset, which
        // must not scroll.
        static bool Layerable(ToonDecal decal, Material material, Vector2 mainScale, Vector2 mainOffset) =>
            !decal.Mirrored && LayerBlendMode(decal.Blend) >= 0 && decal.LayerAlpha >= 0 && decal.Color.a * decal.LayerAlpha <= 1 &&
            (decal.Tiled || decal.TextureScale == Vector2.one && decal.TextureOffset == Vector2.zero) &&
            (decal.Mask == null || decal.MaskScale == mainScale && decal.MaskOffset == mainOffset &&
                (!material.HasProperty("_MainTex_ScrollRotate") || material.GetVector("_MainTex_ScrollRotate") == Vector4.zero));

        // The source samples its image at (R(θ)(uv - c) / size + 0.5) × st.xy +
        // st.zw; lilToon at (R(θ)(uv - 0.5) + 0.5) × S + T. They agree for
        // S = st.xy / size and T = 0.5 st.xy + st.zw - 0.5 S - S × R(θ)(c - 0.5).
        static void ToonDecalLayer(SourceValues values, Material material, ToonDecal decal, string layer, List<Texture2D> textures, List<string> effects)
        {
            var scale = new Vector2(decal.TextureScale.x / decal.Size.x, decal.TextureScale.y / decal.Size.y);
            var turned = Rotate(decal.Center - new Vector2(.5f, .5f), decal.Angle);
            var offset = .5f * decal.TextureScale + decal.TextureOffset - .5f * scale - Vector2.Scale(scale, turned);
            Set(material, "_UseMain" + layer + "Tex", 1);
            SetTexture(material, "_Main" + layer + "Tex", decal.Texture, scale, offset);
            material.SetColor("_Color" + layer, new Color(decal.Color.r, decal.Color.g, decal.Color.b, decal.Color.a * decal.LayerAlpha));
            Set(material, "_Main" + layer + "TexAngle", decal.Angle);
            Set(material, "_Main" + layer + "TexIsDecal", decal.Tiled ? 0 : 1);
            Set(material, "_Main" + layer + "TexBlendMode", LayerBlendMode(decal.Blend));
            Set(material, "_Main" + layer + "Tex_UVMode", 0);
            Set(material, "_Main" + layer + "EnableLighting", 1);
            if (decal.Mask != null)
                CopyMask(values, material, "_DecalMask", "_Decal" + decal.Index + "MaskChannel", "", "_Main" + layer + "BlendMask", "_UseMain" + layer + "Tex",
                    textures, effects, decal.Label + " Mask", decal.Index);
        }

        static Vector2 Rotate(Vector2 v, float angle)
        {
            var c = Mathf.Cos(angle); var s = Mathf.Sin(angle);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // The source's blend of a decal color over the base, both linear.
        static Vector3 ToonDecalBlend(Vector3 a, Vector3 b, int mode)
        {
            switch (mode)
            {
                case 1: return Vector3.Min(a, b);
                case 2: return Vector3.Scale(a, b);
                case 5: return Vector3.Max(a, b);
                case 6: return Vector3.one - Vector3.Scale(Vector3.one - a, Vector3.one - b);
                case 7: return Vector3.Max(a - b, Vector3.zero);
                case 8: return a + b;
                case 9:
                    return new Vector3(Overlay(a.x, b.x), Overlay(a.y, b.y), Overlay(a.z, b.z));
                case 20: return a + Vector3.Scale(a, b);
                default: return b;
            }
        }

        static float Overlay(float a, float b) => a < .5f ? 2 * a * b : 1 - 2 * (1 - a) * (1 - b);

        // The main image times the main color with the decals applied in order,
        // over the mesh's 0 to 1 UV square, becomes the new main image and the
        // main color white (its alpha kept). The main image must lie on that
        // square unscrolled and unadjusted, since lilToon adjusts it before the
        // decals the source applies after. False when that cannot be done.
        static bool BakeToonDecals(Material material, List<ToonDecal> decals, List<Texture2D> textures)
        {
            if (textures == null) return false;
            if (material.GetTextureScale("_MainTex") != Vector2.one || material.GetTextureOffset("_MainTex") != Vector2.zero) return false;
            if (material.HasProperty("_MainTex_ScrollRotate") && material.GetVector("_MainTex_ScrollRotate") != Vector4.zero) return false;
            if (material.HasProperty("_MainTexHSVG") && material.GetVector("_MainTexHSVG") != new Vector4(0, 1, 1, 1)) return false;
            if (material.HasProperty("_MainGradationStrength") && material.GetFloat("_MainGradationStrength") > 0 &&
                material.GetTexture("_MainGradationTex") != null) return false;

            var main = material.GetTexture("_MainTex");
            var size = 4;
            if (main != null) size = Mathf.Max(size, main.width, main.height);
            foreach (var decal in decals)
            {
                if (decal.Texture != null) size = Mathf.Max(size, decal.Texture.width, decal.Texture.height);
                if (decal.Mask != null) size = Mathf.Max(size, decal.Mask.width, decal.Mask.height);
            }
            size = Mathf.Min(size, 2048);
            var pixels = main != null ? Sampled(main, size, size) : Filled(size, Color.white);
            if (pixels == null) return false;

            var images = new List<(Color[] pixels, int width, int height)>();
            var masks = new List<Color[]>();
            foreach (var decal in decals)
            {
                if (decal.Texture != null)
                {
                    int w = Mathf.Clamp(decal.Texture.width, 1, 2048), h = Mathf.Clamp(decal.Texture.height, 1, 2048);
                    var image = Sampled(decal.Texture, w, h);
                    if (image == null) return false;
                    images.Add((image, w, h));
                }
                else images.Add((null, 0, 0));
                // The mask laid out on the mesh UV through its tiling and offset.
                if (decal.Mask != null)
                {
                    var mask = Sampled(decal.Mask, size, size, decal.MaskScale, decal.MaskOffset);
                    if (mask == null) return false;
                    masks.Add(mask);
                }
                else masks.Add(null);
            }

            var tint = material.GetColor("_Color");
            var linearTint = tint.linear;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = y * size + x;
                    var uv = new Vector2((x + .5f) / size, (y + .5f) / size);
                    var p = pixels[i];
                    var color = new Vector3(p.r * linearTint.r, p.g * linearTint.g, p.b * linearTint.b);
                    for (var d = 0; d < decals.Count; d++)
                    {
                        var decal = decals[d];
                        var at = uv;
                        if (decal.Mirrored) at.x = Mathf.Abs(at.x - .5f) + .5f;
                        var local = Rotate(at - decal.Center, decal.Angle);
                        var place = new Vector2(local.x / decal.Size.x + .5f, local.y / decal.Size.y + .5f);
                        if (!decal.Tiled && (place.x < 0 || place.x > 1 || place.y < 0 || place.y > 1)) continue;
                        var sample = decal.Color.linear;
                        var sampleAlpha = decal.Color.a;
                        if (decal.Texture != null)
                        {
                            var t = Bilinear(images[d], decal.Texture, Vector2.Scale(place, decal.TextureScale) + decal.TextureOffset);
                            sample = new Color(sample.r * t.r, sample.g * t.g, sample.b * t.b);
                            sampleAlpha *= t.a;
                        }
                        if (masks[d] != null) sampleAlpha *= masks[d][i][decal.MaskChannel];
                        var mixed = Mathf.Clamp01(decal.AlphaIntensity * sampleAlpha * Mathf.Clamp01(decal.BlendAlpha));
                        var decalColor = new Vector3(sample.r, sample.g, sample.b);
                        color = Vector3.Lerp(color, ToonDecalBlend(color, decalColor, decal.Blend), mixed);
                    }
                    var gamma = new Color(Mathf.Clamp01(color.x), Mathf.Clamp01(color.y), Mathf.Clamp01(color.z)).gamma;
                    pixels[i] = new Color(gamma.r, gamma.g, gamma.b, p.a);
                }
            var baked = NewImage(main, "__VRVlog_Decal", size, pixels, textures);
            if (baked == null) return false;
            SetTexture(material, "_MainTex", baked, Vector2.one, Vector2.zero);
            material.SetColor("_Color", new Color(1, 1, 1, tint.a));
            return true;
        }

        static Color[] Filled(int size, Color color)
        {
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
            return pixels;
        }

        // The image read at uv with bilinear filtering and its wrap modes.
        static Color Bilinear((Color[] pixels, int width, int height) image, Texture texture, Vector2 uv)
        {
            var fx = uv.x * image.width - .5f; var fy = uv.y * image.height - .5f;
            var x0 = Mathf.FloorToInt(fx); var y0 = Mathf.FloorToInt(fy);
            var tx = fx - x0; var ty = fy - y0;
            int X(int x) => Wrap(x, image.width, texture.wrapModeU);
            int Y(int y) => Wrap(y, image.height, texture.wrapModeV);
            Color At(int x, int y) => image.pixels[Y(y) * image.width + X(x)];
            return Color.Lerp(Color.Lerp(At(x0, y0), At(x0 + 1, y0), tx), Color.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), tx), ty);
        }

        static int Wrap(int i, int n, TextureWrapMode mode)
        {
            switch (mode)
            {
                case TextureWrapMode.Clamp: return Mathf.Clamp(i, 0, n - 1);
                case TextureWrapMode.Mirror:
                    var m = ((i % (2 * n)) + 2 * n) % (2 * n);
                    return m < n ? m : 2 * n - 1 - m;
                case TextureWrapMode.MirrorOnce: return Mathf.Clamp(i < 0 ? -i - 1 : i, 0, n - 1);
                default: return ((i % n) + n) % n;
            }
        }
    }
}
