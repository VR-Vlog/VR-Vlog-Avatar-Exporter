using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public class DependencyRoundTripTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task SupportedBackendPreservesMeshesMorphsAndMaterialBindingsOnReimport(bool fullLilToon)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            // A morph the app drives: one nothing uses is left out of the VRM.
            const string drivenMorph = "JawOpen";
            fixture.Mesh.AddBlendShapeFrame(drivenMorph, 100, Enumerable.Repeat(Vector3.right * .03f, fixture.Mesh.vertexCount).ToArray(), null, null);
            var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
            var shader = Shader.Find("lilToon");
            Assert.That(shader, Is.Not.Null, "The real lilToon package is required.");
            foreach (var skin in skins) skin.sharedMaterial.shader = shader;
            var originalMeshes = skins.Select(s => s.sharedMesh).ToArray();
            var originalMaterials = skins.Select(s => s.sharedMaterial).ToArray();
            // Unused BlendShapes are left out of the VRM: a list face binds the
            // fixture morph on both meshes so it stays in the round trip.
            var face = new AnimationClip { name = "Hair detail face" };
            foreach (var skin in skins)
                AnimationUtility.SetEditorCurve(face, EditorCurveBinding.FloatCurve(skin.name, typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 100));
            var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Dependency regression", "Tests",
                exporterVersion: fullLilToon ? "compatibility-test" : null,
                lilToonVersion: fullLilToon ? "2.3.4" : null,
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }, listFaces: new[] { (face.name, face, 0f) });
            Vrm10Instance imported = null;
            try
            {
                Assert.That(bytes.Length, Is.GreaterThan(20));
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null);
                foreach (var name in new[] { "Front", "Back" })
                {
                    var skin = imported.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name == name);
                    Assert.That(skin.sharedMesh.vertexCount, Is.EqualTo(3));
                    Assert.That(skin.sharedMesh.GetBlendShapeIndex(drivenMorph), Is.GreaterThanOrEqualTo(0));
                    Assert.That(skin.sharedMaterials.Length, Is.EqualTo(skin.sharedMesh.subMeshCount));
                    Assert.That(skin.sharedMaterials.All(m => m != null && m.shader != null), Is.True);
                    Assert.That(skin.bones.All(b => b != null), Is.True);
                }
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); Object.DestroyImmediate(face); }
            Assert.That(skins.Select(s => s.sharedMesh), Is.EqualTo(originalMeshes));
            Assert.That(skins.Select(s => s.sharedMaterial), Is.EqualTo(originalMaterials));
        }
    }
}
