using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using VRVlog.AvatarConstraints;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using ConstraintSource = UnityEngine.Animations.ConstraintSource;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class AvatarConstraintsExportTests
    {
        // Two siblings under one parent and an unrelated branch, as in
        // ConstraintExportTests. Every object is a node unless nodes leaves
        // it out.
        sealed class Fixture : IDisposable
        {
            internal readonly GameObject Source = new GameObject("Source avatar");
            internal readonly GameObject Copy = new GameObject("Copy");
            internal readonly Transform Parent, A, B, Other;
            internal readonly AvatarConstraintsExport Extension = new AvatarConstraintsExport();
            internal readonly Dictionary<GameObject, int> Nodes = new Dictionary<GameObject, int>();
            internal readonly HashSet<GameObject> Unwritten = new HashSet<GameObject>();

            internal Fixture()
            {
                Parent = Child(Copy.transform, "Parent", Vector3.up);
                A = Child(Parent, "A", new Vector3(.1f, 0, 0));
                B = Child(Parent, "B", new Vector3(-.1f, 0, 0));
                Other = Child(Child(Copy.transform, "Other parent", Vector3.down), "Other", Vector3.zero);
                foreach (var transform in Copy.GetComponentsInChildren<Transform>()) Nodes[transform.gameObject] = Nodes.Count;
            }

            static Transform Child(Transform parent, string name, Vector3 local)
            {
                var value = new GameObject(name).transform; value.SetParent(parent, false); value.localPosition = local; return value;
            }

            internal T Constraint<T>(Transform target, float weight = 1, params Transform[] sources) where T : Behaviour, IConstraint
            {
                var constraint = target.gameObject.AddComponent<T>();
                foreach (var source in sources) constraint.AddSource(new ConstraintSource { sourceTransform = source, weight = 1 });
                constraint.weight = weight; constraint.locked = true; constraint.constraintActive = true;
                return constraint;
            }

            internal void Convert() => ConstraintExport.Convert(Source, Copy, new List<string>(), new ExportFallbackReport(), extension: Extension);

            internal int Node(GameObject gameObject) => !Unwritten.Contains(gameObject) && Nodes.TryGetValue(gameObject, out var node) ? node : -1;

            // Resolved and read back through the shared contract.
            internal AvatarConstraintsData Resolve()
            {
                var data = Extension.Resolve(Node);
                return data == null ? null : AvatarConstraintsData.Read(AvatarConstraintsData.Write(data));
            }

            public void Dispose() { Object.DestroyImmediate(Copy); Object.DestroyImmediate(Source); }
        }

        // A constraint turned into a VRM constraint stays one; only one left
        // in its pose goes to the extension.
        [Test]
        public void OnlyConstraintsLeftInTheirPoseAreAdded()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.A);
            f.A.localRotation = f.B.localRotation = Quaternion.Euler(0, 0, 20);
            var scale = f.Constraint<ScaleConstraint>(f.Parent, 1, f.Other);
            f.Convert();
            Assert.That(f.B.GetComponent<Vrm10RotationConstraint>(), Is.Not.Null);
            Assert.That(f.Extension.Components, Is.EqualTo(new Component[] { scale }));
            var data = f.Resolve();
            Assert.That(data.Constraints.Single().Type, Is.EqualTo("scale"));
            Assert.That(f.Extension.IndexOf(scale), Is.EqualTo(0));
            Assert.That(f.Extension.IndexOf(f.B.GetComponent<RotationConstraint>()), Is.EqualTo(-1));
        }

        // Scale and several weighted sources: the settings as authored.
        [Test]
        public void AUnityScaleConstraintIsWrittenAsAuthored()
        {
            using var f = new Fixture();
            var scale = f.Constraint<ScaleConstraint>(f.Parent, .75f, f.A, f.Other);
            scale.SetSource(1, new ConstraintSource { sourceTransform = f.Other, weight = .25f });
            scale.scalingAxis = Axis.X | Axis.Z;
            scale.scaleAtRest = new Vector3(1, 2, 3);
            scale.scaleOffset = new Vector3(.5f, 1, 1);
            scale.enabled = false;
            f.Convert();

            var c = f.Resolve().Constraints.Single();
            Assert.That((c.Node, c.Type, c.Origin, c.Enabled, c.Active, c.Locked, c.Weight, c.LocalSpace, c.Freeze),
                Is.EqualTo((f.Nodes[f.Parent.gameObject], "scale", "unity", false, true, true, .75, false, false)));
            Assert.That(c.ScaleAxes, Is.EqualTo(new[] { true, false, true }));
            Assert.That(c.ScaleAtRest, Is.EqualTo(new double[] { 1, 2, 3 }));
            Assert.That(c.ScaleOffset, Is.EqualTo(new double[] { .5, 1, 1 }));
            Assert.That(c.PositionAxes, Is.Null);
            Assert.That(c.Sources.Select(s => (s.Node, s.Weight)), Is.EqualTo(new[] { (f.Nodes[f.A.gameObject], 1d), (f.Nodes[f.Other.gameObject], .25) }));
            Assert.That(c.Sources.All(s => s.PositionOffset == null && s.RotationOffset == null));
        }

        // A parent constraint carries its offsets on each source.
        [Test]
        public void AParentConstraintWritesTheOffsetOfEachSource()
        {
            using var f = new Fixture();
            var parent = f.Constraint<ParentConstraint>(f.B, .5f, f.Other, f.A);
            parent.SetTranslationOffset(0, new Vector3(0, .25f, 0));
            parent.SetRotationOffset(1, new Vector3(0, 90, 0));
            parent.translationAxis = Axis.Y;
            f.Convert();
            Assume.That(f.Extension.Components, Is.EqualTo(new Component[] { parent }), "A parent constraint at half weight keeps its pose.");

            var c = f.Resolve().Constraints.Single();
            Assert.That(c.Type, Is.EqualTo("parent"));
            Assert.That(c.PositionAxes, Is.EqualTo(new[] { false, true, false }));
            Assert.That(c.RotationAxes, Is.EqualTo(new[] { true, true, true }));
            Assert.That(c.Sources[0].PositionOffset, Is.EqualTo(new double[] { 0, .25, 0 }));
            Assert.That(c.Sources[1].RotationOffset, Is.EqualTo(new double[] { 0, 90, 0 }));
        }

        // The file has no node for a source: the constraint keeps its pose,
        // as before the extension, and no enabled curve binds to it.
        [Test]
        public void AConstraintWithASourceTheFileDoesNotWriteIsLeftOut()
        {
            using var f = new Fixture();
            var scale = f.Constraint<ScaleConstraint>(f.Parent, 1, f.Other);
            f.Convert();
            f.Unwritten.Add(f.Other.gameObject);
            Assert.That(f.Resolve(), Is.Null);
            Assert.That(f.Extension.IndexOf(scale), Is.EqualTo(-1));
        }

        // An up object without a node leaves the constraint out only when its
        // settings read it; otherwise worldUpNode is -1.
        [Test]
        public void AnUpObjectWithoutANodeMattersOnlyWhenTheSettingsReadIt()
        {
            using var f = new Fixture();
            var aim = f.Constraint<AimConstraint>(f.Parent, .5f, f.A, f.Other);
            aim.worldUpType = AimConstraint.WorldUpType.Vector;
            aim.worldUpObject = f.B;
            f.Convert();
            Assume.That(f.Extension.Components, Is.EqualTo(new Component[] { aim }), "Two sources keep the pose.");
            f.Unwritten.Add(f.B.gameObject);
            var c = f.Resolve().Constraints.Single();
            Assert.That((c.Type, c.WorldUp, c.WorldUpNode), Is.EqualTo(("aim", "vector", -1)));

            aim.worldUpType = AimConstraint.WorldUpType.ObjectUp;
            Assert.That(f.Resolve(), Is.Null);
            Assert.That(f.Extension.IndexOf(aim), Is.EqualTo(-1));
            f.Unwritten.Clear();
            Assert.That(f.Resolve().Constraints.Single().WorldUpNode, Is.EqualTo(f.Nodes[f.B.gameObject]));
        }

        // A second constraint on a target a VRM constraint already moves stays
        // out, so the app does not solve the target twice.
        [Test]
        public void AConstraintBesideAVrmConstraintIsLeftOut()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.A);
            f.Constraint<AimConstraint>(f.B, .5f, f.A, f.Other);
            f.A.localRotation = f.B.localRotation = Quaternion.Euler(0, 0, 20);
            f.Convert();
            Assume.That(f.B.GetComponent<Vrm10RotationConstraint>(), Is.Not.Null);
            Assert.That(f.Extension.Components, Is.Empty);
        }

        // The shared reader takes each type's keys and no others, and no VRChat
        // setting on a Unity constraint.
        [Test]
        public void TheReaderRejectsKeysTheTypeDoesNotHave()
        {
            var data = new AvatarConstraintsData();
            data.Constraints.Add(new AvatarConstraintsData.Constraint
            {
                Node = 1, Type = "rotation", RotationAxes = new[] { true, true, true }, RotationAtRest = new double[3], RotationOffset = new double[3],
            });
            data.Constraints[0].Sources.Add(new AvatarConstraintsData.Source { Node = 2, Weight = 1 });
            var json = AvatarConstraintsData.Write(data);
            Assert.That(AvatarConstraintsData.Read(json).Constraints.Single().Sources.Single().Node, Is.EqualTo(2));

            Dictionary<string, object> Entry(Dictionary<string, object> value) => (Dictionary<string, object>)((List<object>)value["constraints"])[0];
            var extra = AvatarConstraintsData.Write(data);
            Entry(extra)["scaleAxes"] = new List<object> { true, true, true };
            Assert.Throws<InvalidOperationException>(() => AvatarConstraintsData.Read(extra));
            var local = AvatarConstraintsData.Write(data);
            Entry(local)["localSpace"] = true;
            Assert.Throws<InvalidOperationException>(() => AvatarConstraintsData.Read(local));
            data.Constraints[0].Origin = "vrchat"; data.Constraints[0].LocalSpace = true;
            Assert.That(AvatarConstraintsData.Read(AvatarConstraintsData.Write(data)).Constraints.Single().LocalSpace, Is.True);
        }
    }
}
