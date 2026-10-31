using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Toon shader families whose transparency is read from their own
    // properties. Each probe shader declares only the property names of its
    // family, so the families are told apart the way real materials are.
    public sealed class ShaderAlphaFamilyTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        Material Probe(string name, string properties, string pragma = "", string fallback = "")
        {
            var shader = Own(ShaderUtil.CreateShaderAsset("Shader \"Hidden/VRVlogTests/AlphaFamily/" + name + "\"\n{\nProperties\n{\n" + properties + "\n}\n" +
                "SubShader\n{\nPass\n{\nCGPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n" + pragma + "\n#include \"UnityCG.cginc\"\n" +
                "float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }\nfixed4 frag() : SV_Target { return 1; }\nENDCG\n}\n}\n" + fallback + "\n}"));
            Assume.That(ShaderUtil.ShaderHasError(shader), Is.False);
            return Own(new Material(shader) { name = name });
        }

        Texture2D Texture(string name, Color color)
        {
            var texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = name });
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels); texture.Apply();
            return texture;
        }

        Material Substitute(Material source, List<Texture2D> textures = null)
        {
            textures = textures ?? new List<Texture2D>();
            var output = UnsupportedShaderSubstitution.Create(source, out _, out _, out _, textures);
            owned.AddRange(textures);
            Assert.That(output, Is.Not.Null);
            return Own(output);
        }

        static void AssertMask(Material output, float mode, float scale, float value)
        {
            Assert.That(output.GetFloat("_AlphaMaskMode"), Is.EqualTo(mode), "_AlphaMaskMode");
            Assert.That(output.GetFloat("_AlphaMaskScale"), Is.EqualTo(scale).Within(1e-5), "_AlphaMaskScale");
            Assert.That(output.GetFloat("_AlphaMaskValue"), Is.EqualTo(value).Within(1e-5), "_AlphaMaskValue");
        }

        const string Uts = "_MainTex (\"Main\", 2D) = \"white\" {}\n_BaseColor (\"Base\", Color) = (1,1,1,1)\n_ClippingMask (\"Clip\", 2D) = \"white\" {}\n" +
            "_Clipping_Level (\"Level\", Range(0, 1)) = 0\n_Inverse_Clipping (\"Inverse\", Float) = 0\n_IsBaseMapAlphaAsClippingMask (\"Base Alpha\", Float) = 0\n" +
            "_Tweak_transparency (\"Tweak\", Range(-1, 1)) = 0\n_CullMode (\"Cull\", Float) = 2";

        // UTS keeps a pixel where its mask plus the level reaches 0.5: a level
        // is an offset, not a threshold, and the base color's alpha is unused.
        [Test] public void UtsClippingAddsTheLevelToTheClippingMask()
        {
            var main = Texture("main", Color.white); var clip = Texture("clip", Color.gray);
            var source = Probe("Toon_DoubleShadeWithFeather_Clipping", Uts);
            source.SetTexture("_MainTex", main); source.SetTextureScale("_MainTex", new Vector2(2, 1)); source.SetTextureOffset("_MainTex", new Vector2(.25f, 0));
            source.SetTexture("_ClippingMask", clip); source.SetTextureScale("_ClippingMask", new Vector2(2, 2)); source.SetTextureOffset("_ClippingMask", new Vector2(.5f, 0));
            source.SetColor("_BaseColor", new Color(1, 0, 0, 0));
            source.SetFloat("_Clipping_Level", .3f); source.SetFloat("_Inverse_Clipping", 1);

            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            Assert.That(output.GetColor("_Color"), Is.EqualTo(new Color(1, 0, 0, 1)));
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.5f));
            AssertMask(output, 1, -1, 1.3f);
            Assert.That(output.GetTexture("_AlphaMask"), Is.SameAs(clip));
            // UTS2 samples the mask at its own tiling; lilToon on top of the main image's.
            Assert.That(output.GetTextureScale("_AlphaMask"), Is.EqualTo(new Vector2(1, 2)));
            Assert.That(output.GetTextureOffset("_AlphaMask"), Is.EqualTo(new Vector2(.25f, 0)));
        }

        [Test] public void UtsClippingWithoutAMaskStaysVisibleAtAnyLevel()
        {
            var source = Probe("Toon_DoubleShadeWithFeather_Clipping", Uts);
            source.SetTexture("_MainTex", Texture("main", new Color(1, 1, 1, 0))); source.SetFloat("_Clipping_Level", 1);
            var output = Substitute(source);
            Assert.That(output.GetTexture("_AlphaMask"), Is.Null);
            AssertMask(output, 1, 1, 1);
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.5f));
        }

        // The ToonColor and Mobile TransClipping shaders do not blend; they clip
        // the main alpha plus the level.
        [Test] public void UtsToonColorTransClippingClipsTheMainAlphaPlusTheLevel()
        {
            var source = Probe("ToonColor_DoubleShadeWithFeather_TransClipping", Uts);
            source.SetTexture("_MainTex", Texture("main", new Color(1, 1, 1, .4f)));
            source.SetFloat("_IsBaseMapAlphaAsClippingMask", 1); source.SetFloat("_Clipping_Level", .2f);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            AssertMask(output, 3, 0, .2f);
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.5f));
        }

        // The other TransClipping shaders blend m + tweak and clip where m +
        // level falls below 0.5.
        [Test] public void UtsTransClippingBlendsTheMainAlphaPlusTheTweak()
        {
            var source = Probe("Toon_DoubleShadeWithFeather_TransClipping", Uts);
            source.SetTexture("_MainTex", Texture("main", new Color(1, 1, 1, .4f)));
            source.SetFloat("_IsBaseMapAlphaAsClippingMask", 1); source.SetFloat("_Clipping_Level", .2f); source.SetFloat("_Tweak_transparency", .1f);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            AssertMask(output, 3, 0, .1f);
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.4f).Within(1e-5));
        }

        // Only the clipping variants declare _Clipping_Level, so a missing one
        // clips even though the error shader's queue is opaque.
        [Test] public void UtsWithAMissingShaderStillClips()
        {
            var clip = Texture("clip", Color.gray);
            var source = Probe("Toon_DoubleShadeWithFeather_Clipping", Uts);
            source.SetTexture("_ClippingMask", clip); source.SetFloat("_Clipping_Level", .3f);
            source.renderQueue = -1;
            source.shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(source.shader, Is.Not.Null);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            AssertMask(output, 1, 1, .3f);
            Assert.That(output.GetTexture("_AlphaMask"), Is.SameAs(clip));
        }

        [Test] public void UtsTransparentBakesAnInvertedMainAlphaAndShiftsTheCutoffByTheTweak()
        {
            var main = Texture("main", new Color(1, 1, 1, .25f));
            var source = Probe("Toon_DoubleShadeWithFeather_Transparent", Uts);
            source.SetTexture("_MainTex", main);
            source.SetFloat("_IsBaseMapAlphaAsClippingMask", 1); source.SetFloat("_Inverse_Clipping", 1);
            source.SetFloat("_Tweak_transparency", .1f); source.SetFloat("_Clipping_Level", .2f);
            var textures = new List<Texture2D>();
            var output = Substitute(source, textures);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            AssertMask(output, 1, 1, .1f);
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.4f).Within(1e-5));
            Assert.That(textures, Has.Count.EqualTo(1));
            Assert.That(output.GetTexture("_AlphaMask"), Is.SameAs(textures[0]));
            Assert.That(textures[0].GetPixel(1, 1).r, Is.EqualTo(.75f).Within(.01f));
        }

        const string Uts3 = Uts + "\n_ClippingMode (\"Clipping\", Float) = 0\n_TransparentEnabled (\"Transparent\", Float) = 0\n" +
            "_ZWriteMode (\"ZWrite Mode\", Float) = 1\n_ZWrite (\"__zw\", Float) = 1";

        [Test] public void Uts3ReadsItsModeAndDepthFromItsOwnSwitches()
        {
            var clip = Texture("clip", Color.gray);
            var source = Probe("UnityToon", Uts3);
            source.SetTexture("_ClippingMask", clip); source.SetTextureScale("_ClippingMask", new Vector2(3, 3));
            source.SetFloat("_ClippingMode", 2); source.SetFloat("_ZWriteMode", 0);
            source.EnableKeyword("_ALPHATEST_ON");
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            Assert.That(output.GetFloat("_ZWrite"), Is.EqualTo(0f), "The color pass writes depth by _ZWriteMode.");
            AssertMask(output, 1, 1, 0);
            Assert.That(output.GetTextureScale("_AlphaMask"), Is.EqualTo(Vector2.one), "UTS3 samples the mask at the main image's coordinates.");

            source.SetFloat("_ClippingMode", 0);
            Assert.That(Substitute(source).shader.name, Is.EqualTo("lilToon"));
            source.SetFloat("_TransparentEnabled", 1);
            Assert.That(Substitute(source).shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
        }

        const string Wf = "_MainTex (\"Main\", 2D) = \"white\" {}\n_Color (\"Color\", Color) = (1,1,1,1)\n_AL_Source (\"Source\", Float) = 0\n" +
            "_AL_MaskTex (\"Mask\", 2D) = \"white\" {}\n_AL_InvMaskVal (\"Invert\", Float) = 0\n_AL_Power (\"Power\", Range(0, 2)) = 1\n" +
            "_Cutoff (\"Cutoff\", Range(0, 1)) = 0.5\n_AL_ZWrite (\"ZWrite\", Float) = 0\n_CullMode (\"Cull\", Float) = 2";

        [Test] public void UnlitWFReplacesTheAlphaWithItsMaskRedScaledByThePower()
        {
            var mask = Texture("mask", Color.gray);
            var source = Probe("UnToon_Transparent", Wf);
            source.SetFloat("_AL_Source", 1); source.SetTexture("_AL_MaskTex", mask); source.SetTextureScale("_AL_MaskTex", new Vector2(4, 4));
            source.SetFloat("_AL_InvMaskVal", 1); source.SetFloat("_AL_Power", 1.5f);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            AssertMask(output, 1, -1.5f, 1.5f);
            Assert.That(output.GetTexture("_AlphaMask"), Is.SameAs(mask));
            Assert.That(output.GetTextureScale("_AlphaMask"), Is.EqualTo(Vector2.one));
        }

        [Test] public void UnlitWFScalesTheMainAlphaByThePowerThroughTheColor()
        {
            var source = Probe("UnToon_Transparent", Wf);
            source.SetTexture("_MainTex", Texture("main", Color.white)); source.SetColor("_Color", new Color(1, 1, 1, .5f)); source.SetFloat("_AL_Power", 1.5f);
            var output = Substitute(source);
            Assert.That(output.GetColor("_Color").a, Is.EqualTo(.75f).Within(1e-5));
            Assert.That(output.GetFloat("_AlphaMaskMode"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(2f));
        }

        // The blended variants without _CullMode draw both faces in two passes.
        [Test] public void UnlitWFBlendsBothFacesWithoutACullMode()
        {
            var source = Probe("UnToon_TransparentBothFaces", Wf.Replace("\n_CullMode (\"Cull\", Float) = 2", ""));
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(0f));
        }

        [Test] public void UnlitWFBakesTheMaskAlphaForACutout()
        {
            var source = Probe("UnToon_TransCutout", Wf);
            source.SetFloat("_AL_Source", 2); source.SetTexture("_AL_MaskTex", Texture("mask", new Color(1, 0, 0, .3f))); source.SetFloat("_Cutoff", .2f);
            var textures = new List<Texture2D>();
            var output = Substitute(source, textures);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.2f).Within(1e-5));
            AssertMask(output, 1, 1, 0);
            Assert.That(textures, Has.Count.EqualTo(1));
            Assert.That(textures[0].GetPixel(1, 1).r, Is.EqualTo(.3f).Within(.01f));
        }

        const string Sunao = "_MainTex (\"Main\", 2D) = \"white\" {}\n_Color (\"Color\", Color) = (1,1,1,1)\n_Alpha (\"Alpha\", Range(0, 2)) = 1\n" +
            "_Cutout (\"Cutout\", Range(0, 1)) = 0.5\n_AlphaMask (\"Alpha Mask\", 2D) = \"white\" {}\n_AlphaMaskStrength (\"Strength\", Range(0, 1)) = 1\n" +
            "_Culling (\"Culling\", Int) = 0\n_EnableZWrite (\"ZWrite\", Int) = 1";

        [Test] public void SunaoKeepsItsAlphaFactorMaskStrengthAndCulling()
        {
            var mask = Texture("mask", Color.gray);
            var source = Probe("Sunao_Cutout", Sunao);
            source.SetTexture("_AlphaMask", mask); source.SetFloat("_AlphaMaskStrength", .4f);
            source.SetFloat("_Alpha", 2); source.SetFloat("_Cutout", .6f);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.3f).Within(1e-5), "a * 2 >= 0.6 where a >= 0.3.");
            AssertMask(output, 2, .4f, .6f);
            Assert.That(output.GetTexture("_AlphaMask"), Is.SameAs(mask), "A grey mask is used as it is.");
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(0f));
        }

        [Test] public void SunaoBakesTheLuminanceOfAColoredMask()
        {
            var source = Probe("Sunao_Transparent", Sunao);
            source.SetTexture("_AlphaMask", Texture("mask", new Color(1, 0, 0, 1)));
            var textures = new List<Texture2D>();
            var output = Substitute(source, textures);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            AssertMask(output, 2, 1, 0);
            Assert.That(textures, Has.Count.EqualTo(1));
            Assert.That(textures[0].GetPixel(1, 1).r, Is.EqualTo(.2126f).Within(.01f));
        }

        const string Arktoon = "_MainTex (\"Main\", 2D) = \"white\" {}\n_Color (\"Color\", Color) = (1,1,1,1)\n_AlphaMask (\"Alpha Mask\", 2D) = \"white\" {}\n" +
            "_UseDoubleSided (\"Double Sided\", Int) = 0\n_DoubleSidedFlipBackfaceNormal (\"Flip\", Float) = 0\n_ZWrite (\"ZWrite\", Float) = 0";

        [Test] public void ArktoonFadeMultipliesItsAlphaMaskAndDrawsBothSides()
        {
            var mask = Texture("mask", Color.gray);
            var source = Probe("arktoon_Fade", Arktoon);
            source.SetTexture("_MainTex", Texture("main", Color.white)); source.SetTextureScale("_MainTex", new Vector2(2, 2));
            source.SetTexture("_AlphaMask", mask); source.SetFloat("_UseDoubleSided", 1);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            AssertMask(output, 2, 1, 0);
            Assert.That(output.GetTextureScale("_AlphaMask"), Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(0f));
        }

        [Test] public void ArktoonCutoutUsesItsBorderAdjustAsTheCutoff()
        {
            var source = Probe("arktoon_AlphaCutout", Arktoon + "\n_CutoutCutoutAdjust (\"Adjust\", Range(0, 1)) = 0.5");
            source.SetFloat("_CutoutCutoutAdjust", .7f);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.7f).Within(1e-5));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(2f));
        }

        const string MToon = "_MainTex (\"Main\", 2D) = \"white\" {}\n_Color (\"Color\", Color) = (1,1,1,1)\n_Cutoff (\"Cutoff\", Range(0, 1)) = 0.5\n" +
            "_ShadeColor (\"Shade\", Color) = (1,1,1,1)\n_ShadeToony (\"Toony\", Range(0, 1)) = 0.9\n_BlendMode (\"Blend\", Float) = 0\n" +
            "_CullMode (\"Cull\", Float) = 2\n_ZWrite (\"ZWrite\", Float) = 1";

        [TestCase(0, "lilToon")]
        [TestCase(1, "Hidden/lilToonCutout")]
        [TestCase(2, "Hidden/lilToonTransparent")]
        [TestCase(3, "Hidden/lilToonTransparent")]
        public void MToonFollowsItsBlendMode(int blend, string expected)
        {
            var source = Probe("MToon", MToon);
            source.SetFloat("_BlendMode", blend); source.SetFloat("_Cutoff", .3f); source.SetFloat("_ZWrite", blend == 3 ? 1 : 0);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo(expected));
            if (blend == 1) Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.3f).Within(1e-5));
            if (blend >= 2) Assert.That(output.GetFloat("_ZWrite"), Is.EqualTo(blend == 3 ? 1f : 0f));
        }

        [Test] public void AnAlphaTestQueueReadsTheCutoffNamesOfNodeBasedShaders()
        {
            var source = Probe("Generated", "_MainTex (\"Main\", 2D) = \"white\" {}\n_MaskClipValue (\"Mask Clip Value\", Float) = 0.5");
            source.SetFloat("_MaskClipValue", .35f); source.renderQueue = 2450;
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.35f).Within(1e-5));
        }

        // A keyword the shader does not declare was left by an earlier shader.
        [Test] public void AKeywordTheShaderDoesNotDeclareDoesNotMakeItTransparent()
        {
            var source = Probe("Plain", "_MainTex (\"Main\", 2D) = \"white\" {}\n_Color (\"Color\", Color) = (1,1,1,1)");
            source.EnableKeyword("_ALPHABLEND_ON");
            Assume.That(source.shaderKeywords, Does.Contain("_ALPHABLEND_ON"));
            Assert.That(Substitute(source).shader.name, Is.EqualTo("lilToon"));
        }

        // Standard's keywords are in the keyword space of a shader that falls
        // back to it, but its own passes never read them.
        [Test] public void AKeywordOnlyTheFallbackDeclaresDoesNotMakeItTransparent()
        {
            var source = Probe("FallsBack", "_MainTex (\"Main\", 2D) = \"white\" {}\n_Color (\"Color\", Color) = (1,1,1,1)", fallback: "FallBack \"Standard\"");
            source.EnableKeyword("_ALPHABLEND_ON");
            Assume.That(source.shader.keywordSpace.FindKeyword("_ALPHABLEND_ON").isValid, Is.True);
            Assert.That(Substitute(source).shader.name, Is.EqualTo("lilToon"));
        }

        [Test] public void AKeywordTheShaderUsesStillMakesItTransparent()
        {
            var source = Probe("Blends", "_MainTex (\"Main\", 2D) = \"white\" {}\n_Color (\"Color\", Color) = (1,1,1,1)", "#pragma shader_feature_local _ALPHABLEND_ON");
            source.EnableKeyword("_ALPHABLEND_ON");
            Assert.That(Substitute(source).shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
        }

        const string Clips = "fixed4 _Color;\nfixed4 frag() : SV_Target { fixed4 c = _Color; clip(c.a - 0.5); return c; }";
        const string Draws = "fixed4 _Color;\nfixed4 frag() : SV_Target { return _Color; }";
        const string ColorClips = "Shader \"X\" { SubShader { Pass { CGPROGRAM\n" + Clips + "\nENDCG } } }";
        const string ColorDraws = "Shader \"X\" { SubShader { Pass { CGPROGRAM\n" + Draws + "\nENDCG } } }";

        // A shader that clips in a color pass and says so only by a Cutout
        // VRChat fallback.
        [TestCase("ToonCutout", ColorClips, true)]
        [TestCase("ToonCutoutDoubleSided", ColorClips, true)]
        [TestCase("cutout", "Shader \"X\" { SubShader { Pass { CGPROGRAM\nvoid frag() { discard; }\nENDCG } } }", true)]
        [TestCase("ToonCutout", "Shader \"X\" { SubShader { CGPROGRAM\n#pragma surface surf Lambert\nvoid surf() { clip(-1); }\nENDCG } }", true)]
        [TestCase("ToonCutout", ColorDraws, false)]
        [TestCase("ToonCutout", "Shader \"X\" { SubShader { Pass { CGPROGRAM\n// clip(c.a - 0.5);\n" + Draws + "\nENDCG } } }", false)]
        [TestCase("ToonCutout", "Shader \"X\" { SubShader { Pass { CGPROGRAM\n" + Draws + "\nENDCG }\nPass { Tags { \"LightMode\"=\"ShadowCaster\" } CGPROGRAM\n" + Clips + "\nENDCG } } }", false)]
        [TestCase("ToonCutout", "Shader \"X\" { SubShader { Pass { CGPROGRAM\n" + Draws + "\nENDCG } }\nSubShader { Pass { CGPROGRAM\n" + Clips + "\nENDCG } } }", false)]
        [TestCase("ToonCutout", null, false)]
        [TestCase("Toon", ColorClips, false)]
        [TestCase("ToonTransparent", ColorClips, false)]
        [TestCase("NotCutout", ColorClips, false)]
        [TestCase("toonstandardCutout", ColorClips, false)]
        [TestCase("", ColorClips, false)]
        public void AVRChatFallbackClipsOnlyWithACutoutAndAColorPassClip(string fallback, string shaderText, bool expected) =>
            Assert.That(UnsupportedShaderSubstitution.FallbackClips(fallback, shaderText), Is.EqualTo(expected));

        // The tag is read from the shader file's own Tags, and the queue of a
        // blended material still decides Transparent.
        [Test]
        public void AShaderFileWithACutoutFallbackAndAClipBecomesCutout()
        {
            const string path = "Assets/VRVlogTestsFallbackTag.shader";
            string ShaderFile(string name, string tag, string fragment) =>
                "Shader \"Hidden/VRVlogTests/AlphaFamily/" + name + "\"\n{\nProperties\n{\n_MainTex (\"Main\", 2D) = \"white\" {}\n_Color (\"Color\", Color) = (1,1,1,1)\n}\n" +
                "SubShader\n{\nTags { \"RenderType\"=\"Opaque\" " + tag + " }\nPass\n{\nCGPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n" +
                "float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }\n" + fragment + "\nENDCG\n}\n}\n}";
            Material Load(string text)
            {
                System.IO.File.WriteAllText(path, text);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Assume.That(shader, Is.Not.Null);
                return Own(new Material(shader) { name = "Tagged" });
            }
            try
            {
                var source = Load(ShaderFile("TaggedClips", "\"VRCFallback\"=\"ToonCutout\"", Clips));
                var output = Substitute(source);
                Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
                Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.5f));
                source.renderQueue = 3000;
                Assert.That(Substitute(source).shader.name, Is.EqualTo("Hidden/lilToonTransparent"));

                Assert.That(Substitute(Load(ShaderFile("TaggedDraws", "\"VRCFallback\"=\"ToonCutout\"", Draws))).shader.name, Is.EqualTo("lilToon"));
                Assert.That(Substitute(Load(ShaderFile("UntaggedClips", "", Clips))).shader.name, Is.EqualTo("lilToon"));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
    }
}
