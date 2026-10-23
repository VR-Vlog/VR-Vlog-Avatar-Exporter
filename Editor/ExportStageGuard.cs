using System;
using System.Collections.Generic;
using System.Linq;

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
        internal static ExportStageFailure Find(Exception error)
        {
            for (var depth = 0; error != null && depth < 16; depth++)
            {
                if (error is ExportStageFailure failure) return failure;
                if (error is AggregateException aggregate)
                    foreach (var inner in aggregate.InnerExceptions)
                        if (Find(inner) is ExportStageFailure nested) return nested;
                error = error.InnerException;
            }
            return null;
        }
    }

    // One guard per export call. In strict mode every stage behaves as before:
    // the first failure stops the export. Otherwise a failing stage is left out,
    // recorded in the fallback report, and the export continues.
    internal sealed class ExportStageGuard
    {
        // Tests throw here to prove that each stage can fail without stopping.
        internal static Action<ExportStage> FaultInjection;

        // The user-facing exports (window, app transfer) continue past failing
        // stages unless this is turned off. Off restores the old behaviour.
        internal const string ContinuePreference = "VRVlog.LilToonExporter.ContinueOnStageFailure";
        internal static bool ContinueByDefault => UnityEditor.EditorPrefs.GetBool(ContinuePreference, true);

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
                FaultInjection?.Invoke(stage);
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
                FaultInjection?.Invoke(stage);
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
        internal static bool IsCancellation(Exception error)
        {
            for (var depth = 0; error != null && depth < 16; depth++)
            {
                if (error is OperationCanceledException) return true;
                if (error is AggregateException aggregate && aggregate.InnerExceptions.Any(IsCancellation)) return true;
                error = error.InnerException;
            }
            return false;
        }

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

        internal static string Label(ExportStage stage)
        {
            switch (stage)
            {
                case ExportStage.GimmickDetection: return "補助ギミックの自動除外";
                case ExportStage.SourceValidation: return "アバターの参照の検査";
                case ExportStage.ExpressionMenu: return "VRChatメニューの表情・ポーズ";
                case ExportStage.BlinkCapture: return "瞬き";
                case ExportStage.PoseCapture: return "同梱ポーズ";
                case ExportStage.CopyValidation: return "書き出し用コピーの参照の検査";
                case ExportStage.MaAppearance: return "Modular Avatarの表示状態の固定";
                case ExportStage.FaceEmo: return "FaceEmoの表情";
                case ExportStage.BuildProcess: return "Modular Avatar / NDMFの処理";
                case ExportStage.Expressions: return "メニュー由来の表情";
                case ExportStage.OptimizerEvidence: return "最適化ツールの材質の照合";
                case ExportStage.LilToonSnapshot: return "lilToonの材質情報";
                case ExportStage.TextureBake: return "標準VRM表示用の画像の焼き込み";
                case ExportStage.Attachments: return "独立したパーツの接続の確認";
                case ExportStage.PhysBone: return "揺れ物";
                case ExportStage.Constraints: return "コンストレイントの変換";
                case ExportStage.LookAt: return "視線";
                case ExportStage.SkinRepair: return "スキンの修復";
                case ExportStage.ExpressionOutput: return "VRM表情の追加";
                case ExportStage.BlinkOutput: return "瞬きの表情の追加";
                case ExportStage.TrackingOutput: return "フェイストラッキング";
                case ExportStage.LilToonExtension: return "lilToonの表現";
                case ExportStage.PhysBoneVerify: return "揺れ物の出力の照合";
                case ExportStage.PoseOutput: return "同梱ポーズの追加";
                case ExportStage.UnusedMorphs: return "使われないBlendShapeの削除";
                case ExportStage.ObjectToggles: return "メニューの表示切り替え";
                default: return stage.ToString();
            }
        }

        // The phase only groups stages for display; it does not decide when they run.
        internal static ExportPhase Phase(ExportStage stage)
        {
            switch (stage)
            {
                case ExportStage.GimmickDetection:
                case ExportStage.SourceValidation:
                case ExportStage.CopyValidation:
                case ExportStage.MaAppearance:
                    return ExportPhase.Precheck;
                case ExportStage.BuildProcess:
                case ExportStage.Attachments:
                case ExportStage.SkinRepair:
                case ExportStage.Constraints:
                    return ExportPhase.Skeleton;
                case ExportStage.ExpressionMenu:
                case ExportStage.BlinkCapture:
                case ExportStage.PoseCapture:
                case ExportStage.FaceEmo:
                case ExportStage.Expressions:
                case ExportStage.ExpressionOutput:
                case ExportStage.BlinkOutput:
                case ExportStage.TrackingOutput:
                case ExportStage.UnusedMorphs:
                case ExportStage.LookAt:
                case ExportStage.PoseOutput:
                case ExportStage.ObjectToggles:
                    return ExportPhase.Expressions;
                case ExportStage.LilToonSnapshot:
                case ExportStage.TextureBake:
                case ExportStage.OptimizerEvidence:
                case ExportStage.LilToonExtension:
                    return ExportPhase.Materials;
                case ExportStage.PhysBone:
                case ExportStage.PhysBoneVerify:
                    return ExportPhase.Springs;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

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

        // What leaving the stage out means for the file. Stages that only check
        // something are approximations; the rest drop their feature.
        internal static ExportOutcome Outcome(ExportStage stage)
        {
            switch (stage)
            {
                case ExportStage.SourceValidation:
                case ExportStage.CopyValidation:
                case ExportStage.OptimizerEvidence:
                case ExportStage.TextureBake:
                case ExportStage.Attachments:
                case ExportStage.SkinRepair:
                case ExportStage.LilToonSnapshot:
                case ExportStage.LilToonExtension:
                case ExportStage.PhysBoneVerify:
                case ExportStage.MaAppearance:
                case ExportStage.BuildProcess:
                // Left out, the file only keeps every BlendShape.
                case ExportStage.UnusedMorphs:
                    return ExportOutcome.Approximated;
                default:
                    return ExportOutcome.Omitted;
            }
        }
    }
}
