using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using VRVlog.LilToon;

namespace VRVlog.LilToonExporter
{
    // Text a user pastes when hair or a body vanishes or turns see-through in
    // the app. It is read from the written VRM, so it shows what the app gets,
    // and it never carries names: meshes and materials are numbered, shader
    // names outside a fixed public list are hidden, and every name the avatar
    // or the VRM holds is masked in the lines taken from the export report.
    internal sealed class ExportDiagnostics
    {
        internal const string QuickTransfer = "かんたん転送";
        internal const string DetailedTransfer = "通常の転送";
        internal const string TransferSave = "転送画面から保存";
        internal const string BoardSave = "表情ボードから保存";

        const string Mask = "*";

        // Shader names starting with one of these are public package names and kept as they are.
        static readonly string[] PublicShaderPrefixes =
        {
            "lilToon", "Hidden/lilToon", "_lil/", "Hidden/ltspass", "Standard", "Unlit/", "Legacy Shaders/", "Mobile/", "Particles/",
            "VRM/", "VRM10/", "UniGLTF/", "Universal Render Pipeline/", "UnityChanToonShader/", "Toon", ".poiyomi/", "Poiyomi",
        };

        // Parts of a shader name that change how a material draws, in the order they are listed.
        static readonly (string Part, string Label)[] ShaderTraits =
        {
            ("Fur", "ファー"), ("Gem", "宝石"), ("Refraction", "屈折"), ("TwoPass", "2パス"), ("OnePass", "1パス"),
            ("OutlineOnly", "輪郭線のみ"), ("Outline", "輪郭線"), ("Cutout", "カットアウト"), ("Transparent", "半透明"),
            ("Overlay", "オーバーレイ"), ("Lite", "Lite"), ("Multi", "Multi"), ("FakeShadow", "影のみ"), ("Tessellation", "テッセレーション"),
        };

        internal static ExportDiagnostics Last { get; private set; }

        readonly byte[] recordedBytes;
        readonly List<string> head = new List<string>();
        readonly List<MeshLine> meshes = new List<MeshLine>();
        readonly List<MaterialLine> materials = new List<MaterialLine>();
        readonly List<string> changes = new List<string>();
        readonly Dictionary<int, byte[]> images = new Dictionary<int, byte[]>();
        readonly Dictionary<int, string> alpha = new Dictionary<int, string>();
        string text;

        sealed class MeshLine
        {
            internal int Vertices, Submeshes, BlendShapes;
            internal string Index = "", Materials = "";
            internal bool Skinned;
        }

        sealed class MaterialLine
        {
            internal string Shader = "", Converted = "", Transparency = "", Queue = "", Sides = "", Textures = "";
            internal int MainImage = -1;
            internal string MainImageText = "";
            internal float Cutoff = 0.5f;
        }

        ExportDiagnostics(byte[] bytes) { recordedBytes = bytes; }

        // Keeps the diagnostics of the latest export. The same bytes recorded
        // twice (the result list, then the board that saved them) keep the first record.
        internal static ExportDiagnostics Record(GameObject source, byte[] bytes, ExportFallbackReport report, IEnumerable<string> warnings, string kind)
        {
            if (bytes == null) return Last;
            if (Last != null && ReferenceEquals(Last.recordedBytes, bytes)) return Last;
            try { Last = Build(source, bytes, report, warnings, kind); }
            catch (Exception error)
            {
                Last = new ExportDiagnostics(bytes);
                Last.head.Add("VR Vlog 書き出し診断");
                Last.head.Add("VRMを読めませんでした: " + error.GetType().Name);
            }
            return Last;
        }

        internal static void Forget() => Last = null;

