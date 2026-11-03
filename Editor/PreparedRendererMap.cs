using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Follows each captured renderer to the one a build pass left, asking the
    // pass once per renderer. Paths differ in which entries may be dropped:
    // Find leaves a lost renderer to the caller and Require refuses it.
    internal sealed class PreparedRendererMap
    {
        readonly Func<SkinnedMeshRenderer, SkinnedMeshRenderer> replacement;
        readonly Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer> mapped = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer>();

        internal PreparedRendererMap(Func<SkinnedMeshRenderer, SkinnedMeshRenderer> replacement) =>
            this.replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));

        internal SkinnedMeshRenderer Find(SkinnedMeshRenderer old)
        {
            if (!mapped.TryGetValue(old, out var current)) mapped.Add(old, current = replacement(old));
            return current;
        }

        internal SkinnedMeshRenderer Require(SkinnedMeshRenderer old)
        {
            var current = Find(old);
            if (current == null) throw new InvalidOperationException(NdmfExportPreparation.UnknownRendererRelocation);
            return current;
        }

        // Callers build every next value first and publish only once each
        // original entry has a compatible destination.
        internal static void Publish<T>(ICollection<T> target, IEnumerable<T> values)
        {
            target.Clear();
            foreach (var value in values) target.Add(value);
        }
    }
}
