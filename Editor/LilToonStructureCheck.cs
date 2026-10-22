using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRVlog.LilToon;

namespace VRVlog.LilToonExporter
{
    // Compares the installed lilToon shaders with the 2.3.4 catalogue before
    // export: property names, types, runtime texture dimensions, named passes
    // and their LightMode. Values are not compared. Differences of the shaders
    // the avatar uses become warnings (LilToonFullSnapshot leaves them out or
    // approximates the material); only lilToon's baker stops the export. This
    // file has no Unity types so host tests run the comparison; reading
    // shaders is in LilToonStructureCheck.Unity.cs.
    internal static partial class LilToonStructureCheck
    {
        // Exporter bakes main color layers with lilToon's own baker. The baker
        // in the distributed 2.0.0..2.3.4 packages lacks four decal parameters
        // of the tag source, so require the properties every distribution
        // declares instead of the catalogue's source list. Its editor-only
        // texture packing properties are not used by the export.
        internal const string BakerShader = "Hidden/ltsother_baker";
        internal static readonly string[] BakerProperties =
        {
            "_Color", "_MainTex", "_MainTex_ScrollRotate", "_MainTexHSVG", "_MainGradationStrength", "_MainGradationTex", "_MainColorAdjustMask",
            "_UseMain2ndTex", "_Color2nd", "_Main2ndTex", "_Main2ndTexAngle", "_Main2ndTexIsDecal", "_Main2ndTexIsLeftOnly", "_Main2ndTexIsRightOnly",
            "_Main2ndTexShouldCopy", "_Main2ndTexShouldFlipMirror", "_Main2ndTexShouldFlipCopy", "_Main2ndTexIsMSDF", "_Main2ndBlendMask", "_Main2ndTexBlendMode",
            "_UseMain3rdTex", "_Color3rd", "_Main3rdTex", "_Main3rdTexAngle", "_Main3rdTexIsDecal", "_Main3rdTexIsLeftOnly", "_Main3rdTexIsRightOnly",
            "_Main3rdTexShouldCopy", "_Main3rdTexShouldFlipMirror", "_Main3rdTexShouldFlipCopy", "_Main3rdTexIsMSDF", "_Main3rdBlendMask", "_Main3rdTexBlendMode",
            "_AlphaMaskMode", "_AlphaMask", "_AlphaMaskScale", "_AlphaMaskValue",
        };
        internal const int ShownDifferences = 5;

        internal enum Kind { MissingShader, UnknownProperty, MissingProperty, PropertyType, TextureDimension, MissingPass, UnknownPass, PassLightMode }

        internal sealed class Difference
        {
            internal readonly Kind Kind; internal readonly string Shader, Name, Actual, Expected;
            internal Difference(Kind kind, string shader, string name = "", string actual = "", string expected = "")
            { Kind = kind; Shader = shader; Name = name; Actual = actual; Expected = expected; }
            public override string ToString() => Kind + " " + Shader + " " + Name + " " + Actual + " " + Expected;
        }

        // Type is float, color, vector or texture. Dimension is 2D, Cube, 3D,
        // ... for textures. Passes keep Unity's order, including unnamed ones.
        internal sealed class ShaderStructure
        {
            internal readonly string Name;
            internal readonly List<(string Name, string Type, string Dimension)> Properties = new List<(string, string, string)>();
            internal readonly List<(string Name, string LightMode)> Passes = new List<(string, string)>();
            internal ShaderStructure(string name) { Name = name; }
        }

