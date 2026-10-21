using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    internal sealed partial class ExperimentalExpressionCaptureSession
    {
        internal sealed class ClipInput
        {
            internal AnimationClip Clip;
            internal string Name;
            internal float Time;
            internal string Error;
        }

        internal sealed class ClipRecommendation
        {
            internal AnimationClip Clip;
            internal string Source;
        }

        [Serializable]
        internal sealed class PoseReference
        {
            public string Guid, Name, Category;
            public long LocalId;
            public float Time;
        }

        [Serializable]
        internal sealed class PoseNameReference { public string Id, Name; }

        readonly Dictionary<AnimationClip, HashSet<string>> clipRoots = new Dictionary<AnimationClip, HashSet<string>>();
        // Clips a gesture enters in any of the avatar's controllers. The gesture route exports them even
        // when they change nothing, so the board keeps them.
        readonly HashSet<AnimationClip> gestureClips = new HashSet<AnimationClip>();

        void CaptureClipRoots()
        {
            var walked = new HashSet<RuntimeAnimatorController>();
            void Add(RuntimeAnimatorController controller, string root)
            {
                if (controller == null) return;
                if (walked.Add(controller))
                {
                    // Nothing here may stop the board from opening. A controller that cannot be walked keeps
                    // all its clips shown.
                    try { gestureClips.UnionWith(VrChatGestureExpressions.TargetClips(controller)); }
                    catch (Exception) { gestureClips.UnionWith(controller.animationClips.Where(clip => clip != null)); }
                }
                foreach (var clip in controller.animationClips.Where(clip => clip != null))
                {
                    if (!clipRoots.TryGetValue(clip, out var roots)) clipRoots.Add(clip, roots = new HashSet<string>(StringComparer.Ordinal));
                    roots.Add(root);
                }
            }
            var metadata = VrChatExpressionMenu.Read(Source, new VrChatMenuImportPolicy { SkipAll = true });
            foreach (var controller in metadata.OtherControllers.Concat(new[] { metadata.Controller })) Add(controller, "");
            foreach (var animator in Source.GetComponentsInChildren<Animator>(true))
                Add(animator.runtimeAnimatorController, AnimationUtility.CalculateTransformPath(animator.transform, Source.transform));
            foreach (var component in Source.GetComponentsInChildren<Component>(true).Where(component => component != null && !(component is Animator)))
            {
                using var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || !(property.objectReferenceValue is RuntimeAnimatorController controller)) continue;
                    if (component.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") continue;
                    var root = (string)null;
                    if (component.GetType().FullName == "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator")
                    {
                        var mode = component.GetType().GetField("pathMode").GetValue(component).ToString();
                        if (mode == "Absolute") root = "";
                        else
                        {
                            var reference = component.GetType().GetField("relativePathRoot").GetValue(component);
                            var owner = reference?.GetType().GetMethod("Get", new[] { typeof(Component) })?.Invoke(reference, new object[] { Source.transform }) as GameObject;
                            root = AnimationUtility.CalculateTransformPath((owner != null ? owner : component.gameObject).transform, Source.transform);
                        }
                    }
                    // Unknown plugin semantics are not safely avatar-relative.
                    Add(controller, root);
                }
            }
        }

        string ClipRoot(AnimationClip clip)
        {
            if (!clipRoots.TryGetValue(clip, out var roots)) return ""; // Explicit, unregistered .anim uses avatar-relative paths.
            if (roots.Count != 1 || roots.Contains(null))
                throw new InvalidOperationException(ExporterLocalization.T("表情クリップの適用元を一意に確定できません。Controllerの登録先と相対パスを確認してください: ") + clip.name);
            return roots.Single();
        }

        internal List<ClipRecommendation> RecommendClips()
        {
            RequireValid();
            var result = new List<ClipRecommendation>();
            var seen = new HashSet<AnimationClip>();
            void Add(IEnumerable<AnimationClip> clips, string origin)
            {
                foreach (var clip in clips.Where(clip => clip != null))
                    if (seen.Add(clip) && AnimationUtility.GetCurveBindings(clip).Any(binding =>
                        binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)))
                        result.Add(new ClipRecommendation { Clip = clip, Source = origin });
            }
            Add(faceEmoBindings.RegisteredClips, "FaceEmoの登録");
            var metadata = VrChatExpressionMenu.Read(Source, new VrChatMenuImportPolicy { SkipAll = true });
            var controllers = metadata.OtherControllers.Concat(new[] { metadata.Controller })
                .Concat(Source.GetComponentsInChildren<Animator>(true).Select(animator => animator.runtimeAnimatorController))
                .Where(controller => controller != null).ToHashSet();
            // MergeAnimator and similar authoring components can contribute
            // their own controller before NDMF combines the avatar's FX.
            foreach (var component in Source.GetComponentsInChildren<Component>(true).Where(component => component != null))
            {
                using var serialized = new SerializedObject(component);
                var properties = serialized.GetIterator();
                while (properties.Next(true))
                    if (properties.propertyType == SerializedPropertyType.ObjectReference && properties.objectReferenceValue is RuntimeAnimatorController controller)
                        controllers.Add(controller);
            }
            foreach (var controller in controllers) Add(controller.animationClips, "アバターに登録されたController");
            return result.OrderBy(item => item.Clip.name, StringComparer.Ordinal).ToList();
        }

        // The VRChat Expressions Menu paths ("Face / Smile") of the Toggle and
        // Button items that select each clip in the FX controller, in menu
        // order. The board reads emotions from them as the quick export reads
        // its menu faces. Nothing here may stop the board from opening.
        // overLimit is the menu's count when it is over the read limit; the
        // board then says why no emotion comes from the menu item names.
        internal static Dictionary<AnimationClip, List<string>> MenuClipPaths(GameObject avatar, out VrChatMenuImportSummary overLimit) =>
            MenuClipPaths(() => VrChatExpressionMenu.Read(avatar), out overLimit);

        internal static Dictionary<AnimationClip, List<string>> MenuClipPaths(Func<VrChatExpressionMenu.Source> read, out VrChatMenuImportSummary overLimit) =>
            ReadMenuLinks(read, out overLimit).Paths;

        // MenuClipPaths and MenuEntriesOutsideBoard from one menu read. A menu
        // that cannot be read gives no paths and a null Outside (unreadable,
        // not "no items"); over the read limit it also sets overLimit. The
        // record route still drops the menu's faces then, so the band says the
        // items cannot be checked rather than nothing.
        internal static (Dictionary<AnimationClip, List<string>> Paths, List<string> Outside) ReadMenuLinks(Func<VrChatExpressionMenu.Source> read,
            out VrChatMenuImportSummary overLimit)
        {
            overLimit = null;
            try
            {
                var menu = read();
                return MenuLinks(menu.Entries, menu.Controller, menu.Defaults);
            }
            catch (VrChatMenuImportLimitException limit) { overLimit = limit.Summary; return (new Dictionary<AnimationClip, List<string>>(), null); }
            catch (Exception) { return (new Dictionary<AnimationClip, List<string>>(), null); }
        }

        // The note beside the emotion slots when the menu was over the read
        // limit, null otherwise. Over 256 items the count is the menu's own
        // (a lower bound when counting stopped); other limits are submenus.
        internal const string MenuOverLimitItemsFormat = "項目名から感情を選んでいません・表情メニューが{0}件で上限の{1}件を超えています";
        internal const string MenuOverLimitItemsAtLeastFormat = "項目名から感情を選んでいません・表情メニューが{0}件以上で上限の{1}件を超えています";
        internal const string MenuOverLimitSubmenus = "項目名から感情を選んでいません・表情メニューのサブメニューが多く上限を超えています";

        internal static string MenuOverLimitNote(VrChatMenuImportSummary summary)
        {
            if (summary == null) return null;
            if (summary.LimitReason != "candidates") return ExporterLocalization.T(MenuOverLimitSubmenus);
            return string.Format(ExporterLocalization.T(summary.IsComplete ? MenuOverLimitItemsFormat : MenuOverLimitItemsAtLeastFormat),
                summary.CandidateCount, VrChatExpressionMenu.MaximumCandidates);
        }

        // A clip is linked to an item only when the item alone decides it: a
        // state playing the clip directly is entered by a transition (any
        // state, state or entry) that no condition makes false under the menu
        // defaults overlaid with the item's fixed values, and that some
        // condition on the item's parameter makes false when only that
        // parameter goes back to its default (off, released). An item that
        // selects several clips (a face split over Eyes/Mouth/Brow layers) is
        // linked to none: each clip is only part of the face. Blend Trees and
        // sub-machine destinations are not followed.
        internal static Dictionary<AnimationClip, List<string>> MenuClipPaths(IEnumerable<VrChatExpressionMenu.Entry> entries,
            RuntimeAnimatorController fx, IDictionary<string, float> defaults) => MenuLinks(entries, fx, defaults).Paths;

        // The menu paths, in menu order, of the Toggle and Button items that
        // move the face but are linked to no single clip: the item selects
        // several face destinations (a face split over layers), or a Blend
        // Tree or sub-machine that moves the face. The record route records
        // only the board, which has no item for them. An item linked to one
        // clip and an item that moves no BlendShape (clothing) are not listed.
        internal static List<string> MenuEntriesOutsideBoard(IEnumerable<VrChatExpressionMenu.Entry> entries,
            RuntimeAnimatorController fx, IDictionary<string, float> defaults) => MenuLinks(entries, fx, defaults).Outside;

        static bool MovesFace(AnimationClip clip) => clip != null && AnimationUtility.GetCurveBindings(clip).Any(binding =>
            binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal));

        // MenuClipPaths and MenuEntriesOutsideBoard from one walk of the FX
        // controller, with the same on/off rule for both.
        internal static (Dictionary<AnimationClip, List<string>> Paths, List<string> Outside) MenuLinks(IEnumerable<VrChatExpressionMenu.Entry> entries,
            RuntimeAnimatorController fx, IDictionary<string, float> defaults)
        {
            var result = new Dictionary<AnimationClip, List<string>>();
            var outside = new List<string>();
            if (entries == null || fx == null) return (result, outside);
            var overrides = ExpressionDependencies.Overrides(fx);
            AnimationClip Replaced(AnimationClip clip) => overrides.TryGetValue(clip, out var replaced) ? replaced : clip;
            bool TreeMovesFace(Motion motion, HashSet<Motion> seen)
            {
                if (motion is AnimationClip clip) return MovesFace(Replaced(clip));
                return motion is BlendTree tree && seen.Add(tree) && tree.children.Any(child => TreeMovesFace(child.motion, seen));
            }
            bool MachineMovesFace(AnimatorStateMachine machine, HashSet<AnimatorStateMachine> seen) =>
                machine != null && seen.Add(machine) && (machine.states.Any(child => TreeMovesFace(child.state?.motion, new HashSet<Motion>())) ||
                    machine.stateMachines.Any(child => MachineMovesFace(child.stateMachine, seen)));
            var links = new List<(AnimatorTransitionBase Transition, AnimationClip Clip)>();
            // Destinations that move the face; Single is false for a Blend Tree or a sub-machine.
            var faces = new List<(AnimatorTransitionBase Transition, UnityEngine.Object Face, bool Single)>();
            void Link(AnimatorTransitionBase transition)
            {
                if (transition == null) return;
                if (transition.destinationState?.motion is AnimationClip clip)
                {
                    links.Add((transition, Replaced(clip)));
                    if (MovesFace(Replaced(clip))) faces.Add((transition, Replaced(clip), true));
                }
                else if (transition.destinationState?.motion is BlendTree tree)
                {
                    if (TreeMovesFace(tree, new HashSet<Motion>())) faces.Add((transition, tree, false));
                }
                else if (transition.destinationState == null && transition.destinationStateMachine != null &&
                    MachineMovesFace(transition.destinationStateMachine, new HashSet<AnimatorStateMachine>()))
                    faces.Add((transition, transition.destinationStateMachine, false));
            }
            void Walk(AnimatorStateMachine machine, HashSet<AnimatorStateMachine> seen)
            {
                if (machine == null || !seen.Add(machine)) return;
                foreach (var transition in machine.anyStateTransitions) Link(transition);
                foreach (var transition in machine.entryTransitions) Link(transition);
                foreach (var child in machine.states)
                    foreach (var transition in child.state?.transitions ?? Array.Empty<AnimatorStateTransition>()) Link(transition);
                foreach (var child in machine.stateMachines)
                {
                    foreach (var transition in machine.GetStateMachineTransitions(child.stateMachine)) Link(transition);
                    Walk(child.stateMachine, seen);
                }
            }
            var seenMachines = new HashSet<AnimatorStateMachine>();
            foreach (var layer in ExpressionDependencies.Controller(fx).layers) Walk(layer.stateMachine, seenMachines);
            var items = new List<(VrChatExpressionMenu.Entry Entry, string Parameter, float Value, Func<AnimatorTransitionBase, bool> Selects)>();
            foreach (var entry in entries)
            {
                if (entry == null || entry.Error != null || entry.ControlType != "Toggle" && entry.ControlType != "Button" ||
                    string.IsNullOrEmpty(entry.ControlParameter) || !entry.Parameters.ContainsKey(entry.ControlParameter)) continue;
                var parameter = entry.ControlParameter;
                var on = new Dictionary<string, float>(StringComparer.Ordinal);
                if (defaults != null) foreach (var pair in defaults) on[pair.Key] = pair.Value;
                foreach (var pair in entry.Parameters) on[pair.Key] = pair.Value;
                var off = new Dictionary<string, float>(on, StringComparer.Ordinal)
                    { [parameter] = defaults != null && defaults.TryGetValue(parameter, out var value) ? value : 0f };
                items.Add((entry, parameter, entry.Parameters[parameter], transition =>
                {
                    var own = transition.conditions.Where(condition => condition.parameter == parameter).ToArray();
                    // Off at the default: some condition on the parameter is false there.
                    return own.Length != 0 && !ExpressionDependencies.IsFalse(transition, on) && ExpressionDependencies.IsFalse(transition, off);
                }));
            }
            // A face destination every item of a parameter selects, when the
            // parameter has items for 2 or more values, is shared (a reset
            // layer entered by Face NotEqual 0): it would give every item a
            // second face. Only the board-less count ignores it.
            var shared = new HashSet<(string Parameter, int Face)>();
            foreach (var group in items.GroupBy(item => item.Parameter, StringComparer.Ordinal))
            {
                if (group.Select(item => item.Value).Distinct().Count() < 2) continue;
                for (var index = 0; index < faces.Count; index++)
                    if (group.All(item => item.Selects(faces[index].Transition))) shared.Add((group.Key, index));
            }
            foreach (var (entry, parameter, _, Selects) in items)
            {
                var selected = new HashSet<AnimationClip>();
                foreach (var link in links)
                    if (Selects(link.Transition)) selected.Add(link.Clip);
                if (selected.Count == 1)
                {
                    var clip = selected.Single();
                    if (!result.TryGetValue(clip, out var paths)) result.Add(clip, paths = new List<string>());
                    if (!paths.Contains(entry.Name)) paths.Add(entry.Name);
                    continue;
                }
                // An item linked to one clip has returned above, even when the
                // same item also enters a Blend Tree face (a mixed item): the
                // clip is on the board, so it is not counted here.
                var moved = faces.Where((face, index) => Selects(face.Transition) && !shared.Contains((parameter, index))).ToArray();
                if ((moved.Select(face => face.Face).Distinct().Count() > 1 || moved.Any(face => !face.Single)) && !outside.Contains(entry.Name))
                    outside.Add(entry.Name);
            }
            return (result, outside);
        }

        internal string ClipError(AnimationClip clip, float time)
        {
            try { return NoFaceError(clip, BuildClipPose(clip, time, "プレビュー")); }
            catch (InvalidOperationException error) { return error.Message; }
        }

        internal Dictionary<AnimationClip, string> ClipErrors(IEnumerable<AnimationClip> clips)
        {
            RequireValid(); // One geometry/fingerprint check for the complete synchronous batch.
            var result = new Dictionary<AnimationClip, string>();
            foreach (var clip in clips.Where(clip => clip != null).Distinct())
            {
                try { result.Add(clip, NoFaceError(clip, BuildClipPoseUnchecked(clip, clip.length, "プレビュー"))); }
                catch (InvalidOperationException error) { result.Add(clip, error.Message); }
            }
            return result;
        }

        internal const string NoFaceChange = "元から0のBlendShapeを0にするだけで、顔を何も変えないアニメーションです。";

        // Lip-sync and blink switches in the avatar's controllers hold mouth or eyelid shapes at the 0 they
        // already rest at. The VRM leaves them out, so the board treats them as unsupported: they are not
        // shown, and an exclusion saved before still counts. A clip that moves a shape away from the face
        // on the board, a FaceEmo face and a clip a gesture enters stay faces.
        string NoFaceError(AnimationClip clip, Pose pose)
        {
            if (!clipRoots.ContainsKey(clip) || gestureClips.Contains(clip) || faceEmoBindings.RegisteredClips.Contains(clip)) return null;
            if (AnimationUtility.GetCurveBindings(clip).Any(binding => AnimationUtility.GetEditorCurve(clip, binding).keys
                .Any(key => key.value != 0 || key.inTangent != 0 || key.outTangent != 0))) return null;
            for (var i = 0; i < Baseline.Rows.Count; i++)
                if (!Baseline.Rows[i].Weights.SequenceEqual(pose.Rows[i].Weights)) return null;
            return ExporterLocalization.T(NoFaceChange);
        }

        // Read only the explicitly authored morph curves. No Animator graph,
        // callbacks, events, physics or unrelated idle layers participate.
        Pose BuildClipPose(AnimationClip clip, float time, string name)
        {
            RequireValid();
            return BuildClipPoseUnchecked(clip, time, name);
        }

        Pose BuildClipPoseUnchecked(AnimationClip clip, float time, string name)
        {
            if (clip == null) throw new InvalidOperationException(ExporterLocalization.T("UnityのProjectからAnimationClipを指定してください。"));
            if (!Finite(time) || time < 0 || time > clip.length)
                throw new InvalidOperationException(ExporterLocalization.T("収録時刻は0〜") + clip.length.ToString("G9", CultureInfo.InvariantCulture) + ExporterLocalization.T("秒で指定してください。"));
            if (AnimationUtility.GetAnimationEvents(clip).Length != 0)
                throw new InvalidOperationException(ExporterLocalization.T("Animation Eventを含みます。この実験版ではイベント付きの表情は収録できません。"));
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var unsupported = bindings.Where(binding => binding.type != typeof(SkinnedMeshRenderer) ||
                !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)).ToArray();
            if (unsupported.Length > 0)
                throw new InvalidOperationException(ExporterLocalization.T("顔の変形以外の変更を含みます。材質・ボーン・表示切替は追加対応が必要です: ") +
                    unsupported[0].path + " / " + unsupported[0].propertyName);
            if (bindings.Length == 0) throw new InvalidOperationException(ExporterLocalization.T("このクリップにはBlendShapeの表情がありません。全身ポーズはポーズ欄へ追加してください。"));
            var root = ClipRoot(clip);
            var pose = ClonePose(Baseline); pose.Name = ValidateName(name);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var applied = 0;
            foreach (var binding in bindings)
            {
                var path = string.IsNullOrEmpty(root) ? binding.path : string.IsNullOrEmpty(binding.path) ? root : root + "/" + binding.path;
                // A mesh left out of recording drops only its own part of the face.
                if (skippedPaths.Contains(path)) continue;
                if (!authoringRenderers.TryGetValue(path, out var original))
                    throw new InvalidOperationException(ExporterLocalization.T("元のアバターに対象メッシュがないか、同じ階層に複数あります: ") + path);
                var renderer = preparation.PreparedRendererFor(original);
                var channels = Channels.Where(channel => channel.Renderer == renderer).ToArray();
                if (channels.Length != 1)
                    throw new InvalidOperationException(ExporterLocalization.T("表示中の書き出し対象にメッシュがありません。統合・分割の対応が必要です: ") + path);
                var channel = channels[0]; var shape = binding.propertyName.Substring("blendShape.".Length);
                if (!used.Add(channel.Path + "\n" + shape)) throw new InvalidOperationException(ExporterLocalization.T("変形の参照が重複しています: ") + shape);
                var nativeCurve = AnimationUtility.GetEditorCurve(clip, binding);
                VrChatGestureExpressions.ReadCurve(nativeCurve); // Reuse the finite curve/weighted tangent limits.
                var weight = nativeCurve.Evaluate(time);
                if (!Finite(weight)) throw new InvalidOperationException(ExporterLocalization.T("表情の変形量が有限ではありません: ") + shape);
                // A shape the original mesh never had changes nothing in VRChat,
                // and the menu export drops it the same way, so the board must not
                // reject the face for it. A shape lost by preparation still fails.
                if (!HasShape(pose, channel.Path, shape) && !preparation.HadShape(original, shape)) continue;
                Set(pose, channel.Path, shape, weight);
                applied++;
            }
            if (applied == 0) throw new InvalidOperationException(ExporterLocalization.T("参照を解決した結果、有効な顔のBlendShapeがないため、この表情を省略しました。"));
            pose.InstalledSourceId = ClipId(clip, time);
            ValidatePose(pose);
            return pose;
        }

        static string ClipId(AnimationClip clip, float time)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId);
            return "clip/" + (string.IsNullOrEmpty(guid) ? "temporary-" + clip.GetInstanceID() : guid + "/" + localId) +
                "/" + time.ToString("R", CultureInfo.InvariantCulture);
        }

        internal void PreviewClip(AnimationClip clip, float time = 0) => ApplyUnchecked(BuildClipPose(clip, time, "プレビュー"));

        internal ImportResult ImportClips(IEnumerable<ClipInput> inputs)
        {
            RequireValid();
            var result = new ImportResult(); var staged = new List<Pose>();
            var names = new HashSet<string>(Expressions.Select(pose => pose.Name), StringComparer.Ordinal);
            foreach (var input in inputs)
            {
                var pose = BuildClipPoseUnchecked(input.Clip, input.Time, input.Name);
                if (!names.Add(pose.Name)) { result.Skipped.Add(pose.Name + ": 同名の表情を登録済みです。"); continue; }
                if (Expressions.Count + staged.Count >= MaximumExpressions) throw new InvalidOperationException(ExporterLocalization.T("記録できる表情は64件までです。"));
                staged.Add(pose); result.Imported.Add(pose.Name);
            }
            Expressions.AddRange(staged); // Atomic: invalid files never publish a partial batch or change the preview.
            return result;
        }

        // Replace only records owned by this window's file list. Validate the
        // complete batch before changing any record; manual/loaded faces stay.
        // A repeated name gets " (2)", " (3)"... in list order, the same rule
        // as the final VRM names, so two files named alike are both recorded.
        internal Pose[] ReplaceClipRecords(IEnumerable<ClipInput> inputs, IEnumerable<Pose> previous)
        {
            RequireValid();
            var replaced = new HashSet<Pose>(previous ?? Array.Empty<Pose>());
            var retained = Expressions.Where(pose => !replaced.Contains(pose)).ToArray();
            var staged = inputs.Select(input => BuildClipPoseUnchecked(input.Clip, input.Time, input.Name)).ToArray();
            if (retained.Length + staged.Length > MaximumExpressions)
                throw new InvalidOperationException(ExporterLocalization.T("記録できる表情は64件までです。"));
            var names = new HashSet<string>(retained.Select(pose => pose.Name), StringComparer.Ordinal);
            foreach (var pose in staged)
            {
                if (names.Contains(pose.Name)) pose.Name = VrmMenuExpressions.UniqueName(pose.Name, names.Contains);
                names.Add(pose.Name);
            }
            Expressions.Clear(); Expressions.AddRange(retained); Expressions.AddRange(staged);
            return staged;
        }

        static PoseReference SavePoseReference(ManualPose pose)
        {
            if (pose.Clip == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(pose.Clip, out string guid, out long id) || string.IsNullOrEmpty(guid))
                throw new InvalidOperationException(ExporterLocalization.T("ポーズの元クリップをUnityのProjectへ保存してから設定を保存してください。"));
            return new PoseReference { Guid = guid, LocalId = id, Name = pose.Name, Category = pose.Category, Time = pose.Time };
        }

        static ManualPose LoadPoseReference(PoseReference reference)
        {
            if (reference == null || !Finite(reference.Time) || string.IsNullOrEmpty(reference.Guid))
                throw new InvalidOperationException(ExporterLocalization.T("ポーズの記録設定が不正です。"));
            var clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(reference.Guid)).OfType<AnimationClip>().FirstOrDefault(candidate =>
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string guid, out long id) && id == reference.LocalId);
            if (clip == null || reference.Time < 0 || reference.Time > clip.length)
                throw new InvalidOperationException(ExporterLocalization.T("ポーズの元クリップが見つからないか、時刻が範囲外です: ") + reference.Name);
            return new ManualPose { Clip = clip, Name = reference.Name, Category = reference.Category, Time = reference.Time };
        }

        byte[] InjectSourcePoses(byte[] bytes, ICollection<string> warnings, PoseExportOptions poseOptions, Vector3 rootScale)
        {
            // APL definitions still belong to the original avatar; generated
            // menu poses belong to the prepared copy. Collect both before
            // binding the data to the final VRM's Humanoid nodes.
            using var poses = new PoseExportSession(Source, poseOptions.Copy(), exclusions.Contains);
            poses.CollectPrepared(Copy, warnings);
            var failed = poses.Entries.FirstOrDefault(entry => entry.Source.Contains("手動") && entry.Error != null && !poseOptions.Excluded.Contains(entry.Id));
            if (failed != null) throw new InvalidOperationException(ExporterLocalization.T("ポーズを書き出せません: ") + failed.Name + " / " + failed.Error);
            var document = GlbDocument.Read(bytes);
            var extensions = (Dictionary<string, object>)document.Json["extensions"];
            var vrm = (Dictionary<string, object>)extensions["VRMC_vrm"];
            var bones = (Dictionary<string, object>)((Dictionary<string, object>)vrm["humanoid"])["humanBones"];
            int Node(string name)
            {
                if (!bones.TryGetValue(name, out var value)) throw new InvalidOperationException(ExporterLocalization.T("出力VRMにポーズの骨がありません: ") + name);
                return Convert.ToInt32(((Dictionary<string, object>)value)["node"], CultureInfo.InvariantCulture);
            }
            foreach (var pose in poses.Selected()) foreach (var bone in pose.Bones) bone.Node = Node(bone.Name);
            foreach (var animation in poses.SelectedAnimations()) foreach (var bone in animation.Bones) bone.Node = Node(bone.Name);
            return poses.Inject(bytes, rootScale);
        }
    }
}
