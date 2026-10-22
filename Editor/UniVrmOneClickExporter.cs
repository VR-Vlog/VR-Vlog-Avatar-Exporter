using System;
using System.Collections.Generic;
using System.Linq;
using UniGLTF;
using UniVRM10;
using UniGLTF.Extensions.VRMC_vrm;
using UnityEngine;
using VRM10.MToon10;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VRVlog.LilToonExporter
{
    internal static class UniVrmOneClickExporter
    {
        internal const string LostHumanoidAnimator = "NDMF / 最適化ツールの処理でヒューマノイドに必要なルートの Animator が失われたため、書き出しを止めました。元のアバターは変更していません。アバターのルートに VRChat の Avatar Descriptor を付けるか、Animator を削除しない最適化設定にしてください。";
        internal const string EmptyMeshRenderersOmitted = "メッシュが空のRendererを書き出しから外しました: ";
        internal const string LostRequiredHumanBones = "NDMF / 最適化ツールの処理でヒューマノイドに必要なボーンが失われたため、書き出しを止めました。元のアバターは変更していません。失われたボーン: ";
        internal const string SourceNotHumanoid = "選んだアバターのルートにヒューマノイドの Animator がないため、VRM に書き出せません。元のアバターは変更していません。モデルの Rig を Humanoid にして、その Avatar をルートの Animator に設定してください。";
        internal const string SourceMissingHumanBones = "選んだアバターにヒューマノイドに必要なボーンがないため、VRM に書き出せません。元のアバターは変更していません。足りないボーン: ";
        internal const string SourceHasNoMesh = "書き出せるメッシュがありません\nSkinned Mesh RendererかMesh Filter付きのMesh Rendererを持つアバターを選択";
        internal const string OutputHasNoMesh = "書き出し対象のメッシュがありません\n非表示のオブジェクトと除外の設定を見直す";

        // Unity's AvatarBuilder rejects a humanoid without any of these, so a
        // VRM missing one cannot be imported again as a humanoid.
        internal static readonly HumanBodyBones[] RequiredHumanBones = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand
        };

        static bool HasHumanoidAnimator(GameObject root)
        {
            var animator = root.GetComponent<Animator>();
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        // VRM 1.0 requires a humanoid. Without one every stage would run and the
        // saved file would then fail validation naming only a glTF bone key.
        internal static void ValidateSourceHumanoid(GameObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!HasHumanoidAnimator(source)) throw new InvalidOperationException(SourceNotHumanoid);
            // An inactive root has no bound skeleton yet, and a disabled Animator may
            // not resolve its bones either. The check on the prepared copy, whose root
            // is shown (ValidatePreparedHumanoid), still covers both cases.
            var animator = source.GetComponent<Animator>();
            if (!source.activeInHierarchy || !animator.enabled) return;
            var missing = RequiredHumanBones.Where(bone => animator.GetBoneTransform(bone) == null).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException(SourceMissingHumanBones + string.Join(", ", missing));
        }

        // Without any geometry UniVRM still saves a VRM with no mesh and the
        // expression, blink and tracking stages fail one by one. Inactive objects
        // and disabled renderers count, so only a source with no mesh at all stops.
        internal static void ValidateSourceHasMesh(GameObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var hasMesh = source.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                renderer is SkinnedMeshRenderer skin ? ExportRendererSelection.HasGeometry(skin.sharedMesh) :
                renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) && ExportRendererSelection.HasGeometry(filter.sharedMesh));
            if (!hasMesh) throw new InvalidOperationException(SourceHasNoMesh);
        }

        // Hidden objects, disabled renderers and exclusions pass the source check
        // but are not written, so check the saved file itself as well.
        internal static void ValidateOutputHasMesh(byte[] glb)
        {
            var json = GlbDocument.Read(glb).Json;
            if (!json.TryGetValue("meshes", out var meshes) || !(meshes is List<object> list) || list.Count == 0)
                throw new InvalidOperationException(OutputHasNoMesh);
        }

        // NDMF / optimizer passes may remove the root Animator or required
        // humanoid bones from the owned copy. Stop before UniVRM saves a VRM
        // that cannot be imported as a humanoid; the source is never touched.
        internal static void ValidatePreparedHumanoid(GameObject source, GameObject clone)
        {
            if (!HasHumanoidAnimator(source)) return;
            if (!HasHumanoidAnimator(clone)) throw new InvalidOperationException(LostHumanoidAnimator);
            var animator = clone.GetComponent<Animator>();
            var missing = RequiredHumanBones.Where(bone => animator.GetBoneTransform(bone) == null).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException(LostRequiredHumanBones + string.Join(", ", missing));
        }

        public static byte[] Export(GameObject source, string avatarName, string author, ICollection<string> warnings = null, bool suppressSharedTextureEmission = false,
            string exporterVersion = null, string lilToonVersion = null, bool suppressHdrTextureEmission = false,
            IEnumerable<GameObject> excludedObjects = null, MaterialBakeOptions bakeOptions = null, ExportGimmickOptions gimmickOptions = null, BlinkExportOptions blinkOptions = null, PoseExportOptions poseOptions = null,
            ExportRecoveryOptions recoveryOptions = null, ExportRecoveryReport recoveryReport = null, AvatarLicenseOptions licenseOptions = null, bool disableAudioLink = false,
            GameObject recoverySource = null, Func<Material, Material> recoveryMaterialSource = null, Action<GameObject> authorCopyExpressions = null,
            bool inferEmotionPresets = true, VrChatVisemeExport.Captured descriptorVisemes = null,
            bool inferNeutralAndGazePresets = false, IReadOnlyCollection<string> keepEmptyPresets = null)
        {
            var report = recoveryReport ?? new ExportRecoveryReport();
            report.Begin();
            report.DiagnosticSource = recoverySource;
            if (recoveryMaterialSource != null)
                foreach (var material in source.GetComponentsInChildren<Renderer>(true).SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct())
                    report.Track(material, recoveryMaterialSource(material));
            var guard = new ExportStageGuard(report.ContinueOnStageFailure);
            // Remedies found for each left-out stage, kept for the result list.
            var omittedDiagnostics = new Dictionary<ExportStage, List<ExportRecoveryDiagnostic>>();
            guard.OnOmit = (stage, error) =>
            {
                // Retried stages were already diagnosed by ExportCore before the
                // copy was destroyed; stages handled in place are diagnosed here.
                if (report.Diagnostics.Count == 0)
                    try { report.Fail(source, error); } catch { }
                omittedDiagnostics[stage] = report.Diagnostics.ToList();
                report.Diagnostics.Clear();
            };
            // A restarted attempt must not repeat the warnings of a discarded one.
            List<string> attemptWarnings = null;
            try
            {
                var bytes = guard.Retry(() =>
                {
                    attemptWarnings = guard.ContinueOnFailure && warnings != null ? new List<string>() : null;
                    return ExportCore(source, avatarName, author, attemptWarnings ?? warnings, suppressSharedTextureEmission, exporterVersion, lilToonVersion,
                        suppressHdrTextureEmission, excludedObjects, bakeOptions, gimmickOptions, blinkOptions, poseOptions, recoveryOptions, report, licenseOptions?.Copy(), disableAudioLink,
                        authorCopyExpressions, guard, inferEmotionPresets, descriptorVisemes, inferNeutralAndGazePresets, keepEmptyPresets);
                }, failure => report.Fallbacks.Clear());
                CopyWarnings(attemptWarnings, warnings);
                RecordOmissions(guard, report, warnings, omittedDiagnostics);
                report.Stage = "完了";
                report.Phase = ExportPhase.ResultList;
                report.Succeeded = true;
                report.ClearError();
                return bytes;
            }
            catch (Exception error)
            {
                CopyWarnings(attemptWarnings, warnings);
                var cause = ExportStageGuard.Unwrap(error);
                // A diagnostic failure must never replace the original export error.
                if (report.Diagnostics.Count == 0)
                    try { report.Fail(source, cause); } catch { }
                if (!ReferenceEquals(cause, error)) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw();
                throw;
            }
        }

        internal const string LostMorphFollowedWarning = "ビルド処理で表情「{0}」の変形「{1}」が別のRendererへ移ったため、移動先の同じ名前の変形に付け替えました。";
        internal const string LostMorphOmittedWarning = "ビルド処理で表情「{0}」の変形「{1}」が見つからなくなったため、その変形を省略しました。";
        internal const string LostExpressionOmittedWarning = "ビルド処理で表情「{0}」の変形がすべて見つからなくなったため、この表情を省略しました。";
        internal const string LostBlinkFollowedWarning = "ビルド処理で閉眼用の変形が別のRendererへ移ったため、移動先の同じ名前の変形に付け替えました。";
        internal const string LostBlinkWarning ="ビルド処理で閉眼用の変形を追えなくなったため、瞬きなしで書き出しました。";
        internal const string LostTrackingWarning = "ビルド処理で表情トラッキング用の変形を追えなくなったため、元の変形との照合を省略しました。";

        // The technical reason behind a lost-shape warning. Other languages show
        // it only when it has a translation, never as raw Japanese.
        static string LostDetail(string headline, string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";
            var shown = ExporterLocalization.T(detail);
            return ExporterLocalization.T(headline) == headline || shown != detail ? " (" + shown + ")" : "";
        }

        // Authored VRM clips and generated shapes that the build passes moved
        // or removed (lenient exports only).
        static void RecordLostMorphs(NdmfExportPreparation transformed, List<PreparedExpressionBindings.LostAuthoredMorph> lost,
            ICollection<string> warnings, ExportRecoveryReport report)
        {
            foreach (var shape in transformed.LostMorphs)
                report.Fallbacks.Record(ExportOutcome.Omitted, "表情", shape, ExporterLocalization.T(NdmfExportPreparation.LostGeneratedMorphs) + shape);
            foreach (var clip in lost.GroupBy(item => item.Clip))
            {
                if (clip.Any(item => item.ClipEmptied))
                {
                    var reason = string.Format(ExporterLocalization.T(LostExpressionOmittedWarning), clip.Key);
                    warnings?.Add(reason);
                    report.Fallbacks.Record(ExportOutcome.Omitted, "表情", clip.Key, reason);
                    continue;
                }
                foreach (var item in clip)
                {
                    var reason = string.Format(ExporterLocalization.T(item.Followed ? LostMorphFollowedWarning : LostMorphOmittedWarning), clip.Key, item.Shape);
                    warnings?.Add(reason);
                    report.Fallbacks.Record(ExportOutcome.Approximated, "表情", clip.Key, reason);
                }
            }
        }

        // Menu and FaceEmo entries are already left out one by one when their
        // target is gone after the build passes. List them in the results too.
        static void RecordLostMenuExpressions(VrChatExpressionMenu.Source menu, ExportRecoveryReport report)
        {
            foreach (var entry in menu?.Entries ?? new List<VrChatExpressionMenu.Entry>())
                if (entry.Error != null && (entry.Error.Contains(NdmfExportPreparation.UnknownRendererRelocation) ||
                    entry.Error.StartsWith(PreparedExpressionBindings.LostAfterPreparation, StringComparison.Ordinal)))
                    report.Fallbacks.Record(ExportOutcome.Omitted, "表情", entry.Name ?? entry.Id ?? "", entry.Error);
        }

        // A menu expression decided from its timed transitions or terminal
        // clip key is kept, but is an approximation of the moving original.
        internal static void RecordSettledMenuExpressions(VrChatExpressionMenu.Source menu, ExportRecoveryReport report)
        {
            foreach (var entry in menu?.Entries ?? new List<VrChatExpressionMenu.Entry>())
                if (entry.Error == null)
                    foreach (var message in ExpressionSettlement.Messages.Where(entry.Messages.Contains))
                        report.Fallbacks.Record(ExportOutcome.Approximated, "表情", entry.Name ?? entry.Id ?? "", message);
        }

        // An animated expression whose loop ends neutral shows no change in
        // apps that only read the VRM expression. Ask before saving.
        internal static void RecordExpressionsEndingNeutral(IEnumerable<VrmMenuExpressions.Expression> expressions, ExportRecoveryReport report)
        {
            foreach (var expression in expressions ?? Enumerable.Empty<VrmMenuExpressions.Expression>())
                if (expression.EndsNeutral) report.Fallbacks.Add(ExportFallbackKind.ExpressionEndsNeutral, expression.Name);
        }

        internal const string InferredPresetCategory = "名前から自動で設定";

        // Presets filled from a name or from the shapes a menu face moves
        // (emotions, mouth, neutral, gaze) are kept output the user should check.
        internal static void RecordInferredPresets(IEnumerable<(string Preset, string Source, string Format)> filled, ExportRecoveryReport report)
        {
            var category = ExporterLocalization.T(InferredPresetCategory);
            foreach (var (preset, source, format) in filled)
                report.Fallbacks.Record(ExportOutcome.Kept, category, preset,
                    string.Format(ExporterLocalization.T(format ?? VrmExpressionBindings.FilledFormat), preset, source));
        }

        internal const string UnofficialLilToonReason = "公式のlilToon 2.3.4のシェーダーではありません";
        internal const string StandardMaterialLimitWarning = "標準VRMの材質が{0}個あり、アプリの上限の{1}個を超えています。アプリで読み込めない場合があります。";
        internal const string StandardTextureLimitWarning = "標準VRMの画像が{0}枚あり、アプリの上限の{1}枚を超えています。アプリで読み込めない場合があります。";

        // A file without the lilToon extension is not checked against the app's
        // limits at save time, so say so when the standard VRM is still over them.
        static void RecordStandardOverLimit(byte[] bytes, ICollection<string> warnings, ExportRecoveryReport report)
        {
            var (materials, textures) = LilToonGlbExtension.StandardCounts(bytes);
            var category = ExporterLocalization.T("上限を超えた項目");
            if (materials > LilToonMobileProfile.MaximumMaterials)
            {
                var reason = string.Format(ExporterLocalization.T(StandardMaterialLimitWarning), materials, LilToonMobileProfile.MaximumMaterials);
                warnings?.Add(reason);
                report.Fallbacks.Record(ExportOutcome.Kept, category, ExporterLocalization.T("標準VRMの材質"), reason, materials);
            }
            if (textures > LilToonMobileProfile.MaximumTextures)
            {
                var reason = string.Format(ExporterLocalization.T(StandardTextureLimitWarning), textures, LilToonMobileProfile.MaximumTextures);
                warnings?.Add(reason);
                report.Fallbacks.Record(ExportOutcome.Kept, category, ExporterLocalization.T("標準VRMの画像"), reason, textures);
            }
        }

        internal const string OmittedGimmickCategory = "補助ギミック";

        // A gimmick left out by design is a result line, not a confirmation:
        // the export lost nothing the avatar shows in VRM.
        internal static void RecordOmittedGimmick(GameObject source, ExportGimmickFinding finding, ICollection<string> warnings, ExportRecoveryReport report)
        {
            // The result line is the record; a warning would repeat it as a notice.
            var path = ExportFallbackReport.RelativePath(source.transform, finding.Target.transform);
            var entry = report.Fallbacks.Record(ExportOutcome.Omitted, ExporterLocalization.T(OmittedGimmickCategory),
                string.IsNullOrEmpty(path) ? source.name : path, ExporterLocalization.T(finding.Reason));
            entry.RuleId = finding.RuleId;
            // The result list selects the object from its path, "" for the root.
            entry.Path = path ?? "";
        }

        internal const string HiddenRootWarning = "アバター本体または親が非表示だったため、書き出し用のコピーだけ表示にして書き出しました。シーンのアバターは変更していません。";

        static void RecordHiddenRoot(GameObject source, ICollection<string> warnings, ExportRecoveryReport report)
        {
            var reason = ExporterLocalization.T(HiddenRootWarning);
            warnings?.Add(reason);
            report.Fallbacks.Record(ExportOutcome.Kept, ExporterLocalization.T("非表示のアバター"), source.name, reason);
        }

        internal const string MissingShaderWarningFormat = "シェーダーが見つからないマテリアルが{0}個あります。VRMは保存しましたが、見た目を正しく再現できていません。アバターに必要なシェーダーをUnityプロジェクトに追加して、もう一度書き出してください。対象: ";

        const string MissingShaderName = "Hidden/InternalErrorShader";

        // Materials whose shader is missing from the project (Unity draws them
        // pink), each with the first renderer that uses it. Only renderers that
        // become glTF meshes count, as in UnsupportedShaderSubstitution. Empty
        // slots are left to the check for unassigned materials. On the export
        // copy, original maps a copied material back to the avatar's own, so
        // copies of one material count once and keep its name.
        internal static List<(Material Material, Renderer Renderer)> MissingShaderMaterials(GameObject root, Func<Material, Material> original = null)
        {
            var found = new List<(Material Material, Renderer Renderer)>();
            foreach (var renderer in ExportRendererSelection.Enumerate(root))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer is MeshRenderer && filter != null ? filter.sharedMesh : null;
                if (!ExportRendererSelection.HasGeometry(mesh)) continue;
                foreach (var slot in renderer.sharedMaterials)
                {
                    if (slot == null || slot.shader != null && slot.shader.name != MissingShaderName) continue;
                    var material = original?.Invoke(slot) ?? slot;
                    if (!found.Any(pair => pair.Material == material)) found.Add((material, renderer));
                }
            }
            return found;
        }

        // The export goes on, but the saved VRM will not look like the avatar:
        // one result line per material and one summary sentence at the top.
        // Runs once per attempt on the export copy after exclusions and the
        // build passes, before unsupported shaders are approximated, so only
        // materials that become glTF materials count.
        static void RecordMissingShaders(GameObject clone, ICollection<string> warnings, ExportRecoveryReport report)
        {
            var missing = MissingShaderMaterials(clone, report.SourceMaterial);
            // The one count every saved surface shows; names can repeat.
            report.Fallbacks.MissingShaderCount = missing.Count;
            if (missing.Count == 0) return;
            foreach (var (material, renderer) in missing)
                report.Fallbacks.Add(ExportFallbackKind.MissingShader, material.name, clone.transform, renderer.transform);
            var warning = string.Format(ExporterLocalization.T(MissingShaderWarningFormat), missing.Count) +
                string.Join(", ", missing.Select(pair => pair.Material.name));
            if (warnings is IList<string> list) list.Insert(0, warning);
            else warnings?.Add(warning);
        }

        static void CopyWarnings(List<string> attempt, ICollection<string> warnings)
        {
            if (attempt == null || warnings == null) return;
            foreach (var warning in attempt) warnings.Add(warning);
        }

        static void RecordOmissions(ExportStageGuard guard, ExportRecoveryReport report, ICollection<string> warnings,
            Dictionary<ExportStage, List<ExportRecoveryDiagnostic>> diagnostics)
        {
            foreach (var omission in guard.Omissions)
            {
                diagnostics.TryGetValue(omission.Stage, out var found);
                report.Fallbacks.AddStage(omission.Stage, omission.Error.Message, found);
                warnings?.Add(ExporterLocalization.T("書き出しの一部を省略しました") + ": " +
                    ExportStageGuard.DisplayName(omission.Stage) + " — " + omission.Error.Message);
            }
        }

        static byte[] ExportCore(GameObject source, string avatarName, string author, ICollection<string> warnings, bool suppressSharedTextureEmission,
            string exporterVersion, string lilToonVersion, bool suppressHdrTextureEmission, IEnumerable<GameObject> excludedObjects,
            MaterialBakeOptions bakeOptions, ExportGimmickOptions gimmickOptions, BlinkExportOptions blinkOptions, PoseExportOptions poseOptions,
            ExportRecoveryOptions recoveryOptions, ExportRecoveryReport recoveryReport, AvatarLicenseOptions licenseOptions, bool disableAudioLink,
            Action<GameObject> authorCopyExpressions, ExportStageGuard guard, bool inferEmotionPresets, VrChatVisemeExport.Captured descriptorVisemes,
            bool inferNeutralAndGazePresets, IReadOnlyCollection<string> keepEmptyPresets)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(avatarName)) throw new InvalidOperationException("アバター名を取得できませんでした。");
            if (string.IsNullOrWhiteSpace(author)) throw new InvalidOperationException("作者名を入力してください。");

            EnsureUniVrmVersion();
            // A hidden avatar or hidden parent does not stop the export. Only the
            // owned copy's root is shown; the scene object is never changed, and
            // the avatar's own inactive children stay out as usual.
            if (ExportRendererSelection.HiddenInScene(source)) RecordHiddenRoot(source, warnings, recoveryReport);
            recoveryReport.Stage = "原本検査";
            recoveryReport.Phase = ExportPhase.Precheck;
            var trackingProfile = source.GetComponentInChildren<VrmTrackingMarker>(true)?.profile;
            var manualObjects = (excludedObjects ?? Array.Empty<GameObject>()).ToArray();
            gimmickOptions = new ExportGimmickOptions
            {
                AutoExclude = gimmickOptions?.AutoExclude != false,
                IncludedObjects = (gimmickOptions?.IncludedObjects ?? Array.Empty<GameObject>()).ToArray(),
                IncludedComponents = (gimmickOptions?.IncludedComponents ?? Array.Empty<Component>()).ToArray()
            };
            using var manualExclusions = new ExportObjectExclusions(source, manualObjects);
            var findings = gimmickOptions.AutoExclude
                ? guard.Run(ExportStage.GimmickDetection, () => ExportGimmickDetection.Analyze(source, manualExclusions.Contains), () => new List<ExportGimmickFinding>())
                : new List<ExportGimmickFinding>();
            // A failed detection keeps every object instead of guessing.
            if (guard.IsOmitted(ExportStage.GimmickDetection)) gimmickOptions.AutoExclude = false;
            var automaticRoots = ExportGimmickDetection.AutomaticRoots(findings, gimmickOptions).ToArray();
            using var exclusions = new ExportObjectExclusions(source, manualObjects.Concat(automaticRoots));
            var automaticComponents = ExportGimmickDetection.AutomaticComponents(findings, gimmickOptions).ToArray();
            foreach (var finding in findings.Where(f => f.Renderer == null))
                if (automaticRoots.Contains(finding.Target) || automaticComponents.Contains(finding)) RecordOmittedGimmick(source, finding, warnings, recoveryReport);
                else if (finding.Unit == GimmickExclusionUnit.Review) warnings?.Add("自動除外せず保持: " + finding.Target.name + " — " + finding.Reason);
            // Preserve rejection of effective external/excluded source targets.
            // Only unresolved getters wait for the selected root on the copy.
            guard.Run(ExportStage.SourceValidation, () => NdmfExportPreparation.ValidateSource(source, exclusions.Contains, deferUnresolvedTargets: true));
            // Re-read the live assets on every export; a preview is never a stale
            // cached source of expression weights after the user edits a clip.
            var menuPolicy = ExportRecoveryReport.MenuImportPolicy(source, recoveryOptions);
            recoveryReport.Stage = "表情メニュー読込";
            recoveryReport.Phase = ExportPhase.Expressions;
            var menu = guard.Run(ExportStage.ExpressionMenu, () =>
            {
                var read = VrChatExpressionMenu.Read(source, menuPolicy);
                exclusions.FilterExpressions(read, warnings);
                return read;
            }, () => new VrChatExpressionMenu.Source());
            // Without the menu, later passes must not read it again either.
            if (guard.IsOmitted(ExportStage.ExpressionMenu)) menuPolicy = new VrChatMenuImportPolicy { SkipAll = true };
            var sourceBlink = guard.Run(ExportStage.BlinkCapture,
                () => BlinkExportSession.CaptureForExport(source, blinkOptions, exclusions.Contains, suppressSharedTextureEmission, suppressHdrTextureEmission),
                () => BlinkExportSession.CaptureForExport(source, new BlinkExportOptions { Mode = BlinkExportMode.None }, exclusions.Contains,
                    suppressSharedTextureEmission, suppressHdrTextureEmission));
            using var poses = guard.Run(ExportStage.PoseCapture, () => new PoseExportSession(source, poseOptions, exclusions.Contains, menuPolicy),
                () => PoseExportSession.None(source));
            recoveryReport.Stage = "コピー作成";
            recoveryReport.Phase = ExportPhase.Precheck;
            var clone = NdmfExportPreparation.InstantiateOwnedCopy(source);
            clone.name = source.name;
            var temporaryMaterials = new List<Material>();
            var temporaryMeshes = new List<Mesh>();
            var temporaryTextures = new List<Texture2D>();
            var fixedRootJoints = new HashSet<Transform>();
            ExportOptimizationBindings optimizerBindings = null;
            AuthoredExpressionEndpoints authoredEndpoints = null;
            try
            {
                // MA resolves against the selected avatar after detaching its
                // owned copy, just as NDMF does when it creates BuildContext.
                // Source getters can use an enclosing root or no root at all.
                guard.Step(ExportStage.CopyValidation, () => NdmfExportPreparation.ValidateCopy(source, clone,
                    target => exclusions.ContainsCopyTransform(clone, target)));
                var recovery = recoveryOptions == null || recoveryOptions.Actions.Count == 0 ? null : new ExportRecoveryCopySession(source, clone, temporaryMaterials, recoveryOptions, recoveryReport, bakeOptions);
                using var blink = guard.Watch(ExportStage.BlinkCapture, () => sourceBlink.ForClone(source, clone));
                // Lip Sync captured before the caller removed the descriptor
                // follows this copy and the optimizer the same way as blink.
                var capturedVisemes = descriptorVisemes?.ForClone(source, clone);
                using var gimmicks = guard.Watch(ExportStage.GimmickDetection, () => new ExportGimmickSession(source, clone, gimmickOptions, findings));
                bool ExcludedBinding(string path) => exclusions.ContainsPath(path) || gimmicks.ContainsPath(path);
                using var expressionBindings = new PreparedExpressionBindings(clone, menu, excludedPath: ExcludedBinding);
                guard.Step(ExportStage.MaAppearance, () => MaAppearanceSnapshot.Apply(source, clone, temporaryMeshes, exclusions, warnings));
                PoseExportSession.RemoveAplFromCopy(source, clone);
                recovery?.Apply(warnings);
                if (disableAudioLink) DisableAudioLinkOnCopy(clone, temporaryMaterials, (copy, original) => recoveryReport.Track(copy, original));
                guard.Watch(ExportStage.GimmickDetection, () => gimmicks.Apply(null, menu, warnings));
                if (exporterVersion == null) LilToonMainTextureBaker.ValidateAvatar(clone, options: bakeOptions);
                // Omission consent follows source identity before plugins clone
                // materials. All actual baking waits for the final appearance.
                if (exporterVersion == null) LilToonMainTextureBaker.ApplyOmissions(clone, temporaryMaterials, warnings, bakeOptions);
                var requiresPreparation = NdmfExportPreparation.NeedsProcessing(clone);
                if (requiresPreparation)
                    SkinnedMeshFallbackWeights.Preserve(clone, temporaryMeshes, warnings, fixedRootJoints);
                var unifiedPreparation = trackingProfile == null ? new UnifiedExpressionPreparation(clone,
                    suppressSharedTextureEmission: suppressSharedTextureEmission, suppressHdrTextureEmission: suppressHdrTextureEmission) : null;
                var faceEmoBindings = guard.Step(ExportStage.FaceEmo, () => FaceEmoExpressions.Capture(source, clone, ExcludedBinding,
                    expressionBindings.ExcludesPreparedPath, deferPermanentOverrides: true), () => null);
                List<VrmMenuExpressions.Expression> expressions = null;
                recoveryReport.Stage = "ビルド処理";
                recoveryReport.Phase = ExportPhase.Skeleton;
                // Caller-selected faces join the copy as authored VRM clips, so
                // the same identity capture, relocation and neutral baking apply.
                authorCopyExpressions?.Invoke(clone);
                expressionBindings.CaptureAuthoredExpressions();
                using var preparation = guard.Watch(ExportStage.BuildProcess, () => NdmfExportPreparation.Prepare(source, clone, warnings, (copy, original) =>
                {
                    recoveryReport.Track(copy, original);
                    recovery?.Track(copy, original);
                }, afterTransforming: transformed =>
                {
                    // A lenient export keeps the build passes: lost shapes are
                    // followed by name, and only what cannot be found is left out.
                    var lenient = guard.ContinueOnFailure;
                    expressionBindings.RebindPrepared(transformed.PreparedRendererFor);
                    var lostAuthored = lenient ? new List<PreparedExpressionBindings.LostAuthoredMorph>() : null;
                    expressionBindings.RebindAuthoredExpressions(transformed.IsolatedCopyOf, lenient ? transformed.FollowShape : null, lostAuthored);
                    if (lenient) RecordLostMorphs(transformed, lostAuthored, warnings, recoveryReport);
                    faceEmoBindings?.RebindPrepared(transformed.PreparedRendererFor,
                        transformed.ObjectRegistry, transformed.IsolatedCopyOf);
                    if (!lenient)
                    {
                        guard.Watch(ExportStage.BlinkCapture, () => blink.RebindPrepared(transformed.PreparedRendererFor));
                        capturedVisemes?.RebindPrepared(transformed.PreparedRendererFor);
                        unifiedPreparation?.RebindPrepared(transformed.PreparedRendererFor);
                        guard.Watch(ExportStage.BlinkCapture, () => blink.VerifyPreparedIdentity(clone));
                        unifiedPreparation?.VerifyIdentityAndDeformation();
                    }
                    else
                    {
                        if (!blink.FollowPrepared(clone, transformed.FollowShape, out var blinkMoved, out var blinkReason))
                        {
                            var reason = ExporterLocalization.T(LostBlinkWarning);
                            warnings?.Add(reason + LostDetail(LostBlinkWarning, blinkReason));
                            recoveryReport.Fallbacks.Add(ExportFallbackKind.Blink, reason: reason);
                        }
                        else if (blinkMoved)
                        {
                            var reason = ExporterLocalization.T(LostBlinkFollowedWarning);
                            warnings?.Add(reason);
                            recoveryReport.Fallbacks.Record(ExportOutcome.Approximated, "瞬き", "", reason);
                        }
                        // A Lip Sync mesh that cannot be followed is treated as not exported.
                        // Only names the mesh had before the build passes are followed,
                        // so one stale Lip Sync name does not lose the other mouth
                        // shapes. A renderer that survived is checked against its
                        // current mesh, so a shape a pass stripped is left out on its
                        // own; one a pass destroyed or merged away is followed by the
                        // names it had before.
                        capturedVisemes?.RebindPrepared(old => transformed.FollowShapes(old, capturedVisemes.Shapes.Values.Where(shape =>
                            !string.IsNullOrEmpty(shape) && transformed.HadShape(old, shape) &&
                            (old == null || (old.sharedMesh != null && old.sharedMesh.GetBlendShapeIndex(shape) >= 0)))));
                        var unified = unifiedPreparation;
                        try
                        {
                            unified?.RebindPrepared(old => transformed.FollowShapes(old, unified.ShapesOf(old)));
                            unified?.VerifyIdentityAndDeformation();
                        }
                        catch (Exception error) when (!(error is ExportStageFailure) && !ExportStageGuard.IsCancellation(error))
                        {
                            // Tracking is captured again from the baked copy below;
                            // only the check against the original routes is lost.
                            unifiedPreparation = null;
                            var reason = ExporterLocalization.T(LostTrackingWarning);
                            warnings?.Add(reason + LostDetail(LostTrackingWarning, error.Message));
                            recoveryReport.Fallbacks.Record(ExportOutcome.Approximated, "表情トラッキング", "", reason);
                        }
                    }
                    var preparedMenuPolicy = menuPolicy?.WithOwnedCopies(value => transformed.IsolatedCopyOf(value as UnityEngine.Object));
                    // Analyze every endpoint and neutral from the same prepared
                    // authored weights; fractional/additive FX is applied once.
                    menu = guard.Step(ExportStage.Expressions, () => VrChatExpressionSampler.Analyze(clone, expressionBindings.ExcludesPreparedPath,
                        preparedMenuPolicy, source, faceEmoBindings), () => new VrChatExpressionMenu.Source());
                    // Analyzed curves use the prepared hierarchy. Keep the
                    // earlier bindings only for authoring identity/exclusions.
                    using var preparedMenuBindings = new PreparedExpressionBindings(clone, menu,
                        excludedPath: expressionBindings.ExcludesPreparedPath);
                    guard.Watch(ExportStage.GimmickDetection, () => gimmicks.Apply(preparedMenuBindings, menu, warnings));
                    guard.Step(ExportStage.FaceEmo, () => FaceEmoExpressions.ApplyPreparedDefaultFace(clone, menu, preparedMenuBindings,
                        preparedMenuBindings.ExcludesPreparedPath, faceEmoBindings));
                    guard.Watch(ExportStage.PoseCapture, () => poses.CollectPrepared(clone, warnings, preparedMenuPolicy));
                    // Our own steps below name their stage. Unnamed, a failure here
                    // would be blamed on the build passes and retried without them.
                    // Stopping from the limit window cancels; the guard rethrows it.
                    guard.Watch(ExportStage.PoseCapture, () => PoseLimitChooser.Apply(poses, warnings, recoveryReport));
                    recoveryReport.Stage = "基準形評価";
                    recoveryReport.Phase = ExportPhase.Expressions;
                    // This beta retains the prepared copy's rest values. Selected
                    // expressions still use their original FX evaluation above;
                    // all endpoints below share this retained neutral snapshot.
                    warnings?.Add(ExporterLocalization.T("このベータ版ではFXの初期状態の復元を省略し、書き出し用コピーの設定を基本の顔・体形として保存しました。VRChatでの通常時の見た目と異なる場合があります。"));
                    guard.Watch(ExportStage.Expressions, () => AvatarBaseShape.NormalizeSourceWeights(clone, UnityEditor.PlayerSettings.legacyClampBlendShapeWeights));
                    guard.Watch(ExportStage.BlinkCapture, () => blink.ResolvePreparedNeutral(clone, suppressSharedTextureEmission: suppressSharedTextureEmission,
                        suppressHdrTextureEmission: suppressHdrTextureEmission));
                    if (blink.MissingAutomatic)
                    {
                        warnings?.Add(ExporterLocalization.T(BlinkExportSession.MissingAutomaticWarning));
                        recoveryReport.Fallbacks.Add(ExportFallbackKind.Blink);
                    }
                    if (blink.UnresolvedAutomatic)
                    {
                        warnings?.Add(ExporterLocalization.T(BlinkExportSession.UnresolvedAutomaticWarning) + " " + ExporterLocalization.T(blink.UnresolvedReason));
                        recoveryReport.Fallbacks.Add(ExportFallbackKind.Blink);
                    }
                    var neutral = guard.Watch(ExportStage.Expressions, () => NeutralShapeSnapshot.Capture(clone));
                    authoredEndpoints = guard.Watch(ExportStage.Expressions, () => AuthoredExpressionEndpoints.Capture(clone, neutral, trackingProfile));
                    guard.Step(ExportStage.Expressions, () => preparedMenuBindings.Capture(menu));
                    guard.Watch(ExportStage.Expressions, () =>
                    {
                        if (lenient) RecordLostMenuExpressions(menu, recoveryReport);
                        RecordSettledMenuExpressions(menu, recoveryReport);
                    });
                    guard.Watch(ExportStage.BlinkCapture, () => blink.Bake(clone, temporaryMeshes));
                    guard.Watch(ExportStage.Expressions, () =>
                    {
                        AvatarBaseShape.Preserve(clone, clone, temporaryMeshes, warnings);
                        authoredEndpoints.Bake(temporaryMeshes);
                    });
                    expressions = guard.Step(ExportStage.Expressions,
                        () => VrChatExpressionBaker.Bake(null, clone, menu, temporaryMeshes, warnings, preparedMenuBindings), () => new List<VrmMenuExpressions.Expression>());
                    guard.Watch(ExportStage.Expressions, () => RecordExpressionsEndingNeutral(expressions, recoveryReport));
                    // Generated absolute endpoints now define the protected
                    // routes for both automatic and explicitly authored tracking.
                    unifiedPreparation = trackingProfile == null ? guard.Watch(ExportStage.TrackingOutput, () => new UnifiedExpressionPreparation(clone,
                        suppressSharedTextureEmission: suppressSharedTextureEmission, suppressHdrTextureEmission: suppressHdrTextureEmission)) : null;
                    optimizerBindings = guard.Watch(ExportStage.OptimizerEvidence, () => ExportOptimizationBindings.Capture(clone,
                        trackingProfile == null ? null : authoredEndpoints.TrackingProfile, unifiedPreparation,
                        objectRegistry: transformed.ObjectRegistry, neutral: neutral, preservePreparedNeutral: true,
                        menuMorphs: expressions.SelectMany(expression => expression.Direct.Select(direct => (direct.Renderer, direct.Shape))
                            .Concat(expression.DirectPoints.Values)),
                        keptMorphs: capturedVisemes?.Morphs(clone)));
                }, skipProcessing: guard.IsOmitted(ExportStage.BuildProcess), tolerateLostMorphs: guard.ContinueOnFailure));
                // AAO can remove the root Animator (generic platform) or
                // required humanoid bones; UniVRM would then save silently.
                // A lenient export retries without the build passes; a source
                // that already lacks them still stops.
                guard.Watch(ExportStage.BuildProcess, () => ValidatePreparedHumanoid(source, clone));
                optimizerBindings.ValidateAndApply((copy, original) =>
                {
                    if (!(copy is Material copiedMaterial) || !(original is Material originalMaterial)) return;
                    recoveryReport.Track(copiedMaterial, originalMaterial);
                    recovery?.Track(copiedMaterial, originalMaterial);
                });
                guard.Watch(ExportStage.BlinkCapture, () => blink.Remap(optimizerBindings));
                capturedVisemes?.Remap(optimizerBindings);
                // Direct references stay as captured; MenuBinding resolves them later.
                guard.Step(ExportStage.Expressions, () => RemapMenuExpressions(expressions, optimizerBindings));
                // Authoring passes may restore source material references. Reapply
                // only the selected, source-identity recipe to the owned copy.
                recovery?.Apply();
                // NDMF/authoring passes can introduce materials or restore source
                // references. Disable external audio on the final owned copy too.
                if (disableAudioLink) DisableAudioLinkOnCopy(clone, temporaryMaterials, (copy, original) => recoveryReport.Track(copy, original));
                if(exporterVersion!=null) PreserveExtraMaterialSlots(clone,temporaryMeshes);
                recoveryReport.Stage = "材質保存";
                recoveryReport.Phase = ExportPhase.Materials;
                // Excluded objects are gone from the copy by now; the approximation
                // below would hide which materials had lost their shader.
                RecordMissingShaders(clone, warnings, recoveryReport);
                // Unsupported shaders become official lilToon approximations so
                // they follow the ordinary lilToon route instead of stopping.
                if (exporterVersion != null) guard.Step(ExportStage.LilToonSnapshot, () => UnsupportedShaderSubstitution.Apply(clone, temporaryMaterials, warnings, recoveryReport.Fallbacks, (copy, original) =>
                {
                    recoveryReport.Track(copy, original);
                    recovery?.Track(copy, original);
                }));
                var fullSnapshot = exporterVersion != null ? guard.Step(ExportStage.LilToonSnapshot,
                    () => LilToonFullSnapshot.Capture(clone,suppressSharedTextureEmission,suppressHdrTextureEmission, warnings, animationSource: source), () => null) : null;
                var fallbackWarnings=fullSnapshot==null?warnings:new List<string>();
                // With no lilToon material at all the lilToon extension has nothing to
                // carry. Save a plain MToon VRM that is marked as intentionally so.
                var withoutLilToon = fullSnapshot != null && fullSnapshot.IsEmpty;
                // Derived lilToon builds were approximated above. One still left here
                // could not be, and is not "no lilToon": strict exports stop there;
                // otherwise each such material becomes color-and-image MToon and is
                // listed as approximated.
                if (withoutLilToon && !guard.ContinueOnFailure &&
                    ExportRendererSelection.Enumerate(clone).SelectMany(renderer => renderer.sharedMaterials).Any(LilToonMaterialReader.IsLilToon))
                    throw new InvalidOperationException("公式lilToon材質が見つかりません。");
                if (withoutLilToon && guard.ContinueOnFailure)
                    foreach (var material in ExportRendererSelection.Enumerate(clone).SelectMany(renderer => renderer.sharedMaterials)
                                 .Where(material => material != null && LilToonMaterialReader.IsLilToon(material)).Distinct())
                    {
                        var reason = ExporterLocalization.T(UnofficialLilToonReason);
                        warnings?.Add(ExporterLocalization.T(MaterialApproximatedWarning) + material.name + " — " + reason);
                        recoveryReport.Fallbacks.Add(ExportFallbackKind.MaterialApproximated, material.name, reason: reason);
                    }
                if (withoutLilToon) { fullSnapshot = null; fallbackWarnings = warnings; }
                guard.Step(ExportStage.TextureBake, () =>
                {
                    if (fullSnapshot == null) LilToonMainTextureBaker.ValidateAvatar(clone);
                    LilToonMainTextureBaker.Prepare(clone, temporaryMaterials, temporaryTextures, fallbackWarnings, suppressSharedTextureEmission, suppressHdrTextureEmission, approximationOnly: fullSnapshot != null);
                });
                // Also cover meshes/joints newly created by authoring passes.
                SkinnedMeshFallbackWeights.Preserve(clone, temporaryMeshes, warnings, fixedRootJoints);
                // MA has already applied its authored Merge Armature / Bone
                // Proxy connections. Independent props are not evidence of a
                // failed hair attachment and must not trigger a modal edit.
                using var attachments = guard.Step(ExportStage.Attachments,
                    () => new ExportAttachmentSession(source, clone, fixedRootJoints: fixedRootJoints), () => null);
                if (attachments != null && attachments.Parts.Count > 0)
                    warnings?.Add("本体のボーンと独立したパーツは現在の接続を保持しました: " +
                        string.Join(", ", attachments.Parts.ConvertAll(part => part.Root.name)));
                recoveryReport.Stage = "揺れ物変換";
                recoveryReport.Phase = ExportPhase.Springs;
                var springs = guard.Step(ExportStage.PhysBone, () => PhysBoneSpringExport.Convert(source, clone, warnings, recoveryReport.Fallbacks), () => null);
                recoveryReport.Stage = "コンストレイント変換";
                recoveryReport.Phase = ExportPhase.Skeleton;
                guard.Step(ExportStage.Constraints, () => ConstraintExport.Convert(source, clone, warnings, recoveryReport.Fallbacks), () => null);
                recoveryReport.Stage = "材質保存";
                recoveryReport.Phase = ExportPhase.Materials;
                ReplaceLilToonMaterials(clone, temporaryMaterials, temporaryTextures, fallbackWarnings, suppressSharedTextureEmission, convertOthers: withoutLilToon, fallbacks: recoveryReport.Fallbacks,
                    lenient: guard.ContinueOnFailure);
                guard.Run(ExportStage.OptimizerEvidence, () => optimizerBindings.VerifyMaterialEvidence());
                MakeRendererMeshesUnique(clone, temporaryMeshes);
                recoveryReport.Stage = "VRM変換";
                recoveryReport.Phase = ExportPhase.Export;
                var lookAt = guard.Run(ExportStage.LookAt, () => VrmLookAtExport.Capture(source, clone, warnings), () => null);
                DisableEmptyMeshRenderers(clone, warnings);
                // The root scale RootScaleBake moves into the VRM model space.
                var rootScale = clone.transform.localScale;
                // Every capture above is taken; objects with nothing to write
                // would only be empty nodes. Normal output: the Console only.
                if (gimmickOptions.AutoExclude)
                {
                    var pruned = ExportEmptyNodes.Prune(clone);
                    if (pruned > 0) Debug.Log($"VR Vlog: left {pruned} empty objects out of the VRM.");
                }
                VrmExpressionBindings.DescriptorVisemes visemes = null;
                var exported = Vrm10AppearanceExporter.Export(
                    new GltfExportSettings { ExportVertexColor = true },
                    clone,
                    materialExporter: new BuiltInVrm10MaterialExporter(),
                    textureSerializer: new MobileTextureSerializer(warnings),
                    vrmMeta: CreateMeta(avatarName.Trim(), author.Trim(), licenseOptions),
                    afterExport: (converter, model, storage) =>
                    {
                        if (fullSnapshot != null) guard.Step(ExportStage.LilToonSnapshot, () => fullSnapshot.Bind(converter, model, storage));
                        guard.Watch(ExportStage.BlinkCapture, () => blink.Bind(converter, model, storage));
                        guard.Watch(ExportStage.PoseCapture, () => poses.Bind(converter, model, storage));
                        optimizerBindings.Bind(converter, model, storage);
                        int OutputNode(GameObject go) => converter.Nodes.TryGetValue(go, out var node) ? model.Nodes.IndexOf(node) : -1;
                        visemes = capturedVisemes != null ? capturedVisemes.Resolve(OutputNode) : VrChatVisemeExport.Capture(clone, OutputNode);
                    }, warnings: warnings);
                recoveryReport.Stage = "出力検査";
                recoveryReport.Phase = ExportPhase.Export;
                // Output stages transform finished bytes; a failing one keeps the
                // bytes it was given.
                exported = guard.Run(ExportStage.SkinRepair, () => ExportSkinRoots.Repair(exported, warnings), () => exported);
                if (lookAt != null) exported = guard.Run(ExportStage.LookAt, () => lookAt.Apply(exported), () => exported);
                // Empty emotion presets are filled from menu faces, whole-face
                // names, then the shapes an unnamed menu face moves; empty mouth,
                // neutral and gaze presets from names. Each filled preset is
                // listed in the warnings and the result list.
                exported = guard.Run(ExportStage.ExpressionOutput, () =>
                {
                    var filled = new List<(string Preset, string Source, string Format)>();
                    // Only the faces Add registered may fill an empty emotion;
                    // their names no longer carry a source prefix, so each is
                    // inferred from its menu path as before.
                    var menuOutput = VrmMenuExpressions.Add(exported, expressions, optimizerBindings, warnings);
                    // Empty mouth presets take the VRChat Lip Sync shapes
                    // before the known names, as the expression board does.
                    var output = VrmExpressionBindings.AddMissing(menuOutput, warnings, inferBlink: false,
                        inferEmotions: inferEmotionPresets, filledPresets: filled,
                        emotionCandidates: VrmMenuExpressions.EmotionCandidates(expressions),
                        emotionShapes: VrmMenuExpressions.EmotionShapes(expressions), shapeNames: ExpressionShapeNames.Shipped,
                        descriptor: visemes, inferNeutralAndGaze: inferNeutralAndGazePresets, keepEmptyPresets: keepEmptyPresets);
                    RecordInferredPresets(filled, recoveryReport);
                    return output;
                }, () => exported);
                exported = guard.Run(ExportStage.BlinkOutput, () => blink.Apply(exported), () => exported);
                exported = guard.Run(ExportStage.TrackingOutput, () =>
                {
                    var tracked = trackingProfile != null ? optimizerBindings.ApplyTracking(exported) : optimizerBindings.ApplyUnified(exported, warnings);
                    if (blink.RequiresUnifiedEvidence && !VrmUnifiedExpressions.HasUsableEvidence(tracked))
                        throw new InvalidOperationException(UnifiedExpressionPreparation.LostTracking);
                    return tracked;
                }, () => exported);
                // After every expression is bound: BlendShapes that nothing
                // binds and the app does not drive by name are left out.
                exported = guard.Run(ExportStage.UnusedMorphs, () =>
                {
                    // Normal output, not a fallback: the Console only, no
                    // warning and no result line.
                    var skipped = new List<string>();
                    var removed = new List<string>();
                    var stripped = UnusedMorphTargets.Strip(exported, skipped, removed);
                    foreach (var message in skipped) Debug.LogWarning("VR Vlog: " + message);
                    // The first 20 names only: a real avatar leaves out hundreds.
                    if (removed.Count > 0)
                        Debug.Log($"VR Vlog: left {removed.Count} unused BlendShapes out of the VRM: " + string.Join(", ", removed.Take(20)) +
                                  (removed.Count > 20 ? $" … (+{removed.Count - 20} more)" : ""));
                    return stripped;
                }, () => exported);
                var marked = false;
                if (fullSnapshot != null)
                {
                    // The dedicated snapshot predates fallback baking. Its binary
                    // payloads no longer depend on mutable Unity texture objects.
                    // Over the app's material or texture limit the file keeps
                    // only the standard VRM materials.
                    var injected = guard.Run(ExportStage.LilToonExtension, () =>
                    {
                        var withExtension = fullSnapshot.Inject(exported, exporterVersion, lilToonVersion);
                        // The save check would reject this file; a lenient export drops the extension instead.
                        if (guard.ContinueOnFailure) LilToonGlbExtension.RequireWithinLimits(withExtension);
                        return withExtension;
                    }, () => null);
                    marked = injected == null;
                    if (injected != null)
                    {
                        exported = injected;
                        foreach(var warning in fallbackWarnings)warnings?.Add("標準VRM表示の近似: "+warning);
                    }
                    else
                    {
                        // The save check accepts a file without the extension only when marked.
                        exported = LilToonGlbExtension.MarkWithoutLilToon(exported);
                        foreach(var warning in fallbackWarnings)warnings?.Add(warning);
                    }
                }
                else if (marked = withoutLilToon || guard.IsOmitted(ExportStage.LilToonSnapshot)) exported = LilToonGlbExtension.MarkWithoutLilToon(exported);
                if (marked && guard.ContinueOnFailure) RecordStandardOverLimit(exported, warnings, recoveryReport);
                guard.Run(ExportStage.PhysBoneVerify, () => PhysBoneSpringExport.VerifyOutput(exported, springs));
                exported = guard.Run(ExportStage.PoseOutput, () => poses.Inject(exported, rootScale), () => exported);
                return AvatarFormatRequirements.Write(exported);
            }
            catch (Exception error)
            {
                // Capture Unity-object targets before temporary objects disappear.
                try { recoveryReport.Fail(source, ExportStageGuard.Unwrap(error)); } catch { }
                throw;
            }
            finally
            {
                optimizerBindings?.Dispose();
                authoredEndpoints?.Dispose();
                UnityEngine.Object.DestroyImmediate(clone);
                foreach (var material in temporaryMaterials) UnityEngine.Object.DestroyImmediate(material);
                foreach (var mesh in temporaryMeshes) UnityEngine.Object.DestroyImmediate(mesh);
                foreach (var texture in temporaryTextures) UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        internal static void DisableAudioLinkOnCopy(GameObject copy, ICollection<Material> owned,
            Action<Material, Material> copyObserver = null)
        {
            if (copy == null || UnityEditor.EditorUtility.IsPersistent(copy))
                throw new ArgumentException("An independent export copy is required.", nameof(copy));
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            var controls = new[] { "_UseAudioLink", "_AudioLinkAsLocal", "_AudioLink2Main2nd", "_AudioLink2Main3rd",
                "_AudioLink2Emission", "_AudioLink2EmissionGrad", "_AudioLink2Emission2nd", "_AudioLink2Emission2ndGrad", "_AudioLink2Vertex" };
            // Official lilToon Multi uses these keywords exclusively for AudioLink.
            var audioKeywords = new[] { "_MAPPING_6_FRAMES_LAYOUT", "_SUNDISK_HIGH_QUALITY" };
            var replacements = new Dictionary<Material, Material>();
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < slots.Length; i++)
                {
                    var original = slots[i];
                    if (original == null) continue;
                    var official = ExportRecoveryReport.Official(original);
                    if (!controls.Any(property => original.HasProperty(property) && original.GetFloat(property) != 0) &&
                        !(official && original.HasProperty("_UseAudioLink") && audioKeywords.Any(original.IsKeywordEnabled))) continue;
                    if (!replacements.TryGetValue(original, out var material))
                    {
                        material = new Material(original) { name = original.name };
                        owned.Add(material);
                        replacements.Add(original, material);
                        foreach (var property in controls)
                            if (material.HasProperty(property)) material.SetFloat(property, 0);
                        if (official) foreach (var keyword in audioKeywords) material.DisableKeyword(keyword);
                        copyObserver?.Invoke(material, original);
                    }
                    slots[i] = material;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = slots;
            }
        }

        private static void RemapMenuExpressions(IEnumerable<VrmMenuExpressions.Expression> expressions,
            ExportOptimizationBindings bindings)
        {
            foreach (var expression in expressions)
            {
                for (var index = 0; index < expression.Targets.Count; index++)
                    expression.Targets[index] = bindings.MapGeneratedName(expression.Targets[index]);
                if (expression.Animation == null) continue;
                foreach (var channel in expression.Animation.Channels)
                    foreach (var point in channel.Points)
                        if (point.Target != null) point.Target = bindings.MapGeneratedName(point.Target);
            }
        }

        static void PreserveExtraMaterialSlots(GameObject avatar,ICollection<Mesh> owned)
        {
            foreach(var renderer in ExportRendererSelection.Enumerate(avatar))
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                var filter=renderer.GetComponent<MeshFilter>();
                var original=renderer is SkinnedMeshRenderer skin?skin.sharedMesh:filter != null ? filter.sharedMesh : null;
                var slots=renderer.sharedMaterials.Length;
                if(original==null || original.subMeshCount==0 || slots<=original.subMeshCount)continue;
                // Unity draws the last submesh once per extra material. glTF
                // represents those additional draws as explicit primitives.
                var mesh=UnityEngine.Object.Instantiate(original);owned.Add(mesh);
                var count=mesh.subMeshCount;var indices=mesh.GetIndices(count-1);var topology=mesh.GetTopology(count-1);
                mesh.subMeshCount=slots;
                for(var slot=count;slot<slots;slot++)mesh.SetIndices(indices,topology,slot,false);
                if(renderer is SkinnedMeshRenderer skinned)skinned.sharedMesh=mesh;
                else renderer.GetComponent<MeshFilter>().sharedMesh=mesh;
            }
        }

        internal static void MakeRendererMeshesUnique(GameObject clone, ICollection<Mesh> owned)
        {
            // UniVRM 0.131 keys exported skins by Mesh and throws when two
            // renderers share one. Split only the export copy's mesh references.
            var seen = new HashSet<Mesh>();
            foreach (var renderer in ExportRendererSelection.Enumerate(clone))
            {
                var skin = renderer as SkinnedMeshRenderer;
                var filter = skin == null ? renderer.GetComponent<MeshFilter>() : null;
                var mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null || seen.Add(mesh)) continue;
                var copy = UnityEngine.Object.Instantiate(mesh);
                copy.name = mesh.name;
                owned.Add(copy);
                if (skin == null) filter.sharedMesh = copy;
                else
                {
                    var weights = new float[mesh.blendShapeCount];
                    for (var i = 0; i < weights.Length; i++) weights[i] = skin.GetBlendShapeWeight(i);
                    skin.sharedMesh = copy;
                    for (var i = 0; i < weights.Length; i++) skin.SetBlendShapeWeight(i, weights[i]);
                }
            }
        }

        // UniVRM 0.131 ModelExporter skips a renderer without vertices or
        // indices and logs one console warning for each. Disable only those
        // renderers on the export copy, after every authoring pass, so batch
        // logs keep the warnings that matter. Their nodes and bones are still
        // written; a material used only by such a renderer no longer is.
        // When no mesh renderer has geometry, leave the copy as it is.
        internal static void DisableEmptyMeshRenderers(GameObject clone, ICollection<string> warnings)
        {
            if (clone == null || UnityEditor.EditorUtility.IsPersistent(clone))
                throw new ArgumentException("An independent export copy is required.", nameof(clone));
            var renderers = ExportRendererSelection.Enumerate(clone).Where(renderer => renderer is SkinnedMeshRenderer || renderer is MeshRenderer).ToArray();
            var empty = renderers.Where(renderer =>
            {
                var filter = renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>() : null;
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                return !ExportRendererSelection.HasGeometry(mesh);
            }).ToArray();
            if (empty.Length == 0 || empty.Length == renderers.Length) return;
            foreach (var renderer in empty) renderer.enabled = false;
            warnings?.Add(ExporterLocalization.T(EmptyMeshRenderersOmitted) + string.Join(", ", empty.Select(renderer =>
                renderer.transform == clone.transform ? clone.name : UnityEditor.AnimationUtility.CalculateTransformPath(renderer.transform, clone.transform))));
        }

        private static void EnsureUniVrmVersion()
        {
            var package = PackageManagerPackageInfo.FindForAssembly(typeof(Vrm10Exporter).Assembly);
            var gltf = PackageManagerPackageInfo.FindForAssembly(typeof(ExportingGltfData).Assembly);
            Compatibility.DependencyPolicy.RequireUniVrm(package?.version, gltf?.version);
        }

        internal static VRM10ObjectMeta CreateMeta(string avatarName, string author, AvatarLicenseOptions licenseOptions = null)
        {
            // Build disclosure-sensitive metadata from the fields shown in this
            // export window. Never copy contact information, references,
            // thumbnails, or license settings from an imported VRM implicitly.
            var meta = new VRM10ObjectMeta
            {
                Name = avatarName,
                Version = "1.0",
                Authors = new List<string> { author },
                Redistribution = false,
            };
            licenseOptions?.ApplyTo(meta);
            return meta;
        }

        internal static Dictionary<Material, Material> ReplaceLilToonMaterials(GameObject clone, List<Material> created, List<Texture2D> textures, ICollection<string> warnings, bool suppressSharedTextureEmission, bool convertOthers = false, ExportFallbackReport fallbacks = null,
            bool lenient = false)
        {
            var converted = new Dictionary<Material, Material>();
            // Shared masks are converted once per export and owned until all
            // fallback materials have been serialized. Never cache across exports.
            var outlineMasks = new Dictionary<Texture, Texture2D>();
            var others = new Dictionary<Material, Material>();
            var unassigned = new List<string>();
            var assigned = false;
            foreach (var renderer in ExportRendererSelection.Enumerate(clone))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null)
                    {
                        var path = renderer.transform == clone.transform ? clone.name : UnityEditor.AnimationUtility.CalculateTransformPath(renderer.transform, clone.transform);
                        if (!unassigned.Contains(path)) unassigned.Add(path);
                        continue;
                    }
                    assigned = true;
                    if (!LilToonMaterialReader.IsLilToon(source))
                    {
                        // Other shaders left after the substitution (expression targets,
                        // failed approximations, the legacy route) keep UniVRM's own export.
                        // Only an avatar without any lilToon material gets color-and-image MToon.
                        if (!convertOthers) continue;
                        if (source.shader != null && source.shader.name == MToon10Meta.UnityShaderName) continue;
                        if (!others.TryGetValue(source, out var plain)) others.Add(source, plain = CreateMToonDefault(source, created));
                        materials[i] = plain;
                        changed = true;
                        fallbacks?.Add(ExportFallbackKind.NonLilToonMaterials, null, clone.transform, renderer.transform);
                        continue;
                    }
                    if (!converted.TryGetValue(source, out var fallback))
                    {
                        try { fallback = CreateMToonFallback(source, created, warnings, suppressSharedTextureEmission, textures, outlineMasks); }
                        catch (Exception error) when (lenient && !(error is OperationCanceledException))
                        {
                            // One unreadable material must not cost the whole file.
                            fallback = CreateMToonDefault(source, created);
                            warnings?.Add(ExporterLocalization.T(MaterialApproximatedWarning) + source.name + " — " + error.Message);
                            fallbacks?.Add(ExportFallbackKind.MaterialApproximated, source.name, reason: error.Message);
                        }
                        converted.Add(source, fallback);
                    }
                    materials[i] = fallback;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
            if (others.Count > 0)
                warnings?.Add(ExporterLocalization.T(NonLilToonWarning) + string.Join(", ", others.Keys.Select(material => material.name).Distinct()));
            // A product whose material references are all broken has no shader
            // to report. Name the empty renderers instead of blaming lilToon.
            if (!lenient && !assigned && unassigned.Count > 0)
                throw new InvalidOperationException(MissingMaterialsError + string.Join(", ", unassigned.Take(5)) +
                    (unassigned.Count > 5 ? ", …" : ""));
            if (converted.Count == 0 && others.Count == 0 && !lenient) throw new InvalidOperationException("選択したアバターに対応するlilToonマテリアルがありません。");
            return converted;
        }

        internal const string MissingMaterialsError = "表示中のメッシュに材質が1つも割り当てられていないため、書き出せません。材質の参照が切れている可能性があります。元のアバターで材質を割り当ててください: ";
        internal const string MaterialApproximatedWarning = "変換できなかった材質をMToonの既定値で書き出しました（色とメイン画像のみ）: ";
        internal const string NonLilToonWarning = "lilToon以外の材質をMToonの既定値で書き出しました（色とメイン画像のみ）: ";

        internal static Material CreateMToonDefault(Material source, List<Material> created)
        {
            var shader = Shader.Find(MToon10Meta.UnityShaderName);
            if (shader == null) throw new InvalidOperationException("UniVRMのMToon10シェーダーを利用できません。");
            var material = new Material(shader) { name = source.name };
            created.Add(material);
            var mainName = source.HasProperty("_MainTex") ? "_MainTex" : source.HasProperty("_BaseMap") ? "_BaseMap" : null;
            var main = mainName != null ? source.GetTexture(mainName) as Texture2D : null;
            var color = source.HasProperty("_Color") ? source.GetColor("_Color") : source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : UnityEngine.Color.white;
            // Flat toon: the shade color equals the base color, so no shading band appears.
            var shaderName = source.shader != null ? source.shader.name : "";
            var cutout = shaderName.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("Clipping", StringComparison.OrdinalIgnoreCase) >= 0;
            var context = new MToon10Context(material)
            {
                AlphaMode = cutout ? MToon10AlphaMode.Cutout : MToon10AlphaMode.Opaque,
                AlphaCutoff = 0.5f,
                DoubleSidedMode = MToon10DoubleSidedMode.Off,
                BaseColorFactorSrgb = new UnityEngine.Color(color.r, color.g, color.b, 1f),
                BaseColorTexture = main,
                ShadeColorFactorSrgb = new UnityEngine.Color(color.r, color.g, color.b, 1f),
                ShadeColorTexture = main,
                OutlineWidthMode = MToon10OutlineMode.None,
            };
            if (main != null)
            {
                context.TextureScale = source.GetTextureScale(mainName);
                context.TextureOffset = source.GetTextureOffset(mainName);
            }
            context.Validate();
            return material;
        }

        internal static Material CreateMToonFallback(Material source, List<Material> created, ICollection<string> warnings, bool suppressSharedTextureEmission = false, List<Texture2D> textures = null, IDictionary<Texture, Texture2D> outlineMasks = null)
        {
            // Validate the full mobile subset before producing any fallback output.
            LilToonMaterialReader.Read(source, 0, (_, __) => 0, warnings, suppressSharedTextureEmission);
            var shader = Shader.Find(MToon10Meta.UnityShaderName);
            if (shader == null) throw new InvalidOperationException("UniVRMのMToon10シェーダーを利用できません。");
            var material = new Material(shader) { name = source.name };
            created.Add(material);
            var shadowEnabled = source.HasProperty("_UseShadow") && source.GetFloat("_UseShadow") > 0.5f;
            var backlightEnabled = source.HasProperty("_UseBacklight") && source.GetFloat("_UseBacklight") > 0.5f;
            var normalEnabled = EnabledOrTexture(source, "_UseBumpMap", "_BumpMap");
            var emissionEnabled = EnabledOrTexture(source, "_UseEmission", "_EmissionMap") &&
                !LilToonEmissionPolicy.IsSuppressed(source, suppressSharedTextureEmission);
            var matcapAdditive = MatcapAdditive(source);
            var rimEnabled = source.HasProperty("_UseRim") && source.GetFloat("_UseRim") > 0.5f;
            var fakeShadow = LilToonMaterialReader.IsFakeShadow(source);
            var outlineEnabled = !fakeShadow && LilToonMaterialReader.HasPortableOutline(source);
            var shadeTexture = Texture(source, "_ShadowColorTex");
            if (shadeTexture == Texture2D.whiteTexture) shadeTexture = null;
            var context = new MToon10Context(material)
            {
                AlphaMode = AlphaMode(source),
                AlphaCutoff = Float(source, "_Cutoff", 0.5f),
                // glTF/MToon cannot express front-face culling. Double-sided is
                // the safe portable approximation for lilToon's front/off modes.
                DoubleSidedMode = Float(source, "_Cull", 2f) == 2f ? MToon10DoubleSidedMode.Off : MToon10DoubleSidedMode.On,
                BaseColorFactorSrgb = FallbackColor(source, MaterialColorType.color, suppressSharedTextureEmission),
                BaseColorTexture = Texture(source, "_MainTex"),
                ShadeColorFactorSrgb = FallbackColor(source, MaterialColorType.shadeColor, suppressSharedTextureEmission),
                ShadingShiftFactor = shadowEnabled ? MobileMaterialMath.ShadowShift(Float(source, "_ShadowBorder", 0.5f), Float(source, "_ShadowBlur", 0.1f)) : 0f,
                ShadingToonyFactor = shadowEnabled ? MobileMaterialMath.ShadowToony(Float(source, "_ShadowBorder", 0.5f), Float(source, "_ShadowBlur", 0.1f)) : 0f,
                // An unset MToon shade texture is white, not the base image.
                ShadeColorTexture = shadowEnabled
                    ? shadeTexture ?? Texture(source, "_MainTex")
                    : Texture(source, "_MainTex"),
                NormalTexture = normalEnabled ? Texture(source, "_BumpMap") : null,
                NormalTextureScale = normalEnabled ? Float(source, "_BumpScale", 1f) : 0f,
                EmissiveFactorLinear = FallbackColor(source, MaterialColorType.emissionColor, suppressSharedTextureEmission),
                // MToon has no emission mask or main-color tint; bake them so a
                // small mask does not light the whole mesh (lilToon 2.3.4).
                EmissiveTexture = emissionEnabled ? EmissionMaskBaker.Create(source, textures) ?? Texture(source, "_EmissionMap") : null,
                MatcapColorFactorSrgb = FallbackColor(source, MaterialColorType.matcapColor, suppressSharedTextureEmission),
                MatcapTexture = matcapAdditive ? Texture(source, "_MatCapTex") : null,
                // MToon has no directional backlight. When lilToon rim light is
                // unused, its parametric rim is the closest portable fallback.
                ParametricRimColorFactorSrgb = FallbackColor(source, MaterialColorType.rimColor, suppressSharedTextureEmission),
                ParametricRimFresnelPowerFactor = Mathf.Max(0f, rimEnabled
                    ? MobileMaterialMath.RimPower(Float(source, "_RimBorder", 0.5f), Float(source, "_RimFresnelPower", 1f))
                    : backlightEnabled ? Float(source, "_BacklightDirectivity", 5f) : 1f),
                ParametricRimLiftFactor = 0f,
                RimLightingMixFactor = Mathf.Clamp01(rimEnabled ? Float(source, "_RimEnableLighting", 1f) :
                    matcapAdditive ? Float(source, "_MatCapEnableLighting", 1f) : 1f),
                RimMultiplyTexture = rimEnabled ? Texture(source, "_RimColorTex") : null,
                OutlineWidthMode = outlineEnabled ? MToon10OutlineMode.World : MToon10OutlineMode.None,
                // lilToon stores this UI value in centimetre-like units and
                // multiplies it by 0.01 in the outline vertex shader. MToon10
                // world-coordinate width is already expressed in metres.
                OutlineWidthFactor = Mathf.Max(0f, Float(source, "_OutlineWidth", 0f)) * 0.01f,
                // lilToon's _OutlineTex colors the outline; MToon's texture is
                // a green-channel width mask, so they are not interchangeable.
                OutlineWidthMultiplyTexture = outlineEnabled ? OutlineMaskTexture.Create(Texture(source, "_OutlineWidthMask"), textures, outlineMasks) : null,
                OutlineColorFactorSrgb = FallbackColor(source, MaterialColorType.outlineColor, suppressSharedTextureEmission),
                OutlineLightingMixFactor = Mathf.Clamp01(Float(source, "_OutlineEnableLighting", 0f)),
            };
            if (source.HasProperty("_MainTex"))
            {
                // lilToon 2.3.4 also samples its NoScaleOffset width mask with
                // uvMain (_MainTex_ST), not a separate _OutlineWidthMask_ST.
                context.TextureScale = source.GetTextureScale("_MainTex");
                context.TextureOffset = source.GetTextureOffset("_MainTex");
            }
            if (fakeShadow)
            {
                // MToon has no stencil, so FakeShadow would cover the whole mesh
                // it shares geometry with. Keep its slot (the lilToon extension
                // still references it) but make the portable fallback invisible:
                // FallbackColor gives alpha 0, which MToon blending clips.
                context.AlphaMode = MToon10AlphaMode.Transparent;
                context.TransparentWithZWriteMode = MToon10TransparentWithZWriteMode.Off;
            }
            context.Validate();
            return material;
        }

        // This is shared by the actual fallback and UE endpoint validation;
        // the latter must compare against this proxy rather than guessed lilToon
        // property names or the serialized linear glTF color values.
        internal static Color FallbackColor(Material source, MaterialColorType type, bool suppressSharedTextureEmission = false)
        {
            switch (type)
            {
                case MaterialColorType.color:
                    var color = Color(source, "_Color", UnityEngine.Color.white);
                    if (LilToonMaterialReader.IsFakeShadow(source)) color.a = 0f;
                    return color;
                case MaterialColorType.shadeColor: return source.HasProperty("_UseShadow") && source.GetFloat("_UseShadow") > .5f
                    ? MobileMaterialMath.ShadeColor(Color(source, "_Color", UnityEngine.Color.white),
                        MobileMaterialMath.Shadow2ndShade(Color(source, "_ShadowColor", UnityEngine.Color.gray), Color(source, "_Shadow2ndColor", UnityEngine.Color.clear),
                            Float(source, "_ShadowBorder", 0.5f), Float(source, "_Shadow2ndBorder", 0.15f)),
                        Float(source, "_ShadowStrength", 1f))
                    : Color(source, "_Color", UnityEngine.Color.white);
                // lilToon 2.3.4 scales emission by lerp(1, invLighting, _EmissionFluorescence),
                // and invLighting is 0 under normal light. MToon emission is not
                // light-dependent, so use that lit-scene value: 1 - fluorescence.
                case MaterialColorType.emissionColor: return EnabledOrTexture(source, "_UseEmission", "_EmissionMap") && !LilToonEmissionPolicy.IsSuppressed(source, suppressSharedTextureEmission)
                    ? Color(source, "_EmissionColor", UnityEngine.Color.black).linear * Mathf.Clamp01(Float(source, "_EmissionBlend", 1f))
                        * (1f - Mathf.Clamp01(Float(source, "_EmissionFluorescence", 0f))) : UnityEngine.Color.black;
                case MaterialColorType.matcapColor: return MatcapAdditive(source)
                    ? MobileMaterialMath.MatcapColor(Color(source, "_MatCapColor", UnityEngine.Color.white), Float(source, "_MatCapBlend", 1f)) : UnityEngine.Color.black;
                case MaterialColorType.rimColor: return source.HasProperty("_UseRim") && source.GetFloat("_UseRim") > .5f
                    ? MobileMaterialMath.MatcapColor(Color(source, "_RimColor", UnityEngine.Color.black), 1f)
                    : source.HasProperty("_UseBacklight") && source.GetFloat("_UseBacklight") > .5f ? Color(source, "_BacklightColor", UnityEngine.Color.black) : UnityEngine.Color.black;
                case MaterialColorType.outlineColor: return Color(source, "_OutlineColor", UnityEngine.Color.black);
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static MToon10AlphaMode AlphaMode(Material material)
        {
            var shaderName = material.shader != null ? material.shader.name : "";
            if (shaderName.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0) return MToon10AlphaMode.Cutout;
            if (shaderName.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("Refraction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("Gem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("Fur", StringComparison.OrdinalIgnoreCase) >= 0) return MToon10AlphaMode.Transparent;
            var mode = Mathf.RoundToInt(Float(material, "_TransparentMode", 0f));
            return mode == 1 ? MToon10AlphaMode.Cutout : mode == 2 ? MToon10AlphaMode.Transparent : MToon10AlphaMode.Opaque;
        }

        private static float Float(Material material, string property, float fallback) => material.HasProperty(property) ? material.GetFloat(property) : fallback;
        private static Color Color(Material material, string property, Color fallback) => material.HasProperty(property) ? material.GetColor(property) : fallback;
        private static Texture Texture(Material material, string property) => material.HasProperty(property) ? material.GetTexture(property) : null;
        private static bool EnabledOrTexture(Material material, string enableProperty, string textureProperty) =>
            material.HasProperty(enableProperty)
                ? material.GetFloat(enableProperty) > 0.5f
                : Texture(material, textureProperty) != null;

        // MToon adds its MatCap. lilToon's multiply mode (3) darkens instead,
        // so adding it would brighten the surface; such a MatCap is omitted.
        private static bool MatcapAdditive(Material material) =>
            material.HasProperty("_UseMatCap") && material.GetFloat("_UseMatCap") > 0.5f &&
            Mathf.RoundToInt(Float(material, "_MatCapBlendMode", 1f)) != 3;
    }
}
