using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // Explicit expression capture used by the primary VRM export window.
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

        [Serializable]
        internal sealed class Settings
        {
            public int Version = 1;
            public string[] MeshHashes;
            public Pose Baseline;
            public List<Pose> Expressions = new List<Pose>();
            public List<PoseReference> ManualPoses = new List<PoseReference>();
            public string[] ExcludedPoses;
            public List<PoseNameReference> PoseNames;
            // Absent in records saved before emotion presets: no assignment.
            public List<PresetAssignment> Presets;
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
        internal readonly PoseExportOptions PoseOptions = new PoseExportOptions();
        readonly Dictionary<string, SkinnedMeshRenderer> authoringRenderers = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
        readonly Dictionary<Renderer, Renderer> authoringRecoveryRenderers = new Dictionary<Renderer, Renderer>();
        // Meshes left out of recording because another mesh shares their path.
        // A face's curves on them are dropped; the rest of the face is kept.
        readonly HashSet<string> skippedPaths = new HashSet<string>(StringComparer.Ordinal);
        internal VrChatExpressionMenu.Source Candidates { get; private set; }
        internal Pose Baseline { get; private set; }
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        NdmfExportPreparation preparation;
        PreparedExpressionBindings authoredBindings;
        BlinkExportSession descriptorBlink;
        ExportObjectExclusions exclusions;
        ExportGimmickSession gimmicks;
        // The ordinary routes pass these on, so they leave out what Copy leaves out.
        GameObject[] manualExcluded = Array.Empty<GameObject>();
        ExportGimmickOptions gimmickOptions = new ExportGimmickOptions();
        internal IReadOnlyList<GameObject> ExcludedObjects => manualExcluded;
        internal ExportGimmickOptions GimmickOptions => new ExportGimmickOptions { AutoExclude = gimmickOptions.AutoExclude,
            IncludedObjects = gimmickOptions.IncludedObjects.ToArray(), IncludedComponents = gimmickOptions.IncludedComponents.ToArray() };
        ExperimentalExpressionControllerScope controllers;
        FaceEmoExpressions.BindingSnapshot faceEmoBindings;
        ExportRecoverySourceStamp sourceStamp;
        Pose preparedRest;
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

        internal ExperimentalExpressionCaptureSession(GameObject source) : this(source, true) { }

        // Manual exclusions and gimmick options follow the ordinary export:
        // detection skips what is excluded by hand, and both leave Copy.
        internal ExperimentalExpressionCaptureSession(GameObject source, bool replayInstalledDefaults,
            IEnumerable<GameObject> excludedObjects = null, ExportGimmickOptions gimmickOptions = null)
        {
            Source = source;
            try
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                using var steps = ExpressionBoardTimings.Sequence();
                steps.Next("clipRoots");
                CaptureClipRoots();
                steps.Next("copy");
                Copy = NdmfExportPreparation.InstantiateOwnedCopy(source);
                Copy.name = source.name;
                foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    var mapped = ExportRecoveryCopySession.Resolve(renderer.transform, source.transform, Copy.transform);
                    var index = Array.IndexOf(renderer.GetComponents<Renderer>().Where(item => item.GetType() == renderer.GetType()).ToArray(), renderer);
                    authoringRecoveryRenderers.Add(renderer, mapped.GetComponents<Renderer>().Where(item => item.GetType() == renderer.GetType()).ElementAt(index));
                }
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
                foreach (var finding in findings.Where(value => value.Renderer == null))
                    if (automaticRoots.Contains(finding.Target)) Warnings.Add("補助ギミックを省略: " + finding.Target.name + " — " + finding.Reason);
                    else if (finding.Unit == GimmickExclusionUnit.Review) Warnings.Add("自動除外せず保持: " + finding.Target.name + " — " + finding.Reason);
                steps.Next("blink");
                // Preserve descriptor shape names/identity before MA can move
                // the renderer or reorder its channels. Missing automatic
                // configuration must still permit manual/no-blink exports.
                try
                {
                    using var configured = BlinkExportSession.CaptureForExport(source, null, exclusions.Contains);
                    if (configured.ConfiguredByDescriptor) descriptorBlink = configured.ForClone(source, Copy);
                }
                catch (InvalidOperationException) { }
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
                preparation = NdmfExportPreparation.Prepare(source, Copy, Warnings, afterTransforming: transformed => {
                    authoredBindings.RebindPrepared(transformed.PreparedRendererFor);
                    authoredBindings.RebindAuthoredExpressions(transformed.IsolatedCopyOf);
                    descriptorBlink?.RebindPrepared(transformed.PreparedRendererFor);
                    faceEmoBindings.RebindPrepared(transformed.PreparedRendererFor, transformed.ObjectRegistry, transformed.IsolatedCopyOf);
                    gimmicks.Apply(null, null, Warnings);
                });
                steps.Next("channels");
                if (replayInstalledDefaults) controllers = new ExperimentalExpressionControllerScope(Copy, Warnings);
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
                    if (VrChatExpressionSampler.FindRenderer(Copy, path) != renderer)
                        throw new InvalidOperationException(ExporterLocalization.T("表情のメッシュを一意に特定できません。"));
                    renderer.forceMatrixRecalculationPerRender = true;
                    Channels.Add(new Channel { Renderer = renderer, Mesh = mesh, Path = path,
                        Shapes = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray(),
                        MeshHash = MeshStamp(mesh) });
                }
                steps.Next("menu");
                var installed = VrChatExpressionMenu.Read(Copy);
                if (replayInstalledDefaults && installed.Controller != null)
                {
                    NeutralInputProof.Read(Copy, installed);
                    try
                    {
                        var neutral = Snapshot("FXの基準表情");
                        foreach (var value in ExperimentalInstalledExpressionPlayer.Sample(Copy, installed, new Dictionary<string, float>(), warnings: Warnings))
                            if (!skippedPaths.Contains(value.Path)) Set(neutral, value.Path, value.Shape, value.Weight);
                        ValidatePose(neutral); ApplyUnchecked(neutral);
                    }
                    catch (InvalidOperationException error) { Warnings.Add(ExporterLocalization.T("FXの基準表情を収録できませんでした: ") + error.Message); }
                }
                steps.Next("snapshot");
                preparedRest = Snapshot("準備後の顔");
                Baseline = Snapshot("基準の顔");
                steps.Next("fingerprint");
                // Settle source read caches before guarding subsequent edits.
                // Like the recovery window, a partially driven VRChat rotation
                // may differ by the solver's re-encoding noise only.
                sourceStamp = ExportRecoverySourceStamp.Capture(source);
                // The stamps were just taken from the source as it is: the rest
                // of this batch need not hash the whole avatar again.
                if (batchDepth > 0) validatedBatch = batchId;
            }
            catch { Dispose(); throw; }
        }

        internal bool HasCurrentSource => !disposed && Source != null && sourceStamp.Matches(Source);

        // Hashing the whole source avatar and its meshes takes a long time on a
        // real avatar, and preparing the board checks the session once per
        // file and per slot. Inside one Batch nothing else in the editor runs,
        // so the source cannot change: the hashes are checked once per batch.
        // Export checks them again anyway, as it writes the file.
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

        void RequireValid(bool always = false)
        {
            if (disposed || Copy == null) throw new InvalidOperationException(ExporterLocalization.T("表情記録のコピーがありません。"));
            var hashed = !always && batchDepth > 0 && validatedBatch == batchId;
            using var timing = hashed ? null : ExpressionBoardTimings.Step("validate");
            if (Source == null || !hashed && !sourceStamp.Matches(Source))
                throw new InvalidOperationException(ExporterLocalization.T("元のアバターが変更されました。「変更」からアバターを指定し直してください。"));
            foreach (var channel in Channels)
                if (channel.Renderer == null || !channel.Renderer.enabled || !channel.Renderer.gameObject.activeInHierarchy ||
                    channel.Renderer.sharedMesh != channel.Mesh || !hashed && MeshStamp(channel.Mesh) != channel.MeshHash)
                    throw new InvalidOperationException(ExporterLocalization.T("書き出し用メッシュが変更されました。「変更」からアバターを指定し直してください。"));
            if (batchDepth > 0) validatedBatch = batchId;
        }

        internal void DiscoverCandidates()
        {
            RequireValid();
            if (controllers == null) controllers = new ExperimentalExpressionControllerScope(Copy, Warnings);
            var current = Snapshot("現在の顔");
            try
            {
                ApplyUnchecked(preparedRest);
                var result = VrChatExpressionMenu.Read(Copy);
                NeutralInputProof.Read(Copy, result);
                foreach (var entry in result.Entries)
                {
                    if (entry.Error != null) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("導入済みの表情を収録中", entry.Name,
                        (float)result.Entries.IndexOf(entry) / Mathf.Max(1, result.Entries.Count))) throw new OperationCanceledException();
                    try
                    {
                        var settlement = new List<string>();
                        entry.Values.AddRange(ExperimentalInstalledExpressionPlayer.Sample(Copy, result, entry.Parameters, warnings: settlement));
                        foreach (var message in settlement) { entry.Messages.Add(message); Warnings.Add(entry.Name + ": " + message); }
                    }
                    catch (InvalidOperationException error) { entry.Error = error.Message; }
                }
                // Registered FaceEmo branches can exist outside the avatar root,
                // including gesture variants not exposed by a generated menu.
                FaceEmoExpressions.Add(Copy, result, authoringSource: Source, bindings: faceEmoBindings);
                using (var bindings = new PreparedExpressionBindings(Copy, result))
                    FaceEmoExpressions.ApplyPreparedDefaultFace(Copy, result, bindings, registeredBindings: faceEmoBindings);
                Candidates = result;
            }
            finally { ApplyUnchecked(current); EditorUtility.ClearProgressBar(); }
        }

        internal void PreviewCandidate(VrChatExpressionMenu.Entry entry, double time = 0)
        {
            RequireValid();
            if (Candidates == null || !Candidates.Entries.Contains(entry))
                throw new InvalidOperationException(ExporterLocalization.T("このコピーから取得した表情候補を選んでください。"));
            if (entry.Error != null) throw new InvalidOperationException(entry.Error);
            if (entry.Unevaluated.Count != 0) throw new InvalidOperationException(ExporterLocalization.T("未解決の変形がある候補は記録できません。"));
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0 || time > entry.Duration)
                throw new InvalidOperationException(ExporterLocalization.T("表情の時刻が範囲外です。"));
            // Build and validate first. A missing channel must not partially change the preview.
            var pose = ClonePose(preparedRest);
            foreach (var value in entry.Values.Where(value => !skippedPaths.Contains(value.Path))) Set(pose, value.Path, value.Shape, value.Weight);
            foreach (var value in entry.Animation.Where(value => !skippedPaths.Contains(value.Path))) Set(pose, value.Path, value.Shape, (float)value.Curve.Evaluate(time));
            ValidatePose(pose);
            ApplyUnchecked(pose);
        }

        internal sealed class ImportResult
        {
            internal readonly List<string> Imported = new List<string>();
            internal readonly List<string> Skipped = new List<string>();
        }

        internal ImportResult ImportCandidates(IEnumerable<VrChatExpressionMenu.Entry> entries)
        {
            RequireValid();
            if (Candidates == null) throw new InvalidOperationException(ExporterLocalization.T("既存の表情を先に読み込んでください。"));
            var result = new ImportResult();
            var current = Snapshot("現在の顔");
            var staged = new List<Pose>();
            var names = new HashSet<string>(Expressions.Select(pose => pose.Name), StringComparer.Ordinal);
            try
            {
                foreach (var entry in entries)
                {
                    if (!Candidates.Entries.Contains(entry)) throw new InvalidOperationException(ExporterLocalization.T("別のコピーの表情が選択されています。"));
                    if (entry.Error != null || entry.Unevaluated.Count > 0)
                    { result.Skipped.Add(entry.Name + ": " + (entry.Error ?? "未解決の変形")); continue; }
                    if (entry.Animation.Count != 0)
                    { result.Skipped.Add(entry.Name + ": 動く表情はプレビューで収録時刻を指定してください。"); continue; }
                    PreviewCandidate(entry);
                    var name = ValidateName(entry.Name);
                    var pose = Snapshot(name);
                    pose.InstalledSourceId = entry.Id;
                    if (names.Contains(name))
                    { result.Skipped.Add(name + ": 同名の表情を登録済みです。"); continue; }
                    names.Add(name); staged.Add(pose); result.Imported.Add(name);
                }
                // A failure never publishes half an import or changes the preview.
                Expressions.AddRange(staged);
                return result;
            }
            finally { ApplyUnchecked(current); }
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

        internal void SetWeight(int channel, int shape, float value)
        {
            if (!Finite(value)) throw new InvalidOperationException(ExporterLocalization.T("変形量に有限の数値を入力してください。"));
            var target = Channels[channel];
            if (disposed || target.Renderer == null || target.Renderer.sharedMesh != target.Mesh)
                throw new InvalidOperationException(ExporterLocalization.T("記録用メッシュがありません。"));
            target.Renderer.SetBlendShapeWeight(shape, value);
        }

        // Stops a save before the export when the source changed after the board opened.
        internal void RequireCurrentSource() => RequireValid(always: true);

        internal void CaptureBaseline()
        {
            RequireValid();
            Baseline = Snapshot("基準の顔");
        }

        internal void RestoreBaseline()
        {
            RequireValid();
            ApplyUnchecked(Baseline);
        }

        internal Pose Capture(string name)
        {
            RequireValid();
            name = ValidateName(name);
            if (Expressions.Any(pose => pose.Name == name)) throw new InvalidOperationException(ExporterLocalization.T("同じ名前の表情があります。別の名前を入力してください。"));
            var pose = Snapshot(name);
            if (!HasGeometryChange(pose)) throw new InvalidOperationException(ExporterLocalization.T("基準の顔と形状が同じです。変形を調整してから記録してください。"));
            Expressions.Add(pose);
            return pose;
        }

        internal void PreviewRecorded(Pose pose)
        {
            RequireValid();
            if (!Expressions.Contains(pose)) throw new InvalidOperationException(ExporterLocalization.T("記録した表情を選んでください。"));
            ValidatePose(pose);
            ApplyUnchecked(pose);
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

        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        internal string SaveSettings()
        {
            // Recorded arrays remain useful recovery data after source edits.
            // Applying or exporting them still requires all identity checks.
            if (disposed) throw new InvalidOperationException(ExporterLocalization.T("表情記録のコピーがありません。"));
            return JsonUtility.ToJson(new Settings { MeshHashes = Channels.Select(channel => channel.MeshHash).ToArray(),
                Baseline = ClonePose(Baseline), Expressions = Expressions.Select(ClonePose).ToList(),
                ManualPoses = PoseOptions.Manual.Select(SavePoseReference).ToList(), ExcludedPoses = PoseOptions.Excluded.ToArray(),
                PoseNames = PoseOptions.Names.Select(pair => new PoseNameReference { Id = pair.Key, Name = pair.Value }).ToList(),
                Presets = SavePresets() }, true);
        }

        internal void LoadSettings(string json)
        {
            RequireValid();
            if (json == null || json.Length > 8 * 1024 * 1024) throw new InvalidOperationException(ExporterLocalization.T("記録設定のサイズが不正です。"));
            Settings settings;
            try { settings = JsonUtility.FromJson<Settings>(json); }
            catch (Exception error) { throw new InvalidOperationException(ExporterLocalization.T("記録設定を読み込めません。"), error); }
            if (settings == null || settings.Version != 1 || settings.MeshHashes == null ||
                !settings.MeshHashes.SequenceEqual(Channels.Select(channel => channel.MeshHash)) || settings.Expressions == null)
                throw new InvalidOperationException(ExporterLocalization.T("記録設定の形式またはメッシュが一致しません。"));
            ValidatePose(settings.Baseline);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pose in settings.Expressions)
            {
                ValidatePose(pose);
                if (ValidateName(pose.Name) != pose.Name || !names.Add(pose.Name))
                    throw new InvalidOperationException(ExporterLocalization.T("記録設定の表情名が重複または不正です。"));
            }
            var manualPoses = (settings.ManualPoses ?? new List<PoseReference>()).Select(LoadPoseReference).ToList();
            if (manualPoses.Count > 128) throw new InvalidOperationException(ExporterLocalization.T("同梱ポーズは128件までです。"));
            var poseNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var reference in settings.PoseNames ?? new List<PoseNameReference>())
                if (reference == null || string.IsNullOrEmpty(reference.Id) || poseNames.ContainsKey(reference.Id))
                    throw new InvalidOperationException(ExporterLocalization.T("ポーズの表示名設定が不正です。"));
                else poseNames.Add(reference.Id, ValidateName(reference.Name));
            var presets = LoadPresets(settings.Presets);
            // Publish only after validating every row and expression.
            Baseline = ClonePose(settings.Baseline);
            Expressions.Clear(); Expressions.AddRange(settings.Expressions.Select(ClonePose));
            PoseOptions.Manual.Clear(); PoseOptions.Manual.AddRange(manualPoses);
            PoseOptions.Excluded.Clear(); PoseOptions.Names.Clear();
            PoseOptions.Excluded.UnionWith(settings.ExcludedPoses ?? Array.Empty<string>());
            foreach (var pair in poseNames) PoseOptions.Names.Add(pair.Key, pair.Value);
            Presets.Clear();
            foreach (var assignment in presets) Presets.Add(ParsePreset(assignment.Preset), assignment);
            ApplyUnchecked(Baseline);
        }

        internal byte[] Export(string avatarName, string author, ICollection<string> warnings = null, BlinkExportOptions blinkOptions = null,
            AvatarLicenseOptions licenseOptions = null, ExportRecoveryOptions recoveryOptions = null, ExportRecoveryReport recoveryReport = null, IEnumerable<Pose> recordedExpressions = null, PoseExportOptions recordedPoses = null,
            IEnumerable<PresetAssignment> presets = null, bool replaceCustomFaces = false,
            IReadOnlyList<(string Name, AnimationClip Clip, float Time)> listFaces = null)
        {
            // The export always hashes the source again, even inside a batch.
            RequireValid(always: true);
            // The same checks as the other user-facing exports (ExportSettings.Export):
            // stop before any stage on a source VRM cannot carry. A failing stage
            // still stops here and offers the recovery window.
            UniVrmOneClickExporter.ValidateSourceHumanoid(Source);
            UniVrmOneClickExporter.ValidateSourceHasMesh(Source);
            // Stop without lilToon before preparing the export copy; other versions only warn.
            var lilToonVersion = LilToonExporterWindow.RequireLilToon(Source, warnings);
            var expressions = (recordedExpressions ?? Expressions).Select(ClonePose).ToArray();
            var poses = (recordedPoses ?? PoseOptions).Copy();
            ValidatePose(Baseline);
            // A chosen "none" is a face without a pose or morphs: an empty
            // mouth preset, an empty blink slot.
            var selectedPresets = SelectedPresets(presets?.Select(value => value?.Copy()).ToArray());
            var presetFaces = selectedPresets
                .Where(item => !item.Assignment.IsNone || ChosenNone(item.Preset, item.Assignment))
                .Select(item =>
                {
                    var pose = item.Assignment.IsNone ? null : BuildPresetPose(item.Preset, item.Assignment);
                    var morphs = pose == null ? new List<(string Path, string Shape, float Weight)>()
                        : BlinkPresets.Contains(item.Preset) ? BlinkMorphs(item.Assignment, pose) : PresetMorphs(item.Assignment);
                    return (item.Preset, Pose: pose, Morphs: morphs, Automatic: item.Assignment.Automatic && !item.Assignment.IsNone);
                }).ToArray();
            // Gaze and neutral the board leaves open are filled from names as in
            // the quick export; only the user's own "none" keeps one empty.
            var keepEmpty = selectedPresets
                .Where(item => item.Assignment.IsNone && !item.Assignment.Automatic &&
                    (LookAtPresets.Contains(item.Preset) || item.Preset == ExpressionPreset.neutral))
                .Select(item => item.Preset.ToString()).ToArray();
            var leftEye = selectedPresets.FirstOrDefault(item => item.Preset == ExpressionPreset.blinkLeft).Assignment;
            var rightEye = selectedPresets.FirstOrDefault(item => item.Preset == ExpressionPreset.blinkRight).Assignment;
            if (leftEye != null && !leftEye.IsNone && BlinkSideOverlap(ExpressionPreset.blinkLeft, leftEye, rightEye) is string overlap)
                throw new InvalidOperationException(overlap);
            // Blink presets are written by the blink export only. Automatic
            // candidates repeat its own choice, so they leave blinkOptions as
            // is. The user's choices, "none" included, replace their slots of
            // that setting as a manual one, even when it is no blink.
            var blinkFaces = presetFaces.Where(face => BlinkPresets.Contains(face.Preset) && !face.Automatic).ToArray();
            if (blinkFaces.Length > 0) blinkOptions = AssignedBlink(blinkOptions, blinkFaces.Select(face => (face.Preset, face.Morphs)));
            presetFaces = presetFaces.Where(face => !BlinkPresets.Contains(face.Preset) && !LeftToNameBinding(face.Preset, face.Automatic, face.Morphs)).ToArray();
            // Without explicit faces, retain the ordinary exporter's VRChat
            // menu/FX, authored expressions, metadata and preparation route.
            // replaceCustomFaces asks for exactly the recorded custom faces, even
            // none, so both shortcuts that keep the menu faces are skipped.
            if (!replaceCustomFaces && expressions.Length == 0 && presetFaces.Length == 0 && listFaces == null)
            {
                var ordinary = UniVrmOneClickExporter.Export(Source, avatarName, author, warnings,
                    exporterVersion: LilToonExporterWindow.PackageVersion(),
                    lilToonVersion: lilToonVersion, blinkOptions: blinkOptions, poseOptions: poses,
                    licenseOptions: licenseOptions ?? new AvatarLicenseOptions(), disableAudioLink: true,
                    recoveryOptions: recoveryOptions, recoveryReport: recoveryReport, inferEmotionPresets: false,
                    inferNeutralAndGazePresets: true, keepEmptyPresets: keepEmpty,
                    excludedObjects: manualExcluded, gimmickOptions: GimmickOptions);
                UniVrmOneClickExporter.ValidateOutputHasMesh(ordinary);
                return ordinary;
            }
            // Standard presets alone keep that ordinary route. They enter its copy
            // as authored VRM clips, so menu faces and authored presets remain.
            // listFaces (the list-only export) plays no menu face: the exporter
            // reads the listed clips as its faces, moving ones with their
            // animation; the default face is still the ordinary one.
            if (!replaceCustomFaces && expressions.Length == 0)
            {
                var authored = new List<Object>();
                try
                {
                    var authoredVrm = UniVrmOneClickExporter.Export(Source, avatarName, author, warnings,
                        exporterVersion: LilToonExporterWindow.PackageVersion(),
                        lilToonVersion: lilToonVersion, blinkOptions: blinkOptions, poseOptions: poses,
                        licenseOptions: licenseOptions ?? new AvatarLicenseOptions(), disableAudioLink: true,
                        recoveryOptions: recoveryOptions, recoveryReport: recoveryReport,
                        authorCopyExpressions: clone => AuthorPresets(clone, presetFaces.Select(face => (face.Preset, face.Morphs)), authored),
                        inferEmotionPresets: false, inferNeutralAndGazePresets: true, keepEmptyPresets: keepEmpty,
                        excludedObjects: manualExcluded, gimmickOptions: GimmickOptions,
                        listFaces: listFaces?.Select(face => (face.Name, AvatarRelativeClip(face.Clip, face.Time, authored), face.Time)).ToList());
                    UniVrmOneClickExporter.ValidateOutputHasMesh(authoredVrm);
                    return authoredVrm;
                }
                finally { foreach (var asset in authored) if (asset != null) Object.DestroyImmediate(asset); }
            }
            foreach (var pose in expressions)
            {
                ValidatePose(pose);
                if (!HasGeometryChange(pose) && string.IsNullOrEmpty(pose.InstalledSourceId))
                    throw new InvalidOperationException(pose.Name + ExporterLocalization.T(": 基準の顔と形状が同じです。"));
            }
            expressions = MergeIdentical(expressions, recoveryReport);
            var exportCopy = NdmfExportPreparation.InstantiateOwnedCopy(Copy);
            exportCopy.name = Copy.name;
            var meshes = new List<Mesh>();
            var assets = new List<Object>();
            try
            {
                // Resolve configured eyelids while the disposable copy still has
                // its descriptor. The normal exporter remaps these renderer
                // references to its own copy before any preparation pass.
                var exportBlinkOptions = blinkOptions;
                if (blinkOptions?.Mode == BlinkExportMode.Manual)
                {
                    using var configured = BlinkExportSession.CaptureForExport(Source, blinkOptions);
                    configured.RebindPrepared(renderer => {
                        var path = AnimationUtility.CalculateTransformPath(renderer.transform, Source.transform);
                        if (!authoringRenderers.TryGetValue(path, out var authoring))
                            throw new InvalidOperationException(NdmfExportPreparation.UnknownRendererRelocation);
                        return preparation.PreparedRendererFor(authoring);
                    });
                    using var cloned = configured.ForClone(Copy, exportCopy);
                    exportBlinkOptions = new BlinkExportOptions();
                    exportBlinkOptions.SelectMode(BlinkExportMode.Manual, cloned);
                }
                else if (blinkOptions == null || blinkOptions.Mode == BlinkExportMode.Auto)
                {
                    using var resolvedBlink = descriptorBlink?.ForClone(Copy, exportCopy) ?? BlinkExportSession.CaptureForExport(exportCopy, blinkOptions);
                    if (descriptorBlink != null || resolvedBlink.ConfiguredByDescriptor)
                    {
                        exportBlinkOptions = new BlinkExportOptions();
                        exportBlinkOptions.SelectMode(BlinkExportMode.Manual, resolvedBlink);
                    }
                }
                // Lip Sync names the mouth shapes on this descriptor. Like blink,
                // capture it before the descriptor is removed below.
                var visemes = VrChatVisemeExport.Read(exportCopy);
                // Source controllers remain on the preview for candidate discovery.
                // This private export copy already contains the selected completed faces.
                // Prevent a second Animator evaluation from replacing the explicit baseline.
                foreach (var descriptor in exportCopy.GetComponents<Component>().Where(component => component != null &&
                    component.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor").ToArray())
                    Object.DestroyImmediate(descriptor);
                foreach (var animator in exportCopy.GetComponentsInChildren<Animator>(true)) animator.runtimeAnimatorController = null;
                // Declare bindings before the normal exporter runs: its authored
                // endpoint and optimization mapping then protect and relocate them.
                // Late registration by morph name cannot survive AAO renaming.
                var instance = exportCopy.GetComponent<Vrm10Instance>() ?? exportCopy.AddComponent<Vrm10Instance>();
                var settings = instance.Vrm != null ? Object.Instantiate(instance.Vrm) : ScriptableObject.CreateInstance<VRM10Object>();
                assets.Add(settings);
                settings.Prefab = null;
                // Replace selectable custom faces, retaining authored functional
                // and standard presets (mouth, gaze, blink, neutral and emotions).
                if (expressions.Length > 0 || replaceCustomFaces || settings.Expression == null)
                {
                    var previous = settings.Expression;
                    settings.Expression = new VRM10ObjectExpression();
                    if (previous != null)
                        foreach (var (preset, clip) in previous.Clips)
                            if (preset != ExpressionPreset.custom) settings.Expression.AddClip(preset, clip);
                }
                instance.Vrm = settings;
                long addedBytes = 0;
                for (var i = 0; i < Channels.Count; i++)
                {
                    var renderer = VrChatExpressionSampler.FindRenderer(exportCopy, Channels[i].Path);
                    var mesh = Object.Instantiate(Channels[i].Mesh); meshes.Add(mesh); renderer.sharedMesh = mesh;
                    for (var shape = 0; shape < Baseline.Rows[i].Weights.Length; shape++)
                        renderer.SetBlendShapeWeight(shape, Baseline.Rows[i].Weights[shape]);
                }
                // Recorded names and presets identify each target; _2, _3
                // settle repeated renderer paths.
                var usedNames = DeterministicNames.BlendShapeNames(exportCopy);
                // Appends the change from Baseline to `to` on one channel as a
                // new blend shape and binds it, counting the added vertex data.
                void AppendCapture(List<MorphTargetBinding> into, int channel, string kind, string key, Pose to)
                {
                    var renderer = VrChatExpressionSampler.FindRenderer(exportCopy, Channels[channel].Path);
                    var target = DeterministicNames.Unique("__VRVlog_Capture_" + DeterministicNames.Hash(kind, Channels[channel].Path, key), usedNames);
                    addedBytes += Channels[channel].Mesh.vertexCount * 36L;
                    if (addedBytes > 128L * 1024 * 1024) throw new InvalidOperationException(ExporterLocalization.T("追加表情の形状データが128 MiBを超えます。"));
                    AvatarBaseShape.AppendExpression(Channels[channel].Mesh, renderer.sharedMesh, target,
                        Baseline.Rows[channel].Weights, to.Rows[channel].Weights, PlayerSettings.legacyClampBlendShapeWeights);
                    renderer.SetBlendShapeWeight(renderer.sharedMesh.blendShapeCount - 1, 0);
                    into.Add(new MorphTargetBinding(Channels[channel].Path, renderer.sharedMesh.blendShapeCount - 1, 1f));
                }
                foreach (var pose in expressions)
                {
                    var expression = ScriptableObject.CreateInstance<VRM10Expression>(); assets.Add(expression);
                    // The recorded name as is; a repeated, VRM preset or face
                    // tracking name gets " (2)", " (3)"... like a menu face.
                    expression.name = VrmMenuExpressions.UniqueName(pose.Name,
                        name => settings.Expression.CustomClips.Any(clip => clip != null && clip.name == name));
                    expression.IsBinary = true;
                    expression.OverrideBlink = expression.OverrideMouth = expression.OverrideLookAt = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_vrm.ExpressionOverrideType.block;
                    var bindings = new List<MorphTargetBinding>();
                    for (var i = 0; i < Channels.Count; i++)
                    {
                        if (Baseline.Rows[i].Weights.SequenceEqual(pose.Rows[i].Weights)) continue;
                        AppendCapture(bindings, i, "Capture", pose.Name, pose);
                    }
                    if (bindings.Count == 0)
                    {
                        // Preserve a named installed neutral face. A zero-delta
                        // target also keeps existing VRM/app morph-based catalogs
                        // aware of the entry and its override/reset semantics.
                        AppendCapture(bindings, 0, "Capture", pose.Name, Baseline);
                    }
                    expression.MorphTargetBindings = bindings.ToArray();
                    settings.Expression.CustomClips.Add(expression);
                }
                // Standard presets are separate clips. Recorded custom faces stay
                // custom clips under their recorded names (numbered on a clash),
                // so the animation extension still finds them.
                foreach (var face in presetFaces)
                {
                    var expression = CreatePresetExpression(face.Preset); assets.Add(expression);
                    var bindings = new List<MorphTargetBinding>();
                    // A chosen "none" stays an empty preset.
                    for (var i = 0; i < (face.Pose != null ? Channels.Count : 0); i++)
                    {
                        if (Baseline.Rows[i].Weights.SequenceEqual(face.Pose.Rows[i].Weights)) continue;
                        AppendCapture(bindings, i, "CapturePreset", face.Preset.ToString(), face.Pose);
                    }
                    expression.MorphTargetBindings = bindings.ToArray();
                    settings.Expression.AddClip(face.Preset, expression);
                }
                // Only exporter-owned isolation records establish material provenance.
                // Unknown materials generated by plugins never acquire a guessed remedy.
                var materialOrigins = Source.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct()
                    .ToDictionary(material => (Material)preparation.IsolatedCopyOf(material), material => material);
                var mappedRecovery = new ExportRecoveryOptions();
                foreach (var action in ExportRecoveryOptions.ActionsOrEmpty(recoveryOptions))
                {
                    var material = action.Material != null ? (Material)preparation.IsolatedCopyOf(action.Material) : null;
                    Renderer renderer = null;
                    if (action.Renderer != null)
                    {
                        if (authoringRecoveryRenderers.TryGetValue(action.Renderer, out var original))
                        {
                            var prepared = preparation.PreparedRendererFor(original);
                            if (prepared != null)
                            {
                                var mapped = ExportRecoveryCopySession.Resolve(prepared.transform, Copy.transform, exportCopy.transform);
                                var index = Array.IndexOf(prepared.GetComponents<Renderer>().Where(item => item.GetType() == prepared.GetType()).ToArray(), prepared);
                                renderer = mapped.GetComponents<Renderer>().Where(item => item.GetType() == prepared.GetType()).ElementAt(index);
                            }
                        }
                    }
                    mappedRecovery.Actions.Add(new ExportRecoveryAction { Id = action.Id, Kind = action.Kind, Material = material,
                        Renderer = renderer });
                }
                var bytes = UniVrmOneClickExporter.Export(exportCopy, avatarName, author, warnings,
                    exporterVersion: LilToonExporterWindow.PackageVersion(),
                    lilToonVersion: lilToonVersion, blinkOptions: exportBlinkOptions,
                    licenseOptions: licenseOptions ?? new AvatarLicenseOptions(), disableAudioLink: true,
                    recoveryOptions: mappedRecovery, recoveryReport: recoveryReport, recoverySource: Source,
                    recoveryMaterialSource: material => materialOrigins.TryGetValue(material, out var original) ? original : material,
                    inferEmotionPresets: false, inferNeutralAndGazePresets: true, keepEmptyPresets: keepEmpty,
                    descriptorVisemes: visemes);
                UniVrmOneClickExporter.ValidateOutputHasMesh(bytes);
                // The exporter's clone keeps this root scale and bakes it into the VRM.
                return InjectSourcePoses(bytes, warnings, poses, exportCopy.transform.localScale, recoveryReport);
            }
            finally
            {
                Object.DestroyImmediate(exportCopy);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                foreach (var asset in assets) Object.DestroyImmediate(asset);
            }
        }

        static string ValidateName(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 128 || name.Any(char.IsControl))
                throw new InvalidOperationException(ExporterLocalization.T("表情名は制御文字を含まない1〜128文字で入力してください。"));
            return name;
        }

        // Recorded faces with exactly the same weights on every row are one
        // face to the app, as menu faces are (VrmMenuExpressions.Add): the
        // first in the list is written and each later one is listed in the
        // result as merged into it. Presets hold their own faces, so none
        // points at a merged one.
        internal static Pose[] MergeIdentical(IEnumerable<Pose> poses, ExportRecoveryReport report = null)
        {
            var kept = new List<Pose>();
            foreach (var pose in poses)
            {
                var same = kept.FirstOrDefault(other => other.Rows.Count == pose.Rows.Count && other.Rows.Zip(pose.Rows, (a, b) =>
                    a.Path == b.Path && a.Shapes.SequenceEqual(b.Shapes) && a.Weights.SequenceEqual(b.Weights)).All(equal => equal));
                if (same == null) { kept.Add(pose); continue; }
                report?.Fallbacks.Record(ExportOutcome.Kept, ExporterLocalization.T(UniVrmOneClickExporter.MergedExpressionCategory), pose.Name,
                    string.Format(ExporterLocalization.T(UniVrmOneClickExporter.MergedExpressionFormat), same.Name));
            }
            return kept.ToArray();
        }

        internal static Pose ClonePose(Pose pose) => new Pose { Name = pose.Name, InstalledSourceId = pose.InstalledSourceId, Rows = pose.Rows.Select(row => new Row {
            Path = row.Path, Shapes = (string[])row.Shapes.Clone(), Weights = (float[])row.Weights.Clone() }).ToList() };

        static string MeshStamp(Mesh mesh)
        {
            using var hash = new ExportSourceFingerprint.Digest();
            ExportSourceFingerprint.MeshData(hash, mesh);
            return hash.Finish().ToString();
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

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
            controllers?.Dispose(); controllers = null;
            descriptorBlink?.Dispose(); descriptorBlink = null;
            preparation?.Dispose(); preparation = null;
            authoredBindings?.Dispose(); authoredBindings = null;
            gimmicks?.Dispose(); gimmicks = null;
            exclusions?.Dispose(); exclusions = null;
            foreach (var mesh in ownedMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
            ownedMeshes.Clear();
        }
    }
}
