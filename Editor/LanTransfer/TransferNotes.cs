using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

    // One texture the export made smaller. The size before is 0 when the note does not say it.
    internal sealed class TransferTextureNote
    {
        internal string Name;
        internal int FromWidth, FromHeight, Width, Height;
    }

    // One sentence of the PhysBone notes and the PhysBones that have it.
    internal sealed class TransferSpringNote
    {
        internal string Sentence;
        internal readonly List<string> Bones = new List<string>();
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
        // The name-free diagnostics text of the same export, made when copied. Null without one.
        internal Func<string> Diagnostics;
        // The record kinds, then the lines without a kind.
        internal readonly List<TransferNoteGroup> Records = new List<TransferNoteGroup>();
        internal int NoteCount => Notes.Sum(group => group.Count);

        // What the conversion changed, read back from the sentences ExportNoteText writes. Null or
        // empty when the export did not write them; their lines are then not in Records.
        internal string LilToonInstalled, LilToonDrawn;
        // The expressions added from VRChat; 0 when none could be, null without the note.
        internal int? Expressions;
        internal bool FxRestSkipped;
        internal readonly List<TransferTextureNote> Textures = new List<TransferTextureNote>();
        // Every PhysBone with a note, in the order of the export.
        internal readonly List<string> SpringBones = new List<string>();
        // In the order of ExportNoteText.SpringSentences; only the sentences some PhysBone has.
        internal readonly List<TransferSpringNote> Springs = new List<TransferSpringNote>();

        internal bool HasConversion => LilToonDrawn != null || Expressions != null || FxRestSkipped || Textures.Count > 0 || SpringBones.Count > 0;

        internal static TransferNotes Build(IEnumerable<string> lines)
        {
            var notes = new TransferNotes();
            var groups = new List<TransferNoteGroup>();
            var plain = new TransferNoteGroup();
            foreach (var line in (lines ?? Array.Empty<string>()).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).Distinct(StringComparer.Ordinal))
            {
                if (notes.ReadConversion(line)) continue;
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
            notes.Springs.RemoveAll(spring => spring.Bones.Count == 0);
            return notes;
        }

        // Some notes are written in the language of the export, so a template matches in Japanese or translated.
        static readonly Dictionary<string, Regex> Patterns = new Dictionary<string, Regex>();

        static Match Read(string line, string template)
        {
#if VRVLOG_LAN_TRANSFER_CLI
            foreach (var text in new[] { template })
#else
            foreach (var text in new[] { template, ExporterLocalization.T(template) })
#endif
            {
                if (!Patterns.TryGetValue(text, out var pattern))
                    Patterns[text] = pattern = new Regex("^" + Regex.Replace(Regex.Escape(text), @"\\{(\d)}", "(?<a$1>.+?)") + "$");
                var match = pattern.Match(line);
                if (match.Success) return match;
            }
            return null;
        }

        static int Number(Match match, int index) => int.TryParse(match.Groups["a" + index].Value, out var value) ? value : 0;

        bool ReadConversion(string line)
        {
            Match match;
            if ((match = Read(line, ExportNoteText.LilToonRendered)) != null)
            {
                LilToonInstalled = match.Groups["a0"].Value;
                LilToonDrawn = match.Groups["a1"].Value;
                return true;
            }
            if ((match = Read(line, ExportNoteText.ExpressionsAdded)) != null) { Expressions = Number(match, 0); return true; }
            if (Read(line, ExportNoteText.ExpressionsNone) != null) { Expressions = 0; return true; }
            if (Read(line, ExportNoteText.FxRestSkipped) != null) { FxRestSkipped = true; return true; }
            foreach (var template in new[] { ExportNoteText.LilToonTextureResized, ExportNoteText.OutputTextureResized })
            {
                if ((match = Read(line, template)) == null) continue;
                Textures.Add(new TransferTextureNote
                {
                    Name = match.Groups["a0"].Value, FromWidth = Number(match, 1), FromHeight = Number(match, 2), Width = Number(match, 3), Height = Number(match, 4),
                });
                return true;
            }
            foreach (var template in new[] { ExportNoteText.MainTextureResized, ExportNoteText.AlphaMaskResized })
            {
                if ((match = Read(line, template)) == null) continue;
                Textures.Add(new TransferTextureNote { Name = match.Groups["a0"].Value, Width = Number(match, 1), Height = Number(match, 2) });
                return true;
            }
            foreach (var sentence in ExportNoteText.SpringSentences)
            {
                var suffix = ": " + sentence;
                if (!line.EndsWith(suffix, StringComparison.Ordinal) || line.Length == suffix.Length) continue;
                var bone = line.Substring(0, line.Length - suffix.Length);
                if (!SpringBones.Contains(bone)) SpringBones.Add(bone);
                if (Springs.Count == 0) Springs.AddRange(ExportNoteText.SpringSentences.Select(text => new TransferSpringNote { Sentence = text }));
                var spring = Springs.First(note => note.Sentence == sentence);
                if (!spring.Bones.Contains(bone)) spring.Bones.Add(bone);
                return true;
            }
            return false;
        }
    }
}
