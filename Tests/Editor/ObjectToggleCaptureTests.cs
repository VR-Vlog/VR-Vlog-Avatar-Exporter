using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.AvatarAnimator;
using UnityEngine.TestTools;
using VRVlog.AvatarFormats;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using VRVlog.ObjectToggles;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // VRVLOG_object_toggles and VRVLOG_object_toggle_transforms: the data
    // contract, the capture from the menu and FX of a VRChat avatar, and the
    // extensions in a real export. The capture
    // tests use the real SDK types through reflection and skip without them.
    public sealed class ObjectToggleCaptureTests
    {
        [TearDown]
        public void ResetFaultInjection() => ExportTestHooks.FaultInjection = null;

        static ObjectToggleData.State State(int value, params bool[] active)
        {
            var state = new ObjectToggleData.State { Value = value };
            state.Active.AddRange(active);
            return state;
        }

        static ObjectToggleData.Item Item(string id, string name, string parameter, int value, params string[] path)
        {
            var item = new ObjectToggleData.Item { Id = id, Name = name, Parameter = parameter, Value = value };
            item.Path.AddRange(path);
            return item;
        }

        static ObjectToggleData Data()
        {
            var data = new ObjectToggleData();
            var outfit = new ObjectToggleData.Group { Parameter = "Outfit", Name = "Clothes", DefaultValue = 1 };
            outfit.Targets.AddRange(new[] { 4, 5 });
            outfit.States.Add(State(0, false, false));
            outfit.States.Add(State(1, true, false));
            outfit.States.Add(State(2, false, true));
            var hat = new ObjectToggleData.Group { Parameter = "Hat", Name = "Hat", DefaultValue = 0 };
            hat.Targets.Add(7);
            hat.States.Add(State(0, false));
            hat.States.Add(State(1, true));
            data.Groups.Add(outfit);
            data.Groups.Add(hat);
            data.Items.Add(Item("m0", "Dress", "Outfit", 1, "Clothes"));
            data.Items.Add(Item("m1", "Suit", "Outfit", 2, "Clothes"));
            data.Items.Add(Item("m2", "Hat", "Hat", 1));
            return data;
        }

        static Dictionary<string, object> Json() =>
            (Dictionary<string, object>)JsonDom.Parse(JsonDom.Serialize(ObjectToggleData.Write(Data())));

        static Dictionary<string, object> At(Dictionary<string, object> root, string list, int index) =>
            (Dictionary<string, object>)((List<object>)root[list])[index];

        static void Rejects(Action<Dictionary<string, object>> change)
        {
            var json = Json();
            change(json);
            Assert.Throws<InvalidOperationException>(() => ObjectToggleData.Read(json));
        }

        static string[] States(ObjectToggleData.Group group) =>
            group.States.Select(state => state.Value + ":" + string.Concat(state.Active.Select(active => active ? "T" : "F"))).ToArray();

        [Test]
        public void DataRoundTripsThroughJson()
        {
            var read = ObjectToggleData.Read(Json());
            Assert.That(read.Groups.Select(group => group.Parameter + "/" + group.Name + "/" + group.DefaultValue),
                Is.EqualTo(new[] { "Outfit/Clothes/1", "Hat/Hat/0" }));
            Assert.That(read.GroupFor("Outfit").Targets, Is.EqualTo(new[] { 4, 5 }));
            Assert.That(States(read.GroupFor("Outfit")), Is.EqualTo(new[] { "0:FF", "1:TF", "2:FT" }));
            Assert.That(States(read.GroupFor("Hat")), Is.EqualTo(new[] { "0:F", "1:T" }));
            Assert.That(read.Items.Select(item => item.Id + "=" + item.Name + "@" + string.Join("/", item.Path) + ":" + item.Parameter + "=" + item.Value),
                Is.EqualTo(new[] { "m0=Dress@Clothes:Outfit=1", "m1=Suit@Clothes:Outfit=2", "m2=Hat@:Hat=1" }));
            Assert.That(JsonDom.Serialize(ObjectToggleData.Write(read)), Is.EqualTo(JsonDom.Serialize(ObjectToggleData.Write(Data()))));
        }

        [Test]
        public void DataOutsideTheContractIsRejected()
        {
            Rejects(json => json["schemaVersion"] = 2L);
            Rejects(json => json["extra"] = true);
            Rejects(json => json["groups"] = new List<object>());
            Rejects(json => At(json, "groups", 1)["parameter"] = "Outfit");
            Rejects(json => At(json, "groups", 0)["name"] = "");
            Rejects(json => At(json, "groups", 0)["defaultValue"] = 3L);
            Rejects(json => At(json, "groups", 0)["defaultValue"] = 1.5);
            Rejects(json => At(json, "groups", 1)["targets"] = new List<object> { 4L });
            Rejects(json => At(json, "groups", 1)["targets"] = new List<object> { -1L });
            Rejects(json => ((List<object>)At(json, "groups", 0)["states"]).RemoveAt(0));
            Rejects(json => ((Dictionary<string, object>)((List<object>)At(json, "groups", 0)["states"])[1])["active"] = new List<object> { true });
            Rejects(json => ((Dictionary<string, object>)((List<object>)At(json, "groups", 0)["states"])[1])["value"] = 0L);
            Rejects(json => ((Dictionary<string, object>)((List<object>)At(json, "groups", 0)["states"])[1])["active"] = new List<object> { 1L, 0L });
            Rejects(json => At(json, "items", 0)["value"] = 0L);
            Rejects(json => At(json, "items", 0)["value"] = 3L);
            Rejects(json => At(json, "items", 0)["parameter"] = "Unknown");
            Rejects(json => At(json, "items", 1)["id"] = "m0");
            Rejects(json => At(json, "items", 0)["name"] = new string('a', 1025));
            Rejects(json => ((List<object>)json["items"]).RemoveAt(2));
            Assert.Throws<InvalidOperationException>(() => ObjectToggleData.Write(new ObjectToggleData()), "An empty extension is never written.");
        }

        [Test]
        public void AvatarWithoutTheSdkOrAMenuHasNoToggles()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var warnings = new List<string>();
            Assert.That(ObjectToggleCapture.Capture(fixture.Copy, null, warnings), Is.Null);
            Assert.That(warnings, Is.Empty);
            var bytes = new byte[] { 1, 2, 3 };
            Assert.That(ObjectToggleCapture.Inject(bytes, null), Is.SameAs(bytes));
        }

        [Test]
        public void MenuTogglesBecomeGroupsOfTheObjectsTheySwitch()
        {
            using var avatar = new ToggleAvatar();
            var clone = Object.Instantiate(avatar.Source);
            try
            {
                var warnings = new List<string>();
                var capture = ObjectToggleCapture.Capture(clone, null, warnings);
                Assert.That(capture, Is.Not.Null, string.Join("\n", warnings));
                var nodes = clone.GetComponentsInChildren<Transform>(true).Select(transform => transform.gameObject).ToList();
                var data = capture.Resolve(nodes.IndexOf, warnings);
                string Name(int node) => nodes[node].name;

                Assert.That(data.Groups.Select(group => group.Parameter), Is.EqualTo(new[] { "Hat", "Outfit", "Glasses", "Accessory" }));
                // A bool toggle of an object that is off by default.
                var hat = data.GroupFor("Hat");
                Assert.That(hat.Name, Is.EqualTo("Hat"));
                Assert.That(hat.DefaultValue, Is.EqualTo(0));
                Assert.That(hat.Targets.Select(Name), Is.EqualTo(new[] { "Hat" }));
                Assert.That(States(hat), Is.EqualTo(new[] { "0:F", "1:T" }));
                // Two outfits on one int parameter exclude each other; 0 wears neither.
                var outfit = data.GroupFor("Outfit");
                Assert.That(outfit.Name, Is.EqualTo("Clothes"), "The group is named after the submenu its items share.");
                Assert.That(outfit.DefaultValue, Is.EqualTo(1));
                Assert.That(outfit.Targets.Select(Name), Is.EqualTo(new[] { "Dress", "Suit" }));
                Assert.That(States(outfit), Is.EqualTo(new[] { "0:FF", "1:TF", "2:FT" }));
                // A toggle that only moves a blend shape switches no object.
                Assert.That(data.GroupFor("Noop"), Is.Null);
                // An object two parameters switch stays with the first; the
                // second keeps only what it alone switches.
                Assert.That(data.GroupFor("Glasses").Targets.Select(Name), Is.EqualTo(new[] { "Glasses" }));
                Assert.That(data.GroupFor("Accessory").Targets.Select(Name), Is.EqualTo(new[] { "Ribbon" }));
                Assert.That(States(data.GroupFor("Accessory")), Is.EqualTo(new[] { "0:F", "1:T" }));
                Assert.That(warnings, Has.Count.EqualTo(1));
                Assert.That(warnings[0], Does.EndWith(": Glasses"));

                Assert.That(data.Items.Select(item => item.Name + "@" + string.Join("/", item.Path) + ":" + item.Parameter + "=" + item.Value),
                    Is.EqualTo(new[] { "Hat@:Hat=1", "Dress@Clothes:Outfit=1", "Suit@Clothes:Outfit=2", "Glasses@:Glasses=1", "Accessory@:Accessory=1" }));
                Assert.That(data.Items.Select(item => item.Id).Distinct().Count(), Is.EqualTo(5));
                Assert.DoesNotThrow(() => ObjectToggleData.Write(data));

                // The copy shows every target so its mesh is written; the source is untouched.
                foreach (var path in new[] { "Hat", "Clothes/Dress", "Clothes/Suit", "Glasses", "Ribbon" })
                    Assert.That(clone.transform.Find(path).gameObject.activeSelf, Is.True, path);
                foreach (var path in new[] { "Hat", "Clothes/Suit", "Glasses", "Ribbon" })
                    Assert.That(avatar.Source.transform.Find(path).gameObject.activeSelf, Is.False, path);

                // A target the export removed is left out of the file, and so
                // is an item that no longer switches anything: the app never
                // gets a switch that does nothing.
                warnings.Clear();
                var withoutSuit = capture.Resolve(target => target.name == "Suit" ? -1 : nodes.IndexOf(target), warnings);
                Assert.That(withoutSuit.GroupFor("Outfit").Targets.Select(Name), Is.EqualTo(new[] { "Dress" }));
                Assert.That(States(withoutSuit.GroupFor("Outfit")), Is.EqualTo(new[] { "0:F", "1:T" }));
                Assert.That(withoutSuit.Items.Select(item => item.Name), Is.EqualTo(new[] { "Hat", "Dress", "Glasses", "Accessory" }));
                Assert.That(warnings.Single(), Does.EndWith("Clothes/Suit"));
                Assert.DoesNotThrow(() => ObjectToggleData.Write(withoutSuit));
                // A group whose every target is gone is left out with its items.
                warnings.Clear();
                var withoutHat = capture.Resolve(target => target.name == "Hat" ? -1 : nodes.IndexOf(target), warnings);
                Assert.That(withoutHat.GroupFor("Hat"), Is.Null);
                Assert.That(withoutHat.Items.Select(item => item.Name), Is.EqualTo(new[] { "Dress", "Suit", "Glasses", "Accessory" }));
                Assert.That(warnings.Single(), Does.EndWith("Hat"));
            }
            finally { Object.DestroyImmediate(clone); }
        }

        // A parent-constrained prop inside a toggled object moves under its
        // source, out of the object the app hides: it joins that toggle so it
        // still hides with it (#596), whichever of a nested pair moves first.
        // One whose source sits inside a toggle it is not in, or under a prop
        // that joined one, would start hiding with that toggle, so it keeps
        // the pose. A prop without a mesh shows nothing either way.
        [Test]
        public void APropMovedOutOfAToggleStillHidesWithIt()
        {
            using var avatar = new ToggleAvatar();
            var clone = Object.Instantiate(avatar.Source);
            try
            {
                var hat = clone.transform.Find("Hat");
                Transform Node(string name, Transform parent)
                {
                    var node = new GameObject(name).transform;
                    node.SetParent(parent, false);
                    return node;
                }
                Transform Prop(string name, Transform parent, Transform source, bool mesh = true)
                {
                    var prop = Node(name, parent);
                    if (mesh)
                    {
                        prop.gameObject.AddComponent<MeshFilter>().sharedMesh = hat.GetComponent<MeshFilter>().sharedMesh;
                        prop.gameObject.AddComponent<MeshRenderer>().sharedMaterial = hat.GetComponent<MeshRenderer>().sharedMaterial;
                    }
                    var constraint = prop.gameObject.AddComponent<UnityEngine.Animations.ParentConstraint>();
                    constraint.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = source, weight = 1 });
                    constraint.constraintActive = true;
                    return prop;
                }
                var anchor = Node("Anchor", clone.transform);
                anchor.localPosition = new Vector3(0, 1, 0);
                var other = Node("Other anchor", clone.transform);
                var socket = Node("Socket", clone.transform.Find("Glasses"));
                var room = Prop("Room", hat, anchor);
                var inner = Prop("Inner", room, other);
                var lamp = Prop("Lamp", hat, socket);
                var stray = Prop("Stray", clone.transform, socket);
                var rider = Prop("Rider", clone.transform, room);
                var empty = Prop("Empty", hat, socket, mesh: false);
                empty.gameObject.AddComponent<MeshFilter>();
                empty.gameObject.AddComponent<MeshRenderer>();

                var warnings = new List<string>();
                var capture = ObjectToggleCapture.Capture(clone, null, warnings);
                Assert.That(capture, Is.Not.Null, string.Join("\n", warnings));
                var fallbacks = new ExportFallbackReport();
                ConstraintExport.Convert(avatar.Source, clone, warnings, fallbacks, capture);

                Assert.That(room.parent, Is.SameAs(anchor), "The prop follows its source.");
                Assert.That(inner.parent, Is.SameAs(other), "The prop inside it moves after it left the toggle.");
                Assert.That(lamp.parent, Is.SameAs(hat), "Its source is inside another toggle.");
                Assert.That(stray.parent, Is.SameAs(clone.transform), "Its source is inside a toggle it is not in.");
                Assert.That(rider.parent, Is.SameAs(clone.transform), "Its source joined a toggle it is not in.");
                Assert.That(empty.parent, Is.SameAs(socket), "Without a mesh the toggles do not matter.");
                Assert.That(fallbacks.Results.Where(entry => entry.Reason == ConstraintExport.OmittedToggle).Select(entry => entry.Path),
                    Is.EquivalentTo(new[] { "Hat/Lamp", "Stray", "Rider" }));

                var nodes = clone.GetComponentsInChildren<Transform>(true).Select(transform => transform.gameObject).ToList();
                string Name(int node) => nodes[node].name;
                var data = capture.Resolve(nodes.IndexOf, warnings);
                var group = data.GroupFor("Hat");
                Assert.That(group.Targets.Select(Name), Is.EqualTo(new[] { "Hat", "Room", "Inner" }));
                Assert.That(States(group), Is.EqualTo(new[] { "0:FFF", "1:TTT" }));
                Assert.That(data.GroupFor("Glasses").Targets.Select(Name), Is.EqualTo(new[] { "Glasses" }));
                Assert.DoesNotThrow(() => ObjectToggleData.Write(data));
            }
            finally { Object.DestroyImmediate(clone); }
        }

        // An aim with an offset turns a node above its object (#770). The
        // toggles still switch the object itself: the node holds nothing else.
        [Test]
        public void AnAimWithAnOffsetKeepsItsObjectInTheToggle()
        {
            using var avatar = new ToggleAvatar();
            var clone = Object.Instantiate(avatar.Source);
            try
            {
                var hat = clone.transform.Find("Hat");
                var anchor = new GameObject("Anchor").transform;
                anchor.SetParent(clone.transform, false);
                anchor.localPosition = new Vector3(0, 1, 0);
                var look = new GameObject("Look").transform;
                look.SetParent(clone.transform, false);
                look.position = hat.position + Vector3.right;
                var visor = new GameObject("Visor").transform;
                visor.SetParent(hat, false);
                visor.gameObject.AddComponent<MeshFilter>().sharedMesh = hat.GetComponent<MeshFilter>().sharedMesh;
                visor.gameObject.AddComponent<MeshRenderer>().sharedMaterial = hat.GetComponent<MeshRenderer>().sharedMaterial;
                void Aim(Transform target)
                {
                    var aim = target.gameObject.AddComponent<UnityEngine.Animations.AimConstraint>();
                    aim.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = look, weight = 1 });
                    aim.aimVector = Vector3.forward; aim.constraintActive = true;
                }
                Aim(hat);
                Aim(visor);
                var follow = visor.gameObject.AddComponent<UnityEngine.Animations.PositionConstraint>();
                follow.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = anchor, weight = 1 });
                follow.constraintActive = true;
                hat.rotation = visor.rotation = Quaternion.identity;

                var warnings = new List<string>();
                var capture = ObjectToggleCapture.Capture(clone, null, warnings);
                Assert.That(capture, Is.Not.Null, string.Join("\n", warnings));
                var fallbacks = new ExportFallbackReport();
                ConstraintExport.Convert(avatar.Source, clone, warnings, fallbacks, capture);

                Assert.That(fallbacks.Results.Select(entry => entry.Reason), Is.EquivalentTo(new[]
                    { ConstraintExport.ApproximatedAim, ConstraintExport.ApproximatedAim, ConstraintExport.ApproximatedPosition }));
                Assert.That(hat.parent.name, Is.EqualTo("Hat-" + ConstraintExport.RelayName));
                Assert.That(visor.parent.name, Is.EqualTo("Visor-" + ConstraintExport.RelayName));
                Assert.That(visor.parent.parent, Is.SameAs(anchor));

                var nodes = clone.GetComponentsInChildren<Transform>(true).Select(transform => transform.gameObject).ToList();
                string Name(int node) => nodes[node].name;
                var data = capture.Resolve(nodes.IndexOf, warnings);
                var group = data.GroupFor("Hat");
                Assert.That(group.Targets.Select(Name), Is.EqualTo(new[] { "Hat", "Visor" }));
                Assert.That(States(group), Is.EqualTo(new[] { "0:FF", "1:TT" }));
                Assert.DoesNotThrow(() => ObjectToggleData.Write(data));
            }
            finally { Object.DestroyImmediate(clone); }
        }

        // Only the parameters a layer that can switch objects reads are
        // played; any other leaves every object as its default does.
        [Test]
        public void OnlyParametersOfLayersThatSwitchObjectsArePlayed()
        {
            var directory = TempAssetFolder.Create("__VRVlogObjectToggleLayers_");
            try
            {
                var fx = AnimatorController.CreateAnimatorControllerAtPath(directory + "/FX.controller");
                var serial = 0;
                AnimationClip Clip(params EditorCurveBinding[] bindings)
                {
                    var clip = new AnimationClip();
                    foreach (var binding in bindings) AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1f / 60, 1));
                    AssetDatabase.CreateAsset(clip, directory + "/Clip" + serial++ + ".anim");
                    return clip;
                }
                var hat = EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive");
                var smile = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Smile");
                // Two states of one bool parameter.
                AnimatorStateMachine Layer(string parameter, Motion off, Motion on, bool offWritesDefaults = false, bool onWritesDefaults = false)
                {
                    fx.AddParameter(parameter, AnimatorControllerParameterType.Bool);
                    fx.AddLayer(parameter);
                    var machine = fx.layers.Last().stateMachine;
                    foreach (var (value, motion, writeDefaults) in new[] { (false, off, offWritesDefaults), (true, on, onWritesDefaults) })
                    {
                        var state = machine.AddState(parameter + value);
                        state.motion = motion;
                        state.writeDefaultValues = writeDefaults;
                        var transition = machine.AddAnyStateTransition(state);
                        transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, parameter);
                    }
                    return machine;
                }
                string[] Switching() => ObjectToggleCapture.ParametersThatSwitchObjects(fx).OrderBy(name => name, StringComparer.Ordinal).ToArray();

                Layer("Shape", Clip(smile), Clip());
                // Mixed write defaults matter only once some layer sets m_IsActive.
                Layer("Mixed", Clip(smile), Clip(), onWritesDefaults: true);
                Assert.That(Switching(), Is.Empty, "An FX that sets no m_IsActive is never played.");

                Layer("Hat", Clip(hat), Clip());
                // A clip that drives a parameter another layer may read.
                fx.AddParameter("Driven", AnimatorControllerParameterType.Float);
                Layer("Driver", Clip(EditorCurveBinding.FloatCurve("", typeof(Animator), "Driven")), Clip());
                // A blend tree under a sub-state machine, with its own parameter.
                fx.AddParameter("Mix", AnimatorControllerParameterType.Float);
                var machine = Layer("Nested", Clip(smile), Clip());
                var tree = new BlendTree { name = "Tree", blendParameter = "Mix", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, fx);
                tree.AddChild(Clip(smile), 0);
                tree.AddChild(Clip(hat), 1);
                machine.AddStateMachine("Inner").AddState("Tree").motion = tree;
                // "Blend" is the name an unused second blend parameter keeps;
                // reading it as well only plays a parameter of that name.
                Assert.That(Switching().Where(name => name != "Blend"), Is.EqualTo(new[] { "Driver", "Hat", "Mix", "Mixed", "Nested" }));

                // A controller this does not read plays every parameter.
                var overrides = new AnimatorOverrideController(fx);
                try { Assert.That(ObjectToggleCapture.ParametersThatSwitchObjects(overrides), Is.Null); }
                finally { Object.DestroyImmediate(overrides); }
            }
            finally { AssetDatabase.DeleteAsset(directory); }
        }

        [Test]
        public void SyncedLayersAndOverridesPlayEveryParameterWhenAnyOfTheirClipsSwitchesObjects()
        {
            var directory = TempAssetFolder.Create("__VRVlogObjectToggleSynced_");
            try
            {
                var serial = 0;
                AnimationClip Clip(string property, Type type)
                {
                    var clip = new AnimationClip();
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(type == typeof(GameObject) ? "Hat" : "Body", type, property), AnimationCurve.Constant(0, 1f / 60, 1));
                    AssetDatabase.CreateAsset(clip, directory + "/Clip" + serial++ + ".anim");
                    return clip;
                }
                AnimationClip Smile() => Clip("blendShape.Smile", typeof(SkinnedMeshRenderer));
                AnimationClip Hat() => Clip("m_IsActive", typeof(GameObject));
                AnimatorController Controller(string name, out AnimatorState state)
                {
                    var controller = AnimatorController.CreateAnimatorControllerAtPath(directory + "/" + name + ".controller");
                    controller.AddParameter("Shape", AnimatorControllerParameterType.Bool);
                    state = controller.layers[0].stateMachine.AddState("Smile");
                    state.motion = Smile();
                    controller.layers[0].stateMachine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.If, 0, "Shape");
                    return controller;
                }
                void AddSynced(AnimatorController controller, AnimatorState state, Motion motion)
                {
                    var synced = new AnimatorControllerLayer { name = "Synced", defaultWeight = 1, syncedLayerIndex = 0, stateMachine = new AnimatorStateMachine { name = "Synced", hideFlags = HideFlags.HideInHierarchy } };
                    AssetDatabase.AddObjectToAsset(synced.stateMachine, controller);
                    synced.SetOverrideMotion(state, motion);
                    controller.AddLayer(synced);
                }

                var quiet = Controller("Quiet", out var quietState);
                AddSynced(quiet, quietState, Smile());
                Assert.That(ObjectToggleCapture.ParametersThatSwitchObjects(quiet), Is.Empty, "A synced layer whose clips set no m_IsActive is not played.");

                // The only m_IsActive is the synced layer's own motion for the state.
                var synced = Controller("Synced", out var syncedState);
                AddSynced(synced, syncedState, Hat());
                Assert.That(ObjectToggleCapture.ParametersThatSwitchObjects(synced), Is.Null);

                // The only m_IsActive is a clip the override puts in place of the smile.
                var source = Controller("Source", out var sourceState);
                var overrides = new AnimatorOverrideController(source);
                try
                {
                    Assert.That(ObjectToggleCapture.ParametersThatSwitchObjects(overrides), Is.Empty);
                    overrides[(AnimationClip)sourceState.motion] = Hat();
                    Assert.That(ObjectToggleCapture.ParametersThatSwitchObjects(overrides), Is.Null);
                }
                finally { Object.DestroyImmediate(overrides); }
            }
            finally { AssetDatabase.DeleteAsset(directory); }
        }

        const string HairPath = "Hips/Spine/Chest/Neck/Head/HairBone";

        static string Values(double[] values) => values == null ? "-" : string.Join(",", values.Select(value => value.ToString("0.###")));

        // The hat's clip shrinks the hair bone under it and lowers it a little;
        // the bone is not humanoid and has no mesh.
        [Test]
        public void AToggleWritesTheBonesItsClipsMoveAsTransforms()
        {
            using var avatar = new ToggleAvatar();
            var clone = Object.Instantiate(avatar.Source);
            try
            {
                var warnings = new List<string>();
                var capture = ObjectToggleCapture.Capture(clone, null, warnings);
                Assert.That(capture, Is.Not.Null, string.Join("\n", warnings));
                Assert.That(capture.MovedTargets.Select(bone => bone.name), Is.EqualTo(new[] { "HairBone" }));
                var nodes = clone.GetComponentsInChildren<Transform>(true).Select(transform => transform.gameObject).ToList();
                var data = capture.Resolve(nodes.IndexOf, warnings);
                // Samples after the hat's start from the rest pose again: a
                // bone left shrunk would read as switched by the next groups.
                Assert.That(warnings, Has.None.EndsWith("HairBone"));
                var transforms = capture.Transforms;
                Assert.That(transforms.Groups.Select(group => group.Parameter), Is.EqualTo(new[] { "Hat" }));
                var hat = transforms.GroupFor("Hat");
                Assert.That(hat.Targets.Select(target => nodes[target.Node].name), Is.EqualTo(new[] { "HairBone" }));
                Assert.That(hat.Targets[0].Properties, Is.EqualTo(new[] { ObjectToggleTransformData.Translation, ObjectToggleTransformData.Scale }));
                Assert.That(hat.States.Select(state => state.Value + ":" + Values(state.Transforms[0].Translation) + "/" + Values(state.Transforms[0].Rotation) + "/" + Values(state.Transforms[0].Scale)),
                    Is.EqualTo(new[] { "0:0,0.1,0/0,0,0,1/1,1,1", "1:0,0.08,0/0,0,0,1/0.01,0.01,0.01" }));
                // The data keeps to the contract with the toggles.
                var read = ObjectToggleTransformData.Read(ObjectToggleTransformData.Write(transforms, data), data);
                Assert.That(read.GroupFor("Hat").States.Select(state => Values(state.Transforms[0].Rotation)), Is.EqualTo(new[] { "-", "-" }));
                Assert.That(read.GroupFor("Hat").States.Select(state => Values(state.Transforms[0].Scale)), Is.EqualTo(new[] { "1,1,1", "0.01,0.01,0.01" }));

                // A bone the export left out only drops its transforms.
                warnings.Clear();
                var withoutBone = capture.Resolve(target => target.name == "HairBone" ? -1 : nodes.IndexOf(target), warnings);
                Assert.That(withoutBone.GroupFor("Hat"), Is.Not.Null);
                Assert.That(capture.Transforms, Is.Null);
                Assert.That(warnings.Single(), Does.EndWith(HairPath));

                // A bone a constraint moved under another parent keeps its pose:
                // its values are local to the parent it was sampled under.
                warnings.Clear();
                var hair = clone.transform.Find(HairPath);
                var head = hair.parent;
                hair.SetParent(clone.transform, true);
                Assert.That(capture.Resolve(nodes.IndexOf, warnings).GroupFor("Hat"), Is.Not.Null);
                Assert.That(capture.Transforms, Is.Null);
                Assert.That(warnings.Single(), Does.StartWith(ExporterLocalization.T(ObjectToggleCapture.ReparentedWarning)).And.EndWith(HairPath));
                hair.SetParent(head, true);
                warnings.Clear();

                // Leaving out empty objects keeps the bones the toggles move.
                var pruned = Object.Instantiate(clone);
                try
                {
                    var moved = capture.MovedTargets.Select(bone => pruned.transform.Find(HairPath).gameObject).ToArray();
                    ExportEmptyNodes.Prune(pruned, moved);
                    Assert.That(pruned.transform.Find(HairPath), Is.Not.Null);
                    ExportEmptyNodes.Prune(pruned);
                    Assert.That(pruned.transform.Find(HairPath), Is.Null);
                }
                finally { Object.DestroyImmediate(pruned); }
            }
            finally { Object.DestroyImmediate(clone); }
        }

        // Both extensions go into the file; transforms that cannot be written
        // are left out without losing the toggles.
        [Test]
        public void InjectPlacesTheTransformsNextToTheToggles()
        {
            var nodes = Enumerable.Range(0, 10).Select(i => (object)new Dictionary<string, object> { ["name"] = "n" + i }).ToList();
            var bytes = GlbDocument.Create(new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" }, ["nodes"] = nodes
            }, Array.Empty<byte>()).Write();
            var data = Data();
            ObjectToggleTransformData Transforms(int node)
            {
                var transforms = new ObjectToggleTransformData();
                var group = new ObjectToggleTransformData.Group { Parameter = "Hat" };
                group.Targets.Add(new ObjectToggleTransformData.Target { Node = node, Properties = { ObjectToggleTransformData.Scale } });
                foreach (var (value, scale) in new[] { (0, 1.0), (1, .01) })
                    group.States.Add(new ObjectToggleTransformData.State { Value = value, Transforms = { new ObjectToggleTransformData.Transform { Scale = new[] { scale, scale, scale } } } });
                transforms.Groups.Add(group);
                return transforms;
            }
            Dictionary<string, object> Extensions(byte[] written) => (Dictionary<string, object>)GlbDocument.Read(written).Json["extensions"];

            var both = Extensions(ObjectToggleCapture.Inject(bytes, data, Transforms(3)));
            Assert.That(ObjectToggleData.Read(both[ObjectToggleData.Extension]).Groups.Count, Is.EqualTo(2));
            Assert.That(ObjectToggleTransformData.Read(both[ObjectToggleTransformData.Extension], data).GroupFor("Hat").StateFor(1).Transforms[0].Scale,
                Is.EqualTo(new[] { .01, .01, .01 }));
            LogAssert.Expect(LogType.Warning, new Regex("left the bones the toggles move out"));
            var outOfRange = Extensions(ObjectToggleCapture.Inject(bytes, data, Transforms(10)));
            Assert.That(outOfRange.Keys, Is.EqualTo(new[] { ObjectToggleData.Extension }));
        }

        // Only bones a clip of the FX moves are compared: not the root, not
        // humanoid bones, not the root's children while the root is scaled,
        // and nothing for a path that matches no object.
        [Test]
        public void HumanoidBonesAndUnmovedNodesAreNotTransformTargets()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var directory = TempAssetFolder.Create("__VRVlogObjectToggleBones_");
            try
            {
                var head = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                foreach (var name in new[] { "HairBone", "Still" }) new GameObject(name).transform.SetParent(head, false);
                var fx = AnimatorController.CreateAnimatorControllerAtPath(directory + "/FX.controller");
                var clip = new AnimationClip();
                foreach (var (path, property) in new[]
                         {
                             ("", "m_LocalScale.x"), ("Hips/Spine/Chest/Neck/Head", "m_LocalScale.x"), (HairPath, "m_LocalPosition.y"),
                             ("Independent hair", "m_LocalPosition.y"), ("Missing/Bone", "m_LocalScale.x")
                         })
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), AnimationCurve.Constant(0, 1f / 60, 1));
                AssetDatabase.CreateAsset(clip, directory + "/Clip.anim");
                fx.AddLayer("Bones");
                fx.layers[0].stateMachine.AddState("Bones").motion = clip;
                string[] Moving()
                {
                    var transforms = fixture.Source.GetComponentsInChildren<Transform>(true);
                    return ObjectToggleCapture.MovingCandidates(fixture.Source, fx, transforms).Select(i => transforms[i].name).ToArray();
                }

                Assert.That(Moving(), Is.EquivalentTo(new[] { "Independent hair", "HairBone" }));
                fixture.Source.transform.localScale = Vector3.one * 2;
                Assert.That(Moving(), Is.EqualTo(new[] { "HairBone" }), "RootScaleBake rewrites the root's children.");
            }
            finally { AssetDatabase.DeleteAsset(directory); }
        }

        // A layer whose clips only move a bone a toggle may write is played,
        // like a layer that switches objects.
        [Test]
        public void ALayerThatOnlyMovesABoneIsPlayed()
        {
            var directory = TempAssetFolder.Create("__VRVlogObjectToggleBoneLayer_");
            try
            {
                var fx = AnimatorController.CreateAnimatorControllerAtPath(directory + "/FX.controller");
                var clip = new AnimationClip();
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Head/HairBone", typeof(Transform), "m_LocalScale.x"), AnimationCurve.Constant(0, 1f / 60, .01f));
                AssetDatabase.CreateAsset(clip, directory + "/Clip.anim");
                fx.AddParameter("Bone", AnimatorControllerParameterType.Bool);
                fx.AddLayer("Bone");
                var machine = fx.layers.Last().stateMachine;
                machine.AddState("Off");
                machine.AddAnyStateTransition(machine.AddState("On")).AddCondition(AnimatorConditionMode.If, 0, "Bone");
                machine.states.Single(state => state.state.name == "On").state.motion = clip;

                Assert.That(ObjectToggleCapture.ParametersThatSwitchObjects(fx), Is.Empty);
                Assert.That(ObjectToggleCapture.ParametersThatSwitchObjects(fx, path => path == "Head/HairBone"), Is.EqualTo(new[] { "Bone" }));
                Assert.That(ObjectToggleCapture.ParametersThatSwitchObjects(fx, path => false), Is.Empty);
            }
            finally { AssetDatabase.DeleteAsset(directory); }
        }

        [Test]
        public void ExportWritesTheTogglesAndTheMeshesThatAreOffByDefault()
        {
            var shader = OptionalTools.RequireShader("lilToon", "Install lilToon for the real exporter entry point.");
            using var avatar = new ToggleAvatar();
            var material = new Material(shader) { name = "Toggles" };
            try
            {
                foreach (var renderer in avatar.Source.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
                var warnings = new List<string>();
                var json = GlbDocument.Read(Export(avatar.Source, warnings, null)).Json;

                Assert.That((List<object>)json["extensionsUsed"], Has.Member(ObjectToggleData.Extension), string.Join("\n", warnings));
                var data = ObjectToggleData.Read(((Dictionary<string, object>)json["extensions"])[ObjectToggleData.Extension]);
                var nodes = ((List<object>)json["nodes"]).Cast<Dictionary<string, object>>().ToList();
                string Name(int node) => nodes[node].TryGetValue("name", out var name) ? (string)name : null;
                Assert.That(data.Groups.Select(group => group.Parameter + ":" + string.Join(",", group.Targets.Select(Name))),
                    Is.EqualTo(new[] { "Hat:Hat", "Outfit:Dress,Suit", "Glasses:Glasses", "Accessory:Ribbon" }));
                foreach (var name in new[] { "Hat", "Dress", "Suit", "Glasses", "Ribbon" })
                    Assert.That(nodes.Single(node => Name(nodes.IndexOf(node)) == name).ContainsKey("mesh"), Is.True, name + " must be written with its mesh.");
                var extras = (Dictionary<string, object>)((Dictionary<string, object>)json["asset"])["extras"];
                var required = (Dictionary<string, object>)extras[AvatarFormatVersions.RequiredAppVersionsKey];
                Assert.That(required[ObjectToggleData.Extension], Is.EqualTo("1.0.8"));
                // The hair bone the hat shrinks, in its own extension.
                Assert.That((List<object>)json["extensionsUsed"], Has.Member(ObjectToggleTransformData.Extension));
                var transforms = ObjectToggleTransformData.Read(((Dictionary<string, object>)json["extensions"])[ObjectToggleTransformData.Extension], data);
                Assert.That(transforms.Groups.Select(group => group.Parameter + ":" + string.Join(",", group.Targets.Select(target => Name(target.Node)))),
                    Is.EqualTo(new[] { "Hat:HairBone" }));
                Assert.That(transforms.GroupFor("Hat").StateFor(1).Transforms[0].Scale, Is.EqualTo(new[] { .01, .01, .01 }).Within(1e-6));
                Assert.That(required[ObjectToggleTransformData.Extension], Is.EqualTo("1.2.9"));
                foreach (var path in new[] { "Hat", "Clothes/Suit", "Glasses", "Ribbon" })
                    Assert.That(avatar.Source.transform.Find(path).gameObject.activeSelf, Is.False, path);
            }
            finally { Object.DestroyImmediate(material); }
        }

        // A VRM viewer that reads only the standard materials has no toggles:
        // the meshes that are off by default get an invisible copy of the
        // material they share with shown ones, and the lilToon extension still
        // carries the authored material for both, so the app shows and hides
        // them as before (Documentation~/decisions/2026-11-23-hide-default-off-outfits.md).
        [Test]
        public async Task ViewersOfTheStandardMaterialsDoNotShowTheMeshesThatAreOffByDefault()
        {
            var shader = OptionalTools.RequireShader("lilToon", "Install lilToon for the real exporter entry point.");
            using var avatar = new ToggleAvatar();
            var material = new Material(shader) { name = "Toggles" };
            Vrm10Instance imported = null;
            try
            {
                foreach (var renderer in avatar.Source.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
                var warnings = new List<string>();
                var bytes = Export(avatar.Source, warnings, null);
                var json = GlbDocument.Read(bytes).Json;

                var nodes = ((List<object>)json["nodes"]).Cast<Dictionary<string, object>>().ToList();
                var meshes = ((List<object>)json["meshes"]).Cast<Dictionary<string, object>>().ToList();
                var materials = ((List<object>)json["materials"]).Cast<Dictionary<string, object>>().ToList();
                int MaterialOf(string name)
                {
                    var node = nodes.Single(candidate => candidate.TryGetValue("name", out var value) && (string)value == name);
                    var primitive = (Dictionary<string, object>)((List<object>)meshes[Convert.ToInt32(node["mesh"])]["primitives"]).Single();
                    return Convert.ToInt32(primitive["material"]);
                }
                double Alpha(int index) => Convert.ToDouble(((List<object>)((Dictionary<string, object>)materials[index]["pbrMetallicRoughness"])["baseColorFactor"])[3]);
                string Mode(int index) => materials[index].TryGetValue("alphaMode", out var mode) ? (string)mode : "OPAQUE";
                var shown = MaterialOf("Dress");
                Assert.That(Mode(shown), Is.EqualTo("OPAQUE"));
                Assert.That(Alpha(shown), Is.EqualTo(1).Within(1e-6));
                Assert.That(materials[shown]["name"], Is.EqualTo("Toggles"));
                var hidden = MaterialOf("Hat");
                Assert.That(hidden, Is.Not.EqualTo(shown), "The shown meshes must keep their own material.");
                foreach (var name in new[] { "Hat", "Suit", "Glasses", "Ribbon" })
                    Assert.That(MaterialOf(name), Is.EqualTo(hidden), name);
                Assert.That(Mode(hidden), Is.EqualTo("BLEND"));
                Assert.That(Alpha(hidden), Is.EqualTo(0).Within(1e-6));
                Assert.That(materials[hidden]["name"], Is.EqualTo("Toggles" + UniVrmOneClickExporter.HiddenByDefaultSuffix));
                var mtoon = (Dictionary<string, object>)((Dictionary<string, object>)materials[hidden]["extensions"])["VRMC_materials_mtoon"];
                Assert.That(mtoon.TryGetValue("transparentWithZWrite", out var zWrite) && (bool)zWrite, Is.False);
                // The app draws both with the authored lilToon material.
                var lilToon = (Dictionary<string, object>)((Dictionary<string, object>)json["extensions"])[LilToonMobileProfile.ExtensionName];
                var records = ((List<object>)lilToon["materials"]).Cast<Dictionary<string, object>>().ToList();
                Assert.That(records.Select(record => Convert.ToInt32(record["materialIndex"])), Is.EquivalentTo(new[] { shown, hidden }));
                Assert.That(records.Select(record => (string)record["name"]), Is.EqualTo(new[] { "Toggles", "Toggles" }));
                Assert.That(material.GetColor("_Color").a, Is.EqualTo(1f), "The source material must not change.");

                // UniVRM reads only the standard materials, as other VRM viewers do.
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null);
                Material Loaded(string name) => imported.GetComponentsInChildren<Renderer>(true).Single(renderer => renderer.name == name).sharedMaterial;
                foreach (var name in new[] { "Hat", "Suit", "Glasses", "Ribbon" })
                {
                    Assert.That(Loaded(name).GetColor("_Color").a, Is.EqualTo(0f).Within(1e-6), name);
                    Assert.That(Loaded(name).renderQueue, Is.GreaterThanOrEqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent), name);
                }
                Assert.That(Loaded("Dress").GetColor("_Color").a, Is.EqualTo(1f).Within(1e-6));
                Assert.That(Loaded("Dress").renderQueue, Is.LessThan((int)UnityEngine.Rendering.RenderQueue.Transparent));
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(material);
            }
        }

        // The lilToon extension could not be written: the file is written again
        // without the snapshot and without hiding anything, and the result list
        // names that one omission, not the extension's too.
        [Test]
        public void AFailedLilToonExtensionIsListedOnceAndHidesNothing()
        {
            var shader = OptionalTools.RequireShader("lilToon", "Install lilToon for the real exporter entry point.");
            using var avatar = new ToggleAvatar();
            var material = new Material(shader) { name = "Toggles" };
            try
            {
                foreach (var renderer in avatar.Source.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
                var calls = 0;
                ExportTestHooks.FaultInjection = stage =>
                {
                    if (stage == ExportStage.LilToonExtension && ++calls == 1) throw new InvalidOperationException("injected extension");
                };
                var warnings = new List<string>();
                var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
                var json = GlbDocument.Read(Export(avatar.Source, warnings, report)).Json;

                Assert.That(calls, Is.EqualTo(1));
                Assert.That(report.Fallbacks.Results.Where(result => result.Stage != null).Select(result => result.Stage),
                    Is.EqualTo(new ExportStage?[] { ExportStage.LilToonSnapshot }));
                Assert.That(warnings.Count(warning => warning.StartsWith(ExporterLocalization.T(UniVrmOneClickExporter.StageOmittedWarning))), Is.EqualTo(1),
                    string.Join("\n", warnings));
                Assert.That(warnings.Single(warning => warning.StartsWith(ExporterLocalization.T(UniVrmOneClickExporter.StageOmittedWarning))),
                    Does.Contain("injected extension"));
                Assert.That(((Dictionary<string, object>)json["extensions"]).ContainsKey(LilToonMobileProfile.ExtensionName), Is.False);
                var materials = ((List<object>)json["materials"]).Cast<Dictionary<string, object>>();
                Assert.That(materials.Select(value => (string)value["name"]), Has.None.EndsWith(UniVrmOneClickExporter.HiddenByDefaultSuffix));
            }
            finally { Object.DestroyImmediate(material); }
        }

        // Writing the toggles failed after the meshes were turned on: the file
        // is written again without the stage, so meshes that are off by default
        // stay out instead of showing without a way to hide them.
        [Test]
        public void AFailedToggleStageLeavesTheDefaultOffMeshesOut()
        {
            var shader = OptionalTools.RequireShader("lilToon", "Install lilToon for the real exporter entry point.");
            using var avatar = new ToggleAvatar();
            var material = new Material(shader) { name = "Toggles" };
            try
            {
                foreach (var renderer in avatar.Source.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
                var calls = 0;
                ExportTestHooks.FaultInjection = stage =>
                {
                    // The second call is the mapping after the export.
                    if (stage == ExportStage.ObjectToggles && ++calls == 2) throw new InvalidOperationException("injected toggles");
                };
                var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
                var json = GlbDocument.Read(Export(avatar.Source, new List<string>(), report)).Json;

                Assert.That(calls, Is.EqualTo(2));
                Assert.That(report.Fallbacks.Results.Single(result => result.Stage != null).Stage, Is.EqualTo(ExportStage.ObjectToggles));
                Assert.That(json.TryGetValue("extensionsUsed", out var used) && ((List<object>)used).Contains(ObjectToggleData.Extension), Is.False);
                var nodes = ((List<object>)json["nodes"]).Cast<Dictionary<string, object>>().ToList();
                foreach (var name in new[] { "Hat", "Suit", "Glasses", "Ribbon" })
                    Assert.That(nodes.Any(node => node.TryGetValue("name", out var value) && (string)value == name && node.ContainsKey("mesh")), Is.False, name);
                Assert.That(nodes.Any(node => node.TryGetValue("name", out var value) && (string)value == "Dress" && node.ContainsKey("mesh")), Is.True);
            }
            finally { Object.DestroyImmediate(material); }
        }

        // AAO's Merge Skinned Mesh merges Hat (and Brim below it), a toggle
        // target that is off by default, into Merged after the toggles turned
        // Hat on. With copyEnablementAnimation AAO moves Hat's toggle to
        // Merged's enabled: the toggle's lost object is not warned about, and
        // Merged's enabled target rests off as Hat did, where VRChat's build
        // leaves it, while Merged keeps its mesh for the FX to show. Without it
        // the merge keeps what it did: Hat's toggle is lost with a warning and
        // nothing turns Merged off. AAO 1.8 copies no m_IsActive for Hat alone (see
        // ObjectToggleCapture.ReadCopiedEnablement); the warning then stays
        // exactly when Merged gets no enabled target.
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void AMergeThatCopiesEnablementHandsTheToggleToTheMergedRendererAtTheTargetsRest(bool copies, bool brim)
        {
            var shader = OptionalTools.RequireShader("lilToon", "Install lilToon for the real exporter entry point.");
            var mergeType = OptionalTools.Find("Anatawa12.AvatarOptimizer.MergeSkinnedMesh");
            var initialize = mergeType?.GetMethod("Initialize", new[] { typeof(int) });
            if (initialize == null || mergeType.GetProperty("SourceSkinnedMeshRenderers") == null)
                Assert.Ignore("Install AAO with the supported Merge Skinned Mesh configuration API.");
            using var avatar = new ToggleAvatar();
            var material = new Material(shader) { name = "Toggles" };
            var meshes = new List<Mesh>();
            try
            {
                foreach (var renderer in avatar.Source.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
                // Meshes skinned to the head leave nothing on their objects once
                // merged, so AAO removes the objects (RemoveEmptyRendererObject below).
                var hat = avatar.Source.transform.Find("Hat").gameObject;
                var head = avatar.Source.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head" && t.parent != null && t.parent.name == "Neck");
                var shape = hat.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(hat.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(hat.GetComponent<MeshFilter>());
                SkinnedMeshRenderer Skin(GameObject holder)
                {
                    var mesh = Object.Instantiate(shape);
                    meshes.Add(mesh);
                    mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, mesh.vertexCount).ToArray();
                    mesh.bindposes = new[] { head.worldToLocalMatrix * holder.transform.localToWorldMatrix };
                    var skin = holder.AddComponent<SkinnedMeshRenderer>();
                    skin.sharedMesh = mesh;
                    skin.bones = new[] { head };
                    skin.rootBone = head;
                    skin.sharedMaterial = material;
                    return skin;
                }
                var skins = new List<SkinnedMeshRenderer> { Skin(hat) };
                if (brim)
                {
                    var holder = new GameObject("Brim");
                    holder.transform.SetParent(hat.transform, false);
                    // First, so AAO removes Brim before it finds Hat empty.
                    skins.Insert(0, Skin(holder));
                }
                var merged = new GameObject("Merged");
                merged.transform.SetParent(avatar.Source.transform, false);
                merged.AddComponent<SkinnedMeshRenderer>();
                var merge = merged.AddComponent(mergeType);
                initialize.Invoke(merge, new object[] { 2 });
                var sources = mergeType.GetProperty("SourceSkinnedMeshRenderers").GetValue(merge);
                foreach (var skin in skins) sources.GetType().GetMethod("Add", new[] { typeof(SkinnedMeshRenderer) }).Invoke(sources, new object[] { skin });
                mergeType.GetProperty("RemoveEmptyRendererObject").SetValue(merge, true);
                using (var serialized = new SerializedObject(merge))
                {
                    serialized.FindProperty("copyEnablementAnimation").boolValue = copies;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                var warnings = new List<string>();
                var glb = GlbDocument.Read(Export(avatar.Source, warnings, null));
                var json = glb.Json;

                var nodes = ((List<object>)json["nodes"]).Cast<Dictionary<string, object>>().ToList();
                var mergedNode = nodes.FindIndex(node => node.TryGetValue("name", out var name) && (string)name == "Merged");
                Assert.That(mergedNode, Is.GreaterThanOrEqualTo(0), string.Join("\n", warnings));
                Assert.That(nodes[mergedNode].ContainsKey("mesh"), Is.True, "Merged must be written with its mesh.");
                Assert.That(nodes.Any(node => node.TryGetValue("name", out var name) && (string)name == "Hat"), Is.False, "The merge must have removed Hat.");
                var extensions = (Dictionary<string, object>)json["extensions"];
                var toggles = ObjectToggleData.Read(extensions[ObjectToggleData.Extension]);
                Assert.That(toggles.Groups.Select(group => group.Parameter), Has.No.Member("Hat"));
                var animator = AvatarAnimatorData.Read(AvatarAnimatorPacking.Document(glb));
                var enabled = animator.Targets.Where(target => target.Property == "enabled" && target.Node == mergedNode).ToList();
                var warned = warnings.Contains(ExporterLocalization.T(ObjectToggleCapture.MissingWarning) + "Hat");
                if (copies && !brim)
                {
                    Assert.That(warned, Is.EqualTo(enabled.Count == 0), string.Join("\n", warnings));
                    return;
                }
                Assert.That(warned, Is.EqualTo(!copies), string.Join("\n", warnings));
                if (!copies)
                {
                    Assert.That(enabled, Is.Empty);
                    return;
                }
                Assert.That(enabled, Has.Count.EqualTo(1), "Merged's enabled must be a target.");
                Assert.That(enabled[0].Component, Is.EqualTo("renderer"));
                Assert.That(enabled[0].Rest, Is.EqualTo(0), "Merged must rest off as Hat did before the toggles.");
                var target = animator.Targets.IndexOf(enabled[0]);
                var values = animator.Clips.SelectMany(clip => clip.Tracks).Where(track => track.Target == target)
                    .SelectMany(track => track.Curves).SelectMany(curve => curve.Keys).Select(key => key.Value).Distinct().OrderBy(value => value);
                Assert.That(values, Is.EqualTo(new[] { 0.0, 1.0 }), "The FX must turn Merged off and on as it did Hat.");
                Assert.That(avatar.Source.transform.Find("Hat").gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
            }
        }

        static byte[] Export(GameObject source, ICollection<string> warnings, ExportRecoveryReport report) =>
            UniVrmOneClickExporter.Export(source, "Object toggles", "Tests", warnings: warnings,
                exporterVersion: "0.11.8", lilToonVersion: "2.3.4",
                gimmickOptions: new ExportGimmickOptions { AutoExclude = false },
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }, recoveryReport: report);

        // A humanoid avatar with a VRChat descriptor, Expression Menu,
        // parameters and an FX controller whose layers switch meshes:
        //   Hat (bool, off by default), Clothes/Dress and Clothes/Suit (one int
        //   parameter, Dress by default), Nothing (moves a blend shape only),
        //   Glasses (bool) and Accessory (bool, switches Ribbon and Glasses).
        //   Hat on also shrinks and lowers HairBone under the head.
        sealed class ToggleAvatar : IDisposable
        {
            internal readonly AttachmentConnectionTests.Fixture Avatar;
            internal GameObject Source => Avatar.Source;
            readonly string directory;
            readonly List<Object> owned = new List<Object>();
            readonly Type menuType;
            readonly AnimatorController fx;
            int serial;

            internal ToggleAvatar()
            {
                var descriptorType = OptionalTools.Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                menuType = OptionalTools.Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");
                var parameterType = OptionalTools.Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters");
                if (descriptorType == null || menuType == null || parameterType == null)
                    Assert.Ignore("Install the real VRChat SDK to read Expression Menu toggles.");
                Avatar = new AttachmentConnectionTests.Fixture();
                try
                {
                    directory = TempAssetFolder.Create("__VRVlogObjectToggleTests_");
                    var mesh = new Mesh { name = "Toggle part" };
                    mesh.vertices = new[] { new Vector3(-.1f, 1.6f, .1f), new Vector3(.1f, 1.6f, .1f), new Vector3(0, 1.8f, .1f) };
                    mesh.triangles = new[] { 0, 1, 2 };
                    mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                    mesh.normals = Enumerable.Repeat(Vector3.forward, 3).ToArray();
                    owned.Add(mesh);
                    var material = Source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
                    void Part(string name, Transform parent, bool active)
                    {
                        var part = new GameObject(name);
                        part.transform.SetParent(parent, false);
                        part.AddComponent<MeshFilter>().sharedMesh = mesh;
                        part.AddComponent<MeshRenderer>().sharedMaterial = material;
                        part.SetActive(active);
                    }
                    Part("Hat", Source.transform, false);
                    var clothes = new GameObject("Clothes").transform;
                    clothes.SetParent(Source.transform, false);
                    Part("Dress", clothes, true);
                    Part("Suit", clothes, false);
                    Part("Glasses", Source.transform, false);
                    Part("Ribbon", Source.transform, false);
                    var hair = new GameObject("HairBone").transform;
                    hair.SetParent(Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head), false);
                    hair.localPosition = new Vector3(0, .1f, 0);

                    var root = Menu();
                    Control(root, "Hat", "Toggle", "Hat", 1);
                    var clothesMenu = Menu();
                    Control(clothesMenu, "Dress", "Toggle", "Outfit", 1);
                    Control(clothesMenu, "Suit", "Toggle", "Outfit", 2);
                    Control(root, "Clothes", "SubMenu", subMenu: clothesMenu);
                    Control(root, "Nothing", "Toggle", "Noop", 1);
                    Control(root, "Glasses", "Toggle", "Glasses", 1);
                    Control(root, "Accessory", "Toggle", "Accessory", 1);

                    var parameters = ScriptableObject.CreateInstance(parameterType);
                    owned.Add(parameters);
                    AssetDatabase.CreateAsset(parameters, directory + "/Parameters.asset");
                    var declared = new[] { ("Hat", "Bool", 0f), ("Outfit", "Int", 1f), ("Noop", "Bool", 0f), ("Glasses", "Bool", 0f), ("Accessory", "Bool", 0f) };
                    var parameterField = parameterType.GetField("parameters");
                    var rowType = parameterField.FieldType.GetElementType();
                    var rows = Array.CreateInstance(rowType, declared.Length);
                    for (var i = 0; i < declared.Length; i++)
                    {
                        var row = Activator.CreateInstance(rowType);
                        VrcAvatar.Set(row, "name", declared[i].Item1);
                        VrcAvatar.Set(row, "valueType", declared[i].Item2);
                        VrcAvatar.Set(row, "defaultValue", declared[i].Item3);
                        rows.SetValue(row, i);
                    }
                    parameterField.SetValue(parameters, rows);

                    fx = AnimatorController.CreateAnimatorControllerAtPath(directory + "/FX.controller");
                    owned.Add(fx);
                    var hatOn = Active(("Hat", true));
                    Pose(hatOn, HairPath, new Vector3(0, .08f, 0), Vector3.one * .01f);
                    Layer("Hat", AnimatorControllerParameterType.Bool, 0, (0, Active(("Hat", false))), (1, hatOn));
                    Layer("Outfit", AnimatorControllerParameterType.Int, 1,
                        (0, Active(("Clothes/Dress", false), ("Clothes/Suit", false))),
                        (1, Active(("Clothes/Dress", true), ("Clothes/Suit", false))),
                        (2, Active(("Clothes/Dress", false), ("Clothes/Suit", true))));
                    Layer("Noop", AnimatorControllerParameterType.Bool, 0, (0, Shape(35)), (1, Shape(80)));
                    Layer("Glasses", AnimatorControllerParameterType.Bool, 0, (0, Active(("Glasses", false))), (1, Active(("Glasses", true))));
                    // Off writes only Ribbon, so the Glasses layer above still decides Glasses.
                    Layer("Accessory", AnimatorControllerParameterType.Bool, 0,
                        (0, Active(("Ribbon", false))), (1, Active(("Ribbon", true), ("Glasses", true))));
                    var layers = fx.layers;
                    foreach (var layer in layers) layer.defaultWeight = 1;
                    fx.layers = layers;

                    var descriptor = Source.AddComponent(descriptorType);
                    VrcAvatar.Set(descriptor, "customExpressions", true);
                    VrcAvatar.Set(descriptor, "customizeAnimationLayers", true);
                    VrcAvatar.Set(descriptor, "expressionsMenu", root);
                    VrcAvatar.Set(descriptor, "expressionParameters", parameters);
                    Layers(descriptor, "baseAnimationLayers", new[] { "Base", "Additive", "Gesture", "Action", "FX" });
                    Layers(descriptor, "specialAnimationLayers", new[] { "Sitting", "TPose", "IKPose" });
                    foreach (var asset in owned.Where(EditorUtility.IsPersistent))
                    {
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            ScriptableObject Menu()
            {
                var menu = ScriptableObject.CreateInstance(menuType);
                owned.Add(menu);
                AssetDatabase.CreateAsset(menu, directory + "/Menu" + serial++ + ".asset");
                return menu;
            }

            static void Control(ScriptableObject menu, string name, string type, string parameterName = null, float value = 0, ScriptableObject subMenu = null)
            {
                var controls = (IList)VrChatExpressionMenu.Member(menu, "controls");
                var control = Activator.CreateInstance(controls.GetType().GetGenericArguments()[0]);
                controls.Add(control);
                VrcAvatar.Set(control, "name", name);
                VrcAvatar.Set(control, "type", type);
                VrcAvatar.Set(control, "value", value);
                VrcAvatar.Set(control, "subMenu", subMenu);
                if (parameterName != null)
                {
                    var parameter = Activator.CreateInstance(control.GetType().GetField("parameter").FieldType);
                    VrcAvatar.Set(parameter, "name", parameterName);
                    VrcAvatar.Set(control, "parameter", parameter);
                }
                EditorUtility.SetDirty(menu);
            }

            AnimationClip Active(params (string Path, bool Active)[] objects)
            {
                var clip = new AnimationClip { name = "Toggle " + serial };
                foreach (var (path, active) in objects)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"),
                        AnimationCurve.Constant(0, 1f / 60, active ? 1 : 0));
                return Save(clip);
            }

            static void Pose(AnimationClip clip, string path, Vector3 position, Vector3 scale)
            {
                foreach (var (property, value) in new[] { ("m_LocalPosition", position), ("m_LocalScale", scale) })
                    for (var axis = 0; axis < 3; axis++)
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property + "." + "xyz"[axis]),
                            AnimationCurve.Constant(0, 1f / 60, value[axis]));
            }

            AnimationClip Shape(float weight)
            {
                var clip = new AnimationClip { name = "Shape " + serial };
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                    AnimationCurve.Constant(0, 1f / 60, weight));
                return Save(clip);
            }

            AnimationClip Save(AnimationClip clip)
            {
                owned.Add(clip);
                AssetDatabase.CreateAsset(clip, directory + "/Clip" + serial++ + ".anim");
                return clip;
            }

            // One state per value, entered from Any State; write defaults off,
            // so a state writes only the objects its clip names.
            void Layer(string parameter, AnimatorControllerParameterType type, int defaultValue, params (int Value, AnimationClip Clip)[] states)
            {
                fx.AddParameter(new AnimatorControllerParameter
                {
                    name = parameter, type = type, defaultBool = defaultValue != 0, defaultInt = defaultValue, defaultFloat = defaultValue
                });
                fx.AddLayer(parameter);
                var machine = fx.layers.Last().stateMachine;
                foreach (var (value, clip) in states)
                {
                    var state = machine.AddState(parameter + " " + value);
                    state.motion = clip;
                    state.writeDefaultValues = false;
                    if (value == defaultValue) machine.defaultState = state;
                    var transition = machine.AddAnyStateTransition(state);
                    transition.canTransitionToSelf = false;
                    transition.hasExitTime = false;
                    transition.duration = 0;
                    if (type == AnimatorControllerParameterType.Bool)
                        transition.AddCondition(value != 0 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, parameter);
                    else transition.AddCondition(AnimatorConditionMode.Equals, value, parameter);
                }
            }

            void Layers(Component descriptor, string fieldName, string[] types)
            {
                var field = descriptor.GetType().GetField(fieldName);
                var rowType = field.FieldType.GetElementType();
                var rows = Array.CreateInstance(rowType, types.Length);
                for (var i = 0; i < types.Length; i++)
                {
                    var row = Activator.CreateInstance(rowType);
                    VrcAvatar.Set(row, "type", types[i]);
                    VrcAvatar.Set(row, "isDefault", false);
                    VrcAvatar.Set(row, "animatorController", fx);
                    rows.SetValue(row, i);
                }
                field.SetValue(descriptor, rows);
            }

            public void Dispose()
            {
                Avatar?.Dispose();
                if (!string.IsNullOrEmpty(directory)) AssetDatabase.DeleteAsset(directory);
                foreach (var value in owned.Where(value => value != null && !EditorUtility.IsPersistent(value)).Reverse()) Object.DestroyImmediate(value);
                owned.Clear();
            }
        }
    }
}
