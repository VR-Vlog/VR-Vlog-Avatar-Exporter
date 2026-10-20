using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniGLTF.Extensions.VRMC_vrm;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class MaterialFallbackTests
    {
        [Test]
        public void LightingSchemaStoresAuthoredValuesAndFallbackUsesCorrectUnits()
        {
            var source = new Material(Shader.Find("Hidden/VRVlogTests/lilToon"));
            var created = new List<Material>();
            try
            {
                source.SetFloat("_LightMinLimit", .1f);
                source.SetFloat("_LightMaxLimit", .8f);
                var direction = new Vector4(.2f, .3f, -.7f, 1f);
                source.SetVector("_LightDirectionOverride", direction);
                source.SetFloat("_UseShadow", 1f);
                source.SetFloat("_ShadowBorder", .7f);
                source.SetFloat("_ShadowBlur", .2f);
                source.SetFloat("_UseRim", 1f);
                source.SetFloat("_RimEnableLighting", .2f);
                source.SetFloat("_RimBlur", .9f);
                source.SetColor("_EmissionColor", new Color(.5f, .6f, .7f, 1f));
                source.SetFloat("_EmissionBlend", .4f);
                var record = LilToonMaterialReader.Read(source, 0, (_, __) => 0, suppressSharedTextureEmission: false);
                Assert.AreEqual(.8f, record.floats.Single(item => item.name == "_LightMaxLimit").value);
                Assert.AreEqual(1, record.vectors.Count);
                Assert.AreEqual(direction.z, record.vectors[0].z);
                Assert.AreEqual(direction.w, record.vectors[0].w);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(source, created, null, false);
                Assert.AreEqual(-.4f, fallback.GetFloat("_ShadingShiftFactor"), .00001f);
                Assert.AreEqual(.8f, fallback.GetFloat("_ShadingToonyFactor"), .00001f);
                Assert.AreEqual(.2f, fallback.GetFloat("_RimLightingMix"), .00001f);
                Assert.AreEqual(0f, fallback.GetFloat("_RimLift"), .00001f);
                var expectedEmission = source.GetColor("_EmissionColor").linear * .4f;
                Assert.AreEqual(expectedEmission.r, fallback.GetColor("_EmissionColor").r, .00001f);
                Assert.AreEqual(direction, source.GetVector("_LightDirectionOverride"));
                Assert.AreEqual(.8f, source.GetFloat("_LightMaxLimit"));
                Assert.AreEqual(.9f, source.GetFloat("_RimBlur"));
            }
            finally
            {
                foreach (var material in created) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void OutlineMaskConvertsRedToGreenAndRetainsSource()
        {
            var source = new Material(Shader.Find("Hidden/VRVlogTests/lilToon"));
            var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var materials = new List<Material>();
            var textures = new List<Texture2D>();
            var masks = new Dictionary<Texture, Texture2D>();
            try
            {
                mask.SetPixels(new[] { new Color(.2f,.9f,1), Color.black, Color.white, Color.black });
                mask.Apply();
                source.SetTexture("_OutlineWidthMask", mask);
                source.SetTextureScale("_MainTex", new Vector2(2f, 3f));
                source.SetTextureOffset("_MainTex", new Vector2(.1f, .2f));
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(source, materials, null, true, textures, masks);
                var converted = (Texture2D)fallback.GetTexture("_OutlineWidthTex");
                Assert.AreNotSame(mask, converted);
                Assert.AreEqual(.2f, converted.GetPixels()[0].g, .01f);
                Assert.AreSame(mask, source.GetTexture("_OutlineWidthMask"));
                Assert.AreEqual(.9f, mask.GetPixels()[0].g, .01f);
                Assert.AreEqual(source.GetTextureScale("_MainTex"), fallback.GetTextureScale("_MainTex"));
                Assert.AreEqual(source.GetTextureOffset("_MainTex"), fallback.GetTextureOffset("_MainTex"));
                var second = new Material(source);
                materials.Add(second);
                var shared = UniVrmOneClickExporter.CreateMToonFallback(second, materials, null, true, textures, masks);
                Assert.AreSame(converted, shared.GetTexture("_OutlineWidthTex"));
                Assert.AreEqual(1, textures.Count, "Shared masks must not duplicate GPU/CPU storage.");
                var nextExport = UniVrmOneClickExporter.CreateMToonFallback(second, materials, null, true, textures, new Dictionary<Texture, Texture2D>());
                Assert.AreNotSame(converted, nextExport.GetTexture("_OutlineWidthTex"), "A later export must not reuse a stale conversion.");
                source.SetFloat("_OutlineVertexR2Width", 1f);
                var warnings = new List<string>();
                fallback = UniVrmOneClickExporter.CreateMToonFallback(source, materials, warnings, true, textures);
                Assert.AreEqual(0, fallback.GetInt("_OutlineWidthMode"));
                Assert.IsTrue(LilToonMaterialReader.Read(source,0,(_,__)=>0).features.Contains("outline"));
                Assert.IsNotEmpty(warnings);
            }
            finally
            {
                foreach (var material in materials) Object.DestroyImmediate(material);
                foreach (var texture in textures) Object.DestroyImmediate(texture);
                Object.DestroyImmediate(mask); Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void EnabledShadowUsesBaseImageForNullOrBuiltInWhiteShadeMap()
        {
            var source = new Material(Shader.Find("Hidden/VRVlogTests/lilToon"));
            var image = new Texture2D(2, 2);
            var shade = new Texture2D(2, 2);
            var created = new List<Material>();
            try
            {
                source.SetTexture("_MainTex", image);
                source.SetFloat("_UseShadow", 1f);
                foreach (var unset in new Texture[] { null, Texture2D.whiteTexture })
                {
                    source.SetTexture("_ShadowColorTex", unset);
                    var fallback = UniVrmOneClickExporter.CreateMToonFallback(source, created, null);
                    Assert.AreSame(image, fallback.GetTexture("_ShadeTex"));
                    Assert.AreSame(unset, source.GetTexture("_ShadowColorTex"));
                    var record = LilToonMaterialReader.Read(source, 0, (_, __) => 0);
                    Assert.IsFalse(record.textures.Any(t => t.semantic == "shadow"));
                }
                source.SetTexture("_ShadowColorTex", shade);
                Assert.AreSame(shade, UniVrmOneClickExporter.CreateMToonFallback(source, created, null).GetTexture("_ShadeTex"));
            }
            finally
            {
                foreach (var material in created) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(shade);
            }
        }

        [Test]
        public void DefaultExportKeepsShadeImageAndIntentionalEmissionWithoutChangingSource()
        {
            var source = new Material(Shader.Find("Hidden/VRVlogTests/lilToon"));
            var image = new Texture2D(2, 2);
            var created = new List<Material>();
            try
            {
                source.SetTexture("_MainTex", image);
                source.SetTexture("_EmissionMap", image);
                source.SetTexture("_OutlineTex", image);
                var originalEmission = source.GetColor("_EmissionColor");
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(source, created, new List<string>());
                Assert.AreSame(image, fallback.GetTexture("_MainTex"));
                Assert.AreSame(image, fallback.GetTexture("_ShadeTex"));
                Assert.Greater(fallback.GetColor("_EmissionColor").maxColorComponent, 0f);
                Assert.AreEqual(0.0014f, fallback.GetFloat("_OutlineWidth"), 0.000001f);
                Assert.AreNotSame(image, fallback.GetTexture("_OutlineWidthTex"));
                var record = LilToonMaterialReader.Read(source, 0, (_, __) => 0);
                Assert.IsTrue(record.features.Contains("emission"));
                Assert.IsTrue(record.textures.Any(t => t.semantic == "emission"));
                Assert.AreEqual(originalEmission, source.GetColor("_EmissionColor"));
                Assert.AreEqual(0.14f, source.GetFloat("_OutlineWidth"), 0.000001f);
                Assert.AreSame(image, source.GetTexture("_EmissionMap"));
            }
            finally
            {
                foreach (var material in created) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(image);
            }
        }

        [Test]
        public void FakeShadowFallbackIsInvisibleAndOrdinaryFallbackStaysOpaque()
        {
            // MToon has no stencil: a visible FakeShadow fallback would cover
            // the whole mesh it shares (for example a hair mesh) in shadow color.
            var shader = Shader.Find("_lil/[Optional] lilToonFakeShadow");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var ordinary = new Material(Shader.Find("Hidden/VRVlogTests/lilToon"));
            var created = new List<Material>();
            try
            {
                var shadowColor = new Color(.74f, .57f, .6f, 1f);
                source.SetColor("_Color", shadowColor);
                Assert.IsTrue(LilToonMaterialReader.IsLilToon(source));
                Assert.IsTrue(LilToonMaterialReader.IsFakeShadow(source));
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(source, created, new List<string>());
                Assert.AreEqual(2, fallback.GetInt("_AlphaMode"), "FakeShadow fallback must blend.");
                Assert.AreEqual(0, fallback.GetInt("_TransparentWithZWrite"));
                Assert.AreEqual(0, fallback.GetInt("_OutlineWidthMode"));
                Assert.AreEqual(0f, fallback.GetColor("_Color").a);
                Assert.AreEqual(0f, UniVrmOneClickExporter.FallbackColor(source, MaterialColorType.color).a);
                // The lilToon extension still receives the authored color.
                Assert.Less(((Vector4)(shadowColor - source.GetColor("_Color"))).magnitude, .0001f);

                Assert.IsFalse(LilToonMaterialReader.IsFakeShadow(ordinary));
                var opaque = UniVrmOneClickExporter.CreateMToonFallback(ordinary, created, null);
                Assert.AreEqual(0, opaque.GetInt("_AlphaMode"));
                Assert.AreEqual(1f, opaque.GetColor("_Color").a);
            }
            finally
            {
                foreach (var material in created) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(ordinary);
            }
        }

        [TestCase(null, 1f)]
        [TestCase(0f, 1f)]
        [TestCase(.5f, .5f)]
        [TestCase(1f, 0f)]
        public void FluorescentEmissionUsesTheLitSceneStrength(float? fluorescence, float expected)
        {
            // lilToon 2.3.4 multiplies fluorescent emission by its inverse
            // lighting, which is 0 under normal light; MToon cannot vary it.
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Install lilToon for the fluorescence property.");
            var source = new Material(shader);
            var image = new Texture2D(2, 2);
            var created = new List<Material>();
            try
            {
                source.SetTexture("_EmissionMap", image);
                source.SetFloat("_UseEmission", 1f);
                source.SetFloat("_EmissionBlend", 1f);
                source.SetColor("_EmissionColor", Color.white);
                if (fluorescence.HasValue) source.SetFloat("_EmissionFluorescence", fluorescence.Value);
                var emission = UniVrmOneClickExporter.FallbackColor(source, MaterialColorType.emissionColor);
                Assert.AreEqual(expected, emission.r, .00001f);
                Assert.AreEqual(expected, emission.g, .00001f);
                Assert.AreEqual(expected, emission.b, .00001f);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(source, created, new List<string>(), false);
                Assert.AreEqual(expected, fallback.GetColor("_EmissionColor").r, .00001f);
                // The lilToon extension keeps the authored value.
                if (fluorescence.HasValue)
                    Assert.AreEqual(fluorescence.Value, LilToonMaterialReader.Read(source, 0, (_, __) => 0,
                        suppressSharedTextureEmission: false).floats.Single(item => item.name == "_EmissionFluorescence").value);
            }
            finally
            {
                foreach (var material in created) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(image);
            }
        }

        [Test]
        public void MultiplyMatcapIsOmittedWhileAddedMatcapStays()
        {
            var shader = Shader.Find("lilToon");
            Assume.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var matcap = new Texture2D(2, 2);
            var created = new List<Material>();
            try
            {
                source.SetFloat("_UseMatCap", 1f);
                source.SetTexture("_MatCapTex", matcap);
                source.SetColor("_MatCapColor", new Color(.8f, .6f, .4f, 1f));
                source.SetFloat("_MatCapBlend", 1f);
                source.SetFloat("_MatCapEnableLighting", .3f);
                source.SetFloat("_MatCapBlendMode", 1f);
                var added = UniVrmOneClickExporter.CreateMToonFallback(source, created, null);
                Assert.AreSame(matcap, added.GetTexture("_MatcapTex"));
                Assert.Greater(added.GetColor("_MatcapColor").maxColorComponent, 0f);
                Assert.AreEqual(.3f, added.GetFloat("_RimLightingMix"), .00001f);

                // lilToon blend mode 3 multiplies; MToon would add it and brighten.
                source.SetFloat("_MatCapBlendMode", 3f);
                var multiplied = UniVrmOneClickExporter.CreateMToonFallback(source, created, null);
                Assert.IsNull(multiplied.GetTexture("_MatcapTex"));
                Assert.AreEqual(UnityEngine.Color.black, UniVrmOneClickExporter.FallbackColor(source, MaterialColorType.matcapColor));
                Assert.AreEqual(0f, multiplied.GetColor("_MatcapColor").maxColorComponent, .00001f);
                Assert.AreEqual(1f, multiplied.GetFloat("_RimLightingMix"), .00001f, "Same as an unused MatCap.");
                Assert.AreEqual(3f, source.GetFloat("_MatCapBlendMode"));
                Assert.AreSame(matcap, source.GetTexture("_MatCapTex"));
            }
            finally
            {
                foreach (var material in created) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(matcap);
            }
        }

        [Test]
        public void SecondShadowColorIsMixedIntoTheShadeColor()
        {
            var shader = Shader.Find("lilToon");
            Assume.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var fixture = new Material(Shader.Find("Hidden/VRVlogTests/lilToon"));
            var created = new List<Material>();
            try
            {
                var main = new Color(.9f, .8f, .7f, 1f);
                var shadow = new Color(.6f, .5f, .7f, 1f);
                var second = new Color(.3f, .2f, .4f, 1f);
                source.SetColor("_Color", main);
                source.SetFloat("_UseShadow", 1f);
                source.SetColor("_ShadowColor", shadow);
                source.SetFloat("_ShadowStrength", .8f);
                source.SetFloat("_ShadowBorder", .5f);
                source.SetFloat("_Shadow2ndBorder", .25f);
                var legacy = MobileMaterialMath.ShadeColor(main, shadow, .8f);

                // An unused 2nd shadow (alpha 0) keeps the previous result exactly.
                source.SetColor("_Shadow2ndColor", new Color(second.r, second.g, second.b, 0f));
                Assert.AreEqual(legacy, UniVrmOneClickExporter.FallbackColor(source, MaterialColorType.shadeColor));

                source.SetColor("_Shadow2ndColor", second);
                var mixed = UniVrmOneClickExporter.FallbackColor(source, MaterialColorType.shadeColor);
                // The 2nd border covers half of the 1st band.
                var expected = MobileMaterialMath.ShadeColor(main, UnityEngine.Color.Lerp(shadow.linear, second.linear, .5f).gamma, .8f);
                Assert.AreEqual(expected.r, mixed.r, .0001f);
                Assert.AreEqual(expected.g, mixed.g, .0001f);
                Assert.AreEqual(expected.b, mixed.b, .0001f);
                Assert.Less(mixed.r, legacy.r);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(source, created, null);
                Assert.AreEqual(mixed.g, fallback.GetColor("_ShadeColor").g, .0001f);
                // GetColor round-trips through the color space in linear projects, so compare with a tolerance.
                Assert.Less(((Vector4)(second - source.GetColor("_Shadow2ndColor"))).magnitude, .0001f, "The source material must not change.");

                // With the shadow off the shade stays the main color.
                source.SetFloat("_UseShadow", 0f);
                Assert.AreEqual(main, UniVrmOneClickExporter.FallbackColor(source, MaterialColorType.shadeColor));

                // A material without the 2nd shadow property keeps the previous formula.
                fixture.SetFloat("_UseShadow", 1f);
                Assert.AreEqual(MobileMaterialMath.ShadeColor(fixture.GetColor("_Color"), fixture.GetColor("_ShadowColor"), fixture.GetFloat("_ShadowStrength")),
                    UniVrmOneClickExporter.FallbackColor(fixture, MaterialColorType.shadeColor));
            }
            finally
            {
                foreach (var material in created) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(fixture);
            }
        }

        [Test]
        public void OptOutRetainsEmissionInBothMaterialRepresentations()
        {
            var source = new Material(Shader.Find("Hidden/VRVlogTests/lilToon"));
            var image = new Texture2D(2, 2);
            var created = new List<Material>();
            try
            {
                source.SetTexture("_MainTex", image);
                source.SetTexture("_EmissionMap", image);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(source, created, null, false);
                Assert.Greater(fallback.GetColor("_EmissionColor").maxColorComponent, 0f);
                Assert.AreSame(image, fallback.GetTexture("_EmissionMap"));
                Assert.IsTrue(LilToonMaterialReader.Read(source, 0, (_, __) => 0,
                    suppressSharedTextureEmission: false).features.Contains("emission"));
            }
            finally
            {
                foreach (var material in created) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(image);
            }
        }
    }
}
