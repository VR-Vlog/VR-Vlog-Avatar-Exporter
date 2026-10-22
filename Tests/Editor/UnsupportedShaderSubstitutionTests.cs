using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniGLTF.Extensions.VRMC_vrm;
using UniVRM10;
using UnityEngine;
using VRVlog.LilToon;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class UnsupportedShaderSubstitutionTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        Texture2D Texture(string name) => Own(new Texture2D(4, 4) { name = name });
        Material Standard(string name = "Plain") => Own(new Material(Shader.Find("Standard")) { name = name });

        Material Substitute(Material source)
        {
            var output = UnsupportedShaderSubstitution.Create(source);
            Assert.That(output, Is.Not.Null);
            return Own(output);
        }

        [Test] public void AStandardMaterialKeepsItsMainImageColorNormalAndEmission()
        {
            var main = Texture("main"); var normal = Texture("normal"); var glow = Texture("glow");
            var source = Standard();
            source.SetColor("_Color", Color.red); source.SetTexture("_MainTex", main);
            source.SetTextureScale("_MainTex", new Vector2(2, 1)); source.SetTextureOffset("_MainTex", new Vector2(.25f, 0));
            source.SetTexture("_BumpMap", normal); source.SetFloat("_BumpScale", .5f);
            source.SetTexture("_EmissionMap", glow); source.SetColor("_EmissionColor", new Color(2, 1, 0, 1)); source.EnableKeyword("_EMISSION");

            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("lilToon"));
            Assert.That(output.name, Is.EqualTo("Plain"), "Material evidence is matched by name.");
            Assert.That(output.GetFloat("_TransparentMode"), Is.EqualTo(0f));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(main));
            Assert.That(output.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(2, 1)));
            Assert.That(output.GetTextureOffset("_MainTex"), Is.EqualTo(new Vector2(.25f, 0)));
            Assert.That(output.GetColor("_Color"), Is.EqualTo(Color.red));
            Assert.That(output.GetTexture("_BumpMap"), Is.SameAs(normal));
            Assert.That(output.GetFloat("_UseBumpMap"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_BumpScale"), Is.EqualTo(.5f));
            Assert.That(output.GetTexture("_EmissionMap"), Is.SameAs(glow));
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetColor("_EmissionColor"), Is.EqualTo(source.GetColor("_EmissionColor")));
            Assert.That(source.shader.name, Is.EqualTo("Standard"), "The source material is never changed.");
        }

        [Test] public void EmissionStaysOffWhenTheSourceKeywordIsOff()
        {
            var source = Standard();
            source.SetColor("_EmissionColor", Color.white); source.DisableKeyword("_EMISSION");
            Assert.That(Substitute(source).GetFloat("_UseEmission"), Is.EqualTo(0f));
        }

        [Test] public void CutoutAndTransparentStandardModesChooseTheMatchingOfficialShader()
        {
            var cutout = Standard("Cutout");
            cutout.SetFloat("_Mode", 1); cutout.EnableKeyword("_ALPHATEST_ON"); cutout.SetFloat("_Cutoff", .3f);
            var clipped = Substitute(cutout);
            Assert.That(clipped.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            Assert.That(clipped.GetFloat("_TransparentMode"), Is.EqualTo(1f));
            Assert.That(clipped.GetFloat("_Cutoff"), Is.EqualTo(.3f).Within(1e-6));

            var fade = Standard("Fade");
            fade.SetFloat("_Mode", 3); fade.EnableKeyword("_ALPHAPREMULTIPLY_ON"); fade.renderQueue = 3000;
            var blended = Substitute(fade);
            Assert.That(blended.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            Assert.That(blended.GetFloat("_TransparentMode"), Is.EqualTo(2f));
            Assert.That(blended.renderQueue, Is.EqualTo(blended.shader.renderQueue), "The official transparent shader's own queue is kept.");
            Assert.That(blended.GetFloat("_Cutoff"), Is.EqualTo(0f), "lilToon clips in every mode; a blended source keeps faint pixels.");
        }

        [Test] public void ABlendedSourceKeepsWhetherItWritesDepth()
        {
            var fade = Standard("Fade");
            fade.SetFloat("_Mode", 2); fade.EnableKeyword("_ALPHABLEND_ON"); fade.SetFloat("_ZWrite", 0);
            Assert.That(Substitute(fade).GetFloat("_ZWrite"), Is.EqualTo(0f), "A declared _ZWrite is the authored value.");
            // The probe blends and writes depth with a fixed command, without a property.
            var fixedDepth = Own(new Material(Shader.Find("Hidden/VRVlogTests/DepthWriteProbe")) { name = "Fixed" });
            var blended = Substitute(fixedDepth);
            Assert.That(blended.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            Assert.That(blended.GetFloat("_ZWrite"), Is.EqualTo(1f));
            // Built-in blended shaders have no file to read and do not write depth.
            var builtIn = Own(new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")) { name = "BuiltIn" });
            Assert.That(Substitute(builtIn).GetFloat("_ZWrite"), Is.EqualTo(0f));
        }

        [TestCase("SubShader { Tags { \"Queue\"=\"Transparent\" } Pass { Blend SrcAlpha OneMinusSrcAlpha ZWrite Off } }", false)]
        [TestCase("SubShader { Pass { Blend SrcAlpha OneMinusSrcAlpha } }", true)]
        [TestCase("SubShader { ZWrite Off Pass { Blend SrcAlpha OneMinusSrcAlpha } }", false)]
        [TestCase("SubShader { ZWrite Off Pass { ZWrite On ColorMask 0 } Pass { Blend SrcAlpha OneMinusSrcAlpha } }", true)]
        [TestCase("SubShader { Pass { ZWrite Off } Pass { Tags { \"LightMode\"=\"ShadowCaster\" } ZWrite On } }", false)]
        [TestCase("SubShader { Pass { /* ZWrite On */ ZWrite Off // ZWrite On\n } }", false)]
        [TestCase("SubShader { Pass { ZWrite [_ZWriteOn] CGPROGRAM float4 f() { return 0; } ENDCG } }", true)]
        [TestCase("SubShader { Pass { ZWrite [_Other] } }", false)]
        [TestCase("SubShader { Pass { ZWrite Off } } SubShader { Pass { ZWrite On } }", false)]
        [TestCase("SubShader { CGPROGRAM #pragma surface surf Lambert alpha:fade\n ENDCG }", false)]
        [TestCase("SubShader { ZWrite [_ZWriteOn] CGPROGRAM #pragma surface surf Lambert keepalpha\n ENDCG Pass { Tags { \"LightMode\"=\"ShadowCaster\" } ZWrite On } }", true)]
        public void TheFirstSubShaderDecidesWhetherColorPassesWriteDepth(string shaderLab, bool expected)
        {
            Assert.That(UnsupportedShaderSubstitution.ShaderWritesDepth(shaderLab, name => name == "_ZWriteOn" ? 1 : 0), Is.EqualTo(expected));
        }

        [Test] public void AStencilWriterStaysWithoutColorAndDepth()
        {
            // The probe masks every color channel and does not write depth; an
            // opaque copy that draws would cover the body as a white shell.
            var writer = Own(new Material(Shader.Find("Hidden/VRVlogTests/StencilWriterProbe")) { name = "Writer" });
            var substitute = Substitute(writer);
            Assert.That(substitute.shader.name, Is.EqualTo("lilToon"));
            Assert.That(substitute.GetFloat("_ColorMask"), Is.EqualTo(0f));
            Assert.That(substitute.GetFloat("_ZWrite"), Is.EqualTo(0f));
            // An ordinary opaque source keeps lilToon's defaults.
            var plain = Substitute(Standard());
            Assert.That(plain.GetFloat("_ColorMask"), Is.EqualTo(15f));
            Assert.That(plain.GetFloat("_ZWrite"), Is.EqualTo(1f));
        }

        [TestCase("SubShader { ColorMask 0 ZWrite Off Pass { Tags { \"LightMode\"=\"ForwardBase\" } } }", true)]
        [TestCase("SubShader { Pass { ColorMask 0 } Pass { ColorMask 0 } }", true)]
        [TestCase("SubShader { Pass { ColorMask 0 } Pass { Blend One One ColorMask RGB } }", false)]
        [TestCase("SubShader { ColorMask 0 Pass { } Pass { ColorMask RGBA } }", false)]
        [TestCase("SubShader { Pass { ZWrite On } }", false)]
        [TestCase("SubShader { Pass { ColorMask 0 } Pass { Tags { \"LightMode\"=\"ShadowCaster\" } } }", true)]
        [TestCase("SubShader { Pass { ColorMask [_Mask] } }", true)]
        [TestCase("SubShader { Pass { ColorMask [_Unset] } }", false)]
        [TestCase("SubShader { Pass { /* ColorMask 0 */ ColorMask RGBA // ColorMask 0\n } }", false)]
        [TestCase("SubShader { Pass { ColorMask 0 } UsePass \"Other/PASS\" }", false)]
        [TestCase("SubShader { Pass { ColorMask RGBA } } SubShader { Pass { ColorMask 0 } }", false)]
        [TestCase("SubShader { ColorMask 0 CGPROGRAM #pragma surface surf Lambert\n ENDCG }", true)]
        [TestCase("SubShader { CGPROGRAM #pragma surface surf Lambert\n ENDCG }", false)]
        [TestCase("SubShader { CGPROGRAM #pragma surface surf Lambert\n ENDCG Pass { Tags { \"LightMode\"=\"ShadowCaster\" } } }", false)]
        [TestCase("SubShader { ColorMask 0 CGPROGRAM #pragma surface surf Lambert\n ENDCG Pass { Tags { \"LightMode\"=\"ShadowCaster\" } } }", true)]
        public void TheFirstSubShaderDecidesWhetherAnyPassDrawsColor(string shaderLab, bool expected)
        {
            Assert.That(UnsupportedShaderSubstitution.ShaderDrawsNoColor(shaderLab, (name, fallback) => name == "_Mask" ? 0 : fallback), Is.EqualTo(expected));
        }

        // A pass whose stencil test fails where nothing marked the stencil
        // draws only over pixels another material marked, so on its own it
        // draws nothing.
        [TestCase("SubShader { Pass { ColorMask 0 Stencil { Ref 121 Comp Always Pass Replace } } Pass { Blend One One ColorMask RGB Stencil { Ref 121 Comp Equal } } }", true)]
        [TestCase("SubShader { Stencil { Ref 2 Comp Equal } Pass { } }", true)]
        [TestCase("SubShader { Pass { Stencil { Ref [_Ref] Comp [_Comp] } } }", true)]
        [TestCase("SubShader { Pass { Stencil { Ref [_Zero] Comp Equal } } }", false)]
        [TestCase("SubShader { Pass { Stencil { Ref 1 Comp NotEqual } } }", false)]
        [TestCase("SubShader { Pass { Stencil { Ref 0 Comp NotEqual } } }", true)]
        [TestCase("SubShader { Pass { Stencil { Ref 1 ReadMask 2 Comp Equal } } }", false)]
        [TestCase("SubShader { Pass { Stencil { Ref 1 CompFront Equal CompBack Always } } }", false)]
        [TestCase("SubShader { Pass { Stencil { Ref 1 Comp Always CompFront Equal CompBack LEqual } } }", true)]
        [TestCase("SubShader { Pass { Stencil { Ref 1 Comp GEqual } } }", false)]
        [TestCase("SubShader { Stencil { Ref 1 Comp Equal } Pass { Stencil { Ref 1 Comp Always } } }", false)]
        [TestCase("SubShader { Pass { Stencil { Ref 1 Comp Equal } } Pass { } }", false)]
        public void AStencilTestThatFailsOnAClearedBufferDrawsNothing(string shaderLab, bool expected)
        {
            // _Ref is 5 and _Comp is 3 (Equal) on the material; _Zero is 0.
            float Property(string name, float fallback) => name == "_Ref" ? 5 : name == "_Comp" ? 3 : name == "_Zero" ? 0 : fallback;
            Assert.That(UnsupportedShaderSubstitution.ShaderDrawsNoColor(shaderLab, Property), Is.EqualTo(expected));
        }

        [Test] public void ToonPropertyNamesMapToShadowClippingAndCull()
        {
            var main = Texture("main"); var shade = Texture("shade"); var normal = Texture("normal");
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/ToonPropertyProbe")) { name = "Toon" });
            source.SetTexture("_MainTex", main); source.SetColor("_BaseColor", new Color(1, .5f, .5f, 1));
            source.SetTexture("_1st_ShadeMap", shade); source.SetColor("_1st_ShadeColor", new Color(.5f, .25f, .25f, 1));
            source.SetTexture("_NormalMap", normal); source.SetFloat("_BumpScale", .7f);
            source.SetColor("_Emissive_Color", new Color(0, 0, 1, 1));
            source.SetFloat("_ClippingMode", 1); source.SetFloat("_Clipping_Level", .4f);
            source.SetFloat("_CullMode", 0); source.SetFloat("_BaseColor_Step", .6f); source.SetFloat("_BaseShade_Feather", .2f);

            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(main));
            Assert.That(output.GetColor("_Color"), Is.EqualTo(new Color(1, .5f, .5f, 1)));
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.4f).Within(1e-6));
            Assert.That(output.GetTexture("_BumpMap"), Is.SameAs(normal));
            Assert.That(output.GetFloat("_BumpScale"), Is.EqualTo(.7f).Within(1e-6));
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f), "A colored emission without a toggle is on.");
            Assert.That(output.GetColor("_EmissionColor"), Is.EqualTo(new Color(0, 0, 1, 1)));
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.SameAs(shade), "A separate shade image is used directly.");
            Assert.That(Vector4.Distance(output.GetColor("_ShadowColor"), new Color(.5f, .25f, .25f, 1)), Is.LessThan(1e-5f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.6f).Within(1e-6));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.2f).Within(1e-6));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(0f));
        }

        [Test] public void EmissionStrengthScalesTheLinearColor()
        {
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/EmissionStrengthProbe")) { name = "Glow" });
            var authored = new Color(.5f, .25f, .4f, .7f);
            source.SetColor("_EmissionColor", authored); source.SetFloat("_EmissionStrength", 2);
            var output = Substitute(source).GetColor("_EmissionColor");
            var expected = authored.linear;
            Assert.That(output.linear.r, Is.EqualTo(expected.r * 2).Within(1e-4), "The shader doubles the linear value, not the saved sRGB value.");
            Assert.That(output.linear.g, Is.EqualTo(expected.g * 2).Within(1e-4));
            Assert.That(output.linear.b, Is.EqualTo(expected.b * 2).Within(1e-4));
            Assert.That(output.r, Is.EqualTo(.6859f).Within(1e-3), "sRGB 0.5 doubled in linear is about sRGB 0.686, not 1.");
            Assert.That(output.a, Is.EqualTo(.7f).Within(1e-6), "Alpha is not scaled.");
        }

        [Test] public void AShaderSwitchDecidesTheEmissionEvenWithAWhiteColor()
        {
            var glow = Texture("glow");
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/EmissionSwitchProbe")) { name = "Switch" });
            source.SetTexture("_EmissionMap", glow);
            Assert.That(Substitute(source).GetFloat("_UseEmission"), Is.EqualTo(0f), "The switch is off by default.");
            source.SetFloat("_ES_Enable", 1);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_EmissionMap"), Is.SameAs(glow));
        }

        [Test] public void WithoutASwitchTheShaderDefaultColorAloneDoesNotGlow()
        {
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/EmissionNoSwitchProbe")) { name = "Always" });
            Assert.That(Substitute(source).GetFloat("_UseEmission"), Is.EqualTo(0f), "Nothing authored means nothing glows.");
            source.SetColor("_EmissionColor", new Color(1, 0, 0, 1));
            Assert.That(Substitute(source).GetFloat("_UseEmission"), Is.EqualTo(1f), "An authored color glows.");
        }

        [Test] public void AnEmissionImageNamedEmissionMasksTheColor()
        {
            var mask = Texture("mask");
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/EmissionNoSwitchProbe")) { name = "Masked" });
            source.SetTexture("_Emission", mask); source.SetColor("_EmissionColor", new Color(.6f, .6f, .6f, 1));
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_EmissionMap"), Is.SameAs(mask));
        }

        [Test] public void EmissionTintedByTheBaseColorUsesTheAlbedo()
        {
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/EmissionNoSwitchProbe")) { name = "Tinted" });
            source.SetColor("_EmissionColor", new Color(.5f, .5f, .5f, 1));
            Assert.That(Substitute(source).GetFloat("_EmissionMainStrength"), Is.EqualTo(0f));
            source.SetFloat("_EmissionBaseColorAsMap", 1);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_EmissionMainStrength"), Is.EqualTo(1f));
        }

        [Test] public void AShadeImageEqualToTheMainImageBecomesARatioToTheMainColor()
        {
            var main = Texture("main");
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/ToonPropertyProbe")) { name = "Toon" });
            source.SetTexture("_MainTex", main); source.SetTexture("_1st_ShadeMap", main);
            source.SetColor("_BaseColor", new Color(1, .5f, 1, 1)); source.SetColor("_1st_ShadeColor", new Color(.5f, .25f, .8f, 1));
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("lilToon"));
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.Null);
            var shadow = output.GetColor("_ShadowColor");
            Assert.That(shadow.r, Is.EqualTo(.5f).Within(1e-5)); Assert.That(shadow.g, Is.EqualTo(.5f).Within(1e-5));
            Assert.That(shadow.b, Is.EqualTo(.8f).Within(1e-5));
        }

        [Test] public void ADerivedLilToonBuildCopiesTheValuesTheOfficialShaderAlsoHas()
        {
            var main = Texture("main");
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/lilToonDerivedProbe")) { name = "Derived" });
            Assert.That(LilToonMaterialReader.IsLilToon(source), Is.True);
            Assert.That(UnsupportedShaderSubstitution.NeedsSubstitute(source), Is.True);
            source.SetFloat("_TransparentMode", 1); source.SetTexture("_MainTex", main); source.SetColor("_Color", Color.green);
            source.SetFloat("_Cutoff", .25f); source.SetFloat("_UseShadow", 1); source.SetColor("_ShadowColor", Color.blue);
            source.SetFloat("_ShadowBorder", .7f); source.SetFloat("_Cull", 0); source.SetFloat("_DerivedOnly", 3);

            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(main));
            Assert.That(output.GetColor("_Color"), Is.EqualTo(Color.green));
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.25f).Within(1e-6));
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetColor("_ShadowColor"), Is.EqualTo(Color.blue));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.7f).Within(1e-6));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(0f));
            Assert.That(output.HasProperty("_DerivedOnly"), Is.False);
        }

        // lilToon 2.3.4 RenderingMode: 5 is fur, 6 fur cutout; refraction (3, 4),
        // fur two-pass (7) and gem (8) become plain transparent.
        [TestCase(5, "Hidden/lilToonTransparent")]
        [TestCase(6, "Hidden/lilToonCutout")]
        [TestCase(3, "Hidden/lilToonTransparent")]
        [TestCase(8, "Hidden/lilToonTransparent")]
        public void DerivedRenderingModesChooseTheNearestOfficialShader(int declared, string expected)
        {
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/lilToonDerivedProbe")) { name = "Derived" });
            source.SetFloat("_TransparentMode", declared);
            Assert.That(Substitute(source).shader.name, Is.EqualTo(expected));
        }

        [Test] public void ADynamicMainImageIsLeftOutSoTheSnapshotCanSaveTheSubstitute()
        {
            var screen = Own(new RenderTexture(4, 4, 0) { name = "screen" });
            var source = Own(new Material(Shader.Find("Unlit/Texture")) { name = "Screen" });
            source.SetTexture("_MainTex", screen);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("lilToon"));
            Assert.That(output.GetTexture("_MainTex"), Is.Null, "A render texture cannot be saved in the file.");

            var (avatar, renderers) = Build(source);
            Own(avatar);
            var created = new List<Material>();
            UnsupportedShaderSubstitution.Apply(avatar, created, new List<string>(), new ExportFallbackReport(), null);
            owned.AddRange(created);
            Assert.That(renderers[0].sharedMaterial, Is.SameAs(created.Single()));
            Assert.DoesNotThrow(() => LilToonFullSnapshot.Capture(avatar, warnings: new List<string>()));
        }

        [Test] public void RenderersWithoutAnExportedMeshAreNotSubstituted()
        {
            var plain = Standard("Sparks");
            var avatar = Own(new GameObject("avatar"));
            var sparks = new GameObject("sparks"); sparks.transform.SetParent(avatar.transform, false);
            sparks.AddComponent<ParticleSystem>();
            var renderer = sparks.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = plain;
            var created = new List<Material>(); var warnings = new List<string>(); var fallbacks = new ExportFallbackReport();
            UnsupportedShaderSubstitution.Apply(avatar, created, warnings, fallbacks, null);
            Assert.That(created, Is.Empty);
            Assert.That(renderer.sharedMaterial, Is.SameAs(plain));
            Assert.That(fallbacks.Count, Is.Zero, "Materials absent from the file are not listed.");
            Assert.That(warnings, Is.Empty);
        }

        [Test] public void AMaterialWhoseShaderIsMissingIsReadFromItsSavedValues()
        {
            var main = Texture("main");
            var source = Standard("Missing");
            source.SetColor("_Color", Color.yellow); source.SetTexture("_MainTex", main);
            source.shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(source.shader, Is.Not.Null);
            Assume.That(source.HasProperty("_Color"), Is.False);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("lilToon"));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(main));
            Assert.That(output.GetColor("_Color"), Is.EqualTo(Color.yellow));
        }

        Material MissingDerived(int declared)
        {
            var source = Own(new Material(Shader.Find("Hidden/VRVlogTests/lilToonDerivedProbe")) { name = "MissingDerived" });
            source.SetFloat("_TransparentMode", declared); source.SetColor("_Color", Color.green); source.SetFloat("_Cutoff", .25f);
            source.renderQueue = -1;
            source.shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(source.shader, Is.Not.Null);
            Assume.That(source.HasProperty("_TransparentMode"), Is.False);
            return source;
        }

        // Render queue "From Shader" (-1) leaves only _TransparentMode to tell
        // the rendering mode of a derived lilToon whose shader is missing.
        [TestCase(1, "Hidden/lilToonCutout")]
        [TestCase(2, "Hidden/lilToonTransparent")]
        public void AMissingDerivedLilToonKeepsItsSavedRenderingMode(int declared, string expected)
        {
            var output = Substitute(MissingDerived(declared));
            Assert.That(output.shader.name, Is.EqualTo(expected));
            Assert.That(output.GetFloat("_TransparentMode"), Is.EqualTo((float)declared));
            Assert.That(output.renderQueue, Is.EqualTo(output.shader.renderQueue));
            Assert.That(output.GetColor("_Color"), Is.EqualTo(Color.green));
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.25f).Within(1e-6));
        }

        [Test] public void AMissingShaderWithoutTheLilToonVersionStaysOnTheGenericRules()
        {
            var source = MissingDerived(1);
            using (var serialized = new UnityEditor.SerializedObject(source))
            {
                var floats = serialized.FindProperty("m_SavedProperties.m_Floats");
                var ints = serialized.FindProperty("m_SavedProperties.m_Ints");
                foreach (var entries in new[] { floats, ints })
                    for (var i = entries == null ? -1 : entries.arraySize - 1; i >= 0; i--)
                        if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == "_lilToonVersion") entries.DeleteArrayElementAtIndex(i);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("lilToon"), "Without the version, _TransparentMode is not read.");
            Assert.That(output.GetColor("_Color"), Is.EqualTo(Color.green));
        }

        static readonly Vector4 NeutralTone = new Vector4(0, 1, 1, 1);
        Material ColorProbe(string name = "Adjusted") => Own(new Material(Shader.Find("Hidden/VRVlogTests/ToonColorAdjustProbe")) { name = name });

        Material Substitute(Material source, out bool colorSkipped)
        {
            var output = UnsupportedShaderSubstitution.Create(source, out colorSkipped);
            Assert.That(output, Is.Not.Null);
            return Own(output);
        }

        static void AssertTone(Material output, Vector4 expected) =>
            Assert.That(Vector4.Distance(output.GetVector("_MainTexHSVG"), expected), Is.LessThan(1e-6f), output.GetVector("_MainTexHSVG").ToString("F4"));

        [Test] public void ABrightnessFactorBecomesTheValueOfTheToneCorrection()
        {
            var source = ColorProbe();
            source.SetFloat("_Bright", .6f);
            var output = Substitute(source, out var skipped);
            AssertTone(output, new Vector4(0, 1, .6f, 1));
            Assert.That(skipped, Is.False);
            Assert.That(LilToonMainTextureBaker.NeedsBake(output), Is.True, "The existing baker bakes it into the exported image.");
        }

        [TestCase(-.25f, .75f)]
        [TestCase(.5f, 1.5f)]
        public void AColorAdjustBrightnessScalesTheValue(float brightness, float value)
        {
            var source = ColorProbe();
            source.SetFloat("_MainColorAdjustToggle", 1); source.SetFloat("_MainBrightness", brightness);
            var output = Substitute(source, out var skipped);
            AssertTone(output, new Vector4(0, 1, value, 1));
            Assert.That(skipped, Is.False);
        }

        [Test] public void AColorAdjustBrightnessIsIgnoredWhileItsSectionIsOff()
        {
            var source = ColorProbe();
            source.SetFloat("_MainBrightness", -.5f); source.SetFloat("_Saturation", 1);
            var output = Substitute(source, out var skipped);
            AssertTone(output, NeutralTone);
            Assert.That(skipped, Is.False);
        }

        [Test] public void NeutralAdjustmentsLeaveTheToneCorrectionAtTheOfficialDefault()
        {
            var official = Own(new Material(Shader.Find("lilToon")));
            Assume.That((Vector4)official.GetVector("_MainTexHSVG"), Is.EqualTo(NeutralTone));
            var source = ColorProbe();
            source.SetFloat("_EnableGammaFix", 1); source.SetFloat("_EnableBlightFix", 1);
            source.SetFloat("_MainColorAdjustToggle", 1); source.SetFloat("_MainHueShiftToggle", 1);
            source.EnableKeyword("USE_HUE_SHIFT");
            var output = Substitute(source, out var skipped);
            Assert.That((Vector4)output.GetVector("_MainTexHSVG"), Is.EqualTo(official.GetVector("_MainTexHSVG")), "Not even rounded.");
            Assert.That(skipped, Is.False);
            Assert.That(LilToonMainTextureBaker.NeedsBake(output), Is.False, "No bake is added for neutral values.");
            Assert.That(LilToonMainTextureBaker.NeedsBake(Substitute(Standard())), Is.False);
        }

        // The slider maximum is 6.283185, just under 2π: a whole turn.
        [TestCase(6.283185f)]
        [TestCase(12.566371f)]
        public void AWholeTurnOfHueIsNotReported(float radians)
        {
            var source = ColorProbe();
            source.EnableKeyword("USE_HUE_SHIFT"); source.SetFloat("_HueShift", radians);
            Substitute(source, out var skipped);
            Assert.That(skipped, Is.False);
        }

        [TestCase("saturation")]
        [TestCase("hue")]
        [TestCase("gradation")]
        [TestCase("maskedBrightness")]
        [TestCase("gamma")]
        [TestCase("brightnessFix")]
        [TestCase("hueRadians")]
        public void AdjustmentsWithoutAnHsvCounterpartAreLeftOutAndReported(string adjustment)
        {
            var source = ColorProbe();
            switch (adjustment)
            {
                case "saturation": source.SetFloat("_MainColorAdjustToggle", 1); source.SetFloat("_Saturation", .5f); break;
                case "hue": source.SetFloat("_MainColorAdjustToggle", 1); source.SetFloat("_MainHueShiftToggle", 1); source.SetFloat("_MainHueShift", .25f); break;
                case "gradation": source.SetFloat("_MainColorAdjustToggle", 1); source.SetFloat("_MainGradationStrength", .5f); break;
                case "maskedBrightness": source.SetFloat("_MainColorAdjustToggle", 1); source.SetFloat("_MainBrightness", -.5f); source.SetFloat("_MainBrightnessGlobalMask", 2); break;
                case "gamma": source.SetFloat("_EnableGammaFix", 1); source.SetFloat("_GammaG", 2); break;
                case "brightnessFix": source.SetFloat("_EnableBlightFix", 1); source.SetFloat("_BlightOffset", .2f); break;
                case "hueRadians": source.EnableKeyword("USE_HUE_SHIFT"); source.SetFloat("_HueShift", 1); break;
            }
            var output = Substitute(source, out var skipped);
            AssertTone(output, NeutralTone);
            Assert.That(skipped, Is.True);
        }

        [Test] public void ApplyNamesTheMaterialsWhoseAdjustmentWasLeftOut()
        {
            var adjusted = ColorProbe();
            adjusted.SetFloat("_MainColorAdjustToggle", 1); adjusted.SetFloat("_Saturation", .5f); adjusted.SetFloat("_MainBrightness", -.5f);
            var plain = Standard();
            var (avatar, renderers) = Build(adjusted, plain);
            Own(avatar);
            var created = new List<Material>(); var warnings = new List<string>();
            UnsupportedShaderSubstitution.Apply(avatar, created, warnings, new ExportFallbackReport(), null);
            owned.AddRange(created);
            AssertTone(renderers[0].sharedMaterial, new Vector4(0, 1, .5f, 1));
            Assert.That(warnings, Is.EqualTo(new[]
            {
                ExporterLocalization.T(UnsupportedShaderSubstitution.Warning) + "Adjusted, Plain",
                ExporterLocalization.T(UnsupportedShaderSubstitution.ColorAdjustmentSkipped) + "Adjusted",
            }));
        }

        [Test] public void LilToonMaterialsKeepTheirOwnToneCorrection()
        {
            var tone = new Vector4(.1f, .5f, .8f, 1.2f);
            var official = Own(new Material(Shader.Find("lilToon")) { name = "Official" });
            official.SetVector("_MainTexHSVG", tone);
            var (avatar, renderers) = Build(official);
            Own(avatar);
            var created = new List<Material>(); var warnings = new List<string>();
            UnsupportedShaderSubstitution.Apply(avatar, created, warnings, new ExportFallbackReport(), null);
            Assert.That(created, Is.Empty);
            Assert.That(renderers[0].sharedMaterial, Is.SameAs(official));
            Assert.That((Vector4)official.GetVector("_MainTexHSVG"), Is.EqualTo(tone));
            Assert.That(warnings, Is.Empty);

            var derived = Own(new Material(Shader.Find("Hidden/VRVlogTests/lilToonDerivedProbe")) { name = "Derived" });
            var output = Substitute(derived, out var skipped);
            AssertTone(output, NeutralTone);
            Assert.That(skipped, Is.False, "A derived build copies lilToon's own values instead.");
        }

        static (GameObject Avatar, Renderer[] Renderers) Build(params Material[] materials)
        {
            var avatar = new GameObject("avatar");
            var renderers = materials.Select((material, index) =>
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = "part" + index;
                part.transform.SetParent(avatar.transform, false);
                var renderer = part.GetComponent<Renderer>(); renderer.sharedMaterial = material;
                return renderer;
            }).ToArray();
            return (avatar, renderers);
        }

        [Test] public void ApplyReplacesEverySlotOnceAndReportsItAsAnApproximation()
        {
            var plain = Standard();
            var official = Own(new Material(Shader.Find("lilToon")) { name = "Official" });
            var mtoon = Own(new Material(Shader.Find("VRM10/MToon10")) { name = "MToon" });
            var (avatar, renderers) = Build(plain, plain, official, mtoon);
            Own(avatar);
            var created = new List<Material>(); var warnings = new List<string>();
            var fallbacks = new ExportFallbackReport(); var tracked = new List<(Material, Material)>();
            UnsupportedShaderSubstitution.Apply(avatar, created, warnings, fallbacks, (copy, original) => tracked.Add((copy, original)));
            owned.AddRange(created);

            Assert.That(created.Count, Is.EqualTo(1), "A shared material is substituted once.");
            var substitute = created[0];
            Assert.That(renderers[0].sharedMaterial, Is.SameAs(substitute));
            Assert.That(renderers[1].sharedMaterial, Is.SameAs(substitute));
            Assert.That(renderers[2].sharedMaterial, Is.SameAs(official));
            Assert.That(renderers[3].sharedMaterial, Is.SameAs(mtoon));
            Assert.That(tracked, Is.EqualTo(new[] { (substitute, plain) }));
            var item = fallbacks.Items.Single();
            Assert.That(item.Kind, Is.EqualTo(ExportFallbackKind.ShaderSubstituted));
            Assert.That(item.Names, Is.EqualTo(new[] { "Plain" }));
            Assert.That(item.Paths, Is.EquivalentTo(new[] { "part0", "part1" }));
            Assert.That(warnings, Is.EqualTo(new[] { ExporterLocalization.T(UnsupportedShaderSubstitution.Warning) + "Plain" }));

            LilToonFullSnapshot snapshot = null;
            Assert.DoesNotThrow(() => snapshot = LilToonFullSnapshot.Capture(avatar, warnings: new List<string>()));
            Assert.That(snapshot.IsEmpty, Is.False, "The substitute takes the ordinary lilToon route.");
        }

        [Test] public void MaterialsAnimatedByExpressionsKeepTheirShader()
        {
            var plain = Standard("Animated");
            var (avatar, renderers) = Build(plain);
            Own(avatar);
            var vrm = Own(ScriptableObject.CreateInstance<VRM10Object>());
            var clip = Own(ScriptableObject.CreateInstance<VRM10Expression>()); clip.name = "Glow";
            clip.MaterialColorBindings = new[] { new MaterialColorBinding { MaterialName = plain.name, BindType = MaterialColorType.color, TargetValue = Vector4.one } };
            vrm.Expression.CustomClips.Add(clip);
            avatar.AddComponent<Vrm10Instance>().Vrm = vrm;
            var created = new List<Material>(); var warnings = new List<string>(); var fallbacks = new ExportFallbackReport();
            UnsupportedShaderSubstitution.Apply(avatar, created, warnings, fallbacks, null);
            Assert.That(created, Is.Empty);
            Assert.That(renderers[0].sharedMaterial, Is.SameAs(plain));
            Assert.That(fallbacks.Count, Is.Zero);
            Assert.That(warnings, Is.EqualTo(new[] { ExporterLocalization.T(UnsupportedShaderSubstitution.KeptForExpressions) + "Animated" }));
        }

        [Test] public void TheConfirmationNamesTheSubstitution()
        {
            var report = new ExportFallbackReport();
            report.Add(ExportFallbackKind.ShaderSubstituted, "Plain");
            var row = ExportResultList.Build(report, null).Rows.Single();
            Assert.That(row.Content, Is.EqualTo(ExporterLocalization.T(ExportFallbackConfirmation.Failure(ExportFallbackKind.ShaderSubstituted))));
            Assert.That(row.Targets.Select(target => target.Name), Is.EqualTo(new[] { "Plain" }));
            Assert.That(report.Results.Single().Outcome, Is.EqualTo(ExportOutcome.Approximated), "Listed as approximated in the result.");
            Assert.That(ExportFallbackConfirmation.Failure(ExportFallbackKind.ShaderSubstituted),
                Is.Not.EqualTo(ExportFallbackConfirmation.Failure(ExportFallbackKind.NonLilToonMaterials)));
        }

        [Test] public void AMixedAvatarExportsTheSubstituteInTheLilToonExtension()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var official = Own(new Material(Shader.Find("lilToon")) { name = "Official" });
            var plain = Standard();
            plain.SetColor("_Color", Color.red);
            var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
            skins[0].sharedMaterial = official;
            foreach (var skin in skins.Skip(1)) skin.sharedMaterial = plain;
            var report = new ExportRecoveryReport();
            var warnings = new List<string>();
            var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Substituted", "Tests", warnings,
                exporterVersion: "tests", lilToonVersion: "2.3.4", recoveryReport: report,
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
            var glb = GlbDocument.Read(bytes);
            LilToonFullContract.Validate(glb.Json, bytes.Length, glb.Binary.Length);
            var records = LilToonFullContract.List(LilToonFullContract.Root(glb.Json), "materials").Select(LilToonFullContract.Object).ToArray();
            Assert.That(records.Select(record => LilToonFullContract.Text(record, "name")), Is.EquivalentTo(new[] { "Official", "Plain" }));
            Assert.That(records.Select(record => LilToonFullContract.Text(record, "sourceShader")), Is.All.EqualTo("lilToon"));
            Assert.That(report.Fallbacks.Items.Select(item => item.Kind), Is.EqualTo(new[] { ExportFallbackKind.ShaderSubstituted }));
            Assert.That(warnings.Any(warning => warning.StartsWith(ExporterLocalization.T(UnsupportedShaderSubstitution.Warning), StringComparison.Ordinal)), Is.True);
            Assert.That(plain.shader.name, Is.EqualTo("Standard"));
            Assert.That(skins[1].sharedMaterial, Is.SameAs(plain), "The source avatar keeps its own material.");
        }

        [Test] public void AMixedAvatarWithARenderTextureScreenStillExports()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var official = Own(new Material(Shader.Find("lilToon")) { name = "Official" });
            var screen = Own(new Material(Shader.Find("Unlit/Texture")) { name = "Screen" });
            screen.SetTexture("_MainTex", Own(new RenderTexture(4, 4, 0) { name = "camera" }));
            var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
            skins[0].sharedMaterial = official;
            foreach (var skin in skins.Skip(1)) skin.sharedMaterial = screen;
            var report = new ExportRecoveryReport();
            var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Screen", "Tests", new List<string>(),
                exporterVersion: "tests", lilToonVersion: "2.3.4", recoveryReport: report,
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
            var glb = GlbDocument.Read(bytes);
            LilToonFullContract.Validate(glb.Json, bytes.Length, glb.Binary.Length);
            var records = LilToonFullContract.List(LilToonFullContract.Root(glb.Json), "materials").Select(LilToonFullContract.Object).ToArray();
            Assert.That(records.Select(record => LilToonFullContract.Text(record, "name")), Is.EquivalentTo(new[] { "Official", "Screen" }));
            Assert.That(report.Fallbacks.Items.Select(item => item.Kind), Is.EqualTo(new[] { ExportFallbackKind.ShaderSubstituted }),
                "No stage is left out because of the render texture.");
        }
    }
}
