using NUnit.Framework;
using VRVlog.Expressions;
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

        static ExpressionAnimationData.Curve Curve(WrapMode wrap, params Keyframe[] keys) =>
            VrChatGestureExpressions.ConvertCurve(new AnimationCurve(keys) { preWrapMode = wrap, postWrapMode = wrap });

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

        [Test]
        public void ABlinkWithinTheHoldDoesNotOutscoreTheHeldFace()
        {
            // The closed eye adds 100 for 0.1 s; the face holds 80 for 3 s and 4.7 s.
            var time = TimeOf(Clip(
                ("Sad", new[] { Flat(0, 0), Flat(1, 80), Flat(9, 80), Flat(10, 0) }),
                ("Close", new[] { Flat(0, 0), Flat(4, 0), Flat(4.1f, 100), Flat(4.2f, 100), Flat(4.3f, 0), Flat(10, 0) })));
            Assert.That(time, Is.EqualTo(6.65f).Within(1e-4));
        }

        [Test]
        public void TheHoldIsMeasuredFromTheRestWeight()
        {
            // At rest the shape is 100; the face lowers it to 20 over 3–7 s.
            var curve = Curve(WrapMode.ClampForever, Flat(0, 100), Flat(2, 100), Flat(3, 20), Flat(7, 20), Flat(8, 100), Flat(10, 100));
            Assert.That(ClipFaceTime.Of(new[] { (curve, 100.0) }, 10), Is.EqualTo(5).Within(1e-9));
            // Measured from 0 instead, the rest stretches would win.
            Assert.That(ClipFaceTime.Of(new[] { (curve, 0.0) }, 10), Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void HoldsThatOnlyRestKeepTheLastFrame()
        {
            var time = TimeOf(Clip(("Close", new[] { Flat(0, 0), Flat(4, 0), Flat(4.1f, 100), Flat(4.2f, 0), Flat(10, 0) })));
            Assert.That(time, Is.EqualTo(10).Within(1e-5));
        }

        [Test]
        public void ALoopingOrPingPongCurveHoldsInEveryRepeat()
        {
            // Keys only for 0–4 s of a 12 s face: the loop holds over 1–3, 5–7 and 9–11 s.
            var loop = Curve(WrapMode.Loop, Flat(0, 0), Flat(1, 100), Flat(3, 100), Flat(4, 0));
            Assert.That(ClipFaceTime.Of(new[] { (loop, 0.0) }, 12), Is.EqualTo(2).Within(1e-9));
            // Ping-pong turns back at 2 s: 100 over 0–1 s, then 3–5 s.
            var pingPong = Curve(WrapMode.PingPong, Flat(0, 100), Flat(1, 100), Flat(2, 0));
            Assert.That(ClipFaceTime.Of(new[] { (pingPong, 0.0) }, 6), Is.EqualTo(4).Within(1e-9));
            var looped = Curve(WrapMode.Loop, Flat(0, 100), Flat(1, 100), Flat(2, 0));
            Assert.That(ClipFaceTime.Of(new[] { (looped, 0.0) }, 6), Is.EqualTo(.5).Within(1e-9));
        }

        [Test]
        public void AHeldValueIsItsKeyValue()
        {
            var curve = Curve(WrapMode.ClampForever, Flat(0, 0), Flat(1, 75.3f), Flat(2, 75.3f), Flat(3, 0));
            for (var time = 1.01; time < 2; time += .01)
                Assert.That(ClipFaceTime.Value(curve, time, 3), Is.EqualTo(curve.Keys[1].Value), time.ToString());
            Assert.That(ClipFaceTime.Value(curve, .5, 3), Is.EqualTo(curve.Evaluate(.5)));
        }
    }
}
