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
    // roll and aim with one source each. A parent constraint that fully follows
    // one source becomes a child of that source instead, which moves the same
    // way (props held in a hand). So does a position constraint that fully
    // follows one source, which then also turns with it (approximated).
    // An aim with a rotation offset, which VRM aims lack, turns an empty node
    // put in between the target and its parent instead (RelayAim).
    // The rest keep the current pose, as before,
    // and are listed in the result list. The originals stay on the copy
    // (UniVRM does not write them). Off constraints keep the pose too and are
    // listed; weightless ones have nothing to move. Lock is not read: it only
    // matters while editing, and VRChat drives an unlocked constraint as well.
    internal static class ConstraintExport
    {
        internal const string Category = "コンストレイント";
        internal const string KeptRotation = "VRMの回転コンストレイントに変換しました";
        internal const string KeptParent = "ソースの子に移し、ソースと一緒に動くようにしました";
        internal const string ApproximatedRotation = "VRMの回転コンストレイントで近似しました。基準の向きか強さが元と違うため、大きく動いたときの向きが元と少し異なります";
        internal const string ApproximatedRoll = "1軸だけの回転をVRMのロールコンストレイントで近似しました";
        internal const string ApproximatedPosition = "ソースの子に移し、ソースと一緒に動くようにしました。ソースが回ると一緒に回るため、向きと位置が元と異なる場合があります";
        internal const string ApproximatedAim = "VRMのエイムコンストレイントで近似しました。向ける軸は保ちますが、ねじれが元と異なる場合があります";
        internal const string OmittedKind = "VRMに同じ働きのコンストレイントがないため、今の姿勢のまま書き出しました";
        internal const string OmittedSetting = "VRMで表せない設定（複数のソース・自分自身がソース・2軸だけの回転・ワールドに固定・軸以外の向き）のため、今の姿勢のまま書き出しました";
        internal const string OmittedParent = "ソースと対象の親が違う回転（ワールド空間の回転と1軸だけの回転）はVRMで表せないため、今の姿勢のまま書き出しました";
        internal const string OmittedRest = "VRMで近似すると基準の向きの差で元より大きくずれるため、今の姿勢のまま書き出しました";
        internal const string OmittedOutside = "アバターのルート・アバターの外・非表示のオブジェクトを参照しているため、今の姿勢のまま書き出しました";
        internal const string OmittedDuplicate = "同じオブジェクトに別のコンストレイントが既にあるため、今の姿勢のまま書き出しました";
        internal const string OmittedCycle = "ソースのコンストレイントをたどるとこのオブジェクトに戻り、VRMはこの循環を書けないため、今の姿勢のまま書き出しました";
        internal const string OmittedOff = "オフになっているため、今の姿勢のまま書き出しました。アニメーションでオンにしてもVRMでは動きません";
        internal const string OmittedReparent = "ソースの子に移すと動きが変わる設定（一部の軸だけ・強さが1未満・ローカル空間・人型のボーンを含む・回転を写すコンストレイントのソースになっている・拡大縮小で形が崩れる）のため、今の姿勢のまま書き出しました";
        internal const string OmittedBound = "VRMの表情か一人称の設定がこの中のメッシュを使っていて、ソースの子に移すとその設定が外れるため、今の姿勢のまま書き出しました";
        internal const string OmittedToggle = "ソースの子に移すとメニューの表示切り替えが元と変わるため、今の姿勢のまま書き出しました";

        // A VRM rotation constraint applies the source's local change in the
        // target's own frame. It equals the original when both rest rotations
        // match; past 60 degrees it moves further off than keeping the pose.
        const float KeptRestAngle = 1f, MaximumRestAngle = 60f;
        // VRM aim turns the axis onto the source even at rest.
        const float MaximumAimAngle = 2f;
        internal const string RelayName = "VRVlog Aim";
        const string VrcPrefix = "VRC.SDK3.Dynamics.Constraint.Components.VRC";

        internal sealed class Result { internal int Sources, Kept, Approximated, Omitted; }

        enum Kind { Rotation, Aim, LookAt, Parent, Position, Scale }

        sealed class Plan
        {
            internal Component Component;
            internal Kind Kind;
            internal bool On, LocalSpace, FreezeToWorld, X = true, Y = true, Z = true, Positions = true;
            internal float Weight;
            internal Transform Target;
            internal Vector3 AimAxis = Vector3.forward;
            internal readonly List<(Transform Transform, float Weight)> Sources = new List<(Transform, float)>();
        }

        internal static Result Convert(GameObject source, GameObject copy, ICollection<string> warnings, ExportFallbackReport fallbacks = null,
            ObjectToggleCapture toggles = null, IEnumerable<Transform> animated = null)
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
            var moved = new List<Moved>();
            var relays = new Relays(copy, authored, plans.Where(p => p.Kind == Kind.Rotation).SelectMany(p => p.Sources.Select(s => s.Transform)),
                plans.Where(p => (p.Kind == Kind.Parent || p.Kind == Kind.Rotation) && p.On && p.Weight > 0).Select(p => p.Target)
                    .Concat(animated ?? Enumerable.Empty<Transform>()));
            var membership = new ToggleMembership(toggles, plans.Select(plan => plan.Target));
            var humanoid = plans.Any(p => p.Kind != Kind.Scale) ? HumanoidBones(copy) : new HashSet<Transform>();
            // Targets this pass aims; one may still follow a position constraint.
            var aimed = new HashSet<Transform>();
            var followed = new HashSet<Transform>();
            var (driven, turning) = Turning(copy, humanoid, plans, taken);
            var bound = BoundPaths(copy);
            var decisions = new List<(int Order, Plan Plan, ExportOutcome Outcome, string Reason)>();
            // Paths point at the user's avatar, so take them before any target moves.
            var paths = plans.Select(plan => ExportFallbackReport.RelativePath(copy.transform, plan.Target)).ToArray();
            try
            {
                // Moving a target under its source changes its local frame, which
                // VRM rotation constraints read, so parents go after all of them.
                foreach (var (plan, order) in plans.Select((plan, order) => (plan, order)).OrderBy(p => Follows(p.plan) ? 1 : 0))
                {
                    var sources = plan.Sources.Where(s => s.Transform != null && s.Weight > 0 && !float.IsInfinity(s.Weight)).ToArray();
                    // Weightless constraints, or ones without a source or target, have nothing to move.
                    if (!(plan.Weight > 0) || sources.Length == 0 || plan.Target == null) continue;
                    var (outcome, reason) = !plan.On ? (ExportOutcome.Omitted, OmittedOff)
                        : Follows(plan) ? Reparent(plan, sources, copy.transform, humanoid, bound, taken, edges, moved, aimed, followed, membership, paths[order], relays)
                        : Apply(plan, sources, copy.transform, driven, turning, taken, edges, added, humanoid, bound, relays);
                    // As a child, a position-constrained target also turns with the source.
                    if (plan.Kind == Kind.Position && outcome == ExportOutcome.Kept) (outcome, reason) = (ExportOutcome.Approximated, ApproximatedPosition);
                    if (outcome != ExportOutcome.Omitted)
                    {
                        taken.Add(plan.Target);
                        if (plan.Kind == Kind.Aim || plan.Kind == Kind.LookAt) aimed.Add(plan.Target);
                        if (Follows(plan)) followed.Add(plan.Target);
                        // The rotation may have moved its source up to the bone that
                        // turns, and an aim may sit on the node above the target.
                        if (!Follows(plan) && added.Last() is IVrm10Constraint last) AddEdge(edges, last.ConstraintTarget, last.ConstraintSource);
                    }
                    decisions.Add((order, plan, outcome, reason));
                }
                KeepAimsOnHeldSources(decisions, relays, edges);
            }
            catch
            {
                // The stage is omitted as a whole; never write a partial set.
                foreach (var component in added) if (component != null) UnityEngine.Object.DestroyImmediate(component);
                for (var index = moved.Count - 1; index >= 0; index--) moved[index].Restore();
                relays.Restore();
                throw;
            }
            membership.Apply();
            foreach (var (order, plan, outcome, reason) in decisions.OrderBy(d => d.Order))
            {
                result.Sources++;
                if (outcome == ExportOutcome.Omitted) result.Omitted++;
                else if (outcome == ExportOutcome.Kept) result.Kept++;
                else result.Approximated++;
                var entry = fallbacks?.Record(outcome, Category, plan.Target.name + " (" + plan.Component.GetType().Name + ")", reason);
                if (entry != null) entry.Path = paths[order];
            }
            if (added.Any(component => component != null) && copy.GetComponent<Vrm10Instance>() == null) copy.AddComponent<Vrm10Instance>();
            if (result.Sources > 0)
                warnings?.Add($"コンストレイント変換: 元{result.Sources}件 / 変換{result.Kept + result.Approximated}件（うち近似{result.Approximated}件） / 省略{result.Omitted}件。");
            return result;
        }

        // A relay turns its target towards a point that moves in the avatar.
        // Where the file leaves that point in place, because a constraint on
        // it or between it and the aim was not written, the aim would turn the
        // target the wrong way, so the target keeps its pose as before. A
        // target that stops turning holds what hangs below it in turn.
        static void KeepAimsOnHeldSources(List<(int Order, Plan Plan, ExportOutcome Outcome, string Reason)> decisions, Relays relays,
            Dictionary<Transform, List<Transform>> edges)
        {
            var omitted = decisions.Where(d => d.Outcome == ExportOutcome.Omitted && d.Reason != OmittedOff).ToArray();
            var held = new HashSet<Transform>(omitted.Select(d => d.Plan.Target));
            var stays = new HashSet<Transform>(omitted.Where(d => Follows(d.Plan)).Select(d => d.Plan.Target));
            for (var changed = true; changed;)
            {
                changed = false;
                for (var index = 0; index < decisions.Count; index++)
                {
                    var (order, plan, outcome, _) = decisions[index];
                    if (outcome == ExportOutcome.Omitted || plan.Kind != Kind.Aim && plan.Kind != Kind.LookAt) continue;
                    var node = relays.Above(plan.Target);
                    if (node == plan.Target || !node.TryGetComponent<Vrm10AimConstraint>(out var aim) || !Held(aim.Source, node, held, stays)) continue;
                    edges.Remove(node);
                    relays.Unwrap(plan.Target, aim);
                    decisions[index] = (order, plan, ExportOutcome.Omitted, OmittedRest);
                    held.Add(plan.Target);
                    changed = true;
                }
            }
        }

        // What the source shares with the aiming node moves both alike.
        static bool Held(Transform source, Transform node, HashSet<Transform> held, HashSet<Transform> stays)
        {
            if (stays.Contains(source) && !node.IsChildOf(source)) return true;
            for (var parent = source.parent; parent != null && !node.IsChildOf(parent); parent = parent.parent)
                if (held.Contains(parent)) return true;
            return false;
        }

        static (ExportOutcome, string) Apply(Plan plan, (Transform Transform, float Weight)[] sources, Transform root, HashSet<Transform> driven,
            HashSet<Transform> turning, HashSet<Transform> taken, Dictionary<Transform, List<Transform>> edges, List<Component> added,
            HashSet<Transform> humanoid, HashSet<string> bound, Relays relays)
        {
            if (plan.Kind == Kind.Scale) return (ExportOutcome.Omitted, OmittedKind);
            if (sources.Length != 1 || plan.FreezeToWorld) return (ExportOutcome.Omitted, OmittedSetting);
            var target = plan.Target;
            var source = sources[0].Transform;
            if (source == target) return (ExportOutcome.Omitted, OmittedSetting);
            if (!Inside(root, target) || !Inside(root, source)) return (ExportOutcome.Omitted, OmittedOutside);
            if (taken.Contains(target)) return (ExportOutcome.Omitted, OmittedDuplicate);
            // Beyond a loop of constraints, a rotation or roll reads the source's
            // local rotation, and an aim the source's world position and the
            // parent's world pose; any of them may lead back inside the target.
            var aims = plan.Kind != Kind.Rotation;
            if (Reaches(edges, source, target) || DependsOnInside(edges, source, aims ? Reading.Position : Reading.Rotation, target, false)
                || aims && DependsOnInside(edges, target.parent, Reading.Pose, target, false))
                return (ExportOutcome.Omitted, OmittedCycle);
            var weight = Mathf.Clamp01(plan.Weight * Mathf.Min(1, sources[0].Weight));
            if (plan.Kind == Kind.Rotation)
            {
                var axes = (plan.X ? 1 : 0) + (plan.Y ? 1 : 0) + (plan.Z ? 1 : 0);
                // Roll maps the delta through both rests in the shared parent
                // frame, even for a local-space source.
                if ((!plan.LocalSpace || axes == 1) && target.parent != source.parent)
                {
                    // A world-space rotation still matches when both ends hang
                    // from the same turning bone through bones that never turn:
                    // the source is then the nearest turning bone at or above it.
                    if (axes != 3 || plan.LocalSpace) return (ExportOutcome.Omitted, OmittedParent);
                    var moving = source;
                    while (moving != root && !turning.Contains(moving)) moving = moving.parent;
                    // Only a humanoid bone the app poses before any constraint can
                    // lead: VRM solves spring joints after constraints, and a target
                    // converted here may still be left out or moved, whatever the order.
                    if (moving == root || moving == target || !driven.Contains(moving)
                        || Frame(moving, root, turning) != Frame(target, root, turning))
                        return (ExportOutcome.Omitted, OmittedParent);
                    if (Reaches(edges, moving, target)) return (ExportOutcome.Omitted, OmittedCycle);
                    source = moving;
                }
                if (axes == 1)
                {
                    var roll = target.gameObject.AddComponent<Vrm10RollConstraint>(); added.Add(roll);
                    roll.Source = source; roll.Weight = weight;
                    roll.RollAxis = plan.X ? RollAxis.X : plan.Y ? RollAxis.Y : RollAxis.Z;
                    return (ExportOutcome.Approximated, ApproximatedRoll);
                }
                if (axes != 3) return (ExportOutcome.Omitted, OmittedSetting);
                // A world-space pair shares the frame it turns in, so the world
                // rotations compare; a local-space one compares as authored.
                var rest = plan.LocalSpace ? Quaternion.Angle(target.localRotation, source.localRotation) : Quaternion.Angle(target.rotation, source.rotation);
                if (rest >= MaximumRestAngle) return (ExportOutcome.Omitted, OmittedRest);
                var rotation = target.gameObject.AddComponent<Vrm10RotationConstraint>(); added.Add(rotation);
                rotation.Source = source; rotation.Weight = weight;
                return rest <= KeptRestAngle && weight == 1 ? (ExportOutcome.Kept, KeptRotation) : (ExportOutcome.Approximated, ApproximatedRotation);
            }
            // Aim and look-at: one direction towards the source position.
            if (!(plan.X && plan.Y && plan.Z) || !TryAxis(plan.AimAxis, out var axis, out var vector)) return (ExportOutcome.Omitted, OmittedSetting);
            var toSource = source.position - target.position;
            if (toSource.sqrMagnitude < 1e-10f) return (ExportOutcome.Omitted, OmittedRest);
            // An offset (Unity's Activate keeps the pose with one) leaves the
            // axis off the source; the relay node points it there instead.
            var node = target;
            if (Vector3.Angle(target.rotation * vector, toSource) > MaximumAimAngle)
            {
                node = relays.Insert(target, vector, toSource, humanoid, bound);
                if (node == null) return (ExportOutcome.Omitted, OmittedRest);
                turning.Add(node);
            }
            var aim = node.gameObject.AddComponent<Vrm10AimConstraint>(); added.Add(aim);
            aim.Source = source; aim.Weight = weight; aim.AimAxis = axis;
            return (ExportOutcome.Approximated, ApproximatedAim);
        }

        // A parent constraint at full weight on every axis keeps the target at a
        // fixed offset from its source; as a child of the source the target
        // stays at the same offset (VRM has no parent constraint). The current
        // pose is the solved one: VRChat and Unity constraints run in the editor.
        // A position constraint moves the same way; an aim this pass wrote on
        // the same object still turns it, as VRM aims by the world direction.
        static (ExportOutcome, string) Reparent(Plan plan, (Transform Transform, float Weight)[] sources, Transform root, HashSet<Transform> humanoid,
            HashSet<string> bound, HashSet<Transform> taken, Dictionary<Transform, List<Transform>> edges, List<Moved> moved, HashSet<Transform> aimed,
            HashSet<Transform> followed, ToggleMembership membership, string userPath, Relays relays)
        {
            if (sources.Length != 1 || plan.FreezeToWorld) return (ExportOutcome.Omitted, OmittedSetting);
            var target = plan.Target;
            // The node that moves: the target, or the relay node above it, which
            // carries it and whose aim then keeps turning it.
            var node = relays.Above(target);
            var source = sources[0].Transform;
            // A source inside the target would follow itself.
            if (source.IsChildOf(node)) return (ExportOutcome.Omitted, OmittedSetting);
            if (!Inside(root, node) || !Inside(root, source)) return (ExportOutcome.Omitted, OmittedOutside);
            var aiming = plan.Kind == Kind.Position && aimed.Contains(target) && !followed.Contains(target);
            if (taken.Contains(target) && !aiming) return (ExportOutcome.Omitted, OmittedDuplicate);
            // Moving a humanoid bone would change the skeleton, and a rotation
            // or roll reads the local rotation of its source. An aim reads the
            // world position, which the move keeps; a loop through it is the
            // cycle check's.
            if (!(plan.X && plan.Y && plan.Z && plan.Positions) || plan.LocalSpace || plan.Weight * Mathf.Min(1, sources[0].Weight) < FullWeight ||
                humanoid.Any(bone => bone.IsChildOf(node)) || edges.ContainsKey(node) && !aiming ||
                edges.Any(edge => edge.Value.Contains(target) && !edge.Key.TryGetComponent<Vrm10AimConstraint>(out _)))
                return (ExportOutcome.Omitted, OmittedReparent);
            // If the source's pose depends on something inside the target, the
            // target would, once it follows, move with itself.
            if (DependsOnInside(edges, source, Reading.Pose, node, true)) return (ExportOutcome.Omitted, OmittedCycle);
            if (node.parent == source) return (ExportOutcome.Kept, KeptParent);
            // UniVRM finds expression and first-person renderers by these paths
            // when it writes the file; a moved target would leave them dangling.
            var path = ExportFallbackReport.RelativePath(root, node);
            if (bound.Any(p => p == path || p.StartsWith(path + "/", StringComparison.Ordinal))) return (ExportOutcome.Omitted, OmittedBound);
            if (!membership.Allows(target, source, out var leader)) return (ExportOutcome.Omitted, OmittedToggle);
            var before = new Moved(node);
            try { ExportAttachmentSession.ReparentPreservingPose(node, source); }
            catch (InvalidOperationException) { return (ExportOutcome.Omitted, OmittedReparent); }
            moved.Add(before);
            membership.Moved(target, leader, userPath);
            return (ExportOutcome.Kept, KeptParent);
        }

        const float FullWeight = .999f;

        // Parent and position constraints follow by moving under the source.
        static bool Follows(Plan plan) => plan.Kind == Kind.Parent || plan.Kind == Kind.Position;

        // The menu toggles were turned on for the export, and the app hides a
        // toggled object by its own node. A target moved out of one would stay
        // visible, so it follows that toggle. Membership is taken from the
        // hierarchy before anything moves, and a moved target counts as inside
        // the toggle it follows, so the order of the moves does not matter.
        sealed class ToggleMembership
        {
            readonly ObjectToggleCapture toggles;
            readonly Transform[] switched;
            readonly Dictionary<Transform, Transform[]> original = new Dictionary<Transform, Transform[]>();
            readonly Dictionary<Transform, Transform> leaders = new Dictionary<Transform, Transform>();
            readonly List<(Transform Target, Transform Leader, string Path)> following = new List<(Transform, Transform, string)>();

            internal ToggleMembership(ObjectToggleCapture toggles, IEnumerable<Transform> targets)
            {
                this.toggles = toggles;
                switched = toggles?.Targets.Where(t => t != null).Select(t => t.transform).ToArray() ?? Array.Empty<Transform>();
                foreach (var target in targets)
                    if (target != null && !original.ContainsKey(target)) original.Add(target, Around(target));
            }

            // The toggles that show and hide the value now.
            Transform[] Around(Transform value)
            {
                var result = switched.Where(value.IsChildOf).ToList();
                for (var at = value; at != null; at = at.parent)
                    if (leaders.TryGetValue(at, out var leader)) result.Add(leader);
                return result.Distinct().ToArray();
            }

            // Whether the toggles show and hide the target as before once it is
            // under the source: it may join a toggle (leader) it would leave,
            // but not one it was never in, nor two at once, nor a second one.
            // A target without a mesh shows nothing either way.
            internal bool Allows(Transform target, Transform source, out Transform leader)
            {
                leader = null;
                if (switched.Length == 0 || !target.GetComponentsInChildren<Renderer>(true).Any(Shows))
                    return true;
                var before = original[target];
                var after = Around(source).Concat(switched.Contains(target) ? new[] { target } : Array.Empty<Transform>()).ToArray();
                if (after.Except(before).Any()) return false;
                var missing = before.Except(after).ToArray();
                if (missing.Length == 0) return true;
                if (missing.Length > 1 || switched.Contains(target) ||
                    !toggles.HasRoom(following.Select(f => f.Leader.gameObject).Append(missing[0].gameObject).ToArray()))
                    return false;
                leader = missing[0];
                return true;
            }

            static bool Shows(Renderer renderer) =>
                renderer is SkinnedMeshRenderer skin ? skin.sharedMesh != null :
                renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null;

            internal void Moved(Transform target, Transform leader, string path)
            {
                if (leader == null) return;
                leaders[target] = leader;
                following.Add((target, leader, path));
            }

            // After every move succeeded, so a failure only has to move targets back.
            internal void Apply()
            {
                foreach (var (target, leader, path) in following) toggles.Follow(target.gameObject, leader.gameObject, path);
            }
        }

        static HashSet<string> BoundPaths(GameObject copy)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var settings = copy.TryGetComponent<Vrm10Instance>(out var instance) ? instance.Vrm : null;
            if (settings == null) return paths;
            if (settings.Expression != null)
                foreach (var entry in settings.Expression.Clips)
                    if (entry.Clip != null)
                        foreach (var binding in entry.Clip.MorphTargetBindings) if (binding.RelativePath != null) paths.Add(binding.RelativePath);
            if (settings.FirstPerson != null)
                foreach (var flag in settings.FirstPerson.Renderers) if (flag.Renderer != null) paths.Add(flag.Renderer);
            return paths;
        }

        sealed class Moved
        {
            readonly Transform target, parent;
            readonly int sibling;
            readonly Vector3 position, scale;
            readonly Quaternion rotation;

            internal Moved(Transform target)
            {
                this.target = target; parent = target.parent; sibling = target.GetSiblingIndex();
                position = target.localPosition; rotation = target.localRotation; scale = target.localScale;
            }

            internal void Restore()
            {
                if (target == null) return;
                target.SetParent(parent, false); target.SetSiblingIndex(sibling);
                target.localPosition = position; target.localRotation = rotation; target.localScale = scale;
            }
        }

        // A VRM aim has no offset: it turns its axis straight onto the source,
        // even at rest. For a target whose axis is off the source, an empty
        // node at the same place under the same parent takes the aim, with the
        // axis on the source, and carries the target, which keeps its pose.
        // Turning the node by the shortest arc turns the target the same way.
        sealed class Relays
        {
            readonly Transform root;
            readonly HashSet<Transform> rotationSources, framed;
            readonly Dictionary<Transform, Transform> above = new Dictionary<Transform, Transform>();
            readonly List<(Transform Target, Transform Node, Transform Parent, int Sibling, Vector3 Position, Quaternion Rotation)> inserted =
                new List<(Transform, Transform, Transform, int, Vector3, Quaternion)>();
            HashSet<string> names;

            // A rotation or roll reads its source's local rotation, which the
            // node would take over from the target. Framed targets keep their
            // parent: a parent or rotation constraint of their own would lose
            // the target to the aim, and expression tracks hold local values.
            internal Relays(GameObject copy, IEnumerable<IVrm10Constraint> authored, IEnumerable<Transform> rotationSources, IEnumerable<Transform> framed)
            {
                root = copy.transform;
                this.framed = new HashSet<Transform>(framed.Where(t => t != null));
                this.rotationSources = new HashSet<Transform>(rotationSources.Concat(authored.Where(c => !(c is Vrm10AimConstraint))
                    .Select(c => c.ConstraintSource)).Where(t => t != null));
            }

            internal Transform Above(Transform target) => above.TryGetValue(target, out var node) ? node : target;

            // The node, or null where one would change more than the aim: in the
            // skeleton, in a spring chain, under a rotation that reads the target,
            // or for VRM settings that find meshes inside it by path.
            internal Transform Insert(Transform target, Vector3 axis, Vector3 toSource, HashSet<Transform> humanoid, HashSet<string> bound)
            {
                var path = ExportFallbackReport.RelativePath(root, target);
                if (humanoid.Any(bone => bone.IsChildOf(target)) || target.TryGetComponent<VRM10SpringBoneJoint>(out _) || framed.Contains(target) ||
                    rotationSources.Any(source => source.IsChildOf(target)) ||
                    bound.Any(p => p == path || p.StartsWith(path + "/", StringComparison.Ordinal)))
                    return null;
                names ??= new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => t.name), StringComparer.Ordinal);
                var parent = target.parent;
                var world = target.localToWorldMatrix;
                var rotation = target.localRotation;
                // Unit scale, so the target's own scale stays a plain TRS below it.
                var turn = Quaternion.Inverse(parent.rotation) * Quaternion.FromToRotation(target.rotation * axis, toSource) * target.rotation;
                var node = new GameObject(PhysBoneSpringExport.UniqueName(names, target.name + "-" + RelayName)).transform;
                node.gameObject.layer = target.gameObject.layer;
                node.SetParent(parent, false);
                node.SetSiblingIndex(target.GetSiblingIndex());
                node.localPosition = target.localPosition; node.localRotation = turn; node.localScale = Vector3.one;
                inserted.Add((target, node, parent, target.GetSiblingIndex(), target.localPosition, rotation));
                target.SetParent(node, false);
                target.localPosition = Vector3.zero; target.localRotation = Quaternion.Inverse(turn) * rotation;
                above[target] = node;
                if (Vector3.Angle(node.rotation * axis, toSource) <= MaximumAimAngle && Same(world, target.localToWorldMatrix)) return node;
                Undo(inserted.Count - 1);
                return null;
            }

            static bool Same(Matrix4x4 a, Matrix4x4 b)
            {
                for (var index = 0; index < 16; index++)
                    if (Mathf.Abs(a[index] - b[index]) > 1e-4f * Mathf.Max(1, Mathf.Abs(a[index]))) return false;
                return true;
            }

            void Undo(int index)
            {
                var (target, node, parent, sibling, position, rotation) = inserted[index];
                inserted.RemoveAt(index);
                if (target != null)
                {
                    above.Remove(target);
                    target.SetParent(parent, false); target.SetSiblingIndex(sibling);
                    target.localPosition = position; target.localRotation = rotation;
                }
                if (node != null) UnityEngine.Object.DestroyImmediate(node.gameObject);
            }

            // Drops the aim and takes the node out where it now stands, the
            // target keeping its pose. A move may have scaled the node; where
            // that does not fold into the target, the node stays, empty.
            internal void Unwrap(Transform target, Vrm10AimConstraint aim)
            {
                UnityEngine.Object.DestroyImmediate(aim);
                var node = above[target];
                var world = target.localToWorldMatrix;
                var (position, rotation, scale) = (target.localPosition, target.localRotation, target.localScale);
                var sibling = node.GetSiblingIndex();
                target.SetParent(node.parent, false); target.SetSiblingIndex(sibling);
                target.localPosition = node.localPosition + node.localRotation * Vector3.Scale(node.localScale, position);
                target.localRotation = node.localRotation * rotation;
                target.localScale = Vector3.Scale(node.localScale, scale);
                if (!Same(world, target.localToWorldMatrix))
                {
                    target.SetParent(node, false);
                    target.localPosition = position; target.localRotation = rotation; target.localScale = scale;
                    return;
                }
                above.Remove(target);
                inserted.RemoveAll(entry => entry.Target == target);
                UnityEngine.Object.DestroyImmediate(node.gameObject);
            }

            internal void Restore()
            {
                for (var index = inserted.Count - 1; index >= 0; index--) Undo(index);
            }
        }

        internal static HashSet<Transform> HumanoidBones(GameObject copy)
        {
            var bones = new HashSet<Transform>();
            var animator = copy.GetComponent<Animator>();
            for (var index = 0; index < (int)HumanBodyBones.LastBone; index++)
            {
                // An Animator that is not bound yet still knows its bones by name. Without
                // them the body would swing and constraints would drive it, with no warning.
                var bone = VRVlog.LilToonExporter.HumanoidBones.Resolve(animator, (HumanBodyBones)index);
                if (bone != null) bones.Add(bone);
            }
            return bones;
        }

        // Bones whose local rotation may change in the app: the skeleton
        // (driven, unless it also swings or is constrained), spring joints and
        // any constraint target. Any other bone only carries its parent.
        static (HashSet<Transform> Driven, HashSet<Transform> Turning) Turning(GameObject copy, HashSet<Transform> humanoid, IEnumerable<Plan> plans,
            IEnumerable<Transform> authored)
        {
            var others = new HashSet<Transform>(copy.GetComponentsInChildren<VRM10SpringBoneJoint>(true).Select(joint => joint.transform));
            others.UnionWith(authored);
            others.UnionWith(plans.Select(plan => plan.Target).Where(target => target != null));
            var driven = new HashSet<Transform>(humanoid);
            driven.ExceptWith(others);
            var turning = new HashSet<Transform>(humanoid);
            turning.UnionWith(others);
            return (driven, turning);
        }

        // The nearest turning bone above, or the root: the frame the bone turns in.
        static Transform Frame(Transform value, Transform root, HashSet<Transform> turning)
        {
            var parent = value.parent;
            while (parent != root && !turning.Contains(parent)) parent = parent.parent;
            return parent;
        }

        // UniVRM does not write the root as a node, so it can be neither end.
        static bool Inside(Transform root, Transform value) =>
            value != null && value != root && value.IsChildOf(root) && value.gameObject.activeInHierarchy;

        static void AddEdge(Dictionary<Transform, List<Transform>> edges, Transform target, Transform source)
        {
            if (!edges.TryGetValue(target, out var sources)) edges.Add(target, sources = new List<Transform>());
            sources.Add(source);
        }

        // What is read of a node: its local rotation (a rotation or roll reads
        // it of its source), its world position (an aim reads it of its source),
        // or its whole world pose (a follow takes it of its source).
        enum Reading { Rotation, Position, Pose }

        // Whether what is read of `from` depends on something inside the target,
        // which a converted rotation or aim turns, and a follow (`moves`) also
        // moves. A world pose comes from the parent's and from the node's own
        // local rotation; a world position only from the parent's pose. A local
        // rotation changes only by the node's own VRM constraint: a rotation or
        // roll reads its source's local rotation; an aim reads its source's world
        // position and turns its node by the world direction, so it also takes
        // the parent's pose.
        static bool DependsOnInside(Dictionary<Transform, List<Transform>> edges, Transform from, Reading reading, Transform target, bool moves)
        {
            var visited = new HashSet<(Transform, Reading)>();
            var pending = new Stack<(Transform Node, Reading Reading)>();
            pending.Push((from, reading));
            while (pending.Count > 0)
            {
                var (current, read) = pending.Pop();
                if (current == null || !visited.Add((current, read))) continue;
                if (read == Reading.Pose)
                {
                    if (current.IsChildOf(target)) return true;
                    pending.Push((current.parent, Reading.Pose));
                    pending.Push((current, Reading.Rotation));
                }
                else if (read == Reading.Position)
                {
                    // Turning the target does not move its own origin.
                    if (current.IsChildOf(target) && (moves || current != target)) return true;
                    pending.Push((current.parent, Reading.Pose));
                }
                else
                {
                    if (current == target) return true;
                    var aim = current.TryGetComponent<Vrm10AimConstraint>(out _);
                    if (aim) pending.Push((current.parent, Reading.Pose));
                    if (edges.TryGetValue(current, out var next))
                        foreach (var value in next) pending.Push((value, aim ? Reading.Position : Reading.Rotation));
                }
            }
            return false;
        }

        // VRM forbids constraints that read each other in a loop.
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
                // Lock only decides whether moving the target in the editor re-measures the offsets; in Play Mode,
                // in VRChat and in the app an active constraint drives its target either way, so Lock is not read.
                On = behaviour.isActiveAndEnabled && constraint.constraintActive };
            var axes = Axis.X | Axis.Y | Axis.Z;
            switch (component)
            {
                case RotationConstraint rotation: plan.Kind = Kind.Rotation; axes = rotation.rotationAxis; break;
                case AimConstraint aim: plan.Kind = Kind.Aim; axes = aim.rotationAxis; plan.AimAxis = aim.aimVector; break;
                case LookAtConstraint _: plan.Kind = Kind.LookAt; break;
                case ParentConstraint parent:
                    plan.Kind = Kind.Parent; axes = parent.rotationAxis;
                    plan.Positions = parent.translationAxis == (Axis.X | Axis.Y | Axis.Z); break;
                case PositionConstraint position: plan.Kind = Kind.Position; axes = position.translationAxis; break;
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
                Weight = settings.FindProperty("GlobalWeight")?.floatValue ?? 0,
                LocalSpace = True("SolveInLocalSpace"), FreezeToWorld = True("FreezeToWorld"),
                Target = settings.FindProperty("TargetTransform")?.objectReferenceValue as Transform ?? component.transform,
            };
            if (kind == Kind.Rotation || kind == Kind.Aim || kind == Kind.Parent)
            { plan.X = True("AffectsRotationX"); plan.Y = True("AffectsRotationY"); plan.Z = True("AffectsRotationZ"); }
            if (kind == Kind.Position) { plan.X = True("AffectsPositionX"); plan.Y = True("AffectsPositionY"); plan.Z = True("AffectsPositionZ"); }
            if (kind == Kind.Parent) plan.Positions = True("AffectsPositionX") && True("AffectsPositionY") && True("AffectsPositionZ");
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
