using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.Poses;

namespace VRVlog.LilToonExporter
{
    // Which bundled poses an export keeps when they exceed the app's limits.
    // The sizes follow the contract's conservative JSON bound (Measure in
    // HumanoidPoseData / HumanoidAnimationData), so a plan within its limits
    // always writes. The default drops rows from the end of the list.
    internal sealed class PoseLimitPlan
    {
        internal sealed class Row
        {
            internal string Id, Name, Category, Source;
            internal bool Animated;
            internal int Frames;
            internal double Duration;
            // Contract size of this row's entry; the envelope is counted once.
            internal long Size;
            // A moving pose over 8,192 frames. Shown, never written.
            internal bool OverFrameLimit;
            // A moving pose whose data alone exceeds 16MB. Shown, never written.
            internal bool TooLarge;
            internal bool Keep = true;
            internal bool Selectable => !OverFrameLimit && !TooLarge;
        }

        internal readonly List<Row> Rows = new List<Row>();

        internal PoseLimitPlan(IEnumerable<PoseCandidate> candidates)
        {
            foreach (var candidate in candidates)
            {
                var row = new Row { Id = candidate.Id, Name = candidate.Name, Category = candidate.Category ?? "", Source = candidate.Source ?? "",
                    Animated = candidate.IsAnimation, OverFrameLimit = candidate.OverFrameLimit };
                if (candidate.Animation != null)
                {
                    row.Animated = true; row.Frames = candidate.Animation.Frames.Count; row.Duration = candidate.Animation.Duration;
                    row.Size = AnimationSize(candidate.Animation);
                    row.TooLarge = AnimationEnvelope + row.Size > HumanoidAnimationData.MaximumBytes;
                }
                else if (candidate.Data != null) row.Size = PoseSize(candidate.Data);
                else if (!candidate.OverFrameLimit) continue;
                row.Keep = row.Selectable;
                Rows.Add(row);
            }
            Exceeds = !Within;
            NeedsChoice = Exceeds || Rows.Any(row => row.TooLarge);
        }

        // Over a limit with every row that can be written kept. Only then are
        // rows dropped, by the saved choice or the default.
        internal readonly bool Exceeds;

        // Over a limit, or a moving pose too large to keep. Within the limits
        // nothing is dropped whatever choice was saved, also when the window
        // opens only to show a pose too large to keep.
        internal readonly bool NeedsChoice;

        internal IEnumerable<Row> Kept => Rows.Where(row => row.Keep);
        internal int KeptCount => Kept.Count();
        internal int SelectableCount => Rows.Count(row => row.Selectable);
        internal int AnimationFrames => Kept.Where(row => row.Animated).Sum(row => row.Frames);
        internal long AnimationBytes => Kept.Any(row => row.Animated) ? AnimationEnvelope + Kept.Where(row => row.Animated).Sum(row => row.Size) : 0;
        internal long PoseBytes => Kept.Any(row => !row.Animated) ? PoseEnvelope + Kept.Where(row => !row.Animated).Sum(row => row.Size) : 0;

        internal bool CountOver => KeptCount > HumanoidPoseData.MaximumPoses;
        internal bool FramesOver => AnimationFrames > HumanoidAnimationData.MaximumTotalFrames;
        internal bool AnimationBytesOver => AnimationBytes > HumanoidAnimationData.MaximumBytes;
        internal bool PoseBytesOver => PoseBytes > HumanoidPoseData.MaximumBytes;
        internal bool Within => !CountOver && !FramesOver && !AnimationBytesOver && !PoseBytesOver;

        internal HashSet<string> Dropped => new HashSet<string>(Rows.Where(row => row.Selectable && !row.Keep).Select(row => row.Id), StringComparer.Ordinal);
        // Rows the app's limits leave out of the file: the dropped ones and
        // the moving poses too large to keep.
        internal IEnumerable<Row> LeftOut => Rows.Where(row => !row.Keep && !row.OverFrameLimit);

