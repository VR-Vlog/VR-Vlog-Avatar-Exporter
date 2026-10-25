using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRVlog.LilToon;

namespace VRVlog.LilToonExporter
{
    // Text a user pastes when hair or a body vanishes or turns see-through in
    // the app. It is read from the written VRM, so it shows what the app gets.
    // It is built only from numbers and fixed words, never from text that can
    // hold a name: meshes and materials are numbered, a shader shows as its
    // family, and a line of the export report shows its reason only when that
    // reason is a fixed sentence of the exporter.
    // NamedText is a separate text for a private message: the same lines with
    // the names and paths left in. It is made only when the user picks it.
    internal sealed class ExportDiagnostics
    {
        internal const string NamedNotice = "名前・パスを含みます。DM など非公開の場だけで送ってください";
        internal const string QuickTransfer = "かんたん転送";
        internal const string DetailedTransfer = "通常の転送";
        internal const string TransferSave = "転送画面から保存";
        internal const string BoardSave = "表情ボードから保存";
        internal const string ReasonHidden = "文面は省略";

        // Shader families by name prefix. Only the family label is written, never the shader name.
        static readonly (string Prefix, string Family)[] ShaderFamilies =
        {
            ("lilToon", "lilToon"), ("Hidden/lilToon", "lilToon"), ("_lil/", "lilToon"), ("Hidden/ltspass", "lilToon"),
            ("Standard", "Standard"), ("Unlit/", "Unlit"), ("Legacy Shaders/", "Legacy Shaders"), ("Mobile/", "Mobile"),
            ("Particles/", "Particles"), ("VRM/MToon", "MToon"), ("VRM10/", "MToon"), ("UniGLTF/", "UniGLTF"),
            ("Universal Render Pipeline/", "URP"), ("UnityChanToonShader/", "UnityChanToonShader"), (".poiyomi/", "Poiyomi"),
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
        readonly List<string> gimmicks = new List<string>();
        // The lines only the named text has, and its change lines with every name kept.
        readonly List<string> namedHead = new List<string>();
        readonly List<string> gimmickNames = new List<string>();
        readonly List<string> namedChanges = new List<string>();
        readonly Dictionary<int, byte[]> images = new Dictionary<int, byte[]>();
        readonly Dictionary<int, string> alpha = new Dictionary<int, string>();
        string text, namedText;
        // False when the VRM could not be read and only the head was written.
        bool read = true;
        static HashSet<string> fixedTexts;

        sealed class MeshLine
        {
            internal int Vertices, Submeshes, BlendShapes;
            internal string Name = "", Index = "", Materials = "";
            internal bool Skinned;
        }

        sealed class MaterialLine
        {
            internal string Name = "", ShaderName = "", Shader = "", Converted = "", Transparency = "", Queue = "", Sides = "", Textures = "";
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
                Last = new ExportDiagnostics(bytes) { read = false };
                Last.head.Add("VR Vlog 書き出し診断");
                Last.head.Add("VRMを読めませんでした: " + error.GetType().Name);
            }
            return Last;
        }

        internal static void Forget() => Last = null;

        internal string Text() => text ?? (text = Write(false));

        internal string NamedText() => namedText ?? (namedText = Write(true));

