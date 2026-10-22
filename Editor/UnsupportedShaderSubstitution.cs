using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UniVRM10;
using VRM10.MToon10;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRVlog.LilToon;
using ShaderPropertyType = UnityEngine.Rendering.ShaderPropertyType;

namespace VRVlog.LilToonExporter
{
    // Materials on shaders outside the official lilToon 2.3.4 set (derived
    // lilToon builds, other toon shaders, built-in or missing shaders) are
    // replaced on the export copy by an official lilToon material that keeps
    // what can be read in common: main image and color, cutout/transparency,
    // normal map, emission and shadow color. The rest of the export then
    // treats them as ordinary lilToon materials.
    internal static class UnsupportedShaderSubstitution
    {
        internal const string Warning = "非対応のシェーダーを公式lilToonで近似しました（主画像・色・明るさ・透過・法線・発光・影色のみ）: ";
        internal const string KeptForExpressions = "表情で色や画像の位置を動かす材質は元のシェーダーのまま書き出しました: ";
        internal const string Failed = "公式lilToonで近似できなかった材質は元のまま書き出しました: ";
        internal const string ColorAdjustmentSkipped = "元のシェーダーの色調補正のうち公式lilToonで再現できないもの（色相・彩度・ガンマ・グラデーション・マスク付きの補正）は反映していません: ";

        const string Opaque = "lilToon";
        const string Cutout = "Hidden/lilToonCutout";
        const string Transparent = "Hidden/lilToonTransparent";
        const string MissingShader = "Hidden/InternalErrorShader";

        enum Mode { Opaque, Cutout, Transparent }

        internal static bool NeedsSubstitute(Material material) =>
            material != null && (material.shader == null || !LilToon234Catalogue.Shaders.ContainsKey(material.shader.name) &&
                material.shader.name != MToon10Meta.UnityShaderName);

        internal static void Apply(GameObject copy, ICollection<Material> created, ICollection<string> warnings,
            ExportFallbackReport fallbacks, Action<Material, Material> track)
        {
            if (copy == null || EditorUtility.IsPersistent(copy))
                throw new ArgumentException("An independent export copy is required.", nameof(copy));
            if (created == null) throw new ArgumentNullException(nameof(created));
            var animated = ExpressionMaterialNames(copy);
            var replacements = new Dictionary<Material, Material>();
            var substituted = new List<string>();
            var kept = new List<string>();
            var failed = new List<string>();
            var colorSkipped = new List<string>();
            foreach (var renderer in ExportRendererSelection.Enumerate(copy))
            {
                // Only renderers that become glTF meshes, as in LilToonFullSnapshot.
                // Particle and trail materials are not exported and are not listed.
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer is MeshRenderer && filter != null ? filter.sharedMesh : null;
                if (!ExportRendererSelection.HasGeometry(mesh)) continue;
                var slots = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < slots.Length; i++)
                {
                    var source = slots[i];
                    if (!NeedsSubstitute(source)) continue;
                    if (!replacements.TryGetValue(source, out var substitute))
                    {
                        // Expression evidence was captured from these materials'
                        // own properties; a derived lilToon cannot export as-is.
                        if (animated.Contains(source.name) && !LooksLikeLilToon(source))
                        {
                            if (!kept.Contains(source.name)) kept.Add(source.name);
                            substitute = null;
                        }
                        else
                        {
                            var skipped = false;
                            try { substitute = Create(source, out skipped); }
                            catch (Exception) { substitute = null; }
                            if (substitute == null) { if (!failed.Contains(source.name)) failed.Add(source.name); }
                            else
                            {
                                created.Add(substitute);
                                track?.Invoke(substitute, source);
                                if (!substituted.Contains(source.name)) substituted.Add(source.name);
                                if (skipped && !colorSkipped.Contains(source.name)) colorSkipped.Add(source.name);
                            }
                        }
                        replacements.Add(source, substitute);
                    }
                    if (substitute == null) continue;
                    fallbacks?.Add(ExportFallbackKind.ShaderSubstituted, source.name, copy.transform, renderer.transform);
                    slots[i] = substitute;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = slots;
            }
            if (warnings == null) return;
            if (substituted.Count > 0) warnings.Add(ExporterLocalization.T(Warning) + string.Join(", ", substituted));
            if (colorSkipped.Count > 0) warnings.Add(ExporterLocalization.T(ColorAdjustmentSkipped) + string.Join(", ", colorSkipped));
            if (kept.Count > 0) warnings.Add(ExporterLocalization.T(KeptForExpressions) + string.Join(", ", kept));
            if (failed.Count > 0) warnings.Add(ExporterLocalization.T(Failed) + string.Join(", ", failed));
        }

