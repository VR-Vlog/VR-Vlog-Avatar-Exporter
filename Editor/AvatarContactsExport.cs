using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRVlog.AvatarContacts;

namespace VRVlog.LilToonExporter
{
    // VRVLOG_avatar_contacts (Schema/VRVLOG_avatar_contacts.1.schema.json):
    // the avatar's VRChat Contact Senders and Receivers with their shape,
    // tags and parameter, for the app to solve the overlaps and feed the
    // Receivers' parameters. Older apps do not read the extension.
    //
    // The export removes the objects that hold contacts before the build
    // (ExportGimmickDetection) or prunes them as empty, so a contact hangs
    // from the nearest ancestor that is written, placed in that node's space,
    // or from the avatar root (node -1) in the model space, as
    // AvatarEffectsExport places effects. Capture reads the copy before those
    // objects go; Rebind reads again the contacts the build kept (it may move
    // them or rename their parameters) and measures each against the
    // ancestors that are still there; Resolve binds them to output nodes.
    internal sealed class AvatarContactsExport
    {
        internal const string SenderType = ExportGimmickRules.ContactSender;
        internal const string ReceiverType = ExportGimmickRules.ContactReceiver;
        internal const string LimitNote = "Contact Receiverが上限の{1}個を超えたため、{0}個をアプリで動かしません";

        sealed class Entry
        {
            internal bool Receiver;
            // The component on the copy; Unity's null once the export removed it.
            internal Component Component;
            // The contact's root object, then each ancestor up to the avatar root.
            internal Transform[] Ancestors;
            // The shape's pose (scale included) against each of them; against the root in the model space.
            internal Matrix4x4[] Offsets;
            internal Matrix4x4 World;
            // In the root object's units, before its scale.
            internal float Radius, Height;
            internal AvatarContactsData.Contact Values;
        }

        Transform root;
        readonly List<Entry> entries = new List<Entry>();
        int limited;

        internal int Count => entries.Count;
        // The result line: null when no Receiver was left out.
        internal string LeftOut => limited == 0 ? null
            : ExporterLocalization.Format(LimitNote, limited, AvatarContactsData.MaximumReceivers);

        // Reads every Sender and Receiver under root (the export copy),
        // enabled or not, in hierarchy order, skipping those under EditorOnly
        // (VRChat removes them at upload) and those excluded (objects the user
        // left out of the export). Receivers past the limit are counted.
        // Null when there is nothing to write or to tell.
        internal static AvatarContactsExport Capture(GameObject root, Func<Transform, bool> excluded = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var capture = new AvatarContactsExport { root = root.transform };
            var receivers = 0;
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                var receiver = IsType(component, ReceiverType);
                if (!receiver && !IsType(component, SenderType)) continue;
                if (EditorOnly(component.transform, root.transform) || excluded != null && excluded(component.transform)) continue;
                var entry = new Entry { Receiver = receiver, Component = component };
                if (!Read(entry, component)) continue;
                if (receiver && receivers++ >= AvatarContactsData.MaximumReceivers)
                {
                    capture.limited++;
                    continue;
                }
                capture.entries.Add(entry);
            }
            capture.Rebind();
            return capture.entries.Count == 0 && capture.limited == 0 ? null : capture;
        }

        static bool IsType(Component component, string fullName) => component != null && component.GetType().FullName == fullName;

        static bool EditorOnly(Transform transform, Transform root)
        {
            for (var current = transform; current != null; current = current == root ? null : current.parent)
                if (current.CompareTag("EditorOnly")) return true;
            return false;
        }

        // The settings and pose of a live component; false for a shape or
        // Receiver Type this exporter does not know (a newer SDK).
        static bool Read(Entry entry, Component component)
        {
            var shape = Member(component, "shapeType") is Enum shapeType ? Lower(shapeType.ToString()) : "sphere";
            if (Array.IndexOf(AvatarContactsData.Shapes, shape) < 0) return false;
            var values = new AvatarContactsData.Contact { Shape = shape };
            if (entry.Receiver)
            {
                var type = Member(component, "receiverType") is Enum receiverType ? Lower(receiverType.ToString()) : "constant";
                if (Array.IndexOf(AvatarContactsData.ReceiverTypes, type) < 0) return false;
                values.Type = type;
                var parameter = Member(component, "parameter") as string ?? "";
                values.Parameter = parameter.Length <= AvatarContactsData.MaximumParameterLength ? parameter : "";
                values.AllowSelf = Member(component, "allowSelf") is bool self && self;
                values.AllowOthers = Member(component, "allowOthers") is bool others && others;
                values.LocalOnly = Member(component, "localOnly") is bool local && local;
                values.MinVelocity = Math.Max(0, Number(Member(component, "minVelocity")));
            }
            if (Member(component, "collisionTags") is IEnumerable tags)
                foreach (var tag in tags)
                    if (tag is string text && text.Length > 0 && text.Length <= 1024 && values.Tags.Count < AvatarContactsData.MaximumTags)
                        values.Tags.Add(text);
            var at = Member(component, "rootTransform") as Transform;
            // Unity's missing-object sentinel is not CLR null.
            if (at == null) at = component.transform;
            var position = Member(component, "position") is Vector3 p && Finite(p) ? p : Vector3.zero;
            var rotation = Member(component, "rotation") is Quaternion q && Finite(q) && Dot(q) > 1e-12f ? Normalized(q) : Quaternion.identity;
            entry.Values = values;
            entry.Radius = Mathf.Max(0, (float)Number(Member(component, "radius")));
            // Below the diameter VRChat solves a capsule as the sphere it is.
            entry.Height = shape == "capsule" ? Mathf.Max(0, (float)Number(Member(component, "height"))) : 0;
            entry.World = at.localToWorldMatrix * Matrix4x4.TRS(position, rotation, Vector3.one);
            var chain = new List<Transform>();
            for (var ancestor = at; ancestor != null; ancestor = ancestor.parent)
            {
                chain.Add(ancestor);
                if (ancestor == component.transform.root) break;
            }
            entry.Ancestors = chain.ToArray();
            return true;
        }

