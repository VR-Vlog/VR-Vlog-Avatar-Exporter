using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // Capture renderer identity before authoring passes; resolve shape names again
    // afterwards. Only the disposable export copy receives generated geometry.
    internal sealed class BlinkExportSession : IDisposable
    {
        internal readonly List<BlinkShapeBinding>[] Slots = {
            new List<BlinkShapeBinding>(), new List<BlinkShapeBinding>(), new List<BlinkShapeBinding>()
        };
        internal string Description;
        internal bool PreserveAuthored;
        internal bool ConfiguredByDescriptor;
        internal bool Disabled;
        // Automatic setup found no closing shape at all and fell back to no blink.
        internal bool MissingAutomatic;
        internal const string MissingAutomaticDescription = "瞬きなし（閉眼用の変形が見つかりません）";
        internal const string MissingAutomaticWarning = "閉眼用の変形が見つからないため瞬きなしで書き出しました（「瞬き」の「確認・調整」で手動選択）";
        // Automatic setup found closing names but could not pick one (duplicate
        // names, an eye without its other side, or shapes that do not move), so
        // it fell back to no blink. UnresolvedReason keeps the old error text.
        internal bool UnresolvedAutomatic;
        internal string UnresolvedReason;
        internal const string UnresolvedAutomaticDescription = "瞬きなし（閉眼用の変形を自動で決められません）";
        internal const string UnresolvedAutomaticWarning = "閉眼用の変形を自動で決められないため瞬きなしで書き出しました（「瞬き」の「確認・調整」で手動選択）";
        internal const string DuplicateClosingNames = "閉眼用の名前が重複しています。「確認・調整」で設定してください。";
        internal const string UnmatchedClosingSide = "閉眼用の左右がそろっていません: ";
        internal const string UnusableClosingShapes = "閉眼用の変形を特定できません。「確認・調整」で選択するか「瞬きなし」を指定してください。";
        bool allowMissingAutomaticBlink;
        bool deferredAutomatic;
        sealed class Candidate
        {
            internal BlinkShapeBinding Binding;
            internal bool Moving;
            internal float ReferenceWeight;
        }
        readonly List<Candidate> candidates = new List<Candidate>();
        readonly Dictionary<(SkinnedMeshRenderer Renderer, string Shape), float> inferredEndpoints =
            new Dictionary<(SkinnedMeshRenderer, string), float>();
        internal bool HasBilateralPreset => authored[0] || Slots[0].Count > 0;
        internal bool RequiresUnifiedEvidence => allowMissingAutomaticBlink && !PreserveAuthored && Slots[0].Count == 0 && Slots[1].Count == 0;
        readonly Dictionary<SkinnedMeshRenderer, int> nodes = new Dictionary<SkinnedMeshRenderer, int>();
        readonly bool[] authored = new bool[3];
        readonly List<Object> expressionCopies = new List<Object>();

        internal static BlinkExportSession Resolve(GameObject source, BlinkExportOptions options = null,
            Func<Transform, bool> excluded = null, bool suppressSharedTextureEmission = false, bool suppressHdrTextureEmission = false)
            => Resolve(source, options, excluded, suppressSharedTextureEmission, suppressHdrTextureEmission, false);

        // Export first captures identity; FX default states can change whether
        // a serialized fully-resting channel actually has closure range.
        internal static BlinkExportSession CaptureForExport(GameObject source, BlinkExportOptions options = null,
            Func<Transform, bool> excluded = null, bool suppressSharedTextureEmission = false, bool suppressHdrTextureEmission = false)
            => Resolve(source, options, excluded, suppressSharedTextureEmission, suppressHdrTextureEmission, true);

        static BlinkExportSession Resolve(GameObject source, BlinkExportOptions options,
            Func<Transform, bool> excluded, bool suppressSharedTextureEmission, bool suppressHdrTextureEmission, bool deferAutomaticUsability)
        {
            if (source == null) throw new InvalidOperationException("アバターを指定してください。");
            options = options ?? new BlinkExportOptions();
            var result = new BlinkExportSession();
            // Any exact, unified or one-sided closing name, even an inert one.
            var namedClosure = false;
            if (options.Mode == BlinkExportMode.None)
            {
                result.Disabled = true;
                result.Description = "瞬きなし";
                return result;
            }
            if (options.Mode == BlinkExportMode.Manual)
            {
                var selected = new[] { options.Both, options.Left, options.Right };
                for (var slot = 0; slot < selected.Length; slot++)
                    result.Slots[slot].AddRange(selected[slot].Select(b => b.Copy()));
                result.Description = "手動設定";
            }
            else
            {
                var expressions = ForeignUniVrm.AuthoredSettings(source)?.Expression;
                var clips = new[] { expressions?.Blink, expressions?.BlinkLeft, expressions?.BlinkRight };
                if (clips.Any(c => c != null))
                {
                    result.PreserveAuthored = true;
                    result.Description = "元のVRM設定";
                    for (var slot = 0; slot < clips.Length; slot++)
                    {
                        var clip = clips[slot];
                        result.authored[slot] = clip != null;
                        if (clip == null) continue;
                        foreach (var binding in clip.MorphTargetBindings ?? Array.Empty<MorphTargetBinding>())
                        {
                            var renderer = VrChatExpressionSampler.FindRenderer(source, binding.RelativePath);
                            if (renderer.sharedMesh == null || binding.Index < 0 || binding.Index >= renderer.sharedMesh.blendShapeCount)
                                throw new InvalidOperationException("元のVRMの瞬き設定が存在しない変形を参照しています。");
                            result.Slots[slot].Add(new BlinkShapeBinding { Renderer = renderer,
                                Shape = renderer.sharedMesh.GetBlendShapeName(binding.Index), Weight = binding.Weight * 100f });
                        }
                    }
                }
                else if (TryDescriptor(source, excluded, out var descriptorBinding))
                {
                    result.ConfiguredByDescriptor = true;
                    result.Slots[0].Add(descriptorBinding);
                    result.Description = "VRChatの閉眼設定";
                }
                else
                {
                    result.deferredAutomatic = deferAutomaticUsability;
                    var rendererNames = ExportRendererSelection.Enumerate(source).OfType<SkinnedMeshRenderer>()
                        .Where(renderer => excluded?.Invoke(renderer.transform) != true && renderer.sharedMesh != null)
                        .ToDictionary(renderer => renderer, renderer => Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
                            .Select(renderer.sharedMesh.GetBlendShapeName).ToArray());
                    namedClosure = rendererNames.Values.Any(names =>
                        BlinkShapeNames.AutomaticCandidates(names).Any() || BlinkShapeNames.PartialCandidates(names).Any());
                    result.allowMissingAutomaticBlink = deferAutomaticUsability || source.GetComponentInChildren<VrmTrackingMarker>(true)?.profile == null &&
                        UnifiedExpressionPreparation.HasUsableEvidence(source, excluded, suppressSharedTextureEmission, suppressHdrTextureEmission);
                    var completePairs = true;
                    var partialFamilies = new Dictionary<int, List<BlinkShapeBinding>[]>();
                    var requiredPartialRenderers = new HashSet<SkinnedMeshRenderer>();
                    foreach (var pair in rendererNames)
                    {
                        var renderer = pair.Key;
                        var names = pair.Value;
                        Func<int, bool> usable = deferAutomaticUsability ? null :
                            index => AvatarBaseShape.HasUsableRawEndpoint(renderer.sharedMesh, index, renderer.GetBlendShapeWeight(index));
                        // A complete named pair can span two renderers even on
                        // avatars without Unified Expression tracking evidence.
                        var resolved = BlinkShapeNames.Resolve(names, true,
                            usable);
                        if (resolved[0] == -2)
                        {
                            result.UnresolvedReason = result.UnresolvedReason ?? DuplicateClosingNames;
                            continue;
                        }
                        var completePair = resolved[1] >= 0 && resolved[2] >= 0;
                        if (!result.allowMissingAutomaticBlink && resolved[0] < 0 && !completePair &&
                            (resolved[1] >= 0 || resolved[2] >= 0)) requiredPartialRenderers.Add(renderer);
                        if (resolved[0] >= 0 && !completePair) completePairs = false;
                        for (var slot = 0; slot < resolved.Length; slot++)
                        {
                            if (resolved[slot] < 0) continue;
                            var binding = new BlinkShapeBinding { Renderer = renderer, Shape = names[resolved[slot]] };
                            result.CaptureInferredEndpoint(binding);
                            result.CaptureCandidate(binding);
                            if (slot == 0 || completePair) result.Slots[slot].Add(binding);
                        }
                        if (!completePair)
                            foreach (var candidate in BlinkShapeNames.PartialCandidates(names, usable))
                            {
                                var binding = new BlinkShapeBinding { Renderer = renderer, Shape = names[candidate.Index] };
                                result.CaptureInferredEndpoint(binding);
                                result.CaptureCandidate(binding);
                                var family = BlinkShapeNames.PartialFamily(binding.Shape, candidate.Slot == 1);
                                if (family < 0) continue;
                                if (!partialFamilies.TryGetValue(family, out var sides))
                                    partialFamilies.Add(family, sides = new[] { new List<BlinkShapeBinding>(), new List<BlinkShapeBinding>() });
                                sides[candidate.Slot - 1].Add(binding);
                            }
                    }
                    // Only matching name families can supply the other eye.
                    var claimedPartialRenderers = new HashSet<SkinnedMeshRenderer>();
                    foreach (var family in partialFamilies.OrderBy(pair => pair.Key))
                    {
                        var sides = family.Value.Select(side => side.Where(binding => !claimedPartialRenderers.Contains(binding.Renderer)).ToArray()).ToArray();
                        if ((sides[0].Length > 0 || result.Slots[1].Any(binding => BlinkShapeNames.PartialFamily(binding.Shape, true) == family.Key)) &&
                            (sides[1].Length > 0 || result.Slots[2].Any(binding => BlinkShapeNames.PartialFamily(binding.Shape, false) == family.Key)))
                        {
                            result.Slots[1].AddRange(sides[0]);
                            result.Slots[2].AddRange(sides[1]);
                            claimedPartialRenderers.UnionWith(sides.SelectMany(side => side).Select(binding => binding.Renderer));
                        }
                    }
                    var unmatched = requiredPartialRenderers.FirstOrDefault(renderer => !claimedPartialRenderers.Contains(renderer));
                    if (unmatched != null)
                        result.UnresolvedReason = result.UnresolvedReason ?? UnmatchedClosingSide + unmatched.name;
                    // Resolve bilateral closure across the avatar: UE models
                    // may put their left and right eyes on different renderers.
                    if (result.Slots[1].Count > 0 && result.Slots[2].Count > 0)
                    {
                        var bilateralRenderers = new HashSet<SkinnedMeshRenderer>(result.Slots[0].Select(binding => binding.Renderer));
                        foreach (var binding in result.Slots[1].Concat(result.Slots[2]))
                            if (!bilateralRenderers.Contains(binding.Renderer)) result.Slots[0].Add(binding.Copy());
                    }
                    else { result.Slots[1].Clear(); result.Slots[2].Clear(); }
                    if (!completePairs) { result.Slots[1].Clear(); result.Slots[2].Clear(); }
                    result.Description = "自動設定";
                    // Closing names that never move cannot close the eyes either.
                    if (result.UnresolvedReason == null && namedClosure && !result.allowMissingAutomaticBlink &&
                        result.Slots[0].Count == 0 && result.Slots[1].Count == 0)
                        result.UnresolvedReason = UnusableClosingShapes;
                }
            }
            // No closing name anywhere: export without blink instead of stopping.
            if (options.Mode == BlinkExportMode.Auto && !result.PreserveAuthored && !result.ConfiguredByDescriptor &&
                !result.allowMissingAutomaticBlink && !namedClosure && result.UnresolvedReason == null && result.Slots.All(slot => slot.Count == 0) && result.candidates.Count == 0)
            {
                result.Disabled = true;
                result.MissingAutomatic = true;
                result.Description = MissingAutomaticDescription;
            }
            // Ambiguous, one-sided or inert closing names: the automatic setup
            // does not guess. It exports without blink and says why; a manual
            // choice in the blink settings still exports the blink.
            if (result.UnresolvedReason != null)
            {
                foreach (var slot in result.Slots) slot.Clear();
                result.Disabled = true;
                result.UnresolvedAutomatic = true;
                result.Description = UnresolvedAutomaticDescription;
            }
            result.Validate(source, excluded);
            foreach (var binding in result.Slots.SelectMany(slot => slot)) result.CaptureCandidate(binding);
            return result;
        }

        void CaptureCandidate(BlinkShapeBinding binding)
        {
            if (candidates.Any(candidate => candidate.Binding.Renderer == binding.Renderer && candidate.Binding.Shape == binding.Shape)) return;
            var index = binding.Renderer.sharedMesh.GetBlendShapeIndex(binding.Shape);
            var reference = binding.Renderer.GetBlendShapeWeight(index);
            var endpoint = Endpoint(binding);
            var moving = AvatarBaseShape.HasUsableMorphEndpoint(binding.Renderer.sharedMesh, index, reference, endpoint);
            if (!moving) { reference = 0; moving = AvatarBaseShape.HasUsableMorphEndpoint(binding.Renderer.sharedMesh, index, reference, endpoint); }
            candidates.Add(new Candidate { Binding = binding.Copy(), Moving = moving, ReferenceWeight = reference });
        }

        float Endpoint(BlinkShapeBinding binding) => inferredEndpoints.TryGetValue((binding.Renderer, binding.Shape), out var weight)
            ? weight : binding.Weight;

        // The closing amount this session bakes for one of its bindings, for a
        // manual setting that has to reproduce it.
        internal float ClosingAmount(BlinkShapeBinding binding) => Endpoint(binding);

        void CaptureInferredEndpoint(BlinkShapeBinding binding)
        {
            if (!VRVlog.FaceTracking.UnifiedExpressionRegistry.TryCanonicalize(binding.Shape, out var canonical) ||
                canonical != "EyeClosed" && canonical != "EyeClosedLeft" && canonical != "EyeClosedRight") return;
            var mesh = binding.Renderer.sharedMesh;
            var index = mesh.GetBlendShapeIndex(binding.Shape);
            var frame = mesh.GetBlendShapeFrameCount(index) - 1;
            if (frame < 0) return;
            var endpoint = mesh.GetBlendShapeFrameWeight(index, frame);
            if (float.IsNaN(endpoint) || float.IsInfinity(endpoint) || endpoint <= 0) return;
            // The raw UE target exports its final source frame. Keep that
            // endpoint internally; public/manual closure amounts remain0..100.
            inferredEndpoints[(binding.Renderer, binding.Shape)] = endpoint;
        }

        internal void VerifyPreparedIdentity(GameObject prepared, Func<Transform, bool> excluded = null)
        {
            foreach (var candidate in candidates)
            {
                var binding = candidate.Binding;
                var renderer = binding.Renderer;
                if (renderer == null || !renderer.transform.IsChildOf(prepared.transform) || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy || renderer.sharedMesh == null || excluded?.Invoke(renderer.transform) == true)
                    throw new InvalidOperationException("Modular Avatar / NDMF の処理で瞬きの対象Rendererが失われました。");
                var index = BlinkShapeNames.Unique(Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
                    .Select(renderer.sharedMesh.GetBlendShapeName).ToArray(), binding.Shape, StringComparison.Ordinal);
                if (index < 0 || candidate.Moving && !AvatarBaseShape.HasUsableMorphEndpoint(renderer.sharedMesh, index, candidate.ReferenceWeight, Endpoint(binding)))
                    throw new InvalidOperationException("Modular Avatar / NDMF の処理で瞬きの変形が失われました: " + binding.Shape);
            }
        }

        internal void RebindPrepared(Func<SkinnedMeshRenderer, SkinnedMeshRenderer> replacement)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            var mapped = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer>();
            SkinnedMeshRenderer Map(SkinnedMeshRenderer old)
            {
                if (!mapped.TryGetValue(old, out var current)) mapped.Add(old, current = replacement(old));
                if (current == null) throw new InvalidOperationException(NdmfExportPreparation.UnknownRendererRelocation);
                return current;
            }
            InvalidOperationException Conflict(SkinnedMeshRenderer renderer, string shape) => new InvalidOperationException(
                "Modular Avatar / NDMF の処理で同じ瞬きの変形に異なる閉眼量が統合されました。統合前の瞬き設定を一致させてください。: " + renderer.name + " / " + shape);
            var nextEndpoints = new Dictionary<(SkinnedMeshRenderer Renderer, string Shape), float>();
            foreach (var pair in inferredEndpoints)
            {
                var current = Map(pair.Key.Renderer);
                var key = (current, pair.Key.Shape);
                if (nextEndpoints.TryGetValue(key, out var endpoint) && !endpoint.Equals(pair.Value))
                    throw Conflict(current, pair.Key.Shape);
                nextEndpoints[key] = pair.Value;
            }
            var nextSlots = new List<BlinkShapeBinding>[Slots.Length];
            for (var slot = 0; slot < Slots.Length; slot++)
            {
                nextSlots[slot] = new List<BlinkShapeBinding>();
                var seen = new Dictionary<(SkinnedMeshRenderer Renderer, string Shape), (float Endpoint, float Weight)>();
                foreach (var binding in Slots[slot])
                {
                    var row = binding.Copy(); row.Renderer = Map(binding.Renderer);
                    var key = (row.Renderer, row.Shape);
                    var endpoint = Endpoint(binding);
                    if (seen.TryGetValue(key, out var previous))
                    {
                        if (!previous.Endpoint.Equals(endpoint) || !previous.Weight.Equals(row.Weight))
                            throw Conflict(row.Renderer, row.Shape);
                        continue;
                    }
                    seen.Add(key, (endpoint, row.Weight));
                    nextSlots[slot].Add(row);
                }
            }
            if (nextSlots[1].Any(left => nextSlots[2].Any(right => left.Renderer == right.Renderer &&
                string.Equals(left.Shape, right.Shape, StringComparison.Ordinal))))
                throw new InvalidOperationException("Modular Avatar / NDMF の処理で左右別の瞬きが同じ変形に統合されました。左右を別々に操作できる変形を残してください。");
            // Keep each captured deformation obligation, including different
            // source reference weights, even when output bindings coalesce.
            var nextCandidates = candidates.Select(candidate => {
                var row = candidate.Binding.Copy(); row.Renderer = Map(row.Renderer);
                return new Candidate { Binding = row, Moving = candidate.Moving, ReferenceWeight = candidate.ReferenceWeight };
            }).ToArray();
            // Publish only after every mapping and collision has been checked.
            inferredEndpoints.Clear();
            foreach (var pair in nextEndpoints) inferredEndpoints.Add(pair.Key, pair.Value);
            for (var slot = 0; slot < Slots.Length; slot++)
            {
                Slots[slot].Clear(); Slots[slot].AddRange(nextSlots[slot]);
            }
            candidates.Clear(); candidates.AddRange(nextCandidates);
        }

        internal const string LostBlinkDescription = "瞬きなし（ビルド処理で閉眼用の変形が失われました）";

        // Lenient rebind: follow each blink renderer to the one that holds its
        // shapes after the build passes. If that is not certain, export with no
        // blink instead of stopping the export.
        internal bool FollowPrepared(GameObject prepared, Func<SkinnedMeshRenderer, string, SkinnedMeshRenderer> follow,
            out bool moved, out string reason)
        {
            if (follow == null) throw new ArgumentNullException(nameof(follow));
            reason = null;
            moved = false;
            if (Disabled) return true;
            var shapes = new Dictionary<SkinnedMeshRenderer, HashSet<string>>();
            void Need(SkinnedMeshRenderer renderer, string shape)
            {
                // A replaced renderer is destroyed but must still be followed.
                if (ReferenceEquals(renderer, null)) return;
                if (!shapes.TryGetValue(renderer, out var set)) shapes.Add(renderer, set = new HashSet<string>(StringComparer.Ordinal));
                if (shape != null) set.Add(shape);
            }
            foreach (var binding in Slots.SelectMany(slot => slot)) Need(binding.Renderer, binding.Shape);
            foreach (var candidate in candidates) Need(candidate.Binding.Renderer, candidate.Binding.Shape);
            foreach (var pair in inferredEndpoints) Need(pair.Key.Renderer, pair.Key.Shape);
            try
            {
                var map = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer>();
                foreach (var pair in shapes)
                {
                    var targets = pair.Value.Count == 0 ? new[] { follow(pair.Key, null) }
                        : pair.Value.Select(shape => follow(pair.Key, shape)).Distinct().ToArray();
                    if (targets.Length != 1 || targets[0] == null)
                        throw new InvalidOperationException("Modular Avatar / NDMF の処理で瞬きの対象Rendererが失われました。");
                    map.Add(pair.Key, targets[0]);
                    moved |= targets[0] != follow(pair.Key, null);
                }
                RebindPrepared(renderer => map.TryGetValue(renderer, out var current) ? current : null);
                VerifyPreparedIdentity(prepared);
                return true;
            }
            // Any failure here (a lost shape, a missing manual renderer) exports
            // without blink; only a cancellation still stops the export.
            catch (Exception e) when (!ExportStageGuard.IsCancellation(e))
            {
                reason = e.Message;
                moved = false;
                DisableLost();
                return false;
            }
        }

        void DisableLost()
        {
            foreach (var slot in Slots) slot.Clear();
            candidates.Clear();
            inferredEndpoints.Clear();
            Array.Clear(authored, 0, authored.Length);
            PreserveAuthored = false;
            ConfiguredByDescriptor = false;
            MissingAutomatic = false;
            allowMissingAutomaticBlink = false;
            deferredAutomatic = false;
            Disabled = true;
            Description = LostBlinkDescription;
        }

        internal void ResolvePreparedNeutral(GameObject prepared, Func<Transform, bool> excluded = null,
            bool suppressSharedTextureEmission = false, bool suppressHdrTextureEmission = false)
        {
            if (!deferredAutomatic) { Validate(prepared, excluded); return; }
            using var resolved = Resolve(prepared, new BlinkExportOptions(), excluded,
                suppressSharedTextureEmission, suppressHdrTextureEmission);
            for (var slot = 0; slot < Slots.Length; slot++)
            {
                Slots[slot].Clear(); Slots[slot].AddRange(resolved.Slots[slot].Select(binding => binding.Copy()));
            }
            Description = resolved.Description;
            Disabled = resolved.Disabled;
            MissingAutomatic = resolved.MissingAutomatic;
            UnresolvedAutomatic = resolved.UnresolvedAutomatic;
            UnresolvedReason = resolved.UnresolvedReason;
            PreserveAuthored = resolved.PreserveAuthored;
            Array.Copy(resolved.authored, authored, authored.Length);
            allowMissingAutomaticBlink = resolved.allowMissingAutomaticBlink;
            inferredEndpoints.Clear();
            foreach (var pair in resolved.inferredEndpoints) inferredEndpoints.Add(pair.Key, pair.Value);
            deferredAutomatic = false;
        }

        internal static bool TryDescriptor(GameObject source, Func<Transform, bool> excluded, out BlinkShapeBinding binding)
        {
            binding = null;
            var descriptor = source.GetComponents<Component>().FirstOrDefault(c => c != null &&
                c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (!(VrChatExpressionMenu.Member(descriptor, "enableEyeLook") is bool enabled) || !enabled) return false;
            var settings = VrChatExpressionMenu.Member(descriptor, "customEyeLookSettings");
            // The public SDK enum is Blendshapes. Never use LookingUp/Down slots.
            if (VrChatExpressionMenu.Member(settings, "eyelidType")?.ToString() != "Blendshapes") return false;
            var renderer = VrChatExpressionMenu.Member(settings, "eyelidsSkinnedMesh") as SkinnedMeshRenderer;
            var indices = VrChatExpressionMenu.Member(settings, "eyelidsBlendshapes") as int[];
            if (renderer == null || renderer.sharedMesh == null || indices == null || indices.Length == 0 ||
                !renderer.enabled || !ExportRendererSelection.ActiveUnder(renderer.gameObject, source.transform) ||
                !renderer.transform.IsChildOf(source.transform) || excluded?.Invoke(renderer.transform) == true) return false;
            var index = indices[0];
            if (index < 0 || index >= renderer.sharedMesh.blendShapeCount) return false;
            binding = new BlinkShapeBinding { Renderer = renderer, Shape = renderer.sharedMesh.GetBlendShapeName(index) };
            return true;
        }

        internal void Validate(GameObject root, Func<Transform, bool> excluded = null)
        {
            if (Disabled) return;
            foreach (var slot in Slots)
            {
                var seen = new HashSet<(SkinnedMeshRenderer, string)>();
                foreach (var binding in slot)
                {
                    var renderer = binding.Renderer;
                    if (renderer == null || !renderer.transform.IsChildOf(root.transform) || !renderer.enabled ||
                        !ExportRendererSelection.ActiveUnder(renderer.gameObject, root.transform) || renderer.sharedMesh == null || excluded?.Invoke(renderer.transform) == true)
                        throw new InvalidOperationException("瞬きの対象が書き出しに含まれていません。「確認・調整」で指定し直してください。");
                    var names = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Select(renderer.sharedMesh.GetBlendShapeName).ToArray();
                    // A selected binding identifies the exact mesh channel. The
                    // automatic semantic lookup remains case insensitive and
                    // rejects aliases such as Blink / BLINK before this point.
                    if (string.IsNullOrEmpty(binding.Shape) || BlinkShapeNames.Unique(names, binding.Shape, StringComparison.Ordinal) < 0)
                        throw new InvalidOperationException("瞬きの変形が見つからないか重複しています: " + binding.Shape);
                    if (float.IsNaN(binding.Weight) || float.IsInfinity(binding.Weight) || binding.Weight < 0 || binding.Weight > 100)
                        throw new InvalidOperationException("瞬きの適用量は0〜100%で指定してください。");
                    if (!seen.Add((renderer, binding.Shape)))
                        throw new InvalidOperationException("同じ瞬きの変形を重複して指定できません。");
                }
            }
            if (PreserveAuthored) return; // An intentionally empty clip is authoritative.
            if (Slots[1].Count == 0 != (Slots[2].Count == 0))
                throw new InvalidOperationException("左右別の瞬きは左目・右目を両方指定してください。");
            if (Slots[0].Count == 0 && Slots[1].Count == 0 && !allowMissingAutomaticBlink)
                throw new InvalidOperationException("閉眼用の変形を特定できません。「確認・調整」で選択するか「瞬きなし」を指定してください。");
            if (Slots[1].Any(left => Slots[2].Any(right => left.Renderer == right.Renderer &&
                string.Equals(left.Shape, right.Shape, StringComparison.Ordinal))))
                throw new InvalidOperationException("左右別には異なる閉眼用の変形を指定してください。");
        }

        internal BlinkExportSession ForClone(GameObject source, GameObject clone)
        {
            if (source == clone) throw new ArgumentException("An independent export copy is required.");
            var copy = new BlinkExportSession { Description = Description, PreserveAuthored = PreserveAuthored, Disabled = Disabled, MissingAutomatic = MissingAutomatic,
                UnresolvedAutomatic = UnresolvedAutomatic, UnresolvedReason = UnresolvedReason,
                allowMissingAutomaticBlink = allowMissingAutomaticBlink, deferredAutomatic = deferredAutomatic };
            Array.Copy(authored, copy.authored, authored.Length);
            SkinnedMeshRenderer Target(SkinnedMeshRenderer renderer)
            {
                var route = new Stack<int>();
                for (var t = renderer.transform; t != source.transform; t = t.parent) route.Push(t.GetSiblingIndex());
                var target = clone.transform;
                while (route.Count != 0) target = target.GetChild(route.Pop());
                var index = Array.IndexOf(renderer.GetComponents<SkinnedMeshRenderer>(), renderer);
                return target.GetComponents<SkinnedMeshRenderer>()[index];
            }
            for (var slot = 0; slot < Slots.Length; slot++)
                foreach (var binding in Slots[slot])
                {
                    var row = binding.Copy(); row.Renderer = Target(binding.Renderer);
                    copy.Slots[slot].Add(row);
                }
            foreach (var candidate in candidates)
            {
                var row = candidate.Binding.Copy(); row.Renderer = Target(row.Renderer);
                copy.candidates.Add(new Candidate { Binding = row, Moving = candidate.Moving, ReferenceWeight = candidate.ReferenceWeight });
            }
            foreach (var pair in inferredEndpoints) copy.inferredEndpoints.Add((Target(pair.Key.Renderer), pair.Key.Shape), pair.Value);
            return copy;
        }

        internal void Bake(GameObject clone, ICollection<Mesh> owned)
        {
            if (deferredAutomatic) throw new InvalidOperationException("瞬きの自動設定にはFXのneutral評価が必要です。");
            Validate(clone);
            var used = DeterministicNames.BlendShapeNames(clone);
            string PathOf(SkinnedMeshRenderer skin) => UnityEditor.AnimationUtility.CalculateTransformPath(skin.transform, clone.transform);
            if (Disabled)
            {
                // A no-op standard binding prevents a viewer's raw-name blink
                // fallback from driving the original morphs when blinking is off.
                var renderer = ExportRendererSelection.Enumerate(clone).OfType<SkinnedMeshRenderer>()
                    .FirstOrDefault(r => ExportRendererSelection.HasGeometry(r.sharedMesh));
                if (renderer == null) return;
                var mesh = Object.Instantiate(renderer.sharedMesh); owned.Add(mesh); renderer.sharedMesh = mesh;
                var name = DeterministicNames.Unique("__VRVlog_BlinkNone_" + DeterministicNames.Hash("BlinkNone", PathOf(renderer)), used);
                var zeros = new Vector3[mesh.vertexCount]; mesh.AddBlendShapeFrame(name, 100f, zeros, zeros, zeros);
                foreach (var slot in Slots) slot.Add(new BlinkShapeBinding { Renderer = renderer, Shape = name });
                return;
            }
            foreach (var group in Slots.SelectMany((s, slot) => s.Select(binding => (binding, slot))).GroupBy(item => item.binding.Renderer))
            {
                var renderer = group.Key;
                var original = renderer.sharedMesh;
                var generated = Object.Instantiate(original); owned.Add(generated);
                foreach (var (binding, slot) in group)
                {
                    var index = BlinkShapeNames.Unique(Enumerable.Range(0, original.blendShapeCount)
                        .Select(original.GetBlendShapeName).ToArray(), binding.Shape, StringComparison.Ordinal);
                    // A shape bound to both blink and one side gets one channel
                    // per slot, so the slot is part of the name.
                    var name = DeterministicNames.Unique("__VRVlog_Blink_" + DeterministicNames.Hash("Blink",
                        PathOf(renderer), binding.Shape, BlinkShapeNames.Presets[slot]), used);
                    AvatarBaseShape.AppendAnimatedShape(original, generated, name, index,
                        renderer.GetBlendShapeWeight(index), Endpoint(binding));
                    binding.Shape = name;
                    binding.Weight = 100f;
                }
                renderer.sharedMesh = generated;
            }
            if (PreserveAuthored)
            {
                // UniVRM reads the prepared hierarchy too. Give it private clips
                // with current paths/indices; never mutate the source VRM assets.
                var instance = clone.GetComponent<Vrm10Instance>();
                instance.Vrm = Object.Instantiate(instance.Vrm); expressionCopies.Add(instance.Vrm);
                var expression = instance.Vrm.Expression;
                var clips = new[] { expression.Blink, expression.BlinkLeft, expression.BlinkRight };
                for (var slot = 0; slot < clips.Length; slot++)
                {
                    if (clips[slot] == null) continue;
                    clips[slot] = Object.Instantiate(clips[slot]); expressionCopies.Add(clips[slot]);
                    clips[slot].MorphTargetBindings = Slots[slot].Select(b => new MorphTargetBinding(
                        UnityEditor.AnimationUtility.CalculateTransformPath(b.Renderer.transform, clone.transform),
                        b.Renderer.sharedMesh.GetBlendShapeIndex(b.Shape), b.Weight / 100f)).ToArray();
                }
                expression.Blink = clips[0]; expression.BlinkLeft = clips[1]; expression.BlinkRight = clips[2];
            }
        }

        public void Dispose()
        {
            foreach (var asset in expressionCopies) if (asset != null) Object.DestroyImmediate(asset);
            expressionCopies.Clear();
        }

        internal void Remap(ExportOptimizationBindings mappings)
        {
            foreach (var binding in Slots.SelectMany(slot => slot))
            {
                var mapped = mappings.MapMorph(binding.Renderer, binding.Shape);
                binding.Renderer = mapped.Renderer;
                binding.Shape = mapped.Shape;
            }
        }

        internal void Bind(ModelExporter converter, VRVlog.LilToonExporter.Vendor.VrmLib.Model model, ExportingGltfData storage)
        {
            foreach (var renderer in Slots.SelectMany(s => s).Select(b => b.Renderer).Distinct())
            {
                if (!converter.Nodes.TryGetValue(renderer.gameObject, out var node))
                    throw new InvalidOperationException("瞬きの対象が最終VRMにありません。");
                var index = model.Nodes.IndexOf(node);
                if (index < 0) throw new InvalidOperationException("瞬きの出力nodeを解決できません。");
                nodes.Add(renderer, index);
            }
        }

        internal byte[] Apply(byte[] bytes)
        {
            var glb = GlbDocument.Read(bytes);
            var vrm = (Dictionary<string, object>)((Dictionary<string, object>)glb.Json["extensions"])["VRMC_vrm"];
            var expressions = ObjectMap(vrm, "expressions");
            var presets = ObjectMap(expressions, "preset");
            if (PreserveAuthored)
            {
                for (var slot = 0; slot < authored.Length; slot++)
                    if (authored[slot] && !presets.ContainsKey(BlinkShapeNames.Presets[slot]))
                        throw new InvalidOperationException("元のVRMの瞬き設定が出力時に失われました。");
            }
            else foreach (var preset in BlinkShapeNames.Presets) presets.Remove(preset);
            var outputNodes = (List<object>)glb.Json["nodes"];
            var meshes = (List<object>)glb.Json["meshes"];
            for (var slot = 0; slot < Slots.Length; slot++)
            {
                if (PreserveAuthored && (!authored[slot] || Slots[slot].Count == 0)) continue;
                if (Slots[slot].Count == 0 && !Disabled) continue;
                var binds = new List<object>();
                foreach (var binding in Slots[slot])
                {
                    var node = nodes[binding.Renderer];
                    var meshIndex = Convert.ToInt32(((Dictionary<string, object>)outputNodes[node])["mesh"]);
                    var mesh = (Dictionary<string, object>)meshes[meshIndex];
                    var names = ((List<object>)((Dictionary<string, object>)mesh["extras"])["targetNames"]).Cast<string>().ToArray();
                    var target = BlinkShapeNames.Unique(names, binding.Shape);
                    if (target < 0) throw new InvalidOperationException("最終VRMで瞬きの変形を特定できません。");
                    foreach (Dictionary<string, object> primitive in (List<object>)mesh["primitives"])
                        if (!(primitive.TryGetValue("targets", out var rawTargets) && rawTargets is List<object> targets) || target >= targets.Count)
                            throw new InvalidOperationException("瞬きのmorph target番号が不正です。");
                    binds.Add(new Dictionary<string, object> { { "node", (long)node }, { "index", (long)target },
                        { "weight", PreserveAuthored ? (double)binding.Weight / 100.0 : 1.0 } });
                }
                if (PreserveAuthored)
                    ((Dictionary<string, object>)presets[BlinkShapeNames.Presets[slot]])["morphTargetBinds"] = binds;
                else presets[BlinkShapeNames.Presets[slot]] = new Dictionary<string, object> { { "morphTargetBinds", binds }, { "isBinary", false } };
            }
            // Perfect Sync custom keys must agree with an explicit override too.
            if (!PreserveAuthored && expressions.TryGetValue("custom", out var rawCustom) && rawCustom is Dictionary<string, object> custom)
                foreach (var key in custom.Keys.ToArray())
                    if (string.Equals(key, "EyeBlinkLeft", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "EyeBlinkRight", StringComparison.OrdinalIgnoreCase))
                    {
                        var slot = key.EndsWith("Left", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                        if (presets.TryGetValue(BlinkShapeNames.Presets[slot], out var value) || presets.TryGetValue("blink", out value))
                            custom[key] = value;
                    }
            return glb.Write();
        }

        static Dictionary<string, object> ObjectMap(Dictionary<string, object> parent, string key)
        {
            if (!parent.TryGetValue(key, out var value)) parent[key] = value = new Dictionary<string, object>();
            return (Dictionary<string, object>)value;
        }
    }
}
