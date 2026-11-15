using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRVlog.AvatarPhysBones;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // VRVLOG_avatar_physbones: the shared reader's contract without the SDK,
    // and Capture and Resolve on real PhysBones (skipped without the SDK).
    public sealed class AvatarPhysBonesExportTests
    {
        static AvatarPhysBonesData.PhysBone Entry() => new AvatarPhysBonesData.PhysBone
            { Node = 3, Parameter = "Tail", MaxStretch = .5, MaxSquish = .25, AllowGrabbing = true, AllowPosing = false };

        [Test]
        public void SharedReaderReadsBackWhatItWrites()
        {
            var data = new AvatarPhysBonesData();
            data.PhysBones.Add(Entry());
            data.PhysBones.Add(new AvatarPhysBonesData.PhysBone { Node = 0 });
            var json = AvatarPhysBonesData.Write(data);
            Assert.That(json["schemaVersion"], Is.EqualTo(1L));

            var read = AvatarPhysBonesData.Read(json);
            var first = read.PhysBones[0];
            Assert.That((first.Node, first.Parameter, first.MaxStretch, first.MaxSquish, first.AllowGrabbing, first.AllowPosing, first.LimitAngle),
                Is.EqualTo((3, "Tail", .5, .25, true, false, 180d)));
            Assert.That(read.PhysBones[1].Parameter, Is.EqualTo(""), "A PhysBone without a parameter is still an entry.");
        }

        [Test]
        public void SharedReaderRejectsWhatTheContractDoesNotAllow()
        {
            Dictionary<string, object> Written(Action<Dictionary<string, object>> change)
            {
                var data = new AvatarPhysBonesData();
                data.PhysBones.Add(Entry());
                var json = AvatarPhysBonesData.Write(data);
                change((Dictionary<string, object>)((List<object>)json["physBones"])[0]);
                return json;
            }
            Assert.That(() => AvatarPhysBonesData.Read(Written(e => e["limitAngle"] = 0d)), Throws.InvalidOperationException, "_Angle divides by it.");
            Assert.That(() => AvatarPhysBonesData.Read(Written(e => e["limitAngle"] = 200d)), Throws.InvalidOperationException);
            Assert.That(() => AvatarPhysBonesData.Read(Written(e => e["maxStretch"] = -1d)), Throws.InvalidOperationException);
            Assert.That(() => AvatarPhysBonesData.Read(Written(e => e["parameter"] = 1L)), Throws.InvalidOperationException);
            Assert.That(() => AvatarPhysBonesData.Read(Written(e => e["parameter"] = new string('a', AvatarPhysBonesData.MaximumParameterLength + 1))), Throws.InvalidOperationException);
            Assert.That(() => AvatarPhysBonesData.Read(Written(e => e["immobile"] = 0d)), Throws.InvalidOperationException, "Unknown keys are not read.");
            Assert.That(() => AvatarPhysBonesData.Read(Written(e => e.Remove("allowPosing"))), Throws.InvalidOperationException);
            var newer = Written(_ => { });
            newer["schemaVersion"] = 2L;
            Assert.That(() => AvatarPhysBonesData.Read(newer), Throws.InvalidOperationException);
        }

        [Test]
        public void ParameterNamesAreThePrefixWithEachSuffix()
        {
            Assert.That(AvatarPhysBonesData.ParameterNames("Tail"), Is.EqualTo(new[] { "Tail_IsGrabbed", "Tail_IsPosed", "Tail_Angle", "Tail_Stretch", "Tail_Squish" }));
            Assert.That(AvatarPhysBonesData.ParameterNames(""), Is.Empty);
            Assert.That(AvatarPhysBonesData.ParameterNames(null), Is.Empty);
        }

        // Capture reads every PhysBone, enabled or not, in hierarchy order,
        // but not those under EditorOnly; Resolve keeps those whose root has a
        // node, under the root's node, and the builtin names are theirs.
        [Test]
        public void CaptureReadsTheSettingsAndResolveBindsTheRoots()
        {
            var type = OptionalTools.Require(PhysBoneSpringExport.PhysBoneType, "Real VRChat SDK integration test.");
            var root = new GameObject("Avatar");
            try
            {
                Transform Child(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = Vector3.down * .1f; return t; }
                var tail = Child(root.transform, "Tail"); Child(tail, "Tail tip");
                var holder = Child(root.transform, "Holder");
                var ear = Child(root.transform, "Ear"); Child(ear, "Ear tip");
                var skirt = Child(root.transform, "Skirt"); Child(skirt, "Skirt tip");
                var editorOnly = Child(root.transform, "Editor only"); Child(editorOnly, "Editor only tip");
                editorOnly.tag = "EditorOnly";

                var tailBone = tail.gameObject.AddComponent(type);
                Set(tailBone, "parameter", "Tail");
                Set(tailBone, "maxStretch", .5f);
                SetChoice(tailBone, "allowGrabbing", "True");
                SetChoice(tailBone, "allowPosing", "False");
                // On another object, with its Root Transform on Ear.
                var earBone = holder.gameObject.AddComponent(type);
                Set(earBone, "rootTransform", ear);
                Set(earBone, "parameter", "Ear");
                SetChoice(earBone, "allowGrabbing", "Other");
                ((Behaviour)earBone).enabled = false;
                // Its root is not written.
                var skirtBone = skirt.gameObject.AddComponent(type);
                Set(skirtBone, "parameter", "Skirt");
                // VRChat removes it at upload.
                Set(editorOnly.gameObject.AddComponent(type), "parameter", "EditorOnly");

                var capture = AvatarPhysBonesExport.Capture(root);
                Assert.That(capture.Count, Is.EqualTo(3));
                var nodes = new Dictionary<GameObject, int> { [tail.gameObject] = 4, [ear.gameObject] = 7 };
                var data = AvatarPhysBonesData.Read(AvatarPhysBonesData.Write(capture.Resolve(go => nodes.TryGetValue(go, out var node) ? node : -1)));
                Assert.That(data.PhysBones.Select(p => (p.Node, p.Parameter, p.MaxStretch, p.AllowGrabbing, p.AllowPosing)),
                    Is.EqualTo(new[] { (4, "Tail", (double).5f, true, false), (7, "Ear", 0d, true, ReadChoice(earBone, "allowPosing") != "False") }));
                // The disabled Ear is builtin; Skirt, whose root has no node, is not.
                Assert.That(AvatarPhysBonesExport.ParameterNames(data), Is.EquivalentTo(
                    AvatarPhysBonesData.ParameterNames("Tail").Concat(AvatarPhysBonesData.ParameterNames("Ear"))));
                Assert.That(AvatarPhysBonesExport.ParameterNames(null), Is.Empty);
                Assert.That(capture.Resolve(_ => -1), Is.Null, "No root written, no table.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CaptureOfACopyWithoutPhysBonesIsNull()
        {
            var root = new GameObject("Avatar");
            try { Assert.That(AvatarPhysBonesExport.Capture(root), Is.Null); }
            finally { Object.DestroyImmediate(root); }
        }

        internal static void Set(Component component, string name, object value)
        {
            using var data = new SerializedObject(component);
            var p = data.FindProperty(name);
            Assert.That(p, Is.Not.Null, name);
            if (value is float f) p.floatValue = f;
            else if (value is string s) p.stringValue = s;
            else if (value is Object reference) p.objectReferenceValue = reference;
            else throw new ArgumentException(name);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        // An AdvancedBool by its name, or a bool on SDKs before it.
        static void SetChoice(Component component, string name, string choice)
        {
            using var data = new SerializedObject(component);
            var p = data.FindProperty(name);
            Assert.That(p, Is.Not.Null, name);
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = choice != "False";
            else
            {
                var index = Array.IndexOf(p.enumNames, choice);
                if (index < 0) Assert.Ignore(name + " has no " + choice + " in this SDK.");
                p.enumValueIndex = index;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        static string ReadChoice(Component component, string name)
        {
            using var data = new SerializedObject(component);
            var p = data.FindProperty(name);
            return p.propertyType == SerializedPropertyType.Boolean ? (p.boolValue ? "True" : "False") : p.enumNames[p.enumValueIndex];
        }
    }
}
