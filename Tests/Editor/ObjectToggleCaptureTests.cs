using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.AvatarFormats;
using VRVlog.ObjectToggles;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // VRVLOG_object_toggles: the data contract, the capture from the menu and
    // FX of a VRChat avatar, and the extension in a real export. The capture
    // tests use the real SDK types through reflection and skip without them.
    public sealed class ObjectToggleCaptureTests
    {
        [TearDown]
        public void ResetFaultInjection() => ExportStageGuard.FaultInjection = null;

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

        // Only the parameters a layer that can switch objects reads are
        // played; any other leaves every object as its default does.
        [Test]
        public void OnlyParametersOfLayersThatSwitchObjectsArePlayed()
        {
            var folder = "__VRVlogObjectToggleLayers_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder);
            var directory = "Assets/" + folder;
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
            var folder = "__VRVlogObjectToggleSynced_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder);
            var directory = "Assets/" + folder;
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

        [Test]
        public void ExportWritesTheTogglesAndTheMeshesThatAreOffByDefault()
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Install lilToon for the real exporter entry point.");
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
                Assert.That(((Dictionary<string, object>)extras[AvatarFormatVersions.RequiredAppVersionsKey])[ObjectToggleData.Extension], Is.EqualTo("1.0.8"));
                foreach (var path in new[] { "Hat", "Clothes/Suit", "Glasses", "Ribbon" })
                    Assert.That(avatar.Source.transform.Find(path).gameObject.activeSelf, Is.False, path);
            }
            finally { Object.DestroyImmediate(material); }
        }

        // Writing the toggles failed after the meshes were turned on: the file
        // is written again without the stage, so meshes that are off by default
        // stay out instead of showing without a way to hide them.
        [Test]
        public void AFailedToggleStageLeavesTheDefaultOffMeshesOut()
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Install lilToon for the real exporter entry point.");
            using var avatar = new ToggleAvatar();
            var material = new Material(shader) { name = "Toggles" };
            try
            {
                foreach (var renderer in avatar.Source.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
                var calls = 0;
                ExportStageGuard.FaultInjection = stage =>
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

        static byte[] Export(GameObject source, ICollection<string> warnings, ExportRecoveryReport report) =>
            UniVrmOneClickExporter.Export(source, "Object toggles", "Tests", warnings,
                exporterVersion: "0.11.8", lilToonVersion: "2.3.4",
                gimmickOptions: new ExportGimmickOptions { AutoExclude = false },
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }, recoveryReport: report);

        // A humanoid avatar with a VRChat descriptor, Expression Menu,
        // parameters and an FX controller whose layers switch meshes:
        //   Hat (bool, off by default), Clothes/Dress and Clothes/Suit (one int
        //   parameter, Dress by default), Nothing (moves a blend shape only),
        //   Glasses (bool) and Accessory (bool, switches Ribbon and Glasses).
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
                var descriptorType = Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                menuType = Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");
                var parameterType = Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters");
                if (descriptorType == null || menuType == null || parameterType == null)
                    Assert.Ignore("Install the real VRChat SDK to read Expression Menu toggles.");
                Avatar = new AttachmentConnectionTests.Fixture();
                try
                {
                    var folder = "__VRVlogObjectToggleTests_" + Guid.NewGuid().ToString("N");
                    AssetDatabase.CreateFolder("Assets", folder);
                    directory = "Assets/" + folder;
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
                        Set(row, "name", declared[i].Item1);
                        Set(row, "valueType", declared[i].Item2);
                        Set(row, "defaultValue", declared[i].Item3);
                        rows.SetValue(row, i);
                    }
                    parameterField.SetValue(parameters, rows);

                    fx = AnimatorController.CreateAnimatorControllerAtPath(directory + "/FX.controller");
                    owned.Add(fx);
                    Layer("Hat", AnimatorControllerParameterType.Bool, 0, (0, Active(("Hat", false))), (1, Active(("Hat", true))));
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
                    Set(descriptor, "customExpressions", true);
                    Set(descriptor, "customizeAnimationLayers", true);
                    Set(descriptor, "expressionsMenu", root);
                    Set(descriptor, "expressionParameters", parameters);
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

            static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            static void Set(object target, string name, object value) => MenuImportSdkRecoveryTests.SdkFixture.Set(target, name, value);

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
                Set(control, "name", name);
                Set(control, "type", type);
                Set(control, "value", value);
                Set(control, "subMenu", subMenu);
                if (parameterName != null)
                {
                    var parameter = Activator.CreateInstance(control.GetType().GetField("parameter").FieldType);
                    Set(parameter, "name", parameterName);
                    Set(control, "parameter", parameter);
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
                    Set(row, "type", types[i]);
                    Set(row, "isDefault", false);
                    Set(row, "animatorController", fx);
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
