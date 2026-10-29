using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEngine;
using PresetAssignment = VRVlog.LilToonExporter.ExperimentalExpressionCaptureSession.PresetAssignment;

namespace VRVlog.LilToonExporter
{
    // The expression board of the VRM export window. Every face that was
    // found or added sits in exactly one place: one of the assignable VRM
    // preset slots (emotions, blink, mouth, gaze and neutral), the custom
    // list (exported as VRM custom expressions) or the excluded list. Slots are the session's preset assignments; this class
    // only decides where a face goes and what the export receives.
    internal sealed class ExpressionBoard
    {
        internal enum PlaceKind { Other, Excluded, Slot }

        internal readonly struct Place : IEquatable<Place>
        {
            internal readonly PlaceKind Kind;
            internal readonly ExpressionPreset Preset;
            Place(PlaceKind kind, ExpressionPreset preset) { Kind = kind; Preset = preset; }
            internal static Place Other => new Place(PlaceKind.Other, ExpressionPreset.custom);
            internal static Place Excluded => new Place(PlaceKind.Excluded, ExpressionPreset.custom);
            internal static Place Slot(ExpressionPreset preset) => new Place(PlaceKind.Slot, preset);
            public bool Equals(Place other) => Kind == other.Kind && (Kind != PlaceKind.Slot || Preset == other.Preset);
            public override bool Equals(object value) => value is Place other && Equals(other);
            public override int GetHashCode() => Kind == PlaceKind.Slot ? (int)Kind * 397 ^ (int)Preset : (int)Kind;
            public override string ToString() => Kind == PlaceKind.Slot ? "Slot(" + Preset + ")" : Kind.ToString();
        }

        internal sealed class Item
        {
            internal AnimationClip Clip;
            internal string Error;
            internal bool Added;
            internal string Source;
        }

        readonly IDictionary<ExpressionPreset, PresetAssignment> presets;
        readonly Action<ExpressionPreset, PresetAssignment> setPreset;
        readonly Func<IEnumerable<AnimationClip>, IDictionary<ExpressionPreset, PresetAssignment>> suggest;
        readonly Func<ExpressionPreset, bool> authored;
        internal readonly List<Item> Items = new List<Item>();
        internal readonly HashSet<AnimationClip> Excluded = new HashSet<AnimationClip>();
        // Names the user gave faces; a face without one is named after its file.
        internal readonly Dictionary<AnimationClip, string> Names = new Dictionary<AnimationClip, string>();

        internal ExpressionBoard(IDictionary<ExpressionPreset, PresetAssignment> presets, Action<ExpressionPreset, PresetAssignment> setPreset,
            Func<IEnumerable<AnimationClip>, IDictionary<ExpressionPreset, PresetAssignment>> suggest, Func<ExpressionPreset, bool> authored)
        {
            this.presets = presets ?? throw new ArgumentNullException(nameof(presets));
            this.setPreset = setPreset ?? throw new ArgumentNullException(nameof(setPreset));
            this.suggest = suggest ?? throw new ArgumentNullException(nameof(suggest));
            this.authored = authored ?? (_ => false);
        }

        static IReadOnlyList<ExpressionPreset> Slots => ExperimentalExpressionCaptureSession.AssignablePresets;

        // A group of slots (for example the emotions), or every slot when null.
        static IEnumerable<ExpressionPreset> SlotsIn(IEnumerable<ExpressionPreset> group) =>
            group == null ? Slots : Slots.Where(group.Contains);

        // Files the user added come first, then the avatar's registered faces.
        internal void SetItems(IEnumerable<Item> added, IEnumerable<Item> found)
        {
            Items.Clear();
            var seen = new HashSet<AnimationClip>();
            foreach (var item in (added ?? Array.Empty<Item>()).Select(item => { item.Added = true; return item; })
                .Concat((found ?? Array.Empty<Item>()).Select(item => { item.Added = false; return item; })))
                if (item?.Clip != null && seen.Add(item.Clip)) Items.Add(item);
        }

        internal Item Find(AnimationClip clip) => clip == null ? null : Items.FirstOrDefault(item => item.Clip == clip);
        internal static bool Usable(Item item) => item?.Clip != null && item.Error == null;

        // A file the user added stays where it was put once it stops working,
        // with its reason, until it is repaired or removed. The avatar's own
        // unsupported faces are not shown.
        internal static bool Shown(Item item) => Usable(item) || item?.Clip != null && item.Added;

