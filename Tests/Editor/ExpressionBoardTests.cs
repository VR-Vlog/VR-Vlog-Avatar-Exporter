using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEngine;
using Object = UnityEngine.Object;
using Assignment = VRVlog.LilToonExporter.ExperimentalExpressionCaptureSession.PresetAssignment;
using Place = VRVlog.LilToonExporter.ExpressionBoard.Place;
using PlaceKind = VRVlog.LilToonExporter.ExpressionBoard.PlaceKind;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExpressionBoardTests
    {
        readonly List<AnimationClip> clips = new List<AnimationClip>();
        Dictionary<ExpressionPreset, Assignment> presets;
        List<AnimationClip[]> suggestedFrom;
        HashSet<ExpressionPreset> authored;

        [SetUp]
        public void SetUp()
        {
            presets = new Dictionary<ExpressionPreset, Assignment>();
            suggestedFrom = new List<AnimationClip[]>();
            authored = new HashSet<ExpressionPreset>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var clip in clips) Object.DestroyImmediate(clip);
            clips.Clear();
        }

        AnimationClip Clip(string name)
        {
            var clip = new AnimationClip { name = name };
            clips.Add(clip);
            return clip;
        }

        static ExpressionBoard.Item Item(AnimationClip clip, string error = null) => new ExpressionBoard.Item { Clip = clip, Error = error, Source = "Fixture" };

        // A stand-in for the session: names that start with a preset name are
        // suggested for that preset, first match wins.
        ExpressionBoard Board(IEnumerable<ExpressionBoard.Item> added, IEnumerable<ExpressionBoard.Item> found)
        {
            var board = new ExpressionBoard(presets, (preset, value) => { if (value == null) presets.Remove(preset); else presets[preset] = value; },
                candidates =>
                {
                    var list = candidates.ToArray(); suggestedFrom.Add(list);
                    var result = new Dictionary<ExpressionPreset, Assignment>();
                    foreach (var clip in list)
                        foreach (var preset in ExperimentalExpressionCaptureSession.AssignablePresets)
                            if (clip.name.StartsWith(preset.ToString()) && !result.ContainsKey(preset) && !authored.Contains(preset))
                            { var value = Assignment.ForClip(preset, clip); value.Automatic = true; result.Add(preset, value); }
                    return result;
                }, authored.Contains);
            board.SetItems(added, found);
            return board;
        }

        [Test]
        public void MovesBetweenPlacesAndSwapsAnOccupiedSlot()
        {
            var a = Clip("a"); var b = Clip("b");
            var board = Board(null, new[] { Item(a), Item(b) });
            Assert.That(board.Move(a, Place.Slot(ExpressionPreset.happy)), Is.True);
            Assert.That(board.PlaceOf(a), Is.EqualTo(Place.Slot(ExpressionPreset.happy)));
            Assert.That(presets[ExpressionPreset.happy].Automatic, Is.False);
            Assert.That(board.Move(a, Place.Slot(ExpressionPreset.happy)), Is.False, "Moving to the same place does nothing.");

            // Moving a slot face to another slot leaves the first slot explicitly empty.
            Assert.That(board.Move(a, Place.Slot(ExpressionPreset.sad)), Is.True);
            Assert.That(presets[ExpressionPreset.happy].IsNone, Is.True);
            Assert.That(board.PlaceOf(a), Is.EqualTo(Place.Slot(ExpressionPreset.sad)));

            // Dropping on an occupied slot sends the previous face back to the custom list.
            Assert.That(board.Move(b, Place.Slot(ExpressionPreset.sad)), Is.True);
            Assert.That(board.PlaceOf(b), Is.EqualTo(Place.Slot(ExpressionPreset.sad)));
            Assert.That(board.PlaceOf(a), Is.EqualTo(Place.Other));

            Assert.That(board.Move(b, Place.Excluded), Is.True);
            Assert.That(board.PlaceOf(b), Is.EqualTo(Place.Excluded));
            Assert.That(presets[ExpressionPreset.sad].IsNone, Is.True);
            Assert.That(board.Move(b, Place.Slot(ExpressionPreset.angry)), Is.True);
            Assert.That(board.Excluded, Is.Empty, "A face placed in a slot is no longer excluded.");
            Assert.That(board.Move(b, Place.Other), Is.True);
            Assert.That(board.PlaceOf(b), Is.EqualTo(Place.Other));
        }

        [Test]
        public void UnusableFacesNeverEnterASlot()
        {
            var broken = Clip("happy broken");
            var board = Board(new[] { Item(broken, "unsupported") }, null);
            Assert.That(board.CanMove(broken, Place.Slot(ExpressionPreset.happy)), Is.False);
            Assert.That(board.Move(broken, Place.Slot(ExpressionPreset.happy)), Is.False);
            Assert.That(board.CanMove(broken, Place.Excluded), Is.True);
            board.ApplyAutomatic();
            Assert.That(presets.ContainsKey(ExpressionPreset.happy), Is.False);
            Assert.That(board.ExportedCustom(), Is.Empty, "An unsupported face is never exported.");
            Assert.That(board.CanMove(Clip("unknown"), Place.Excluded), Is.False);
        }

        // An added file that stopped working is shown and counted where it is,
        // and stops the save from the custom list; the avatar's are not shown.
        [Test]
        public void ABrokenAddedFileIsShownCountedAndStopsTheSave()
        {
            var added = Clip("added broken"); var found = Clip("found broken"); var face = Clip("face");
            var board = Board(new[] { Item(added, "unsupported") }, new[] { Item(face), Item(found, "unsupported") });
            Assert.That(board.ItemsIn(PlaceKind.Other).Select(item => item.Clip), Is.EqualTo(new[] { added, face }));
            Assert.That(board.OtherCount, Is.EqualTo(2));
            Assert.That(board.ExportedCustom(), Is.EqualTo(new[] { face }));
            Assert.That(board.Unexported?.Clip, Is.SameAs(added));
            Assert.That(board.Move(added, Place.Excluded), Is.True);
            Assert.That(board.ItemsIn(PlaceKind.Excluded).Single().Clip, Is.SameAs(added));
            Assert.That(board.Unexported, Is.Null, "A file not exported does not stop the save.");
            Assert.That(board.UsesRecordRoute, Is.True);
        }

        [Test]
        public void ExportListFollowsTheBoardAndTheRouteDecision()
        {
            var a = Clip("a"); var b = Clip("b"); var c = Clip("c"); var broken = Clip("broken");
            var board = Board(null, new[] { Item(a), Item(b), Item(c), Item(broken, "unsupported") });
            Assert.That(board.UsesRecordRoute, Is.False, "Without exclusions or added files the ordinary route keeps the avatar's faces.");
            Assert.That(board.ExportedCustom(), Is.EqualTo(new[] { a, b, c }));
            board.Move(a, Place.Slot(ExpressionPreset.happy));
            Assert.That(board.UsesRecordRoute, Is.False);
            board.Move(b, Place.Excluded);
            Assert.That(board.UsesRecordRoute, Is.True);
            Assert.That(board.ExportedCustom(), Is.EqualTo(new[] { c }));
            Assert.That(board.SlotCount(), Is.EqualTo(1));
            Assert.That(board.OtherCount, Is.EqualTo(1));
            Assert.That(board.ExcludedCount, Is.EqualTo(1));

            var added = Board(new[] { Item(Clip("added")) }, new[] { Item(a) });
            Assert.That(added.UsesRecordRoute, Is.True, "An added file uses the record route.");
        }

        // Gaze and neutral the board leaves open are filled from names at
        // export, and only "none" the user picks keeps one empty. A face that
        // leaves one of those slots, or a cleared one, leaves it open rather
        // than "none"; the other slots stay explicitly empty as before.
        [Test]
        public void LeavingAGazeOrNeutralSlotLeavesItOpen()
        {
            var up = Clip("up"); var neutral = Clip("neutral"); var right = Clip("right"); var happy = Clip("happy");
            var board = Board(null, new[] { Item(up), Item(neutral), Item(right), Item(happy) });
            Assert.That(board.Move(up, Place.Slot(ExpressionPreset.lookUp)), Is.True);
            Assert.That(board.Move(up, Place.Other), Is.True);
            Assert.That(presets.ContainsKey(ExpressionPreset.lookUp), Is.False, "A face leaving a gaze slot leaves it open.");
            Assert.That(board.Move(neutral, Place.Slot(ExpressionPreset.neutral)), Is.True);
            Assert.That(board.Move(neutral, Place.Excluded), Is.True);
            Assert.That(presets.ContainsKey(ExpressionPreset.neutral), Is.False, "A face leaving the neutral slot leaves it open.");
            Assert.That(board.Move(right, Place.Slot(ExpressionPreset.lookRight)), Is.True);
            Assert.That(board.Move(right, Place.Slot(ExpressionPreset.happy)), Is.True);
            Assert.That(presets.ContainsKey(ExpressionPreset.lookRight), Is.False, "A face moving to another slot leaves the gaze slot open.");
            Assert.That(presets[ExpressionPreset.happy].Clip, Is.EqualTo(right));

            presets[ExpressionPreset.lookLeft] = Assignment.ForShape(ExpressionPreset.lookLeft, "Face", "LookLeft");
            board.ClearSlot(ExpressionPreset.lookLeft);
            Assert.That(presets.ContainsKey(ExpressionPreset.lookLeft), Is.False, "A cleared gaze slot is left open.");
            // The user's own "none" on a gaze slot is kept.
            presets[ExpressionPreset.lookDown] = Assignment.None(ExpressionPreset.lookDown);
            board.ApplyAutomatic();
            Assert.That(presets[ExpressionPreset.lookDown].IsNone, Is.True);

            // Other slots are still left explicitly empty.
            Assert.That(board.Move(happy, Place.Slot(ExpressionPreset.sad)), Is.True);
            Assert.That(board.Move(happy, Place.Other), Is.True);
            Assert.That(presets[ExpressionPreset.sad].IsNone && !presets[ExpressionPreset.sad].Automatic, Is.True);
            board.ClearSlot(ExpressionPreset.happy);
            Assert.That(presets[ExpressionPreset.happy].IsNone && !presets[ExpressionPreset.happy].Automatic, Is.True);
        }

        [Test]
        public void ARecordRouteOverSixtyFourFacesRecordsThemAll()
        {
            var faces = Enumerable.Range(0, 67).Select(index => Item(Clip("face " + index))).ToArray();
            var board = Board(null, faces);
            Assert.That(board.ExportedCustom().Count, Is.EqualTo(67));
            board.Move(faces[0].Clip, Place.Excluded);
            Assert.That(board.UsesRecordRoute, Is.True);
            Assert.That(board.ExportedCustom().Count, Is.EqualTo(66));
            board.Move(faces[1].Clip, Place.Slot(ExpressionPreset.happy));
            Assert.That(board.ExportedCustom().Count, Is.EqualTo(65));
        }

        [Test]
        public void RenameRefusesNamesTheExportCannotUse()
        {
            var smile = Clip("smile"); var wink = Clip("wink"); var excluded = Clip("hidden");
            var board = Board(null, new[] { Item(smile), Item(wink), Item(excluded) });
            board.Move(excluded, Place.Excluded);
            Assert.That(board.RenameError(smile, "   "), Is.EqualTo(ExpressionBoard.EmptyName));
            Assert.That(board.RenameError(smile, new string('a', 129)), Is.EqualTo(ExpressionBoard.NameTooLong));
            Assert.That(board.RenameError(smile, new string('a', 128)), Is.Null);
            Assert.That(board.RenameError(smile, "two\nlines"), Is.EqualTo(ExpressionBoard.NameHasControl));
            Assert.That(board.RenameError(smile, "a\tb"), Is.EqualTo(ExpressionBoard.NameHasControl));
            Assert.That(board.RenameError(smile, "happy"), Is.EqualTo(ExpressionBoard.NameReservedFormat), "Preset names are reserved.");
            Assert.That(board.RenameError(smile, "Happy"), Is.Null, "Preset names are matched exactly.");
            Assert.That(board.RenameError(smile, "wink"), Is.EqualTo(ExpressionBoard.NameTakenFormat));
            Assert.That(board.RenameError(smile, " wink "), Is.EqualTo(ExpressionBoard.NameTakenFormat));
            Assert.That(board.RenameError(smile, "hidden"), Is.Null, "An excluded face is not exported, so its name is free.");
            Assert.That(board.RenameError(smile, "smile"), Is.Null);
            Assert.That(board.Rename(smile, "wink"), Is.False);
            Assert.That(board.Rename(smile, "happy"), Is.False);
            Assert.That(board.Renamed(smile), Is.False);

            // A renamed face takes its new name, and frees its file name.
            Assert.That(board.Rename(wink, "closed eyes"), Is.True);
            Assert.That(board.RenameError(smile, "closed eyes"), Is.EqualTo(ExpressionBoard.NameTakenFormat));
            Assert.That(board.RenameError(smile, "wink"), Is.Null);
        }

        // The avatar's own faces the board cannot use are neither shown nor
        // exported; the export names them once instead of dropping them silently.
        [Test]
        public void UnusableFacesAreNamedForTheResultList()
        {
            var usable = Clip("usable"); var hidden = Clip("hidden"); var excluded = Clip("excluded");
            var more = Enumerable.Range(0, 6).Select(index => Clip("more " + index)).ToArray();
            var board = Board(null, new[] { Item(usable), Item(hidden, "hidden mesh"), Item(excluded, "other reason") }
                .Concat(more.Select(clip => Item(clip, "hidden mesh"))));
            board.Excluded.Add(excluded);

            Assert.That(board.ExportedCustom(), Is.EqualTo(new[] { usable }));
            Assert.That(board.UnusableCustom().Select(item => item.Clip), Is.EqualTo(new[] { hidden }.Concat(more)),
                "A face the user left out is not reported as unusable.");
            Assert.That(board.UnusableCustomNote(), Is.EqualTo(string.Format(ExporterLocalization.T(ExpressionBoard.UnusableCustomFormat),
                7, "hidden, more 0, more 1, more 2, more 3, …", "hidden mesh")));
            Assert.That(Board(null, new[] { Item(usable) }).UnusableCustomNote(), Is.Null);
        }
    }
}