        string Write(bool withNames)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < head.Count; i++)
            {
                builder.AppendLine(head[i]);
                if (i == 0 && withNames) builder.AppendLine(NamedNotice);
            }
            if (withNames) foreach (var line in namedHead) builder.AppendLine(line);
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
                        .Append(" / マテリアル ").Append(mesh.Materials);
                    if (withNames) builder.Append(" / 名前 ").Append(mesh.Name);
                    builder.AppendLine();
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
                        .Append(" / ").Append(material.Sides);
                    if (withNames) builder.Append(" / 名前 ").Append(material.Name).Append(" / シェーダー ").Append(material.ShaderName);
                    builder.AppendLine();
                    builder.Append("   メイン ").Append(material.MainImageText);
                    if (material.MainImage >= 0) builder.Append(' ').Append(Alpha(material.MainImage, material.Cutoff));
                    builder.Append(" / テクスチャ ").AppendLine(material.Textures);
                }
            }
            if (read)
            {
                builder.AppendLine("[書き出さなかったギミック]");
                foreach (var line in gimmicks) builder.AppendLine(line);
                if (withNames) foreach (var line in gimmickNames) builder.AppendLine(line);
                builder.AppendLine("[変換で変えた項目]");
                var lines = withNames ? namedChanges : changes;
                if (lines.Count == 0) builder.AppendLine("なし");
                foreach (var line in lines) builder.AppendLine(line);
            }
            return builder.ToString();
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
            var listedMeshes = new Dictionary<int, int>();
            var vertexTotal = 0L;
            // The numbers a report line's targets show as, by the name in the VRM. The names themselves are not written.
            var numbers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rawNode in Array(json, "nodes"))
            {
                if (!(rawNode is Dictionary<string, object> node) || !node.ContainsKey("mesh")) continue;
                var meshIndex = Integer(node, "mesh");
                if (meshIndex < 0 || meshIndex >= meshesJson.Count) continue;
                if (!listedMeshes.TryGetValue(meshIndex, out var number))
                    listedMeshes[meshIndex] = number = diagnostics.meshes.Count + 1;
                if (node.TryGetValue("name", out var rawNodeName) && rawNodeName is string nodeName && !numbers.ContainsKey(nodeName))
                    numbers[nodeName] = "メッシュ #" + number;
                if (number <= diagnostics.meshes.Count) continue;
                var primitives = Array(meshesJson[meshIndex] as Dictionary<string, object>, "primitives")
                    .OfType<Dictionary<string, object>>().ToList();
                var line = new MeshLine { Submeshes = primitives.Count, Skinned = node.ContainsKey("skin"), Name = node.TryGetValue("name", out var rawName) ? rawName as string ?? "" : "" };
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
                line.Name = material.TryGetValue("name", out var rawMaterialName) ? rawMaterialName as string ?? "" : "";
                line.ShaderName = shader ?? "不明";
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
                diagnostics.namedHead.Add("アバター " + source.name);
                diagnostics.namedHead.Add("初期で非表示 " + Paths(source, renderers.Where(renderer => !renderer.gameObject.activeInHierarchy)));
                diagnostics.namedHead.Add("無効 " + Paths(source, renderers.Where(renderer => renderer.gameObject.activeInHierarchy && !renderer.enabled)));
            }

            for (var index = 0; index < gltfMaterials.Count; index++)
                if (gltfMaterials[index] is Dictionary<string, object> material && material.TryGetValue("name", out var rawName) &&
                    rawName is string name && !numbers.ContainsKey(name))
                    numbers[name] = "マテリアル #" + (index + 1);

            diagnostics.gimmicks.AddRange(Gimmicks(source, json, report));
            diagnostics.gimmickNames.AddRange(GimmickNames(source, report));

            var list = ExportResultList.Build(report, warnings);
            head.Add("変換の結果 " + string.Join("・", ExportResultList.Order.Select(outcome => ExportResultList.Label(outcome) + list.Summary[outcome])) +
                "・参考" + list.NoticeCount + (list.MissingShaderCount > 0 ? " / シェーダーなし " + list.MissingShaderCount : ""));
            // Lines that read the same once the names are gone are written once with their count.
            var counts = new Dictionary<(string, string), int>();
            var order = new List<(string Line, string Targets)>();
            foreach (var row in list.Rows)
            {
                var key = (ChangeLine(row), Targets(row, numbers));
                if (!counts.ContainsKey(key)) order.Add(key);
                counts[key] = (counts.TryGetValue(key, out var count) ? count : 0) + row.Count;
            }
            foreach (var key in order)
                diagnostics.changes.Add(key.Line + (counts[key] > 1 ? "（" + counts[key] + "件）" : "") + key.Targets);
            foreach (var row in list.Rows) diagnostics.namedChanges.Add(NamedChangeLine(row));
            return diagnostics;
        }

        // The objects' paths under the avatar, or "なし".
        static string Paths(GameObject source, IEnumerable<Component> components)
        {
            var paths = components.Select(component => ExportFallbackReport.RelativePath(source.transform, component.transform))
                .Where(path => path != null).Select(path => path.Length == 0 ? source.name : path).Distinct().ToList();
            return paths.Count == 0 ? "なし" : string.Join(", ", paths);
        }

        // Where the gimmicks and parts counted by Gimmicks are, for the named text only.
        internal static List<string> GimmickNames(GameObject source, ExportFallbackReport report)
        {
            var lines = new List<string>();
            foreach (var entry in report?.Results ?? (IReadOnlyList<ExportResultEntry>)new ExportResultEntry[0])
                if (entry.RuleId != null && entry.Outcome == ExportOutcome.Omitted)
                    lines.Add("外した " + ExportGimmickChoices.GroupName(entry.RuleId) + " " + (entry.Path ?? entry.Name));
            if (source == null) return lines;
            var parts = Parts(source);
            for (var i = 0; i < PartKinds.Length; i++)
                if (parts[i].Count > 0) lines.Add(PartKinds[i] + "の場所 " + Paths(source, parts[i]));
            return lines;
        }

        // The board's part kinds, then trails and lines, which UniVRM does not write either.
        static readonly string[] PartKinds = ExportGimmickChoices.PartNames.Concat(new[] { "トレイル・ライン" }).ToArray();
        // Constraints get their own line, counted against the VRM, not with the parts that are never written.
        static readonly int ConstraintKind = System.Array.IndexOf(PartKinds, "コンストレイント");

        // The avatar's components by PartKinds, inactive objects included.
        static List<Component>[] Parts(GameObject source)
        {
            var parts = PartKinds.Select(_ => new List<Component>()).ToArray();
            foreach (var component in source.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var index = component is TrailRenderer || component is LineRenderer ? parts.Length - 1 : ExportGimmickChoices.PartIndex(component);
                if (index >= 0) parts[index].Add(component);
            }
            return parts;
        }

        // One line of the report with its own words: the reason as written, the targets by name.
        internal static string NamedChangeLine(ExportResultRow row)
        {
            var line = new StringBuilder(ExportResultList.Label(row.Outcome)).Append(" [").Append(row.Group).Append("] ").Append(row.Content);
            if (row.Targets.Count > 0) line.Append(" / 対象 ").Append(string.Join(", ", row.Targets.Select(target => string.IsNullOrEmpty(target.Path) ? target.Name : target.Path)));
            if (row.Count > Math.Max(1, row.Targets.Count)) line.Append("（").Append(row.Count).Append("件）");
            if (!string.IsNullOrEmpty(row.Reason)) line.Append(" / 理由 ").Append(row.Reason);
            // A reason of several lines stays on its own line here.
            return Regex.Replace(line.ToString(), @"\s*\n\s*", " ");
        }

        // Kinds of parts that are not written, by the exporter's own kinds:
        // the gimmicks a rule removed (by the board's group name), the avatar's
        // components VRM has no place for, and PhysBones and constraints next to
        // what the VRM holds after conversion. Counts only, never a name.
        internal static List<string> Gimmicks(GameObject source, Dictionary<string, object> json, ExportFallbackReport report)
        {
            var lines = new List<string>();
            var removed = new List<(string Group, int Count)>();
            foreach (var entry in report?.Results ?? (IReadOnlyList<ExportResultEntry>)new ExportResultEntry[0])
            {
                if (entry.RuleId == null || entry.Outcome != ExportOutcome.Omitted) continue;
                var group = ExportGimmickChoices.GroupName(entry.RuleId);
                var index = removed.FindIndex(item => item.Group == group);
                if (index < 0) removed.Add((group, entry.Count));
                else removed[index] = (group, removed[index].Count + entry.Count);
            }
            lines.Add("外したギミック " + (removed.Count == 0 ? "なし" : string.Join(" / ", removed.Select(item => item.Group + " " + item.Count))));
            if (source == null) return lines;

            var parts = Parts(source);
            var physBones = source.GetComponentsInChildren<Component>(true).Count(PhysBoneSpringExport.IsPhysBone);
            lines.Add("書き出さない部品 " + string.Join(" / ", PartKinds.Select((name, i) => (name, i)).Where(item => item.i != ConstraintKind)
                .Select(item => item.name + " " + parts[item.i].Count)));

            var springs = json.TryGetValue("extensions", out var rawExtensions) && rawExtensions is Dictionary<string, object> extensions &&
                extensions.TryGetValue("VRMC_springBone", out var rawSprings) ? rawSprings as Dictionary<string, object> : null;
            lines.Add("揺れもの 元のPhysBone " + physBones + " / VRMの揺れもの " + Array(springs, "springs").Count);
            var constrained = Array(json, "nodes").OfType<Dictionary<string, object>>().Count(node =>
                node.TryGetValue("extensions", out var raw) && raw is Dictionary<string, object> nodeExtensions && nodeExtensions.ContainsKey("VRMC_node_constraint"));
            lines.Add("コンストレイント 元 " + parts[ConstraintKind].Count + " / VRM " + constrained);
            return lines;
        }

        // One line of the report as fixed words. The group is a category the exporter's code names. The reason is
        // written only when it is fixed text of the exporter; a reason built with a name shows as "文面は省略".
        internal static string ChangeLine(ExportResultRow row) =>
            ExportResultList.Label(row.Outcome) + " [" + row.Group + "] " + (IsFixedText(row.Content) ? row.Content : ReasonHidden);

        // The targets of a report line as mesh and material numbers.
        internal static string Targets(ExportResultRow row, IReadOnlyDictionary<string, string> numbers)
        {
            var line = new StringBuilder();
            var known = new List<string>();
            var unknown = 0;
            foreach (var target in row.Targets)
            {
                if (target.Name != null && numbers.TryGetValue(target.Name, out var number))
                {
                    if (!known.Contains(number)) known.Add(number);
                }
                else unknown++;
            }
            if (known.Count > 0) line.Append(" / 対象 ").Append(string.Join("・", known)).Append(unknown > 0 ? " ほか" + unknown : "");
            return line.ToString();
        }

        // True for a sentence or word the exporter itself writes: a source or a translation in its locale tables.
        // Text built with a name, a path or an exception message is never one of them.
        internal static bool IsFixedText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (fixedTexts == null)
            {
                var found = new HashSet<string>(StringComparer.Ordinal);
                foreach (var guid in AssetDatabase.FindAssets("ExporterLocale_ t:TextAsset"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!Regex.IsMatch(path, @"/Editor/Locales/ExporterLocale_[A-Za-z-]+\.json$")) continue;
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    var table = asset != null ? JsonUtility.FromJson<LocaleTable>(asset.text) : null;
                    foreach (var entry in table?.entries ?? new LocaleEntry[0])
                    {
                        if (entry == null) continue;
                        if (!string.IsNullOrEmpty(entry.source)) found.Add(entry.source);
                        if (!string.IsNullOrEmpty(entry.translated)) found.Add(entry.translated);
                    }
                }
                fixedTexts = found;
            }
            return fixedTexts.Contains(text);
        }

        [Serializable] sealed class LocaleTable { public LocaleEntry[] entries; }
        [Serializable] sealed class LocaleEntry { public string source, translated; }

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
                var mobile = record != null && record.TryGetValue("shaderFamily", out var raw) ? raw as string : null;
                return mobile != null && Regex.IsMatch(mobile, "^[A-Za-z0-9_ -]{1,32}$") ? "lilToon（" + mobile + "）" : "不明";
            }
            if (shader == "Hidden/InternalErrorShader") return "シェーダーなし";
            var family = ShaderFamilies.FirstOrDefault(known => shader.StartsWith(known.Prefix, StringComparison.Ordinal)).Family;
            if (family == null) return "その他（lilToon以外）";
            var traits = new List<string>();
            foreach (var (part, label) in ShaderTraits)
                if (shader.IndexOf(part, StringComparison.Ordinal) >= 0 && !(part == "Outline" && traits.Contains("輪郭線のみ"))) traits.Add(label);
            return family + (traits.Count > 0 ? "（" + string.Join("・", traits) + "）" : "");
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
