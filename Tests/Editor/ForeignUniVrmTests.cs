using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using VRVlog.LilToonExporter.Vendor.VRM10.MToon10;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // An avatar set up with the project's own UniVRM (any version) exports with
    // its settings, through the exporter's bundled copy. The project UniVRM
    // types are reached by name only; without one the cases that need it are ignored.
    public sealed class ForeignUniVrmTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        static Type Project(string fullName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.GetName().Name.StartsWith("VRVlog.", StringComparison.Ordinal))
                .Select(assembly => assembly.GetType(fullName, false)).FirstOrDefault(value => value != null);
            if (type == null) Assert.Ignore("The project has no UniVRM of its own.");
            return type;
        }

        ScriptableObject ProjectBlink(bool binary, float weight)
        {
            var clip = ScriptableObject.CreateInstance(Project("UniVRM10.VRM10Expression")); owned.Add(clip);
            using (var serialized = new SerializedObject(clip))
            {
                serialized.FindProperty("IsBinary").boolValue = binary;
                var bindings = serialized.FindProperty("MorphTargetBindings");
                bindings.arraySize = 1;
                var binding = bindings.GetArrayElementAtIndex(0);
                binding.FindPropertyRelative("RelativePath").stringValue = "Front";
                binding.FindPropertyRelative("Index").intValue = 0;
                binding.FindPropertyRelative("Weight").floatValue = weight;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return clip;
        }

        ScriptableObject ProjectSettings(ScriptableObject blink)
        {
            var settings = ScriptableObject.CreateInstance(Project("UniVRM10.VRM10Object")); owned.Add(settings);
            using (var serialized = new SerializedObject(settings))
            {
                serialized.FindProperty("Expression.Blink").objectReferenceValue = blink;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return settings;
        }

        static void Attach(GameObject avatar, ScriptableObject settings)
        {
            var instance = avatar.AddComponent(Project("UniVRM10.Vrm10Instance"));
            using (var serialized = new SerializedObject(instance))
            {
                serialized.FindProperty("Vrm").objectReferenceValue = settings;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test] public void BundledUniVrmIsNotTheProjectsAndItsShadersHaveTheirOwnNames()
        {
            Assert.That(ForeignUniVrm.IsForeign(typeof(Vrm10Instance)), Is.False);
            Assert.That(typeof(Vrm10Instance).Assembly.GetName().Name, Does.StartWith("VRVlog.LilToonExporter.Vendor."));
            foreach (var pair in ForeignUniVrm.Shaders)
            {
                Assert.That(pair.Value, Is.Not.EqualTo(pair.Key));
                Assert.That(Shader.Find(pair.Value), Is.Not.Null, pair.Value);
            }
        }

        [Test] public void ProjectUniVrmSettingsOnTheSourceAreExportedAndLeftUnchanged()
        {
            // As with the bundled Vrm10Instance, the authored amount is baked
            // into a generated target that the clip drives fully. The target
            // starts from the fixture's posed 35%, so 60% exports (60-35)/(100-35)
            // of what the same avatar exports at 100%.
            var partial = ExportedBlinkHeight(.6f);
            var full = ExportedBlinkHeight(1f);
            Assert.That(full, Is.GreaterThan(1e-5f));
            Assert.That(partial / full, Is.EqualTo(25f / 65f).Within(1e-3f));
        }

        float ExportedBlinkHeight(float weight)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon")); owned.Add(material);
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
            var blink = ProjectBlink(true, weight);
            var settings = ProjectSettings(blink);
            Attach(fixture.Source, settings);
            var glb = GlbDocument.Read(UniVrmOneClickExporter.Export(fixture.Source, "Project UniVRM", "Tests"));
            var extension = (Dictionary<string, object>)((Dictionary<string, object>)glb.Json["extensions"])["VRMC_vrm"];
            var presets = (Dictionary<string, object>)((Dictionary<string, object>)extension["expressions"])["preset"];
            var exported = (Dictionary<string, object>)presets["blink"];
            Assert.That(exported["isBinary"], Is.EqualTo(true));
            var binds = (List<object>)exported["morphTargetBinds"];
            Assert.That(binds.Count, Is.EqualTo(1));
            var bind = (Dictionary<string, object>)binds[0];
            Assert.That(Convert.ToSingle(bind["weight"]), Is.EqualTo(1f).Within(1e-5f));
            var node = (Dictionary<string, object>)((List<object>)glb.Json["nodes"])[Convert.ToInt32(bind["node"])];
            var mesh = (Dictionary<string, object>)((List<object>)glb.Json["meshes"])[Convert.ToInt32(node["mesh"])];
            var target = Convert.ToInt32(bind["index"]);
            Assert.That((string)((List<object>)((Dictionary<string, object>)mesh["extras"])["targetNames"])[target], Does.StartWith("__VRVlog_Blink_"));
            var primitive = (Dictionary<string, object>)((List<object>)mesh["primitives"])[0];
            var positions = Convert.ToInt32(((Dictionary<string, object>)((List<object>)primitive["targets"])[target])["POSITION"]);
            var accessor = (Dictionary<string, object>)((List<object>)glb.Json["accessors"])[positions];

            // The source keeps the project UniVRM component and assets as they were.
            Assert.That(fixture.Source.GetComponent<Vrm10Instance>(), Is.Null);
            Assert.That(fixture.Source.GetComponents<MonoBehaviour>().Count(component => component.GetType().FullName == "UniVRM10.Vrm10Instance"), Is.EqualTo(1));
            using (var serialized = new SerializedObject(blink))
                Assert.That(serialized.FindProperty("MorphTargetBindings").GetArrayElementAtIndex(0).FindPropertyRelative("Weight").floatValue, Is.EqualTo(weight));
            return Math.Abs(Convert.ToSingle(((List<object>)accessor["max"])[1])) + Math.Abs(Convert.ToSingle(((List<object>)accessor["min"])[1]));
        }

        [Test] public void AuthoredSettingsAreConvertedOnceAndFollowEditsOfTheProjectAsset()
        {
            var avatar = new GameObject("Avatar"); owned.Add(avatar);
            var blink = ProjectBlink(false, .25f);
            Attach(avatar, ProjectSettings(blink));
            var first = ForeignUniVrm.AuthoredSettings(avatar);
            Assert.That(first, Is.Not.Null);
            Assert.That(first.Expression.Blink.MorphTargetBindings.Single().Weight, Is.EqualTo(.25f));
            Assert.That(first.Expression.Blink.IsBinary, Is.False);
            Assert.That(ForeignUniVrm.AuthoredSettings(avatar), Is.SameAs(first));

            // An Inspector edit goes through ApplyModifiedProperties.
            using (var serialized = new SerializedObject(blink))
            {
                serialized.FindProperty("MorphTargetBindings").GetArrayElementAtIndex(0).FindPropertyRelative("Weight").floatValue = .75f;
                serialized.ApplyModifiedProperties();
            }
            var second = ForeignUniVrm.AuthoredSettings(avatar);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Expression.Blink.MorphTargetBindings.Single().Weight, Is.EqualTo(.75f));
            Assert.That(first == null, Is.True, "The superseded conversion is released.");
        }

        [Test] public void AdoptGivesTheCopyBundledComponentsAndMaterialsOnly()
        {
            var shader = Shader.Find("VRM10/MToon10");
            if (shader == null) Assert.Ignore("The project has no UniVRM MToon10.");
            var source = new GameObject("Avatar"); owned.Add(source);
            Attach(source, ProjectSettings(ProjectBlink(true, 1f)));
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.transform.SetParent(source.transform);
            var material = new Material(shader) { renderQueue = 2450 }; owned.Add(material);
            material.SetColor("_Color", new Color(.2f, .4f, .6f, 1f));
            body.GetComponent<Renderer>().sharedMaterial = material;

            var copy = Object.Instantiate(source); owned.Add(copy);
            ForeignUniVrm.Adopt(copy);
            Assert.That(copy.GetComponents<MonoBehaviour>().Where(component => component != null).Select(component => component.GetType())
                .Where(ForeignUniVrm.IsForeign), Is.Empty);
            var instance = copy.GetComponent<Vrm10Instance>();
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.Vrm.Expression.Blink.IsBinary, Is.True);
            var adopted = copy.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.That(adopted.shader.name, Is.EqualTo(MToon10Meta.UnityShaderName));
            Assert.That(adopted.renderQueue, Is.EqualTo(2450));
            var color = adopted.GetColor("_Color");
            Assert.That(new[] { color.r, color.g, color.b, color.a }, Is.EqualTo(new[] { .2f, .4f, .6f, 1f }).Within(1e-4f));

            // The source and its material are not touched.
            Assert.That(source.GetComponent<Vrm10Instance>(), Is.Null);
            Assert.That(source.GetComponentInChildren<Renderer>().sharedMaterial, Is.SameAs(material));
            Assert.That(material.shader, Is.SameAs(shader));

            // What was made for the copy goes with it.
            Object.DestroyImmediate(copy);
            var next = new GameObject("Next"); owned.Add(next);
            ForeignUniVrm.Adopt(next);
            Assert.That(adopted == null, Is.True);
        }
    }
}
