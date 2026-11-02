using System;
using System.Collections.Generic;

namespace VRVlog.LilToonExporter
{
    // How an item reached the file: kept as authored, baked into generated data,
    // approximated with a visible difference, or omitted from the file.
    internal enum ExportOutcome { Kept, Baked, Approximated, Omitted }

    // Stages that may fail without stopping the export. A stage that cannot be
    // left out (the copy, the VRM conversion, the file itself) is not listed.
    internal enum ExportStage
    {
        GimmickDetection,
        SourceValidation,
        ExpressionMenu,
        BlinkCapture,
        PoseCapture,
        CopyValidation,
        MaAppearance,
        FaceEmo,
        BuildProcess,
        // NDMF's Optimizing phase only: left out first, the outfit and mesh
        // edits of the earlier phases stay in the file.
        Optimizing,
        Expressions,
        OptimizerEvidence,
        LilToonSnapshot,
        TextureBake,
        Attachments,
        PhysBone,
        Constraints,
        LookAt,
        SkinRepair,
        ExpressionOutput,
        BlinkOutput,
        TrackingOutput,
        LilToonExtension,
        PhysBoneVerify,
        PoseOutput,
        UnusedMorphs,
        ObjectToggles,
        ExpressionGestures,
        FxRest,
    }

    // The steps a user sees. Each stage belongs to one phase; the stages stay
    // separate so that leaving one out keeps the rest of its phase.
    internal enum ExportPhase { Precheck, Skeleton, Expressions, Materials, Springs, Export, ResultList }

    // Carries a failure out of a stage that changed the export copy. The export
    // is restarted from a fresh copy with this stage left out.
    internal sealed class ExportStageFailure : Exception
    {
        internal readonly ExportStage Stage;
        internal ExportStageFailure(ExportStage stage, Exception cause) : base(cause.Message, cause) { Stage = stage; }

        // NDMF wraps callback failures in its own summary; look through it.
        internal static ExportStageFailure Find(Exception error) =>
            (ExportStageFailure)ExceptionChain.Find(error, value => value is ExportStageFailure);
    }

    // One guard per export call. In strict mode every stage behaves as before:
    // the first failure stops the export. Otherwise a failing stage is left out,
    // recorded in the fallback report, and the export continues.
    internal sealed class ExportStageGuard
    {
        // The user-facing exports (window, app transfer) continue past failing
        // stages. Tests turn this off to check the old behaviour of stopping.
        // Exports continue past a failed stage unless the editor preference
        // turns that off (see Documentation~/decisions/2026-10-22-quick-equals-default-normal.md).
        internal const string ContinuePreference = "VRVlog.LilToonExporter.ContinueOnStageFailure";
        internal static bool ContinueByDefault => ExportTestHooks.ContinueByDefault ?? UnityEditor.EditorPrefs.GetBool(ContinuePreference, true);

        internal readonly bool ContinueOnFailure;
        // Called once per left-out stage, before the export copy is destroyed.
        internal Action<ExportStage, Exception> OnOmit;
        readonly HashSet<ExportStage> omitted = new HashSet<ExportStage>();
        readonly List<(ExportStage Stage, Exception Error)> omissions = new List<(ExportStage, Exception)>();

        internal ExportStageGuard(bool continueOnFailure) { ContinueOnFailure = continueOnFailure; }

        internal bool IsOmitted(ExportStage stage) => omitted.Contains(stage);
        internal IReadOnlyList<(ExportStage Stage, Exception Error)> Omissions => omissions;

        // Stages that only read the source or transform the finished bytes: on
        // failure the fallback value is used in place and the export goes on.
        internal T Run<T>(ExportStage stage, Func<T> body, Func<T> fallback)
        {
            if (omitted.Contains(stage)) return fallback();
            try
            {
                ExportTestHooks.FaultInjection?.Invoke(stage);
                return body();
            }
            catch (Exception error) when (CanOmit(error) && ExportStageFailure.Find(error) == null)
            {
                Omit(stage, error);
                return fallback();
            }
        }

