using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.AvatarConstraints;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace VRVlog.LilToonExporter
{
    // VRVLOG_avatar_constraints: the constraints ConstraintExport leaves in
    // their current pose, with their Unity or VRChat settings as authored, so
    // the app can solve them as Unity does. ConstraintExport adds each one it
    // leaves out; Resolve turns them into entries once the glTF nodes are
    // known, and IndexOf gives the entry an FX enabled curve binds to. A
    // constraint whose target, a source or the up object the file has no node
    // for is not written: it keeps its pose, as before. Older apps do not
    // read the extension and keep that pose too.
    internal sealed class AvatarConstraintsExport
    {
        const string VrcPrefix = "VRC.SDK3.Dynamics.Constraint.Components.VRC";

        readonly List<Component> components = new List<Component>();
        readonly Dictionary<Component, int> index = new Dictionary<Component, int>();

        internal IReadOnlyList<Component> Components => components;

        internal void Add(Component component)
        {
            if (component != null && !components.Contains(component)) components.Add(component);
        }

        // A Unity constraint or a VRC Constraint (read by name: the exporter
        // has no SDK reference).
        internal static bool IsConstraint(Type type) =>
            type != null && (typeof(IConstraint).IsAssignableFrom(type) && typeof(Behaviour).IsAssignableFrom(type) || VrcType(type) != null);

        static string VrcType(Type type)
        {
            var name = type?.FullName;
            if (name == null || !name.StartsWith(VrcPrefix, StringComparison.Ordinal) || !name.EndsWith("Constraint", StringComparison.Ordinal)) return null;
            var kind = name.Substring(VrcPrefix.Length, name.Length - VrcPrefix.Length - "Constraint".Length);
            switch (kind)
            {
                case "Position": return "position";
                case "Rotation": return "rotation";
                case "Scale": return "scale";
                case "Parent": return "parent";
                case "Aim": return "aim";
                case "LookAt": return "lookAt";
                default: return null;
            }
        }

        // The object a constraint moves: its own, or a VRC Constraint's Target Transform.
        internal static Transform TargetOf(Component component)
        {
            if (component == null) return null;
            if (component is IConstraint || VrcType(component.GetType()) == null) return component.transform;
            using var settings = new SerializedObject(component);
            var target = settings.FindProperty("TargetTransform")?.objectReferenceValue as Transform;
            return target != null ? target : component.transform;
        }

        // The entry of each constraint the file has nodes for, in the order
        // they were added; null when there is none.
        internal AvatarConstraintsData Resolve(Func<GameObject, int> node)
        {
            index.Clear();
            var data = new AvatarConstraintsData();
            foreach (var component in components)
            {
                var entry = component == null ? null : component is IConstraint ? ReadUnity(component, node) : ReadVrc(component, node);
                if (entry == null) continue;
                index[component] = data.Constraints.Count;
                data.Constraints.Add(entry);
            }
            return data.Constraints.Count > 0 ? data : null;
        }

        // The entry the last Resolve wrote for the component, or -1.
        internal int IndexOf(Component component) => component != null && index.TryGetValue(component, out var value) ? value : -1;

        internal static byte[] Inject(byte[] bytes, AvatarConstraintsData data)
        {
            if (data == null || data.Constraints.Count == 0) return bytes;
            ExportTestHooks.TableFaultInjection?.Invoke(AvatarConstraintsData.Extension);
            var document = GlbDocument.Read(bytes);
            var nodes = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> list ? list.Count : 0;
            Checked(data, nodes);
            document.PlaceExtension(AvatarConstraintsData.Extension, AvatarConstraintsData.Write(data));
            return document.Write();
        }

        // The data when it fits the contract and every node is below nodes;
        // throws otherwise. Null stays null.
        internal static AvatarConstraintsData Checked(AvatarConstraintsData data, int nodes)
        {
            if (data == null) return null;
            AvatarConstraintsData.Write(data);
            if (data.Constraints.Any(c => c.Node >= nodes || c.WorldUpNode >= nodes || c.Sources.Any(s => s.Node >= nodes)))
                throw new InvalidOperationException("Avatar constraint node is out of range.");
            return data;
        }

        static AvatarConstraintsData.Constraint ReadUnity(Component component, Func<GameObject, int> node)
        {
            var constraint = (IConstraint)component;
            var entry = new AvatarConstraintsData.Constraint
            {
                Origin = "unity", Enabled = ((Behaviour)component).enabled, Active = constraint.constraintActive, Locked = constraint.locked,
                Weight = constraint.weight,
            };
            if (!Node(component.transform, node, out entry.Node)) return null;
            switch (component)
            {
                case PositionConstraint position:
                    entry.Type = "position";
                    entry.PositionAxes = Axes(position.translationAxis);
                    entry.PositionAtRest = Vector(position.translationAtRest); entry.PositionOffset = Vector(position.translationOffset);
                    break;
                case RotationConstraint rotation:
                    entry.Type = "rotation";
                    entry.RotationAxes = Axes(rotation.rotationAxis);
                    entry.RotationAtRest = Vector(rotation.rotationAtRest); entry.RotationOffset = Vector(rotation.rotationOffset);
                    break;
                case ScaleConstraint scale:
                    entry.Type = "scale";
                    entry.ScaleAxes = Axes(scale.scalingAxis);
                    entry.ScaleAtRest = Vector(scale.scaleAtRest); entry.ScaleOffset = Vector(scale.scaleOffset);
                    break;
                case ParentConstraint parent:
                    entry.Type = "parent";
                    entry.PositionAxes = Axes(parent.translationAxis); entry.RotationAxes = Axes(parent.rotationAxis);
                    entry.PositionAtRest = Vector(parent.translationAtRest); entry.RotationAtRest = Vector(parent.rotationAtRest);
                    break;
                case AimConstraint aim:
                    entry.Type = "aim";
                    entry.RotationAxes = Axes(aim.rotationAxis);
                    entry.RotationAtRest = Vector(aim.rotationAtRest); entry.RotationOffset = Vector(aim.rotationOffset);
                    entry.AimAxis = Vector(aim.aimVector); entry.UpAxis = Vector(aim.upVector);
                    var worldUp = (int)aim.worldUpType;
                    if (worldUp < 0 || worldUp >= AvatarConstraintsData.WorldUpTypes.Length) return null;
                    entry.WorldUp = AvatarConstraintsData.WorldUpTypes[worldUp]; entry.WorldUpVector = Vector(aim.worldUpVector);
                    if (!UpNode(aim.worldUpObject, UsesUpObject(entry), node, out entry.WorldUpNode)) return null;
                    break;
                case LookAtConstraint lookAt:
                    entry.Type = "lookAt";
                    entry.RotationAtRest = Vector(lookAt.rotationAtRest); entry.RotationOffset = Vector(lookAt.rotationOffset);
                    entry.Roll = lookAt.roll; entry.UseUpNode = lookAt.useUpObject;
                    if (!UpNode(lookAt.worldUpObject, UsesUpObject(entry), node, out entry.WorldUpNode)) return null;
                    break;
                default: return null;
            }
            for (var i = 0; i < constraint.sourceCount; i++)
            {
                var source = constraint.GetSource(i);
                // Unity skips a source without an object.
                if (source.sourceTransform == null) continue;
                if (!Node(source.sourceTransform, node, out var sourceNode)) return null;
                var item = new AvatarConstraintsData.Source { Node = sourceNode, Weight = source.weight };
                if (component is ParentConstraint parent)
                {
                    item.PositionOffset = Vector(parent.GetTranslationOffset(i));
                    item.RotationOffset = Vector(parent.GetRotationOffset(i));
                }
                entry.Sources.Add(item);
            }
            return entry;
        }

        // The serialized fields of a VRC Constraint, read by name.
        static AvatarConstraintsData.Constraint ReadVrc(Component component, Func<GameObject, int> node)
        {
            var type = VrcType(component.GetType());
            if (type == null) return null;
            using var settings = new SerializedObject(component);
            bool True(string property) => settings.FindProperty(property)?.boolValue == true;
            double[] Read(string property, Vector3 fallback) => Vector(settings.FindProperty(property)?.vector3Value ?? fallback);
            bool[] Affects(string kind) => new[] { True("Affects" + kind + "X"), True("Affects" + kind + "Y"), True("Affects" + kind + "Z") };
            var entry = new AvatarConstraintsData.Constraint
            {
                Type = type, Origin = "vrchat", Enabled = !(component is Behaviour behaviour) || behaviour.enabled, Active = True("IsActive"),
                Locked = True("Locked"), Weight = settings.FindProperty("GlobalWeight")?.floatValue ?? 0,
                LocalSpace = True("SolveInLocalSpace"), Freeze = True("FreezeToWorld"),
            };
            if (!Node(TargetOf(component), node, out entry.Node)) return null;
            if (type == "position" || type == "parent") { entry.PositionAxes = Affects("Position"); entry.PositionAtRest = Read("PositionAtRest", Vector3.zero); }
            if (type == "rotation" || type == "parent" || type == "aim") entry.RotationAxes = Affects("Rotation");
            if (type != "position" && type != "scale") entry.RotationAtRest = Read("RotationAtRest", Vector3.zero);
            if (type == "position") entry.PositionOffset = Read("PositionOffset", Vector3.zero);
            if (type == "rotation" || type == "aim" || type == "lookAt") entry.RotationOffset = Read("RotationOffset", Vector3.zero);
            if (type == "scale")
            {
                entry.ScaleAxes = Affects("Scale");
                entry.ScaleAtRest = Read("ScaleAtRest", Vector3.one); entry.ScaleOffset = Read("ScaleOffset", Vector3.one);
            }
            if (type == "aim")
            {
                entry.AimAxis = Read("AimAxis", Vector3.forward); entry.UpAxis = Read("UpAxis", Vector3.up);
                var worldUp = settings.FindProperty("WorldUp")?.intValue ?? 0;
                if (worldUp < 0 || worldUp >= AvatarConstraintsData.WorldUpTypes.Length) return null;
                entry.WorldUp = AvatarConstraintsData.WorldUpTypes[worldUp]; entry.WorldUpVector = Read("WorldUpVector", Vector3.up);
            }
            if (type == "lookAt") { entry.Roll = settings.FindProperty("Roll")?.floatValue ?? 0; entry.UseUpNode = True("UseUpTransform"); }
            if (type == "aim" || type == "lookAt")
            {
                var up = settings.FindProperty("WorldUpTransform")?.objectReferenceValue as Transform;
                if (!UpNode(up, UsesUpObject(entry), node, out entry.WorldUpNode)) return null;
            }
            // Sixteen inline entries followed by an overflow list; entries
            // beyond totalLength are stale.
            var count = settings.FindProperty("Sources.totalLength")?.intValue ?? 0;
            for (var i = 0; i < count; i++)
            {
                var path = i < 16 ? "Sources.source" + i : "Sources.overflowList.Array.data[" + (i - 16) + "]";
                var transform = settings.FindProperty(path + ".SourceTransform")?.objectReferenceValue as Transform;
                if (transform == null) continue;
                if (!Node(transform, node, out var sourceNode)) return null;
                var item = new AvatarConstraintsData.Source { Node = sourceNode, Weight = settings.FindProperty(path + ".Weight")?.floatValue ?? 0 };
                if (type == "parent")
                {
                    item.PositionOffset = Read(path + ".ParentPositionOffset", Vector3.zero);
                    item.RotationOffset = Read(path + ".ParentRotationOffset", Vector3.zero);
                }
                entry.Sources.Add(item);
            }
            return entry;
        }

        static bool Node(Transform transform, Func<GameObject, int> node, out int value)
        {
            value = transform == null ? -1 : node(transform.gameObject);
            return value >= 0;
        }

        // No up object is -1. One the file has no node for leaves the
        // constraint out when its settings read it, and is -1 otherwise.
        static bool UpNode(Transform transform, bool used, Func<GameObject, int> node, out int value)
        {
            if (transform == null) { value = -1; return true; }
            if (Node(transform, node, out value)) return true;
            value = -1;
            return !used;
        }

        // Aim reads the up object for Object Up and Object Rotation Up only,
        // Look At when Use Up Object is on.
        static bool UsesUpObject(AvatarConstraintsData.Constraint entry) =>
            entry.Type == "aim" ? entry.WorldUp == "objectUp" || entry.WorldUp == "objectRotationUp" : entry.UseUpNode;

        static double[] Vector(Vector3 value) => new double[] { value.x, value.y, value.z };
        static bool[] Axes(Axis axis) => new[] { (axis & Axis.X) != 0, (axis & Axis.Y) != 0, (axis & Axis.Z) != 0 };
    }
}
