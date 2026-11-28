using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VRVlog.AvatarFormats;
using VRVlog.Expressions;
using VRVlog.ObjectToggles;

namespace VRVlog.LilToonExporter.Tests
{
    // What the Formats contract readers accept and reject at their limits,
    // with the exact exception text. The app reads the same files, so a change
    // here changes which avatars the app loads.
    public sealed class FormatsContractBoundaryTests
    {
        const string LimitInvalid = "Humanoid limit data is invalid or exceeds the supported limits.";
        const string LipSyncInvalid = "Lip sync data is invalid or exceeds the supported limits.";
        const string GestureInvalid = "Expression gesture data is invalid or exceeds the supported limits.";
        const string ToggleInvalid = "Object toggle data is invalid or exceeds the supported limits.";
        const string AnimationInvalid = "Expression animation data is invalid or exceeds the supported limits.";
        const string AnimationObject = "Invalid expression animation object.";
        const string AnimationArray = "Invalid expression animation array.";
        const string AnimationName = "Invalid expression animation name.";

        static readonly string Long = new string('a', 1024);
        static readonly string TooLong = new string('a', 1025);

        static void Rejects(string message, TestDelegate read) =>
            Assert.That(Assert.Throws<InvalidOperationException>(read).Message, Is.EqualTo(message));

        static Dictionary<string, object> Lip(Action<Dictionary<string, object>> change = null)
        {
            var root = new Dictionary<string, object> { ["schemaVersion"] = 1L, ["dominantViseme"] = true, ["transitionSeconds"] = .5 };
            change?.Invoke(root);
            return root;
        }

