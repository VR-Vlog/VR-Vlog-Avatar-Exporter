using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRVlog.AvatarContacts;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // VRVLOG_avatar_contacts: the shared reader's contract without the SDK,
    // and Capture, Rebind and Resolve on real contacts (skipped without the SDK).
    public sealed class AvatarContactsExportTests
    {
        static AvatarContactsData.Contact Receiver(string parameter = "Pat") => new AvatarContactsData.Contact
        {
            Node = 2, Shape = "capsule", Radius = .1, Height = .3, Position = new[] { 0, .1, 0 }, Rotation = new[] { 0, 0, 0, 1d },
            Type = "proximity", Parameter = parameter, AllowSelf = true, AllowOthers = false, LocalOnly = true, MinVelocity = .05,
        };

        static AvatarContactsData Data()
        {
            var data = new AvatarContactsData();
            var sender = new AvatarContactsData.Contact { Node = -1, Radius = .05 };
            sender.Tags.Add("Hand");
            data.Senders.Add(sender);
            var receiver = Receiver();
            receiver.Tags.Add("Head");
            receiver.Tags.Add("Hand");
            data.Receivers.Add(receiver);
            data.Omitted.Add(new AvatarContactsData.Omission { Kind = "contactLimit", Count = 2 });
            return data;
        }

        [Test]
        public void SharedReaderReadsBackWhatItWrites()
        {
            var json = AvatarContactsData.Write(Data());
            Assert.That(json["schemaVersion"], Is.EqualTo(1L));
            Assert.That(((Dictionary<string, object>)((List<object>)json["senders"])[0]).Keys,
                Is.EquivalentTo(new[] { "node", "shape", "radius", "height", "position", "rotation", "tags" }), "A Sender has no Receiver keys.");

            var read = AvatarContactsData.Read(json);
            var sender = read.Senders.Single();
            Assert.That((sender.Node, sender.Shape, sender.Radius, sender.Height), Is.EqualTo((-1, "sphere", .05, 0d)));
            Assert.That(sender.Tags, Is.EqualTo(new[] { "Hand" }));
            var receiver = read.Receivers.Single();
            Assert.That((receiver.Node, receiver.Shape, receiver.Radius, receiver.Height, receiver.Type, receiver.Parameter),
                Is.EqualTo((2, "capsule", .1, .3, "proximity", "Pat")));
            Assert.That((receiver.AllowSelf, receiver.AllowOthers, receiver.LocalOnly, receiver.MinVelocity), Is.EqualTo((true, false, true, .05)));
            Assert.That(receiver.Position, Is.EqualTo(new[] { 0, .1, 0 }));
            Assert.That(receiver.Rotation, Is.EqualTo(new[] { 0, 0, 0, 1d }));
            Assert.That(receiver.Tags, Is.EqualTo(new[] { "Head", "Hand" }));
            Assert.That(read.Omitted.Select(o => (o.Kind, o.Count)), Is.EqualTo(new[] { ("contactLimit", 2) }));
        }

        [Test]
        public void SharedReaderRejectsWhatTheContractDoesNotAllow()
        {
            Dictionary<string, object> Written(Action<Dictionary<string, object>> change)
            {
                var json = AvatarContactsData.Write(Data());
                change(json);
                return json;
            }
            Dictionary<string, object> Entry(Dictionary<string, object> json) => (Dictionary<string, object>)((List<object>)json["receivers"])[0];
            Assert.That(() => AvatarContactsData.Read(Written(j => Entry(j)["shape"] = "box")), Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => Entry(j)["type"] = "onExit")), Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => Entry(j)["radius"] = -1d)), Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => Entry(j)["node"] = -2L)), Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => Entry(j)["rotation"] = new List<object> { 0d, 0d, 1d })), Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => Entry(j)["tags"] = new List<object> { "" })), Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => Entry(j).Remove("minVelocity"))), Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => ((Dictionary<string, object>)((List<object>)j["senders"])[0])["parameter"] = "Pat")),
                Throws.InvalidOperationException, "A Sender has no parameter.");
            Assert.That(() => AvatarContactsData.Read(Written(j => j["receivers"] = Enumerable.Repeat(Entry(j), AvatarContactsData.MaximumReceivers + 1).Cast<object>().ToList())),
                Throws.InvalidOperationException, "The app solves at most 64 Receivers.");
            Assert.That(() => AvatarContactsData.Read(Written(j => j["omitted"] = new List<object> { new Dictionary<string, object> { ["kind"] = "contactLimit", ["count"] = 0L } })),
                Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => j["omitted"] = new List<object> { new Dictionary<string, object> { ["kind"] = "other", ["count"] = 1L } })),
                Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => j["schemaVersion"] = 2L)), Throws.InvalidOperationException);
            Assert.That(() => AvatarContactsData.Read(Written(j => j.Remove("omitted"))), Throws.InvalidOperationException);
        }

        [Test]
        public void InjectPlacesTheTableAndChecksTheNodes()
        {
            var bytes = GlbDocument.Create(new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["nodes"] = new List<object> { new Dictionary<string, object>(), new Dictionary<string, object>(), new Dictionary<string, object>() },
            }, new byte[0]).Write();
            var document = GlbDocument.Read(AvatarContactsExport.Inject(bytes, Data()));
            Assert.That((List<object>)document.Json["extensionsUsed"], Has.Member(AvatarContactsData.Extension));
            var read = AvatarContactsData.Read(((Dictionary<string, object>)document.Json["extensions"])[AvatarContactsData.Extension]);
            Assert.That(read.Receivers.Single().Parameter, Is.EqualTo("Pat"));
            Assert.That(AvatarContactsExport.Inject(bytes, null), Is.SameAs(bytes));
            Assert.That(() => AvatarContactsExport.Checked(Data(), 2), Throws.InvalidOperationException, "Node 2 is past two nodes.");
        }

        // Every contact, enabled or not, in hierarchy order, but not those under
        // EditorOnly or excluded; each hangs from its root's node or the nearest
        // written ancestor, placed and scaled in that node's space.
        [Test]
        public void CaptureReadsTheShapesAndResolveHangsThemFromWrittenNodes()
        {
            var receiverType = OptionalTools.Require(AvatarContactsExport.ReceiverType, "Real VRChat SDK integration test.");
            var senderType = OptionalTools.Require(AvatarContactsExport.SenderType, "Real VRChat SDK integration test.");
            var root = new GameObject("Avatar");
            try
            {
                Transform Child(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
                var head = Child(root.transform, "Head");
                head.localPosition = new Vector3(0, 1.4f, 0);
                head.localScale = Vector3.one * 2;
                // Its own object is not written: it hangs from Head.
                var pat = Child(head, "Pat");
                pat.localPosition = new Vector3(0, .1f, .02f);
                pat.localRotation = Quaternion.Euler(0, 30, 0);
                pat.localScale = Vector3.one * 1.5f;
                var receiver = pat.gameObject.AddComponent(receiverType);
                Set(receiver, "shapeType", "Capsule");
                Set(receiver, "radius", .05f);
                Set(receiver, "height", .2f);
                Set(receiver, "position", new Vector3(0, .01f, 0));
                Set(receiver, "rotation", Quaternion.Euler(90, 0, 0));
                Set(receiver, "collisionTags", new List<string> { "Hand", "", "Finger" });
                Set(receiver, "receiverType", "Proximity");
                Set(receiver, "parameter", "HeadPat");
                Set(receiver, "allowSelf", false);
                Set(receiver, "allowOthers", true);
                Set(receiver, "localOnly", true);
                ((Behaviour)receiver).enabled = false;
                // On another object, with its Root Transform on Head, which is written.
                var holder = Child(root.transform, "Holder");
                var hand = holder.gameObject.AddComponent(senderType);
                Set(hand, "rootTransform", head);
                Set(hand, "radius", .1f);
                Set(hand, "collisionTags", new List<string> { "Head" });
                // On the root itself: the model space.
                Set(root.AddComponent(senderType), "radius", .2f);
                var editorOnly = Child(root.transform, "Editor only");
                editorOnly.tag = "EditorOnly";
                editorOnly.gameObject.AddComponent(senderType);
                var excluded = Child(root.transform, "Excluded");
                excluded.gameObject.AddComponent(receiverType);

                var capture = AvatarContactsExport.Capture(root, t => t == excluded);
                Assert.That(capture.Count, Is.EqualTo(3));
                Assert.That(capture.LeftOut, Is.Null);
                var nodes = new Dictionary<GameObject, int> { [head.gameObject] = 5 };
                var data = AvatarContactsData.Read(AvatarContactsData.Write(capture.Resolve(go => nodes.TryGetValue(go, out var node) ? node : -1)));

                Assert.That(data.Senders.Select(s => (s.Node, s.Shape, s.Height)), Is.EqualTo(new[] { (-1, "sphere", 0d), (5, "sphere", 0d) }));
                Assert.That(data.Senders[0].Radius, Is.EqualTo(.2).Within(1e-6), "On the root, in the model space.");
                Assert.That(data.Senders[1].Radius, Is.EqualTo(.1).Within(1e-6), "On Head's own units: its Root Transform is Head.");
                Assert.That(data.Senders[1].Tags, Is.EqualTo(new[] { "Head" }));
                Assert.That(data.Senders[1].Position, Is.EqualTo(new[] { 0d, 0d, 0d }).Within(1e-6));

                var pad = data.Receivers.Single();
                Assert.That((pad.Node, pad.Shape, pad.Type, pad.Parameter, pad.AllowSelf, pad.AllowOthers, pad.LocalOnly),
                    Is.EqualTo((5, "capsule", "proximity", "HeadPat", false, true, true)));
                Assert.That(pad.Tags, Is.EqualTo(new[] { "Hand", "Finger" }), "Empty tags are not written.");
                // Pat's scale against Head's: 1.5.
                Assert.That(pad.Radius, Is.EqualTo(.075).Within(1e-5));
                Assert.That(pad.Height, Is.EqualTo(.3).Within(1e-5));
                var shape = pat.localToWorldMatrix * Matrix4x4.TRS(new Vector3(0, .01f, 0), Quaternion.Euler(90, 0, 0), Vector3.one);
                var expected = head.worldToLocalMatrix * shape;
                var position = expected.GetColumn(3);
                Assert.That(pad.Position, Is.EqualTo(new double[] { position.x, position.y, position.z }).Within(1e-5));
                var rotation = expected.rotation;
                Assert.That(Math.Abs(pad.Rotation[0] * rotation.x + pad.Rotation[1] * rotation.y + pad.Rotation[2] * rotation.z + pad.Rotation[3] * rotation.w),
                    Is.EqualTo(1).Within(1e-5));

                // Without Head written, everything is in the model space; Head's scale of 2 comes in.
                var model = capture.Resolve(_ => -1);
                Assert.That(model.Receivers.Single().Node, Is.EqualTo(-1));
                Assert.That(model.Receivers.Single().Radius, Is.EqualTo(.15).Within(1e-5));
            }
            finally { Object.DestroyImmediate(root); }
        }

        // The build may rename a Receiver's parameter (Modular Avatar) or
        // remove its object: Rebind reads the kept ones again and measures the
        // removed ones against the ancestors still there.
        [Test]
        public void RebindReadsKeptContactsAgainAndKeepsRemovedOnes()
        {
            var receiverType = OptionalTools.Require(AvatarContactsExport.ReceiverType, "Real VRChat SDK integration test.");
            var root = new GameObject("Avatar");
            try
            {
                Transform Child(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
                var head = Child(root.transform, "Head");
                var kept = Child(head, "Kept");
                var renamed = kept.gameObject.AddComponent(receiverType);
                Set(renamed, "parameter", "Before");
                var removed = Child(head, "Removed");
                removed.localPosition = new Vector3(0, .2f, 0);
                Set(removed.gameObject.AddComponent(receiverType), "parameter", "Gone");

                var capture = AvatarContactsExport.Capture(root);
                Set(renamed, "parameter", "After");
                Object.DestroyImmediate(removed.gameObject);
                capture.Rebind();
                var nodes = new Dictionary<GameObject, int> { [head.gameObject] = 1 };
                var data = capture.Resolve(go => nodes.TryGetValue(go, out var node) ? node : -1);
                Assert.That(data.Receivers.Select(r => (r.Node, r.Parameter)), Is.EqualTo(new[] { (1, "After"), (1, "Gone") }));
                Assert.That(data.Receivers[1].Position, Is.EqualTo(new[] { 0, .2, 0 }).Within(1e-6));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ReceiversPastTheLimitAreCounted()
        {
            var receiverType = OptionalTools.Require(AvatarContactsExport.ReceiverType, "Real VRChat SDK integration test.");
            var root = new GameObject("Avatar");
            try
            {
                for (var i = 0; i < AvatarContactsData.MaximumReceivers + 2; i++)
                {
                    var t = new GameObject("Receiver" + i).transform;
                    t.SetParent(root.transform, false);
                    Set(t.gameObject.AddComponent(receiverType), "parameter", "P" + i);
                }
                var capture = AvatarContactsExport.Capture(root);
                var data = AvatarContactsData.Read(AvatarContactsData.Write(capture.Resolve(_ => -1)));
                Assert.That(data.Receivers.Count, Is.EqualTo(AvatarContactsData.MaximumReceivers));
                Assert.That(data.Receivers.Last().Parameter, Is.EqualTo("P63"), "The first 64 in hierarchy order.");
                Assert.That(data.Omitted.Select(o => (o.Kind, o.Count)), Is.EqualTo(new[] { ("contactLimit", 2) }));
                Assert.That(capture.LeftOut, Is.EqualTo(ExporterLocalization.Format(AvatarContactsExport.LimitNote, 2, AvatarContactsData.MaximumReceivers)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CaptureOfACopyWithoutContactsIsNull()
        {
            var root = new GameObject("Avatar");
            try { Assert.That(AvatarContactsExport.Capture(root), Is.Null); }
            finally { Object.DestroyImmediate(root); }
        }

        // Fields by reflection, enums by name: the SDK's own types are not referenced.
        static void Set(Component component, string name, object value)
        {
            FieldInfo field = null;
            for (var type = component.GetType(); type != null && field == null; type = type.BaseType)
                field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(component, value is string text && field.FieldType.IsEnum ? Enum.Parse(field.FieldType, text) : value);
        }
    }
}
