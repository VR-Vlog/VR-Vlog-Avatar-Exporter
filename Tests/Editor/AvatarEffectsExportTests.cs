using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRVlog.AvatarFormats;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class AvatarEffectsExportTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        [Test]
        public void ModulesKeepTheirSerializedNamesAndValues()
        {
            var root = Root();
            var system = System(root.transform, "Sparkles", 50);
            var main = system.main;
            main.startLifetime = 2.5f;
            main.loop = false;
            var emission = system.emission;
            emission.rateOverTime = 7f;
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.blue, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));

            var particle = Single(Resolve(root, _ => 0));

            Assert.That(particle.Keys, Is.SupersetOf(new[] { "node", "position", "rotation", "scale", "material", "system", "ParticleSystemRenderer" }.Concat(AvatarEffectsExport.WrittenModules)));
            var initial = (Dictionary<string, object>)particle["InitialModule"];
            Assert.That(initial["maxNumParticles"], Is.EqualTo(50L));
            Assert.That((double)((Dictionary<string, object>)initial["startLifetime"])["scalar"], Is.EqualTo(2.5).Within(1e-6));
            Assert.That(((Dictionary<string, object>)particle["system"])["looping"], Is.EqualTo(false));
            var rate = (Dictionary<string, object>)((Dictionary<string, object>)particle["EmissionModule"])["rateOverTime"];
            Assert.That((double)rate["scalar"], Is.EqualTo(7).Within(1e-6));

            var colorModule = (Dictionary<string, object>)particle["ColorModule"];
            Assert.That(colorModule["enabled"], Is.EqualTo(true));
            var written = (Dictionary<string, object>)((Dictionary<string, object>)colorModule["gradient"])["maxGradient"];
            Assert.That(written["mode"], Is.EqualTo("blend"));
            Assert.That(((List<object>)written["colorKeys"]).Cast<List<object>>().Select(k => k.Cast<double>().ToArray()),
                Is.EqualTo(new[] { new double[] { 0, 1, 0, 0 }, new double[] { 1, 0, 0, 1 } }));
            Assert.That(((List<object>)written["alphaKeys"]).Count, Is.EqualTo(2));

            var curve = (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)particle["SizeModule"])["curve"])["maxCurve"];
            var keys = ((List<object>)curve["keys"]).Cast<List<object>>().ToArray();
            Assert.That(keys.Select(k => ((double)k[0], (double)k[1])), Is.EqualTo(new[] { (0.0, 1.0), (1.0, 0.0) }));

            var renderer = (Dictionary<string, object>)particle["ParticleSystemRenderer"];
            Assert.That(renderer["m_Enabled"], Is.EqualTo(true));
            Assert.That(renderer, Contains.Key("m_RenderMode"));
            Assert.That(renderer.Keys, Has.None.EqualTo("m_Materials").And.None.EqualTo("m_SortingOrder").And.None.EqualTo("m_Mesh"));
        }

        [Test]
        public void ModulesInUseThatAreNotWrittenAreCounted()
        {
            var root = Root();
            System(root.transform, "Plain", 10);
            Assert.That(Resolve(root, _ => 0)["omitted"], Is.Empty, "A new system uses only the modules written.");

            var system = System(root.transform, "Busy", 10);
            var noise = system.noise; noise.enabled = true;
            var trails = system.trails; trails.enabled = true;
            var collision = system.collision; collision.enabled = false;

            Assert.That(Omitted(Resolve(root, _ => 0)), Is.EqualTo(new[] { ("particleModule", 2) }));
        }

        [Test]
        public void SystemsOverTheLimitsAreLeftOutInHierarchyOrder()
        {
            var root = Root();
            for (var i = 0; i < AvatarEffectsExport.MaximumParticleSystems + 1; i++) System(root.transform, "System" + i, 10);
            var extension = Resolve(root, _ => 0);
            Assert.That(((List<object>)extension["particles"]).Count, Is.EqualTo(AvatarEffectsExport.MaximumParticleSystems));
            Assert.That(Omitted(extension), Is.EqualTo(new[] { ("effectLimit", 1) }));

            var other = Root();
            System(other.transform, "Large", 1500);
            System(other.transform, "Over", 600);
            System(other.transform, "Fits", 400);
            extension = Resolve(other, _ => 0);
            Assert.That(((List<object>)extension["particles"]).Cast<Dictionary<string, object>>()
                .Select(p => ((Dictionary<string, object>)p["InitialModule"])["maxNumParticles"]), Is.EqualTo(new[] { 1500L, 400L }));
            Assert.That(Omitted(extension), Is.EqualTo(new[] { ("effectLimit", 1) }));
        }

        [Test]
        public void SystemsLeftOutOfTheExportAreNotRead()
        {
            var root = Root();
            System(root.transform, "Kept", 10);
            var removed = System(root.transform, "Removed", 10);
            Assert.That(AvatarEffectsExport.Capture(root, t => t == removed.transform).Count, Is.EqualTo(1));
            Assert.That(AvatarEffectsExport.Capture(Root()), Is.Null);
        }

        [Test]
        public void ASystemHangsFromTheNearestWrittenNodeWithItsPose()
        {
            var root = Root();
            // The avatar is written at the origin with its scale in the model space.
            root.transform.localPosition = new Vector3(5, 0, 0);
            root.transform.localRotation = Quaternion.Euler(0, 30, 0);
            root.transform.localScale = new Vector3(.5f, .5f, .5f);
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0, 1.7f, 0);
            var system = System(head, "Sparkles", 10);
            system.transform.localPosition = new Vector3(0, .1f, .2f);
            system.transform.localRotation = Quaternion.Euler(0, 90, 0);
            system.transform.localScale = new Vector3(2, 2, 2);

            // The object holding the system is removed: it hangs from Head.
            var particle = Single(Resolve(root, go => go.transform == head ? 1 : -1));
            Assert.That(particle["node"], Is.EqualTo(1));
            AssertNumbers(particle["position"], 0, .1, .2);
            var rotation = Quaternion.Euler(0, 90, 0);
            AssertNumbers(particle["rotation"], rotation.x, rotation.y, rotation.z, rotation.w);
            AssertNumbers(particle["scale"], 2, 2, 2);
            Assert.That(particle["active"], Is.EqualTo(true));

            // Nothing above it is written: it hangs from the root in the model space.
            particle = Single(Resolve(root, _ => -1));
            Assert.That(particle["node"], Is.EqualTo(-1));
            AssertNumbers(particle["position"], 0, .9, .1);
            AssertNumbers(particle["rotation"], rotation.x, rotation.y, rotation.z, rotation.w);
            AssertNumbers(particle["scale"], 1, 1, 1);

            // The object itself is written: no offset.
            particle = Single(Resolve(root, go => go.transform == system.transform ? 4 : -1));
            Assert.That(particle["node"], Is.EqualTo(4));
            AssertNumbers(particle["position"], 0, 0, 0);
            AssertNumbers(particle["scale"], 1, 1, 1);

            // A default-off system is written as off.
            system.gameObject.SetActive(false);
            Assert.That(Single(Resolve(root, _ => -1))["active"], Is.EqualTo(false));
        }

        [Test]
        public void RebindTakesThePoseAgainstTheObjectsTheBuildLeft()
        {
            var root = Root();
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0, 1.7f, 0);
            var holder = new GameObject("Holder").transform;
            holder.SetParent(root.transform, false);
            holder.localPosition = new Vector3(0, 1.8f, 0);
            var system = System(holder, "Sparkles", 10);
            system.transform.localPosition = new Vector3(0, 0, .2f);

            var capture = AvatarEffectsExport.Capture(root);
            // The build moves Holder under Head, keeping where it is, and removes the system's object.
            holder.SetParent(head, true);
            Object.DestroyImmediate(system.gameObject);
            capture.Rebind();
            var particle = Single(capture.Resolve(go => go.transform == head ? 1 : -1));
            Assert.That(particle["node"], Is.EqualTo(1));
            AssertNumbers(particle["position"], 0, .1, .2);

            // Holder itself is written.
            particle = Single(capture.Resolve(go => go.transform == holder ? 2 : go.transform == head ? 1 : -1));
            Assert.That(particle["node"], Is.EqualTo(2));
            AssertNumbers(particle["position"], 0, 0, .2);
        }

        [Test]
        public void WhenNothingIsWrittenTheResultLineStillCountsWhatWasLeftOut()
        {
            var root = Root();
            System(root.transform, "Huge", AvatarEffectsExport.MaximumParticles + 1);
            var capture = AvatarEffectsExport.Capture(root);
            Assert.That(capture.Resolve(_ => 0), Is.Null);
            Assert.That(capture.LeftOut, Is.EqualTo(ExporterLocalization.Format(AvatarEffectsExport.ResultNote,
                ExporterLocalization.Format(AvatarEffectsExport.LimitCount, 1))));

            // The whole copy is gone: the systems have nowhere to hang.
            var other = Root();
            var lost = System(other.transform, "Lost", 10);
            Renderer(lost).sharedMaterial = Material("Sprites/Default", Color.red);
            var noise = lost.noise; noise.enabled = true;
            capture = AvatarEffectsExport.Capture(other);
            owned.Remove(other);
            Object.DestroyImmediate(other);
            Assert.That(capture.Resolve(_ => 0), Is.Null);
            // Its module is not counted: only written systems count theirs.
            Assert.That(capture.LeftOut, Is.EqualTo(ExporterLocalization.Format(AvatarEffectsExport.ResultNote,
                ExporterLocalization.Format(AvatarEffectsExport.LimitCount, 1))));
        }

        [Test]
        public void MaterialsBecomeAnUnlitColorAndBlend()
        {
            var root = Root();
            var additive = Material("Legacy Shaders/Particles/Additive", new Color(.25f, .5f, .75f, .5f), "_TintColor");
            additive.mainTexture = Texture();
            var first = System(root.transform, "First", 10);
            var second = System(root.transform, "Second", 10);
            var third = System(root.transform, "Third", 10);
            Renderer(first).sharedMaterial = additive;
            Renderer(second).sharedMaterial = additive;
            Renderer(third).sharedMaterial = Material("Sprites/Default", new Color(1, 1, 1, .5f));
            Object.DestroyImmediate(System(root.transform, "None", 10).GetComponent<ParticleSystemRenderer>());

            var extension = Resolve(root, _ => 0);
            var particles = ((List<object>)extension["particles"]).Cast<Dictionary<string, object>>().ToArray();
            Assert.That(particles.Select(p => p["material"]), Is.EqualTo(new[] { 0, 0, 1, -1 }), "A shared material is one entry.");
            Assert.That(particles[3].ContainsKey("ParticleSystemRenderer"), Is.False);
            var materials = ((List<object>)extension["materials"]).Cast<Dictionary<string, object>>().ToArray();
            // Legacy particle shaders double their tint.
            AssertNumbers(materials[0]["color"], .5, 1, 1, 1);
            Assert.That(materials.Select(m => m["blend"]), Is.EqualTo(new[] { "additive", "alpha" }));
            Assert.That(materials.Select(m => m["softParticles"]), Is.All.EqualTo(false));
            // The main texture goes to the extension's own image table.
            Assert.That(materials[0]["texture"], Is.EqualTo(0));
            Assert.That(materials[1].ContainsKey("texture"), Is.False);
            Assert.That(((List<object>)extension["images"]).Count, Is.EqualTo(1));
            Assert.That(Omitted(extension), Is.Empty);
        }

        [Test]
        public void TrailsAndLinesKeepTheirSettingsAndShareTheMaterials()
        {
            var root = Root();
            var shared = Material("Sprites/Default", new Color(1, .5f, 0, 1));
            var trailObject = Child(root.transform, "Trail");
            var trail = trailObject.AddComponent<TrailRenderer>();
            trail.time = 1.5f;
            trail.widthCurve = AnimationCurve.Linear(0, .2f, 1, 0);
            trail.sharedMaterial = shared;
            var lineObject = Child(root.transform, "Line");
            lineObject.transform.localPosition = new Vector3(0, 1, 0);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPositions(new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 2) });
            line.sharedMaterial = shared;
            line.enabled = false;

            var extension = Resolve(root, _ => -1);
            var trails = ((List<object>)extension["trails"]).Cast<Dictionary<string, object>>().Single();
            var lines = ((List<object>)extension["lines"]).Cast<Dictionary<string, object>>().Single();
            Assert.That(extension["particles"], Is.Empty);
            Assert.That(extension["lights"], Is.Empty);
            Assert.That(trails["material"], Is.EqualTo(0));
            Assert.That(lines["material"], Is.EqualTo(0), "A material shared by a trail and a line is one entry.");
            Assert.That(((List<object>)extension["materials"]).Count, Is.EqualTo(1));

            var trailValues = (Dictionary<string, object>)trails["TrailRenderer"];
            Assert.That((double)trailValues["m_Time"], Is.EqualTo(1.5).Within(1e-6));
            Assert.That(trailValues["m_Enabled"], Is.EqualTo(true));
            var parameters = (Dictionary<string, object>)trailValues["m_Parameters"];
            Assert.That(parameters, Contains.Key("widthCurve").And.ContainKey("colorGradient"));
            var keys = ((List<object>)((Dictionary<string, object>)parameters["widthCurve"])["keys"]).Cast<List<object>>().ToArray();
            Assert.That(keys.Select(k => ((double)k[0], Math.Round((double)k[1], 4))), Is.EqualTo(new[] { (0.0, .2), (1.0, 0.0) }));
            Assert.That(trailValues.Keys, Has.None.EqualTo("m_Materials").And.None.EqualTo("m_SortingOrder"));

            // A line in world space is written in its own space.
            var lineValues = (Dictionary<string, object>)lines["LineRenderer"];
            Assert.That(lineValues["m_Enabled"], Is.EqualTo(false));
            Assert.That(lineValues["m_UseWorldSpace"], Is.EqualTo(false));
            var points = ((List<object>)lineValues["m_Positions"]).ToArray();
            AssertNumbers(points[0], 0, 0, 0);
            AssertNumbers(points[1], 0, 0, 2);
            AssertNumbers(lines["position"], 0, 1, 0);
        }

        [Test]
        public void TrailsAndLinesShareOneLimit()
        {
            var root = Root();
            for (var i = 0; i < AvatarEffectsExport.MaximumTrailsAndLines; i++)
                Child(root.transform, "Stroke" + i).AddComponent(i % 2 == 0 ? typeof(TrailRenderer) : typeof(LineRenderer));
            Child(root.transform, "Over").AddComponent<LineRenderer>();
            var extension = Resolve(root, _ => 0);
            Assert.That(((List<object>)extension["trails"]).Count + ((List<object>)extension["lines"]).Count, Is.EqualTo(AvatarEffectsExport.MaximumTrailsAndLines));
            Assert.That(Omitted(extension), Is.EqualTo(new[] { ("effectLimit", 1) }));
        }

        [Test]
        public void LightsKeepTheirTypeColorIntensityRangeAndSpotAngle()
        {
            var root = Root();
            // VRChat removes EditorOnly objects at upload; a preview light under one takes no slot.
            var preview = Child(root.transform, "Preview");
            preview.tag = "EditorOnly";
            Child(preview.transform, "PreviewLight").AddComponent<Light>().lightmapBakeType = LightmapBakeType.Realtime;
            var spotObject = Child(root.transform, "Spot");
            spotObject.transform.localRotation = UnityEngine.Quaternion.Euler(90, 0, 0);
            var spot = spotObject.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(1, .5f, .25f, 1);
            spot.intensity = 2;
            spot.range = 3;
            spot.spotAngle = 40;
            spot.lightmapBakeType = LightmapBakeType.Realtime;
            // Area and baked lights light nothing on an avatar at run time.
            var area = Child(root.transform, "Area").AddComponent<Light>();
            area.type = LightType.Rectangle;
            var baked = Child(root.transform, "Baked").AddComponent<Light>();
            baked.lightmapBakeType = LightmapBakeType.Baked;
            var point = Child(root.transform, "Point").AddComponent<Light>();
            point.type = LightType.Point;
            point.lightmapBakeType = LightmapBakeType.Mixed;
            point.enabled = false;
            var over = Child(root.transform, "Over").AddComponent<Light>();
            over.lightmapBakeType = LightmapBakeType.Realtime;

            var extension = Resolve(root, _ => -1);
            var lights = ((List<object>)extension["lights"]).Cast<Dictionary<string, object>>().ToArray();
            Assert.That(lights.Select(l => l["type"]), Is.EqualTo(new[] { "spot", "point" }));
            Assert.That(lights.Select(l => l["enabled"]), Is.EqualTo(new[] { true, false }));
            AssertNumbers(lights[0]["color"], 1, .5, .25, 1);
            Assert.That((double)lights[0]["intensity"], Is.EqualTo(2).Within(1e-6));
            Assert.That((double)lights[0]["range"], Is.EqualTo(3).Within(1e-6));
            Assert.That((double)lights[0]["spotAngle"], Is.EqualTo(40).Within(1e-6));
            AssertNumbers(lights[0]["rotation"], Mathf.Sin(Mathf.PI / 4), 0, 0, Mathf.Cos(Mathf.PI / 4));
            Assert.That(lights[0].ContainsKey("material"), Is.False);
            Assert.That(Omitted(extension), Is.EqualTo(new[] { ("effectLimit", 1) }), "Only the third run-time light is over the limit.");
        }

        [Test]
        public void ImagesArePngsInTheBinaryChunkOutsideTheGltfTextures()
        {
            var texture = Texture();
            texture.SetPixels32(new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 128), new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 0) });
            texture.Apply();
            var material = Material("Sprites/Default", Color.white);
            material.mainTexture = texture;
            var root = Root();
            Renderer(System(root.transform, "Sparkles", 10)).sharedMaterial = material;
            Child(root.transform, "Trail").AddComponent<TrailRenderer>().sharedMaterial = material;

            var binary = new byte[] { 1, 2, 3 };
            var bytes = GlbDocument.Create(new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["nodes"] = new List<object> { new Dictionary<string, object>() },
                ["buffers"] = new List<object> { new Dictionary<string, object> { ["byteLength"] = 3L } },
                ["bufferViews"] = new List<object> { new Dictionary<string, object> { ["buffer"] = 0, ["byteOffset"] = 0, ["byteLength"] = 3 } }
            }, binary).Write();
            var resolved = Resolve(root, _ => 0);
            var document = GlbDocument.Read(AvatarEffectsExport.Inject(bytes, resolved));
            Assert.That(((List<object>)resolved["images"]).Cast<Dictionary<string, object>>().Single(), Contains.Key("bytes"), "Inject does not change the extension given.");

            Assert.That(document.Json.ContainsKey("images") || document.Json.ContainsKey("textures"), Is.False);
            var extension = (Dictionary<string, object>)((Dictionary<string, object>)document.Json["extensions"])[AvatarEffectsExport.Extension];
            var image = ((List<object>)extension["images"]).Cast<Dictionary<string, object>>().Single();
            Assert.That(image.Keys, Is.EquivalentTo(new[] { "bufferView", "mimeType" }));
            Assert.That(image["mimeType"], Is.EqualTo("image/png"));
            var view = (Dictionary<string, object>)((List<object>)document.Json["bufferViews"])[Convert.ToInt32(image["bufferView"])];
            Assert.That(Convert.ToInt32(image["bufferView"]), Is.EqualTo(1));
            var png = new byte[Convert.ToInt32(view["byteLength"])];
            Buffer.BlockCopy(document.Binary, Convert.ToInt32(view["byteOffset"]), png, 0, png.Length);
            Assert.That(document.Binary.Take(3), Is.EqualTo(binary), "The binary chunk keeps what was there.");

            var decoded = new Texture2D(1, 1);
            owned.Add(decoded);
            Assert.That(decoded.LoadImage(png), Is.True);
            Assert.That(decoded.GetPixels32(), Is.EqualTo(texture.GetPixels32()));
        }

        [Test]
        public void LargeImagesAreScaledDownToTheLongSide()
        {
            var texture = new Texture2D(1024, 256, TextureFormat.RGBA32, false);
            owned.Add(texture);
            var decoded = new Texture2D(1, 1);
            owned.Add(decoded);
            Assert.That(decoded.LoadImage(AvatarEffectsExport.Png(texture)), Is.True);
            Assert.That((decoded.width, decoded.height), Is.EqualTo((AvatarEffectsExport.MaximumTextureSize, AvatarEffectsExport.MaximumTextureSize / 4)));
        }

        [Test]
        public void BlendFollowsTheBlendFactorsThenTheQueue()
        {
            var standard = Material("Standard", Color.white);
            standard.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            Assert.That(AvatarEffectsExport.Blend(standard), Is.EqualTo("additive"));
            standard.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            Assert.That(AvatarEffectsExport.Blend(standard), Is.EqualTo("alpha"));
            standard.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            Assert.That(AvatarEffectsExport.Blend(standard), Is.EqualTo("opaque"));

            var unlit = Material("Unlit/Color", Color.white);
            Assert.That(AvatarEffectsExport.Blend(unlit), Is.EqualTo("opaque"));
            unlit.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            Assert.That(AvatarEffectsExport.Blend(unlit), Is.EqualTo("alpha"));
        }

        [Test]
        public void InjectChecksTheIndicesAndPlacesTheExtension()
        {
            var bytes = GlbDocument.Create(new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["nodes"] = new List<object> { new Dictionary<string, object>(), new Dictionary<string, object>() }
            }, Array.Empty<byte>()).Write();
            Assert.That(AvatarEffectsExport.Inject(bytes, null), Is.SameAs(bytes));

            var root = Root();
            System(root.transform, "Sparkles", 10);
            var json = GlbDocument.Read(AvatarEffectsExport.Inject(bytes, Resolve(root, _ => 1))).Json;
            Assert.That((List<object>)json["extensionsUsed"], Has.Member(AvatarEffectsExport.Extension));
            var extension = (Dictionary<string, object>)((Dictionary<string, object>)json["extensions"])[AvatarEffectsExport.Extension];
            Assert.That(Convert.ToInt32(extension["schemaVersion"]), Is.EqualTo(1));

            Assert.Throws<InvalidOperationException>(() => AvatarEffectsExport.Inject(bytes, Resolve(root, _ => 2)));
            Assert.DoesNotThrow(() => AvatarEffectsExport.Inject(bytes, Resolve(root, _ => -1)), "-1 is the avatar root.");
            var wrong = Resolve(root, _ => 0);
            ((Dictionary<string, object>)((List<object>)wrong["particles"])[0])["material"] = 3;
            Assert.Throws<InvalidOperationException>(() => AvatarEffectsExport.Inject(bytes, wrong));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExportWritesTheParticlesUnderTheOutputNodes(bool autoExclude)
        {
            using var avatar = new AttachmentConnectionTests.Fixture();
            var head = avatar.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            var go = new GameObject("Sparkles");
            go.transform.SetParent(head, false);
            go.transform.localPosition = new Vector3(0, .1f, 0);
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main; main.maxParticles = 30;
            var noise = system.noise; noise.enabled = true;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Material("Sprites/Default", Color.white);

            var warnings = new List<string>();
            var json = GlbDocument.Read(UniVrmOneClickExporter.Export(avatar.Source, "Avatar effects", "Tests", warnings: warnings,
                exporterVersion: "0.11.8", lilToonVersion: "2.3.4",
                gimmickOptions: new ExportGimmickOptions { AutoExclude = autoExclude },
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None })).Json;

            Assert.That((List<object>)json["extensionsUsed"], Has.Member(AvatarEffectsExport.Extension), string.Join("\n", warnings));
            Assert.That(json.TryGetValue("extensionsRequired", out var required) && ((List<object>)required).Contains(AvatarEffectsExport.Extension), Is.False);
            var extras = (Dictionary<string, object>)((Dictionary<string, object>)json["asset"])["extras"];
            Assert.That(((Dictionary<string, object>)extras[AvatarFormatVersions.RequiredAppVersionsKey])[AvatarEffectsExport.Extension], Is.EqualTo("1.2.8"));

            var extension = (Dictionary<string, object>)((Dictionary<string, object>)json["extensions"])[AvatarEffectsExport.Extension];
            var particle = (Dictionary<string, object>)((List<object>)extension["particles"]).Single();
            var nodes = ((List<object>)json["nodes"]).Cast<Dictionary<string, object>>().Select(n => n.TryGetValue("name", out var name) ? (string)name : "").ToArray();
            var node = nodes[Convert.ToInt32(particle["node"])];
            Assert.That(node, Is.EqualTo("Sparkles").Or.EqualTo(head.name), "The system hangs from its own object or the head.");
            var y = Convert.ToDouble(((List<object>)particle["position"])[1]);
            Assert.That(y, Is.EqualTo(node == "Sparkles" ? 0 : .1).Within(1e-4));
            Assert.That(Convert.ToInt64(((Dictionary<string, object>)particle["InitialModule"])["maxNumParticles"]), Is.EqualTo(30));
            Assert.That(warnings, Has.Member(ExporterLocalization.Format(AvatarEffectsExport.ResultNote, ExporterLocalization.Format(AvatarEffectsExport.ModuleCount, 1))));
        }

        static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        GameObject Root()
        {
            var root = new GameObject("Avatar");
            owned.Add(root);
            return root;
        }

        static ParticleSystem System(Transform parent, string name, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.maxParticles = maxParticles;
            // The editor may give a new renderer its default material, which has a texture.
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = null;
            return system;
        }

        static ParticleSystemRenderer Renderer(ParticleSystem system) => system.GetComponent<ParticleSystemRenderer>();

        Material Material(string shader, Color color, string property = "_Color")
        {
            var found = Shader.Find(shader);
            if (found == null) Assert.Ignore("The built-in shader " + shader + " is not available.");
            var material = new Material(found);
            material.SetColor(property, color);
            owned.Add(material);
            return material;
        }

        Texture2D Texture()
        {
            var texture = new Texture2D(2, 2);
            owned.Add(texture);
            return texture;
        }

        static Dictionary<string, object> Resolve(GameObject root, Func<GameObject, int> node) =>
            AvatarEffectsExport.Capture(root).Resolve(node);

        static Dictionary<string, object> Single(Dictionary<string, object> extension) =>
            (Dictionary<string, object>)((List<object>)extension["particles"]).Single();

        static (string, int)[] Omitted(Dictionary<string, object> extension) =>
            ((List<object>)extension["omitted"]).Cast<Dictionary<string, object>>().Select(o => ((string)o["kind"], (int)o["count"])).ToArray();

        static void AssertNumbers(object value, params double[] expected)
        {
            var actual = ((List<object>)value).Select(Convert.ToDouble).ToArray();
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-4), "index " + i);
        }
    }
}