        [Test]
        public void LipSyncBoundaries()
        {
            Assert.That(LipSyncData.Read(Lip()).TransitionSeconds, Is.EqualTo(.5f));
            Assert.That(LipSyncData.Read(Lip(r => r["transitionSeconds"] = .25)).TransitionSeconds, Is.EqualTo(.25f), "Seconds may be fractional.");
            Assert.That(LipSyncData.Read(Lip(r => r["transitionSeconds"] = 10L)).TransitionSeconds, Is.EqualTo(10f));
            Assert.That(LipSyncData.Read(Lip(r => r["transitionSeconds"] = .5f)).TransitionSeconds, Is.EqualTo(.5f));
            Assert.That(LipSyncData.Read(Lip(r => r["transitionSeconds"] = 0)).TransitionSeconds, Is.EqualTo(0f));
            Assert.DoesNotThrow(() => LipSyncData.Read(Lip(r => r["schemaVersion"] = 1.0)));
            Assert.DoesNotThrow(() => LipSyncData.Read(Lip(r => r["schemaVersion"] = 1)));

            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["transitionSeconds"] = 10.000001)));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["transitionSeconds"] = -.001)));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["transitionSeconds"] = double.NaN)));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["transitionSeconds"] = double.PositiveInfinity)));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["transitionSeconds"] = ".5")));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["transitionSeconds"] = null)));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["schemaVersion"] = 1.5)));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["schemaVersion"] = 1000001L)));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["schemaVersion"] = "1")));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["dominantViseme"] = 1L)));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r["extra"] = 1L)));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(Lip(r => r.Remove("dominantViseme"))));
            Rejects(LipSyncInvalid, () => LipSyncData.Read(new List<object>()));
        }

        const string EyeShapeInvalid = "Eye shape data is invalid or exceeds the supported limits.";

        static Dictionary<string, object> EyeShapes(Action<Dictionary<string, object>, Dictionary<string, object>> change = null)
        {
            var shape = new Dictionary<string, object> { ["node"] = 3L, ["name"] = "まばたき" };
            var root = new Dictionary<string, object> { ["schemaVersion"] = 1L, ["shapes"] = new List<object> { shape } };
            change?.Invoke(root, shape);
            return root;
        }

        static List<object> ManyEyeShapes(int count) => Enumerable.Range(0, count)
            .Select(i => (object)new Dictionary<string, object> { ["node"] = 0L, ["name"] = "eye_" + i }).ToList();

        [Test]
        public void EyeShapeBoundaries()
        {
            var read = EyeShapeData.Read(EyeShapes());
            Assert.That((read.Shapes.Single().Node, read.Shapes.Single().Name), Is.EqualTo((3, "まばたき")));
            Assert.DoesNotThrow(() => EyeShapeData.Read(EyeShapes((r, s) => s["name"] = Long)));
            Assert.DoesNotThrow(() => EyeShapeData.Read(EyeShapes((r, s) => ((List<object>)r["shapes"]).Add(
                new Dictionary<string, object> { ["node"] = 4L, ["name"] = "まばたき" }))), "The same name may be on another node.");
            Assert.DoesNotThrow(() => EyeShapeData.Read(EyeShapes((r, s) => r["shapes"] = ManyEyeShapes(EyeShapeData.MaximumShapes))));
            Assert.That(EyeShapeData.Read(EyeShapeData.Write(read)).Shapes.Single().Name, Is.EqualTo("まばたき"));

            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => ((List<object>)r["shapes"]).Add(
                new Dictionary<string, object> { ["node"] = 3L, ["name"] = "まばたき" }))));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => r["shapes"] = new List<object>())));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => r["shapes"] = ManyEyeShapes(EyeShapeData.MaximumShapes + 1))));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => s["node"] = -1L)));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => s["node"] = 1.5)));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => s["name"] = "")));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => s["name"] = TooLong)));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => s["extra"] = 1L)));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => r["schemaVersion"] = 2L)));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(EyeShapes((r, s) => r.Remove("shapes"))));
            Rejects(EyeShapeInvalid, () => EyeShapeData.Read(new List<object>()));
        }

        static Dictionary<string, object> Limits(Action<Dictionary<string, object>, Dictionary<string, object>> change = null)
        {
            var bone = new Dictionary<string, object>
            {
                ["min"] = new List<object> { -60.0, -45L, -90.0 }, ["max"] = new List<object> { 60.0, 80.0, 90.0 },
                ["center"] = new List<object> { 0.0, 0.0, 0.0 }, ["axisLength"] = .3
            };
            var root = new Dictionary<string, object> { ["schemaVersion"] = 1L, ["bones"] = new Dictionary<string, object> { ["leftUpperArm"] = bone } };
            change?.Invoke(root, bone);
            return root;
        }

        [Test]
        public void HumanoidLimitBoundaries()
        {
            Assert.That(HumanoidLimitData.Read(Limits()).Bones["leftUpperArm"].Min[1], Is.EqualTo(-45));
            Assert.DoesNotThrow(() => HumanoidLimitData.Read(Limits((r, b) => { b["min"] = new List<object> { -180.0, 0L, 0L }; b["max"] = new List<object> { 180.0, 0L, 0L }; })));
            Assert.DoesNotThrow(() => HumanoidLimitData.Read(Limits((r, b) => b["axisLength"] = HumanoidLimitData.MaximumAxisLength)));
            Assert.DoesNotThrow(() => HumanoidLimitData.Read(Limits((r, b) => b["center"] = new List<object> { -180.0, 180.0, 0L })));
            Assert.That(HumanoidLimitData.BoneNames.Count, Is.EqualTo(55));

            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => b["min"] = new List<object> { 1.0, 0L, 0L })));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => b["max"] = new List<object> { -1.0, 0L, 0L })));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => b["min"] = new List<object> { -180.001, 0L, 0L })));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => b["center"] = new List<object> { 0L, 0L })));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => b["center"] = new List<object> { 0L, 0L, double.NaN })));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => b["axisLength"] = -.001)));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => b["axisLength"] = HumanoidLimitData.MaximumAxisLength + .001)));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => b.Remove("center"))));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => b["extra"] = 1L)));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => ((Dictionary<string, object>)r["bones"])["LeftUpperArm"] = b)));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r["bones"] = new Dictionary<string, object>())));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r["schemaVersion"] = 2L)));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r["extra"] = 1L)));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(new List<object>()));
            Rejects(LimitInvalid, () => HumanoidLimitData.Write(new HumanoidLimitData()));
        }

        static List<object> Rotation(params double[] values) => values.Select(value => (object)value).ToList();

        // tPose is optional next to bones, and either one alone is a value.
        [Test]
        public void HumanoidTPoseBoundaries()
        {
            var half = Math.Sqrt(.5);
            var both = HumanoidLimitData.Read(Limits((r, b) => r["tPose"] = new Dictionary<string, object> { ["leftLowerArm"] = Rotation(0, 0, half, half) }));
            Assert.That(both.Bones.Keys, Is.EqualTo(new[] { "leftUpperArm" }));
            Assert.That(both.TPose["leftLowerArm"], Is.EqualTo(new[] { 0, 0, half, half }));
            var alone = HumanoidLimitData.Read(Limits((r, b) => { r.Remove("bones"); r["tPose"] = new Dictionary<string, object> { ["rightHand"] = Rotation(0, 0, 0, 1) }; }));
            Assert.That(alone.Bones, Is.Empty);
            Assert.That(alone.TPose.Keys, Is.EqualTo(new[] { "rightHand" }));
            Assert.DoesNotThrow(() => HumanoidLimitData.Read(Limits((r, b) => r["tPose"] = new Dictionary<string, object> { ["leftHand"] = Rotation(0, 0, 0, 1 + HumanoidLimitData.RotationLengthTolerance * .9) })));
            var written = new HumanoidLimitData();
            written.TPose["leftUpperArm"] = new[] { 0, .0000001, half, half };
            var value = HumanoidLimitData.Write(written);
            Assert.That(value.Keys, Is.EquivalentTo(new[] { "schemaVersion", "tPose" }), "no empty bones");
            Assert.That(((Dictionary<string, object>)value["tPose"])["leftUpperArm"], Is.EqualTo(Rotation(0, 0, Math.Round(half, 6), Math.Round(half, 6))));
            Assert.That(HumanoidLimitData.Write(HumanoidLimitData.Read(Limits())).ContainsKey("tPose"), Is.False, "a file with limits alone is as before");

            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r["tPose"] = new Dictionary<string, object>())));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r["tPose"] = new Dictionary<string, object> { ["leftHand"] = Rotation(0, 0, 0, 1.01) })));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r["tPose"] = new Dictionary<string, object> { ["leftHand"] = Rotation(0, 0, 1) })));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r["tPose"] = new Dictionary<string, object> { ["leftHand"] = Rotation(0, 0, 0, double.NaN) })));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r["tPose"] = new Dictionary<string, object> { ["LeftHand"] = Rotation(0, 0, 0, 1) })));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r["tPose"] = new List<object>())));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r.Remove("bones"))));
            Rejects(LimitInvalid, () => HumanoidLimitData.Read(Limits((r, b) => r.Remove("schemaVersion"))));
        }

        static Dictionary<string, object> Gestures(Action<Dictionary<string, object>, List<object>> change = null)
        {
            var cells = Enumerable.Repeat<object>(null, ExpressionGestureData.CellCount).ToList();
            cells[0] = new List<object> { new Dictionary<string, object> { ["expression"] = "Smile" } };
            var root = new Dictionary<string, object> { ["schemaVersion"] = 1L, ["cells"] = cells };
            change?.Invoke(root, (List<object>)cells[0]);
            return root;
        }

        static Dictionary<string, object> Reference(string expression) => new Dictionary<string, object> { ["expression"] = expression };

        [Test]
        public void GestureBoundaries()
        {
            Assert.That(ExpressionGestureData.Read(Gestures()).Cells[0].Single().Expression, Is.EqualTo("Smile"));
            Assert.DoesNotThrow(() => ExpressionGestureData.Read(Gestures((r, c) => c[0] = Reference(Long))));
            Assert.DoesNotThrow(() => ExpressionGestureData.Read(Gestures((r, c) => c[0] = Reference("a\u0001b"))), "Control characters are not checked.");
            Assert.DoesNotThrow(() => ExpressionGestureData.Read(Gestures((r, c) => ((Dictionary<string, object>)c[0])["drive"] = "left")));
            Assert.DoesNotThrow(() => ExpressionGestureData.Read(Gestures((r, c) => c.AddRange(Enumerable.Range(1, 7).Select(i => (object)Reference("E" + i))))));
            Assert.DoesNotThrow(() => ExpressionGestureData.Read(Gestures((r, c) => ((List<object>)r["cells"])[1] = new List<object>())));
            Assert.DoesNotThrow(() => ExpressionGestureData.Read(Gestures((r, c) => r["schemaVersion"] = 1.0)));

            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => c[0] = Reference(TooLong))));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => c[0] = Reference(""))));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => ((Dictionary<string, object>)c[0])["expression"] = 1L)));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => c.AddRange(Enumerable.Range(1, 8).Select(i => (object)Reference("E" + i))))));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => ((List<object>)r["cells"]).RemoveAt(63))));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => ((List<object>)r["cells"]).Add(null))));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => ((Dictionary<string, object>)c[0])["extra"] = 1L)));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => c[0] = new Dictionary<string, object>())));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => ((Dictionary<string, object>)c[0])["drive"] = "up")));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => r["schemaVersion"] = 1.5)));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => r["extra"] = 1L)));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => r.Remove("cells"))));
            Rejects(GestureInvalid, () => ExpressionGestureData.Read(Gestures((r, c) => r["cells"] = new Dictionary<string, object>())));
        }

        static Dictionary<string, object> Toggles(Action<Dictionary<string, object>, Dictionary<string, object>, Dictionary<string, object>> change = null)
        {
            var group = new Dictionary<string, object>
            {
                ["parameter"] = "P", ["name"] = "N", ["defaultValue"] = 0L, ["targets"] = new List<object> { 0L },
                ["states"] = new List<object>
                {
                    new Dictionary<string, object> { ["value"] = 0L, ["active"] = new List<object> { false } },
                    new Dictionary<string, object> { ["value"] = 1L, ["active"] = new List<object> { true } }
                }
            };
            var item = new Dictionary<string, object> { ["id"] = "i", ["name"] = "n", ["path"] = new List<object>(), ["parameter"] = "P", ["value"] = 1L };
            var root = new Dictionary<string, object> { ["schemaVersion"] = 1L, ["groups"] = new List<object> { group }, ["items"] = new List<object> { item } };
            change?.Invoke(root, group, item);
            return root;
        }

        static Dictionary<string, object> State(object value) => new Dictionary<string, object> { ["value"] = value, ["active"] = new List<object> { true } };

        [Test]
        public void ObjectToggleBoundaries()
        {
            Assert.That(ObjectToggleData.Read(Toggles()).Groups.Single().Targets, Is.EqualTo(new[] { 0 }));
            Assert.That(ObjectToggleData.Read(Toggles((r, g, i) => g["targets"] = new List<object> { 0.0 })).Groups[0].Targets, Is.EqualTo(new[] { 0 }));
            Assert.That(ObjectToggleData.Read(Toggles((r, g, i) => g["targets"] = new List<object> { (long)int.MaxValue })).Groups[0].Targets, Is.EqualTo(new[] { int.MaxValue }));
            Assert.DoesNotThrow(() => ObjectToggleData.Read(Toggles((r, g, i) => ((List<object>)g["states"]).Add(State(1000000L)))));
            Assert.DoesNotThrow(() => ObjectToggleData.Read(Toggles((r, g, i) => ((List<object>)g["states"]).Add(State(-1000000L)))));
            Assert.DoesNotThrow(() => ObjectToggleData.Read(Toggles((r, g, i) => g["defaultValue"] = 1.0)));
            Assert.DoesNotThrow(() => ObjectToggleData.Read(Toggles((r, g, i) => g["name"] = Long)));
            Assert.DoesNotThrow(() => ObjectToggleData.Read(Toggles((r, g, i) => g["name"] = "a\u0001b")), "Control characters are not checked.");
            Assert.DoesNotThrow(() => ObjectToggleData.Read(Toggles((r, g, i) => i["path"] = Enumerable.Repeat<object>("p", 16).ToList())));

            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g["targets"] = new List<object> { -1L })));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g["targets"] = new List<object> { (long)int.MaxValue + 1 })));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g["targets"] = new List<object> { .5 })));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g["targets"] = new List<object>())));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => ((List<object>)g["states"]).Add(State(1000001L)))));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => ((List<object>)g["states"]).Add(State(-1000001L)))));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => ((List<object>)g["states"]).Add(State(2.5)))));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g["defaultValue"] = .5)));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g["defaultValue"] = double.NaN)));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g["name"] = TooLong)));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g["name"] = "")));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => i["path"] = Enumerable.Repeat<object>("p", 17).ToList())));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g["extra"] = 1L)));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => g.Remove("name"))));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => i["extra"] = 1L)));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => r["extra"] = 1L)));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => r["items"] = new List<object>())));
            Rejects(ToggleInvalid, () => ObjectToggleData.Read(Toggles((r, g, i) => r["groups"] = "groups")));
        }

        const string TransformInvalid = "Object toggle transform data is invalid or exceeds the supported limits.";

        // A transform group for the group "P" of Toggles(), whose states are 0 and 1.
        static Dictionary<string, object> Moves(Action<Dictionary<string, object>, Dictionary<string, object>, Dictionary<string, object>> change = null)
        {
            Dictionary<string, object> Transform(double scale) => new Dictionary<string, object>
            {
                ["translation"] = new List<object> { 0.0, .1, 0.0 }, ["scale"] = new List<object> { scale, scale, scale }
            };
            var target = new Dictionary<string, object> { ["node"] = 3L, ["properties"] = new List<object> { "translation", "scale" } };
            var group = new Dictionary<string, object>
            {
                ["parameter"] = "P", ["targets"] = new List<object> { target },
                ["states"] = new List<object>
                {
                    new Dictionary<string, object> { ["value"] = 0L, ["transforms"] = new List<object> { Transform(1) } },
                    new Dictionary<string, object> { ["value"] = 1L, ["transforms"] = new List<object> { Transform(.01) } }
                }
            };
            var root = new Dictionary<string, object> { ["schemaVersion"] = 1L, ["groups"] = new List<object> { group } };
            change?.Invoke(root, group, target);
            return root;
        }

        static Dictionary<string, object> FirstTransform(Dictionary<string, object> group) =>
            (Dictionary<string, object>)((List<object>)((Dictionary<string, object>)((List<object>)group["states"])[0])["transforms"])[0];

        [Test]
        public void ObjectToggleTransformBoundaries()
        {
            var toggles = ObjectToggleData.Read(Toggles());
            var read = ObjectToggleTransformData.Read(Moves(), toggles);
            Assert.That(read.Groups.Single().Targets.Single().Node, Is.EqualTo(3));
            Assert.That(read.Groups[0].Targets[0].Properties, Is.EqualTo(new[] { "translation", "scale" }));
            Assert.That(read.Groups[0].StateFor(1).Transforms[0].Scale, Is.EqualTo(new[] { .01, .01, .01 }));
            Assert.That(read.Groups[0].StateFor(1).Transforms[0].Rotation, Is.Null);
            Assert.DoesNotThrow(() => ObjectToggleTransformData.Read(Moves((r, g, t) =>
            {
                t["properties"] = new List<object> { "rotation" };
                foreach (Dictionary<string, object> state in (List<object>)g["states"])
                    state["transforms"] = new List<object> { new Dictionary<string, object> { ["rotation"] = new List<object> { 0.0, 0.0, 0.0, 1.0005 } } };
            }), toggles), "A rotation of length 1 within 0.001 is read.");

            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves(), null));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => g["parameter"] = "Q"), toggles));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => r["schemaVersion"] = 2L), toggles));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => r["extra"] = 1L), toggles));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => t["extra"] = 1L), toggles));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => t["node"] = -1L), toggles));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => t["properties"] = new List<object>()), toggles));
            // Properties keep their order.
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => t["properties"] = new List<object> { "scale", "translation" }), toggles));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => t["properties"] = new List<object> { "translation", "translation" }), toggles));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => t["properties"] = new List<object> { "translation", "skew" }), toggles));
            // Too few states.
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => ((List<object>)g["states"]).RemoveAt(1)), toggles));
            // A value the toggle group does not have.
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) =>
                ((Dictionary<string, object>)((List<object>)g["states"])[1])["value"] = 2L), toggles));
            // A repeated value.
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) =>
                ((Dictionary<string, object>)((List<object>)g["states"])[1])["value"] = 0L), toggles));
            // A missing property.
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => FirstTransform(g).Remove("scale")), toggles));
            // A property the target does not list.
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => FirstTransform(g)["rotation"] = new List<object> { 0.0, 0.0, 0.0, 1.0 }), toggles));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => FirstTransform(g)["scale"] = new List<object> { 1.0, 1.0 }), toggles));
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) => FirstTransform(g)["scale"] = new List<object> { 1.0, double.NaN, 1.0 }), toggles));
            // A rotation that is not a unit quaternion.
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) =>
            {
                t["properties"] = new List<object> { "rotation" };
                foreach (Dictionary<string, object> state in (List<object>)g["states"])
                    state["transforms"] = new List<object> { new Dictionary<string, object> { ["rotation"] = new List<object> { 0.0, 0.0, 0.0, 2.0 } } };
            }), toggles));
            // Two groups for one parameter.
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) =>
                ((List<object>)r["groups"]).Add(Moves()["groups"] is List<object> more ? more[0] : null)), toggles));
            // A node twice.
            Rejects(TransformInvalid, () => ObjectToggleTransformData.Read(Moves((r, g, t) =>
                ((List<object>)g["targets"]).Add(new Dictionary<string, object> { ["node"] = 3L, ["properties"] = new List<object> { "scale" } })), toggles));
        }

        static List<object> Keys() => new List<object>
        {
            new List<object> { 0.0, 0.0, 1.0, 1.0, 1.0 / 3, 1.0 / 3 },
            new List<object> { 1.0, 1.0, 1.0, 1.0, 1.0 / 3, 1.0 / 3 }
        };

        static Dictionary<string, object> Animation(string expression, object firstTarget, object secondTarget) => new Dictionary<string, object>
        {
            ["expression"] = expression, ["duration"] = 1.0, ["loop"] = false,
            ["channels"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["preWrap"] = "clamp", ["postWrap"] = "clamp", ["keys"] = Keys(),
                    ["points"] = new List<object>
                    {
                        new Dictionary<string, object> { ["value"] = 0.0, ["target"] = firstTarget },
                        new Dictionary<string, object> { ["value"] = 1.0, ["target"] = secondTarget }
                    }
                }
            }
        };

        static Dictionary<string, object> Morph(object node, object index) => new Dictionary<string, object> { ["node"] = node, ["index"] = index };

        // Schema 2: expression list, animation, its morph reference.
        static Dictionary<string, object> Document(Action<Dictionary<string, object>, Dictionary<string, object>, Dictionary<string, object>> change = null)
        {
            var expression = new Dictionary<string, object> { ["expression"] = "Smile", ["source"] = ExpressionSource.Menu };
            var animation = Animation("Smile", Morph(0L, 0L), null);
            var root = new Dictionary<string, object>
            {
                ["schemaVersion"] = 2L, ["expressions"] = new List<object> { expression }, ["animations"] = new List<object> { animation }
            };
            change?.Invoke(root, expression, animation);
            return root;
        }

        static Dictionary<string, object> Point(Dictionary<string, object> animation, int index) =>
            (Dictionary<string, object>)((List<object>)((Dictionary<string, object>)((List<object>)animation["channels"])[0])["points"])[index];

        static void Listed(Dictionary<string, object> root, string name) =>
            ((List<object>)root["expressions"]).Add(new Dictionary<string, object> { ["expression"] = name, ["source"] = ExpressionSource.Menu });

        [Test]
        public void ExpressionAnimationDocumentBoundaries()
        {
            Assert.That(ExpressionAnimationData.ReadDocument(Document()).Animations.Single().Channels[0].Points[0].Morph.Node, Is.Zero);
            Assert.DoesNotThrow(() => ExpressionAnimationData.ReadDocument(Document((r, e, a) => e["extra"] = 1L)), "Objects are only checked for their type.");
            Assert.DoesNotThrow(() => ExpressionAnimationData.ReadDocument(Document((r, e, a) => Point(a, 0)["extra"] = 1L)));
            Assert.DoesNotThrow(() => ExpressionAnimationData.ReadDocument(Document((r, e, a) => Listed(r, Long))));
            Assert.DoesNotThrow(() => ExpressionAnimationData.ReadDocument(Document((r, e, a) => Listed(r, "a\u0001b"))), "Control characters are not checked.");
            Assert.DoesNotThrow(() => ExpressionAnimationData.ReadDocument(Document((r, e, a) => a["duration"] = .5)), "Numbers may be fractional.");
            Assert.DoesNotThrow(() => ExpressionAnimationData.ReadDocument(Document((r, e, a) => r["schemaVersion"] = 2.0)));
            Assert.That(ExpressionAnimationData.ReadDocument(Document((r, e, a) => Point(a, 0)["target"] = Morph(1.0, (long)int.MaxValue)))
                .Animations[0].Channels[0].Points[0].Morph.Index, Is.EqualTo(int.MaxValue));

            Rejects(AnimationName, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => Listed(r, TooLong))));
            Rejects(AnimationName, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => e["source"] = "")));
            Rejects(AnimationName, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => e["source"] = 1L)));
            Rejects(AnimationObject, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => ((List<object>)r["expressions"])[0] = "Smile")));
            Rejects(AnimationObject, () => ExpressionAnimationData.ReadDocument(new List<object>()));
            Rejects(AnimationArray, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => r["expressions"] = e)));
            Rejects(AnimationArray, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => a["channels"] = null)));
            Rejects(AnimationInvalid, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => Point(a, 0)["target"] = Morph(.5, 0L))));
            Rejects(AnimationInvalid, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => Point(a, 0)["target"] = Morph(-1L, 0L))));
            Rejects(AnimationInvalid, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => Point(a, 0)["target"] = Morph(0L, (long)int.MaxValue + 1))));
            Rejects(AnimationInvalid, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => a["duration"] = double.NaN)));
            Rejects(AnimationInvalid, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => a["duration"] = "1")));
            Rejects(AnimationInvalid, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => r["schemaVersion"] = "2")));
            Rejects(AnimationInvalid, () => ExpressionAnimationData.ReadDocument(Document((r, e, a) => r["schemaVersion"] = 3L)));
            // A missing key is not checked by the object reader: the lookup throws.
            Assert.Throws<KeyNotFoundException>(() => ExpressionAnimationData.ReadDocument(Document((r, e, a) => e.Remove("source"))));
            Assert.Throws<KeyNotFoundException>(() => ExpressionAnimationData.ReadDocument(Document((r, e, a) => r.Remove("schemaVersion"))));
        }

        static Dictionary<string, object> SchemaOne(object target) => new Dictionary<string, object>
        {
            ["schemaVersion"] = 1L, ["animations"] = new List<object> { Animation("Smile", null, target) }
        };

        [Test]
        public void ExpressionAnimationSchemaOneBoundaries()
        {
            var target = ExpressionAnimationData.TargetPrefix + "Smile_1";
            Assert.That(ExpressionAnimationData.Read(SchemaOne(target)).Single().Channels[0].Points[1].Target, Is.EqualTo(target));
            Assert.That(ExpressionAnimationData.ReadDocument(SchemaOne(target)).SchemaVersion, Is.EqualTo(1));
            var longest = ExpressionAnimationData.TargetPrefix + new string('a', 1024 - ExpressionAnimationData.TargetPrefix.Length);
            Assert.DoesNotThrow(() => ExpressionAnimationData.Read(SchemaOne(longest)));
            Assert.DoesNotThrow(() => ExpressionAnimationData.Read(SchemaOne(target + "\u0001")));

            Rejects(AnimationName, () => ExpressionAnimationData.Read(SchemaOne(longest + "a")));
            Rejects(AnimationName, () => ExpressionAnimationData.Read(SchemaOne("")));
            Rejects(AnimationInvalid, () => ExpressionAnimationData.Read(SchemaOne("Smile_1")));
            Rejects(AnimationObject, () => ExpressionAnimationData.Read("schema 1"));
            Rejects(AnimationArray, () => ExpressionAnimationData.Read(new Dictionary<string, object> { ["schemaVersion"] = 1L, ["animations"] = "Smile" }));
        }
    }
}