        internal string Text()
        {
            if (text != null) return text;
            var builder = new StringBuilder();
            foreach (var line in head) builder.AppendLine(line);
            if (meshes.Count > 0)
            {
                builder.AppendLine("[メッシュ]");
                for (var i = 0; i < meshes.Count; i++)
                {
                    var mesh = meshes[i];
                    builder.Append('#').Append(i + 1).Append(" 頂点 ").Append(Number(mesh.Vertices))
                        .Append(" / サブメッシュ ").Append(mesh.Submeshes)
                        .Append(" / ブレンドシェイプ ").Append(mesh.BlendShapes)
                        .Append(" / インデックス ").Append(mesh.Index)
                        .Append(mesh.Skinned ? " / スキンあり" : " / スキンなし")
                        .Append(" / マテリアル ").AppendLine(mesh.Materials);
                }
            }
            if (materials.Count > 0)
            {
                builder.AppendLine("[マテリアル]");
                for (var i = 0; i < materials.Count; i++)
                {
                    var material = materials[i];
                    builder.Append('#').Append(i + 1).Append(" 元 ").Append(material.Shader)
                        .Append(" / 変換後 ").Append(material.Converted)
                        .Append(" / ").Append(material.Transparency)
                        .Append(" / キュー ").Append(material.Queue)
                        .Append(" / ").AppendLine(material.Sides);
                    builder.Append("   メイン ").Append(material.MainImageText);
                    if (material.MainImage >= 0) builder.Append(' ').Append(Alpha(material.MainImage, material.Cutoff));
                    builder.Append(" / テクスチャ ").AppendLine(material.Textures);
                }
            }
            builder.AppendLine("[変換で変えた項目]");
            if (changes.Count == 0) builder.AppendLine("なし");
            foreach (var line in changes) builder.AppendLine(line);
            return text = builder.ToString();
        }

        static ExportDiagnostics Build(GameObject source, byte[] bytes, ExportFallbackReport report, IEnumerable<string> warnings, string kind)
        {
            var diagnostics = new ExportDiagnostics(bytes);
            var glb = GlbDocument.Read(bytes);
            var json = glb.Json;
            var accessors = Array(json, "accessors");
            var gltfMaterials = Array(json, "materials");
            var textures = Array(json, "textures");
            var gltfImages = Array(json, "images");
            var views = Array(json, "bufferViews");

            // Records the exporter keeps beside the portable MToon material, by glTF material index.
            var root = LilToonFullContract.Root(json);
            var full = root != null && Integer(root, "schemaMajor") >= 2;
            var records = new Dictionary<int, Dictionary<string, object>>();
            foreach (var raw in root != null ? Array(root, "materials") : new List<object>())
                if (raw is Dictionary<string, object> record) records[Integer(record, "materialIndex")] = record;
            var fullTextures = full ? Array(root, "textures") : new List<object>();

            // Meshes in node order; a mesh used by several nodes is listed once.
            var meshesJson = Array(json, "meshes");
            var listedMeshes = new HashSet<int>();
            var vertexTotal = 0L;
            foreach (var rawNode in Array(json, "nodes"))
            {
                if (!(rawNode is Dictionary<string, object> node) || !node.ContainsKey("mesh")) continue;
                var meshIndex = Integer(node, "mesh");
                if (!listedMeshes.Add(meshIndex) || meshIndex < 0 || meshIndex >= meshesJson.Count) continue;
                var primitives = Array(meshesJson[meshIndex] as Dictionary<string, object>, "primitives")
                    .OfType<Dictionary<string, object>>().ToList();
                var line = new MeshLine { Submeshes = primitives.Count, Skinned = node.ContainsKey("skin") };
                var positions = new HashSet<int>();
                var indexTypes = new List<string>();
                var used = new List<int>();
                foreach (var primitive in primitives)
                {
                    if (primitive.TryGetValue("attributes", out var rawAttributes) && rawAttributes is Dictionary<string, object> attributes &&
                        attributes.ContainsKey("POSITION") && positions.Add(Integer(attributes, "POSITION")))
                        line.Vertices += Integer(At(accessors, Integer(attributes, "POSITION")), "count");
                    line.BlendShapes = Math.Max(line.BlendShapes, Array(primitive, "targets").Count);
                    var indexType = primitive.ContainsKey("indices") ? IndexFormat(Integer(At(accessors, Integer(primitive, "indices")), "componentType")) : "なし";
                    if (!indexTypes.Contains(indexType)) indexTypes.Add(indexType);
                    if (primitive.ContainsKey("material") && !used.Contains(Integer(primitive, "material"))) used.Add(Integer(primitive, "material"));
                }
                line.Index = string.Join("・", indexTypes);
                line.Materials = used.Count == 0 ? "なし" : string.Join(",", used.Select(index => "#" + (index + 1)));
                vertexTotal += line.Vertices;
                diagnostics.meshes.Add(line);
            }

            // Shader names of the avatar's materials by name, for materials the VRM has no record of.
            var sourceShaders = new Dictionary<string, string>(StringComparer.Ordinal);
            var renderers = source != null ? source.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && !sourceShaders.ContainsKey(material.name))
                        sourceShaders[material.name] = material.shader != null ? material.shader.name : "";

