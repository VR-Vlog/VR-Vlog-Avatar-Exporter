using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using UnityEditor;
using UnityEngine;
using F = VRVlog.LilToon.LilToonFullContract;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // 自動で軽量化する: lilToon texture slots of switched-off features are written unassigned.
    public sealed class LilToonInactiveSlotsTests
    {
        // A row is sampled only while its switch is on. Multi selects features
        // with keywords, and gem and refraction blur read the smoothness texture
        // without _UseReflection, so those keep the slot. A slot not in the table
        // or a switch the shader lacks keeps it too.
        [TestCase("lilToon", "_Main2ndTex", "_UseMain2ndTex", 0f, false)]
        [TestCase("lilToon", "_Main2ndTex", "_UseMain2ndTex", 1f, true)]
        [TestCase("lilToon", "_Main3rdBlendMask", "_UseMain3rdTex", 0f, false)]
        [TestCase("lilToon", "_BumpMap", "_UseBumpMap", 0f, false)]
        [TestCase("lilToon", "_Bump2ndScaleMask", "_UseBump2ndMap", 0f, false)]
        [TestCase("lilToon", "_AnisotropyShiftNoiseMask", "_UseAnisotropy", 0f, false)]
        [TestCase("lilToon", "_ShadowColorTex", "_UseShadow", 0f, false)]
        [TestCase("lilToon", "_ShadowColorTex", "_UseShadow", 1f, true)]
        [TestCase("lilToon", "_RimShadeMask", "_UseRimShade", 0f, false)]
        [TestCase("lilToon", "_BacklightColorTex", "_UseBacklight", 0f, false)]
        [TestCase("lilToon", "_SmoothnessTex", "_UseReflection", 0f, false)]
        [TestCase("lilToon", "_MatCapTex", "_UseMatCap", 0f, false)]
        [TestCase("lilToon", "_MatCap2ndBumpMap", "_UseMatCap2nd", 0f, false)]
        [TestCase("lilToon", "_RimColorTex", "_UseRim", 0f, false)]
        [TestCase("lilToon", "_GlitterShapeTex", "_UseGlitter", 0f, false)]
        [TestCase("lilToon", "_EmissionMap", "_UseEmission", 0f, false)]
        [TestCase("lilToon", "_EmissionMap", "_UseEmission", 1f, true)]
        [TestCase("lilToon", "_Emission2ndGradTex", "_UseEmission2nd", 0f, false)]
        [TestCase("lilToon", "_ParallaxMap", "_UseParallax", 0f, false)]
        [TestCase("lilToon", "_AlphaMask", "_AlphaMaskMode", 0f, false)]
        [TestCase("lilToon", "_AlphaMask", "_AlphaMaskMode", 2f, true)]
        [TestCase("lilToon", "_MainTex", "_UseMain2ndTex", 0f, true)]
        [TestCase("lilToon", "_MainColorAdjustMask", "_UseMain2ndTex", 0f, true)]
        [TestCase("Hidden/lilToonLite", "_EmissionMap", "_UseEmission", 0f, false)]
        [TestCase("Hidden/lilToonLite", "_ShadowColorTex", "_UseShadow", 0f, false)]
        [TestCase("Hidden/lilToonGem", "_SmoothnessTex", "_UseReflection", 0f, true)]
        [TestCase("Hidden/lilToonRefractionBlur", "_SmoothnessTex", "_UseReflection", 0f, true)]
        [TestCase("Hidden/lilToonRefraction", "_SmoothnessTex", "_UseReflection", 0f, false)]
        [TestCase("_lil/lilToonMulti", "_Main2ndTex", "_UseMain2ndTex", 0f, true)]
        public void ASlotIsSampledOnlyWhileItsSwitchIsOn(string shaderName, string slot, string feature, float value, bool sampled)
        {
            var material = new Material(Find(shaderName));
            try
            {
                Assert.That(material.HasProperty(feature), Is.True, feature);
                material.SetFloat(feature, value);
                Assert.That(LilToonInactiveSlots.Sampled(material, slot), Is.EqualTo(sampled));
            }
            finally { Object.DestroyImmediate(material); }
        }

        // Named like an official shader but without _UseShadow: the slot is
        // kept. So is any slot of a shader that is not lilToon 2.3.4.
        [Test]
        public void AMissingSwitchCountsAsSampled()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"_lil/[Optional] lilToonFakeShadow\"\n{\n    Properties\n    {\n" +
                "        _ShadowColorTex (\"t\", 2D) = \"white\" {}\n    }\n    SubShader\n    {\n        Pass\n        {\n" +
                Program + "        }\n    }\n}\n");
            var material = new Material(shader);
            var standard = new Material(Find("Standard"));
            try
            {
                Assert.That(material.shader.name, Is.EqualTo("_lil/[Optional] lilToonFakeShadow"));
                Assert.That(material.HasProperty("_UseShadow"), Is.False);
                Assert.That(LilToonInactiveSlots.Sampled(material, "_ShadowColorTex"), Is.True);
                Assert.That(LilToonInactiveSlots.Sampled(standard, "_ShadowColorTex"), Is.True);
                Assert.That(LilToonInactiveSlots.Sampled(null, "_ShadowColorTex"), Is.True);
                material.shader = Find("lilToon");
                material.SetFloat("_UseShadow", 0);
                Assert.That(LilToonInactiveSlots.Sampled(material, "_ShadowColorTex"), Is.False, "The same slot with the switch present and off.");
            }
            finally
            {
                Object.DestroyImmediate(standard);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [TestCase(0f, false)]
        [TestCase(1f, true)]
        [TestCase(3f, true)]
        public void DissolveSlotsFollowTheDissolveMode(float mode, bool sampled)
        {
            var material = new Material(Find("lilToon"));
            try
            {
                material.SetVector("_DissolveParams", new Vector4(mode, 1, .5f, .1f));
                Assert.That(LilToonInactiveSlots.Sampled(material, "_DissolveMask"), Is.EqualTo(sampled));
                Assert.That(LilToonInactiveSlots.Sampled(material, "_DissolveNoiseMask"), Is.EqualTo(sampled));
            }
            finally { Object.DestroyImmediate(material); }
        }

        // Only outline passes read the outline slots: a shader without one never
        // does, and one whose outline passes are all switched off neither.
        [TestCase("lilToon", false, false)]
        [TestCase("Hidden/lilToonOutline", false, true)]
        [TestCase("Hidden/lilToonOutline", true, false)]
        [TestCase("Hidden/lilToonLiteOutline", false, true)]
        public void OutlineSlotsNeedAnOutlinePassThatIsDrawn(string shaderName, bool passesOff, bool sampled)
        {
            var material = new Material(Find(shaderName));
            try
            {
                if (passesOff)
                    foreach (var mode in new[] { "ForwardBase", "ForwardAdd", "ShadowCaster" }) material.SetShaderPassEnabled(mode, false);
                foreach (var slot in new[] { "_OutlineTex", "_OutlineWidthMask", "_OutlineVectorTex" })
                    Assert.That(LilToonInactiveSlots.Sampled(material, slot), Is.EqualTo(sampled), slot);
            }
            finally { Object.DestroyImmediate(material); }
        }

        // Off, the switched-off slots keep their textures as before. On, they are
        // unassigned and the texture only they used is not written; the line
        // states the one texture and the memory it would have used.
        [TestCase(false)]
        [TestCase(true)]
        public void OnlyTheOptionLeavesOutTexturesOfSwitchedOffSlots(bool on)
        {
            using var fixture = new Fixture();
            var unused = Image("Unused second layer", 64);
            try
            {
                fixture.Material.SetTexture("_Main2ndTex", unused);
                fixture.Material.SetFloat("_UseMain2ndTex", 0);
                var snapshot = LilToonFullSnapshot.Capture(fixture.Avatar, omitInactiveSlots: on);
                var record = Record(snapshot, fixture.Material);
                Assert.That(TextureId(record, "_Main2ndTex") == -1, Is.EqualTo(on));
                Assert.That(Names(snapshot), on ? (IConstraint)Has.No.Member(unused.name) : Has.Member(unused.name));
                Assert.That(F.List(record, "values").Select(F.Object).Single(v => F.Text(v, "name") == "_UseMain2ndTex")["value"],
                    Is.EqualTo(new List<object> { 0f, 0f, 0f, 0f }), "The switch itself is unchanged.");
                Assert.That(snapshot.InactiveSlotsLine(), on
                    ? (IConstraint)Is.EqualTo(string.Format(CultureInfo.InvariantCulture, ExportNoteText.InactiveTexturesOmitted, 1,
                        TexelDensity.Plan.Megabytes(TexelDensity.Bytes(64, 64))))
                    : Is.Null);
                Assert.That(fixture.Material.GetTexture("_Main2ndTex"), Is.SameAs(unused), "The source material is not changed.");
            }
            finally { Object.DestroyImmediate(unused); }
        }

        // A texture a sampled slot also uses is still written, once.
        [Test]
        public void ATextureASampledSlotUsesIsKept()
        {
            using var fixture = new Fixture();
            var shared = Image("Shared main and emission", 32);
            try
            {
                fixture.Material.SetTexture("_MainTex", shared);
                fixture.Material.SetTexture("_EmissionMap", shared);
                fixture.Material.SetFloat("_UseEmission", 0);
                var snapshot = LilToonFullSnapshot.Capture(fixture.Avatar, omitInactiveSlots: true);
                var record = Record(snapshot, fixture.Material);
                Assert.That(TextureId(record, "_EmissionMap"), Is.EqualTo(-1));
                var id = TextureId(record, "_MainTex");
                Assert.That(F.Text(F.Object(Textures(snapshot)[id]), "name"), Is.EqualTo(shared.name));
                Assert.That(Names(snapshot).Count(name => name == shared.name), Is.EqualTo(1));
                Assert.That(snapshot.InactiveSlotsLine(), Is.Null);
            }
            finally { Object.DestroyImmediate(shared); }
        }

        // A switched-off normal slot gets the default normal texture an
        // unassigned normal slot gets.
        [Test]
        public void ASwitchedOffNormalSlotGetsTheDefaultNormalTexture()
        {
            using var fixture = new Fixture();
            var normal = Image("Switched-off normal", 32);
            try
            {
                fixture.Material.SetTexture("_BumpMap", normal);
                fixture.Material.SetFloat("_UseBumpMap", 0);
                fixture.Material.SetTexture("_Bump2ndMap", null);
                var snapshot = LilToonFullSnapshot.Capture(fixture.Avatar, omitInactiveSlots: true);
                var record = Record(snapshot, fixture.Material);
                var id = TextureId(record, "_BumpMap");
                Assert.That(id, Is.Not.EqualTo(-1));
                Assert.That(id, Is.EqualTo(TextureId(record, "_Bump2ndMap")), "The same default as the unassigned second normal map.");
                Assert.That(F.Text(F.Object(Textures(snapshot)[id]), "name"), Is.EqualTo(Texture2D.normalTexture.name));
                Assert.That(Names(snapshot), Has.No.Member(normal.name));
                Assert.That(snapshot.InactiveSlotsLine(), Is.Not.Null);
            }
            finally { Object.DestroyImmediate(normal); }
        }

        // Off, nothing new runs: an export with the option off writes the same
        // bytes whether or not a switched-off slot holds a texture the
        // exporter would leave out on.
        [Test]
        public void OffWritesTheSameExtensionAsBefore()
        {
            using var fixture = new Fixture();
            var unused = Image("Off unused", 16);
            try
            {
                fixture.Material.SetTexture("_EmissionMap", unused);
                fixture.Material.SetFloat("_UseEmission", 0);
                var before = Serialized(LilToonFullSnapshot.Capture(fixture.Avatar));
                var off = LilToonFullSnapshot.Capture(fixture.Avatar, omitInactiveSlots: false);
                Assert.That(Serialized(off), Is.EqualTo(before));
                Assert.That(off.InactiveSlotsLine(), Is.Null);
            }
            finally { Object.DestroyImmediate(unused); }
        }

        // Through the exporter: the line is in the result list only when on.
        [TestCase(false)]
        [TestCase(true)]
        public void TheExportListsTheLineOnlyWhenTheOptionIsOn(bool on)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Find("lilToon")) { name = "Inactive slot material" };
            var unused = Image("Inactive slot emission", 64);
            try
            {
                material.SetTexture("_EmissionMap", unused);
                material.SetFloat("_UseEmission", 0);
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Inactive slots", "Tests", warnings,
                    exporterVersion: "tests", lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None },
                    autoLightweight: on);
                var glb = GlbDocument.Read(bytes);
                var names = F.List(F.Root(glb.Json), "textures").Select(F.Object).Select(t => F.Text(t, "name")).ToList();
                Assert.That(names, on ? (IConstraint)Has.No.Member(unused.name) : Has.Member(unused.name));
                var line = string.Format(CultureInfo.InvariantCulture, ExportNoteText.InactiveTexturesOmitted, 1, TexelDensity.Plan.Megabytes(TexelDensity.Bytes(64, 64)));
                Assert.That(warnings.Count(w => w == line), Is.EqualTo(on ? 1 : 0));
            }
            finally
            {
                Object.DestroyImmediate(unused);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TheTextFollowsTheWordingRulesAndExistsInEveryLocale()
        {
            var text = ExportNoteText.InactiveTexturesOmitted;
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, "[。、！!\n]|ください|できます|\\d \\S"), Is.False, text);
            Assert.That(string.Format(CultureInfo.InvariantCulture, text, 2, "1.5"),
                Is.EqualTo("オフの機能だけが使うテクスチャ2枚を書き出さずメモリ使用量を1.5MB減らしました"));
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
                Assert.That(File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json")),
                    Does.Contain("{\"source\":\"" + text + "\",\"translated\":\""), locale);
        }

        const string Program = @"            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 vert(float4 position : POSITION) : SV_POSITION { return UnityObjectToClipPos(position); }
            fixed4 frag() : SV_Target { return 0; }
            ENDCG
