using System;
using System.Collections.Generic;
using System.Linq;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    internal sealed partial class ExperimentalExpressionCaptureSession
    {
        // Every VRM preset except custom can be assigned. A preset without an
        // assignment keeps the existing automatic configuration.
        internal static readonly ExpressionPreset[] EmotionPresets =
            { ExpressionPreset.happy, ExpressionPreset.angry, ExpressionPreset.sad, ExpressionPreset.relaxed, ExpressionPreset.surprised };
        internal static readonly ExpressionPreset[] BlinkPresets =
            { ExpressionPreset.blink, ExpressionPreset.blinkLeft, ExpressionPreset.blinkRight };
        internal static readonly ExpressionPreset[] MouthPresets =
            { ExpressionPreset.aa, ExpressionPreset.ih, ExpressionPreset.ou, ExpressionPreset.ee, ExpressionPreset.oh };
        internal static readonly ExpressionPreset[] LookAtPresets =
            { ExpressionPreset.lookUp, ExpressionPreset.lookDown, ExpressionPreset.lookLeft, ExpressionPreset.lookRight };

        // Display groups, in display order. Labels are localization sources.
        internal static readonly (string Label, ExpressionPreset[] Presets)[] PresetGroups =
        {
            ("感情", EmotionPresets), ("まばたき", BlinkPresets), ("口の形", MouthPresets),
            ("視線", LookAtPresets), ("基準の顔", new[] { ExpressionPreset.neutral }),
        };
        internal static readonly ExpressionPreset[] AssignablePresets = PresetGroups.SelectMany(group => group.Presets).ToArray();

        // VRChat Lip Sync viseme slots (sil, PP, FF, TH, DD, kk, CH, SS, nn,
        // RR, aa, E, I, O, U) that name a VRM mouth preset.
        static readonly (int Slot, ExpressionPreset Preset)[] VisemeSlots =
        {
            (10, ExpressionPreset.aa), (11, ExpressionPreset.ee), (12, ExpressionPreset.ih), (13, ExpressionPreset.oh), (14, ExpressionPreset.ou),
        };

        // One assignment per preset: an AnimationClip face (recorded,
        // FaceEmo or controller clip), one BlendShape at 100, or explicitly none.
        [Serializable]
        internal sealed class PresetAssignment
        {
            public string Preset;
            public string ClipGuid;
            public long ClipLocalId;
            public string Path, Shape;
            [NonSerialized] internal AnimationClip Clip;
            // Initial values chosen from names are shown as automatic and are
            // never persisted; only the user's own choices are remembered.
            [NonSerialized] internal bool Automatic;
            [NonSerialized] bool lookedUp;

            internal bool IsNone => Clip == null && string.IsNullOrEmpty(ClipGuid) && string.IsNullOrEmpty(Shape);
            internal bool IsShape => !string.IsNullOrEmpty(Shape);

            internal static PresetAssignment None(ExpressionPreset preset) => new PresetAssignment { Preset = preset.ToString() };
            internal static PresetAssignment ForClip(ExpressionPreset preset, AnimationClip clip)
            {
                var result = new PresetAssignment { Preset = preset.ToString(), Clip = clip };
                // Remember the asset too: a later deletion is then reported, not read as "none".
                if (clip != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id) && !string.IsNullOrEmpty(guid))
                { result.ClipGuid = guid; result.ClipLocalId = id; }
                return result;
            }
            internal static PresetAssignment ForShape(ExpressionPreset preset, string path, string shape) =>
                new PresetAssignment { Preset = preset.ToString(), Path = path ?? "", Shape = shape };

            internal PresetAssignment Copy() => new PresetAssignment { Preset = Preset, ClipGuid = ClipGuid, ClipLocalId = ClipLocalId,
                Path = Path, Shape = Shape, Clip = Clip, Automatic = Automatic };

            internal AnimationClip ResolveClip()
            {
                if (Clip != null || string.IsNullOrEmpty(ClipGuid) || lookedUp) return Clip;
                lookedUp = true;
                Clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(ClipGuid)).OfType<AnimationClip>().FirstOrDefault(candidate =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string guid, out long id) && id == ClipLocalId);
                return Clip;
            }

            // Persisted form references the clip asset, never an instance ID.
            internal PresetAssignment ForSaving()
            {
                var copy = Copy();
                if (Clip != null)
                {
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(Clip, out string guid, out long id) || string.IsNullOrEmpty(guid)) return null;
                    copy.ClipGuid = guid; copy.ClipLocalId = id;
                }
                return copy;
            }
        }

        internal readonly Dictionary<ExpressionPreset, PresetAssignment> Presets = new Dictionary<ExpressionPreset, PresetAssignment>();
        List<(string Path, string Shape)> presetShapeOptions;

        internal static string PresetLabel(ExpressionPreset preset)
        {
            switch (preset)
            {
                case ExpressionPreset.happy: return "喜び";
                case ExpressionPreset.angry: return "怒り";
                case ExpressionPreset.sad: return "悲しみ";
                case ExpressionPreset.relaxed: return "安らぎ";
                case ExpressionPreset.surprised: return "驚き";
                case ExpressionPreset.blink: return "両目";
                case ExpressionPreset.blinkLeft: return "左目";
                case ExpressionPreset.blinkRight: return "右目";
                case ExpressionPreset.aa: return "あ";
                case ExpressionPreset.ih: return "い";
                case ExpressionPreset.ou: return "う";
                case ExpressionPreset.ee: return "え";
                case ExpressionPreset.oh: return "お";
                case ExpressionPreset.lookUp: return "上";
                case ExpressionPreset.lookDown: return "下";
                case ExpressionPreset.lookLeft: return "左";
                case ExpressionPreset.lookRight: return "右";
                case ExpressionPreset.neutral: return "基準の顔";
                default: throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }

        // Localized label that stands alone in messages: "まばたき 左目", "視線 上".
        // Emotion and neutral labels are already unambiguous.
        internal static string PresetFullLabel(ExpressionPreset preset)
        {
            var label = ExporterLocalization.T(PresetLabel(preset));
            if (EmotionPresets.Contains(preset) || preset == ExpressionPreset.neutral) return label;
            return ExporterLocalization.T(PresetGroups.First(group => group.Presets.Contains(preset)).Label) + " " + label;
        }

        internal static ExpressionPreset ParsePreset(string value)
        {
            foreach (var preset in AssignablePresets)
                if (string.Equals(preset.ToString(), value, StringComparison.Ordinal)) return preset;
            throw new InvalidOperationException(ExporterLocalization.T("標準の表情の種類が不正です: ") + value);
        }

        internal void SetPreset(ExpressionPreset preset, PresetAssignment assignment)
        {
            if (!AssignablePresets.Contains(preset)) throw new InvalidOperationException(ExporterLocalization.T("標準の表情の種類が不正です: ") + preset);
            if (assignment == null) { Presets.Remove(preset); return; }
            if (assignment.Clip != null && assignment.IsShape)
                throw new InvalidOperationException(ExporterLocalization.T("標準の表情には表情ファイルかBlendShapeの一方を指定してください。"));
            assignment.Preset = preset.ToString();
            Presets[preset] = assignment;
        }

        // BlendShapes that can be chosen, named by their authoring (source) path.
        internal IReadOnlyList<(string Path, string Shape)> PresetShapeOptions()
        {
            RequireValid();
            if (presetShapeOptions != null) return presetShapeOptions;
            var result = new List<(string Path, string Shape)>();
            foreach (var path in authoringRenderers.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                Channel channel; SkinnedMeshRenderer original;
                try { channel = AuthoringChannel(path); original = VrChatExpressionSampler.FindRenderer(Source, path); }
                catch (InvalidOperationException) { continue; }
                var mesh = original.sharedMesh;
                for (var index = 0; index < mesh.blendShapeCount; index++)
                {
                    var shape = mesh.GetBlendShapeName(index);
                    if (channel.Shapes.Contains(shape)) result.Add((path, shape));
                }
            }
            return presetShapeOptions = result;
        }

        Channel AuthoringChannel(string path)
        {
            if (!authoringRenderers.TryGetValue(path, out var original))
                throw new InvalidOperationException(ExporterLocalization.T("元のアバターに対象メッシュがないか、同じ階層に複数あります: ") + path);
            var renderer = preparation.PreparedRendererFor(original);
            var channels = Channels.Where(channel => channel.Renderer == renderer).ToArray();
            if (channels.Length != 1)
                throw new InvalidOperationException(ExporterLocalization.T("表示中の書き出し対象にメッシュがありません。統合・分割の対応が必要です: ") + path);
            return channels[0];
        }

        // Absolute source weights in authoring paths, as a VRM clip declares them.
        List<(string Path, string Shape, float Weight)> PresetMorphs(PresetAssignment assignment)
        {
            if (assignment.IsShape) return new List<(string, string, float)> { (assignment.Path ?? "", assignment.Shape, 100f) };
            var clip = assignment.ResolveClip();
            if (clip == null) throw new InvalidOperationException(ExporterLocalization.T("表情の元クリップが見つかりません。もう一度選んでください。"));
            BuildClipPoseUnchecked(clip, clip.length, "プレビュー"); // The same file checks as the added faces.
            var root = ClipRoot(clip);
            return AnimationUtility.GetCurveBindings(clip).Select(binding => (
                string.IsNullOrEmpty(root) ? binding.path : string.IsNullOrEmpty(binding.path) ? root : root + "/" + binding.path,
                binding.propertyName.Substring("blendShape.".Length),
                AnimationUtility.GetEditorCurve(clip, binding).Evaluate(clip.length))).ToList();
        }

        internal Pose BuildPresetPose(ExpressionPreset preset, PresetAssignment assignment)
        {
            RequireValid();
            var label = PresetFullLabel(preset);
            if (assignment == null || assignment.IsNone) throw new InvalidOperationException(label + ExporterLocalization.T(": 表情を選んでください。"));
            try
            {
                var morphs = PresetMorphs(assignment);
                if (morphs.Any(morph => morph.Weight < 0 || morph.Weight > 100))
                    throw new InvalidOperationException(ExporterLocalization.T("標準の表情には0〜100の変形量だけを使えます。"));
                if (!morphs.Any(morph => morph.Weight > 0))
                    throw new InvalidOperationException(ExporterLocalization.T("0より大きい変形がありません。"));
                Pose pose;
                if (assignment.IsShape)
                {
                    var channel = AuthoringChannel(assignment.Path ?? "");
                    pose = ClonePose(Baseline); pose.Name = PresetLabel(preset);
                    Set(pose, channel.Path, assignment.Shape, 100f);
                }
                else pose = BuildClipPoseUnchecked(assignment.ResolveClip(), assignment.ResolveClip().length, PresetLabel(preset));
                ValidatePose(pose);
                if (!HasGeometryChange(pose)) throw new InvalidOperationException(ExporterLocalization.T("基準の顔と形状が同じです。"));
                return pose;
            }
            catch (InvalidOperationException error) { throw new InvalidOperationException(label + ": " + error.Message, error); }
        }

        internal string PresetError(ExpressionPreset preset, PresetAssignment assignment)
        {
            if (assignment == null || assignment.IsNone) return null;
            try { BuildPresetPose(preset, assignment); return null; }
            catch (InvalidOperationException error) { return error.Message; }
        }

        internal void PreviewPreset(ExpressionPreset preset, PresetAssignment assignment) => ApplyUnchecked(BuildPresetPose(preset, assignment));

        (ExpressionPreset Preset, PresetAssignment Assignment)[] SelectedPresets(IEnumerable<PresetAssignment> presets)
        {
            var result = new List<(ExpressionPreset, PresetAssignment)>();
            var seen = new HashSet<ExpressionPreset>();
            foreach (var assignment in presets ?? Presets.Values)
            {
                if (assignment == null) continue;
                var preset = ParsePreset(assignment.Preset);
                if (!seen.Add(preset))
                    throw new InvalidOperationException(ExporterLocalization.T("同じ標準の表情が2回指定されています: ") + PresetFullLabel(preset));
                if (!assignment.IsNone) result.Add((preset, assignment));
            }
            return result.OrderBy(item => Array.IndexOf(AssignablePresets, item.Item1)).ToArray();
        }

        static VRM10Expression CreatePresetExpression(ExpressionPreset preset)
        {
            var expression = ScriptableObject.CreateInstance<VRM10Expression>();
            expression.name = preset.ToString();
            // Presets can be driven by partial weights. At weight 1 an emotion's
            // blend override suppresses blink/mouth/gaze like a recorded face;
            // blink, mouth, gaze and neutral are themselves those channels.
            expression.IsBinary = false;
            expression.OverrideBlink = expression.OverrideMouth = expression.OverrideLookAt = EmotionPresets.Contains(preset)
                ? UniGLTF.Extensions.VRMC_vrm.ExpressionOverrideType.blend : UniGLTF.Extensions.VRMC_vrm.ExpressionOverrideType.none;
            return expression;
        }

        // The ordinary export route: add the presets to its private copy as
        // authored VRM clips before identity capture and neutral baking.
        void AuthorPresets(GameObject clone, IEnumerable<(ExpressionPreset Preset, List<(string Path, string Shape, float Weight)> Morphs)> faces, List<Object> assets)
        {
            var instance = clone.GetComponent<Vrm10Instance>() ?? clone.AddComponent<Vrm10Instance>();
            var settings = instance.Vrm != null ? Object.Instantiate(instance.Vrm) : ScriptableObject.CreateInstance<VRM10Object>();
            if (instance.Vrm != null) settings.name = instance.Vrm.name;
            else
            {
                // A new settings object only carries the presets. Its default
                // lookAt/firstPerson would otherwise appear in the output.
                settings.LookAt = null; settings.FirstPerson = null;
            }
            assets.Add(settings);
            if (settings.Expression == null) settings.Expression = new VRM10ObjectExpression();
            foreach (var face in faces)
            {
                var expression = CreatePresetExpression(face.Preset); assets.Add(expression);
                // A zero would reserve and disable the channel in the authored
                // VRM contract; unlisted channels already keep the neutral face.
                expression.MorphTargetBindings = face.Morphs.Where(morph => morph.Weight > 0).Select(morph => {
                    var renderer = VrChatExpressionSampler.FindRenderer(clone, morph.Path);
                    var index = renderer.sharedMesh.GetBlendShapeIndex(morph.Shape);
                    if (index < 0) throw new InvalidOperationException(PresetFullLabel(face.Preset) +
                        ExporterLocalization.T(": 元のメッシュにBlendShapeがありません: ") + morph.Path + " / " + morph.Shape);
                    return new MorphTargetBinding(morph.Path, index, morph.Weight / 100f);
                }).ToArray();
                settings.Expression.AddClip(face.Preset, expression);
            }
            instance.Vrm = settings;
        }

        // Blink assignments go to the blink export as a manual setting, not as
        // authored clips: it replaces every authored blink preset unless the
        // source VRM's own blink is kept. Unassigned blink presets stay unset.
        BlinkExportOptions AssignedBlink(IEnumerable<(ExpressionPreset Preset, List<(string Path, string Shape, float Weight)> Morphs)> faces)
        {
            var options = new BlinkExportOptions { Mode = BlinkExportMode.Manual };
            var slots = new[] { options.Both, options.Left, options.Right };
            foreach (var face in faces)
                slots[Array.IndexOf(BlinkPresets, face.Preset)].AddRange(face.Morphs.Where(morph => morph.Weight > 0).Select(morph =>
                    new BlinkShapeBinding { Renderer = VrChatExpressionSampler.FindRenderer(Source, morph.Path), Shape = morph.Shape, Weight = morph.Weight }));
            return options;
        }

        List<PresetAssignment> SavePresets() => AssignablePresets.Where(Presets.ContainsKey).Select(preset => Presets[preset])
            .Where(assignment => !assignment.Automatic).Select(assignment => assignment.ForSaving() ??
                throw new InvalidOperationException(ExporterLocalization.T("標準の表情の元クリップをUnityのProjectへ保存してから設定を保存してください。"))).ToList();

        static List<PresetAssignment> LoadPresets(IEnumerable<PresetAssignment> stored)
        {
            var result = new List<PresetAssignment>();
            var seen = new HashSet<ExpressionPreset>();
            foreach (var value in stored ?? Array.Empty<PresetAssignment>())
            {
                if (value == null) throw new InvalidOperationException(ExporterLocalization.T("標準の表情の設定が不正です。"));
                var preset = ParsePreset(value.Preset);
                if (!seen.Add(preset)) throw new InvalidOperationException(ExporterLocalization.T("同じ標準の表情が2回指定されています: ") + PresetFullLabel(preset));
                var copy = value.Copy(); copy.Clip = null; copy.Automatic = false;
                if (!string.IsNullOrEmpty(copy.ClipGuid) && copy.IsShape || string.IsNullOrEmpty(copy.ClipGuid) && copy.ClipLocalId != 0)
                    throw new InvalidOperationException(ExporterLocalization.T("標準の表情の設定が不正です。"));
                if (!string.IsNullOrEmpty(copy.ClipGuid) && copy.ResolveClip() == null)
                    throw new InvalidOperationException(ExporterLocalization.T("標準の表情の元クリップが見つかりません: ") + PresetFullLabel(preset));
                result.Add(copy);
            }
            return result;
        }

        internal static PresetAssignment LoadPresetsForStore(PresetAssignment stored) => LoadPresets(new[] { stored }).Single();

        // Suggest initial values from clip and BlendShape names; the vocabulary
        // and matching rules live in ExpressionNameInference.
        internal static ExpressionPreset? SuggestPreset(string name) => ExpressionNameInference.MatchEmotion(name)?.Preset;

        // Per emotion, the best tier wins; within a tier added/registered clips
        // come before BlendShapes, in the given order. Unusable faces are skipped.
        // Mouth and blink candidates follow what the export would choose by
        // itself; gaze and neutral have none. blinkOptions is the blink setting
        // the export receives (null is automatic).
        internal Dictionary<ExpressionPreset, PresetAssignment> SuggestPresets(IEnumerable<AnimationClip> clips, BlinkExportOptions blinkOptions = null)
        {
            var candidates = new List<(ExpressionPreset Preset, int Tier, int Order, Func<ExpressionPreset, PresetAssignment> Create)>();
            var order = 0;
            foreach (var clip in (clips ?? Array.Empty<AnimationClip>()).Where(clip => clip != null).Distinct())
            {
                var match = ExpressionNameInference.MatchEmotion(clip.name); order++;
                if (match != null) candidates.Add((match.Value.Preset, match.Value.Tier, order, preset => PresetAssignment.ForClip(preset, clip)));
            }
            // Japanese BlendShape names commonly follow the MMD set, where "怒り",
            // "びっくり" or "にっこり" move only the brows, eyes or mouth. Those are
            // offered by hand, not as automatic initial values.
            foreach (var option in PresetShapeOptions())
            {
                var match = ExpressionNameInference.MatchEmotion(option.Shape, latinOnly: true); order++;
                if (match != null) candidates.Add((match.Value.Preset, match.Value.Tier, order, preset => PresetAssignment.ForShape(preset, option.Path, option.Shape)));
            }
            var result = new Dictionary<ExpressionPreset, PresetAssignment>();
            foreach (var candidate in candidates.OrderBy(item => item.Tier).ThenBy(item => item.Order))
            {
                // Faces the avatar's own VRM settings already define stay theirs.
                if (result.ContainsKey(candidate.Preset) || HasAuthoredPreset(candidate.Preset)) continue;
                var assignment = candidate.Create(candidate.Preset); assignment.Automatic = true;
                if (PresetError(candidate.Preset, assignment) == null) result.Add(candidate.Preset, assignment);
            }
            var visemes = DescriptorVisemes(Source);
            var mouth = SuggestMouthPresets(visemes.Renderer, visemes.Shapes);
            foreach (var preset in mouth.Keys) result.Add(preset, mouth[preset]);
            var blink = SuggestBlinkPresets(blinkOptions);
            foreach (var preset in blink.Keys) result.Add(preset, blink[preset]);
            return result;
        }

        // VRChat Lip Sync blend shapes, read like the eyelid settings: only the
        // VisemeBlendShape mode on a renderer of this avatar.
        static (SkinnedMeshRenderer Renderer, string[] Shapes) DescriptorVisemes(GameObject source)
        {
            var descriptor = source.GetComponents<Component>().FirstOrDefault(c => c != null &&
                c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (VrChatExpressionMenu.Member(descriptor, "lipSync")?.ToString() != "VisemeBlendShape") return (null, null);
            var renderer = VrChatExpressionMenu.Member(descriptor, "VisemeSkinnedMesh") as SkinnedMeshRenderer;
            if (renderer == null || !renderer.transform.IsChildOf(source.transform)) return (null, null);
            return (renderer, VrChatExpressionMenu.Member(descriptor, "VisemeBlendShapes") as string[]);
        }

        // Per mouth preset: the Lip Sync viseme shape, otherwise the one shape
        // that the ordinary export's known names (MouthShapes) would bind. A
        // name on several meshes is left to that export, which binds them all.
        // Only exported BlendShapes (PresetShapeOptions) are candidates.
        internal Dictionary<ExpressionPreset, PresetAssignment> SuggestMouthPresets(SkinnedMeshRenderer visemeRenderer, string[] visemeShapes)
        {
            var options = new HashSet<(string Path, string Shape)>(PresetShapeOptions());
            var visemePath = visemeRenderer != null && visemeRenderer.transform.IsChildOf(Source.transform)
                ? AnimationUtility.CalculateTransformPath(visemeRenderer.transform, Source.transform) : null;
            var result = new Dictionary<ExpressionPreset, PresetAssignment>();
            foreach (var preset in MouthPresets)
            {
                if (HasAuthoredPreset(preset)) continue;
                PresetAssignment candidate = null;
                var slot = VisemeSlots.First(item => item.Preset == preset).Slot;
                if (visemePath != null && visemeShapes != null && slot < visemeShapes.Length && !string.IsNullOrEmpty(visemeShapes[slot]) &&
                    options.Contains((visemePath, visemeShapes[slot])))
                    candidate = PresetAssignment.ForShape(preset, visemePath, visemeShapes[slot]);
                if (candidate == null && NameMouthShape(preset) is { } named)
                    candidate = PresetAssignment.ForShape(preset, named.Path, named.Shape);
                if (candidate == null) continue;
                candidate.Automatic = true;
                if (PresetError(preset, candidate) == null) result.Add(preset, candidate);
            }
            return result;
        }

        // The one exported BlendShape the ordinary export binds for this mouth
        // preset by its known names; null when no mesh or several have one.
        (string Path, string Shape)? NameMouthShape(ExpressionPreset preset)
        {
            var aliases = ExpressionNameInference.MouthShapes.First(item => item.Preset == preset.ToString()).Aliases;
            var options = new HashSet<(string Path, string Shape)>(PresetShapeOptions());
            var matches = new List<(string Path, string Shape)>();
            foreach (var path in options.Select(option => option.Path).Distinct())
            {
                var mesh = VrChatExpressionSampler.FindRenderer(Source, path).sharedMesh;
                var names = Enumerable.Range(0, mesh.blendShapeCount).Select(index => (object)mesh.GetBlendShapeName(index)).ToList();
                var found = ExpressionNameInference.FindExact(names, aliases);
                if (found >= 0 && options.Contains((path, (string)names[found]))) matches.Add((path, (string)names[found]));
            }
            return matches.Count == 1 ? matches[0] : ((string, string)?)null;
        }

        // An automatic mouth candidate found by name is what the ordinary
        // export binds by itself; leaving it to that export keeps its output.
        bool LeftToNameBinding(ExpressionPreset preset, bool automatic, List<(string Path, string Shape, float Weight)> morphs) =>
            automatic && MouthPresets.Contains(preset) && morphs.Count == 1 && morphs[0].Weight == 100f &&
            NameMouthShape(preset) is { } named && named.Path == morphs[0].Path && named.Shape == morphs[0].Shape;

        // Blink candidates are exactly the automatic blink export's choice: one
        // exported BlendShape per preset. Kept source VRM blink, a manual
        // setting or no blink offer none, and so does an unresolved setup.
        Dictionary<ExpressionPreset, PresetAssignment> SuggestBlinkPresets(BlinkExportOptions blinkOptions)
        {
            var result = new Dictionary<ExpressionPreset, PresetAssignment>();
            if ((blinkOptions?.Mode ?? BlinkExportMode.Auto) != BlinkExportMode.Auto) return result;
            BlinkExportSession resolved;
            try { resolved = BlinkExportSession.Resolve(Source, blinkOptions, exclusions != null ? exclusions.Contains : (Func<Transform, bool>)null); }
            catch (InvalidOperationException) { return result; }
            using (resolved)
            {
                if (resolved.PreserveAuthored || resolved.Disabled) return result;
                var options = new HashSet<(string Path, string Shape)>(PresetShapeOptions());
                for (var slot = 0; slot < BlinkPresets.Length; slot++)
                {
                    if (resolved.Slots[slot].Count != 1) continue;
                    var binding = resolved.Slots[slot][0];
                    if (binding.Renderer == null || !binding.Renderer.transform.IsChildOf(Source.transform)) continue;
                    var path = AnimationUtility.CalculateTransformPath(binding.Renderer.transform, Source.transform);
                    if (!options.Contains((path, binding.Shape))) continue;
                    var candidate = PresetAssignment.ForShape(BlinkPresets[slot], path, binding.Shape); candidate.Automatic = true;
                    if (PresetError(BlinkPresets[slot], candidate) == null) result.Add(BlinkPresets[slot], candidate);
                }
            }
            return result;
        }

        // What the blink export uses while no blink preset is assigned.
        internal string BlinkFallbackDescription(BlinkExportOptions options)
        {
            RequireValid();
            switch (options?.Mode ?? BlinkExportMode.Auto)
            {
                case BlinkExportMode.None: return ExporterLocalization.T("瞬きなし");
                case BlinkExportMode.Manual: return ExporterLocalization.T("手動設定");
            }
            try
            {
                using var resolved = BlinkExportSession.Resolve(Source, options, exclusions != null ? exclusions.Contains : (Func<Transform, bool>)null);
                return ExporterLocalization.T(resolved.Description);
            }
            catch (InvalidOperationException error) { return ExporterLocalization.T(error.Message); }
        }

        // The source avatar's own VRM settings already fill this emotion.
        internal bool HasAuthoredPreset(ExpressionPreset preset)
        {
            var expression = Source != null && Source.TryGetComponent<Vrm10Instance>(out var instance) ? instance.Vrm?.Expression : null;
            var clip = expression?.Clips.Where(item => item.Preset == preset).Select(item => item.Clip).FirstOrDefault();
            return clip != null && (clip.MorphTargetBindings?.Length > 0 || clip.MaterialColorBindings?.Length > 0 || clip.MaterialUVBindings?.Length > 0);
        }

        internal static string PresetDisplayName(PresetAssignment assignment, bool authored = false)
        {
            if (assignment == null || assignment.IsNone) return ExporterLocalization.T(authored ? "元のVRM設定" : "なし");
            if (assignment.IsShape) return "BlendShape: " + (string.IsNullOrEmpty(assignment.Path) ? "" : assignment.Path + " / ") + assignment.Shape;
            var clip = assignment.ResolveClip();
            return clip != null ? clip.name : ExporterLocalization.T("見つからないファイル");
        }
    }
}
