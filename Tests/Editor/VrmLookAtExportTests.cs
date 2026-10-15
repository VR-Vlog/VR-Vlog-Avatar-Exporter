using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UniGLTF;
using UniGLTF.Extensions.VRMC_vrm;
using UniVRM10;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class VrmLookAtExportTests
    {
        const float Tolerance = .01f;

        [Test]
        public async Task EyesWithoutEyeLookUseTheEyeMidpointAndTheDefaultRange()
        {
            using var fixture = LilToonFixture(eyes: true);
            var warnings = new List<string>();
            var bytes = Export(fixture, warnings);
            var lookAt = LookAt(bytes);
            Assert.That(lookAt["type"], Is.EqualTo("bone"));
            // Head (0, 1.75, 0), eyes at (+-0.03, 1.8, 0.05). glTF mirrors X.
            AssertVector(lookAt["offsetFromHeadBone"], new Vector3(0, .05f, .05f));
            foreach (var key in RangeKeys) AssertRange(lookAt[key], 90, 10);
            Assert.That(warnings, Has.None.Contains(ExporterLocalization.T(VrmLookAtExport.UnmeasuredWarning)));
            // UniVRM reads the same values back in Unity coordinates.
            var imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
            try
            {
                var settings = imported.Vrm.LookAt;
                Assert.That(settings.LookAtType, Is.EqualTo(LookAtType.bone));
                Assert.That(Vector3.Distance(settings.OffsetFromHead, new Vector3(0, .05f, .05f)), Is.LessThan(1e-4f));
                Assert.That(settings.HorizontalInner.CurveXRangeDegree, Is.EqualTo(90).Within(Tolerance));
                Assert.That(settings.HorizontalInner.CurveYRangeDegree, Is.EqualTo(10).Within(Tolerance));
            }
            finally { Object.DestroyImmediate(imported.gameObject); }
        }

        [Test]
        public async Task MeasuredEyeLookPosesBecomeOneToOneRangeMaps()
        {
            using var fixture = LilToonFixture(eyes: true);
            var animator = fixture.Source.GetComponent<Animator>();
            var left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            // Positive X rotation turns +Z downward; positive Y turns it toward +X.
            EnableEyeLook(fixture.Source, left, right, new Dictionary<string, (Quaternion Left, Quaternion Right)>
            {
                ["eyesLookingStraight"] = (Quaternion.identity, Quaternion.identity),
                ["eyesLookingUp"] = (Quaternion.Euler(-12, 0, 0), Quaternion.Euler(-14, 0, 0)),
                ["eyesLookingDown"] = (Quaternion.Euler(16, 0, 0), Quaternion.Euler(15, 0, 0)),
                ["eyesLookingLeft"] = (Quaternion.Euler(0, -30, 0), Quaternion.Euler(0, -20, 0)),
                ["eyesLookingRight"] = (Quaternion.Euler(0, 20, 0), Quaternion.Euler(0, 30, 0)),
            });
            var warnings = new List<string>();
            var bytes = Export(fixture, warnings);
            var lookAt = LookAt(bytes);
            Assert.That(lookAt["type"], Is.EqualTo("bone"));
            AssertVector(lookAt["offsetFromHeadBone"], new Vector3(0, .05f, .05f));
            // Each eye turns 30 degrees outward and 20 inward; both eyes must
            // stay within their own limits, so up and down use the smaller one.
            AssertRange(lookAt["rangeMapHorizontalOuter"], 30, 30);
            AssertRange(lookAt["rangeMapHorizontalInner"], 20, 20);
            AssertRange(lookAt["rangeMapVerticalUp"], 12, 12);
            AssertRange(lookAt["rangeMapVerticalDown"], 15, 15);
            Assert.That(warnings, Has.None.Contains(ExporterLocalization.T(VrmLookAtExport.UnmeasuredWarning)));
            var imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
            try
            {
                Assert.That(imported.Vrm.LookAt.HorizontalOuter.CurveXRangeDegree, Is.EqualTo(30).Within(Tolerance));
                Assert.That(imported.Vrm.LookAt.HorizontalOuter.CurveYRangeDegree, Is.EqualTo(30).Within(Tolerance));
            }
            finally { Object.DestroyImmediate(imported.gameObject); }
            // Measuring never edits the source eyes.
            Assert.That(left.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(right.localRotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void UnmeasurableEyeLookKeepsTheDefaultRangeAndWarns()
        {
            using var fixture = LilToonFixture(eyes: true);
            var animator = fixture.Source.GetComponent<Animator>();
            // Enabled Eye Look whose poses were never recorded turns no eye.
            EnableEyeLook(fixture.Source, animator.GetBoneTransform(HumanBodyBones.LeftEye),
                animator.GetBoneTransform(HumanBodyBones.RightEye), new Dictionary<string, (Quaternion Left, Quaternion Right)>
                {
                    ["eyesLookingStraight"] = (Quaternion.identity, Quaternion.identity),
                    ["eyesLookingUp"] = (Quaternion.identity, Quaternion.identity),
                    ["eyesLookingDown"] = (Quaternion.identity, Quaternion.identity),
                    ["eyesLookingLeft"] = (Quaternion.identity, Quaternion.identity),
                    ["eyesLookingRight"] = (Quaternion.identity, Quaternion.identity),
                });
            var warnings = new List<string>();
            var lookAt = LookAt(Export(fixture, warnings));
            Assert.That(lookAt["type"], Is.EqualTo("bone"));
            foreach (var key in RangeKeys) AssertRange(lookAt[key], 90, 10);
            Assert.That(warnings, Has.Exactly(1).EqualTo(ExporterLocalization.T(VrmLookAtExport.UnmeasuredWarning)));
        }

        [Test]
        public void WithoutEyeBonesTheExportStillHasNoLookAt()
        {
            using var fixture = LilToonFixture(eyes: false);
            var warnings = new List<string>();
            var vrm = Vrm(Export(fixture, warnings));
            Assert.That(vrm.ContainsKey("lookAt"), Is.False);
            Assert.That(warnings, Has.None.Contains(ExporterLocalization.T(VrmLookAtExport.UnmeasuredWarning)));
        }

        [Test]
        public void ExistingVrmLookAtWinsOverEyesAndEyeLook()
        {
            using var fixture = LilToonFixture(eyes: true);
            var animator = fixture.Source.GetComponent<Animator>();
            EnableEyeLook(fixture.Source, animator.GetBoneTransform(HumanBodyBones.LeftEye),
                animator.GetBoneTransform(HumanBodyBones.RightEye), new Dictionary<string, (Quaternion Left, Quaternion Right)>
                {
                    ["eyesLookingStraight"] = (Quaternion.identity, Quaternion.identity),
                    ["eyesLookingUp"] = (Quaternion.Euler(-12, 0, 0), Quaternion.Euler(-12, 0, 0)),
                    ["eyesLookingDown"] = (Quaternion.Euler(12, 0, 0), Quaternion.Euler(12, 0, 0)),
                    ["eyesLookingLeft"] = (Quaternion.Euler(0, -20, 0), Quaternion.Euler(0, -20, 0)),
                    ["eyesLookingRight"] = (Quaternion.Euler(0, 20, 0), Quaternion.Euler(0, 20, 0)),
                });
            var authored = ScriptableObject.CreateInstance<VRM10Object>();
            try
            {
                authored.LookAt.LookAtType = LookAtType.bone;
                authored.LookAt.OffsetFromHead = new Vector3(.01f, .08f, .02f);
                authored.LookAt.HorizontalInner = new CurveMapper(40, 7);
                authored.LookAt.HorizontalOuter = new CurveMapper(45, 8);
                authored.LookAt.VerticalDown = new CurveMapper(35, 6);
                authored.LookAt.VerticalUp = new CurveMapper(30, 5);
                fixture.Source.AddComponent<Vrm10Instance>().Vrm = authored;
                var lookAt = LookAt(Export(fixture, new List<string>()));
                Assert.That(lookAt["type"], Is.EqualTo("bone"));
                AssertVector(lookAt["offsetFromHeadBone"], new Vector3(.01f, .08f, .02f));
                AssertRange(lookAt["rangeMapHorizontalInner"], 40, 7);
                AssertRange(lookAt["rangeMapHorizontalOuter"], 45, 8);
                AssertRange(lookAt["rangeMapVerticalDown"], 35, 6);
                AssertRange(lookAt["rangeMapVerticalUp"], 30, 5);
            }
            finally { Object.DestroyImmediate(authored); }
        }

        [Test]
        public async Task RotatedScaledHeadSwappedSidesAndLinkedPosesMatchUniVrm()
        {
            using var fixture = LilToonFixture(eyes: true);
            var animator = fixture.Source.GetComponent<Animator>();
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            head.localRotation = Quaternion.Euler(10, 30, 5);
            head.localScale = Vector3.one * 1.3f;
            var left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            // Express each gaze in the root's frame as the eye's local rotation.
            var parent = head.rotation;
            Quaternion Local(float pitchDown, float yawPlusX) => Quaternion.Inverse(parent) * Quaternion.Euler(pitchDown, yawPlusX, 0) * parent;
            // The "Left" poses turn toward +X here: sides follow the actual turn.
            // Linked up/down poses keep a stale right value that must be ignored.
            EnableEyeLook(fixture.Source, left, right, new Dictionary<string, (Quaternion Left, Quaternion Right)>
            {
                ["eyesLookingStraight"] = (Quaternion.identity, Quaternion.identity),
                ["eyesLookingUp"] = (Local(-14, 0), Quaternion.identity),
                ["eyesLookingDown"] = (Local(11, 0), Quaternion.identity),
                ["eyesLookingLeft"] = (Local(0, 20), Local(0, 30)),
                ["eyesLookingRight"] = (Local(0, -30), Local(0, -20)),
            }, linked: new[] { "eyesLookingUp", "eyesLookingDown" });
            var bytes = Export(fixture, new List<string>());
            var lookAt = LookAt(bytes);
            AssertRange(lookAt["rangeMapHorizontalOuter"], 30, 30);
            AssertRange(lookAt["rangeMapHorizontalInner"], 20, 20);
            AssertRange(lookAt["rangeMapVerticalUp"], 14, 14);
            AssertRange(lookAt["rangeMapVerticalDown"], 11, 11);
            var imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
            try
            {
                // UniVRM places the origin at Head.TransformPoint(offset).
                Assert.That(imported.TryGetBoneTransform(HumanBodyBones.Head, out var importedHead), Is.True);
                Assert.That(imported.TryGetBoneTransform(HumanBodyBones.LeftEye, out var importedLeft), Is.True);
                Assert.That(imported.TryGetBoneTransform(HumanBodyBones.RightEye, out var importedRight), Is.True);
                var origin = importedHead.TransformPoint(imported.Vrm.LookAt.OffsetFromHead);
                var midpoint = (importedLeft.position + importedRight.position) * .5f;
                Assert.That(Vector3.Distance(origin, midpoint), Is.LessThan(1e-4f));
            }
            finally { Object.DestroyImmediate(imported.gameObject); }
        }

        [Test]
        public void UntouchedVrmLookAtDefaultsAreReplacedByTheEyeMidpoint()
        {
            using var fixture = LilToonFixture(eyes: true);
            // A VRM10Object kept only for its expressions has default look-at values.
            var authored = ScriptableObject.CreateInstance<VRM10Object>();
            try
            {
                Assert.That(VrmLookAtExport.IsUniVrmDefault(authored.LookAt), Is.True);
                fixture.Source.AddComponent<Vrm10Instance>().Vrm = authored;
                var lookAt = LookAt(Export(fixture, new List<string>()));
                Assert.That(lookAt["type"], Is.EqualTo("bone"));
                AssertVector(lookAt["offsetFromHeadBone"], new Vector3(0, .05f, .05f));
                foreach (var key in RangeKeys) AssertRange(lookAt[key], 90, 10);
                Assert.That(authored.LookAt.OffsetFromHead, Is.EqualTo(new Vector3(0, .06f, 0)), "The shared settings are not edited.");
            }
            finally { Object.DestroyImmediate(authored); }
        }

        static readonly string[] RangeKeys = { "rangeMapHorizontalInner", "rangeMapHorizontalOuter", "rangeMapVerticalDown", "rangeMapVerticalUp" };

        static AttachmentConnectionTests.Fixture LilToonFixture(bool eyes)
        {
            var fixture = new AttachmentConnectionTests.Fixture(eyes);
            var shader = Shader.Find("lilToon");
            Assert.That(shader, Is.Not.Null, "The real lilToon package is required.");
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = shader;
            return fixture;
        }

        static byte[] Export(AttachmentConnectionTests.Fixture fixture, ICollection<string> warnings) =>
            UniVrmOneClickExporter.Export(fixture.Source, "Look-at regression", "Tests", warnings,
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });

        static Dictionary<string, object> Vrm(byte[] bytes) =>
            (Dictionary<string, object>)((Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"])["VRMC_vrm"];

        static Dictionary<string, object> LookAt(byte[] bytes)
        {
            var vrm = Vrm(bytes);
            Assert.That(vrm.ContainsKey("lookAt"), Is.True);
            return (Dictionary<string, object>)vrm["lookAt"];
        }

        // Expected values are in Unity coordinates; the file stores glTF X mirrored.
        static void AssertVector(object value, Vector3 unity)
        {
            var list = ((List<object>)value).Select(Convert.ToSingle).ToArray();
            Assert.That(list.Length, Is.EqualTo(3));
            Assert.That(list[0], Is.EqualTo(-unity.x).Within(1e-4f));
            Assert.That(list[1], Is.EqualTo(unity.y).Within(1e-4f));
            Assert.That(list[2], Is.EqualTo(unity.z).Within(1e-4f));
        }

        static void AssertRange(object value, float input, float output)
        {
            var map = (Dictionary<string, object>)value;
            Assert.That(Convert.ToSingle(map["inputMaxValue"]), Is.EqualTo(input).Within(Tolerance));
            Assert.That(Convert.ToSingle(map["outputScale"]), Is.EqualTo(output).Within(Tolerance));
        }

        static void EnableEyeLook(GameObject avatar, Transform left, Transform right, Dictionary<string, (Quaternion Left, Quaternion Right)> poses,
            string[] linked = null)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(t => t != null);
            if (type == null) Assert.Ignore("VRChat SDK is required for the Eye Look integration test.");
            var descriptor = avatar.AddComponent(type);
            type.GetField("enableEyeLook").SetValue(descriptor, true);
            var field = type.GetField("customEyeLookSettings"); var settings = field.GetValue(descriptor);
            var settingsType = settings.GetType();
            settingsType.GetField("leftEye").SetValue(settings, left);
            settingsType.GetField("rightEye").SetValue(settings, right);
            foreach (var pose in poses)
            {
                var poseField = settingsType.GetField(pose.Key);
                var rotations = poseField.GetValue(settings) ?? Activator.CreateInstance(poseField.FieldType);
                var rotationsType = rotations.GetType();
                rotationsType.GetField("linked").SetValue(rotations, linked != null && linked.Contains(pose.Key));
                rotationsType.GetField("left").SetValue(rotations, pose.Value.Left);
                rotationsType.GetField("right").SetValue(rotations, pose.Value.Right);
                poseField.SetValue(settings, rotations);
            }
            field.SetValue(descriptor, settings);
        }
    }
}
