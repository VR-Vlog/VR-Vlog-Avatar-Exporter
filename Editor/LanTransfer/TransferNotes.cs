using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // One kind of note: "<kind>: <target> — <reason>" lines of the same kind.
    internal sealed class TransferNoteGroup
    {
        // Null for the lines that have no kind; they are listed as they are.
        internal string Kind;
        // Shown once above the targets when every target has it; the targets then have none.
        internal string CommonReason;
        internal readonly List<(string Target, string Reason)> Items = new List<(string, string)>();
        internal int Count => Items.Count;
    }

    // The notes of the last export, grouped for the board's page after a transfer. Notes of one kind
    // become one folded row with the count, and a reason shared by every target is shown once.
    // The same line twice counts once.
    internal sealed class TransferNotes
    {
        // Kinds that record what the export did as asked, not what it could not do. They are
        // listed after the notes and not counted as notes.
        static readonly HashSet<string> RecordKinds = new HashSet<string>(StringComparer.Ordinal)
            { "除外", "自動除外せず保持", "追従を指定", "変換用コピーに対策を適用" };

        // Kinds with the most targets first.
        internal readonly List<TransferNoteGroup> Notes = new List<TransferNoteGroup>();
        // The record kinds, then the lines without a kind.
        internal readonly List<TransferNoteGroup> Records = new List<TransferNoteGroup>();
        internal int NoteCount => Notes.Sum(group => group.Count);

        internal static TransferNotes Build(IEnumerable<string> lines)
        {
            var notes = new TransferNotes();
            var groups = new List<TransferNoteGroup>();
            var plain = new TransferNoteGroup();
            foreach (var line in (lines ?? Array.Empty<string>()).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).Distinct(StringComparer.Ordinal))
            {
                var colon = line.IndexOf(": ", StringComparison.Ordinal);
                var kind = colon > 0 ? line.Substring(0, colon) : null;
                var rest = colon > 0 ? line.Substring(colon + 2) : null;
                var dash = rest?.IndexOf(" — ", StringComparison.Ordinal) ?? -1;
                // The kind is a short label before ": " of a "<kind>: <target> — <reason>" line, or one of
                // the record kinds. Other lines, such as "<material>: <sentence>", are lines of their own.
                if (kind == null || (!RecordKinds.Contains(kind) && (dash < 0 || kind.Length > 24 || kind.IndexOf('。') >= 0)))
                {
                    plain.Items.Add((line, null));
                    continue;
                }
                var target = dash >= 0 ? rest.Substring(0, dash) : rest;
                var reason = dash >= 0 ? rest.Substring(dash + 3) : null;
                var group = groups.FirstOrDefault(existing => existing.Kind == kind);
                if (group == null) groups.Add(group = new TransferNoteGroup { Kind = kind });
                group.Items.Add((target, reason));
            }
            foreach (var group in groups)
            {
                var reasons = group.Items.Select(item => item.Reason).Distinct().ToArray();
                if (group.Items.Count > 1 && reasons.Length == 1 && reasons[0] != null)
                {
                    group.CommonReason = reasons[0];
                    for (var i = 0; i < group.Items.Count; i++) group.Items[i] = (group.Items[i].Target, null);
                }
            }
            // OrderByDescending is stable: kinds of the same size keep the order of the export.
            notes.Notes.AddRange(groups.Where(group => !RecordKinds.Contains(group.Kind)).OrderByDescending(group => group.Count));
            notes.Records.AddRange(groups.Where(group => RecordKinds.Contains(group.Kind)));
            if (plain.Items.Count > 0) notes.Records.Add(plain);
            return notes;
        }
    }
}