";

        static Shader Find(string name)
        {
            var shader = Shader.Find(name);
            Assert.That(shader, Is.Not.Null, name);
            return shader;
        }

        static Texture2D Image(string name, int size)
        {
            var image = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name };
            image.SetPixels32(Enumerable.Repeat(new Color32(90, 140, 200, 255), size * size).ToArray());
            image.Apply(true, false);
            return image;
        }

        static Dictionary<string, object> Record(LilToonFullSnapshot snapshot, Material material) =>
            ((Dictionary<Material, Dictionary<string, object>>)typeof(LilToonFullSnapshot)
                .GetField("records", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(snapshot))[material];

        static List<object> Textures(LilToonFullSnapshot snapshot) =>
            (List<object>)typeof(LilToonFullSnapshot).GetField("textures", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(snapshot);

        static List<string> Names(LilToonFullSnapshot snapshot) => Textures(snapshot).Select(F.Object).Select(t => F.Text(t, "name")).ToList();

        static int TextureId(Dictionary<string, object> record, string property) =>
            F.Int(F.List(record, "textures").Select(F.Object).Single(value => F.Text(value, "name") == property), "texture");

        static string Serialized(LilToonFullSnapshot snapshot)
        {
            var records = (Dictionary<Material, Dictionary<string, object>>)typeof(LilToonFullSnapshot)
                .GetField("records", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(snapshot);
            return JsonDom.Serialize(records.Values.Cast<object>().ToList()) + JsonDom.Serialize(Textures(snapshot));
        }

        // One triangle on a MeshRenderer with a lilToon material.
        sealed class Fixture : IDisposable
        {
            internal readonly GameObject Avatar = new GameObject("Inactive slots surface");
            internal readonly Material Material;
            readonly Mesh mesh;

            internal Fixture()
            {
                Material = new Material(Find("lilToon"));
                mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                Avatar.AddComponent<MeshFilter>().sharedMesh = mesh;
                Avatar.AddComponent<MeshRenderer>().sharedMaterial = Material;
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Avatar);
                Object.DestroyImmediate(Material);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
