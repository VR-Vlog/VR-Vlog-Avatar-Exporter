using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

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
        public void KeepsNamesTheAppDrives() => UnusedMorphTargetsFixture.KeepsNamesTheAppDrives(Check);

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
        public void KeepsTheFileWhenNothingIsUnused() => UnusedMorphTargetsFixture.KeepsTheFileWhenNothingIsUnused(Check);

        [Test]
        public void EveryShippedLocaleTranslatesTheUnusedMorphResults()
        {
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = System.IO.Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var text = System.IO.File.ReadAllText(System.IO.Path.Combine(directory, "ExporterLocale_" + locale + ".json"));
                foreach (var source in new[] { UniVrmOneClickExporter.UnusedMorphFormat, UniVrmOneClickExporter.UnusedMorphCategory, ExportStageGuard.Label(ExportStage.UnusedMorphs) })
                    Assert.That(text, Does.Contain("{\"source\":\"" + source + "\",\"translated\":\""), locale + ": " + source);
            }
        }
    }
}