        internal void Run(ExportStage stage, Action body) => Run(stage, () => { body(); return true; }, () => false);

        // Stages that change the export copy: a failure leaves the copy in an
        // unknown state, so it is reported as ExportStageFailure and the whole
        // export is restarted with the stage left out (see Retry).
        internal T Step<T>(ExportStage stage, Func<T> body, Func<T> fallback)
        {
            if (omitted.Contains(stage)) return fallback();
            try
            {
                ExportTestHooks.FaultInjection?.Invoke(stage);
                return body();
            }
            catch (Exception error) when (CanOmit(error) && ExportStageFailure.Find(error) == null)
            {
                throw new ExportStageFailure(stage, error);
            }
        }

        internal void Step(ExportStage stage, Action body) => Step(stage, () => { body(); return true; }, () => false);

        // Like Step, but the body also runs after the stage was left out. Use it
        // when an earlier fallback (an empty session, a disabled option) already
        // makes the body harmless, so skipping it would only add special cases.
        internal T Watch<T>(ExportStage stage, Func<T> body) =>
            omitted.Contains(stage) ? body() : Step(stage, body, body);

        internal void Watch(ExportStage stage, Action body) => Watch(stage, () => { body(); return true; });

        bool CanOmit(Exception error) => ContinueOnFailure && !IsCancellation(error);

        // A cancellation wrapped by a build tool still cancels the export.
        internal static bool IsCancellation(Exception error) => ExceptionChain.Any(error, value => value is OperationCanceledException);

        void Omit(ExportStage stage, Exception error)
        {
            if (!omitted.Add(stage)) return;
            omissions.Add((stage, error));
            OnOmit?.Invoke(stage, error);
        }

        // Runs attempt until it succeeds. Each failure that names a stage which
        // was not yet left out restarts the export without that stage. Anything
        // else (strict mode, cancellation, a stage that cannot be left out, or a
        // stage whose fallback failed too) is thrown unchanged. Every stage can
        // be left out once, so the number of attempts is bounded.
        internal T Retry<T>(Func<T> attempt, Action<ExportStageFailure> beforeRetry)
        {
            while (true)
            {
                try { return attempt(); }
                catch (Exception error) when (ContinueOnFailure && !IsCancellation(error) && ExportStageFailure.Find(error) is ExportStageFailure failure &&
                                              !omitted.Contains(failure.Stage))
                {
                    Omit(failure.Stage, failure.InnerException ?? failure);
                    beforeRetry?.Invoke(failure);
                }
            }
        }

        // The original cause, without the stage wrapper, for reports and rethrow.
        internal static Exception Unwrap(Exception error)
        {
            var failure = ExportStageFailure.Find(error);
            return failure?.InnerException ?? error;
        }