        // Starts from a saved choice (ids no longer listed are ignored) and
        // drops more from the end until the poses fit. Without a choice this
        // is the default. When everything fits the choice is not applied.
        internal void Choose(IEnumerable<string> dropped)
        {
            var set = new HashSet<string>(dropped ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            foreach (var row in Rows) row.Keep = row.Selectable && (!Exceeds || !set.Contains(row.Id));
            if (!Exceeds) return;
            DropFromEnd(row => row.Animated, () => FramesOver || AnimationBytesOver);
            DropFromEnd(row => !row.Animated, () => PoseBytesOver);
            DropFromEnd(row => true, () => CountOver);
        }

        internal void Default() => Choose(null);

        // True when the kept rows are exactly what the default keeps, so
        // nothing has to be remembered for this avatar.
        internal bool IsDefault
        {
            get
            {
                var keep = Rows.Select(row => row.Keep).ToArray();
                Default();
                var same = Rows.Select(row => row.Keep).SequenceEqual(keep);
                for (var i = 0; i < Rows.Count; i++) Rows[i].Keep = keep[i];
                return same;
            }
        }

        void DropFromEnd(Func<Row, bool> kind, Func<bool> over)
        {
            for (var i = Rows.Count - 1; i >= 0 && over(); i--)
                if (Rows[i].Keep && kind(Rows[i])) Rows[i].Keep = false;
        }

        // The JSON entry each row adds, mirroring the contract's Write and Measure.
        static readonly long PoseEnvelope = Size(new Dictionary<string, object> {
            ["schemaVersion"] = 1L, ["space"] = "vrm1AvatarRestDelta", ["poses"] = new List<object>() });
        static readonly long AnimationEnvelope = Size(new Dictionary<string, object> {
            ["schemaVersion"] = 1L, ["space"] = "vrm1AvatarRestDelta", ["animations"] = new List<object>() });

        internal static long PoseSize(HumanoidPoseData pose) => Size(new Dictionary<string, object> {
            ["id"] = pose.Id, ["name"] = pose.Name, ["category"] = pose.Category, ["source"] = pose.Source,
            ["sampleTime"] = pose.SampleTime, ["sampledMotion"] = pose.SampledMotion,
            ["hipsOffset"] = pose.HipsOffset.Cast<object>().ToList(),
            ["bones"] = pose.Bones.Select(bone => (object)new Dictionary<string, object> {
                ["bone"] = bone.Name, ["node"] = 0L, ["rotation"] = bone.Rotation.Cast<object>().ToList()
            }).ToList()
        });

        internal static long AnimationSize(HumanoidAnimationData animation)
        {
            var size = Size(new Dictionary<string, object> {
                ["id"] = animation.Id, ["name"] = animation.Name, ["category"] = animation.Category, ["source"] = animation.Source,
                ["sourceStartTime"] = animation.SourceStartTime, ["duration"] = animation.Duration, ["loop"] = animation.Loop,
                ["bones"] = animation.Bones.Select(bone => (object)new Dictionary<string, object> { ["bone"] = bone.Name, ["node"] = 0L }).ToList(),
                ["frames"] = new List<object>()
            });
            // A frame is {time, hipsOffset:[3], rotations:[[4]...]}; every
            // scalar and container costs one unit, keys their escaped text.
            foreach (var frame in animation.Frames)
                size += Unit + Size("time") + Unit + Size("hipsOffset") + Unit + frame.HipsOffset.Length * Unit +
                    Size("rotations") + Unit + frame.Rotations.Sum(rotation => Unit + rotation.Length * Unit);
            return size;
        }

        const long Unit = 32;

        // Same bound as the contract's Measure: 32 per value plus 6 per character.
        internal static long Size(object value)
        {
            var size = Unit;
            if (value is string text) size += text.Length * 6L;
            else if (value is Dictionary<string, object> obj) foreach (var pair in obj) size += Size(pair.Key) + Size(pair.Value);
            else if (value is List<object> list) foreach (var item in list) size += Size(item);
            return size;
        }
    }
}
