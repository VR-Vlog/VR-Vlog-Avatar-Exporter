using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // One file per shader approximation (Editor/ShaderApprox). The stand-in
    // shader declares the shader's property names; Create is told the real
    // shader name.
    public sealed class NonToonApproximationTests
    {
        const string Probe = "Hidden/VRVlogTests/NonToonProbe";
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        Material Source(int renderingMode = 0, float outlineWidth = 0)
        {
            var shader = Shader.Find(Probe);
            Assume.That(shader, Is.Not.Null);
            var source = Own(new Material(shader) { name = "NonToonSource" });
            source.SetTexture("_BaseTexture", Own(new Texture2D(4, 4) { name = "main" }));
            source.SetFloat("_RenderingMode", renderingMode);
            source.SetFloat("_OutlineWidth", outlineWidth);
            return source;
        }

        Material Substitute(Material source, string shader, out UnsupportedShaderSubstitution.Route route, out IReadOnlyList<string> dropped) =>
            TestObjects.Substitute(owned, source, shader, out _, out route, out dropped, new List<Texture2D>());

        [TestCase(0, "lilToon")]
        [TestCase(1, "Hidden/lilToonCutout")]
        [TestCase(2, "Hidden/lilToonTransparent")]
        public void TheRenderingModeChoosesTheLilToonShaderAndTheMainImageIsKept(int renderingMode, string expected)
        {
            var source = Source(renderingMode);
            source.SetFloat("_Cutoff", .3f);
            var output = Substitute(source, "NonToon", out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo(expected));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(source.GetTexture("_BaseTexture")));
            // The transparent mode also clips at the cutoff.
            if (renderingMode != 0) Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.3f).Within(1e-6f));
        }

        [Test] public void TheOutlineKeepsItsWidthAndColorOverTheMainImage()
        {
            var source = Source(outlineWidth: .2f);
            source.SetColor("_OutlineColor", Color.blue);
            var output = Substitute(source, "NonToon", out _, out var dropped);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonOutline"));
            Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(.2f).Within(1e-6f));
            Assert.That(output.GetFloat("_OutlineFixWidth"), Is.EqualTo(0f));
            Assert.That(output.GetColor("_OutlineColor"), Is.EqualTo(Color.blue));
            Assert.That(output.GetTexture("_OutlineTex"), Is.SameAs(source.GetTexture("_BaseTexture")));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void AZeroWidthDrawsNoOutline()
        {
            Assert.That(Substitute(Source(), "NonToon", out _, out _).shader.name, Is.EqualTo("lilToon"));
        }

        [Test] public void ANormalMapThatAlsoHoldsRoughnessIsLeftOut()
        {
            var source = Source();
            source.SetTexture("_NormalMap", Own(new Texture2D(4, 4) { name = "packed" }));
            source.SetFloat("_NormalMapWithRoughness", 1);
            var output = Substitute(source, "NonToon", out _, out var dropped);
            Assert.That(output.GetTexture("_BumpMap"), Is.Null);
            Assert.That(output.GetFloat("_UseBumpMap"), Is.EqualTo(0f));
            Assert.That(dropped, Does.Contain("Normal Map With Roughness"));
        }

        [Test] public void APlainNormalMapIsKept()
        {
            var source = Source();
            var normal = Own(new Texture2D(4, 4) { name = "normal" });
            source.SetTexture("_NormalMap", normal);
            source.SetFloat("_NormalScale", .5f);
            var output = Substitute(source, "NonToon", out _, out _);
            Assert.That(output.GetTexture("_BumpMap"), Is.SameAs(normal));
            Assert.That(output.GetFloat("_BumpScale"), Is.EqualTo(.5f));
        }

        [Test] public void AMissingNonToonShaderIsKnownByItsSavedValues()
        {
            var source = Source(1);
            source.shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(source.HasProperty("_RenderingMode"), Is.False);
            var output = Substitute(source, null, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
        }

        [Test] public void OtherShadersWithTheSameNamesAreNotTakenForNonToon()
        {
            Substitute(Source(), Probe, out var route, out _);
            Assert.That(route, Is.Not.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
        }
    }
}
