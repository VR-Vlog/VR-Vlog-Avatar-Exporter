using System;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    internal static class VrmTrackingExpressions
    {
        internal static readonly string[] Names = TrackingExpressionNames.PerfectSync;

        internal static void Validate(VrmTrackingProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var entries = profile.expressions ?? Array.Empty<TrackingExpression>();
            if (entries.Length != Names.Length || entries.Any(e => e == null) ||
                entries.Select(e => e.name).Distinct(StringComparer.Ordinal).Count() != Names.Length ||
                Names.Any(n => entries.All(e => e.name != n)))
                throw new InvalidOperationException("追跡設定にはARKit標準52項目を一度ずつ設定してください。");
            foreach (var entry in entries)
            {
                if (entry.morphs == null || entry.morphs.Length == 0)
                    throw new InvalidOperationException("追跡表情にシェイプキーがありません: " + entry.name);
                foreach (var morph in entry.morphs) ValidateMorph(morph, entry.name);
            }
        }

        internal static void ValidateMorph(TrackingMorph morph, string expression)
        {
            if (morph == null || string.IsNullOrWhiteSpace(morph.shape) ||
                float.IsNaN(morph.weight) || float.IsInfinity(morph.weight) || morph.weight <= 0f || morph.weight > 1f)
                throw new InvalidOperationException("追跡表情の設定が不正です: " + expression);
        }
    }
}
