using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class RendererSelectionTests
    {
        [TestCase(false)][TestCase(true)]
        public void EmptyMeshRenderersAreLeftOutOfTheExportCopyOnly(bool fullLilToon)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var shader = Shader.Find("lilToon");
            Assert.That(shader, Is.Not.Null, "The real lilToon package is required for the exporter entry point.");
            var material = new Material(shader);
            var logs = new List<string>();
            void Collect(string message, string stackTrace, LogType type) => logs.Add(message);
            try
            {
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                var emptySkin = new GameObject("Empty skin").AddComponent<SkinnedMeshRenderer>();
                emptySkin.transform.SetParent(fixture.Source.transform, false);
                emptySkin.sharedMaterial = material;
                var emptyStatic = new GameObject("Empty static");
                emptyStatic.transform.SetParent(emptySkin.transform, false);
                emptyStatic.AddComponent<MeshFilter>();
                var emptyStaticRenderer = emptyStatic.AddComponent<MeshRenderer>();
                emptyStaticRenderer.sharedMaterial = material;
                var warnings = new List<string>();
                Application.logMessageReceived += Collect;
                byte[] bytes;
                try
                {
                    bytes = UniVrmOneClickExporter.Export(fixture.Source, "Empty mesh regression", "Tests", warnings,
                        exporterVersion: fullLilToon ? "0.11.9" : null, lilToonVersion: fullLilToon ? "2.3.4" : null,
                        blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                }
                finally { Application.logMessageReceived -= Collect; }

                var glb = GlbDocument.Read(bytes);
                Assert.That(((List<object>)glb.Json["meshes"]).Count, Is.EqualTo(2), "Both renderers with geometry must still be written.");
                Assert.That(logs.Where(message => message.Contains("mesh is null")), Is.Empty);
                var omitted = warnings.Where(warning => warning.StartsWith(ExporterLocalization.T(UniVrmOneClickExporter.EmptyMeshRenderersOmitted), StringComparison.Ordinal)).ToArray();
                Assert.That(omitted.Length, Is.EqualTo(1));
                StringAssert.EndsWith("Empty skin, Empty skin/Empty static", omitted[0]);
                // Only the export copy is changed.
                Assert.That(emptySkin.enabled && emptyStaticRenderer.enabled, Is.True);
                Assert.That(emptySkin.sharedMesh, Is.Null);
                Assert.That(emptySkin.sharedMaterial, Is.SameAs(material));
                Assert.That(fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Where(skin => skin != emptySkin).All(skin => skin.enabled && skin.sharedMesh == fixture.Mesh), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }

        [Test]
        public void EmptySubmeshesAreLeftOutWithTheirMaterialSlots()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var shader = Shader.Find("lilToon");
            Assert.That(shader, Is.Not.Null, "The real lilToon package is required for the exporter entry point.");
            var materials = new[] { "First", "Empty", "Third" }.Select(name => new Material(shader) { name = name }).ToArray();
            var mesh = UnityEngine.Object.Instantiate(fixture.Mesh);
            mesh.name = "Split hair";
            try
            {
                mesh.subMeshCount = 3;
                mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                mesh.SetTriangles(Array.Empty<int>(), 1);
                mesh.SetTriangles(new[] { 0, 2, 1 }, 2);
                var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var skin in skins) skin.sharedMaterial = materials[0];
                var split = skins.Single(skin => skin.name == "Front");
                split.sharedMesh = mesh;
                split.sharedMaterials = materials;
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Empty submesh regression", "Tests", warnings,
                    exporterVersion: "0.11.9", lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });

                // The empty submesh stopped UniVRM with "Index was out of range".
                var json = GlbDocument.Read(bytes).Json;
                var node = ((List<object>)json["nodes"]).Cast<Dictionary<string, object>>().Single(item => item.TryGetValue("name", out var name) && (string)name == "Front");
                var primitives = (List<object>)((Dictionary<string, object>)((List<object>)json["meshes"])[Convert.ToInt32(node["mesh"])])["primitives"];
                var written = (List<object>)json["materials"];
                Assert.That(primitives.Cast<Dictionary<string, object>>().Select(primitive =>
                    (string)((Dictionary<string, object>)written[Convert.ToInt32(primitive["material"])])["name"]), Is.EqualTo(new[] { "First", "Third" }));
                var prefix = ExporterLocalization.T(UniVrmOneClickExporter.EmptySubmeshesOmitted);
                Assert.That(warnings.Where(warning => warning.StartsWith(prefix, StringComparison.Ordinal)), Is.EqualTo(new[] { prefix + "Front" }));
                // Only the export copy is changed.
                Assert.That(split.sharedMesh, Is.SameAs(mesh));
                Assert.That(mesh.subMeshCount, Is.EqualTo(3));
                Assert.That(split.sharedMaterials, Is.EqualTo(materials));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                foreach (var material in materials) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CopyWithoutAnyMeshIsLeftForTheExistingFailure()
        {
            var copy = new GameObject("avatar");
            try
            {
                var skin = new GameObject("Empty skin").AddComponent<SkinnedMeshRenderer>();
                skin.transform.SetParent(copy.transform, false);
                var warnings = new List<string>();
                UniVrmOneClickExporter.DisableEmptyMeshRenderers(copy, warnings);
                Assert.That(skin.enabled, Is.True);
                Assert.That(warnings, Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
    }
}
