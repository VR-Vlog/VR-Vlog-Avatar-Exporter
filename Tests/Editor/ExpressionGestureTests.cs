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
    public sealed class ExpressionGestureTests
    {
        private string folder;
        private GameObject avatar;
        private Mesh mesh;
        private AnimatorController fx;
        private AnimationClip smile, angry, wink, unlisted;

        [TearDown]
        public void TearDown()
        {
            if (avatar != null) Object.DestroyImmediate(avatar);
            if (mesh != null) Object.DestroyImmediate(mesh);
            if (folder != null) AssetDatabase.DeleteAsset(folder);
        }

        private static Dictionary<string, object> Reference(string expression, string drive = null)
        {
            var reference = new Dictionary<string, object> { ["expression"] = expression };
            if (drive != null) reference["drive"] = drive;
            return reference;
        }

        private static Dictionary<string, object> Table(Action<List<object>> edit = null)
        {
            var cells = Enumerable.Range(0, ExpressionGestureData.CellCount).Select(_ => (object)new List<object>()).ToList();
            cells[ExpressionGestureData.Cell(1, 0)] = new List<object> { Reference("Smile", "left") };
            cells[ExpressionGestureData.Cell(2, 3)] = new List<object> { Reference("Angry"), Reference("Wink") };
            cells[ExpressionGestureData.Cell(0, 4)] = null;
            edit?.Invoke(cells);
            return new Dictionary<string, object> { ["schemaVersion"] = 1L, ["cells"] = cells };
        }

        [Test]
        public void TableRoundTripsWithCellOrderDriveAndUndeterminedCells()
        {
            var data = ExpressionGestureData.Read(Table());
            Assert.That(ExpressionGestureData.Cell(2, 3), Is.EqualTo(19));
            Assert.That(data.Cells[ExpressionGestureData.Cell(0, 4)], Is.Null);
            Assert.That(data.Cells[ExpressionGestureData.Cell(0, 0)], Is.Empty);
            Assert.That(data.Cells[ExpressionGestureData.Cell(1, 0)].Single().Drive, Is.EqualTo(ExpressionGestureData.DriveLeft));
            Assert.That(data.Cells[ExpressionGestureData.Cell(2, 3)].Select(reference => reference.Expression), Is.EqualTo(new[] { "Angry", "Wink" }));
            Assert.That(data.Cells[ExpressionGestureData.Cell(2, 3)].Select(reference => reference.Drive), Is.All.Null);
            var written = ExpressionGestureData.Write(data);
            Assert.That(JsonDom.Serialize(written), Is.EqualTo(JsonDom.Serialize(Table())));
        }

        [Test]
        public void TableOutsideTheSchemaIsRejected()
        {
            var cases = new (string Name, Dictionary<string, object> Table)[]
            {
                ("version", new Dictionary<string, object> { ["schemaVersion"] = 2L, ["cells"] = Table()["cells"] }),
                ("extra key", new Dictionary<string, object> { ["schemaVersion"] = 1L, ["cells"] = Table()["cells"], ["note"] = "x" }),
                ("63 cells", Table(cells => cells.RemoveAt(63))),
                ("unknown drive", Table(cells => cells[0] = new List<object> { Reference("Smile", "both") })),
                ("extra reference key", Table(cells => cells[0] = new List<object> { new Dictionary<string, object> { ["expression"] = "Smile", ["weight"] = 1L } })),
                ("empty name", Table(cells => cells[0] = new List<object> { Reference("") })),
                ("long name", Table(cells => cells[0] = new List<object> { Reference(new string('a', 1025)) })),
                ("same expression twice", Table(cells => cells[0] = new List<object> { Reference("Smile"), Reference("Smile", "right") })),
                ("nine references", Table(cells => cells[0] = Enumerable.Range(0, 9).Select(i => (object)Reference("E" + i)).ToList())),
                ("cell not an array", Table(cells => cells[0] = "Smile")),
                ("no reference anywhere", Table(cells => { for (var i = 0; i < cells.Count; i++) cells[i] = new List<object>(); })),
            };
            foreach (var (name, table) in cases)
                Assert.Throws<InvalidOperationException>(() => ExpressionGestureData.Read(table), name);
            Assert.Throws<InvalidOperationException>(() => ExpressionGestureData.Write(new ExpressionGestureData()));
        }

        [Test]
        public void CaptureReadsEveryGesturePairInLayerOrderWithTriggers()
        {
            if (!BuildAvatar()) return;
            var capture = Capture();
            Assert.That(capture, Is.Not.Null);
            var data = capture.Resolve(face => new[] { new[] { "Smile" }, new[] { "Angry" }, new[] { "Wink" } }[face]);
            Assert.That(Names(data, 0, 0), Is.Empty);
            // The left fist plays Smile through a blend tree on the left trigger.
            Assert.That(Names(data, 1, 0), Is.EqualTo(new[] { "Smile@left" }));
            Assert.That(Names(data, 1, 1), Is.EqualTo(new[] { "Smile@left" }));
            Assert.That(Names(data, 2, 0), Is.EqualTo(new[] { "Angry" }));
            Assert.That(Names(data, 0, 3), Is.EqualTo(new[] { "Wink" }));
            Assert.That(Names(data, 2, 3), Is.EqualTo(new[] { "Angry", "Wink" }));
            // A Random driver cannot be settled.
            Assert.That(data.Cells[ExpressionGestureData.Cell(0, 4)], Is.Null);
            Assert.That(data.Cells[ExpressionGestureData.Cell(2, 4)], Is.Null);
            // A face clip that is not on the board: alone it is unknown, next
            // to a board face it is left out.
            Assert.That(data.Cells[ExpressionGestureData.Cell(0, 5)], Is.Null);
            Assert.That(Names(data, 2, 5), Is.EqualTo(new[] { "Angry" }));
            // Gestures the FX does not use show nothing.
            Assert.That(Names(data, 7, 7), Is.Empty);
        }

        [Test]
        public void InjectedExtensionIsTheCapturedTableUnderTheWrittenNames()
        {
            if (!BuildAvatar()) return;
            var capture = Capture();
            var expressions = new List<VrmMenuExpressions.Expression>
            {
                new VrmMenuExpressions.Expression { Name = "smile", RegisteredName = "Smile", Clip = smile },
                // A face written as an identical face takes that face's name.
                new VrmMenuExpressions.Expression { Name = "angry", MergedInto = "Smile 2", Clip = angry },
                // Wink is not in the file, so it is not referenced.
                new VrmMenuExpressions.Expression { Name = "wink", RegisteredName = "Wink", Clip = wink },
            };
            var bytes = Glb("Smile", "Smile 2");
            var injected = capture.Inject(bytes, expressions);
            var json = GlbDocument.Read(injected).Json;
            Assert.That((List<object>)json["extensionsUsed"], Does.Contain(ExpressionGestureData.Extension));
            var written = ExpressionGestureData.Read(((Dictionary<string, object>)json["extensions"])[ExpressionGestureData.Extension]);
            var expected = capture.Resolve(face => face == 0 ? new[] { "Smile" } : face == 1 ? new[] { "Smile 2" } : Array.Empty<string>());
            Assert.That(JsonDom.Serialize(ExpressionGestureData.Write(written)), Is.EqualTo(JsonDom.Serialize(ExpressionGestureData.Write(expected))));
            Assert.That(Names(written, 2, 3), Is.EqualTo(new[] { "Smile 2" }));
            Assert.That(written.Cells[ExpressionGestureData.Cell(0, 3)], Is.Null);
            // A file that carries none of the faces stays as it was.
            Assert.That(capture.Inject(Glb("Other"), expressions), Is.EqualTo(Glb("Other")));
        }

        [Test]
        public void AvatarWithoutHandGesturesWritesNothing()
        {
            if (!BuildAvatar()) return;
            // The face layers stay and switch on renamed parameters, so only the
            // missing GestureLeft and GestureRight stop it.
            static string Renamed(string name) => name == "GestureLeft" || name == "GestureRight" ? "Hand" + name.Substring("Gesture".Length) : name;
            fx.parameters = fx.parameters.Select(parameter => { parameter.name = Renamed(parameter.name); return parameter; }).ToArray();
            foreach (var layer in fx.layers)
                foreach (var transition in layer.stateMachine.anyStateTransitions)
                    transition.conditions = transition.conditions.Select(condition => { condition.parameter = Renamed(condition.parameter); return condition; }).ToArray();
            Assert.That(Capture(), Is.Null);
            Assert.That(ExpressionGestureCapture.Capture(avatar, Array.Empty<(Object, Object)>()), Is.Null);
        }

        private ExpressionGestureCapture Capture() =>
            ExpressionGestureCapture.Capture(avatar, new (Object, Object)[] { (smile, null), (angry, null), (wink, null) });

        private static string[] Names(ExpressionGestureData data, int left, int right) =>
            data.Cells[ExpressionGestureData.Cell(left, right)]?.Select(reference => reference.Expression + (reference.Drive == null ? "" : "@" + reference.Drive)).ToArray();

        private static byte[] Glb(params string[] custom) => GlbDocument.Create(new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
            ["extensionsUsed"] = new List<object> { "VRMC_vrm" },
            ["extensions"] = new Dictionary<string, object>
            {
                ["VRMC_vrm"] = new Dictionary<string, object>
                {
                    ["expressions"] = new Dictionary<string, object> { ["custom"] = custom.ToDictionary(name => name, name => (object)new Dictionary<string, object>()) }
                }
            }
        }, Array.Empty<byte>()).Write();

        // Left hand: fist plays Smile weighted by the left trigger, open hand
        // plays Angry. Right hand: point plays Wink, victory enters a state
        // with a Random driver, rock'n'roll plays a clip not on the board.
        private bool BuildAvatar()
        {
            var descriptorType = SdkType("VRCAvatarDescriptor");
            if (descriptorType == null || SdkType("VRCAvatarParameterDriver") == null) { Assert.Ignore("Install the real VRChat SDK."); return false; }
            var name = "__ExpressionGestures_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            avatar = new GameObject("Avatar", typeof(Animator));
            var body = new GameObject("Body", typeof(SkinnedMeshRenderer)); body.transform.SetParent(avatar.transform, false);
            mesh = new Mesh { name = "Gesture faces" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
            foreach (var shape in new[] { "Smile", "Angry", "Wink", "Other" })
                mesh.AddBlendShapeFrame(shape, 100, Enumerable.Repeat(Vector3.up * .01f, 3).ToArray(), null, null);
            body.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            foreach (var parameter in new[] { "GestureLeft", "GestureRight" }) fx.AddParameter(parameter, AnimatorControllerParameterType.Int);
            foreach (var parameter in new[] { "GestureLeftWeight", "GestureRightWeight" }) fx.AddParameter(parameter, AnimatorControllerParameterType.Float);
            fx.AddParameter("Picked", AnimatorControllerParameterType.Int);
            smile = Clip("Smile", "Smile"); angry = Clip("Angry", "Angry"); wink = Clip("Wink", "Wink"); unlisted = Clip("Other", "Other");
            var empty = Clip("Empty", null);
            fx.layers[0].stateMachine.defaultState = fx.layers[0].stateMachine.AddState("Base");

            var left = Layer("Left hand");
            var leftIdle = State(left, "Idle", empty);
            var tree = new BlendTree { name = "Fist trigger", blendType = BlendTreeType.Simple1D, blendParameter = "GestureLeftWeight", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, fx);
            tree.AddChild(empty, 0); tree.AddChild(smile, 1);
            var fist = State(left, "Fist", tree);
            var open = State(left, "Open", angry);
            Enter(left, leftIdle, "GestureLeft", 0); Enter(left, fist, "GestureLeft", 1); Enter(left, open, "GestureLeft", 2);

            var right = Layer("Right hand");
            var rightIdle = State(right, "Idle", empty);
            var point = State(right, "Point", wink);
            var random = State(right, "Random", empty);
            ParameterDriverExpressionTests.Driver(random, ParameterDriverExpressionTests.Op("Random", "Picked", 0));
            var other = State(right, "Other", unlisted);
            Enter(right, rightIdle, "GestureRight", 0); Enter(right, point, "GestureRight", 3); Enter(right, random, "GestureRight", 4); Enter(right, other, "GestureRight", 5);
            foreach (var value in new[] { 1, 2, 6, 7 }) Enter(right, rightIdle, "GestureRight", value);

            var descriptor = avatar.AddComponent(descriptorType);
            using var data = new SerializedObject(descriptor);
            data.FindProperty("customizeAnimationLayers").boolValue = true;
            var layers = data.FindProperty("baseAnimationLayers"); layers.arraySize = 1;
            var layer = layers.GetArrayElementAtIndex(0); var type = layer.FindPropertyRelative("type");
            type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
            layer.FindPropertyRelative("isDefault").boolValue = false;
            layer.FindPropertyRelative("animatorController").objectReferenceValue = fx;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return true;
        }

        private AnimationClip Clip(string name, string shape)
        {
            var clip = new AnimationClip { name = name };
            if (shape != null)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape), AnimationCurve.Constant(0, 1, 100));
            AssetDatabase.AddObjectToAsset(clip, fx); return clip;
        }

        private AnimatorStateMachine Layer(string name)
        {
            fx.AddLayer(name); var layers = fx.layers; layers[layers.Length - 1].defaultWeight = 1; fx.layers = layers;
            return layers[layers.Length - 1].stateMachine;
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion)
        {
            var state = machine.AddState(name); state.motion = motion; state.writeDefaultValues = true;
            if (machine.states.Length == 1) machine.defaultState = state;
            return state;
        }

        private static void Enter(AnimatorStateMachine machine, AnimatorState state, string parameter, int value)
        {
            var transition = machine.AddAnyStateTransition(state);
            transition.hasExitTime = false; transition.duration = 0; transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.Equals, value, parameter);
        }

        private static Type SdkType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("VRC.SDK3.Avatars.Components." + name)).FirstOrDefault(type => type != null);
    }
}
