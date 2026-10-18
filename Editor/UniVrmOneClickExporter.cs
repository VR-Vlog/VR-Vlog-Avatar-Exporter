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
        internal const string LostRequiredHumanBones = "NDMF / 最適化ツールの処理でヒューマノイドに必要なボーンが失われたため、書き出しを止めました。元のアバターは変更していません。失われたボーン: ";

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
            bool inferEmotionPresets = true)
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
                        authorCopyExpressions, guard, inferEmotionPresets);
                }, failure => report.Fallbacks.Clear());
                CopyWarnings(attemptWarnings, warnings);
                RecordOmissions(guard, report, warnings, omittedDiagnostics);
                report.Stage = "完了";
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

        // Over 128 bundled poses the first 128 are kept (lenient exports only).
        static void RecordTrimmedPoses(PoseExportSession poses, ICollection<string> warnings, ExportRecoveryReport report)
        {
            var trimmed = poses.TrimmedCount;
            if (trimmed == 0) return;
            var reason = string.Format(ExporterLocalization.T(PoseLimitWarning), VRVlog.Poses.HumanoidPoseData.MaximumPoses, trimmed);
            var name = ExporterLocalization.T(ExportStageGuard.Label(ExportStage.PoseCapture));
            warnings?.Add(reason);
            report.Fallbacks.Add(ExportFallbackKind.LimitExceeded, name, reason: reason);
            var entry = report.Fallbacks.Results.Last(result => result.Stage == null && result.Name == name);
            entry.Count = trimmed;
        }

        internal const string InferredEmotionCategory = "名前から自動で設定";

        // Emotion presets filled from a name are kept output the user should check.
        internal static void RecordInferredEmotions(IEnumerable<(string Preset, string Source)> filled, ExportRecoveryReport report)
        {
            var category = ExporterLocalization.T(InferredEmotionCategory);
            foreach (var (preset, source) in filled)
                report.Fallbacks.Record(ExportOutcome.Kept, category, preset,
                    string.Format(ExporterLocalization.T(VrmExpressionBindings.FilledFormat), preset, source));
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

        internal const string PoseLimitWarning = "同梱ポーズは先頭の{0}件だけを保存し、残りの{1}件を省略しました。「ポーズを確認・調整」で保存する項目を選べます。";

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
                    ExporterLocalization.T(ExportStageGuard.Label(omission.Stage)) + " — " + omission.Error.Message);
            }
        }

        static byte[] ExportCore(GameObject source, string avatarName, string author, ICollection<string> warnings, bool suppressSharedTextureEmission,
            string exporterVersion, string lilToonVersion, bool suppressHdrTextureEmission, IEnumerable<GameObject> excludedObjects,
            MaterialBakeOptions bakeOptions, ExportGimmickOptions gimmickOptions, BlinkExportOptions blinkOptions, PoseExportOptions poseOptions,
            ExportRecoveryOptions recoveryOptions, ExportRecoveryReport recoveryReport, AvatarLicenseOptions licenseOptions, bool disableAudioLink,
            Action<GameObject> authorCopyExpressions, ExportStageGuard guard, bool inferEmotionPresets)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            // Cloning detaches the avatar from its parents. Reject an inactive
            // source before the clone can accidentally become active.
            ExportRendererSelection.RequireActiveRoot(source);
            if (string.IsNullOrWhiteSpace(avatarName)) throw new InvalidOperationException("アバター名を取得できませんでした。");
            if (string.IsNullOrWhiteSpace(author)) throw new InvalidOperationException("作者名を入力してください。");

            EnsureUniVrmVersion();
            recoveryReport.Stage = "原本検査";
            var trackingProfile = source.GetComponentInChildren<VrmTrackingMarker>(true)?.profile;
            var manualObjects = (excludedObjects ?? Array.Empty<GameObject>()).ToArray();
            gimmickOptions = new ExportGimmickOptions
            {
                AutoExclude = gimmickOptions?.AutoExclude != false,
                IncludedObjects = (gimmickOptions?.IncludedObjects ?? Array.Empty<GameObject>()).ToArray()
            };
            using var manualExclusions = new ExportObjectExclusions(source, manualObjects);
            var findings = gimmickOptions.AutoExclude
                ? guard.Run(ExportStage.GimmickDetection, () => ExportGimmickDetection.Analyze(source, manualExclusions.Contains), () => new List<ExportGimmickFinding>())
                : new List<ExportGimmickFinding>();
            // A failed detection keeps every object instead of guessing.
            if (guard.IsOmitted(ExportStage.GimmickDetection)) gimmickOptions.AutoExclude = false;
            var automaticRoots = ExportGimmickDetection.AutomaticRoots(findings, gimmickOptions).ToArray();
            using var exclusions = new ExportObjectExclusions(source, manualObjects.Concat(automaticRoots));
            foreach (var finding in findings.Where(f => f.Renderer == null))
                if (automaticRoots.Contains(finding.Target)) warnings?.Add("補助ギミックを省略: " + finding.Target.name + " — " + finding.Reason);
                else if (finding.Unit == GimmickExclusionUnit.Review) warnings?.Add("自動除外せず保持: " + finding.Target.name + " — " + finding.Reason);
            // Preserve rejection of effective external/excluded source targets.
            // Only unresolved getters wait for the selected root on the copy.
            guard.Run(ExportStage.SourceValidation, () => NdmfExportPreparation.ValidateSource(source, exclusions.Contains, deferUnresolvedTargets: true));
            // Re-read the live assets on every export; a preview is never a stale
            // cached source of expression weights after the user edits a clip.
            var menuPolicy = ExportRecoveryReport.MenuImportPolicy(source, recoveryOptions);
            recoveryReport.Stage = "表情メニュー読込";
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
            poses.TrimToLimit = guard.ContinueOnFailure;
            recoveryReport.Stage = "コピー作成";
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
                using var gimmicks = guard.Watch(ExportStage.GimmickDetection, () => new ExportGimmickSession(source, clone, gimmickOptions));
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
                    expressionBindings.RebindPrepared(transformed.PreparedRendererFor);
                    expressionBindings.RebindAuthoredExpressions(transformed.IsolatedCopyOf);
                    faceEmoBindings?.RebindPrepared(transformed.PreparedRendererFor,
                        transformed.ObjectRegistry, transformed.IsolatedCopyOf);
                    guard.Watch(ExportStage.BlinkCapture, () => blink.RebindPrepared(transformed.PreparedRendererFor));
                    unifiedPreparation?.RebindPrepared(transformed.PreparedRendererFor);
                    guard.Watch(ExportStage.BlinkCapture, () => blink.VerifyPreparedIdentity(clone));
                    unifiedPreparation?.VerifyIdentityAndDeformation();
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
                    RecordTrimmedPoses(poses, warnings, recoveryReport);
                    recoveryReport.Stage = "基準形評価";
                    // This beta retains the prepared copy's rest values. Selected
                    // expressions still use their original FX evaluation above;
                    // all endpoints below share this retained neutral snapshot.
                    warnings?.Add(ExporterLocalization.T("このベータ版ではFXの初期状態の復元を省略し、書き出し用コピーの設定を基本の顔・体形として保存しました。VRChatでの通常時の見た目と異なる場合があります。"));
                    AvatarBaseShape.NormalizeSourceWeights(clone, UnityEditor.PlayerSettings.legacyClampBlendShapeWeights);
                    guard.Watch(ExportStage.BlinkCapture, () => blink.ResolvePreparedNeutral(clone, suppressSharedTextureEmission: suppressSharedTextureEmission,
                        suppressHdrTextureEmission: suppressHdrTextureEmission));
                    if (blink.MissingAutomatic)
                    {
                        warnings?.Add(ExporterLocalization.T(BlinkExportSession.MissingAutomaticWarning));
                        recoveryReport.Fallbacks.Add(ExportFallbackKind.Blink);
                    }
                    var neutral = NeutralShapeSnapshot.Capture(clone);
                    authoredEndpoints = AuthoredExpressionEndpoints.Capture(clone, neutral, trackingProfile);
                    guard.Step(ExportStage.Expressions, () => preparedMenuBindings.Capture(menu));
                    guard.Watch(ExportStage.BlinkCapture, () => blink.Bake(clone, temporaryMeshes));
                    AvatarBaseShape.Preserve(clone, clone, temporaryMeshes, warnings);
                    authoredEndpoints.Bake(temporaryMeshes);
                    expressions = guard.Step(ExportStage.Expressions,
                        () => VrChatExpressionBaker.Bake(null, clone, menu, temporaryMeshes, warnings, preparedMenuBindings), () => new List<VrmMenuExpressions.Expression>());
                    // Generated absolute endpoints now define the protected
                    // routes for both automatic and explicitly authored tracking.
                    unifiedPreparation = trackingProfile == null ? new UnifiedExpressionPreparation(clone,
                        suppressSharedTextureEmission: suppressSharedTextureEmission, suppressHdrTextureEmission: suppressHdrTextureEmission) : null;
                    optimizerBindings = ExportOptimizationBindings.Capture(clone,
                        trackingProfile == null ? null : authoredEndpoints.TrackingProfile, unifiedPreparation,
                        objectRegistry: transformed.ObjectRegistry, neutral: neutral, preservePreparedNeutral: true);
                }, skipProcessing: guard.IsOmitted(ExportStage.BuildProcess)));
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
                guard.Step(ExportStage.Expressions, () => RemapMenuExpressions(expressions, optimizerBindings));
                // Authoring passes may restore source material references. Reapply
                // only the selected, source-identity recipe to the owned copy.
                recovery?.Apply();
                // NDMF/authoring passes can introduce materials or restore source
                // references. Disable external audio on the final owned copy too.
                if (disableAudioLink) DisableAudioLinkOnCopy(clone, temporaryMaterials, (copy, original) => recoveryReport.Track(copy, original));
                if(exporterVersion!=null) PreserveExtraMaterialSlots(clone,temporaryMeshes);
                recoveryReport.Stage = "材質保存";
                var fullSnapshot = exporterVersion != null ? guard.Step(ExportStage.LilToonSnapshot,
                    () => LilToonFullSnapshot.Capture(clone,suppressSharedTextureEmission,suppressHdrTextureEmission, warnings, animationSource: source), () => null) : null;
                var fallbackWarnings=fullSnapshot==null?warnings:new List<string>();
                // With no lilToon material at all the lilToon extension has nothing to
                // carry. Save a plain MToon VRM that is marked as intentionally so.
                var withoutLilToon = fullSnapshot != null && fullSnapshot.IsEmpty;
                // A lilToon-named shader outside the official 2.3.4 set is not "no lilToon".
                // Strict exports stop there; otherwise each such material becomes
                // color-and-image MToon and is listed as approximated.
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
                var springs = guard.Step(ExportStage.PhysBone, () => PhysBoneSpringExport.Convert(source, clone, warnings, recoveryReport.Fallbacks), () => null);
                recoveryReport.Stage = "材質保存";
                ReplaceLilToonMaterials(clone, temporaryMaterials, temporaryTextures, fallbackWarnings, suppressSharedTextureEmission, convertOthers: withoutLilToon, fallbacks: recoveryReport.Fallbacks,
                    lenient: guard.ContinueOnFailure);
                guard.Run(ExportStage.OptimizerEvidence, () => optimizerBindings.VerifyMaterialEvidence());
                MakeRendererMeshesUnique(clone, temporaryMeshes);
                recoveryReport.Stage = "VRM変換";
                var lookAt = guard.Run(ExportStage.LookAt, () => VrmLookAtExport.Capture(source, clone, warnings), () => null);
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
                    }, warnings: warnings);
                recoveryReport.Stage = "出力検査";
                // Output stages transform finished bytes; a failing one keeps the
                // bytes it was given.
                exported = guard.Run(ExportStage.SkinRepair, () => ExportSkinRoots.Repair(exported, warnings), () => exported);
                if (lookAt != null) exported = guard.Run(ExportStage.LookAt, () => lookAt.Apply(exported), () => exported);
                // Empty emotion presets are filled from menu faces or whole-face names;
                // each filled preset is listed in the warnings and the result list.
                exported = guard.Run(ExportStage.ExpressionOutput, () =>
                {
                    var filled = new List<(string Preset, string Source)>();
                    var output = VrmExpressionBindings.AddMissing(VrmMenuExpressions.Add(exported, expressions), warnings, inferBlink: false,
                        inferEmotions: inferEmotionPresets, filledEmotions: filled);
                    RecordInferredEmotions(filled, recoveryReport);
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
                return guard.Run(ExportStage.PoseOutput, () => poses.Inject(exported), () => exported);
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

        private static Dictionary<Material, Material> ReplaceLilToonMaterials(GameObject clone, List<Material> created, List<Texture2D> textures, ICollection<string> warnings, bool suppressSharedTextureEmission, bool convertOthers = false, ExportFallbackReport fallbacks = null,
            bool lenient = false)
        {
            var converted = new Dictionary<Material, Material>();
            // Shared masks are converted once per export and owned until all
            // fallback materials have been serialized. Never cache across exports.
            var outlineMasks = new Dictionary<Texture, Texture2D>();
            var others = new Dictionary<Material, Material>();
            foreach (var renderer in ExportRendererSelection.Enumerate(clone))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null) continue;
                    if (!LilToonMaterialReader.IsLilToon(source))
                    {
                        // Mixed avatars keep UniVRM's own export of other shaders. Only an
                        // avatar without any lilToon material gets color-and-image MToon.
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
            if (converted.Count == 0 && others.Count == 0 && !lenient) throw new InvalidOperationException("選択したアバターに対応するlilToonマテリアルがありません。");
            return converted;
        }

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
            var matcapEnabled = source.HasProperty("_UseMatCap") && source.GetFloat("_UseMatCap") > 0.5f;
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
                EmissiveTexture = emissionEnabled ? Texture(source, "_EmissionMap") : null,
                MatcapColorFactorSrgb = FallbackColor(source, MaterialColorType.matcapColor, suppressSharedTextureEmission),
                MatcapTexture = matcapEnabled ? Texture(source, "_MatCapTex") : null,
                // MToon has no directional backlight. When lilToon rim light is
                // unused, its parametric rim is the closest portable fallback.
                ParametricRimColorFactorSrgb = FallbackColor(source, MaterialColorType.rimColor, suppressSharedTextureEmission),
                ParametricRimFresnelPowerFactor = Mathf.Max(0f, rimEnabled
                    ? MobileMaterialMath.RimPower(Float(source, "_RimBorder", 0.5f), Float(source, "_RimFresnelPower", 1f))
                    : backlightEnabled ? Float(source, "_BacklightDirectivity", 5f) : 1f),
                ParametricRimLiftFactor = 0f,
                RimLightingMixFactor = Mathf.Clamp01(rimEnabled ? Float(source, "_RimEnableLighting", 1f) :
                    matcapEnabled ? Float(source, "_MatCapEnableLighting", 1f) : 1f),
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
                    ? MobileMaterialMath.ShadeColor(Color(source, "_Color", UnityEngine.Color.white), Color(source, "_ShadowColor", UnityEngine.Color.gray), Float(source, "_ShadowStrength", 1f))
                    : Color(source, "_Color", UnityEngine.Color.white);
                case MaterialColorType.emissionColor: return EnabledOrTexture(source, "_UseEmission", "_EmissionMap") && !LilToonEmissionPolicy.IsSuppressed(source, suppressSharedTextureEmission)
                    ? Color(source, "_EmissionColor", UnityEngine.Color.black).linear * Mathf.Clamp01(Float(source, "_EmissionBlend", 1f)) : UnityEngine.Color.black;
                case MaterialColorType.matcapColor: return source.HasProperty("_UseMatCap") && source.GetFloat("_UseMatCap") > .5f
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
    }
}
