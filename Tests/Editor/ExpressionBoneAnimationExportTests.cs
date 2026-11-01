using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.Expressions;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Bone and visibility curves of expression clips, from the clip reader to
    // the transforms of the expression animation extension.
    public sealed class ExpressionBoneAnimationExportTests
    {
        const string Joint = "Independent hair/Head";

        // The export is off until the app plays the transforms; these tests
        // check what it writes once it is on.
        ExpressionTransformsSwitch transforms;
        [SetUp] public void TurnTransformsOn() => transforms = new ExpressionTransformsSwitch(true);
        [TearDown] public void RestoreTransforms() => transforms?.Dispose();

        static Type SdkType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        static EditorCurveBinding Bone(string path, string property) => EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
        static EditorCurveBinding Shown(string path) => EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive");
        static EditorCurveBinding Shape => EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");

        static AnimationClip Clip(string name, params (EditorCurveBinding Binding, AnimationCurve Curve)[] curves)
        {
            var clip = new AnimationClip { name = name };
            foreach (var (binding, curve) in curves) AnimationUtility.SetEditorCurve(clip, binding, curve);
            return clip;
        }

        static (EditorCurveBinding, AnimationCurve) Held(EditorCurveBinding binding, float value) => (binding, AnimationCurve.Constant(0, 1, value));

        // Curves that keep the prepared pose up to float noise, spelled as an
        // authored rest pose often is.
        static (EditorCurveBinding, AnimationCurve)[] Rest(string kind)
        {
            switch (kind)
            {
                case "euler noise": return new[] { Held(Bone(Joint, "localEulerAnglesRaw.x"), .01f), Held(Bone(Joint, "localEulerAnglesRaw.y"), -.02f), Held(Bone(Joint, "localEulerAnglesRaw.z"), 0) };
                case "euler full turn": return new[] { Held(Bone(Joint, "localEulerAnglesRaw.y"), 360), Held(Bone(Joint, "localEulerAnglesRaw.z"), -360.02f) };
                case "negated quaternion": return new[] { Held(Bone(Joint, "m_LocalRotation.x"), 0), Held(Bone(Joint, "m_LocalRotation.y"), .0001f), Held(Bone(Joint, "m_LocalRotation.z"), 0), Held(Bone(Joint, "m_LocalRotation.w"), -1) };
                case "position noise": return new[] { Held(Bone(Joint, "m_LocalPosition.x"), .0003f), Held(Bone(Joint, "m_LocalPosition.y"), 1.7502f) };
                case "scale noise": return new[] { Held(Bone(Joint, "m_LocalScale.x"), 1.0004f) };
                case "held visibility": return new[] { Held(Shown("Back"), 1) };
                default: throw new ArgumentException(kind);
            }
        }

        [TestCase("euler noise")]
        [TestCase("euler full turn")]
        [TestCase("negated quaternion")]
        [TestCase("position noise")]
        [TestCase("scale noise")]
        [TestCase("held visibility")]
        public void RestValuedCurvesAreDropped(string kind)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var clip = Clip("Rest " + kind, Rest(kind).Append(Held(Shape, 80)).ToArray());
            try
            {
                var entry = new VrChatExpressionMenu.Entry();
                VrChatGestureExpressions.ReadClip(fixture.Source, clip, entry);
                Assert.That(entry.Transforms, Is.Empty);
                Assert.That(entry.Values.Single().Weight, Is.EqualTo(80));
                Assert.That(entry.Messages, Is.Empty);
            }
            finally { Object.DestroyImmediate(clip); }
        }

        // Keys on the prepared value whose Bezier handles leave it still move
        // the bone between the keys.
        [Test]
        public void ABoneThatLeavesThePoseBetweenKeysIsKept()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var swing = new AnimationCurve(new Keyframe(0, 0, 5, 5), new Keyframe(1, 0, 5, 5));
            var clip = Clip("Swing", (Bone(Joint, "localEulerAnglesRaw.y"), swing), Held(Shape, 80));
            try
            {
                var entry = new VrChatExpressionMenu.Entry();
                VrChatGestureExpressions.ReadClip(fixture.Source, clip, entry);
                var track = entry.Transforms.Single();
                Assert.That(track.Property, Is.EqualTo(ExpressionTransformAnimation.Euler));
                Assert.That(track.Curves[1].Evaluate(.5), Is.EqualTo(swing.Evaluate(.5f)).Within(1e-5));
                Assert.That(entry.TransformDuration, Is.EqualTo(1));
                Assert.That(entry.Duration, Is.EqualTo(0));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        // Position compares in metres of the avatar: under a 100x parent a
        // centimetre is 0.0001 of the bone's own units.
        [Test]
        public void SmallMovesUnderAScaledParentAreKept()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            fixture.Source.transform.Find("Independent hair").localScale = Vector3.one * 100;
            var y = fixture.Source.transform.Find(Joint).localPosition.y;
            var clip = Clip("Nudge", (Bone(Joint, "m_LocalPosition.y"), AnimationCurve.Linear(0, y, 1, y + .0001f)), Held(Shape, 80));
            try
            {
                var entry = new VrChatExpressionMenu.Entry();
                VrChatGestureExpressions.ReadClip(fixture.Source, clip, entry);
                Assert.That(entry.Transforms.Single().Property, Is.EqualTo(ExpressionTransformAnimation.Translation));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        [Test]
        public void HumanoidBonesAreDroppedWithAWarning()
        {
            using var fixture = new AttachmentConnectionTests.Fixture(eyes: true);
            const string eye = "Hips/Spine/Chest/Neck/Head/LeftEye", neck = "Hips/Spine/Chest/Neck";
            var clip = Clip("Look", Held(Bone(eye, "localEulerAnglesRaw.y"), 15), Held(Bone(neck, "m_LocalPosition.z"), .01f), Held(Shape, 80));
            try
            {
                var entry = new VrChatExpressionMenu.Entry();
                VrChatGestureExpressions.ReadClip(fixture.Source, clip, entry);
                Assert.That(entry.Transforms, Is.Empty);
                Assert.That(entry.Values.Single().Weight, Is.EqualTo(80));
                Assert.That(entry.Messages, Is.EqualTo(new[] { ExporterLocalization.T(ExpressionTransformCurves.HumanoidWarning) + neck + ", " + eye }));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        sealed class Scene : IDisposable
        {
            internal readonly AttachmentConnectionTests.Fixture Fixture = new AttachmentConnectionTests.Fixture();
            internal readonly AnimatorController Controller;
            readonly string folder;
            readonly Material material;

            internal Scene(bool spring = false)
            {
                var descriptorType = SdkType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                if (descriptorType == null || Shader.Find("lilToon") == null || spring && SdkType(PhysBoneSpringExport.PhysBoneType) == null)
                {
                    Dispose(); Assert.Ignore("Install the real VRChat SDK and lilToon.");
                }
                // The export writes lilToon materials only.
                material = new Material(Shader.Find("lilToon"));
                foreach (var skin in Fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterials = new[] { material };
                var name = "__ExpressionBones_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
                Controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
                var idle = Controller.layers[0].stateMachine.AddState("Idle"); idle.writeDefaultValues = false;
                var descriptor = Fixture.Source.AddComponent(descriptorType);
                using (var data = new SerializedObject(descriptor))
                {
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var list = data.FindProperty("baseAnimationLayers"); list.arraySize = 1;
                    var layer = list.GetArrayElementAtIndex(0); var type = layer.FindPropertyRelative("type");
                    type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = Controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                // An object the export leaves out (inactive) and an empty one
                // the export prunes.
                var prop = new GameObject("Hidden prop", typeof(MeshFilter), typeof(MeshRenderer));
                prop.transform.SetParent(Fixture.Source.transform, false);
                prop.GetComponent<MeshFilter>().sharedMesh = Fixture.Mesh;
                prop.GetComponent<MeshRenderer>().sharedMaterial = material;
                prop.SetActive(false);
                new GameObject("Marker").transform.SetParent(Fixture.Source.transform, false);
                if (spring)
                {
                    var physBone = Fixture.Source.transform.Find(Joint).gameObject.AddComponent(SdkType(PhysBoneSpringExport.PhysBoneType));
                    using var data = new SerializedObject(physBone);
                    data.FindProperty("endpointPosition").vector3Value = new Vector3(0, .1f, 0);
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            internal AnimationClip Add(AnimationClip clip) { AssetDatabase.AddObjectToAsset(clip, Controller); return clip; }

            internal byte[] Export(List<string> warnings, params AnimationClip[] faces) =>
                UniVrmOneClickExporter.Export(Fixture.Source, "Expression bones", "Tests", warnings,
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None },
                    listFaces: faces.Select(face => (face.name, face, 0f)).ToList());

            public void Dispose()
            {
                Fixture.Dispose();
                if (material != null) Object.DestroyImmediate(material);
                if (folder != null) AssetDatabase.DeleteAsset(folder);
            }
        }

        static Dictionary<string, object> Extension(byte[] bytes) =>
            (Dictionary<string, object>)((Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"])[ExpressionAnimationData.Extension];

        static Dictionary<string, object> At(List<object> list, int index) => (Dictionary<string, object>)list[index];

        static int NodeNamed(Dictionary<string, object> json, string name)
        {
            var nodes = (List<object>)json["nodes"];
            return Enumerable.Range(0, nodes.Count).Single(i => (string)At(nodes, i)["name"] == name);
        }

        [Test]
        public void BonesAndVisibilityAreWrittenInGltfSpace()
        {
            using var scene = new Scene(spring: true);
            var nod = scene.Add(Clip("Nod", (Shape, AnimationCurve.Linear(0, 35, 1, 100)),
                Held(Bone(Joint, "localEulerAnglesRaw.x"), .01f), (Bone(Joint, "localEulerAnglesRaw.y"), AnimationCurve.Linear(0, 0, 1, 30)),
                (Bone(Joint, "m_LocalPosition.x"), AnimationCurve.Linear(0, 0, 1, .02f)),
                Held(Bone("Hips/Spine/Chest/Neck", "localEulerAnglesRaw.x"), 10), Held(Bone("Marker", "localEulerAnglesRaw.y"), 30),
                Held(Shown("Back"), 0)));
            var prop = scene.Add(Clip("Prop", Held(Bone(Joint, "localEulerAnglesRaw.z"), 20), Held(Shown("Hidden prop"), 1), Held(Shown("Back"), 0)));
            var still = scene.Add(Clip("Still", Held(Shape, 80)));
            var warnings = new List<string>();
            var bytes = scene.Export(warnings, nod, prop, still);
            var detail = string.Join("\n", warnings);
            var json = GlbDocument.Read(bytes).Json;
            var extension = Extension(bytes);
            var document = ExpressionAnimationData.ReadDocument(extension);
            var transforms = ExpressionAnimationData.ReadTransforms(extension, document);
            Assert.That(document.Expressions.Select(e => e.Expression), Is.EquivalentTo(new[] { "Nod", "Prop", "Still" }), detail);
            Assert.That(document.Animations.Select(a => a.Expression), Is.EqualTo(new[] { "Nod" }));
            Assert.That(transforms.Select(t => t.Expression), Is.EquivalentTo(new[] { "Nod", "Prop" }));
            var custom = (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)json["extensions"])["VRMC_vrm"])["expressions"];
            custom = (Dictionary<string, object>)custom["custom"];
            var joint = ((List<object>)At((List<object>)json["nodes"], NodeNamed(json, "Independent hair"))["children"]).Select(Convert.ToInt32).Single();
            var back = NodeNamed(json, "Back");

            var moving = transforms.Single(t => t.Expression == "Nod");
            Assert.That(moving.Duration, Is.EqualTo(document.Animations.Single().Duration));
            Assert.That(moving.Loop, Is.False);
            Assert.That(moving.Tracks.Select(t => t.Node + ":" + t.Property),
                Is.EquivalentTo(new[] { joint + ":euler", joint + ":translation", back + ":active" }));
            // UniVRM mirrors X: euler (x, -y, -z) and position (-x, y, z),
            // values and tangents alike. Noise on x is the prepared 0.
            var euler = moving.Tracks.Single(t => t.Property == ExpressionTransformAnimation.Euler);
            Assert.That(euler.Curves[0].Keys.Single().Value, Is.EqualTo(0));
            Assert.That(euler.Curves[1].Keys.Select(k => k.Value), Is.EqualTo(new[] { 0d, -30d }).Within(1e-4));
            // AnimationCurve.Linear leaves the outer tangents at 0.
            Assert.That(euler.Curves[1].Keys.Select(k => k.OutTangent.Value), Is.EqualTo(new[] { -30d, 0d }).Within(1e-4));
            Assert.That(euler.Curves[1].Evaluate(.5), Is.EqualTo(-15).Within(1e-4));
            var position = moving.Tracks.Single(t => t.Property == ExpressionTransformAnimation.Translation);
            Assert.That(position.Curves[0].Keys.Select(k => k.Value), Is.EqualTo(new[] { 0d, -.02d }).Within(1e-6));
            Assert.That(position.Curves[0].Keys.Select(k => k.InTangent.Value), Is.EqualTo(new[] { 0d, -.02d }).Within(1e-6));
            // The held components are the node's own glTF translation.
            var translation = ((List<object>)At((List<object>)json["nodes"], joint)["translation"]).Select(Convert.ToDouble).ToArray();
            Assert.That(position.Curves[1].Keys.Single().Value, Is.EqualTo(translation[1]).Within(1e-6));
            Assert.That(position.Curves[2].Keys.Single().Value, Is.EqualTo(translation[2]).Within(1e-6));
            Assert.That(moving.Tracks.Single(t => t.Property == ExpressionTransformAnimation.Active).Curves.Single().Evaluate(.5), Is.EqualTo(0));
            // The joint stays a spring joint; the app reads the expression's
            // rotation as the spring's base.
            var springs = (List<object>)((Dictionary<string, object>)((Dictionary<string, object>)json["extensions"])["VRMC_springBone"])["springs"];
            Assert.That(springs.SelectMany(s => (List<object>)((Dictionary<string, object>)s)["joints"])
                .Select(j => Convert.ToInt32(((Dictionary<string, object>)j)["node"])), Does.Contain(joint));

            // Held non-rest bones without shapes: a still pose, no binds, no
            // BlendShape animation. Showing a left-out object drops every
            // visibility change of the face.
            var pose = transforms.Single(t => t.Expression == "Prop");
            Assert.That(pose.Duration, Is.EqualTo(0));
            Assert.That(pose.Loop, Is.False);
            Assert.That(pose.Tracks.Single().Property, Is.EqualTo(ExpressionTransformAnimation.Euler));
            Assert.That(pose.Tracks.Single().Node, Is.EqualTo(joint));
            Assert.That(pose.Tracks.Single().Curves[2].Keys.Single().Value, Is.EqualTo(-20).Within(1e-4));
            Assert.That((List<object>)((Dictionary<string, object>)custom["Prop"])["morphTargetBinds"], Is.Empty);
            Assert.That(((List<object>)((Dictionary<string, object>)custom["Still"])["morphTargetBinds"]).Count, Is.EqualTo(1));
            // A face without binds leaves blink, mouth and gaze to tracking.
            foreach (var key in new[] { "overrideBlink", "overrideMouth", "overrideLookAt" })
            {
                Assert.That(((Dictionary<string, object>)custom["Prop"])[key], Is.EqualTo("none"), key);
                Assert.That(((Dictionary<string, object>)custom["Nod"])[key], Is.EqualTo("block"), key);
                Assert.That(((Dictionary<string, object>)custom["Still"])[key], Is.EqualTo("block"), key);
            }

            Assert.That(warnings, Does.Contain("Nod: " + ExporterLocalization.T(ExpressionTransformCurves.HumanoidWarning) + "Hips/Spine/Chest/Neck"), detail);
            Assert.That(warnings, Does.Contain("Nod: " + ExporterLocalization.T(ExpressionTransformCurves.UnmappedWarning) + "Marker"), detail);
            Assert.That(warnings, Does.Contain("Prop: " + ExporterLocalization.T(ExpressionTransformCurves.HiddenShownWarning) + "Hidden prop"), detail);
        }

        // The app reads transforms back into Unity with translation (-x, y, z),
        // Quaternion.Euler(x, -y, -z), rotation (x, -y, -z, w) and the scale as
        // is. Read that way, the file plays the pose the clip plays in Unity.
        [Test]
        public void TransformsReadBackAsTheClipPose()
        {
            using var scene = new Scene();
            const string rig = "Independent hair";
            var from = Quaternion.Euler(10, 20, 30);
            var to = Quaternion.Euler(-15, 40, 5);
            (EditorCurveBinding, AnimationCurve) Line(string path, string property, float start, float end) => (Bone(path, property), AnimationCurve.Linear(0, start, 1, end));
            var turn = scene.Add(Clip("Turn", (Shape, AnimationCurve.Linear(0, 35, 1, 100)),
                Line(Joint, "m_LocalPosition.x", .01f, .03f), Line(Joint, "m_LocalPosition.y", .76f, .74f), Line(Joint, "m_LocalPosition.z", -.02f, .02f),
                Line(Joint, "localEulerAnglesRaw.x", 5, 25), Line(Joint, "localEulerAnglesRaw.y", -10, 30), Line(Joint, "localEulerAnglesRaw.z", 15, -20),
                Line(rig, "m_LocalRotation.x", from.x, to.x), Line(rig, "m_LocalRotation.y", from.y, to.y),
                Line(rig, "m_LocalRotation.z", from.z, to.z), Line(rig, "m_LocalRotation.w", from.w, to.w),
                Line(rig, "m_LocalScale.x", 1.1f, .9f), Line(rig, "m_LocalScale.y", 1, 1.2f), Line(rig, "m_LocalScale.z", .95f, 1.05f)));
            var warnings = new List<string>();
            var bytes = scene.Export(warnings, turn);
            var json = GlbDocument.Read(bytes).Json;
            var extension = Extension(bytes);
            var transforms = ExpressionAnimationData.ReadTransforms(extension, ExpressionAnimationData.ReadDocument(extension)).Single();
            var joint = ((List<object>)At((List<object>)json["nodes"], NodeNamed(json, rig))["children"]).Select(Convert.ToInt32).Single();
            var hair = NodeNamed(json, rig);
            Assert.That(transforms.Tracks.Select(t => t.Node + ":" + t.Property), Is.EquivalentTo(new[]
            {
                joint + ":" + ExpressionTransformAnimation.Translation, joint + ":" + ExpressionTransformAnimation.Euler,
                hair + ":" + ExpressionTransformAnimation.Rotation, hair + ":" + ExpressionTransformAnimation.Scale
            }), string.Join("\n", warnings));
            double[] Sample(string property, double time) =>
                transforms.Tracks.Single(t => t.Property == property).Curves.Select(c => c.Evaluate(time)).ToArray();

            var pose = Object.Instantiate(scene.Fixture.Source);
            try
            {
                foreach (var time in new[] { 0f, .25f, .5f, 1f })
                {
                    turn.SampleAnimation(pose, time);
                    var position = Sample(ExpressionTransformAnimation.Translation, time);
                    var euler = Sample(ExpressionTransformAnimation.Euler, time);
                    var rotation = Sample(ExpressionTransformAnimation.Rotation, time);
                    var scale = Sample(ExpressionTransformAnimation.Scale, time);
                    var head = pose.transform.Find(Joint);
                    var parent = pose.transform.Find(rig);
                    Assert.That(Vector3.Distance(new Vector3((float)-position[0], (float)position[1], (float)position[2]), head.localPosition), Is.LessThan(1e-5f), "translation at " + time);
                    Assert.That(Quaternion.Angle(Quaternion.Euler((float)euler[0], (float)-euler[1], (float)-euler[2]), head.localRotation), Is.LessThan(.01f), "euler at " + time);
                    var read = new Quaternion((float)rotation[0], (float)-rotation[1], (float)-rotation[2], (float)rotation[3]).normalized;
                    Assert.That(Quaternion.Angle(read, parent.localRotation), Is.LessThan(.01f), "rotation at " + time);
                    Assert.That(Vector3.Distance(new Vector3((float)scale[0], (float)scale[1], (float)scale[2]), parent.localScale), Is.LessThan(1e-5f), "scale at " + time);
                }
            }
            finally { Object.DestroyImmediate(pose); }
        }

        // A face whose only change the export leaves out writes nothing, and
        // the export goes on without the extension.
        [Test]
        public void AFaceThatLosesEveryTrackLeavesTheFileAlone()
        {
            using var scene = new Scene();
            var shown = scene.Add(Clip("Shown", Held(Shown("Hidden prop"), 1)));
            var warnings = new List<string>();
            var bytes = scene.Export(warnings, shown);
            var extensions = (Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
            Assert.That(extensions.ContainsKey(ExpressionAnimationData.Extension), Is.False, string.Join("\n", warnings));
            Assert.That(warnings, Does.Contain("Shown: " + ExporterLocalization.T(ExpressionTransformCurves.HiddenShownWarning) + "Hidden prop"));
        }

        // While the export is off, no face writes bones or visibility, as in
        // 1.4.2: a face with BlendShapes keeps them, one without is left out.
        [Test]
        public void WhileOffNoFaceWritesBonesOrVisibility()
        {
            using var off = new ExpressionTransformsSwitch(false);
            using var scene = new Scene();
            var nod = scene.Add(Clip("Nod", Held(Shape, 80), Held(Bone(Joint, "localEulerAnglesRaw.y"), 30), Held(Shown("Back"), 0)));
            var turn = scene.Add(Clip("Turn", Held(Bone(Joint, "localEulerAnglesRaw.z"), 20)));
            var still = scene.Add(Clip("Still", Held(Shape, 60)));
            Assert.That(ExpressionTransformCurves.Read(scene.Fixture.Source, nod).Tracks, Is.Empty);
            var warnings = new List<string>();
            var bytes = scene.Export(warnings, nod, turn, still);
            var extension = Extension(bytes);
            Assert.That(extension.ContainsKey("transforms"), Is.False);
            Assert.That(ExpressionAnimationData.ReadDocument(extension).Expressions.Select(e => e.Expression),
                Is.EquivalentTo(new[] { "Nod", "Still" }), string.Join("\n", warnings));
        }

        // Rest-valued bone and visibility curves change nothing in the file.
        [Test]
        public void FacesWithoutBonesOrVisibilityWriteNoTransforms()
        {
            using var scene = new Scene();
            AnimationClip[] Faces(bool restCurves) => new[]
            {
                scene.Add(Clip("Smile", restCurves ? new[] { Held(Shape, 80), Held(Bone(Joint, "localEulerAnglesRaw.x"), .01f), Held(Shown("Back"), 1) }
                    : new[] { Held(Shape, 80) })),
                scene.Add(Clip("Wave", restCurves ? new[] { (Shape, AnimationCurve.Linear(0, 35, 1, 100)), Held(Bone(Joint, "m_LocalPosition.y"), 1.7502f) }
                    : new[] { (Shape, AnimationCurve.Linear(0, 35, 1, 100)) }))
            };
            var plain = scene.Export(new List<string>(), Faces(false));
            var withRest = scene.Export(new List<string>(), Faces(true));
            Assert.That(Extension(plain).ContainsKey("transforms"), Is.False);
            Assert.That(ExpressionAnimationData.ReadDocument(Extension(plain)).Animations.Single().Expression, Is.EqualTo("Wave"));
            Assert.That(withRest, Is.EqualTo(plain));
        }
    }

    // Turns the export of the expression transforms on or off for one test
    // and puts the previous state back.
    internal sealed class ExpressionTransformsSwitch : IDisposable
    {
        readonly bool previous = ExpressionTransformCurves.Enabled;
        internal ExpressionTransformsSwitch(bool enabled) => ExpressionTransformCurves.Enabled = enabled;
        public void Dispose() => ExpressionTransformCurves.Enabled = previous;
    }
}
