using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRVlog.AvatarFormats;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class HumanoidLimitCaptureTests
    {
        // Rebuilds the fixture's humanoid with its own limits for the bones. The
        // caller destroys the returned avatar.
        static Avatar WithLimits(GameObject source, params (HumanBodyBones Bone, HumanLimit Limit)[] limits) =>
            AvatarRestPoseTests.Rebuild(source, description =>
            {
                description.human = description.human.Select(human =>
                {
                    foreach (var (bone, limit) in limits)
                        if (human.humanName == HumanTrait.BoneName[(int)bone]) human.limit = limit;
                    return human;
                }).ToArray();
                return description;
            });

        static HumanLimit Limit(float down, float up) => new HumanLimit
        {
            useDefaultValues = false, min = new Vector3(-60, down, -90), max = new Vector3(60, up, 90), center = new Vector3(0, 5, 0), axisLength = .3f
        };

        [Test]
        public void ArmLimitsTheHumanoidSetAreWrittenByTheirVrmNames()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var avatar = WithLimits(f.Source, (HumanBodyBones.LeftUpperArm, Limit(-45, 80)), (HumanBodyBones.RightUpperArm, Limit(-50, 85)),
                (HumanBodyBones.LeftUpperLeg, Limit(-30, 30)));
            try
            {
                var warnings = new List<string>();
                var data = HumanoidLimitCapture.Capture(f.Source.GetComponent<Animator>(), null, warnings);

                Assert.That(warnings, Is.Empty);
                Assert.That(data.Bones.Keys, Is.EquivalentTo(new[] { "leftUpperArm", "rightUpperArm" }), "legs and default limits are left out");
                var left = data.Bones["leftUpperArm"];
                Assert.That(left.Min, Is.EqualTo(new[] { -60.0, -45, -90 }));
                Assert.That(left.Max, Is.EqualTo(new[] { 60.0, 80, 90 }));
                Assert.That(left.Center, Is.EqualTo(new[] { 0.0, 5, 0 }));
                Assert.That(left.AxisLength, Is.EqualTo(.3).Within(1e-6));
                Assert.That(data.Bones["rightUpperArm"].Min[1], Is.EqualTo(-50));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void DefaultLimitsWriteNothing()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var warnings = new List<string>();

            Assert.That(HumanoidLimitCapture.Capture(f.Source.GetComponent<Animator>(), null, warnings), Is.Null);
            Assert.That(HumanoidLimitCapture.Capture(null, null, warnings), Is.Null);
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void ALimitOutsideTheContractWarnsAndWritesNothing()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var limit = Limit(-45, 80); limit.axisLength = (float)HumanoidLimitData.MaximumAxisLength * 2;
            var avatar = WithLimits(f.Source, (HumanBodyBones.LeftUpperArm, limit));
            try
            {
                var warnings = new List<string>();

                Assert.That(HumanoidLimitCapture.Capture(f.Source.GetComponent<Animator>(), null, warnings), Is.Null);
                Assert.That(warnings.Single(), Does.StartWith(HumanoidLimitCapture.FailureWarning));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        // The arms' T-pose rotations are written in glTF axes (X mirrored), with
        // or without limits next to them.
        [Test]
        public void TheArmsTPoseIsWrittenInGltfAxes()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var turn = Quaternion.Euler(10, 20, 30);
            var tPose = new Dictionary<HumanBodyBones, Quaternion> { [HumanBodyBones.LeftLowerArm] = turn };
            var warnings = new List<string>();

            var data = HumanoidLimitCapture.Capture(f.Source.GetComponent<Animator>(), tPose, warnings);

            Assert.That(warnings, Is.Empty);
            Assert.That(data.Bones, Is.Empty, "the fixture has the default limits");
            Assert.That(data.TPose.Keys, Is.EqualTo(new[] { "leftLowerArm" }));
            var written = data.TPose["leftLowerArm"];
            var mirrored = new Quaternion((float)written[0], (float)written[1], (float)written[2], (float)written[3]);
            turn.ToAngleAxis(out var angle, out var axis);
            Assert.That(Quaternion.Angle(mirrored, Quaternion.AngleAxis(-angle, new Vector3(-axis.x, axis.y, axis.z))), Is.LessThan(.01f), "as UniVRM's ReverseX");
            Assert.That(HumanoidLimitCapture.Capture(f.Source.GetComponent<Animator>(), new Dictionary<HumanBodyBones, Quaternion>(), warnings), Is.Null);
        }

        [Test]
        public void InjectedLimitsAreReadBackFromAssetExtrasNextToTheOtherKeys()
        {
            var bytes = GlbDocument.Create(new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["extras"] = new Dictionary<string, object> { ["other"] = 1L } }
            }, Array.Empty<byte>()).Write();
            var data = new HumanoidLimitData();
            data.Bones["leftShoulder"] = new HumanoidLimitData.Limit { Min = new[] { -15.0, -15, 0 }, Max = new[] { 15.0, 30, 0 }, AxisLength = .1 };
            data.TPose["leftHand"] = new[] { 0, 0, .6, .8 };
            Assert.That(HumanoidLimitCapture.Inject(bytes, null), Is.SameAs(bytes));

            var json = GlbDocument.Read(AvatarFormatRequirements.Write(HumanoidLimitCapture.Inject(bytes, data))).Json;

            var read = HumanoidLimitData.FromGltf(json);
            Assert.That(read.Bones.Keys, Is.EqualTo(new[] { "leftShoulder" }));
            Assert.That(read.Bones["leftShoulder"].Max, Is.EqualTo(new[] { 15.0, 30, 0 }));
            Assert.That(read.TPose["leftHand"], Is.EqualTo(new[] { 0, 0, .6, .8 }));
            var extras = (Dictionary<string, object>)((Dictionary<string, object>)json["asset"])["extras"];
            Assert.That(extras["other"], Is.EqualTo(1L));
            Assert.That(extras.ContainsKey(AvatarFormatVersions.RequiredAppVersionsKey), Is.False, "a player that ignores the key still plays the file");
            Assert.That(json.ContainsKey("extensionsUsed"), Is.False);
        }

        [Test]
        public void AFileWithoutTheKeyHasNoLimits()
        {
            Assert.That(HumanoidLimitData.FromGltf(new Dictionary<string, object> { ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" } }), Is.Null);
            Assert.That(HumanoidLimitData.FromGltf(new Dictionary<string, object>()), Is.Null);
        }
    }
}
