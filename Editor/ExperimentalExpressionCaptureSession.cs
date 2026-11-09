using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using static VRVlog.LilToonExporter.NumericChecks;

namespace VRVlog.LilToonExporter
{
    // The expression board's preview copy of an avatar and its VRM export.
    internal sealed partial class ExperimentalExpressionCaptureSession : IDisposable
    {
        [Serializable]
        internal sealed class Row
        {
            public string Path;
            public string[] Shapes;
            public float[] Weights;
        }

        [Serializable]
        internal sealed class Pose
        {
            public string Name;
            public string InstalledSourceId;
            public List<Row> Rows = new List<Row>();
        }

        // The source or a recorded mesh no longer matches the copy. The board
        // keeps every choice apart from the copy, so it builds the copy again
        // from the avatar as it is and carries on; Detail says what differed.
        internal sealed class SourceChangedException : InvalidOperationException
        {
            internal string Detail { get; }
            internal SourceChangedException(string message, string detail) : base(message) { Detail = detail; }
        }

        internal sealed class Channel
        {
            internal SkinnedMeshRenderer Renderer;
            internal Mesh Mesh;
            internal string Path;
            internal string[] Shapes;
            internal string MeshHash;
        }

        internal readonly GameObject Source;
        internal GameObject Copy { get; private set; }
        internal readonly List<string> Warnings = new List<string>();
        internal readonly List<Channel> Channels = new List<Channel>();
        internal readonly List<Pose> Expressions = new List<Pose>();
        // The board keeps one list across its copies, so a pose window open on it stays current.
        internal readonly PoseExportOptions PoseOptions;
        readonly Dictionary<string, SkinnedMeshRenderer> authoringRenderers = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
        // Meshes left out of recording because another mesh shares their path.
        // A face's curves on them are dropped; the rest of the face is kept.
        readonly HashSet<string> skippedPaths = new HashSet<string>(StringComparer.Ordinal);
        // Faces shown on the source when the board opened, by path.
        readonly List<string> shownFaces = new List<string>();
        internal Pose Baseline { get; private set; }
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        NdmfExportPreparation preparation;
        PreparedExpressionBindings authoredBindings;
        ExportObjectExclusions exclusions;
        ExportGimmickSession gimmicks;
        // The ordinary routes pass these on, so they leave out what Copy leaves out.
        GameObject[] manualExcluded = Array.Empty<GameObject>();
        ExportGimmickOptions gimmickOptions = new ExportGimmickOptions();
        internal IReadOnlyList<GameObject> ExcludedObjects => manualExcluded;
        internal ExportGimmickOptions GimmickOptions => new ExportGimmickOptions { AutoExclude = gimmickOptions.AutoExclude,
            IncludedObjects = gimmickOptions.IncludedObjects.ToArray(), IncludedComponents = gimmickOptions.IncludedComponents.ToArray() };
        FaceEmoExpressions.BindingSnapshot faceEmoBindings;
        ExportRecoverySourceStamp sourceStamp;
        bool disposed, ownerReleased;
        int recoveryUsers;
        PreviewRenderUtility retainedPreview;
        sealed class RecoveryLease : IDisposable
        {
            ExperimentalExpressionCaptureSession owner;
            internal RecoveryLease(ExperimentalExpressionCaptureSession owner) { this.owner = owner; }
            public void Dispose()
            {
                if (owner == null) return;
                var current = owner; owner = null; current.recoveryUsers--;
                if (current.ownerReleased && current.recoveryUsers == 0) current.DisposeCore();
            }
        }
        internal IDisposable RetainForRecovery()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ExperimentalExpressionCaptureSession));
            recoveryUsers++; return new RecoveryLease(this);
        }
        internal bool RetainPreviewForRecovery(PreviewRenderUtility preview)
        {
            if (recoveryUsers == 0) return false;
            // Keep the preview camera available until recovery users release
            // the session; the avatar remains in its own diagnostic scene.
            retainedPreview = preview; return true;
        }

        internal const string WithoutBuildPassesWarning =
            "Modular Avatar / NDMFの処理に失敗したため、表情ボードは処理前のアバターで表情を読み取りました";
        const string BuildPassFailureKey = "VRVlog.LilToonExporter.BoardBuildPassFailure";

        // The board stops before Optimizing, but Generating and Transforming can
        // still fail. As a lenient export does, the board leaves out a pass
        // NDMF reports as failed and keeps the others; when the build itself
        // stops, it leaves out the build passes instead of stopping the transfer
        // before any file is written. The export runs them on its own copy.
        internal static ExperimentalExpressionCaptureSession Create(GameObject source,
            IEnumerable<GameObject> excludedObjects = null, ExportGimmickOptions gimmickOptions = null, PoseExportOptions poseOptions = null)
        {
            try { return new ExperimentalExpressionCaptureSession(source, excludedObjects, gimmickOptions, poseOptions: poseOptions); }
            catch (Exception error) when (error.Data.Contains(BuildPassFailureKey) && ExportStageGuard.ContinueByDefault)
            {
                Debug.LogWarning("VR Vlog: " + error);
                var session = new ExperimentalExpressionCaptureSession(source, excludedObjects, gimmickOptions, skipBuildPasses: true, poseOptions: poseOptions);
                session.Warnings.Add(ExporterLocalization.T(WithoutBuildPassesWarning));
                return session;
            }
        }

        // Manual exclusions and gimmick options follow the ordinary export:
        // detection skips what is excluded by hand, and both leave Copy.
        internal ExperimentalExpressionCaptureSession(GameObject source,
            IEnumerable<GameObject> excludedObjects = null, ExportGimmickOptions gimmickOptions = null, bool skipBuildPasses = false,
            PoseExportOptions poseOptions = null)
        {
            Source = source;
            PoseOptions = poseOptions ?? new PoseExportOptions();
            try
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                using var steps = ExpressionBoardTimings.Sequence();
                steps.Next("clipRoots");
                CaptureClipRoots();
                steps.Next("copy");
                Copy = NdmfExportPreparation.InstantiateOwnedCopy(source);
                Copy.name = source.name;
                manualExcluded = (excludedObjects ?? Array.Empty<GameObject>()).ToArray();
                this.gimmickOptions = new ExportGimmickOptions
                {
                    AutoExclude = gimmickOptions?.AutoExclude != false,
                    IncludedObjects = (gimmickOptions?.IncludedObjects ?? Array.Empty<GameObject>()).ToArray(),
                    IncludedComponents = (gimmickOptions?.IncludedComponents ?? Array.Empty<Component>()).ToArray()
                };
                steps.Next("gimmicks");
                List<ExportGimmickFinding> findings;
                using (var manual = new ExportObjectExclusions(source, manualExcluded))
                    findings = this.gimmickOptions.AutoExclude ? ExportGimmickDetection.Analyze(source, manual.Contains) : new List<ExportGimmickFinding>();
                var automaticRoots = ExportGimmickDetection.AutomaticRoots(findings, this.gimmickOptions).ToArray();
                exclusions = new ExportObjectExclusions(source, manualExcluded.Concat(automaticRoots));
                gimmicks = new ExportGimmickSession(source, Copy, this.gimmickOptions, findings);
                bool ExcludedBinding(string path) => exclusions.ContainsPath(path) || gimmicks.ContainsPath(path);
                authoredBindings = new PreparedExpressionBindings(Copy, null, source, ExcludedBinding);
                steps.Next("maAppearance");
                MaAppearanceSnapshot.Apply(source, Copy, ownedMeshes, exclusions, Warnings);
                PoseExportSession.RemoveAplFromCopy(source, Copy);
                gimmicks.Apply(null, null, Warnings);
                steps.Next("faceEmo");
                // As with the blink preview, keep Transforming's committed meshes and FX.
                // Optimizing is deliberately deferred until generated expression targets exist.
                faceEmoBindings = FaceEmoExpressions.Capture(source, Copy, ExcludedBinding,
                    authoredBindings.ExcludesPreparedPath, deferPermanentOverrides: true);
                faceEmoBindings.CompletedFacePlayer = (avatar, metadata, state, layer) =>
                    ExperimentalInstalledExpressionPlayer.Sample(avatar, metadata, new Dictionary<string, float>(), state, layer);
                foreach (var group in Copy.GetComponentsInChildren<SkinnedMeshRenderer>(true).GroupBy(renderer =>
                    AnimationUtility.CalculateTransformPath(renderer.transform, Copy.transform), StringComparer.Ordinal))
                    if (group.Count() == 1) authoringRenderers.Add(group.Key, group.Single());
                steps.Next("authored");
                authoredBindings.CaptureAuthoredExpressions();
                steps.Next("ndmf");
                // The board reads the avatar's clips on the copy as the export
                // reads them, after Transforming. Optimizing passes rename, merge
                // and remove the shapes those clips name; the export follows them
                // through Optimizing itself, so the board must not see them.
                try
                {
                    preparation = NdmfExportPreparation.Prepare(source, Copy, Warnings, afterTransforming: transformed => {
                        authoredBindings.RebindPrepared(transformed.PreparedRendererFor);
                        authoredBindings.RebindAuthoredExpressions(transformed.IsolatedCopyOf);
                        faceEmoBindings.RebindPrepared(transformed.PreparedRendererFor, transformed.ObjectRegistry, transformed.IsolatedCopyOf);
                        gimmicks.Apply(null, null, Warnings);
                    }, skipProcessing: skipBuildPasses, stopBeforeOptimizing: true, tolerateFailedPasses: ExportStageGuard.ContinueByDefault);
                }
                catch (Exception error) when (!skipBuildPasses && !ExportStageGuard.IsCancellation(error))
                {
                    error.Data[BuildPassFailureKey] = true;
                    throw;
                }
                steps.Next("channels");
                foreach (var behaviour in Copy.GetComponentsInChildren<Behaviour>(true))
                    if (behaviour != null && !NdmfExportPreparation.PreservesExportBehaviour(behaviour)) behaviour.enabled = false;
                foreach (var skin in Copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                { skin.updateWhenOffscreen = true; skin.forceMatrixRecalculationPerRender = true; }
                var repeatedPaths = new HashSet<string>(Copy.GetComponentsInChildren<SkinnedMeshRenderer>(true).GroupBy(renderer =>
                    AnimationUtility.CalculateTransformPath(renderer.transform, Copy.transform), StringComparer.Ordinal)
                    .Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.Ordinal);
                foreach (var renderer in ExportRendererSelection.Enumerate(Copy).OfType<SkinnedMeshRenderer>())
                {
                    var mesh = renderer.sharedMesh;
                    if (mesh == null || mesh.blendShapeCount == 0) continue;
                    var path = AnimationUtility.CalculateTransformPath(renderer.transform, Copy.transform);
                    // A mesh sharing its path with another cannot be bound by path:
                    // leave it out of recording and say so, as the ordinary export still writes it.
                    if (repeatedPaths.Contains(path))
                    {
                        if (skippedPaths.Add(path))
                            Warnings.Add(string.Format(ExporterLocalization.T("同じ階層に同じ名前のメッシュがあるため表情の記録対象から外しました: {0}"), path));
                        continue;
                    }
                    // The same for a mesh its path cannot name (a "/" in an object name
                    // above it, or another object at that path) or with a broken weight.
                    SkinnedMeshRenderer resolved;
                    try { resolved = VrChatExpressionSampler.FindRenderer(Copy, path); }
                    catch (InvalidOperationException) { resolved = null; }
                    if (resolved != renderer)
                    {
                        if (skippedPaths.Add(path))
                            Warnings.Add(string.Format(ExporterLocalization.T("階層の名前からメッシュを特定できないため表情の記録対象から外しました: {0}"), path));
                        continue;
                    }
                    if (Enumerable.Range(0, mesh.blendShapeCount).Any(shape => !Finite(renderer.GetBlendShapeWeight(shape))))
                    {
                        if (skippedPaths.Add(path))
                            Warnings.Add(string.Format(ExporterLocalization.T("BlendShapeに不正な変形量があるため表情の記録対象から外しました: {0}"), path));
                        continue;
                    }
                    renderer.forceMatrixRecalculationPerRender = true;
                    Channels.Add(new Channel { Renderer = renderer, Mesh = mesh, Path = path,
                        Shapes = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray(),
                        MeshHash = MeshStamp(mesh) });
                }
                steps.Next("snapshot");
                Baseline = Snapshot("基準の顔");
                steps.Next("fingerprint");
                // Settle source read caches before guarding subsequent edits.
                // Like the recovery window, a partially driven VRChat rotation
                // may differ by the solver's re-encoding noise only.
                sourceStamp = ExportRecoverySourceStamp.Capture(source, detailed: true);
                // The stamps were just taken from the source as it is: the rest
                // of this batch need not hash the whole avatar again.
                if (batchDepth > 0) validatedBatch = batchId;
                hashedEdits = EditorEdits.Count;
                foreach (var channel in Channels)
                    if (SourceFace(channel.Path) is SkinnedMeshRenderer face && Shown(face)) shownFaces.Add(channel.Path);
            }
            catch { Dispose(); throw; }
        }

        SkinnedMeshRenderer SourceFace(string path)
        {
            var target = Source == null ? null : Source.transform.Find(path);
            return target == null ? null : target.GetComponent<SkinnedMeshRenderer>();
        }

        bool Shown(SkinnedMeshRenderer face) => face.enabled && ExportRendererSelection.ActiveUnder(face.gameObject, Source.transform);

        // A face shown when the board opened and hidden on the source now,
        // null when there is none. A copy made from the avatar as it is would
        // leave that face out and drop the files added for it, though the
        // user may only be hiding it for a moment. A face no longer at its
        // path does not count: the build may name the meshes differently.
        internal string HiddenFace()
        {
            if (disposed || Source == null) return null;
            foreach (var path in shownFaces)
                if (SourceFace(path) is SkinnedMeshRenderer face && !Shown(face)) return path;
            return null;
        }

        // What differs between the source and the copy, null when they match.
        // A match also counts as the check of this batch.
        internal string SourceDifference()
        {
            if (disposed || Copy == null) return null;
            var difference = Source == null ? "source (deleted)" : sourceStamp.Difference(Source) ?? ChannelDifference(hashed: false);
            if (difference == null) { if (batchDepth > 0) validatedBatch = batchId; hashedEdits = EditorEdits.Count; }
            return difference;
        }

        string ChannelDifference(bool hashed)
        {
            foreach (var channel in Channels)
                if (channel.Renderer == null || !channel.Renderer.enabled || !channel.Renderer.gameObject.activeInHierarchy ||
                    channel.Renderer.sharedMesh != channel.Mesh || !hashed && MeshStamp(channel.Mesh) != channel.MeshHash)
                    return "mesh " + channel.Path;
            return null;
        }

        // Hashing the whole source avatar and its meshes takes a long time on a
        // real avatar, and preparing the board checks the session once per
        // file and per slot. Inside one Batch nothing else in the editor runs,
        // so the source cannot change: the hashes are checked once per batch.
        // Export checks them again as it writes the file, unless its caller
        // has just checked them (sourceChecked).
        static int batchDepth, batchId;
        int validatedBatch;
        sealed class BatchScope : IDisposable
        {
            bool done;
            public void Dispose() { if (!done) { done = true; batchDepth--; } }
        }
        internal static IDisposable Batch()
        {
            if (batchDepth++ == 0) batchId++;
            return new BatchScope();
        }

        void RequireValid(bool always = false, bool sourceChecked = false)
        {
            if (disposed || Copy == null) throw new InvalidOperationException(ExporterLocalization.T("表情記録のコピーがありません。"));
            var hashed = sourceChecked || !always && batchDepth > 0 && validatedBatch == batchId;
            using var timing = hashed ? null : ExpressionBoardTimings.Step("validate");
            if (Source == null) throw new InvalidOperationException(ExporterLocalization.T("元のアバターが変更されました。「変更」からアバターを指定し直してください。"));
            if (!hashed && sourceStamp.Difference(Source) is string difference)
                throw new SourceChangedException(ExporterLocalization.T("元のアバターが変更されました。「変更」からアバターを指定し直してください。"), difference);
            if (!hashed) hashedEdits = EditorEdits.Count;
            if (ChannelDifference(hashed) is string mesh)
                throw new SourceChangedException(ExporterLocalization.T("書き出し用メッシュが変更されました。「変更」からアバターを指定し直してください。"), mesh);
            if (batchDepth > 0) validatedBatch = batchId;
        }

        static bool HasShape(Pose pose, string path, string shape) =>
            pose.Rows.SingleOrDefault(value => value.Path == path) is var row && row != null && Array.IndexOf(row.Shapes, shape) >= 0;

        static void Set(Pose pose, string path, string shape, float weight)
        {
            var row = pose.Rows.SingleOrDefault(value => value.Path == path);
            var index = row == null ? -1 : Array.IndexOf(row.Shapes, shape);
            if (index < 0) throw new InvalidOperationException(ExporterLocalization.T("候補のBlendShapeが表示中のメッシュにありません: ") + path + " / " + shape);
            row.Weights[index] = weight;
        }

        // Stops a save before the export when the source changed after the board opened.
        internal void RequireCurrentSource() => RequireValid(always: true);

        // The rest of this batch reads the copy as it is, without hashing the
        // source: for the board's idle reads right after it built the copy
        // again from an avatar that still reads differently. Exports and
        // RequireCurrentSource hash it all the same.
        internal void TrustCopyForBatch() { if (!disposed && batchDepth > 0) validatedBatch = batchId; }

        // For a drop on the board: the source is hashed only when the editor
        // reported an edit since the last full check, so moving a face does
        // not wait for the whole avatar. The renderers and meshes are still
        // compared. A script that writes without Undo is not reported; the
        // next action that checks, and every export, still hash the source.
        internal void RequireSourceUnlessUnedited()
        {
            if (!disposed && batchDepth > 0 && hashedEdits == EditorEdits.Count) validatedBatch = batchId;
            RequireValid();
        }
        int hashedEdits = -1;

        // Edits the editor reported since it loaded: Undo-recorded changes,
        // undo and redo, asset imports, deletions and moves, and play mode.
        internal static class EditorEdits
        {
            internal static int Count { get; private set; }
            internal static void Note() => Count++;
            [InitializeOnLoadMethod]
            static void Watch()
            {
                ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) => Note();
                Undo.undoRedoPerformed += Note;
                EditorApplication.projectChanged += Note;
                EditorApplication.playModeStateChanged += change => Note();
            }
        }

        internal void RestoreBaseline()
        {
            RequireValid();
            ApplyUnchecked(Baseline);
        }

        Pose Snapshot(string name)
        {
            var pose = new Pose { Name = name };
            foreach (var channel in Channels)
            {
                var weights = Enumerable.Range(0, channel.Shapes.Length).Select(channel.Renderer.GetBlendShapeWeight).ToArray();
                if (weights.Any(weight => !Finite(weight))) throw new InvalidOperationException(ExporterLocalization.T("BlendShapeに不正な変形量があります。"));
                pose.Rows.Add(new Row { Path = channel.Path, Shapes = (string[])channel.Shapes.Clone(), Weights = weights });
            }
            return pose;
        }

        void ValidatePose(Pose pose)
        {
            if (pose == null || pose.Rows == null || pose.Rows.Count != Channels.Count)
                throw new InvalidOperationException(ExporterLocalization.T("記録設定のメッシュ数が一致しません。"));
            for (var i = 0; i < Channels.Count; i++)
            {
                var row = pose.Rows[i]; var channel = Channels[i];
                if (row == null || row.Path != channel.Path || row.Shapes == null || !row.Shapes.SequenceEqual(channel.Shapes) ||
                    row.Weights == null || row.Weights.Length != channel.Shapes.Length || row.Weights.Any(weight => !Finite(weight)))
                    throw new InvalidOperationException(ExporterLocalization.T("記録設定の参照・変形量が一致しません。"));
            }
        }

        void ApplyUnchecked(Pose pose)
        {
            for (var i = 0; i < Channels.Count; i++)
                for (var shape = 0; shape < pose.Rows[i].Weights.Length; shape++)
                    Channels[i].Renderer.SetBlendShapeWeight(shape, pose.Rows[i].Weights[shape]);
        }

        bool HasGeometryChange(Pose pose)
        {
            var clamp = PlayerSettings.legacyClampBlendShapeWeights;
            for (var i = 0; i < Channels.Count; i++)
            {
                if (Baseline.Rows[i].Weights.SequenceEqual(pose.Rows[i].Weights)) continue;
                // Copying a face mesh with all its BlendShapes takes a long time,
                // and the board checks every slot. When the face moves a vertex
                // and every delta is finite, the copy below would say the same.
                if (MovesVertices(AvatarBaseShape.ExpressionDeltas(Channels[i].Mesh, Baseline.Rows[i].Weights, pose.Rows[i].Weights, clamp))) return true;
                var mesh = Object.Instantiate(Channels[i].Mesh);
                try
                {
                    AvatarBaseShape.AppendExpression(Channels[i].Mesh, mesh, "__CaptureProbe_" + DeterministicNames.Hash("CaptureProbe", Channels[i].Path),
                        Baseline.Rows[i].Weights, pose.Rows[i].Weights, clamp);
                    if (AvatarBaseShape.HasUsableMorphEndpoint(mesh, mesh.blendShapeCount - 1, 0, 100)) return true;
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            return false;
        }

        static bool MovesVertices(AvatarBaseShape.Deltas delta)
        {
            var moves = false;
            for (var vertex = 0; vertex < delta.Vertices.Length; vertex++)
            {
                if (!Finite(delta.Vertices[vertex]) || !Finite(delta.Normals[vertex]) || !Finite(delta.Tangents[vertex])) return false;
                moves |= delta.Vertices[vertex].x != 0f || delta.Vertices[vertex].y != 0f || delta.Vertices[vertex].z != 0f;
            }
            return moves;
        }

        // listFaces are the board's listed clips (a moving one keeps its
        // animation): the VRM's custom expressions. The board passes its list
        // even when it is empty, so there is no other route.
        internal byte[] Export(string avatarName, string author, IReadOnlyList<(string Name, AnimationClip Clip, float Time)> listFaces,
            ICollection<string> warnings = null, BlinkExportOptions blinkOptions = null,
            AvatarLicenseOptions licenseOptions = null, ExportRecoveryOptions recoveryOptions = null, ExportRecoveryReport recoveryReport = null,
            PoseExportOptions recordedPoses = null, IEnumerable<PresetAssignment> presets = null, bool sourceChecked = false,
            bool autoLightweight = false)
        {
            if (listFaces == null) throw new ArgumentNullException(nameof(listFaces));
            // The export hashes the source again, even inside a batch, unless
            // the caller has just done so (sourceChecked) with nothing run since.
            RequireValid(always: true, sourceChecked: sourceChecked);
            // Stop before any stage on a source VRM cannot carry. A failing
            // stage still stops here and offers the recovery window.
            UniVrmOneClickExporter.ValidateSourceHumanoid(Source);
            UniVrmOneClickExporter.ValidateSourceHasMesh(Source);
            // Stop without lilToon before preparing the export copy; other versions only warn.
            var lilToonVersion = ExportEntry.RequireLilToon(Source, warnings);
            var poses = (recordedPoses ?? PoseOptions).Copy();
            ValidatePose(Baseline);
            // A chosen "none" is a face without a pose or morphs: an empty
            // mouth preset, an empty blink slot.
            var selectedPresets = WithoutBrokenSlots(SelectedPresets(presets?.Select(value => value?.Copy()).ToArray()), warnings);
            var presetFaces = selectedPresets
                .Where(item => !item.Assignment.IsNone || ChosenNone(item.Preset, item.Assignment))
                .Select(item =>
                {
                    var pose = item.Assignment.IsNone ? null : BuildPresetPose(item.Preset, item.Assignment);
                    var morphs = pose == null ? new List<(string Path, string Shape, float Weight)>()
                        : ExpressionPresetGroups.Blinks.Contains(item.Preset) ? BlinkMorphs(item.Assignment, pose) : PresetMorphs(item.Assignment);
                    return (item.Preset, Pose: pose, Morphs: morphs, Automatic: item.Assignment.Automatic && !item.Assignment.IsNone);
                }).ToArray();
            // A slot the user filled with a face that also moves bones or shows
            // objects keeps its BlendShapes. An emotion, mouth or blink slot
            // writes those bones and objects too (PresetBonesClip); the result
            // list says what a slot left out.
            foreach (var item in selectedPresets)
                if (PresetLeftOut(item.Assignment) is string leftOut)
                    warnings?.Add(PresetFullLabel(item.Preset) + ": " +
                        ExporterLocalization.T(CarriesBones(item.Assignment.Preset) ? PresetFingersLeftOutNote : PresetLeftOutNote) + leftOut);
            // Gaze and neutral the board leaves open are filled from names as in
            // the quick export; only the user's own "none" keeps one empty.
            var keepEmpty = selectedPresets
                .Where(item => item.Assignment.IsNone && !item.Assignment.Automatic &&
                    (ExpressionPresetGroups.LookAts.Contains(item.Preset) || item.Preset == ExpressionPreset.neutral))
                .Select(item => item.Preset.ToString()).ToArray();
            var leftEye = selectedPresets.FirstOrDefault(item => item.Preset == ExpressionPreset.blinkLeft).Assignment;
            var rightEye = selectedPresets.FirstOrDefault(item => item.Preset == ExpressionPreset.blinkRight).Assignment;
            if (leftEye != null && !leftEye.IsNone && BlinkSideOverlap(ExpressionPreset.blinkLeft, leftEye, rightEye) is string overlap)
                throw new InvalidOperationException(overlap);
            // Blink presets are written by the blink export only. Automatic
            // candidates repeat its own choice, so they leave blinkOptions as
            // is. The user's choices, "none" included, replace their slots of
            // that setting as a manual one, even when it is no blink.
            var blinkFaces = presetFaces.Where(face => ExpressionPresetGroups.Blinks.Contains(face.Preset) && !face.Automatic).ToArray();
            if (blinkFaces.Length > 0) blinkOptions = AssignedBlink(blinkOptions, blinkFaces.Select(face => (face.Preset, face.Morphs)));
            presetFaces = presetFaces.Where(face => !ExpressionPresetGroups.Blinks.Contains(face.Preset) && !LeftToNameBinding(face.Preset, face.Automatic, face.Morphs)).ToArray();
            // Standard presets enter the ordinary route's copy as authored VRM
            // clips, so authored presets remain. listFaces plays no menu face:
            // the exporter reads the listed clips as its faces, moving ones
            // with their animation; the default face is still the ordinary one.
            var authored = new List<Object>();
            try
            {
                var faces = listFaces.Select(face => (face.Name, Clip: AvatarRelativeClip(face.Clip, face.Time, authored), face.Time)).ToList();
                var bonesOnly = faces.Where((face, index) => !TakenWithoutBonesOrVisibility(listFaces[index].Clip)).Select(face => face.Clip).ToArray();
                // The faces a hand gesture enters keep the gesture source; the
                // file lists them first, before FaceEmo's and the others.
                var gestureFaces = faces.Where((face, index) => IsGestureClip(listFaces[index].Clip)).Select(face => face.Clip).ToArray();
                var presetBones = selectedPresets
                    .Select(item => (Preset: item.Preset.ToString(), Label: PresetFullLabel(item.Preset), Clip: PresetBonesClip(item.Assignment, authored)))
                    .Where(item => item.Clip != null).ToList();
                var authoredVrm = UniVrmOneClickExporter.Export(Source, avatarName, author, warnings: warnings,
                    exporterVersion: ExportEntry.PackageVersion(),
                    lilToonVersion: lilToonVersion, blinkOptions: blinkOptions, poseOptions: poses,
                    licenseOptions: licenseOptions ?? new AvatarLicenseOptions(), disableAudioLink: true,
                    recoveryOptions: recoveryOptions, recoveryReport: recoveryReport,
                    authorCopyExpressions: (clone, copyWarnings) => AuthorPresets(clone, presetFaces.Select(face => (face.Preset, face.Morphs)), authored, copyWarnings),
                    inferEmotionPresets: false, inferNeutralAndGazePresets: true, keepEmptyPresets: keepEmpty,
                    excludedObjects: manualExcluded, gimmickOptions: GimmickOptions,
                    listFaces: faces, listFaceSources: listFaces.Select(face => face.Clip).ToList(), autoLightweight: autoLightweight,
                    bonesOnlyFaces: bonesOnly, gestureFaces: gestureFaces, presetBones: presetBones);
                UniVrmOneClickExporter.ValidateOutputHasMesh(authoredVrm);
                return authoredVrm;
            }
            finally { foreach (var asset in authored) if (asset != null) Object.DestroyImmediate(asset); }
        }

        static string ValidateName(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 128 || name.Any(char.IsControl))
                throw new InvalidOperationException(ExporterLocalization.T("表情名は制御文字を含まない1〜128文字で入力してください。"));
            return name;
        }

        internal static Pose ClonePose(Pose pose) => new Pose { Name = pose.Name, InstalledSourceId = pose.InstalledSourceId, Rows = pose.Rows.Select(row => new Row {
            Path = row.Path, Shapes = (string[])row.Shapes.Clone(), Weights = (float[])row.Weights.Clone() }).ToList() };

        static string MeshStamp(Mesh mesh)
        {
            using var hash = new ExportSourceFingerprint.Digest();
            ExportSourceFingerprint.MeshData(hash, mesh);
            return hash.Finish().ToString();
        }

        public void Dispose()
        {
            if (ownerReleased) return;
            ownerReleased = true;
            if (recoveryUsers == 0) DisposeCore();
        }

        void DisposeCore()
        {
            if (disposed) return;
            disposed = true;
            retainedPreview?.Cleanup(); retainedPreview = null;
            if (Copy != null) Object.DestroyImmediate(Copy);
            Copy = null;
            preparation?.Dispose(); preparation = null;
            authoredBindings?.Dispose(); authoredBindings = null;
            gimmicks?.Dispose(); gimmicks = null;
            exclusions?.Dispose(); exclusions = null;
            foreach (var mesh in ownedMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
            ownedMeshes.Clear();
        }
    }
}
