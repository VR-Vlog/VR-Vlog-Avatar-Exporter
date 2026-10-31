using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using F = VRVlog.LilToon.LilToonFullContract;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // 自動で軽量化する: vertex colours no material reads are left out and bone
    // weights are written as normalized unsigned shorts.
    public sealed class VertexDataLightweightTests
    {
        const int Vertices = 3;

        // The lilToon record decides: the outline width from the colours with an
        // outline pass drawn, the vertex hidden by _OutlineDeleteMesh, or the fur
        // direction. lilToonMulti and a record without passes count as reading.
        [TestCase("Hidden/lilToonOutline", 0f, 0f, 0f, true, false)]
        [TestCase("Hidden/lilToonOutline", 1f, 0f, 0f, true, true)]
        [TestCase("Hidden/lilToonOutline", 2f, 0f, 0f, true, true)]
        [TestCase("Hidden/lilToonOutline", 1f, 0f, 0f, false, false)]
        [TestCase("Hidden/lilToonOutline", 1f, 1f, 0f, false, true)]
        [TestCase("Hidden/lilToonFur", 0f, 0f, 1f, true, true)]
        [TestCase("Hidden/lilToonFur", 0f, 0f, 0f, true, false)]
        [TestCase("_lil/lilToonMulti", 1f, 0f, 0f, false, true)]
        [TestCase("lilToon", 0f, 0f, 0f, true, false)]
        public void TheRecordTellsWhetherTheColoursAreRead(string shader, float width, float deleteMesh, float fur, bool outlineDrawn, bool reads)
        {
            var record = Record(shader, width, deleteMesh, fur, outlineDrawn);
            Assert.That(VertexDataLightweight.ReadsVertexColor(MToonMaterial(), record), Is.EqualTo(reads));
        }

        // Without a lilToon record only MToon is known never to read them; a
        // missing record field or an unreadable value counts as reading.
        [Test]
        public void AnythingNotKnownCountsAsReading()
        {
            Assert.That(VertexDataLightweight.ReadsVertexColor(MToonMaterial(), null), Is.False);
            Assert.That(VertexDataLightweight.ReadsVertexColor(new Dictionary<string, object> { { "name", "unlit" } }, null), Is.True);
            Assert.That(VertexDataLightweight.ReadsVertexColor(null, null), Is.True);
            var withoutPasses = Record("Hidden/lilToonOutline", 1, 0, 0, false);
            withoutPasses.Remove("passes");
            Assert.That(VertexDataLightweight.ReadsVertexColor(MToonMaterial(), withoutPasses), Is.True);
            var withoutShader = Record("Hidden/lilToonOutline", 1, 0, 0, false);
            withoutShader.Remove("sourceShader");
            Assert.That(VertexDataLightweight.ReadsVertexColor(MToonMaterial(), withoutShader), Is.True);
            var unreadable = Record("lilToon", 0, 0, 0, true);
            ((List<object>)unreadable["values"]).Add(new Dictionary<string, object> { { "name", "_VertexColor2FurVector" }, { "value", new List<object> { "x" } } });
            Assert.That(VertexDataLightweight.ReadsVertexColor(MToonMaterial(), unreadable), Is.True);
        }

        // A mesh keeps COLOR_0 on every primitive when one of its materials
        // reads it, and loses it on every primitive otherwise; the accessor and
        // bufferView go only when nothing else uses them.
        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        public void AMeshLosesItsColoursOnlyWhenNoMaterialReadsThem(bool firstReads, bool secondReads, bool kept)
        {
            var source = Build(new[] { Record("Hidden/lilToonOutline", firstReads ? 1 : 0, 0, 0, true), Record("Hidden/lilToonOutline", secondReads ? 2 : 0, 0, 0, true) });
            var warnings = new List<string>();
            var result = VertexDataLightweight.Apply(source, warnings, out var saved);
            Assert.That(warnings, Is.Empty);
            Assert.That(saved, Is.EqualTo(source.Length - result.Length));
            Assert.That(saved, Is.GreaterThan(0), "The weights are packed either way.");
            var glb = Consistent(source, result);
            foreach (var primitive in AllPrimitives(glb))
                Assert.That(Attributes(primitive).ContainsKey("COLOR_0"), Is.EqualTo(kept));
            Assert.That(F.List(glb.Json, "accessors").Count, Is.EqualTo(kept ? 4 : 3));
        }

        // A colour accessor something else also uses stays with its bufferView.
        [Test]
        public void AColourAccessorAnotherMeshUsesStays()
        {
            var source = Build(new[] { Record("lilToon", 0, 0, 0, true), Record("Hidden/lilToonOutline", 1, 0, 0, true) }, splitMeshes: true);
            var result = VertexDataLightweight.Apply(source, null, out _);
            var glb = Consistent(source, result);
            var meshes = F.List(glb.Json, "meshes").Select(F.Object).ToList();
            Assert.That(Attributes(Primitives(meshes[0]).Single()).ContainsKey("COLOR_0"), Is.False);
            Assert.That(Attributes(Primitives(meshes[1]).Single()).ContainsKey("COLOR_0"), Is.True);
            Assert.That(F.List(glb.Json, "accessors").Count, Is.EqualTo(4));
        }

        // Each vertex adds up to exactly 65535, a zero stays zero, and each
        // weight is within 1/65535 of the float one when at most three are not
        // zero. With four the remainder put on the largest reaches 1.5/65535.
        [TestCase(1f, 0f, 0f, 0f)]
        [TestCase(.5f, .5f, 0f, 0f)]
        [TestCase(.3f, .7f, 0f, 0f)]
        [TestCase(1f / 3, 1f / 3, 1f / 3, 0f)]
        [TestCase(.1f, .2f, .3f, .4f)]
        [TestCase(.25f, .25f, .25f, .25f)]
        [TestCase(0f, 0f, 0f, 0f)]
        public void WeightsAddUpToExactly65535(float a, float b, float c, float d)
        {
            var weights = new[] { a, b, c, d };
            var packed = Quantize(weights);
            Assert.That(packed, Is.Not.Null);
            var values = Enumerable.Range(0, 4).Select(i => (int)BitConverter.ToUInt16(packed, i * 2)).ToArray();
            Assert.That(values.Sum(), Is.EqualTo(weights.All(w => w == 0) ? 0 : 65535));
            var bound = weights.Count(w => w > 0) <= 3 ? 1d : 1.5d;
            for (var i = 0; i < 4; i++)
            {
                if (weights[i] == 0) Assert.That(values[i], Is.Zero);
                Assert.That(values[i] / 65535d, Is.EqualTo((double)weights[i]).Within(bound / 65535 + 1e-6), i.ToString(CultureInfo.InvariantCulture));
            }
        }

        // Many random vertices of two or three bones stay within 1/65535.
        [Test]
        public void RandomWeightsRoundTripWithin1Over65535()
        {
            var random = new System.Random(549);
            for (var vertex = 0; vertex < 2000; vertex++)
            {
                var bones = 2 + vertex % 2;
                var raw = Enumerable.Range(0, bones).Select(_ => (float)random.NextDouble() + 1e-3f).ToArray();
                var total = raw.Sum();
                var weights = raw.Select(w => w / total).Concat(Enumerable.Repeat(0f, 4 - bones)).ToArray();
                var packed = Quantize(weights);
                var values = Enumerable.Range(0, 4).Select(i => (int)BitConverter.ToUInt16(packed, i * 2)).ToArray();
                Assert.That(values.Sum(), Is.EqualTo(65535));
                for (var i = 0; i < 4; i++)
                    Assert.That(values[i] / 65535d, Is.EqualTo((double)weights[i]).Within(1d / 65535 + 1e-6));
            }
        }

        // Weights that are not ones adding up to 1, or a float accessor with a
        // range, stay as they are.
        [Test]
        public void WeightsThatDoNotAddUpTo1StayFloat()
        {
            Assert.That(Quantize(new[] { .5f, 0f, 0f, 0f }), Is.Null);
            Assert.That(Quantize(new[] { 1.2f, -.2f, 0f, 0f }), Is.Null);
            Assert.That(Quantize(new[] { float.NaN, 1f, 0f, 0f }), Is.Null);
            var (accessor, view, binary) = WeightAccessor(new[] { 1f, 0f, 0f, 0f });
            accessor["max"] = new List<object> { 1d, 0d, 0d, 0d };
            Assert.That(VertexDataLightweight.Quantize(accessor, view, binary), Is.Null);
        }

        // Nothing to change returns the input array itself, and an extension
        // this step has not checked keeps the file with a Console message.
        [Test]
        public void AFileWithNothingToChangeOrAnUnknownExtensionIsKept()
        {
            var source = Build(new[] { Record("Hidden/lilToonOutline", 1, 0, 0, true), Record("lilToon", 0, 0, 0, true) }, weights: false);
            Assert.That(VertexDataLightweight.Apply(source, null, out var saved), Is.SameAs(source));
            Assert.That(saved, Is.Zero);
            var glb = GlbDocument.Read(Build(new[] { Record("lilToon", 0, 0, 0, true), Record("lilToon", 0, 0, 0, true) }));
            glb.Json["extensionsUsed"] = new List<object> { F.ExtensionName, "EXT_unknown" };
            var unknown = glb.Write();
            var warnings = new List<string>();
            Assert.That(VertexDataLightweight.Apply(unknown, warnings, out saved), Is.SameAs(unknown));
            Assert.That(saved, Is.Zero);
            Assert.That(warnings, Is.EqualTo(new[] { string.Format(VertexDataLightweight.UnknownExtensionWarning, "EXT_unknown") }));
        }

        // Through the exporter and back through the vendored importer. Off, the
        // file keeps the float weights and the colours and lists no line. On,
        // the weights load within 1/65535 of the source, the colours go unless
        // the outline width comes from them, and the result list has the line.
        // The vendored importer fills missing colours with white, so the loaded
        // colours are compared only where the file keeps them.
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TheExportPacksVertexDataOnlyWhenTheOptionIsOn(bool on, bool outlineWidthFromColours)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Find(outlineWidthFromColours ? "Hidden/lilToonOutline" : "lilToon")) { name = "Vertex data material" };
            Vrm10Instance imported = null;
            try
            {
                if (outlineWidthFromColours) material.SetFloat("_OutlineVertexR2Width", 1);
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                // Enough vertices that the packed file is smaller even when the
                // colours stay. Each vertex has one or two weights.
                const int count = 30;
                var mesh = fixture.Mesh;
                var bindposes = mesh.bindposes;
                mesh.Clear();
                mesh.vertices = Enumerable.Range(0, count).Select(i => new Vector3(-.1f + i * .01f, 1.7f + i % 3 * .1f, .08f)).ToArray();
                mesh.triangles = Enumerable.Range(0, count).ToArray();
                mesh.uv = Enumerable.Range(0, count).Select(i => new Vector2(i / (float)count, i % 3 / 2f)).ToArray();
                mesh.normals = Enumerable.Repeat(Vector3.forward, count).ToArray();
                var colors = Enumerable.Range(0, count).Select(i => new Color(i / (float)count, .5f, .25f, 1 - i / (float)count)).ToArray();
                mesh.colors = colors;
                var source = Enumerable.Range(0, count).Select(i => i % 10 / 9f).ToArray();
                mesh.boneWeights = source.Select(w => new BoneWeight { boneIndex0 = 1, weight0 = w, boneIndex1 = 0, weight1 = 1 - w }).ToArray();
                mesh.bindposes = bindposes;
                mesh.AddBlendShapeFrame("Hair detail", 100, Enumerable.Repeat(new Vector3(.02f, .01f, 0), count).ToArray(), new Vector3[count], new Vector3[count]);
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Vertex data", "Tests", warnings,
                    exporterVersion: "tests", lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None },
                    autoLightweight: on);
                var glb = GlbDocument.Read(bytes);
                var accessors = F.List(glb.Json, "accessors").Select(F.Object).ToList();
                var primitives = F.List(glb.Json, "meshes").Select(F.Object).SelectMany(Primitives).ToList();
                Assert.That(primitives, Is.Not.Empty);
                foreach (var primitive in primitives)
                {
                    var attributes = Attributes(primitive);
                    Assert.That(attributes.ContainsKey("COLOR_0"), Is.EqualTo(!on || outlineWidthFromColours));
                    var weights = accessors[F.Int(attributes, "WEIGHTS_0")];
                    Assert.That(F.Int(weights, "componentType"), Is.EqualTo(on ? 5123 : 5126));
                    Assert.That(weights.TryGetValue("normalized", out var normalized) && normalized is bool flag && flag, Is.EqualTo(on));
                }
                var prefix = ExportNoteText.VertexDataLightened.Substring(0, ExportNoteText.VertexDataLightened.IndexOf("{0}", StringComparison.Ordinal));
                Assert.That(warnings.Count(w => w.StartsWith(prefix, StringComparison.Ordinal)), Is.EqualTo(on ? 1 : 0));
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null);
                foreach (var skin in imported.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var loadedMesh = skin.sharedMesh;
                    Assert.That(loadedMesh.vertexCount, Is.EqualTo(count), skin.name);
                    if (!on || outlineWidthFromColours)
                        for (var v = 0; v < count; v++)
                            Assert.That(Vector4.Distance(loadedMesh.colors[v], colors[v]), Is.LessThan(1e-4f), skin.name + " " + v);
                    var loaded = loadedMesh.boneWeights;
                    for (var v = 0; v < count; v++)
                    {
                        // Compared largest first: the importer may order the bones differently.
                        var got = new[] { loaded[v].weight0, loaded[v].weight1, loaded[v].weight2, loaded[v].weight3 }.OrderByDescending(w => w).ToArray();
                        var expected = new[] { source[v], 1 - source[v], 0f, 0f }.OrderByDescending(w => w).ToArray();
                        for (var c = 0; c < 4; c++) Assert.That(got[c], Is.EqualTo(expected[c]).Within(1d / 65535 + 1e-6), skin.name + " " + v);
                        Assert.That(got.Sum(), Is.EqualTo(1f).Within(1e-6));
                    }
                }
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TheTextFollowsTheWordingRulesAndExistsInEveryLocale()
        {
            var text = ExportNoteText.VertexDataLightened;
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, "[。、！!\n]|ください|できます|\\d \\S"), Is.False, text);
            Assert.That(VertexDataLightweight.ResultLine(0), Is.Null);
            Assert.That(string.Format(CultureInfo.InvariantCulture, text, TexelDensity.Plan.Megabytes(1)),
                Is.EqualTo("メッシュの頂点データを詰めてVRMを0.1MB小さくしました"));
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
                Assert.That(File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json")),
                    Does.Contain("{\"source\":\"" + text + "\",\"translated\":\""), locale);
        }

        static Shader Find(string name)
        {
            var shader = Shader.Find(name);
            Assert.That(shader, Is.Not.Null, name);
            return shader;
        }

        static Dictionary<string, object> MToonMaterial() => new Dictionary<string, object>
        {
            { "name", "material" }, { "extensions", new Dictionary<string, object> { { "VRMC_materials_mtoon", new Dictionary<string, object>() } } },
        };

        // A lilToon record with the values the rule reads. The fur shader
        // declares no outline width and the others no fur direction.
        static Dictionary<string, object> Record(string shader, float width, float deleteMesh, float fur, bool outlineDrawn)
        {
            Dictionary<string, object> Value(string name, float value) => new Dictionary<string, object>
            {
                { "name", name }, { "type", "Float" }, { "value", new List<object> { (double)value, 0d, 0d, 0d } },
            };
            Dictionary<string, object> Pass(string name, string lightMode, bool enabled) => new Dictionary<string, object>
            {
                { "name", name }, { "lightMode", lightMode }, { "enabled", enabled },
            };
            var values = new List<object>();
            if (shader.Contains("Fur")) values.Add(Value("_VertexColor2FurVector", fur));
            else { values.Add(Value("_OutlineVertexR2Width", width)); values.Add(Value("_OutlineDeleteMesh", deleteMesh)); }
            var passes = new List<object> { Pass("FORWARD", "ForwardBase", true) };
            if (shader.Contains("Outline")) passes.Add(Pass("FORWARD_OUTLINE", "Always", outlineDrawn));
            return new Dictionary<string, object> { { "sourceShader", shader }, { "values", values }, { "passes", passes } };
        }

        static byte[] Quantize(float[] weights)
        {
            var (accessor, view, binary) = WeightAccessor(weights);
            return VertexDataLightweight.Quantize(accessor, view, binary);
        }

        static (Dictionary<string, object>, Dictionary<string, object>, byte[]) WeightAccessor(float[] weights)
        {
            var binary = new byte[8 + 16];
            for (var i = 0; i < 4; i++) Buffer.BlockCopy(BitConverter.GetBytes(weights[i]), 0, binary, 8 + i * 4, 4);
            var accessor = new Dictionary<string, object> { { "bufferView", 0L }, { "componentType", 5126L }, { "type", "VEC4" }, { "count", 1L } };
            var view = new Dictionary<string, object> { { "buffer", 0L }, { "byteOffset", 8L }, { "byteLength", 16L } };
            return (accessor, view, binary);
        }

        // One mesh of two primitives sharing POSITION, COLOR_0, WEIGHTS_0 and
        // JOINTS_0, or with splitMeshes two meshes of one primitive each that
        // share them. Each material has a lilToon record and the extension
        // has one chunk, so a moved bufferView shows in the chunk's bytes.
        static byte[] Build(Dictionary<string, object>[] records, bool splitMeshes = false, bool weights = true)
        {
            var parts = new List<byte[]>
            {
                Floats(0, 0, 0, 1, 0, 0, 0, 1, 0),
                Floats(1, .5f, .25f, 1, .2f, .4f, .6f, .8f, 1, 1, 1, 1),
                weights ? Floats(.7f, .3f, 0, 0, 1, 0, 0, 0, .5f, .25f, .25f, 0) : new byte[] { 0, 0, 0, 0 },
                new byte[] { 0, 1, 0, 0, 1, 0, 0, 0, 0, 1, 2, 0 },
                new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 },
            };
            var binary = new List<byte>();
            var views = new List<object>();
            foreach (var part in parts)
            {
                views.Add(new Dictionary<string, object> { { "buffer", 0L }, { "byteOffset", (long)binary.Count }, { "byteLength", (long)part.Length } });
                binary.AddRange(part);
                while (binary.Count % 4 != 0) binary.Add(0);
            }
            Dictionary<string, object> Accessor(int view, long type, string shape) => new Dictionary<string, object>
            {
                { "bufferView", (long)view }, { "componentType", type }, { "type", shape }, { "count", (long)Vertices },
            };
            var accessors = new List<object>
            {
                Accessor(0, 5126, "VEC3"), Accessor(1, 5126, "VEC4"), weights ? Accessor(2, 5126, "VEC4") : Accessor(3, 5121, "VEC4"), Accessor(3, 5121, "VEC4"),
            };
            ((Dictionary<string, object>)accessors[0])["min"] = new List<object> { 0d, 0d, 0d };
            ((Dictionary<string, object>)accessors[0])["max"] = new List<object> { 1d, 1d, 0d };
            Dictionary<string, object> Primitive(int material) => new Dictionary<string, object>
            {
                { "attributes", new Dictionary<string, object> { { "POSITION", 0L }, { "COLOR_0", 1L }, { "WEIGHTS_0", 2L }, { "JOINTS_0", 3L } } },
                { "material", (long)material },
            };
            var meshes = splitMeshes
                ? new List<object> { Mesh(Primitive(0)), Mesh(Primitive(1)) }
                : new List<object> { Mesh(Primitive(0), Primitive(1)) };
            var materials = records.Select((record, index) => { record["materialIndex"] = (long)index; return (object)MToonMaterial(); }).ToList();
            var json = new Dictionary<string, object>
            {
                { "asset", new Dictionary<string, object> { { "version", "2.0" } } },
                { "extensionsUsed", new List<object> { "VRMC_materials_mtoon", F.ExtensionName } },
                { "buffers", new List<object> { new Dictionary<string, object> { { "byteLength", (long)binary.Count } } } },
                { "bufferViews", views }, { "accessors", accessors }, { "meshes", meshes }, { "materials", materials },
                { "extensions", new Dictionary<string, object> { { F.ExtensionName, new Dictionary<string, object>
                {
                    { "materials", records.Cast<object>().ToList() },
                    { "chunks", new List<object> { new Dictionary<string, object> { { "bufferView", 4L }, { "decodedBytes", 8L }, { "codec", "deflate" } } } },
                } } } },
            };
            return GlbDocument.Create(json, binary.ToArray()).Write();
        }

        static Dictionary<string, object> Mesh(params Dictionary<string, object>[] primitives) =>
            new Dictionary<string, object> { { "primitives", primitives.Cast<object>().ToList() } };

        static byte[] Floats(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

        static IEnumerable<Dictionary<string, object>> Primitives(Dictionary<string, object> mesh) => F.List(mesh, "primitives").Select(F.Object);

        static Dictionary<string, object> Attributes(Dictionary<string, object> primitive) => F.Object(primitive["attributes"]);

        static IEnumerable<Dictionary<string, object>> AllPrimitives(GlbDocument glb) =>
            F.List(glb.Json, "meshes").Select(F.Object).SelectMany(Primitives);

        // Every accessor and bufferView index is in range, every view lies in
        // the binary chunk, and POSITION, COLOR_0, JOINTS_0 and the chunk keep
        // their bytes. Packed weights decode to the source within 1/65535.
        static GlbDocument Consistent(byte[] source, byte[] result)
        {
            var before = GlbDocument.Read(source);
            var after = GlbDocument.Read(result);
            var views = F.List(after.Json, "bufferViews").Select(F.Object).ToList();
            var accessors = F.List(after.Json, "accessors").Select(F.Object).ToList();
            Assert.That(F.Int(F.Object(F.List(after.Json, "buffers")[0]), "byteLength"), Is.EqualTo(after.Binary.Length));
            foreach (var view in views)
                Assert.That(Offset(view) + F.Int(view, "byteLength"), Is.LessThanOrEqualTo(after.Binary.Length));
            foreach (var accessor in accessors) Assert.That(F.Int(accessor, "bufferView"), Is.LessThan(views.Count));
            byte[] Bytes(GlbDocument glb, Dictionary<string, object> view) =>
                glb.Binary.Skip(Offset(view)).Take(F.Int(view, "byteLength")).ToArray();
            byte[] Data(GlbDocument glb, int accessor) =>
                Bytes(glb, F.Object(F.List(glb.Json, "bufferViews")[F.Int(F.Object(F.List(glb.Json, "accessors")[accessor]), "bufferView")]));
            var sourcePrimitives = AllPrimitives(before).ToList();
            var resultPrimitives = AllPrimitives(after).ToList();
            for (var p = 0; p < resultPrimitives.Count; p++)
            {
                var was = Attributes(sourcePrimitives[p]);
                var now = Attributes(resultPrimitives[p]);
                Assert.That(F.Int(resultPrimitives[p], "material"), Is.EqualTo(F.Int(sourcePrimitives[p], "material")));
                foreach (var name in new[] { "POSITION", "COLOR_0", "JOINTS_0" })
                    if (now.ContainsKey(name))
                    {
                        Assert.That(F.Int(now, name), Is.LessThan(accessors.Count), name);
                        Assert.That(Data(after, F.Int(now, name)), Is.EqualTo(Data(before, F.Int(was, name))), name);
                    }
                Assert.That(now.ContainsKey("POSITION") && now.ContainsKey("JOINTS_0") && now.ContainsKey("WEIGHTS_0"), Is.True);
                var weights = accessors[F.Int(now, "WEIGHTS_0")];
                Assert.That(F.Int(weights, "componentType"), Is.EqualTo(5123));
                Assert.That(weights["normalized"], Is.EqualTo(true));
                var packed = Data(after, F.Int(now, "WEIGHTS_0"));
                var floats = Data(before, F.Int(was, "WEIGHTS_0"));
                Assert.That(packed.Length, Is.EqualTo(Vertices * 8));
                for (var v = 0; v < Vertices; v++)
                {
                    Assert.That(Enumerable.Range(0, 4).Sum(c => BitConverter.ToUInt16(packed, v * 8 + c * 2)), Is.EqualTo(65535));
                    for (var c = 0; c < 4; c++)
                        Assert.That(BitConverter.ToUInt16(packed, v * 8 + c * 2) / 65535d,
                            Is.EqualTo((double)BitConverter.ToSingle(floats, v * 16 + c * 4)).Within(1d / 65535 + 1e-6));
                }
            }
            var chunk = F.Int(F.Object(F.List(F.Root(after.Json), "chunks")[0]), "bufferView");
            Assert.That(Bytes(after, views[chunk]), Is.EqualTo(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 }));
            return after;
        }

        static int Offset(Dictionary<string, object> view) => view.ContainsKey("byteOffset") ? F.Int(view, "byteOffset") : 0;
    }
}