        // The name, the phase it is shown under and what leaving it out means for
        // the file, per stage. The phase only groups stages for display; it does
        // not decide when they run. Stages that only check something are
        // approximations; the rest drop their feature.
        static readonly Dictionary<ExportStage, (string Label, ExportPhase Phase, ExportOutcome Outcome)> Stages =
            new Dictionary<ExportStage, (string, ExportPhase, ExportOutcome)>
            {
                [ExportStage.GimmickDetection] = ("補助ギミックの自動除外", ExportPhase.Precheck, ExportOutcome.Omitted),
                [ExportStage.SourceValidation] = ("アバターの参照の検査", ExportPhase.Precheck, ExportOutcome.Approximated),
                [ExportStage.ExpressionMenu] = ("VRChatメニューの表情・ポーズ", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.BlinkCapture] = ("瞬き", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.PoseCapture] = ("同梱ポーズ", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.CopyValidation] = ("書き出し用コピーの参照の検査", ExportPhase.Precheck, ExportOutcome.Approximated),
                [ExportStage.MaAppearance] = ("Modular Avatarの表示状態の固定", ExportPhase.Precheck, ExportOutcome.Approximated),
                [ExportStage.FaceEmo] = ("FaceEmoの表情", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.BuildProcess] = ("Modular Avatar / NDMFの処理", ExportPhase.Skeleton, ExportOutcome.Approximated),
                [ExportStage.Optimizing] = ("最適化ツールの処理", ExportPhase.Skeleton, ExportOutcome.Approximated),
                [ExportStage.Expressions] = ("メニュー由来の表情", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.OptimizerEvidence] = ("最適化ツールの材質の照合", ExportPhase.Materials, ExportOutcome.Approximated),
                [ExportStage.LilToonSnapshot] = ("lilToonの材質情報", ExportPhase.Materials, ExportOutcome.Approximated),
                [ExportStage.TextureBake] = ("標準VRM表示用の画像の焼き込み", ExportPhase.Materials, ExportOutcome.Approximated),
                [ExportStage.Attachments] = ("独立したパーツの接続の確認", ExportPhase.Skeleton, ExportOutcome.Approximated),
                [ExportStage.PhysBone] = ("揺れ物", ExportPhase.Springs, ExportOutcome.Omitted),
                [ExportStage.Constraints] = ("コンストレイントの変換", ExportPhase.Skeleton, ExportOutcome.Omitted),
                [ExportStage.LookAt] = ("視線", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.SkinRepair] = ("スキンの修復", ExportPhase.Skeleton, ExportOutcome.Approximated),
                [ExportStage.ExpressionOutput] = ("VRM表情の追加", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.BlinkOutput] = ("瞬きの表情の追加", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.TrackingOutput] = ("フェイストラッキング", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.LilToonExtension] = ("lilToonの表現", ExportPhase.Materials, ExportOutcome.Approximated),
                [ExportStage.PhysBoneVerify] = ("揺れ物の出力の照合", ExportPhase.Springs, ExportOutcome.Approximated),
                [ExportStage.PoseOutput] = ("同梱ポーズの追加", ExportPhase.Expressions, ExportOutcome.Omitted),
                // Left out, the file only keeps every BlendShape.
                [ExportStage.UnusedMorphs] = ("使われないBlendShapeの削除", ExportPhase.Expressions, ExportOutcome.Approximated),
                [ExportStage.ObjectToggles] = ("メニューの表示切り替え", ExportPhase.Expressions, ExportOutcome.Omitted),
                [ExportStage.ExpressionGestures] = ("ハンドジェスチャーの表情", ExportPhase.Expressions, ExportOutcome.Omitted),
                // Left out, the face and body keep the export copy's values.
                [ExportStage.FxRest] = ("FXの通常時の顔・体形", ExportPhase.Expressions, ExportOutcome.Approximated),
            };

        internal static string Label(ExportStage stage) => Stages.TryGetValue(stage, out var row) ? row.Label : stage.ToString();

        internal static ExportPhase Phase(ExportStage stage) =>
            Stages.TryGetValue(stage, out var row) ? row.Phase : throw new ArgumentOutOfRangeException(nameof(stage), stage, null);

        internal static string PhaseLabel(ExportPhase phase)
        {
            switch (phase)
            {
                case ExportPhase.Precheck: return "事前確認";
                case ExportPhase.Skeleton: return "骨格";
                case ExportPhase.Expressions: return "表情";
                case ExportPhase.Materials: return "マテリアル";
                case ExportPhase.Springs: return "揺れ物";
                case ExportPhase.Export: return "書き出し";
                case ExportPhase.ResultList: return "結果一覧";
                default: return phase.ToString();
            }
        }

        // "表情: 瞬き". Each part is translated on its own; a stage named like its
        // phase ("揺れ物") is shown once.
        internal static string DisplayName(ExportStage stage)
        {
            var phase = PhaseLabel(Phase(stage));
            var label = Label(stage);
            return label == phase ? ExporterLocalization.T(label) : ExporterLocalization.T(phase) + ": " + ExporterLocalization.T(label);
        }

        internal static ExportOutcome Outcome(ExportStage stage) => Stages.TryGetValue(stage, out var row) ? row.Outcome : ExportOutcome.Omitted;
    }
}