        static AnimationClip ClipOf(PresetAssignment assignment) =>
            assignment == null || assignment.IsNone || assignment.IsShape ? null : assignment.ResolveClip();

        internal PresetAssignment SlotValue(ExpressionPreset preset) => presets.TryGetValue(preset, out var value) ? value : null;

        internal Place PlaceOf(AnimationClip clip)
        {
            if (clip != null)
                foreach (var preset in Slots)
                    if (ClipOf(SlotValue(preset)) == clip) return Place.Slot(preset);
            return clip != null && Excluded.Contains(clip) ? Place.Excluded : Place.Other;
        }

        internal bool CanMove(AnimationClip clip, Place target)
        {
            var item = Find(clip);
            if (item == null || PlaceOf(clip).Equals(target)) return false;
            return target.Kind != PlaceKind.Slot || Usable(item) && Slots.Contains(target.Preset);
        }

        // A face leaving a slot leaves it explicitly empty, so the name based
        // suggestion does not put it straight back. A gaze or neutral slot is
        // left open instead: the export fills it from names, and only "none"
        // the user picks keeps it empty. A face entering an occupied slot sends
        // the previous face back to the custom list.
        internal bool Move(AnimationClip clip, Place target)
        {
            if (!CanMove(clip, target)) return false;
            foreach (var preset in Slots)
                if (ClipOf(SlotValue(preset)) == clip) setPreset(preset, Emptied(preset));
            switch (target.Kind)
            {
                case PlaceKind.Slot:
                    Excluded.Remove(clip);
                    setPreset(target.Preset, PresetAssignment.ForClip(target.Preset, clip));
                    break;
                case PlaceKind.Excluded: Excluded.Add(clip); break;
                default: Excluded.Remove(clip); break;
            }
            return true;
        }

        internal void ClearSlot(ExpressionPreset preset) => setPreset(preset, Emptied(preset));

        static PresetAssignment Emptied(ExpressionPreset preset) =>
            ExperimentalExpressionCaptureSession.FilledFromNames(preset) ? null : PresetAssignment.None(preset);

        internal int AutomaticCount(IEnumerable<ExpressionPreset> group = null) =>
            SlotsIn(group).Count(preset => SlotValue(preset) is PresetAssignment value && value.Automatic && !value.IsNone);

        // Automatic values become the user's own choices.
        internal int ConfirmAutomatic(IEnumerable<ExpressionPreset> group = null)
        {
            var count = 0;
            foreach (var preset in SlotsIn(group).ToArray())
            {
                var value = SlotValue(preset);
                if (value == null || !value.Automatic || value.IsNone) continue;
                var copy = value.Copy(); copy.Automatic = false;
                setPreset(preset, copy); count++;
            }
            return count;
        }

        internal void Reset()
        {
            foreach (var preset in Slots) setPreset(preset, null);
            Excluded.Clear();
            ApplyAutomatic();
        }

        // Excluded faces and faces the user placed by hand are not suggested again.
        internal IEnumerable<AnimationClip> SuggestionCandidates()
        {
            var manual = new HashSet<AnimationClip>(Slots.Select(SlotValue).Where(value => value != null && !value.Automatic)
                .Select(ClipOf).Where(clip => clip != null));
            return Items.Where(item => Usable(item) && !Excluded.Contains(item.Clip) && !manual.Contains(item.Clip)).Select(item => item.Clip).ToArray();
        }

        // Suggestions follow the current faces and are recomputed when they
        // change. The user's own choices, including "none", are never replaced.
        internal void ApplyAutomatic()
        {
            var open = Slots.Where(preset => SlotValue(preset) is var current && (current == null || current.Automatic)).ToArray();
            if (open.Length == 0) return;
            var suggested = suggest(SuggestionCandidates()) ?? new Dictionary<ExpressionPreset, PresetAssignment>();
            foreach (var preset in open)
                setPreset(preset, suggested.TryGetValue(preset, out var value) ? value : null);
        }

        // Usable faces of the custom list, in board order. Unsupported faces
        // are not exported.
        internal List<AnimationClip> ExportedCustom() =>
            Items.Where(item => Usable(item) && PlaceOf(item.Clip).Kind == PlaceKind.Other).Select(item => item.Clip).ToList();

        // The faces ExportedCustom leaves out because they cannot be used. The
        // avatar's own are not shown, so the export names them instead.
        internal List<Item> UnusableCustom() =>
            Items.Where(item => item?.Clip != null && !Usable(item) && PlaceOf(item.Clip).Kind == PlaceKind.Other).ToList();

