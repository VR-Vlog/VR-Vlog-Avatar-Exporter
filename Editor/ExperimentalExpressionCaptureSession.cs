using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

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
        // Meshes left out of recording because another mesh shares their path.
        // A face's curves on them are dropped; the rest of the face is kept.
        readonly HashSet<string> skippedPaths = new HashSet<string>(StringComparer.Ordinal);
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
        ExperimentalExpressionControllerScope controllers;
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

        // listFaces are the board's listed clips (a moving one keeps its
        // animation): the VRM's custom expressions. The board passes its list
        // even when it is empty, so there is no other route.
        internal byte[] Export(string avatarName, string author, IReadOnlyList<(string Name, AnimationClip Clip, float Time)> listFaces,
            ICollection<string> warnings = null, BlinkExportOptions blinkOptions = null,
            AvatarLicenseOptions licenseOptions = null, ExportRecoveryOptions recoveryOptions = null, ExportRecoveryReport recoveryReport = null,
            PoseExportOptions recordedPoses = null, IEnumerable<PresetAssignment> presets = null)
        {
            if (listFaces == null) throw new ArgumentNullException(nameof(listFaces));
            // The export always hashes the source again, even inside a batch.
            RequireValid(always: true);
            // Stop before any stage on a source VRM cannot carry. A failing
            // stage still stops here and offers the recovery window.
            UniVrmOneClickExporter.ValidateSourceHumanoid(Source);
            UniVrmOneClickExporter.ValidateSourceHasMesh(Source);
            // Stop without lilToon before preparing the export copy; other versions only warn.
            var lilToonVersion = LilToonExporterWindow.RequireLilToon(Source, warnings);
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
            // Standard presets enter the ordinary route's copy as authored VRM
            // clips, so authored presets remain. listFaces plays no menu face:
            // the exporter reads the listed clips as its faces, moving ones
            // with their animation; the default face is still the ordinary one.
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
                    listFaces: listFaces.Select(face => (face.Name, AvatarRelativeClip(face.Clip, face.Time, authored), face.Time)).ToList());
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
            preparation?.Dispose(); preparation = null;
            authoredBindings?.Dispose(); authoredBindings = null;
            gimmicks?.Dispose(); gimmicks = null;
            exclusions?.Dispose(); exclusions = null;
            foreach (var mesh in ownedMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
            ownedMeshes.Clear();
        }
    }
}
