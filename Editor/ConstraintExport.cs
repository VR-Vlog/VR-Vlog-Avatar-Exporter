using System;
using System.Collections.Generic;
using System.Linq;
using AimAxis = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_node_constraint.AimAxis;
using RollAxis = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_node_constraint.RollAxis;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace VRVlog.LilToonExporter
{
    // Unity and VRChat constraints on the owned, post-NDMF export copy become
    // VRMC_node_constraint where VRM has the same kind. VRM only has rotation,
    // roll and aim with one source each; the rest keep the current pose, as
    // before, and are listed in the result list. The originals stay on the copy
    // (UniVRM does not write them). Off constraints and those with Lock off
    // (VRChat only re-measures At Rest and Offset for them) keep the pose
    // too and are listed; weightless ones have nothing to move.
    internal static class ConstraintExport
    {
        internal const string Category = "コンストレイント";
        internal const string KeptRotation = "VRMの回転コンストレイントに変換しました";
        internal const string ApproximatedRotation = "VRMの回転コンストレイントで近似しました。基準の向きか強さが元と違うため、大きく動いたときの向きが元と少し異なります";
        internal const string ApproximatedRoll = "1軸だけの回転をVRMのロールコンストレイントで近似しました";
        internal const string ApproximatedAim = "VRMのエイムコンストレイントで近似しました。向ける軸は保ちますが、ねじれが元と異なる場合があります";
        internal const string OmittedKind = "VRMに同じ働きのコンストレイントがないため、今の姿勢のまま書き出しました";
        internal const string OmittedSetting = "VRMで表せない設定（複数のソース・自分自身がソース・2軸だけの回転・ワールドに固定・軸以外の向き）のため、今の姿勢のまま書き出しました";
        internal const string OmittedParent = "ソースと対象の親が違う回転（ワールド空間の回転と1軸だけの回転）はVRMで表せないため、今の姿勢のまま書き出しました";
        internal const string OmittedRest = "VRMで近似すると基準の向きの差で元より大きくずれるため、今の姿勢のまま書き出しました";
        internal const string OmittedOutside = "アバターのルート・アバターの外・非表示のオブジェクトを参照しているため、今の姿勢のまま書き出しました";
        internal const string OmittedDuplicate = "同じオブジェクトに別のコンストレイントが既にあるため、今の姿勢のまま書き出しました";
        internal const string OmittedCycle = "ソースのコンストレイントをたどるとこのオブジェクトに戻り、VRMはこの循環を書けないため、今の姿勢のまま書き出しました";
        internal const string OmittedUnlocked = "Lockがオフのため、VRChatでも対象を動かしません。今の姿勢のまま書き出しました";
        internal const string OmittedOff = "オフになっているため、今の姿勢のまま書き出しました。アニメーションでオンにしてもVRMでは動きません";
        internal static readonly string[] Reasons =
        {
            KeptRotation, ApproximatedRotation, ApproximatedRoll, ApproximatedAim,
            OmittedKind, OmittedSetting, OmittedParent, OmittedRest, OmittedOutside, OmittedDuplicate, OmittedCycle,
            OmittedUnlocked, OmittedOff
        };

        // A VRM rotation constraint applies the source's local change in the
        // target's own frame. It equals the original when both rest rotations
        // match; past 60 degrees it moves further off than keeping the pose.
        const float KeptRestAngle = 1f, MaximumRestAngle = 60f;
        // VRM aim turns the axis onto the source even at rest.
        const float MaximumAimAngle = 2f;
        const string VrcPrefix = "VRC.SDK3.Dynamics.Constraint.Components.VRC";

        internal sealed class Result { internal int Sources, Kept, Approximated, Omitted; }

        enum Kind { Rotation, Aim, LookAt, Parent, Position, Scale }

        sealed class Plan
        {
            internal Component Component;
            internal Kind Kind;
            internal bool On, Locked, LocalSpace, FreezeToWorld, X = true, Y = true, Z = true;
            internal float Weight;
            internal Transform Target;
            internal Vector3 AimAxis = Vector3.forward;
            internal readonly List<(Transform Transform, float Weight)> Sources = new List<(Transform, float)>();
        }

        internal static Result Convert(GameObject source, GameObject copy, ICollection<string> warnings, ExportFallbackReport fallbacks = null)
        {
            if (source == null || copy == null || source == copy || copy.transform.IsChildOf(source.transform) ||
                source.transform.IsChildOf(copy.transform) || EditorUtility.IsPersistent(copy))
                throw new InvalidOperationException("コンストレイントの変換には元アバターから独立した一時コピーが必要です。");
            var result = new Result();
            var plans = copy.GetComponentsInChildren<Component>(true).Select(c => ReadUnity(c) ?? ReadVrc(c)).Where(p => p != null).ToArray();
            if (plans.Length == 0) return result;
            // Authored VRM constraints win; VRM allows one per node.
            var authored = copy.GetComponentsInChildren<IVrm10Constraint>(true);
            var taken = new HashSet<Transform>(authored.Select(c => c.ConstraintTarget).Where(t => t != null));
            // Target -> source edges of the VRM constraints in the file, so a
            // new one never closes a cycle (VRMC_node_constraint forbids them).
            var edges = new Dictionary<Transform, List<Transform>>();
            foreach (var constraint in authored)
                if (constraint.ConstraintTarget != null && constraint.ConstraintSource != null)
                    AddEdge(edges, constraint.ConstraintTarget, constraint.ConstraintSource);
            var added = new List<Component>();
            var decisions = new List<(Plan Plan, ExportOutcome Outcome, string Reason)>();
            try
            {
                foreach (var plan in plans)
                {
                    var sources = plan.Sources.Where(s => s.Transform != null && s.Weight > 0 && !float.IsInfinity(s.Weight)).ToArray();
                    // Weightless constraints, or ones without a source or target, have nothing to move.
                    if (!(plan.Weight > 0) || sources.Length == 0 || plan.Target == null) continue;
                    // With Lock off VRChat does not move the target either; it only
                    // re-measures At Rest and Offset, so a VRM constraint would differ.
                    var (outcome, reason) = !plan.On ? (ExportOutcome.Omitted, OmittedOff)
                        : !plan.Locked ? (ExportOutcome.Omitted, OmittedUnlocked)
                        : Apply(plan, sources, copy.transform, taken, edges, added);
                    if (outcome != ExportOutcome.Omitted) { taken.Add(plan.Target); AddEdge(edges, plan.Target, sources[0].Transform); }
                    decisions.Add((plan, outcome, reason));
                }
            }
            catch
            {
                // The stage is omitted as a whole; never write a partial set.
                foreach (var component in added) if (component != null) UnityEngine.Object.DestroyImmediate(component);
                throw;
            }
            foreach (var (plan, outcome, reason) in decisions)
            {
                result.Sources++;
                if (outcome == ExportOutcome.Omitted) result.Omitted++;
                else if (outcome == ExportOutcome.Kept) result.Kept++;
                else result.Approximated++;
                var entry = fallbacks?.Record(outcome, Category, plan.Target.name + " (" + plan.Component.GetType().Name + ")", reason);
                if (entry != null) entry.Path = ExportFallbackReport.RelativePath(copy.transform, plan.Target);
            }
            if (result.Kept + result.Approximated > 0 && copy.GetComponent<Vrm10Instance>() == null) copy.AddComponent<Vrm10Instance>();
            if (result.Sources > 0)
                warnings?.Add($"コンストレイント変換: 元{result.Sources}件 / 変換{result.Kept + result.Approximated}件（うち近似{result.Approximated}件） / 省略{result.Omitted}件。");
            return result;
        }

        static (ExportOutcome, string) Apply(Plan plan, (Transform Transform, float Weight)[] sources, Transform root, HashSet<Transform> taken,
            Dictionary<Transform, List<Transform>> edges, List<Component> added)
        {
            if (plan.Kind == Kind.Parent || plan.Kind == Kind.Position || plan.Kind == Kind.Scale) return (ExportOutcome.Omitted, OmittedKind);
            if (sources.Length != 1 || plan.FreezeToWorld) return (ExportOutcome.Omitted, OmittedSetting);
            var target = plan.Target;
            var source = sources[0].Transform;
            if (source == target) return (ExportOutcome.Omitted, OmittedSetting);
            if (!Inside(root, target) || !Inside(root, source)) return (ExportOutcome.Omitted, OmittedOutside);
            if (taken.Contains(target)) return (ExportOutcome.Omitted, OmittedDuplicate);
            if (Reaches(edges, source, target)) return (ExportOutcome.Omitted, OmittedCycle);
            var weight = Mathf.Clamp01(plan.Weight * Mathf.Min(1, sources[0].Weight));
            if (plan.Kind == Kind.Rotation)
            {
                var axes = (plan.X ? 1 : 0) + (plan.Y ? 1 : 0) + (plan.Z ? 1 : 0);
                // Roll maps the delta through both rests in the shared parent
                // frame, even for a local-space source.
                if ((!plan.LocalSpace || axes == 1) && target.parent != source.parent) return (ExportOutcome.Omitted, OmittedParent);
                if (axes == 1)
                {
                    var roll = target.gameObject.AddComponent<Vrm10RollConstraint>(); added.Add(roll);
                    roll.Source = source; roll.Weight = weight;
                    roll.RollAxis = plan.X ? RollAxis.X : plan.Y ? RollAxis.Y : RollAxis.Z;
                    return (ExportOutcome.Approximated, ApproximatedRoll);
                }
                if (axes != 3) return (ExportOutcome.Omitted, OmittedSetting);
                var rest = Quaternion.Angle(target.localRotation, source.localRotation);
                if (rest >= MaximumRestAngle) return (ExportOutcome.Omitted, OmittedRest);
                var rotation = target.gameObject.AddComponent<Vrm10RotationConstraint>(); added.Add(rotation);
                rotation.Source = source; rotation.Weight = weight;
                return rest <= KeptRestAngle && weight == 1 ? (ExportOutcome.Kept, KeptRotation) : (ExportOutcome.Approximated, ApproximatedRotation);
            }
            // Aim and look-at: one direction towards the source position.
            if (!(plan.X && plan.Y && plan.Z) || !TryAxis(plan.AimAxis, out var axis, out var vector)) return (ExportOutcome.Omitted, OmittedSetting);
            var toSource = source.position - target.position;
            if (toSource.sqrMagnitude < 1e-10f || Vector3.Angle(target.rotation * vector, toSource) > MaximumAimAngle)
                return (ExportOutcome.Omitted, OmittedRest);
            var aim = target.gameObject.AddComponent<Vrm10AimConstraint>(); added.Add(aim);
            aim.Source = source; aim.Weight = weight; aim.AimAxis = axis;
            return (ExportOutcome.Approximated, ApproximatedAim);
        }

        // UniVRM does not write the root as a node, so it can be neither end.
        static bool Inside(Transform root, Transform value) =>
            value != null && value != root && value.IsChildOf(root) && value.gameObject.activeInHierarchy;

        static void AddEdge(Dictionary<Transform, List<Transform>> edges, Transform target, Transform source)
        {
            if (!edges.TryGetValue(target, out var sources)) edges.Add(target, sources = new List<Transform>());
            sources.Add(source);
        }

        // Whether following the sources from `from` arrives at `to`.
        static bool Reaches(Dictionary<Transform, List<Transform>> edges, Transform from, Transform to)
        {
            var visited = new HashSet<Transform>();
            var pending = new Stack<Transform>();
            pending.Push(from);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (current == to) return true;
                if (!visited.Add(current) || !edges.TryGetValue(current, out var next)) continue;
                foreach (var value in next) pending.Push(value);
            }
            return false;
        }

        static bool TryAxis(Vector3 value, out AimAxis axis, out Vector3 vector)
        {
            foreach (var (candidate, direction) in new[]
            {
                (AimAxis.PositiveX, Vector3.right), (AimAxis.NegativeX, Vector3.left), (AimAxis.PositiveY, Vector3.up),
                (AimAxis.NegativeY, Vector3.down), (AimAxis.PositiveZ, Vector3.forward), (AimAxis.NegativeZ, Vector3.back)
            })
                if (value.sqrMagnitude > 1e-12f && Vector3.Angle(value, direction) < .5f) { axis = candidate; vector = direction; return true; }
            axis = default; vector = default;
            return false;
        }

        static Plan ReadUnity(Component component)
        {
            if (!(component is IConstraint constraint) || !(component is Behaviour behaviour)) return null;
            var plan = new Plan { Component = component, Target = component.transform, Weight = constraint.weight,
                // Unity's manual: Lock "has no effect in Play Mode", so an active Unity constraint drives its
                // target in the app whatever Lock says. Only VRChat's Locked keeps the target still.
                On = behaviour.isActiveAndEnabled && constraint.constraintActive, Locked = true };
            var axes = Axis.X | Axis.Y | Axis.Z;
            switch (component)
            {
                case RotationConstraint rotation: plan.Kind = Kind.Rotation; axes = rotation.rotationAxis; break;
                case AimConstraint aim: plan.Kind = Kind.Aim; axes = aim.rotationAxis; plan.AimAxis = aim.aimVector; break;
                case LookAtConstraint _: plan.Kind = Kind.LookAt; break;
                case ParentConstraint _: plan.Kind = Kind.Parent; break;
                case PositionConstraint _: plan.Kind = Kind.Position; break;
                case ScaleConstraint _: plan.Kind = Kind.Scale; break;
                default: return null;
            }
            plan.X = (axes & Axis.X) != 0; plan.Y = (axes & Axis.Y) != 0; plan.Z = (axes & Axis.Z) != 0;
            for (var index = 0; index < constraint.sourceCount; index++)
            {
                var item = constraint.GetSource(index);
                plan.Sources.Add((item.sourceTransform, item.weight));
            }
            return plan;
        }

        // The exporter has no SDK reference; read the serialized fields by name.
        static Plan ReadVrc(Component component)
        {
            var name = component != null ? component.GetType().FullName : null;
            if (name == null || !name.StartsWith(VrcPrefix, StringComparison.Ordinal) || !name.EndsWith("Constraint", StringComparison.Ordinal) ||
                !Enum.TryParse(name.Substring(VrcPrefix.Length, name.Length - VrcPrefix.Length - "Constraint".Length), out Kind kind) ||
                !Enum.IsDefined(typeof(Kind), kind))
                return null;
            using var settings = new SerializedObject(component);
            bool True(string property) => settings.FindProperty(property)?.boolValue == true;
            var plan = new Plan
            {
                Component = component, Kind = kind,
                On = (component is Behaviour behaviour ? behaviour.isActiveAndEnabled : component.gameObject.activeInHierarchy) &&
                    True("IsActive"),
                Locked = True("Locked"),
                Weight = settings.FindProperty("GlobalWeight")?.floatValue ?? 0,
                LocalSpace = True("SolveInLocalSpace"), FreezeToWorld = True("FreezeToWorld"),
                Target = settings.FindProperty("TargetTransform")?.objectReferenceValue as Transform ?? component.transform,
            };
            if (kind == Kind.Rotation || kind == Kind.Aim)
            { plan.X = True("AffectsRotationX"); plan.Y = True("AffectsRotationY"); plan.Z = True("AffectsRotationZ"); }
            if (kind == Kind.Aim) plan.AimAxis = settings.FindProperty("AimAxis")?.vector3Value ?? Vector3.forward;
            // Sixteen inline entries followed by an overflow list; entries
            // beyond totalLength are stale.
            var count = settings.FindProperty("Sources.totalLength")?.intValue ?? 0;
            for (var index = 0; index < count; index++)
            {
                var path = index < 16 ? "Sources.source" + index : "Sources.overflowList.Array.data[" + (index - 16) + "]";
                plan.Sources.Add((settings.FindProperty(path + ".SourceTransform")?.objectReferenceValue as Transform,
                    settings.FindProperty(path + ".Weight")?.floatValue ?? 0));
            }
            return plan;
        }
    }
}
