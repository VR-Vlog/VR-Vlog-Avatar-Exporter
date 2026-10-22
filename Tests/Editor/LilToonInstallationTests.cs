using System.Collections.Generic;
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
        const string Json = "{\"name\": \"jp.lilxyzw.liltoon\", \"version\": \"2.3.4\", \"displayName\": \"lilToon\"}";
        const string AssetsJson = "Assets/lilToon/package.json";
        const string AssetsShader = "Assets/lilToon/Shader/lts.shader";

        static LilToonInstallation FromPaths(string packageJsonPath, string shaderPath, string text) =>
            LilToonInstallation.FromAssetPaths(packageJsonPath, () => shaderPath, path =>
            {
                Assert.That(path, Is.EqualTo(packageJsonPath));
                return text;
            });

        [Test]
        public void PackageJsonGivesTheVersionAndFolder()
        {
            var found = LilToonInstallation.FromPackageJson(AssetsJson, Json);
            Assert.That(found.Version, Is.EqualTo("2.3.4"));
            Assert.That(found.AssetsFolder, Is.EqualTo("Assets/lilToon"));
            Assert.That(found.Report(), Is.EqualTo("2.3.4 (Assets: Assets/lilToon)"));
            Assert.That(found.Describe(text => text), Is.EqualTo("2.3.4（Assetsに導入: Assets/lilToon）"));
        }

        [TestCase("{\"name\": \"jp.lilxyzw.liltoon\"}")]
        [TestCase("{\"name\": \"jp.lilxyzw.liltoon\", \"version\": \"\"}")]
        [TestCase("not json")]
        [TestCase("")]
        public void PackageJsonWithoutAVersionIsUnknown(string json)
        {
            var found = LilToonInstallation.FromPackageJson(AssetsJson, json);
            Assert.That(found.Version, Is.EqualTo(DependencyPolicy.LilToonUnknownVersion));
            Assert.That(DependencyPolicy.LilToonError(found.Version), Is.Null);
            Assert.That(DependencyPolicy.LilToonWarning(found.Version), Does.Contain("不明"));
        }

        [Test]
        public void PackageJsonOfAnotherPackageIsNotLilToon() =>
            Assert.That(LilToonInstallation.FromPackageJson(AssetsJson, "{\"name\": \"com.example.other\", \"version\": \"1.0.0\"}"), Is.Null);

        // The unitypackage import: package.json and shaders under Assets.
        [Test]
        public void AssetsPackageJsonIsFound()
        {
            var found = FromPaths(AssetsJson, AssetsShader, Json);
            Assert.That(found.Version, Is.EqualTo("2.3.4"));
            Assert.That(found.AssetsFolder, Is.EqualTo("Assets/lilToon"));
        }

        // A moved folder keeps the GUID, so the path follows it.
        [Test]
        public void MovedAssetsFolderIsFound() =>
            Assert.That(FromPaths("Assets/Shaders/lilToon/package.json", null, Json).AssetsFolder, Is.EqualTo("Assets/Shaders/lilToon"));

        // Without package.json (deleted, stale GUID) the shader still counts, version unknown.
        [TestCase("")]
        [TestCase("Assets/lilToon/package.json")]
        public void AssetsShaderWithoutPackageJsonIsUnknown(string packageJsonPath)
        {
            var found = FromPaths(packageJsonPath, AssetsShader, null);
            Assert.That(found.Version, Is.EqualTo(DependencyPolicy.LilToonUnknownVersion));
            Assert.That(found.AssetsFolder, Is.EqualTo("Assets/lilToon"));
            Assert.That(found.Describe(text => text), Is.EqualTo("不明（Assetsに導入: Assets/lilToon）"));
        }

        // Package copies are not an Assets install; the registered package covers them.
        [TestCase("Packages/jp.lilxyzw.liltoon/package.json", "Packages/jp.lilxyzw.liltoon/Shader/lts.shader")]
        [TestCase("", "Packages/jp.lilxyzw.liltoon/Shader/lts.shader")]
        [TestCase("", null)]
        [TestCase(null, "")]
        public void NothingUnderAssetsIsNotFound(string packageJsonPath, string shaderPath) =>
            Assert.That(FromPaths(packageJsonPath, shaderPath, Json), Is.Null);

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
                var installed = LilToonInstallation.Find().Version;
                Assert.That(LilToonExporterWindow.RequireLilToon(fixture.Source, warnings), Is.EqualTo(installed));
                // The window's export path: lilToon extension written, installed version recorded.
                var bytes = new ExportSettings().Export(fixture.Source, "Installed lilToon", "Tests", new List<string>(), null, null);
                var glb = GlbDocument.Read(bytes);
                Assert.That(VRVlog.LilToon.LilToonFullContract.Text(VRVlog.LilToon.LilToonFullContract.Root(glb.Json), "sourceLilToonVersion"), Is.EqualTo("2.3.4"));
                var extras = VRVlog.LilToon.LilToonFullContract.Object(VRVlog.LilToon.LilToonFullContract.Object(glb.Json["asset"])["extras"]);
                Assert.That(extras[LilToonFullSnapshot.InstalledLilToonVersionKey], Is.EqualTo(installed));
                Assert.DoesNotThrow(() => VRVlog.LilToon.LilToonFullContract.Validate(glb.Json, bytes.Length, glb.Binary.Length));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void LayerAlphaBakerIncludesLilToonFromTheAssetsFolder()
        {
            var source = "#include \"Packages/jp.lilxyzw.liltoon/Shader/Includes/lil_common.hlsl\"";
            Assert.That(LilToonMainTextureBaker.LayerAlphaSource(source, "Assets/lilToon"),
                Is.EqualTo("#include \"Assets/lilToon/Shader/Includes/lil_common.hlsl\""));
        }

        // Where lilToon is under Assets this compiles the rewritten source;
        // a baker that fails to compile draws the error shader instead of the layers.
        [Test]
        public void LayerAlphaBakerCompilesWhereLilToonIs()
        {
            var shader = LilToonMainTextureBaker.LayerAlphaShader();
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.name, Is.EqualTo("Hidden/VRVlog/LayerAlphaBaker"));
            Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader), Is.False);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(LilToonMainTextureBaker.LayerAlphaShader(), Is.SameAs(shader));
        }

        // A .shader asset that includes lilToon by its package path fails to
        // compile on import, and logs the missing include in the console,
        // where lilToon is under Assets or not installed.
        [Test]
        public void NoShaderAssetIncludesLilToonByItsPackagePath()
        {
            var exporter = PackageInfo.FindForAssembly(typeof(LilToonMainTextureBaker).Assembly);
            Assume.That(exporter, Is.Not.Null, "The exporter is not installed as a package.");
            var shaders = System.IO.Directory.GetFiles(exporter.resolvedPath, "*.shader", System.IO.SearchOption.AllDirectories);
            Assert.That(shaders, Is.Not.Empty);
            foreach (var path in shaders)
                Assert.That(System.IO.File.ReadAllText(path), Does.Not.Contain("Packages/" + LilToonInstallation.PackageName + "/"), path);
            Assert.That(LayerAlphaBakerSource.Text, Does.Contain("#include \"Packages/" + LilToonInstallation.PackageName + "/Shader/Includes/lil_common.hlsl\""));
        }
    }
}
