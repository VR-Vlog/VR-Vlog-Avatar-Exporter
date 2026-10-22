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
    // menu icons do. The export lends AAO a root controller during Optimizing,
    // and Unity crashes in Animator::Sample when that controller has no layers,
    // so the pass records the layer count and samples only when it is safe.
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

        [Test]
        public void ExportSurvivesAnOptimizingPassThatSamplesTheRootThroughAnimationMode()
        {
            var descriptorType = InstalledType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            var proxyType = InstalledType("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy");
            if (descriptorType == null || proxyType == null || InstalledType(PluginType) == null)
                Assert.Ignore("Install the real VRChat SDK, Modular Avatar and NDMF to run their real processing pipeline.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var source = fixture.Source;
            Assert.That(source.GetComponent<Animator>().runtimeAnimatorController, Is.Null);
            foreach (var skin in fixture.Skins) skin.sharedMaterial.shader = Shader.Find("lilToon");
            source.AddComponent(descriptorType);
            // An MA component makes the export run the NDMF phases.
            var proxy = source.transform.Find("Back").gameObject.AddComponent(proxyType);
            proxyType.GetField("boneReference").SetValue(proxy, HumanBodyBones.Head);
            var vrm = ScriptableObject.CreateInstance<VRM10Object>();
            try
            {
                source.AddComponent<Vrm10Instance>().Vrm = vrm;
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(source, "AnimationMode sampling", "Tests", warnings);
                var log = string.Join("\n", warnings);
                Assert.That(bytes, Is.Not.Null.And.Not.Empty, log);
                Assert.That(Get("Layers"), Is.EqualTo(1), "The export lends a controller with one layer; one without layers crashes AnimationMode sampling.");
                Assert.That(Get("Sampled"), Is.EqualTo(1), log);
            }
            finally { Object.DestroyImmediate(vrm); }
        }
    }
}