        static HashSet<string> ExpressionMaterialNames(GameObject copy)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (!copy.TryGetComponent<Vrm10Instance>(out var instance) || instance.Vrm == null || instance.Vrm.Expression == null) return names;
            var expression = instance.Vrm.Expression;
            foreach (var entry in expression.Clips)
            {
                if (entry.Clip == null) continue;
                foreach (var binding in entry.Clip.MaterialColorBindings ?? Array.Empty<MaterialColorBinding>()) names.Add(binding.MaterialName ?? "");
                foreach (var binding in entry.Clip.MaterialUVBindings ?? Array.Empty<MaterialUVBinding>()) names.Add(binding.MaterialName ?? "");
            }
            return names;
        }

        // Returns null when no official lilToon shader is available.
        internal static Material Create(Material source) => Create(source, out _);

        // colorSkipped tells that the source adjusted its main color in a way
        // the substitute does not reproduce.
        internal static Material Create(Material source, out bool colorSkipped)
        {
            var values = new SourceValues(source);
            colorSkipped = false;
            return LooksLikeLilToon(source) || values.SavedLilToon(source) ? FromDerived(source, values) : FromGeneric(source, values, out colorSkipped);
        }

        internal static bool LooksLikeLilToon(Material material) =>
            LilToonMaterialReader.IsLilToon(material) || material != null && material.shader != null && material.shader.name != MissingShader &&
            material.HasProperty("_lilToonVersion") && material.HasProperty("_TransparentMode");

        static Material New(Material source, Mode mode, bool outline)
        {
            var name = mode == Mode.Opaque ? Opaque : mode == Mode.Cutout ? Cutout : Transparent;
            var shader = outline ? Shader.Find(name == Opaque ? "Hidden/lilToonOutline" : name + "Outline") : null;
            if (shader == null) shader = Shader.Find(name);
            if (shader == null || !LilToon234Catalogue.Shaders.ContainsKey(shader.name)) return null;
            var material = new Material(shader) { name = source.name };
            Set(material, "_TransparentMode", (int)mode);
            return material;
        }

