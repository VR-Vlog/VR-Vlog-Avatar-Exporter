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
