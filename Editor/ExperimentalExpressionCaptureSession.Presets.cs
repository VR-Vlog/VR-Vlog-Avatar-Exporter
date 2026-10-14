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
        // Only the emotion presets are user-assignable. Mouth, blink and gaze
        // presets keep their existing automatic configuration.
        internal static readonly ExpressionPreset[] EmotionPresets =
            { ExpressionPreset.happy, ExpressionPreset.angry, ExpressionPreset.sad, ExpressionPreset.relaxed, ExpressionPreset.surprised };

        // One assignment per emotion preset: an AnimationClip face (recorded,
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
                default: throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }

        internal static ExpressionPreset ParsePreset(string value)
        {
            foreach (var preset in EmotionPresets)
                if (string.Equals(preset.ToString(), value, StringComparison.Ordinal)) return preset;
            throw new InvalidOperationException(ExporterLocalization.T("標準の表情の種類が不正です: ") + value);
        }

        internal void SetPreset(ExpressionPreset preset, PresetAssignment assignment)
        {
            if (!EmotionPresets.Contains(preset)) throw new InvalidOperationException(ExporterLocalization.T("標準の表情の種類が不正です: ") + preset);
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
            var label = ExporterLocalization.T(PresetLabel(preset));
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
                    throw new InvalidOperationException(ExporterLocalization.T("同じ標準の表情が2回指定されています: ") + ExporterLocalization.T(PresetLabel(preset)));
                if (!assignment.IsNone) result.Add((preset, assignment));
            }
            return result.OrderBy(item => Array.IndexOf(EmotionPresets, item.Item1)).ToArray();
        }

        static VRM10Expression CreatePresetExpression(ExpressionPreset preset)
        {
            var expression = ScriptableObject.CreateInstance<VRM10Expression>();
            expression.name = preset.ToString();
            // Emotion presets can be driven by partial weights. At weight 1 the
            // blend override suppresses blink/mouth/gaze like a recorded face.
            expression.IsBinary = false;
            expression.OverrideBlink = expression.OverrideMouth = expression.OverrideLookAt = UniGLTF.Extensions.VRMC_vrm.ExpressionOverrideType.blend;
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
                    if (index < 0) throw new InvalidOperationException(ExporterLocalization.T(PresetLabel(face.Preset)) +
                        ExporterLocalization.T(": 元のメッシュにBlendShapeがありません: ") + morph.Path + " / " + morph.Shape);
                    return new MorphTargetBinding(morph.Path, index, morph.Weight / 100f);
                }).ToArray();
                settings.Expression.AddClip(face.Preset, expression);
            }
            instance.Vrm = settings;
        }

        List<PresetAssignment> SavePresets() => EmotionPresets.Where(Presets.ContainsKey).Select(preset => Presets[preset])
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
                if (!seen.Add(preset)) throw new InvalidOperationException(ExporterLocalization.T("同じ標準の表情が2回指定されています: ") + ExporterLocalization.T(PresetLabel(preset)));
                var copy = value.Copy(); copy.Clip = null; copy.Automatic = false;
                if (!string.IsNullOrEmpty(copy.ClipGuid) && copy.IsShape || string.IsNullOrEmpty(copy.ClipGuid) && copy.ClipLocalId != 0)
                    throw new InvalidOperationException(ExporterLocalization.T("標準の表情の設定が不正です。"));
                if (!string.IsNullOrEmpty(copy.ClipGuid) && copy.ResolveClip() == null)
                    throw new InvalidOperationException(ExporterLocalization.T("標準の表情の元クリップが見つかりません: ") + ExporterLocalization.T(PresetLabel(preset)));
                result.Add(copy);
            }
            return result;
        }

        internal static PresetAssignment LoadPresetsForStore(PresetAssignment stored) => LoadPresets(new[] { stored }).Single();

        // Suggest initial values from clip and BlendShape names. Latin words
        // must match a whole word (split at symbols, digits and camelCase);
        // Japanese words match inside the name. Tier 0 words name the emotion
        // itself and win over the looser tier 1 words.
        internal static readonly (ExpressionPreset Preset, int Tier, string[] Words)[] PresetVocabulary =
        {
            (ExpressionPreset.happy, 0, new[] { "happy", "happiness", "smile", "smiling", "grin", "笑顔", "えがお", "喜び", "嬉し", "うれし", "にこにこ", "ニコニコ", "にっこり", "スマイル", "ハッピー" }),
            (ExpressionPreset.angry, 0, new[] { "angry", "anger", "furious", "mad", "怒", "いかり", "イライラ", "ぷんぷん", "プンプン" }),
            (ExpressionPreset.sad, 0, new[] { "sad", "sadness", "sorrow", "unhappy", "悲", "哀", "かなし", "しょんぼり", "しょぼん" }),
            (ExpressionPreset.relaxed, 0, new[] { "relax", "relaxed", "calm", "fun", "peaceful", "楽", "和み", "なごみ", "穏", "リラックス", "ほっこり", "まったり" }),
            (ExpressionPreset.surprised, 0, new[] { "surprise", "surprised", "suprise", "shock", "shocked", "astonished", "驚", "びっくり", "ビックリ", "おどろ" }),
            (ExpressionPreset.happy, 1, new[] { "joy", "joyful", "laugh", "laughing", "glad", "cheerful", "笑", "喜", "にこ", "ニコ" }),
            (ExpressionPreset.angry, 1, new[] { "rage", "annoyed", "grumpy", "むっ", "ムッ" }),
            (ExpressionPreset.sad, 1, new[] { "cry", "crying", "tear", "tears", "weep", "泣", "涙", "なみだ" }),
            (ExpressionPreset.relaxed, 1, new[] { "chill", "gentle", "serene", "安らぎ", "やすらぎ" }),
            (ExpressionPreset.surprised, 1, new[] { "wow", "gasp", "startled", "ぎょっ", "ギョッ" }),
        };

        // A side, a part of the face, a wink/blink or a toggle is not a whole emotion.
        internal static readonly string[] VetoWords = { "eye", "eyes", "mouth", "brow", "eyebrow", "cheek", "wink", "blink", "left", "right", "l", "r",
            "off", "reset", "none", "default", "brw", "mth", "teeth", "tongue", "nose" };
        internal static readonly string[] VetoParts = { "目", "口", "眉", "頬", "ウィンク", "まばたき", "解除", "左", "右" };

        static string[] LatinWords(string name) => System.Text.RegularExpressions.Regex.Matches(name ?? "", "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")
            .Cast<System.Text.RegularExpressions.Match>().Select(match => match.Value.ToLowerInvariant()).ToArray();

        static bool IsLatin(string word) => word.All(character => character < 128);

        internal static (ExpressionPreset Preset, int Tier)? MatchPresetName(string name, bool latinOnly = false)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var words = LatinWords(name);
            if (words.Any(VetoWords.Contains) || VetoParts.Any(part => name.IndexOf(part, StringComparison.Ordinal) >= 0)) return null;
            for (var tier = 0; tier <= 1; tier++)
            {
                var found = PresetVocabulary.Where(entry => entry.Tier == tier && entry.Words.Any(word => IsLatin(word)
                    ? words.Contains(word) : !latinOnly && name.IndexOf(word, StringComparison.Ordinal) >= 0)).Select(entry => entry.Preset).Distinct().ToArray();
                // Two emotions in the same tier say neither.
                if (found.Length == 1) return (found[0], tier);
                if (found.Length > 1) return null;
            }
            return null;
        }

        internal static ExpressionPreset? SuggestPreset(string name) => MatchPresetName(name)?.Preset;

        // Per emotion, the best tier wins; within a tier added/registered clips
        // come before BlendShapes, in the given order. Unusable faces are skipped.
        internal Dictionary<ExpressionPreset, PresetAssignment> SuggestPresets(IEnumerable<AnimationClip> clips)
        {
            var candidates = new List<(ExpressionPreset Preset, int Tier, int Order, Func<ExpressionPreset, PresetAssignment> Create)>();
            var order = 0;
            foreach (var clip in (clips ?? Array.Empty<AnimationClip>()).Where(clip => clip != null).Distinct())
            {
                var match = MatchPresetName(clip.name); order++;
                if (match != null) candidates.Add((match.Value.Preset, match.Value.Tier, order, preset => PresetAssignment.ForClip(preset, clip)));
            }
            // Japanese BlendShape names commonly follow the MMD set, where "怒り",
            // "びっくり" or "にっこり" move only the brows, eyes or mouth. Those are
            // offered by hand, not as automatic initial values.
            foreach (var option in PresetShapeOptions())
            {
                var match = MatchPresetName(option.Shape, latinOnly: true); order++;
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
            return result;
        }

        // The source avatar's own VRM settings already fill this emotion.
        internal bool HasAuthoredPreset(ExpressionPreset preset)
        {
            var expression = Source != null ? Source.GetComponent<Vrm10Instance>()?.Vrm?.Expression : null;
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
