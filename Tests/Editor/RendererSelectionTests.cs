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
