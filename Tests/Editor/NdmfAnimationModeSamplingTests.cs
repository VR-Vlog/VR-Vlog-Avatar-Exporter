using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Runs the installed MA/NDMF pipeline with a test Optimizing pass that
    // samples the avatar root through AnimationMode, as plugins that photograph
    // menu icons do. Unity crashes in Animator::Sample when the root controller
    // has no layers, so the pass records the layer count and samples only when
    // it is safe. The export lends a controller only where AAO itself would
    // otherwise fail, and every other Optimizing pass sees the root unchanged.
    public sealed class NdmfAnimationModeSamplingTests
    {
        const string PluginType = "VRVlog.LilToonExporter.Tests.AnimationModeSamplingTestPlugin";

        static Type InstalledType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

        static void Set(string field, object value) => InstalledType(PluginType)?.GetField(field)?.SetValue(null, value);

        static object Get(string field) => InstalledType(PluginType).GetField(field).GetValue(null);

        [SetUp]
        public void SetUp()
        {
            Set("Sampled", 0); Set("Layers", 0);
            Set("Armed", true);
        }

        // The test pass stays inert outside these tests.
        [TearDown]
        public void TearDown() => Set("Armed", false);

        // A VRChat avatar: AAO reads the descriptor's layers, so nothing is lent
        // and the pass samples the root exactly as in a normal NDMF build.
        [Test]
        public void ExportSurvivesAnOptimizingPassThatSamplesTheRootThroughAnimationMode()
        {
            var descriptorType = InstalledType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            var proxyType = InstalledType("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy");
            if (descriptorType == null || proxyType == null || InstalledType(PluginType) == null)
                Assert.Ignore("Install the real VRChat SDK, Modular Avatar and NDMF to run their real processing pipeline.");
            var optimizerType = InstalledType("Anatawa12.AvatarOptimizer.TraceAndOptimize");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var source = fixture.Source;
            Assert.That(source.GetComponent<Animator>().runtimeAnimatorController, Is.Null);
            foreach (var skin in fixture.Skins) skin.sharedMaterial.shader = Shader.Find("lilToon");
            source.AddComponent(descriptorType);
            // An MA component makes the export run the NDMF phases; an AAO
            // component, when installed, makes AAO parse the animators.
            var proxy = source.transform.Find("Back").gameObject.AddComponent(proxyType);
            proxyType.GetField("boneReference").SetValue(proxy, HumanBodyBones.Head);
            if (optimizerType != null) source.AddComponent(optimizerType);
            var vrm = ScriptableObject.CreateInstance<VRM10Object>();
            try
            {
                source.AddComponent<Vrm10Instance>().Vrm = vrm;
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(source, "AnimationMode sampling", "Tests", warnings);
                var log = string.Join("\n", warnings);
                Assert.That(bytes, Is.Not.Null.And.Not.Empty, log);
                Assert.That(Get("Layers"), Is.EqualTo(-1), "Optimizing passes must see a VRChat avatar's root without a lent controller.");
                Assert.That(Get("Sampled"), Is.EqualTo(1), log);
                Assert.That(source.GetComponent<Animator>().runtimeAnimatorController, Is.Null);
            }
            finally { Object.DestroyImmediate(vrm); }
        }

        // A generic avatar with an AAO component: AAO parses the root controller
        // and throws on none, so the export lends one that sampling survives.
        [Test]
        public void GenericAvatarLendsAaoAControllerThatAnimationModeCanSample()
        {
            var optimizerType = InstalledType("Anatawa12.AvatarOptimizer.TraceAndOptimize");
            if (optimizerType == null || InstalledType("nadena.dev.ndmf.BuildContext") == null || InstalledType(PluginType) == null)
                Assert.Ignore("Install the real Avatar Optimizer and NDMF to run their real processing pipeline.");
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(value =>
                value.name == "com.anatawa12.avatar-optimizer");
            if (package != null && Version.TryParse(package.version.Split('-', '+')[0], out var version) && version < new Version(1, 9))
                Assert.Ignore("Avatar Optimizer runs on generic-platform avatars from 1.9.0 (installed " + package.version + ").");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var source = fixture.Source;
            Assert.That(source.GetComponent<Animator>().runtimeAnimatorController, Is.Null);
            foreach (var skin in fixture.Skins) skin.sharedMaterial.shader = Shader.Find("lilToon");
            source.AddComponent(optimizerType);
            var vrm = ScriptableObject.CreateInstance<VRM10Object>();
            try
            {
                source.AddComponent<Vrm10Instance>().Vrm = vrm;
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(source, "AnimationMode sampling", "Tests", warnings);
                var log = string.Join("\n", warnings);
                Assert.That(bytes, Is.Not.Null.And.Not.Empty, log);
                Assert.That(Get("Layers"), Is.EqualTo(1), "The controller lent to AAO has one layer; one without layers crashes AnimationMode sampling.");
                Assert.That(Get("Sampled"), Is.EqualTo(1), log);
                Assert.That(source.GetComponent<Animator>().runtimeAnimatorController, Is.Null);
            }
            finally { Object.DestroyImmediate(vrm); }
        }
    }
}
