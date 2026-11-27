using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class UnusedMorphTargetsTests
    {
        static readonly Action<bool, string> Check = (condition, message) => Assert.That(condition, Is.True, message);

        [Test]
        public void KeepsTargetsBoundByVrmExpressions() => UnusedMorphTargetsFixture.KeepsTargetsBoundByVrmExpressions(Check);

        [Test]
        public void KeepsTargetsOfExpressionAnimations() => UnusedMorphTargetsFixture.KeepsTargetsOfExpressionAnimations(Check);

        [Test]
        public void KeepsAndRenumbersTheBindsItIsGiven() => UnusedMorphTargetsFixture.KeepsAndRenumbersTheBindsItIsGiven(Check);

        [Test]
        public void KeepsNamesTheAppDrives() => UnusedMorphTargetsFixture.KeepsNamesTheAppDrives(Check);

        [Test]
        public void KeepsTheShapesOfTheCameraGaze() => UnusedMorphTargetsFixture.KeepsTheShapesOfTheCameraGaze(Check);

        [Test]
        public void KeepsTargetsWithAWeight() => UnusedMorphTargetsFixture.KeepsTargetsWithAWeight(Check);

        [Test]
        public void RemovesUnusedAccessorsAndViews() => UnusedMorphTargetsFixture.RemovesUnusedAccessorsAndViews(Check);

        [Test]
        public void RemovesTheListsOfAMeshLeftWithoutTargets() => UnusedMorphTargetsFixture.RemovesTheListsOfAMeshLeftWithoutTargets(Check);

        [Test]
        public void KeepsAMeshWhosePrimitivesDisagree() => UnusedMorphTargetsFixture.KeepsAMeshWhosePrimitivesDisagree(Check);

        [Test]
        public void KeepsTheFileWithAnUnknownExtension() => UnusedMorphTargetsFixture.KeepsTheFileWithAnUnknownExtension(Check);

        [Test]
        public void KeepsAnAccessorAKeptTargetShares() => UnusedMorphTargetsFixture.KeepsAnAccessorAKeptTargetShares(Check);

        [Test]
        public void RemovesSparseTargetsOfSeveralPrimitives() => UnusedMorphTargetsFixture.RemovesSparseTargetsOfSeveralPrimitives(Check);

        [Test]
        public void KeepsTheFileWhenNothingIsUnused() => UnusedMorphTargetsFixture.KeepsTheFileWhenNothingIsUnused(Check);

        // The whole export, not only the strip step: nothing binds these shapes,
        // and a later step must not lose them either.
        [Test]
        public void AnExportKeepsTheCameraGazeShapesThatNoExpressionBinds()
        {
            NoGraphicsDevice.AllowErrorLogs();
            var gaze = new[] { "eye_up", "eye_down", "eye_left", "eye_right" };
            using var avatar = new AttachmentConnectionTests.Fixture(eyes: true);
            foreach (var name in gaze.Append("Unused detail"))
                avatar.Mesh.AddBlendShapeFrame(name, 100, Enumerable.Repeat(new Vector3(.01f, 0, 0), 3).ToArray(), new Vector3[3], new Vector3[3]);
            avatar.Source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.shader = OptionalTools.RequireShader("lilToon", "Install lilToon.");
            var bytes = UniVrmOneClickExporter.Export(avatar.Source, "Camera gaze", "Tests", exporterVersion: "camera-gaze-regression",
                lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
            var json = GlbDocument.Read(bytes).Json;
            var names = ((List<object>)json["meshes"]).Cast<Dictionary<string, object>>()
                .Where(mesh => mesh.ContainsKey("extras"))
                .SelectMany(mesh => (List<object>)((Dictionary<string, object>)mesh["extras"])["targetNames"]).Cast<string>().ToList();
            Assert.That(names, Is.SupersetOf(gaze));
            Assert.That(names, Does.Not.Contain("Unused detail"), "The strip step still has to run for this test to mean anything.");
        }

        [Test]
        public void EveryShippedLocaleTranslatesTheUnusedMorphStage()
        {
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = System.IO.Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var text = System.IO.File.ReadAllText(System.IO.Path.Combine(directory, "ExporterLocale_" + locale + ".json"));
                var source = ExportStageGuard.Label(ExportStage.UnusedMorphs);
                Assert.That(text, Does.Contain("{\"source\":\"" + source + "\",\"translated\":\""), locale + ": " + source);
            }
        }
    }
}
