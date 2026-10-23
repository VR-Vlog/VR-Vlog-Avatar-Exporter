using NUnit.Framework;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class EmissionGradientTests
    {
        // A gradation image wider than the row that is read was averaged after
        // one bilinear shrink, which skips texels: here every 4th texel is
        // white, and a 256-wide row samples between the black ones only.
        [Test]
        public void AWideGradationImageAveragesEveryTexel()
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Install lilToon for the gradation properties.");
            const int width = 1024;
            var material = new Material(shader);
            var gradation = new Texture2D(width, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            try
            {
                var pixels = new Color[width];
                for (var i = 0; i < width; i++) pixels[i] = i % 4 == 0 ? Color.white : Color.black;
                gradation.SetPixels(pixels); gradation.Apply();
                material.SetFloat("_EmissionUseGrad", 1f);
                material.SetTexture("_EmissionGradTex", gradation);
                material.SetFloat("_EmissionGradSpeed", 1f);
                var factor = LilToonEmissionPolicy.GradientFactor(material);
                Assert.AreEqual(.25f, factor.r, .01f);
                Assert.AreEqual(.25f, factor.g, .01f);
                Assert.AreEqual(.25f, factor.b, .01f);
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(gradation);
            }
        }
    }
}
