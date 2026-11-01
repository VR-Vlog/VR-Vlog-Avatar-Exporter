using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The time at which the board reads a clip as one face (ClipFaceTime).
    public sealed class ClipFaceTimeTests
    {
        static Keyframe Flat(float time, float value) => new Keyframe(time, value, 0, 0);

        static AnimationClip Clip(params (string Shape, Keyframe[] Keys)[] curves)
        {
            var clip = new AnimationClip { name = "Face" };
            foreach (var curve in curves)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape." + curve.Shape),
                    new AnimationCurve(curve.Keys));
            return clip;
        }

        static float TimeOf(AnimationClip clip)
        {
            try { return ClipFaceTime.Of(clip); }
            finally { Object.DestroyImmediate(clip); }
        }

        [Test]
        public void AFaceThatStartsAtRestIsReadInTheMiddleOfItsHold()
        {
            // 0 at the start, the face from 1 s to 3 s, back to 40 at the end
            // of the loop: the last frame would read 40.
            var time = TimeOf(Clip(("Sad", new[] { Flat(0, 0), Flat(.5f, 70), Flat(1, 90), Flat(3, 90), Flat(4, 40) })));
            Assert.That(time, Is.EqualTo(2).Within(1e-5));
        }

        [Test]
        public void ALoopReadsItsLongestHoldAndIgnoresABlinkAndASparkle()
        {
            var sparkle = new Keyframe[25];
            for (var index = 0; index < sparkle.Length; index++) sparkle[index] = Flat(index * .5f, index % 2 == 0 ? 0 : 80);
            var time = TimeOf(Clip(
                ("Sad", new[] { Flat(0, 0), Flat(.7f, 60), Flat(3.6f, 60), Flat(3.8f, 0), Flat(4, 60), Flat(11.5f, 60), Flat(12, 0) }),
                ("Close", new[] { Flat(0, 0), Flat(3.6f, 0), Flat(3.8f, 100), Flat(4, 0), Flat(12, 0) }),
                ("Brow", new[] { Flat(0, 0), Flat(.6f, 50), Flat(11.5f, 50), Flat(12, 0) }),
                ("Sparkle", sparkle)));
            // The same face holds over 0.7–3.6 s and 4–11.5 s; the longer one wins.
            Assert.That(time, Is.EqualTo(7.75f).Within(1e-5));
        }

        [Test]
        public void ALongerWaitAtRestLosesToTheFaceItWaitsFor()
        {
            var time = TimeOf(Clip(("Smile", new[] { Flat(0, 0), Flat(2, 0), Flat(2.2f, 100), Flat(3, 100) })));
            Assert.That(time, Is.EqualTo(2.6f).Within(1e-5));
        }

        [Test]
        public void ASteppedKeyHoldsUntilTheNextKey()
        {
            var step = new Keyframe(1, 100, 0, float.PositiveInfinity);
            var time = TimeOf(Clip(("Smile", new[] { Flat(0, 0), step, new Keyframe(3, 0, float.PositiveInfinity, 0), Flat(4, 0) })));
            Assert.That(time, Is.EqualTo(2).Within(1e-5));
        }

        [Test]
        public void AStillOrOneFrameClipKeepsItsLastFrame()
        {
            var still = Clip(("Smile", new[] { Flat(0, 80), Flat(2, 80) }));
            var length = still.length;
            Assert.That(TimeOf(still), Is.EqualTo(length));
            var single = Clip(("Smile", new[] { Flat(0, 80) }));
            length = single.length;
            Assert.That(TimeOf(single), Is.EqualTo(length));
        }

        [Test]
        public void AFaceThatNeverHoldsKeepsItsLastFrame()
        {
            var ramp = Clip(("Smile", new[] { new Keyframe(0, 0, 0, 50), new Keyframe(2, 100, 50, 0) }));
            var length = ramp.length;
            Assert.That(TimeOf(ramp), Is.EqualTo(length));
        }
    }
}