        // asShader names the catalogue entry; an installed shader uses its own name.
        internal static List<Difference> Compare(ShaderStructure shader, string asShader)
        {
            var baker = asShader == BakerShader;
            if (!baker && !LilToon234Catalogue.Shaders.ContainsKey(asShader)) throw new ArgumentException("Not a catalogue shader: " + asShader);
            var differences = new List<Difference>();
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var runtime = new List<string>();
            foreach (var property in shader.Properties)
            {
                // The exporter ignores this placeholder when it reads materials.
                if (property.Name == "_DummyProperty" || !declared.Add(property.Name)) continue;
                if (!LilToon234Catalogue.Properties.TryGetValue(property.Name, out var declaration))
                {
                    differences.Add(new Difference(Kind.UnknownProperty, asShader, property.Name));
                    continue;
                }
                var parts = declaration.Split(':');
                if (parts[1] == "runtime") runtime.Add(property.Name);
                if (property.Type != parts[0])
                    differences.Add(new Difference(Kind.PropertyType, asShader, property.Name, property.Type, parts[0]));
                else if (LilToon234Catalogue.TextureFaces.TryGetValue(property.Name, out var faces) && property.Dimension != (faces == 6 ? "Cube" : "2D"))
                    differences.Add(new Difference(Kind.TextureDimension, asShader, property.Name, property.Dimension, faces == 6 ? "Cube" : "2D"));
            }
            var required = baker ? BakerProperties : LilToon234Catalogue.RequiredProperties[asShader];
            foreach (var name in required)
                if (!declared.Contains(name)) differences.Add(new Difference(Kind.MissingProperty, asShader, name));
            if (baker)
            {
                // Export blits the first pass; the baker's passes are not named.
                if (shader.Passes.Count == 0) differences.Add(new Difference(Kind.MissingShader, asShader));
                return differences;
            }
            // A runtime property of another shader is a structure change here.
            var own = new HashSet<string>(required, StringComparer.Ordinal);
            foreach (var name in runtime)
                if (!own.Contains(name)) differences.Add(new Difference(Kind.UnknownProperty, asShader, name));
            // Count passes as the snapshot records them: GrabPass and lilToon's
            // fake passes have no name or LightMode Never; a repeated name once.
            var passes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pass in shader.Passes)
                if (!string.IsNullOrEmpty(pass.Name) && pass.LightMode != "Never" && !passes.ContainsKey(pass.Name))
                    passes.Add(pass.Name, pass.LightMode ?? "");
            var modes = LilToon234Catalogue.PassLightModes[asShader];
            foreach (var name in LilToon234Catalogue.Passes[asShader])
            {
                if (!passes.TryGetValue(name, out var mode)) differences.Add(new Difference(Kind.MissingPass, asShader, name));
                // Unity may expose interned ShaderTagId names in uppercase.
                else if (!string.Equals(mode, modes[name], StringComparison.OrdinalIgnoreCase))
                    differences.Add(new Difference(Kind.PassLightMode, asShader, name, mode, modes[name]));
            }
            foreach (var name in passes.Keys)
                if (!modes.ContainsKey(name)) differences.Add(new Difference(Kind.UnknownPass, asShader, name));
            return differences;
        }

        // The baker blits with the properties LilToonMainTextureBaker sets, so
        // a missing baker or a missing or retyped baker property stops the
        // export. Extra properties of the baker are harmless.
        internal static bool Stops(Difference difference) =>
            difference.Shader == BakerShader && difference.Kind != Kind.UnknownProperty;

        // translate receives a Japanese format and returns the localized one.
        internal static string Message(IReadOnlyList<Difference> differences, string installedVersion, Func<string, string> translate)
        {
            var version = LilToon234Catalogue.Version;
            var text = new StringBuilder(string.Format(translate("lilToon {0}のベイク用シェーダーが{1}と違うため書き出せません"), installedVersion, version));
            foreach (var difference in differences.Take(ShownDifferences))
                text.Append('\n').Append(string.Format(translate(Template(difference.Kind)),
                    difference.Shader, difference.Name, difference.Actual, difference.Expected, version));
            if (differences.Count > ShownDifferences)
                text.Append('\n').Append(string.Format(translate("ほか{0}件"), differences.Count - ShownDifferences));
            text.Append('\n').Append(string.Format(translate("VCC／ALCOMでlilToonを入れ直すか{0}に切り替える"), version));
            return text.ToString();
        }