            var textureTotal = new HashSet<int>();
            var largest = 0;
            for (var index = 0; index < gltfMaterials.Count; index++)
            {
                var material = gltfMaterials[index] as Dictionary<string, object> ?? new Dictionary<string, object>();
                records.TryGetValue(index, out var record);
                var line = new MaterialLine();
                string shader;
                if (record != null && record.TryGetValue("sourceShader", out var sourceShader) && sourceShader is string official) shader = official;
                else if (material.TryGetValue("name", out var rawName) && rawName is string name && sourceShaders.TryGetValue(name, out var found)) shader = found;
                else shader = null;
                line.Shader = ShaderText(shader, record);
                line.Converted = record == null ? "MToon" : full ? "lilToon（そのまま）" : "lilToon（モバイル）";
                var mode = material.TryGetValue("alphaMode", out var rawMode) ? rawMode as string ?? "OPAQUE" : "OPAQUE";
                if (mode == "MASK")
                {
                    line.Cutoff = material.TryGetValue("alphaCutoff", out var rawCutoff) ? (float)Numeric(rawCutoff) : 0.5f;
                    line.Transparency = "カットアウト Cutoff " + line.Cutoff.ToString("0.###", CultureInfo.InvariantCulture);
                }
                else line.Transparency = mode == "BLEND" ? "半透明" : "不透明";
                line.Queue = record != null && record.ContainsKey("renderQueue") ? Integer(record, "renderQueue").ToString(CultureInfo.InvariantCulture) : "既定";
                line.Sides = material.TryGetValue("doubleSided", out var rawSides) && rawSides is bool both && both ? "両面" : "片面";

                // Textures the portable material uses, and the ones the lilToon record carries.
                var referenced = new HashSet<int>();
                CollectTextures(material, referenced);
                var sizes = new List<int>();
                foreach (var texture in referenced)
                {
                    textureTotal.Add(texture);
                    var image = ImageOf(textures, texture);
                    if (image >= 0 && image < gltfImages.Count)
                    {
                        var size = ImageSize(gltfImages[image] as Dictionary<string, object>, views, glb.Binary);
                        sizes.Add(Math.Max(size.Item1, size.Item2));
                    }
                }
                var recordTextures = 0;
                if (record != null && full)
                    foreach (var rawTexture in Array(record, "textures"))
                    {
                        if (!(rawTexture is Dictionary<string, object> binding) || Integer(binding, "texture") < 0) continue;
                        recordTextures++;
                        if (At(fullTextures, Integer(binding, "texture")) is Dictionary<string, object> stored)
                            sizes.Add(Math.Max(Integer(stored, "width"), Integer(stored, "height")));
                    }
                var count = referenced.Count + recordTextures;
                largest = Math.Max(largest, sizes.DefaultIfEmpty(0).Max());
                line.Textures = count + (sizes.Count > 0 ? "（最大 " + sizes.Max() + "px）" : "");

                var main = BaseColorImage(material, textures);
                if (main >= 0 && main < gltfImages.Count)
                {
                    var image = gltfImages[main] as Dictionary<string, object>;
                    var size = ImageSize(image, views, glb.Binary);
                    var mime = image != null && image.TryGetValue("mimeType", out var rawMime) ? rawMime as string ?? "" : "";
                    line.MainImageText = (mime == "image/jpeg" ? "JPEG " : mime == "image/png" ? "PNG " : "") + size.Item1 + "x" + size.Item2;
                    if (mime == "image/png")
                    {
                        line.MainImage = main;
                        if (!diagnostics.images.ContainsKey(main)) diagnostics.images[main] = ImageBytes(image, views, glb.Binary);
                    }
                    else line.MainImageText += " アルファなし";
                }
                else line.MainImageText = "なし";
                diagnostics.materials.Add(line);
            }