        // Reads again the contacts the build kept and measures each, as
        // captured or as read again, against the ancestors that are still
        // there. The export calls it once the build has moved and removed
        // objects, before the empty ones are pruned and before RootScaleBake
        // moves the root scale into the root's children; the pose against the
        // root takes that scale, as the model space does.
        internal void Rebind()
        {
            if (root == null) return;
            foreach (var entry in entries)
            {
                if (entry.Component != null && entry.Component.transform.IsChildOf(root)) Read(entry, entry.Component);
                // The build may move an object under another parent: from the
                // nearest object still there, walk the parents as they are now.
                var chain = new List<Transform>();
                for (var ancestor = entry.Ancestors.FirstOrDefault(a => a != null); ancestor != null; ancestor = ancestor.parent)
                {
                    chain.Add(ancestor);
                    if (ancestor == root) break;
                }
                if (chain.Count == 0 || chain[chain.Count - 1] != root) chain.Add(root);
                entry.Ancestors = chain.ToArray();
                entry.Offsets = entry.Ancestors.Select(ancestor => ancestor == root
                    ? Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix * entry.World
                    : ancestor.worldToLocalMatrix * entry.World).ToArray();
            }
        }

        // The extension once the output nodes are known: node gives the output
        // node of an object of the copy, -1 when it is not written. A contact
        // with no written ancestor hangs from the root (node -1). Null when
        // there is nothing to write; LeftOut still tells what was left out.
        internal AvatarContactsData Resolve(Func<GameObject, int> node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            var data = new AvatarContactsData();
            foreach (var entry in entries)
            {
                var at = entry.Ancestors.Length - 1;
                var output = -1;
                for (var i = 0; i < entry.Ancestors.Length; i++)
                {
                    var ancestor = entry.Ancestors[i];
                    if (ancestor == null || ancestor == root) continue;
                    if ((output = node(ancestor.gameObject)) >= 0) { at = i; break; }
                }
                var offset = entry.Offsets[at];
                // VRChat scales the shape by the largest axis of its world scale
                // and the app by the node's: write the ratio of the two.
                var nodeScale = Largest((entry.World * offset.inverse).lossyScale);
                var scale = nodeScale > 1e-12f ? Largest(entry.World.lossyScale) / nodeScale : 0;
                var position = offset.GetColumn(3);
                var rotation = Normalized(offset.rotation);
                var values = entry.Values;
                var contact = new AvatarContactsData.Contact
                {
                    Node = output, Shape = values.Shape, Radius = Number(entry.Radius * scale), Height = Number(entry.Height * scale),
                    Position = new[] { Number(position.x), Number(position.y), Number(position.z) },
                    Rotation = new[] { Number(rotation.x), Number(rotation.y), Number(rotation.z), Number(rotation.w) },
                    Type = values.Type, Parameter = values.Parameter, AllowSelf = values.AllowSelf, AllowOthers = values.AllowOthers,
                    LocalOnly = values.LocalOnly, MinVelocity = values.MinVelocity,
                };
                contact.Tags.AddRange(values.Tags);
                (entry.Receiver ? data.Receivers : data.Senders).Add(contact);
            }
            if (limited > 0) data.Omitted.Add(new AvatarContactsData.Omission { Kind = "contactLimit", Count = limited });
            return data.Senders.Count == 0 && data.Receivers.Count == 0 ? null : data;
        }

        internal static byte[] Inject(byte[] bytes, AvatarContactsData data)
        {
            if (data == null || data.Senders.Count == 0 && data.Receivers.Count == 0) return bytes;
            var document = GlbDocument.Read(bytes);
            var nodes = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> list ? list.Count : 0;
            Checked(data, nodes);
            document.PlaceExtension(AvatarContactsData.Extension, AvatarContactsData.Write(data));
            return document.Write();
        }

        // The data when it fits the contract and every node is below nodes;
        // throws otherwise. Null stays null.
        internal static AvatarContactsData Checked(AvatarContactsData data, int nodes)
        {
            if (data == null) return null;
            AvatarContactsData.Write(data);
            if (data.Senders.Concat(data.Receivers).Any(c => c.Node >= nodes)) throw new InvalidOperationException("Avatar contact node is out of range.");
            return data;
        }

        static string Lower(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static float Largest(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        static float Dot(Quaternion q) => q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
        static Quaternion Normalized(Quaternion q)
        {
            var length = Mathf.Sqrt(Dot(q));
            return Finite(q) && length > 1e-6f ? new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : Quaternion.identity;
        }
        static double Number(object value) => value is float single && Finite(single) ? single : 0;
        static double Number(float value) => Finite(value) ? value : 0;

        static object Member(Component source, string name)
        {
            for (var type = source.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                var field = type.GetField(name, flags); if (field != null) return field.GetValue(source);
            }
            return null;
        }
    }
}