        internal const string UnusableCustomFormat = "使えないため書き出さなかった表情（{0}件）: {1} — {2}";
        const int UnusableCustomNamed = 5;

        // One note for the result list, null when every face can be used: the
        // first names, and the first reason (most share one, a hidden mesh).
        internal string UnusableCustomNote()
        {
            var unusable = UnusableCustom();
            if (unusable.Count == 0) return null;
            var names = string.Join(", ", unusable.Take(UnusableCustomNamed).Select(item => NameOf(item.Clip)));
            if (unusable.Count > UnusableCustomNamed) names += ", …";
            return string.Format(ExporterLocalization.T(UnusableCustomFormat), unusable.Count, names, unusable[0].Error);
        }

        // Without exclusions or added files the ordinary export keeps the
        // avatar's own faces; otherwise the custom list is recorded exactly.
        // A hidden unsupported face excluded earlier still counts, so hiding
        // it does not change what is exported.
        // A renamed custom face is recorded too: the ordinary export names
        // faces after the avatar's menu, which the board cannot rename.
        internal bool UsesRecordRoute => Items.Any(item => item.Added || PlaceOf(item.Clip) is var place &&
            (place.Kind == PlaceKind.Excluded || place.Kind == PlaceKind.Other && Usable(item) && Names.ContainsKey(item.Clip)));

        // The ordinary export records this list instead when the menu gives
        // fewer faces. Recorded faces are named after their files; a repeated
        // name gets " (2)" in list order when recorded, so it does not stop either route.

        internal string NameOf(AnimationClip clip) =>
            clip == null ? "" : Names.TryGetValue(clip, out var name) ? name : clip.name?.Trim() ?? "";
        internal bool Renamed(AnimationClip clip) => clip != null && Names.ContainsKey(clip);

        // A slot face is exported under the VRM preset name, so only faces of
        // the custom and excluded lists can be renamed.
        internal bool CanRename(AnimationClip clip) => Find(clip) is Item item && Usable(item) && PlaceOf(clip).Kind != PlaceKind.Slot;

        internal const string EmptyName = "名前が空です";
        internal const string NameTooLong = "名前は128文字までです";
        internal const string NameHasControl = "改行やタブは使えません";
        internal const string NameReservedFormat = "「{0}」はVRMで決まった表情の名前なので使えません";
        internal const string NameTakenFormat = "「{0}」はその他の表情で使われています";

        // Why the name cannot be used for this face, or null. The result is a
        // localization key; a format takes the trimmed name.
        internal string RenameError(AnimationClip clip, string name)
        {
            name = name?.Trim() ?? "";
            if (name.Length == 0) return EmptyName;
            if (name.Length > 128) return NameTooLong;
            if (name.Any(char.IsControl)) return NameHasControl;
            if (VrmMenuExpressions.Reserved(name)) return NameReservedFormat;
            if (ExportedCustom().Any(other => other != clip && string.Equals(NameOf(other), name, StringComparison.Ordinal))) return NameTakenFormat;
            return null;
        }

        // The file name, or an empty name, gives the face its file name back.
        internal bool Rename(AnimationClip clip, string name)
        {
            if (!CanRename(clip)) return false;
            name = name?.Trim() ?? "";
            if (name.Length == 0 || name == (clip.name?.Trim() ?? "")) return Names.Remove(clip);
            if (RenameError(clip, name) != null) return false;
            if (Names.TryGetValue(clip, out var current) && current == name) return false;
            Names[clip] = name;
            return true;
        }

        internal bool SlotFilled(ExpressionPreset preset) => SlotValue(preset) is PresetAssignment value && !value.IsNone || authored(preset);
        internal int SlotCount(IEnumerable<ExpressionPreset> group = null) => SlotsIn(group).Count(SlotFilled);
        internal int OtherCount => ItemsIn(PlaceKind.Other).Count();
        internal int ExcludedCount => ItemsIn(PlaceKind.Excluded).Count();
        // The faces shown in a place. The avatar's unsupported faces are left out.
        internal IEnumerable<Item> ItemsIn(PlaceKind kind) => Items.Where(item => Shown(item) && PlaceOf(item.Clip).Kind == kind);

        // An added file in the custom list that can no longer be exported. It
        // stops the save, so the list written is always the list shown.
        internal Item Unexported => ItemsIn(PlaceKind.Other).FirstOrDefault(item => item.Error != null);
    }
}