            var head = diagnostics.head;
            head.Add("VR Vlog 書き出し診断");
            head.Add("Exporter " + LilToonExporterWindow.PackageVersion() + " / Unity " + Application.unityVersion +
                " / lilToon " + (LilToonExporterWindow.InstalledLilToonVersion() ?? "なし"));
            head.Add("書き出し方法 " + (string.IsNullOrEmpty(kind) ? "書き出し" : kind) +
                " / マテリアルの形式 " + (records.Count == 0 ? "MToonのみ" : (full ? "lilToon" : "lilToon（モバイル）") + " " + records.Count + "/" + gltfMaterials.Count));
            head.Add("合計 メッシュ " + diagnostics.meshes.Count + " / 頂点 " + Number(vertexTotal) + " / マテリアル " + gltfMaterials.Count +
                " / テクスチャ " + (textureTotal.Count + fullTextures.Count) + (largest > 0 ? "（最大 " + largest + "px）" : "") +
                " / ファイル " + (bytes.Length / (1024f * 1024f)).ToString("0.0", CultureInfo.InvariantCulture) + "MB");
            if (source != null)
            {
                // Inactive objects and disabled renderers are not written; they only show here as counts.
                var inactive = renderers.Count(renderer => !renderer.gameObject.activeInHierarchy);
                var disabled = renderers.Count(renderer => renderer.gameObject.activeInHierarchy && !renderer.enabled);
                head.Add("元のアバター レンダラー " + renderers.Length + " / 初期で非表示 " + inactive + " / 無効 " + disabled);
            }

            var names = Names(source, json, report);
            var list = ExportResultList.Build(report, warnings);
            head.Add("変換の結果 " + string.Join("・", ExportResultList.Order.Select(outcome => ExportResultList.Label(outcome) + list.Summary[outcome])) +
                "・参考" + list.NoticeCount + (list.MissingShaderCount > 0 ? " / シェーダーなし " + list.MissingShaderCount : ""));
            foreach (var row in list.Rows)
            {
                var line = ExportResultList.Label(row.Outcome) + " [" + names.Apply(row.Group) + "] " + names.Apply(row.Content);
                if (row.Count > 1) line += "（" + row.Count + "件）";
                diagnostics.changes.Add(line);
            }
            return diagnostics;
        }

