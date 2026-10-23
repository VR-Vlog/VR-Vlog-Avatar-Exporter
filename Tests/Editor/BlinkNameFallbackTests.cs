using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The name fallbacks run only after every existing rule left the eye
    // slots empty, and assign only one unambiguous reading.
    public sealed class BlinkNameFallbackTests
    {
        GameObject root;
        readonly List<Object> owned = new List<Object>();

        [SetUp] public void SetUp() => root = new GameObject("avatar");
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(root);
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        // Vertices 0-2 are the left eye and 3-5 the right eye. Each shape is
        // "name:L", "name:R", "name:B" (both eyes) or "name:-" (no move);
        // a bare name moves both eyes.
        SkinnedMeshRenderer Face(params string[] shapes)
        {
            var go = new GameObject("face"); go.transform.SetParent(root.transform, false);
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, Vector3.forward + Vector3.right, Vector3.forward + Vector3.up },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            foreach (var shape in shapes)
            {
                var split = shape.LastIndexOf(':');
                var name = split < 0 ? shape : shape.Substring(0, split);
                var moves = split < 0 ? "B" : shape.Substring(split + 1);
                var deltas = Enumerable.Range(0, 6).Select(i =>
                    moves == "B" || moves == "L" && i < 3 || moves == "R" && i >= 3 ? Vector3.down * .01f : Vector3.zero).ToArray();
                mesh.AddBlendShapeFrame(name, 100, deltas, null, null);
            }
            owned.Add(mesh);
            var skin = go.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
            return skin;
        }

        static string[] Shapes(BlinkExportSession resolved, int slot) => resolved.Slots[slot].Select(binding => binding.Shape).ToArray();

        static void AssertNothing(BlinkExportSession resolved)
        {
            Assert.That(resolved.Slots.All(slot => slot.Count == 0), Is.True, "A low-confidence reading assigns nothing.");
            Assert.That(resolved.Inferences, Is.Empty);
        }

        // An exact bilateral name with left and right forms of its own name.
        [TestCase("まばたき_L", "まばたき_R")]
        [TestCase("まばたき.L", "まばたき.R")]
        [TestCase("まばたき Left", "まばたき Right")]
        [TestCase("まばたきLeft", "まばたきRight")]
        [TestCase("L_まばたき", "R_まばたき")]
        [TestCase("Left-まばたき", "Right-まばたき")]
        [TestCase("まばたき左", "まばたき右")]
        [TestCase("左まばたき", "右まばたき")]
        public void TheSidesOfAKnownBilateralNameAreItsOwnLeftAndRightForms(string left, string right)
        {
            Face("まばたき", left + ":L", right + ":R", "smile");
            var resolved = BlinkExportSession.Resolve(root);
            Assert.That(Shapes(resolved, 0), Is.EqualTo(new[] { "まばたき" }));
            Assert.That(Shapes(resolved, 1), Is.EqualTo(new[] { left }));
            Assert.That(Shapes(resolved, 2), Is.EqualTo(new[] { right }));
            Assert.That(resolved.Inferences.Single(), Is.EqualTo(BlinkExportSession.InferredSidesLog + "face/まばたき → " + left + " / " + right));
        }

        // A bare L or R without a separator would read as part of a word.
        [Test] public void ABareSideLetterIsNotASideMarker()
        {
            Face("まばたき", "まばたきL:L", "まばたきR:R");
            var resolved = BlinkExportSession.Resolve(root);
            Assert.That(Shapes(resolved, 0), Is.EqualTo(new[] { "まばたき" }));
            Assert.That(resolved.Slots[1], Is.Empty);
            Assert.That(resolved.Inferences, Is.Empty);
        }

        [TestCase("まばたき_L:L", "まばたき_R:R", "まばたき左:L", "まばたき右:R", TestName = "TwoSideFormsAreAmbiguous")]
        [TestCase("まばたき_L:L", TestName = "OneSideOnlyIsNotAPair")]
        [TestCase("まばたき_L:B", "まばたき_R:B", TestName = "SidesThatMoveTheSameEyeAreNotAPair")]
        [TestCase("まばたき_L:L", "まばたき_R:-", TestName = "ASideThatDoesNotMoveIsNotAPair")]
        [TestCase("まばたき_L:L", "まばたき_R:R", "まばたき_r:R", TestName = "ACaseDuplicateSideIsNotAPair")]
        public void UnclearSidesOfABilateralNameAreNotGuessed(params string[] sides)
        {
            Face(new[] { "まばたき" }.Concat(sides).ToArray());
            var resolved = BlinkExportSession.Resolve(root);
            Assert.That(Shapes(resolved, 0), Is.EqualTo(new[] { "まばたき" }), "The bilateral choice itself is unchanged.");
            Assert.That(resolved.Slots[1], Is.Empty);
            Assert.That(resolved.Slots[2], Is.Empty);
            Assert.That(resolved.Inferences, Is.Empty);
        }

        // Every bilateral binding needs its own pair, or none gets one.
        [Test] public void EveryBilateralRendererMustHaveItsPair()
        {
            Face("まばたき", "まばたき_L:L", "まばたき_R:R");
            var lashes = Face("まばたき");
            lashes.name = "lashes";
            var resolved = BlinkExportSession.Resolve(root);
            Assert.That(resolved.Slots[0].Count, Is.EqualTo(2));
            Assert.That(resolved.Slots[1], Is.Empty);
            Assert.That(resolved.Inferences, Is.Empty);
        }

        // What the existing rules choose is never replaced.
        [TestCase(new[] { "Blink", "Blink_L:L", "Blink_R:R", "Blink.L:L", "Blink.R:R" }, "Blink", "Blink_L", "Blink_R")]
        [TestCase(new[] { "vrc.Blink", "Blink_L:L", "Blink_R:R", "vrc.Blink_L:L", "vrc.Blink_R:R" }, "vrc.Blink", "Blink_L", "Blink_R")]
        [TestCase(new[] { "eyelid_Blink", "eyelid_Blink.L:L", "eyelid_Blink.R:R", "eyelid_Blink_L:L", "eyelid_Blink_R:R" }, "eyelid_Blink", "eyelid_Blink.L", "eyelid_Blink.R")]
        public void ExistingChoicesAreUnchanged(string[] shapes, string both, string left, string right)
        {
            Face(shapes.Concat(new[] { "目閉じ", "目閉じ_L:L", "目閉じ_R:R" }).ToArray());
            var resolved = BlinkExportSession.Resolve(root);
            Assert.That(Shapes(resolved, 0), Is.EqualTo(new[] { both }));
            Assert.That(Shapes(resolved, 1), Is.EqualTo(new[] { left }));
            Assert.That(Shapes(resolved, 2), Is.EqualTo(new[] { right }));
            Assert.That(resolved.Inferences, Is.Empty);
        }

        // A recognised closing name that could not be resolved keeps its
        // existing outcome instead of falling through to the name search.
        [Test] public void AnUnresolvedExactNameDoesNotFallThroughToTheNameSearch()
        {
            Face("Blink_L:L", "目閉じ", "目閉じ_L:L", "目閉じ_R:R");
            var resolved = BlinkExportSession.Resolve(root);
            Assert.That(resolved.Slots.All(slot => slot.Count == 0), Is.True);
            Assert.That(resolved.UnresolvedAutomatic, Is.True);
            Assert.That(resolved.Inferences, Is.Empty);
        }

        [TestCase(new[] { "目閉じ" }, new[] { "目閉じ" }, new string[0], new string[0])]
        [TestCase(new[] { "Eyes Blink", "smile" }, new[] { "Eyes Blink" }, new string[0], new string[0])]
        [TestCase(new[] { "閉眼", "閉眼_L:L", "閉眼_R:R" }, new[] { "閉眼" }, new[] { "閉眼_L" }, new[] { "閉眼_R" })]
        [TestCase(new[] { "目閉じ", "左目閉じ:L", "右目閉じ:R" }, new[] { "目閉じ" }, new[] { "左目閉じ" }, new[] { "右目閉じ" })]
        [TestCase(new[] { "Eye_Blink.Left:L", "Eye_Blink.Right:R" }, new[] { "Eye_Blink.Left", "Eye_Blink.Right" }, new[] { "Eye_Blink.Left" }, new[] { "Eye_Blink.Right" })]
        [TestCase(new[] { "L_瞬き:L", "R_瞬き:R" }, new[] { "L_瞬き", "R_瞬き" }, new[] { "L_瞬き" }, new[] { "R_瞬き" })]
        public void AFaceWithoutKnownNamesIsReadByItsBlinkWords(string[] shapes, string[] both, string[] left, string[] right)
        {
            Face(shapes);
            var resolved = BlinkExportSession.Resolve(root);
            Assert.That(Shapes(resolved, 0), Is.EqualTo(both));
            Assert.That(Shapes(resolved, 1), Is.EqualTo(left));
            Assert.That(Shapes(resolved, 2), Is.EqualTo(right));
            Assert.That(resolved.Disabled, Is.False);
            Assert.That(resolved.Inferences.Single(), Does.StartWith(BlinkExportSession.InferredNamesLog));
        }

        [TestCase("目閉じ,閉眼", TestName = "TwoBilateralNamesAreAmbiguous")]
        [TestCase("目閉じ_L:L,目閉じ_R:R,閉眼_L:L,閉眼_R:R", TestName = "TwoPairsWithoutABilateralNameAreAmbiguous")]
        [TestCase("目閉じ_L:L", TestName = "OneSideAloneIsNotAssigned")]
        [TestCase("目閉じ_L:L,閉眼_R:R", TestName = "SidesWithDifferentRootsAreNotAPair")]
        [TestCase("目閉じ_L:B,目閉じ_R:B", TestName = "SidesThatMoveTheSameVerticesAreNotAPair")]
        [TestCase("目閉じ:-", TestName = "AShapeThatDoesNotMoveIsNotAssigned")]
        [TestCase("目閉じ,目とじ", TestName = "TwoJapaneseSpellingsAreAmbiguous")]
        [TestCase("eyes_blink,Eyes_Blink", TestName = "ACaseDuplicateNameIsNotAssigned")]
        [TestCase("blink_smile,smile_blink,笑い目閉じ", TestName = "ASmilingClosureIsNotABlink")]
        [TestCase("wink_L:L,wink_R:R", TestName = "AWinkIsNotABlink")]
        [TestCase("blink2,blink_half,preview_blink", TestName = "OtherWordsOrNumbersAreNotABlink")]
        [TestCase("eye_close,close", TestName = "CloseAloneIsNotABlink")]
        public void LowConfidenceNamesAssignNothing(string shapes)
        {
            Face(shapes.Split(','));
            AssertNothing(BlinkExportSession.Resolve(root));
        }

        // The export capture keeps what the details report.
        [Test] public void TheExportCaptureKeepsTheInference()
        {
            Face("まばたき", "まばたき_L:L", "まばたき_R:R");
            using var session = BlinkExportSession.CaptureForExport(root);
            Assert.That(session.Inferences.Single(), Does.StartWith(BlinkExportSession.InferredSidesLog));
            Assert.That(Shapes(session, 1), Is.EqualTo(new[] { "まばたき_L" }));
        }

        [Test] public void DescriptorClosureGainsItsOwnSides()
        {
            var descriptorType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(t => t != null);
            if (descriptorType == null) Assert.Ignore("VRChat SDK is required for the descriptor integration test.");
            var skin = Face("closed_eyes", "look_up", "look_down", "closed_eyes_L:L", "closed_eyes_R:R");
            var descriptor = root.AddComponent(descriptorType);
            descriptorType.GetField("enableEyeLook").SetValue(descriptor, true);
            var settingsField = descriptorType.GetField("customEyeLookSettings"); var settings = settingsField.GetValue(descriptor);
            var settingsType = settings.GetType(); var lidType = settingsType.GetField("eyelidType");
            lidType.SetValue(settings, Enum.Parse(lidType.FieldType, "Blendshapes"));
            settingsType.GetField("eyelidsSkinnedMesh").SetValue(settings, skin);
            settingsType.GetField("eyelidsBlendshapes").SetValue(settings, new[] { 0, 1, 2 });
            settingsField.SetValue(descriptor, settings);
            var resolved = BlinkExportSession.Resolve(root);
            Assert.That(resolved.ConfiguredByDescriptor, Is.True);
            Assert.That(Shapes(resolved, 0), Is.EqualTo(new[] { "closed_eyes" }));
            Assert.That(Shapes(resolved, 1), Is.EqualTo(new[] { "closed_eyes_L" }));
            Assert.That(Shapes(resolved, 2), Is.EqualTo(new[] { "closed_eyes_R" }));
        }

        [TestCase("Close_L", "Close_R", ExpectedResult = new[] { 1, 2 })]
        [TestCase("Close_L", "Close_left", ExpectedResult = new[] { -1, -1 })]
        [TestCase("CLOSE_l", "CLOSE_r", ExpectedResult = new[] { 1, 2 })]
        public int[] DerivedSidesReadOnlyOneForm(string left, string right)
        {
            var (l, r) = BlinkShapeNames.DerivedSides(new[] { "Close", left, right }, "Close");
            return new[] { l, r };
        }

        [TestCase("目閉じ", ExpectedResult = BlinkShapeNames.ClosingBoth)]
        [TestCase("左目閉じ", ExpectedResult = BlinkShapeNames.ClosingLeft)]
        [TestCase("目閉じ右", ExpectedResult = BlinkShapeNames.ClosingRight)]
        [TestCase("EyeBlink_L", ExpectedResult = BlinkShapeNames.ClosingLeft)]
        [TestCase("eyeBlinkRight", ExpectedResult = BlinkShapeNames.ClosingRight)]
        [TestCase("Fcl_Blink", ExpectedResult = BlinkShapeNames.ClosingBoth)]
        [TestCase("blink_smile", ExpectedResult = BlinkShapeNames.NotClosing)]
        [TestCase("blink01", ExpectedResult = BlinkShapeNames.NotClosing)]
        [TestCase("Blink_L_R", ExpectedResult = BlinkShapeNames.NotClosing)]
        [TestCase("blinking", ExpectedResult = BlinkShapeNames.NotClosing)]
        public int ClosingNamesAreOnlyBlinkWords(string name) => BlinkShapeNames.NamedClosure(name, out _);
    }
}
