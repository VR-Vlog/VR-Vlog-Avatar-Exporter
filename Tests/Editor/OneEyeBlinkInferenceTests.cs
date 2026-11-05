using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // A known bilateral blink gets its left and right shapes from the mesh:
    // two one-eye shapes on the avatar's own left and right that together
    // rebuild it, whatever they are called. Blink then closes those two.
    public sealed class OneEyeBlinkInferenceTests
    {
        AttachmentConnectionTests.Fixture fixture;
        readonly List<Object> owned = new List<Object>();
        GameObject Root => fixture.Source;

        [SetUp] public void SetUp() => fixture = new AttachmentConnectionTests.Fixture(eyes: true);
        [TearDown] public void TearDown()
        {
            fixture.Dispose();
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        // The fixture's left eye is at x = -0.03 and its right eye at +0.03.
        static readonly Vector3[] Vertices = {
            new Vector3(-.04f, 1.8f, .06f), new Vector3(-.03f, 1.81f, .06f), new Vector3(-.02f, 1.8f, .06f), // left eye
            new Vector3(.02f, 1.8f, .06f), new Vector3(.03f, 1.81f, .06f), new Vector3(.04f, 1.8f, .06f),    // right eye
            new Vector3(-.03f, 1.85f, .06f), new Vector3(.03f, 1.85f, .06f),                                // brows
            new Vector3(0, 1.7f, .07f)                                                                       // mouth
        };
        static readonly int[] LeftEye = { 0, 1, 2 }, RightEye = { 3, 4, 5 };

        // Each shape is "name:codes". L / R close that eye, B both; l / r
        // close it halfway; S / T close it as a smile does; E / F raise that
        // brow; M opens the mouth. A bare name closes both eyes.
        static Vector3[] Deltas(string codes)
        {
            var deltas = new Vector3[Vertices.Length];
            void Eye(int[] eye, Func<int, Vector3> move) { for (var k = 0; k < eye.Length; k++) deltas[eye[k]] += move(k); }
            Vector3 Close(int k) => new Vector3(0, -.01f, 0);
            Vector3 Half(int k) => new Vector3(0, -.005f, 0);
            Vector3 Smile(int k) => k == 1 ? new Vector3(0, -.004f, .002f) : new Vector3(0, .006f, 0);
            foreach (var code in codes)
                switch (code)
                {
                    case 'L': Eye(LeftEye, Close); break;
                    case 'R': Eye(RightEye, Close); break;
                    case 'B': Eye(LeftEye, Close); Eye(RightEye, Close); break;
                    case 'l': Eye(LeftEye, Half); break;
                    case 'r': Eye(RightEye, Half); break;
                    case 'S': Eye(LeftEye, Smile); break;
                    case 'T': Eye(RightEye, Smile); break;
                    case 'E': deltas[6] += new Vector3(0, .01f, 0); break;
                    case 'F': deltas[7] += new Vector3(0, .01f, 0); break;
                    case 'M': deltas[8] += new Vector3(0, -.02f, 0); break;
                    case '-': break;
                    default: throw new ArgumentException(codes);
                }
            return deltas;
        }

        SkinnedMeshRenderer Face(params string[] shapes)
        {
            var go = new GameObject("face"); go.transform.SetParent(Root.transform, false);
            var mesh = new Mesh { vertices = Vertices, triangles = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 } };
            foreach (var shape in shapes)
            {
                var split = shape.LastIndexOf(':');
                mesh.AddBlendShapeFrame(split < 0 ? shape : shape.Substring(0, split), 100,
                    Deltas(split < 0 ? "B" : shape.Substring(split + 1)), null, null);
            }
            owned.Add(mesh);
            var skin = go.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
            return skin;
        }

        static string[] Shapes(BlinkExportSession resolved, int slot) => resolved.Slots[slot].Select(binding => binding.Shape).ToArray();

        void AssertSides(BlinkExportSession resolved, string left, string right)
        {
            Assert.That(Shapes(resolved, 1), Is.EqualTo(new[] { left }));
            Assert.That(Shapes(resolved, 2), Is.EqualTo(new[] { right }));
            Assert.That(Shapes(resolved, 0), Is.EquivalentTo(new[] { left, right }), "Blink closes both one-eye shapes.");
        }

        static void AssertBilateralOnly(BlinkExportSession resolved, string both)
        {
            Assert.That(Shapes(resolved, 0), Is.EqualTo(new[] { both }));
            Assert.That(resolved.Slots[1], Is.Empty);
            Assert.That(resolved.Slots[2], Is.Empty);
            Assert.That(resolved.Inferences, Is.Empty);
        }

        // Sides are measured across the humanoid. Without one there is nothing
        // to measure, and the blink itself is still found.
        [TestCase(false)]
        [TestCase(true)]
        public void AnAvatarWithoutAHumanoidKeepsItsBlink(bool generic)
        {
            Face("まばたき", "ウィンク:L", "ウィンク右:R");
            var animator = Root.GetComponent<Animator>();
            Avatar avatar = null;
            if (generic)
            {
                avatar = AvatarBuilder.BuildGenericAvatar(Root, ""); owned.Add(avatar);
                Assert.That(avatar.isValid && !avatar.isHuman, "The case needs a valid Generic Avatar.");
            }
            animator.avatar = avatar;
            Assert.That(HumanoidBones.Resolve(animator, HumanBodyBones.Hips), Is.Null);
            AssertBilateralOnly(BlinkExportSession.Resolve(Root), "まばたき");
        }

        // Side markers found on the local avatars, and names that are not
        // forms of the bilateral name, all read from where the shape moves.
        [TestCase("ウィンク", "ウィンク右")]
        [TestCase("ｳｨﾝｸ２", "ｳｨﾝｸ２右")]
        [TestCase("ウインク左", "ウインク右")]
        [TestCase("左ウインク", "右ウインク")]
        [TestCase("eye_close_L", "eye_close_R")]
        [TestCase("eye_close_left", "eye_close_right")]
        [TestCase("eyelid_Blink.L", "eyelid_Blink.R")]
        [TestCase("Eyes_blink_L", "Eyes_blink_R")]
        [TestCase("Left wink", "Right wink")]
        [TestCase("WinkLeft", "WinkRight")]
        [TestCase("eye_ウィンク_1_L", "eye_ウィンク_1_R")]
        [TestCase("shape_a", "shape_b")]
        public void TheOneEyeShapesThatRebuildTheBlinkAreItsSides(string left, string right)
        {
            Face("まばたき", "smile:SMT", left + ":L", right + ":R", "brow:EF");
            var resolved = BlinkExportSession.Resolve(Root);
            AssertSides(resolved, left, right);
            Assert.That(resolved.Inferences.Single(), Is.EqualTo(BlinkExportSession.MeasuredSidesLog + "face/まばたき → " + left + " / " + right));
        }

        // The mesh order and the order of the names do not decide the side.
        [Test] public void TheRightEyeShapeComingFirstStillGoesRight()
        {
            Face("ウィンク右:R", "まばたき", "ウィンク:L");
            AssertSides(BlinkExportSession.Resolve(Root), "ウィンク", "ウィンク右");
        }

        // A name that marks the other eye than the one it closes is a swap,
        // not a side: nothing is guessed from it.
        [TestCase("eye_close_L:R", "eye_close_R:L")]
        [TestCase("ウィンク:R", "ウィンク右:L")]
        [TestCase("Left wink:R", "Right wink:L")]
        public void ANameThatMarksTheOtherEyeIsNotUsed(string first, string second)
        {
            Face("まばたき", first, second);
            AssertBilateralOnly(BlinkExportSession.Resolve(Root), "まばたき");
        }

        // A swapped pair beside a correct one: the correct one is used.
        [Test] public void ASwappedPairDoesNotHideACorrectOne()
        {
            Face("まばたき", "wink_L:R", "wink_R:L", "eye_close_L:L", "eye_close_R:R");
            AssertSides(BlinkExportSession.Resolve(Root), "eye_close_L", "eye_close_R");
        }

        // Shapes that close both eyes, or close an eye differently, are not
        // the blink's sides; a plain wink beside a smiling one is.
        [Test] public void ASmilingWinkLosesToThePlainOne()
        {
            Face("まばたき", "ウインク:S", "ウインク右:T", "ウィンク２:L", "ウィンク２右:R");
            AssertSides(BlinkExportSession.Resolve(Root), "ウィンク２", "ウィンク２右");
        }

        [TestCase("ウインク:S", "ウインク右:T")]   // a smile closes the eyes another way
        [TestCase("half_L:l", "half_R:r")]         // only halfway
        [TestCase("eye_close:B", "eye_close2:B")] // both eyes each
        [TestCase("brow_L:E", "brow_R:F")]         // brows, not eyes
        [TestCase("wink_L:LM", "wink_R:RM")]       // also opens the mouth
        public void ShapesThatDoNotRebuildTheBlinkAreNotItsSides(string first, string second)
        {
            Face("まばたき", first, second);
            AssertBilateralOnly(BlinkExportSession.Resolve(Root), "まばたき");
        }

        // A left eye alone is not a pair.
        [Test] public void OneEyeAloneGivesNoSides()
        {
            Face("まばたき", "ウィンク:L", "smile:T");
            AssertBilateralOnly(BlinkExportSession.Resolve(Root), "まばたき");
        }

        // The same shapes under two names: the pair named as one wins.
        [Test] public void EqualPairsGoToTheOneNamedAsAPair()
        {
            Face("まばたき", "wink_a:L", "other:R", "wink_b_L:L", "wink_b_R:R");
            AssertSides(BlinkExportSession.Resolve(Root), "wink_b_L", "wink_b_R");
        }

        // Without humanoid eyes or arms nothing tells left from right.
        [Test] public void AnAvatarWithoutAHumanoidGetsNoMeasuredSides()
        {
            Face("まばたき", "ウィンク:L", "ウィンク右:R");
            Object.DestroyImmediate(Root.GetComponent<Animator>());
            AssertBilateralOnly(BlinkExportSession.Resolve(Root), "まばたき");
        }

        // The avatar's own left, not the scene's: a turned avatar keeps it.
        [Test] public void ATurnedAvatarKeepsItsOwnLeft()
        {
            Face("まばたき", "ウィンク:L", "ウィンク右:R");
            Root.transform.rotation = Quaternion.Euler(0, 180, 0);
            AssertSides(BlinkExportSession.Resolve(Root), "ウィンク", "ウィンク右");
        }

        // Sides named after the bilateral shape also make blink close both.
        [Test] public void NamedSidesAlsoComposeTheBlink()
        {
            Face("Blink", "Blink_L:L", "Blink_R:R");
            var resolved = BlinkExportSession.Resolve(Root);
            AssertSides(resolved, "Blink_L", "Blink_R");
            Assert.That(resolved.Inferences, Is.Empty, "A known pair is not a guess.");
        }

        // Named sides that also move something the blink does not (here the
        // brows) would change the blink, so it keeps its bilateral shape.
        [Test] public void SidesThatMoveMoreThanTheBlinkLeaveItAsItWas()
        {
            Face("Blink", "Blink_L:LE", "Blink_R:RF");
            var resolved = BlinkExportSession.Resolve(Root);
            Assert.That(Shapes(resolved, 0), Is.EqualTo(new[] { "Blink" }));
            Assert.That(Shapes(resolved, 1), Is.EqualTo(new[] { "Blink_L" }));
            Assert.That(Shapes(resolved, 2), Is.EqualTo(new[] { "Blink_R" }));
        }

        // Without sides the blink stays its bilateral shape.
        [Test] public void WithoutSidesTheBlinkStaysAsItWas()
        {
            Face("まばたき", "smile:SMT");
            AssertBilateralOnly(BlinkExportSession.Resolve(Root), "まばたき");
        }

        // A manual setting is exported as chosen.
        [Test] public void AManualSettingIsNotComposed()
        {
            var face = Face("まばたき", "ウィンク:L", "ウィンク右:R");
            var options = new BlinkExportOptions { Mode = BlinkExportMode.Manual };
            options.Both.Add(new BlinkShapeBinding { Renderer = face, Shape = "まばたき" });
            options.Left.Add(new BlinkShapeBinding { Renderer = face, Shape = "ウィンク" });
            options.Right.Add(new BlinkShapeBinding { Renderer = face, Shape = "ウィンク右" });
            var resolved = BlinkExportSession.Resolve(Root, options);
            Assert.That(Shapes(resolved, 0), Is.EqualTo(new[] { "まばたき" }));
        }

        // The baked blink closes both eyes as far as the bilateral shape did.
        [Test] public void TheComposedBlinkClosesBothEyesFully()
        {
            Face("まばたき", "ウィンク:L", "ウィンク右:R");
            var resolved = BlinkExportSession.Resolve(Root);
            Assert.That(resolved.Slots[0].Select(binding => binding.Weight), Is.All.EqualTo(100f));
            var expected = Deltas("B");
            var sum = new Vector3[Vertices.Length];
            foreach (var binding in resolved.Slots[0])
            {
                var mesh = binding.Renderer.sharedMesh; var deltas = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(mesh.GetBlendShapeIndex(binding.Shape), 0, deltas, null, null);
                for (var i = 0; i < sum.Length; i++) sum[i] += deltas[i];
            }
            for (var i = 0; i < sum.Length; i++) Assert.That((sum[i] - expected[i]).magnitude, Is.LessThan(1e-6f));
        }
    }
}
