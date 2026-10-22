using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VRVlog.LilToonExporter.Compatibility;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VRVlog.LilToonExporter.Tests
{
    // lilToon imported from its unitypackage under Assets registers no
    // package; the exporter finds it by lilToon's package.json instead.
    public sealed class LilToonInstallationTests
    {
        const string Folder = "Temp/VRVlogLilToonInstallationTests";

        [SetUp]
        public void SetUp() => Directory.CreateDirectory(Folder);

        [TearDown]
        public void TearDown() => Directory.Delete(Folder, true);

        static string Write(string json)
        {
            var path = Folder + "/package.json";
            File.WriteAllText(path, json);
            return path;
        }

        [Test]
        public void PackageJsonGivesTheVersionAndFolder()
        {
            var found = LilToonInstallation.FromPackageJson(Write("{\"name\": \"jp.lilxyzw.liltoon\", \"version\": \"2.3.4\", \"displayName\": \"lilToon\"}"));
            Assert.That(found.Version, Is.EqualTo("2.3.4"));
            Assert.That(found.AssetsFolder, Is.EqualTo(Folder));
            Assert.That(found.Report(), Is.EqualTo("2.3.4 (Assets: " + Folder + ")"));
        }

        [TestCase("{\"name\": \"jp.lilxyzw.liltoon\"}")]
        [TestCase("{\"name\": \"jp.lilxyzw.liltoon\", \"version\": \"\"}")]
        [TestCase("not json")]
        [TestCase("")]
        public void PackageJsonWithoutAVersionIsUnknown(string json)
        {
            var found = LilToonInstallation.FromPackageJson(Write(json));
            Assert.That(found.Version, Is.EqualTo(DependencyPolicy.LilToonUnknownVersion));
            Assert.That(DependencyPolicy.LilToonError(found.Version), Is.Null);
            Assert.That(DependencyPolicy.LilToonWarning(found.Version), Does.Contain("不明"));
        }

        [Test]
        public void PackageJsonOfAnotherPackageIsNotLilToon() =>
            Assert.That(LilToonInstallation.FromPackageJson(Write("{\"name\": \"com.example.other\", \"version\": \"1.0.0\"}")), Is.Null);

        // Holds for both setups: the registered package wins, and without it
        // the Assets copy feeds the diagnostics and the export.
        [Test]
        public void FindMatchesHowThisProjectInstallsLilToon()
        {
            var registered = false;
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
                registered |= package.name == LilToonInstallation.PackageName;
            var found = LilToonInstallation.Find();
            Assert.That(found, Is.Not.Null);
            if (registered)
            {
                Assert.That(found.AssetsFolder, Is.Null);
                Assert.That(LilToonInstallation.FindUnderAssets(), Is.Null);
            }
            else
            {
                Assert.That(found.AssetsFolder, Does.StartWith("Assets/"));
                Assert.That(found.Describe(text => text), Does.Contain("Assetsに導入: " + found.AssetsFolder));
            }
            Assert.That(DependencyDiagnostics.Version(DependencyDiagnostics.Installed(), LilToonInstallation.PackageName), Is.EqualTo(found.Version));
            Assert.That(DependencyDiagnostics.CreateReport(), Does.Contain(LilToonInstallation.PackageName + ": " + found.Report()));
            Assert.That(LilToonExporterWindow.InstalledLilToonVersion(), Is.EqualTo(found.Version));
        }

        [Test]
        public void ExportChecksAcceptTheFoundLilToon()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon"));
            try
            {
                foreach (var renderer in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterial = material;
                var warnings = new List<string>();
                Assert.That(LilToonExporterWindow.RequireLilToon(fixture.Source, warnings), Is.EqualTo(LilToonInstallation.Find().Version));
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}