        // The decoded main texture's alpha, measured when the text is first made.
        string Alpha(int image, float cutoff)
        {
            if (alpha.TryGetValue(image, out var cached)) return cached;
            var result = "アルファ 読めません";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (images.TryGetValue(image, out var data) && data != null && texture.LoadImage(data, false))
                {
                    var pixels = texture.GetPixels32();
                    if (pixels.Length > 0)
                    {
                        byte min = 255; long sum = 0; var below = 0;
                        var threshold = cutoff * 255f;
                        foreach (var pixel in pixels)
                        {
                            if (pixel.a < min) min = pixel.a;
                            sum += pixel.a;
                            if (pixel.a < threshold) below++;
                        }
                        result = min == 255 ? "アルファ すべて不透明" :
                            "アルファ 最小 " + (min / 255f).ToString("0.00", CultureInfo.InvariantCulture) +
                            " 平均 " + (sum / 255f / pixels.Length).ToString("0.00", CultureInfo.InvariantCulture) +
                            " " + cutoff.ToString("0.###", CultureInfo.InvariantCulture) + "未満 " +
                            (100f * below / pixels.Length).ToString("0.0", CultureInfo.InvariantCulture) + "%";
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            images.Remove(image);
            return alpha[image] = result;
        }

        internal static string ShaderText(string shader, Dictionary<string, object> record = null)
        {
            if (string.IsNullOrEmpty(shader))
            {
                // The mobile record names its own family instead of the shader.
                var family = record != null && record.TryGetValue("shaderFamily", out var raw) ? raw as string : null;
                return family != null && Regex.IsMatch(family, "^[A-Za-z0-9_ -]{1,32}$") ? "lilToon（" + family + "）" : "不明";
            }
            if (shader == "Hidden/InternalErrorShader") return "シェーダーなし";
            if (!PublicShaderPrefixes.Any(prefix => shader.StartsWith(prefix, StringComparison.Ordinal))) return "その他（lilToon以外）";
            var traits = new List<string>();
            foreach (var (part, label) in ShaderTraits)
                if (shader.IndexOf(part, StringComparison.Ordinal) >= 0 && !(part == "Outline" && traits.Contains("輪郭線のみ"))) traits.Add(label);
            return shader + (traits.Count > 0 ? "（" + string.Join("・", traits) + "）" : "");
        }

        static string IndexFormat(int componentType)
        {
            switch (componentType)
            {
                case 5121: return "8bit";
                case 5123: return "16bit";
                case 5125: return "32bit";
                default: return "不明";
            }
        }

        static void CollectTextures(object node, HashSet<int> found)
        {
            if (node is Dictionary<string, object> map)
            {
                foreach (var pair in map)
                {
                    if (pair.Key.EndsWith("Texture", StringComparison.Ordinal) && pair.Value is Dictionary<string, object> info && info.ContainsKey("index"))
                        found.Add(Integer(info, "index"));
                    else CollectTextures(pair.Value, found);
                }
            }
            else if (node is List<object> list) foreach (var item in list) CollectTextures(item, found);
        }

        static int BaseColorImage(Dictionary<string, object> material, List<object> textures)
        {
            if (!(material.TryGetValue("pbrMetallicRoughness", out var raw) && raw is Dictionary<string, object> pbr &&
                pbr.TryGetValue("baseColorTexture", out var rawInfo) && rawInfo is Dictionary<string, object> info && info.ContainsKey("index"))) return -1;
            return ImageOf(textures, Integer(info, "index"));
        }

        static int ImageOf(List<object> textures, int texture) =>
            At(textures, texture) is Dictionary<string, object> map && map.ContainsKey("source") ? Integer(map, "source") : -1;

        static byte[] ImageBytes(Dictionary<string, object> image, List<object> views, byte[] binary)
        {
            if (image == null || !image.ContainsKey("bufferView") || !(At(views, Integer(image, "bufferView")) is Dictionary<string, object> view)) return null;
            var offset = view.ContainsKey("byteOffset") ? Integer(view, "byteOffset") : 0;
            var length = Integer(view, "byteLength");
            if (offset < 0 || length <= 0 || (long)offset + length > binary.Length) return null;
            var data = new byte[length];
            Buffer.BlockCopy(binary, offset, data, 0, length);
            return data;
        }

        static Tuple<int, int> ImageSize(Dictionary<string, object> image, List<object> views, byte[] binary)
        {
            if (image == null || !image.ContainsKey("bufferView") || !(At(views, Integer(image, "bufferView")) is Dictionary<string, object> view))
                return Tuple.Create(0, 0);
            var offset = view.ContainsKey("byteOffset") ? Integer(view, "byteOffset") : 0;
            var length = Integer(view, "byteLength");
            try { return LilToonGlbExtension.ImageSize(binary, offset, length); }
            catch (Exception) { return Tuple.Create(0, 0); }
        }

        // Every name the avatar, the VRM or the report can show, so a line from the report never carries one.
        static Redactor Names(GameObject source, Dictionary<string, object> json, ExportFallbackReport report)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            void Add(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim();
                names.Add(name);
                // "Hair (1)" and "Hair.001" also show as "Hair".
                var stem = Regex.Replace(name, @"(\s*\(\d+\)|\.\d+)$", "");
                if (stem.Length > 0) names.Add(stem);
            }
            if (source != null)
            {
                foreach (var transform in source.GetComponentsInChildren<Transform>(true)) Add(transform.name);
                foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        Add(material.name);
                        foreach (var property in material.GetTexturePropertyNames())
                            if (material.HasProperty(property) && material.GetTexture(property) is Texture texture && texture != null) Add(texture.name);
                    }
                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
                    if (mesh == null) continue;
                    Add(mesh.name);
                    for (var i = 0; i < mesh.blendShapeCount; i++) Add(mesh.GetBlendShapeName(i));
                }
            }
            CollectStrings(json, Add);
            foreach (var entry in report?.Results ?? (IReadOnlyList<ExportResultEntry>)System.Array.Empty<ExportResultEntry>())
            {
                Add(entry.Name);
                foreach (var part in (entry.Path ?? "").Split('/')) Add(part);
            }
            return new Redactor(names);
        }

        // Names in the VRM: every "name", the metadata, and the keys of custom expressions.
        static void CollectStrings(object node, Action<string> add, string key = null)
        {
            if (node is Dictionary<string, object> map)
            {
                foreach (var pair in map)
                {
                    if (pair.Key == "custom" && pair.Value is Dictionary<string, object> custom) foreach (var name in custom.Keys) add(name);
                    CollectStrings(pair.Value, add, pair.Key);
                }
            }
            else if (node is List<object> list) foreach (var item in list) CollectStrings(item, add, key);
            else if (node is string value && (key == "name" || key == "authors" || key == "copyrightInformation" || key == "contactInformation"))
                add(value);
        }

        // Masks names and paths. Built once per export from every name that can show.
        internal sealed class Redactor
        {
            readonly Regex ascii;
            readonly List<string> others;

            internal Redactor(IEnumerable<string> names)
            {
                // Longest first, so "Hair_Back" is masked whole before "Hair".
                var ordered = names.Where(name => name.Length >= 2 || name.Any(c => c > 0x7f)).Distinct(StringComparer.Ordinal)
                    .OrderByDescending(name => name.Length).ToList();
                var plain = ordered.Where(name => name.All(c => c < 0x80)).Select(Regex.Escape).ToList();
                ascii = plain.Count == 0 ? null
                    : new Regex(@"(?<![A-Za-z0-9_])(?:" + string.Join("|", plain) + @")(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);
                others = ordered.Where(name => name.Any(c => c >= 0x80)).ToList();
            }

            internal string Apply(string text)
            {
                if (string.IsNullOrEmpty(text)) return "";
                // Paths first: a drive path or a project path may hold names not on the avatar.
                text = Regex.Replace(text, @"[A-Za-z]:[\\/][^\s「」""']*", "<path>");
                text = Regex.Replace(text, @"(?<![A-Za-z0-9_])(Assets|Packages)/[^\s「」""']*", "<path>");
                foreach (var name in others) text = text.Replace(name, Mask);
                if (ascii != null) text = ascii.Replace(text, Mask);
                // Quoted words are names in the report's sentences.
                text = Regex.Replace(text, "「[^」]*」", "「" + Mask + "」");
                text = Regex.Replace(text, "『[^』]*』", "『" + Mask + "』");
                text = Regex.Replace(text, "\"[^\"]*\"", "\"" + Mask + "\"");
                return text;
            }
        }

        static string Number(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

        static List<object> Array(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out var raw) && raw is List<object> list ? list : new List<object>();

        static object At(List<object> list, int index) => index >= 0 && index < list.Count ? list[index] : null;

        static int Integer(object map, string key) =>
            map is Dictionary<string, object> dictionary && dictionary.TryGetValue(key, out var raw) ? (int)Numeric(raw) : 0;

        static double Numeric(object raw)
        {
            switch (raw)
            {
                case long l: return l;
                case int i: return i;
                case double d: return d;
                case float f: return f;
                default: return 0;
            }
        }
    }
}
