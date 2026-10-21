using System;
using System.Collections.Generic;
using System.Linq;
using UniVRM10;
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
        // suggestion does not put it straight back. A face entering an occupied
        // slot sends the previous face back to the custom list.
        internal bool Move(AnimationClip clip, Place target)
        {
            if (!CanMove(clip, target)) return false;
            foreach (var preset in Slots)
                if (ClipOf(SlotValue(preset)) == clip) setPreset(preset, PresetAssignment.None(preset));
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

        internal void ClearSlot(ExpressionPreset preset) => setPreset(preset, PresetAssignment.None(preset));

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
        // are neither shown nor exported.
        internal List<AnimationClip> ExportedCustom() =>
            Items.Where(item => Usable(item) && PlaceOf(item.Clip).Kind == PlaceKind.Other).Select(item => item.Clip).ToList();

        // Without exclusions or added files the ordinary export keeps the
        // avatar's own faces; otherwise the custom list is recorded exactly.
        // A hidden unsupported face excluded earlier still counts, so hiding
        // it does not change what is exported.
        internal bool UsesRecordRoute => Items.Any(item => item.Added || PlaceOf(item.Clip).Kind == PlaceKind.Excluded);
        internal int OverLimitBy => Math.Max(0, ExportedCustom().Count - ExperimentalExpressionCaptureSession.MaximumExpressions);
        internal bool ExceedsLimit => UsesRecordRoute && OverLimitBy > 0;

        // Recorded faces are named after their files and names must be unique.
        // Renaming is not offered, so each repeated name is reported once.
        internal string[] DuplicateNames() => ExportedCustom().GroupBy(clip => clip.name?.Trim() ?? "", StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        internal bool HasDuplicateNames => UsesRecordRoute && DuplicateNames().Length > 0;

        internal bool SlotFilled(ExpressionPreset preset) => SlotValue(preset) is PresetAssignment value && !value.IsNone || authored(preset);
        internal int SlotCount(IEnumerable<ExpressionPreset> group = null) => SlotsIn(group).Count(SlotFilled);
        internal int OtherCount => ExportedCustom().Count;
        internal int ExcludedCount => ItemsIn(PlaceKind.Excluded).Count();
        // The faces shown in a place. Unsupported faces are left out.
        internal IEnumerable<Item> ItemsIn(PlaceKind kind) => Items.Where(item => Usable(item) && PlaceOf(item.Clip).Kind == kind);
    }
}