        static string Template(Kind kind)
        {
            switch (kind)
            {
                case Kind.MissingShader: return "{0}: シェーダーを読み込めません";
                case Kind.UnknownProperty: return "{0}: プロパティ{1}は{4}のこのシェーダーにありません";
                case Kind.MissingProperty: return "{0}: プロパティ{1}がありません";
                case Kind.PropertyType: return "{0}: プロパティ{1}の型が{2}です（{4}は{3}）";
                case Kind.TextureDimension: return "{0}: 画像プロパティ{1}の種類が{2}です（{4}は{3}）";
                case Kind.MissingPass: return "{0}: パス{1}がありません";
                case Kind.UnknownPass: return "{0}: パス{1}は{4}のこのシェーダーにありません";
                default: return "{0}: パス{1}のLightModeが{2}です（{4}は{3}）";
            }
        }

        // One line per shader and kind for the export result, saying what the
        // export does about it, then the settings that make Unity match.
        internal static IEnumerable<string> Warnings(IReadOnlyList<Difference> differences, Func<string, string> translate)
        {
            var version = LilToon234Catalogue.Version;
            foreach (var group in differences.GroupBy(d => (d.Shader, d.Kind)))
            {
                var items = string.Join(", ", group.Select(d => string.IsNullOrEmpty(d.Actual) ? d.Name : d.Name + " " + d.Actual + "→" + d.Expected));
                yield return string.Format(translate(WarningTemplate(group.Key.Kind)), group.Key.Shader, version, items);
            }
            foreach (var remedy in Remedies(differences))
                yield return string.Format(translate(remedy), version);
        }

        static string WarningTemplate(Kind kind)
        {
            switch (kind)
            {
                case Kind.UnknownProperty: return "{0}: lilToon {1}にないプロパティは書き出しません（{2}）";
                case Kind.MissingProperty: return "{0}: lilToon {1}のプロパティが足りないマテリアルはMToonで近似します（{2}）";
                case Kind.PropertyType: return "{0}: 型がlilToon {1}と違うプロパティがあるマテリアルはMToonで近似します（{2}）";
                case Kind.TextureDimension: return "{0}: 画像の種類がlilToon {1}と違うプロパティがあるマテリアルはMToonで近似します（{2}）";
                case Kind.MissingPass: return "{0}: 足りないパスはlilToon {1}のパスとして書き出します（{2}）";
                case Kind.UnknownPass: return "{0}: lilToon {1}にないパスは書き出しません（{2}）";
                case Kind.PassLightMode: return "{0}: LightModeが違うパスはlilToon {1}のLightModeで書き出します（{2}）";
                default: return "{0}: シェーダーを読み込めません";
            }
        }

        // lilToon's Shader Setting removes the ForwardAdd passes when "Use
        // ForwardAdd" is off and renames SHADOW_CASTER_OUTLINE to SHADOW_CASTER
        // when "Fix for Deffered" is on. The app draws the 2.3.4 passes, so
        // these settings, or the 2.3.4 package itself, make Unity look the same.
        internal static IEnumerable<string> Remedies(IReadOnlyList<Difference> differences)
        {
            bool ForwardAdd(Difference d) => d.Kind == Kind.MissingPass && d.Name.StartsWith("FORWARD_ADD", StringComparison.Ordinal);
            bool Deferred(Difference d) => d.Kind == Kind.MissingPass && d.Name == "SHADOW_CASTER_OUTLINE" ||
                d.Kind == Kind.UnknownPass && d.Name == "SHADOW_CASTER";
            if (differences.Any(ForwardAdd)) yield return "lilToonのシェーダー設定で「ForwardAddパスを使用」をオンにするとUnityでもアプリと同じパスで描画します";
            if (differences.Any(Deferred)) yield return "lilToonのシェーダー設定で「Fix for Deffered」をオフにするとUnityでもアプリと同じパスで描画します";
            if (differences.Any(d => !ForwardAdd(d) && !Deferred(d))) yield return "lilToon {0}に切り替えるとUnityでもアプリと同じ見た目になります";
        }
    }
}