        // A derived build shares lilToon's property names. Copy every value the
        // official shader also declares with the same kind.
        static Material FromDerived(Material source, SourceValues values)
        {
            // lilToon 2.3.4 RenderingMode: 0 Opaque, 1 Cutout, 2 Transparent,
            // 3 Refraction, 4 RefractionBlur, 5 Fur, 6 FurCutout, 7 FurTwoPass, 8 Gem.
            var declared = values.Float("_TransparentMode", 0);
            var mode = declared == 1 || declared == 6 ? Mode.Cutout : declared == 0 ? Mode.Opaque : Mode.Transparent;
            var coerced = declared != 0 && declared != 1 && declared != 2;
            var outline = source.shader != null && source.shader.name.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0;
            var material = New(source, mode, outline);
            if (material == null) return null;
            var shader = material.shader;
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                var name = shader.GetPropertyName(i);
                if (name == "_TransparentMode") continue;
                // Render state of a coerced mode (refraction, fur, gem) does not
                // match the plain transparent shader; keep its defaults.
                if (coerced && RenderState(name)) continue;
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Texture:
                        if (values.Texture(name, out var texture, out var scale, out var offset) &&
                            (texture == null || texture.dimension == shader.GetPropertyTextureDimension(i)))
                        {
                            material.SetTexture(name, texture);
                            material.SetTextureScale(name, scale); material.SetTextureOffset(name, offset);
                        }
                        break;
                    case ShaderPropertyType.Float: case ShaderPropertyType.Range: case ShaderPropertyType.Int:
                        if (values.TryFloat(name, out var number)) Set(material, name, number);
                        break;
                    case ShaderPropertyType.Color: case ShaderPropertyType.Vector:
                        if (values.TryVector(name, out var vector) && Finite(vector)) material.SetVector(name, vector);
                        break;
                }
            }
            if (!coerced && values.Queue > 0) material.renderQueue = values.Queue;
            return material;
        }

        static bool RenderState(string name) => name.IndexOf("Blend", StringComparison.Ordinal) >= 0 ||
            name.IndexOf("ZWrite", StringComparison.Ordinal) >= 0 || name.IndexOf("ZTest", StringComparison.Ordinal) >= 0 ||
            name.IndexOf("AlphaToMask", StringComparison.Ordinal) >= 0;

        static readonly string[] MainTextures = { "_MainTex", "_BaseMap", "_BaseColorMap", "_MainTexture", "_Albedo", "_AlbedoMap", "_Diffuse", "_DiffuseMap", "_BaseTex" };
        static readonly string[] MainColors = { "_BaseColor", "_Color", "_MainColor", "_TintColor" };
        static readonly string[] Cutoffs = { "_Cutoff", "_AlphaCutoff", "_Clipping_Level", "_AlphaClipThreshold", "_Cutout" };
        static readonly string[] NormalTextures = { "_BumpMap", "_NormalMap", "_NormalTex" };
        static readonly string[] NormalScales = { "_BumpScale", "_NormalScale", "_NormalMapScale" };
        static readonly string[] EmissionTextures = { "_EmissionMap", "_EmissiveMap", "_Emissive_Tex", "_EmissionTex", "_EmissiveTex", "_Emission" };
        static readonly string[] EmissionColors = { "_EmissionColor", "_Emissive_Color", "_EmissiveColor" };
        static readonly string[] EmissionToggles = { "_EnableEmission", "_UseEmission", "_EmissionEnabled", "_ES_Enable" };
        static readonly string[] EmissionKeywords = { "_EMISSION", "_ES_ENABLE" };
        static readonly string[] EmissionStrengths = { "_EmissionStrength", "_EmissionIntensity" };
        static readonly string[] ShadeColors = { "_ShadeColor", "_1st_ShadeColor", "_ShadowColor" };
        static readonly string[] ShadeTextures = { "_ShadeTexture", "_1st_ShadeMap", "_ShadowColorTex" };

        static Material FromGeneric(Material source, SourceValues values, out bool colorSkipped)
        {
            colorSkipped = false;
            var mode = GenericMode(source, values);
            var material = New(source, mode, outline: false);
            if (material == null) return null;

            Texture main = null; var mainScale = Vector2.one; var mainOffset = Vector2.zero;
            foreach (var name in MainTextures)
                if (values.Texture(name, out var texture, out var scale, out var offset) && Flat(texture))
                { main = texture; mainScale = scale; mainOffset = offset; break; }
            if (main != null)
            {
                material.SetTexture("_MainTex", main);
                material.SetTextureScale("_MainTex", mainScale); material.SetTextureOffset("_MainTex", mainOffset);
            }
            var color = Color.white;
            foreach (var name in MainColors)
                if (values.TryVector(name, out var vector) && Finite(vector)) { color = vector; break; }
            material.SetColor("_Color", color);
            colorSkipped = ApplyColorAdjustment(values, material);
            if (mode == Mode.Cutout)
                foreach (var name in Cutoffs)
                    if (values.TryFloat(name, out var cutoff)) { Set(material, "_Cutoff", Mathf.Clamp01(cutoff)); break; }
            // lilToon clips by _Cutoff in every mode; blended sources do not clip.
            if (mode == Mode.Transparent) Set(material, "_Cutoff", 0);
            // lilToon's transparent shader writes depth by default; follow the source.
            if (mode == Mode.Transparent) Set(material, "_ZWrite", WritesDepth(source, values) ? 1 : 0);

            foreach (var name in NormalTextures)
                if (values.Texture(name, out var normal, out var scale, out var offset) && Flat(normal))
                {
                    material.SetTexture("_BumpMap", normal);
                    material.SetTextureScale("_BumpMap", scale); material.SetTextureOffset("_BumpMap", offset);
                    Set(material, "_UseBumpMap", 1);
                    foreach (var scaleName in NormalScales)
                        if (values.TryFloat(scaleName, out var strength)) { Set(material, "_BumpScale", strength); break; }
                    break;
                }

            ApplyEmission(source, values, material);
            ApplyShade(values, material, main, color);

            foreach (var name in new[] { "_Cull", "_CullMode" })
                if (values.TryFloat(name, out var cull) && (cull == 0 || cull == 1 || cull == 2)) { Set(material, "_Cull", cull); break; }
            if (source.shader != null && source.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0) Set(material, "_AsUnlit", 1);
            return material;
        }

        static Mode GenericMode(Material source, SourceValues values)
        {
            if (values.Keyword("_ALPHABLEND_ON") || values.Keyword("_ALPHAPREMULTIPLY_ON") || values.Keyword("_SURFACE_TYPE_TRANSPARENT") ||
                values.Float("_Surface", 0) == 1 || values.Float("_Mode", 0) >= 2 || values.Float("_ClippingMode", 0) == 2) return Mode.Transparent;
            if (values.Keyword("_ALPHATEST_ON") || values.Float("_AlphaClip", 0) > .5f || values.Float("_Mode", 0) == 1 ||
                values.Float("_ClippingMode", 0) == 1) return Mode.Cutout;
            var name = source.shader == null ? "" : source.shader.name;
            if (Contains(name, "Cutout")) return Mode.Cutout;
            if (Contains(name, "TransClipping") || Contains(name, "Transparent") || Contains(name, "Fade")) return Mode.Transparent;
            if (Contains(name, "Clipping") || Contains(name, "AlphaTest")) return Mode.Cutout;
            var queue = values.Queue;
            if (queue > 2500 && (!values.TryFloat("_DstBlend", out var destination) || destination != 0)) return Mode.Transparent;
            if (queue >= 2450 && queue <= 2500 && Cutoffs.Any(cutoff => values.TryFloat(cutoff, out _))) return Mode.Cutout;
            return Mode.Opaque;
        }

        // Whether a blended source writes depth. A declared _ZWrite holds the
        // authored value; otherwise the ZWrite command in the shader file
        // decides. Built-in and missing shaders have no file to read, and
        // Unity's built-in blended shaders do not write depth.
        static bool WritesDepth(Material source, SourceValues values)
        {
            if (values.TryFloat("_ZWrite", out var declared)) return declared > .5f;
            var path = source.shader == null || source.shader.name == MissingShader ? null : AssetDatabase.GetAssetPath(source.shader);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) return false;
            // GetFullPath resolves a package path to the folder it is installed in.
            path = Path.GetFullPath(path);
            return File.Exists(path) && ShaderWritesDepth(File.ReadAllText(path), name => values.Float(name, 0));
        }

        static readonly Regex Comments = new Regex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline);
        static readonly Regex ZWriteCommand = new Regex(@"\bZWrite\s+(On|Off|\[\s*(\w+)\s*\])", RegexOptions.IgnoreCase);
        static readonly Regex DepthOnlyPass = new Regex(@"""LightMode""\s*=\s*""(ShadowCaster|Meta|DepthOnly|DepthNormals|MotionVectors)""", RegexOptions.IgnoreCase);

        // ShaderLab of the first SubShader (the one Unity uses on desktop
        // hardware): a pass that draws color writes depth when its own ZWrite,
        // else the SubShader's, says On; Unity's default without a command is
        // On. Shadow and depth-only passes do not draw color. The color passes a
        // surface shader generates take the SubShader command, even when the
        // shader also writes its own shadow pass.
        internal static bool ShaderWritesDepth(string text, Func<string, float> property)
        {
            text = Comments.Replace(text, "");
            var start = Regex.Match(text, @"\bSubShader\s*\{");
            if (!start.Success) return false;
            var subShader = Block(text, start.Index + start.Length - 1);
            var outside = new System.Text.StringBuilder();
            var passes = new List<string>();
            var at = 0;
            foreach (Match pass in Regex.Matches(subShader, @"\bPass\s*\{"))
            {
                if (pass.Index < at) continue;
                var body = Block(subShader, pass.Index + pass.Length - 1);
                outside.Append(subShader, at, pass.Index - at);
                passes.Add(body);
                at = pass.Index + pass.Length - 1 + body.Length;
            }
            outside.Append(subShader, at, subShader.Length - at);
            bool? Command(string scope)
            {
                var match = ZWriteCommand.Match(scope);
                if (!match.Success) return null;
                return match.Groups[2].Success ? property(match.Groups[2].Value) > .5f : match.Groups[1].Value.Equals("On", StringComparison.OrdinalIgnoreCase);
            }
            var shared = Command(outside.ToString());
            if (Regex.IsMatch(outside.ToString(), @"#pragma\s+surface\b") && (shared ?? false)) return true;
            if (passes.Count == 0) return shared ?? false;
            return passes.Where(pass => !DepthOnlyPass.IsMatch(pass)).Any(pass => Command(pass) ?? shared ?? true);
        }

        // The text from an opening brace to its closing brace, both included.
        static string Block(string text, int open)
        {
            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
            }
            return text.Substring(open);
        }

        // Main-color adjustments of other toon shaders. lilToon's tone
        // correction (_MainTexHSVG: hue add, saturation and value multipliers,
        // gamma) works in HSV, so only a uniform brightness factor has a
        // faithful counterpart: scaling the HSV value multiplies every channel
        // alike. LilToonMainTextureBaker bakes it into the exported image.
        // Hue rotations around the grey axis, luma-based saturation, output
        // gamma or brightness fixes, gradation maps and masked adjustments use
        // other formulas; they are left out and reported (returns true).
        // Neutral values leave _MainTexHSVG at its default, so nothing is baked.
        static bool ApplyColorAdjustment(SourceValues values, Material material)
        {
            var value = 1f;
            var skipped = false;
            // Brightness factor 0-1 multiplied into the main color, in shaders
            // that also carry output-stage gamma and brightness fixes.
            if (values.TryFloat("_Bright", out var bright) &&
                (values.TryFloat("_EnableGammaFix", out _) || values.TryFloat("_EnableBlightFix", out _)))
            {
                value *= Mathf.Clamp01(bright);
                if (values.Float("_EnableGammaFix", 0) > .5f &&
                    (values.Float("_GammaR", 1) != 1 || values.Float("_GammaG", 1) != 1 || values.Float("_GammaB", 1) != 1)) skipped = true;
                if (values.Float("_EnableBlightFix", 0) > .5f &&
                    (values.Float("_BlightOutput", 1) != 1 || values.Float("_BlightOffset", 0) != 0)) skipped = true;
            }
            // Color-adjust section applied after the main color:
            // saturate(color * (1 + brightness)), with a luma-lerp saturation,
            // a hue shift, a gradation map and optional masks.
            if (values.Float("_MainColorAdjustToggle", 0) > .5f)
            {
                var masked = values.Texture("_MainColorAdjustTexture", out var mask, out _, out _) && mask != null ||
                    values.Float("_MainBrightnessGlobalMask", 0) > 0;
                if (values.TryFloat("_MainBrightness", out var brightness) && brightness != 0)
                {
                    if (masked) skipped = true;
                    else value *= Mathf.Max(0, 1 + brightness);
                }
                if (values.Float("_Saturation", 0) != 0) skipped = true;
                if (values.Float("_MainHueShiftToggle", 0) > .5f &&
                    (values.Float("_MainHueShift", 0) != 0 || values.Float("_MainHueShiftSpeed", 0) != 0)) skipped = true;
                if (values.Float("_MainGradationStrength", 0) > 0 && values.Float("_ColorGradingToggle", 1) > .5f) skipped = true;
            }
            // Hue rotation in radians around the grey axis. A whole turn (the
            // slider maximum is 6.283185, just under 2π) changes nothing.
            var turn = Mathf.Repeat(values.Float("_HueShift", 0), 2 * Mathf.PI);
            if (values.Keyword("USE_HUE_SHIFT") && turn > 1e-4f && turn < 2 * Mathf.PI - 1e-4f) skipped = true;
            if (value != 1) material.SetVector("_MainTexHSVG", new Vector4(0, 1, value, 1));
            return skipped;
        }

        static void ApplyEmission(Material source, SourceValues values, Material material)
        {
            Texture texture = null; var scale = Vector2.one; var offset = Vector2.zero;
            foreach (var name in EmissionTextures)
                if (values.Texture(name, out var value, out var valueScale, out var valueOffset) && Flat(value))
                { texture = value; scale = valueScale; offset = valueOffset; break; }
            var hasColor = false; var color = Color.black; string colorName = null;
            foreach (var name in EmissionColors)
                if (values.TryVector(name, out var vector) && Finite(vector)) { color = vector; hasColor = true; colorName = name; break; }
            // Colors are saved in sRGB and the source shader scales the linear
            // value, as the MToon fallback reads it (_EmissionColor.linear).
            foreach (var name in EmissionStrengths)
                if (values.TryFloat(name, out var strength) && strength >= 0)
                {
                    var scaled = (color.linear * strength).gamma;
                    color = new Color(scaled.r, scaled.g, scaled.b, color.a);
                    break;
                }
            bool enabled;
            var toggle = EmissionToggles.FirstOrDefault(name => values.TryFloat(name, out _));
            var keywords = EmissionKeywords.Where(name => values.DeclaresKeyword(source, name)).ToArray();
            if (toggle != null) enabled = values.Float(toggle, 0) > .5f;
            else if (keywords.Length > 0) enabled = keywords.Any(values.Keyword);
            // Without a switch, an image or an authored color tells that the
            // surface glows; the shader's own default color does not.
            else enabled = texture != null || hasColor && !values.IsDefault(source, colorName);
            if (hasColor && color.maxColorComponent <= 0) enabled = false;
            if (!enabled) return;
            // Emission tinted by the base color instead of an image; lilToon
            // multiplies by the albedo the same way with _EmissionMainStrength.
            if (values.Float("_EmissionBaseColorAsMap", 0) > .5f) Set(material, "_EmissionMainStrength", 1);
            if (texture != null)
            {
                material.SetTexture("_EmissionMap", texture);
                material.SetTextureScale("_EmissionMap", scale); material.SetTextureOffset("_EmissionMap", offset);
            }
            material.SetColor("_EmissionColor", hasColor ? color : Color.white);
            Set(material, "_UseEmission", 1);
        }

        // lilToon multiplies its shadow color by the lit color unless a shadow
        // texture is given. A shade texture equal to the main image therefore
        // becomes a ratio to the main color.
        static void ApplyShade(SourceValues values, Material material, Texture main, Color mainColor)
        {
            var hasColor = false; var shade = Color.white;
            foreach (var name in ShadeColors)
                if (values.TryVector(name, out var vector) && Finite(vector)) { shade = vector; hasColor = true; break; }
            Texture texture = null;
            foreach (var name in ShadeTextures)
                if (values.Texture(name, out var value, out _, out _) && Flat(value)) { texture = value; break; }
            if (!hasColor && texture == null) return;
            Set(material, "_UseShadow", 1);
            if (texture != null && texture != main)
            {
                material.SetTexture("_ShadowColorTex", texture);
                material.SetColor("_ShadowColor", new Color(shade.r, shade.g, shade.b, 1));
            }
            else
            {
                float Ratio(float value, float basis) => Mathf.Clamp01(basis > .0001f ? value / basis : value);
                material.SetColor("_ShadowColor", new Color(Ratio(shade.r, mainColor.r), Ratio(shade.g, mainColor.g), Ratio(shade.b, mainColor.b), 1));
            }
            if (values.TryFloat("_BaseColor_Step", out var step)) Set(material, "_ShadowBorder", Mathf.Clamp01(step));
            else if (values.TryFloat("_ShadeShift", out var shift)) Set(material, "_ShadowBorder", Mathf.Clamp01(.5f - shift * .5f));
            if (values.TryFloat("_BaseShade_Feather", out var feather)) Set(material, "_ShadowBlur", Mathf.Clamp01(feather));
            else if (values.TryFloat("_ShadeToony", out var toony)) Set(material, "_ShadowBlur", Mathf.Clamp01(1 - toony));
        }

        // Every slot the generic mapping fills is a 2D slot.
        static bool Flat(Texture texture) => texture != null && texture.dimension == TextureDimension.Tex2D;
        static bool Contains(string value, string part) => value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector4 value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);

        static void Set(Material material, string name, float value)
        {
            if (!Finite(value) || !material.HasProperty(name)) return;
            var shader = material.shader;
            var index = shader.FindPropertyIndex(name);
            if (index >= 0 && shader.GetPropertyType(index) == ShaderPropertyType.Int) material.SetInteger(name, Mathf.RoundToInt(value));
            else material.SetFloat(name, value);
        }

        // Values of the source material. A missing shader no longer declares
        // its properties, so they are read from the saved material data.
        sealed class SourceValues
        {
            readonly Dictionary<string, (Texture Texture, Vector2 Scale, Vector2 Offset)> textures = new Dictionary<string, (Texture, Vector2, Vector2)>(StringComparer.Ordinal);
            readonly Dictionary<string, float> floats = new Dictionary<string, float>(StringComparer.Ordinal);
            readonly Dictionary<string, Vector4> vectors = new Dictionary<string, Vector4>(StringComparer.Ordinal);
            readonly HashSet<string> keywords = new HashSet<string>(StringComparer.Ordinal);
            internal readonly int Queue;

            internal SourceValues(Material material)
            {
                var shader = material.shader;
                if (shader != null && shader.name != MissingShader)
                {
                    for (var i = 0; i < shader.GetPropertyCount(); i++)
                    {
                        var name = shader.GetPropertyName(i);
                        switch (shader.GetPropertyType(i))
                        {
                            case ShaderPropertyType.Texture:
                                textures[name] = (material.GetTexture(name), material.GetTextureScale(name), material.GetTextureOffset(name)); break;
                            case ShaderPropertyType.Float: case ShaderPropertyType.Range: floats[name] = material.GetFloat(name); break;
                            case ShaderPropertyType.Int: floats[name] = material.GetInteger(name); break;
                            case ShaderPropertyType.Color: case ShaderPropertyType.Vector: vectors[name] = material.GetVector(name); break;
                        }
                    }
                    foreach (var keyword in material.shaderKeywords) keywords.Add(keyword);
                    Queue = material.renderQueue;
                    return;
                }
                using (var serialized = new SerializedObject(material))
                {
                    var saved = serialized.FindProperty("m_SavedProperties");
                    var entries = saved?.FindPropertyRelative("m_TexEnvs");
                    for (var i = 0; entries != null && i < entries.arraySize; i++)
                    {
                        var entry = entries.GetArrayElementAtIndex(i);
                        var value = entry.FindPropertyRelative("second");
                        textures[entry.FindPropertyRelative("first").stringValue] = (value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture,
                            value.FindPropertyRelative("m_Scale").vector2Value, value.FindPropertyRelative("m_Offset").vector2Value);
                    }
                    entries = saved?.FindPropertyRelative("m_Floats");
                    for (var i = 0; entries != null && i < entries.arraySize; i++)
                    {
                        var entry = entries.GetArrayElementAtIndex(i);
                        floats[entry.FindPropertyRelative("first").stringValue] = entry.FindPropertyRelative("second").floatValue;
                    }
                    entries = saved?.FindPropertyRelative("m_Ints");
                    for (var i = 0; entries != null && i < entries.arraySize; i++)
                    {
                        var entry = entries.GetArrayElementAtIndex(i);
                        floats[entry.FindPropertyRelative("first").stringValue] = entry.FindPropertyRelative("second").intValue;
                    }
                    entries = saved?.FindPropertyRelative("m_Colors");
                    for (var i = 0; entries != null && i < entries.arraySize; i++)
                    {
                        var entry = entries.GetArrayElementAtIndex(i);
                        vectors[entry.FindPropertyRelative("first").stringValue] = entry.FindPropertyRelative("second").colorValue;
                    }
                    foreach (var list in new[] { "m_ValidKeywords", "m_InvalidKeywords" })
                    {
                        var names = serialized.FindProperty(list);
                        for (var i = 0; names != null && i < names.arraySize; i++) keywords.Add(names.GetArrayElementAtIndex(i).stringValue);
                    }
                    Queue = serialized.FindProperty("m_CustomRenderQueue")?.intValue ?? -1;
                }
            }

            internal bool Texture(string name, out Texture texture, out Vector2 scale, out Vector2 offset)
            {
                // Render textures and other dynamic images cannot be saved in the
                // file; such a slot is treated as empty.
                if (textures.TryGetValue(name, out var value) && (value.Texture == null || value.Texture is Texture2D || value.Texture is Cubemap))
                {
                    texture = value.Texture; scale = value.Scale; offset = value.Offset;
                    return Finite(scale.x) && Finite(scale.y) && Finite(offset.x) && Finite(offset.y);
                }
                texture = null; scale = Vector2.one; offset = Vector2.zero;
                return false;
            }

            internal bool TryFloat(string name, out float value) => floats.TryGetValue(name, out value) && Finite(value);
            internal float Float(string name, float fallback) => TryFloat(name, out var value) ? value : fallback;
            internal bool TryVector(string name, out Vector4 value) => vectors.TryGetValue(name, out value);
            internal bool Keyword(string name) => keywords.Contains(name);

            // A derived lilToon whose shader is missing still has lilToon's
            // values in the saved data; its rendering mode is read from them.
            internal bool SavedLilToon(Material material) =>
                (material.shader == null || material.shader.name == MissingShader) &&
                TryFloat("_lilToonVersion", out _) && TryFloat("_TransparentMode", out _);

            // Whether a color still has the value the live shader declares. A
            // missing shader has no declaration, so nothing counts as default.
            internal bool IsDefault(Material material, string name)
            {
                if (name == null || material.shader == null || material.shader.name == MissingShader || !vectors.TryGetValue(name, out var value)) return false;
                var index = material.shader.FindPropertyIndex(name);
                return index >= 0 && material.shader.GetPropertyDefaultVectorValue(index) == value;
            }

            // A live shader tells whether the keyword exists; saved data only
            // lists keywords that were on, which is still the authored state.
            internal bool DeclaresKeyword(Material material, string name)
            {
                if (material.shader == null || material.shader.name == MissingShader) return keywords.Contains(name);
                return material.shader.keywordSpace.FindKeyword(name).isValid;
            }
        }
    }
}
