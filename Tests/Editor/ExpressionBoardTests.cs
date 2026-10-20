using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniVRM10;
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
        public void ItemsAreDistinctWithAddedFilesFirst()
        {
            var a = Clip("a"); var b = Clip("b"); var c = Clip("c");
            var board = Board(new[] { Item(c), Item(a) }, new[] { Item(a), Item(b), Item(null) });
            Assert.That(board.Items.Select(item => item.Clip), Is.EqualTo(new[] { c, a, b }));
            Assert.That(board.Items.Select(item => item.Added), Is.EqualTo(new[] { true, true, false }));
            Assert.That(board.ItemsIn(PlaceKind.Other).Count(), Is.EqualTo(3));
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
        public void CloseSemanticsOfTheCardButton()
        {
            var a = Clip("a"); var b = Clip("b");
            var board = Board(null, new[] { Item(a), Item(b) });
            // × in the custom list moves the face to the excluded list.
            Assert.That(board.Move(a, Place.Excluded), Is.True);
            Assert.That(board.ExcludedCount, Is.EqualTo(1));
            // × in a slot returns the face to the custom list and leaves the slot explicitly empty.
            board.Move(b, Place.Slot(ExpressionPreset.relaxed));
            Assert.That(board.Move(b, Place.Other), Is.True);
            Assert.That(presets[ExpressionPreset.relaxed].IsNone, Is.True);
            Assert.That(presets[ExpressionPreset.relaxed].Automatic, Is.False);
            board.ApplyAutomatic();
            Assert.That(presets[ExpressionPreset.relaxed].IsNone, Is.True, "The suggestion does not refill an explicitly empty slot.");
            // A BlendShape value is cleared on its slot.
            presets[ExpressionPreset.angry] = Assignment.ForShape(ExpressionPreset.angry, "Face", "Angry");
            board.ClearSlot(ExpressionPreset.angry);
            Assert.That(presets[ExpressionPreset.angry].IsNone, Is.True);
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
            Assert.That(board.ExportedCustom(), Is.Empty, "An unsupported face is shown but never exported.");
            Assert.That(board.CanMove(Clip("unknown"), Place.Excluded), Is.False);
        }

        [Test]
        public void SuggestionsSkipExcludedAndManuallyPlacedFaces()
        {
            var happy = Clip("happy one"); var happy2 = Clip("happy two"); var sad = Clip("sad one");
            var board = Board(null, new[] { Item(happy), Item(happy2), Item(sad) });
            board.ApplyAutomatic();
            Assert.That(presets[ExpressionPreset.happy].Clip, Is.SameAs(happy));
            Assert.That(presets[ExpressionPreset.happy].Automatic, Is.True);
            Assert.That(board.AutomaticCount(), Is.EqualTo(2));

            // Moving the automatic face away leaves its slot explicitly empty.
            board.Move(happy, Place.Excluded);
            board.ApplyAutomatic();
            Assert.That(suggestedFrom.Last(), Is.EqualTo(new[] { happy2, sad }));
            Assert.That(presets[ExpressionPreset.happy].IsNone, Is.True);
            // An open slot takes the next face once the first one is excluded.
            presets.Remove(ExpressionPreset.happy);
            board.ApplyAutomatic();
            Assert.That(presets[ExpressionPreset.happy].Clip, Is.SameAs(happy2));

            board.Move(sad, Place.Slot(ExpressionPreset.surprised));
            board.ApplyAutomatic();
            Assert.That(suggestedFrom.Last(), Has.No.Member(sad));
            Assert.That(presets[ExpressionPreset.sad].IsNone, Is.True, "The slot the face left stays explicitly empty.");
            Assert.That(board.PlaceOf(sad), Is.EqualTo(Place.Slot(ExpressionPreset.surprised)));
        }

        [Test]
        public void ConfirmAndResetChangeOnlyTheBoardChoices()
        {
            var happy = Clip("happy one"); var other = Clip("other");
            var board = Board(null, new[] { Item(happy), Item(other) });
            board.ApplyAutomatic();
            Assert.That(board.ConfirmAutomatic(), Is.EqualTo(1));
            Assert.That(board.AutomaticCount(), Is.Zero);
            Assert.That(presets[ExpressionPreset.happy].Automatic, Is.False);
            Assert.That(presets[ExpressionPreset.happy].Clip, Is.SameAs(happy));

            board.Move(other, Place.Excluded); board.Move(happy, Place.Other);
            board.Reset();
            Assert.That(board.Excluded, Is.Empty);
            Assert.That(presets[ExpressionPreset.happy].Automatic, Is.True);
            Assert.That(presets.Keys, Is.EquivalentTo(new[] { ExpressionPreset.happy }));
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

        [Test]
        public void AuthoredSlotsCountAsFilled()
        {
            authored.Add(ExpressionPreset.angry);
            var board = Board(null, new[] { Item(Clip("angry one")) });
            board.ApplyAutomatic();
            Assert.That(presets.ContainsKey(ExpressionPreset.angry), Is.False);
            Assert.That(board.SlotFilled(ExpressionPreset.angry), Is.True);
            Assert.That(board.SlotCount(), Is.EqualTo(1));
        }

        [Test]
        public void EveryAssignablePresetIsASlot()
        {
            Assert.That(ExperimentalExpressionCaptureSession.AssignablePresets.Length, Is.EqualTo(18));
            var happy = Clip("happy one"); var blink = Clip("blink one"); var mouth = Clip("aa one"); var gaze = Clip("look");
            var board = Board(null, new[] { Item(happy), Item(blink), Item(mouth), Item(gaze) });
            board.ApplyAutomatic();
            Assert.That(presets[ExpressionPreset.blink].Clip, Is.SameAs(blink));
            Assert.That(presets[ExpressionPreset.aa].Clip, Is.SameAs(mouth));
            Assert.That(board.AutomaticCount(), Is.EqualTo(3));
            Assert.That(board.AutomaticCount(ExperimentalExpressionCaptureSession.EmotionPresets), Is.EqualTo(1));
            Assert.That(board.AutomaticCount(ExperimentalExpressionCaptureSession.BlinkPresets), Is.EqualTo(1));

            // Move between groups: a face leaves its slot explicitly empty.
            Assert.That(board.Move(gaze, Place.Slot(ExpressionPreset.lookUp)), Is.True);
            Assert.That(board.Move(mouth, Place.Slot(ExpressionPreset.neutral)), Is.True);
            Assert.That(presets[ExpressionPreset.aa].IsNone, Is.True);
            Assert.That(board.PlaceOf(mouth), Is.EqualTo(Place.Slot(ExpressionPreset.neutral)));
            Assert.That(board.SlotCount(), Is.EqualTo(4));
            Assert.That(board.SlotCount(ExperimentalExpressionCaptureSession.EmotionPresets), Is.EqualTo(1));
            Assert.That(board.SlotCount(ExperimentalExpressionCaptureSession.LookAtPresets), Is.EqualTo(1));

            // Confirming one group leaves the other groups automatic.
            Assert.That(board.ConfirmAutomatic(ExperimentalExpressionCaptureSession.EmotionPresets), Is.EqualTo(1));
            Assert.That(presets[ExpressionPreset.happy].Automatic, Is.False);
            Assert.That(presets[ExpressionPreset.blink].Automatic, Is.True);
            Assert.That(board.ConfirmAutomatic(), Is.EqualTo(1));
            Assert.That(presets[ExpressionPreset.blink].Automatic, Is.False);

            board.Reset();
            Assert.That(presets.Keys, Is.EquivalentTo(new[] { ExpressionPreset.happy, ExpressionPreset.blink, ExpressionPreset.aa }));
            Assert.That(presets.Values.All(value => value.Automatic), Is.True);
        }

        [Test]
        public void ConfirmClearAndSwapReachAllThirteenAndFiveSlots()
        {
            var all = ExperimentalExpressionCaptureSession.AssignablePresets;
            var groups = ExperimentalExpressionCaptureSession.PresetGroups;
            var main = groups.Take(3).SelectMany(group => group.Presets).ToArray();
            var extra = groups.Skip(3).SelectMany(group => group.Presets).ToArray();
            Assert.That(main.Length, Is.EqualTo(13));
            Assert.That(extra.Length, Is.EqualTo(5));
            var faces = all.ToDictionary(preset => preset, preset => Clip(preset + " face"));
            var board = Board(null, all.Select(preset => Item(faces[preset])));
            board.ApplyAutomatic();
            Assert.That(all.All(preset => presets[preset].Clip == faces[preset] && presets[preset].Automatic), Is.True);
            foreach (var group in groups) Assert.That(board.AutomaticCount(group.Presets), Is.EqualTo(group.Presets.Length), group.Label);

            // Confirming the shown groups leaves the folded ones as suggestions.
            Assert.That(board.ConfirmAutomatic(main), Is.EqualTo(13));
            Assert.That(board.AutomaticCount(main), Is.Zero);
            Assert.That(board.AutomaticCount(extra), Is.EqualTo(5));
            Assert.That(board.ConfirmAutomatic(), Is.EqualTo(5));
            Assert.That(board.SlotCount(), Is.EqualTo(18));

            // A drop on a filled slot in another group swaps the previous face out.
            Assert.That(board.Move(faces[ExpressionPreset.neutral], Place.Slot(ExpressionPreset.happy)), Is.True);
            Assert.That(board.PlaceOf(faces[ExpressionPreset.happy]), Is.EqualTo(Place.Other));
            Assert.That(presets[ExpressionPreset.neutral].IsNone, Is.True);
            Assert.That(board.Move(faces[ExpressionPreset.happy], Place.Slot(ExpressionPreset.neutral)), Is.True);
            Assert.That(board.PlaceOf(faces[ExpressionPreset.happy]), Is.EqualTo(Place.Slot(ExpressionPreset.neutral)));
            Assert.That(board.Move(faces[ExpressionPreset.blinkRight], Place.Slot(ExpressionPreset.lookDown)), Is.True);
            Assert.That(board.PlaceOf(faces[ExpressionPreset.lookDown]), Is.EqualTo(Place.Other));
            Assert.That(board.SlotCount(), Is.EqualTo(17));

            foreach (var preset in all) board.ClearSlot(preset);
            Assert.That(all.All(preset => presets[preset].IsNone && !presets[preset].Automatic), Is.True);
            Assert.That(board.SlotCount(), Is.Zero);
            Assert.That(board.AutomaticCount(), Is.Zero);
            Assert.That(board.OtherCount, Is.EqualTo(18), "Clearing a slot returns its face to the other expressions.");
        }

        // The header counts the suggestions in the shown slots, and its button
        // confirms exactly those; the folded slots keep their suggestions.
        [TestCase(false, 13, 5)]
        [TestCase(true, 18, 0)]
        public void TheConfirmButtonConfirmsWhatTheHeaderCounts(bool extraOpen, int shown, int folded)
        {
            var all = ExperimentalExpressionCaptureSession.AssignablePresets;
            var faces = all.ToDictionary(preset => preset, preset => Clip(preset + " face"));
            var board = Board(null, all.Select(preset => Item(faces[preset])));
            board.ApplyAutomatic();
            var group = ExperimentalExpressionCaptureWindow.ShownPresets(extraOpen);
            var counted = board.AutomaticCount(group);
            Assert.That(counted, Is.EqualTo(shown));
            Assert.That(board.ConfirmAutomatic(group), Is.EqualTo(counted));
            Assert.That(board.AutomaticCount(group), Is.Zero);
            Assert.That(board.AutomaticCount(), Is.EqualTo(folded));
        }

        [Test]
        public void MoreThanTheLimitBlocksOnlyTheRecordRoute()
        {
            var faces = Enumerable.Range(0, ExperimentalExpressionCaptureSession.MaximumExpressions + 3).Select(index => Item(Clip("face " + index))).ToArray();
            var board = Board(null, faces);
            Assert.That(board.OverLimitBy, Is.EqualTo(3));
            Assert.That(board.ExceedsLimit, Is.False, "The ordinary route records nothing, so the limit does not apply.");
            board.Move(faces[0].Clip, Place.Excluded);
            Assert.That(board.ExceedsLimit, Is.True);
            Assert.That(board.OverLimitBy, Is.EqualTo(2));
            board.Move(faces[1].Clip, Place.Excluded); board.Move(faces[2].Clip, Place.Slot(ExpressionPreset.happy));
            Assert.That(board.OverLimitBy, Is.Zero);
            Assert.That(board.ExceedsLimit, Is.False);
            Assert.That(board.ExportedCustom().Count, Is.EqualTo(ExperimentalExpressionCaptureSession.MaximumExpressions));
        }

        [Test]
        public void RepeatedNamesBlockOnlyTheRecordRoute()
        {
            var first = Clip("smile"); var second = Clip("smile"); var third = Clip("smile"); var other = Clip("wink");
            var board = Board(null, new[] { Item(first), Item(second), Item(third), Item(other) });
            Assert.That(board.DuplicateNames(), Is.EqualTo(new[] { "smile" }), "Each repeated name is reported once.");
            Assert.That(board.HasDuplicateNames, Is.False, "The ordinary route records nothing, so names do not matter.");
            board.Move(other, Place.Excluded);
            Assert.That(board.HasDuplicateNames, Is.True);
            board.Move(first, Place.Excluded); board.Move(second, Place.Slot(ExpressionPreset.happy));
            Assert.That(board.DuplicateNames(), Is.Empty, "A face in a slot or the excluded list is not recorded as a custom face.");
            Assert.That(board.HasDuplicateNames, Is.False);
        }
    }
}
