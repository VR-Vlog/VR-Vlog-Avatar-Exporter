using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Direction = VRVlog.LilToonExporter.GazeRangeSection.Direction;

namespace VRVlog.LilToonExporter.Tests
{
    // 視線の動く範囲: the values the section stores and the way its previews turn the eyes.
    public sealed class GazeRangeSectionTests
    {
        [Test]
        public void TheFivePreviewsFormACross()
        {
            Assert.That(GazeRangeSection.Slot(Direction.Up), Is.EqualTo((1, 0)));
            Assert.That(GazeRangeSection.Slot(Direction.Left), Is.EqualTo((0, 1)));
            Assert.That(GazeRangeSection.Slot(Direction.Straight), Is.EqualTo((1, 1)));
            Assert.That(GazeRangeSection.Slot(Direction.Right), Is.EqualTo((2, 1)));
            Assert.That(GazeRangeSection.Slot(Direction.Down), Is.EqualTo((1, 2)));
        }

        // Back at the avatar's own angle the axis is cleared, so the export is
        // exactly what it was before anyone moved the slider.
        [Test]
        public void ASliderBackAtTheAvatarsAngleStoresNothing()
        {
            Assert.That(GazeRangeSection.Stored(12, 11.6f), Is.EqualTo(0));
            Assert.That(GazeRangeSection.Stored(13, 11.6f), Is.EqualTo(13));
            Assert.That(GazeRangeSection.Stored(1, 10), Is.EqualTo(1));
        }

        [Test]
        public void ASetAxisReplacesOnlyItsOwnRanges()
        {
            var measured = new VrmLookAtExport.Ranges
            {
                HorizontalInner = VrmLookAtExport.Range.Linear(14),
                HorizontalOuter = VrmLookAtExport.Range.Linear(18),
                VerticalDown = VrmLookAtExport.Range.Linear(9),
                VerticalUp = VrmLookAtExport.Range.Linear(12),
            };
            var shown = measured.Shown();
            Assert.That((shown.Up, shown.Down, shown.Side), Is.EqualTo((12f, 9f, 14f)), "Sides show the smaller of the two.");
            var kept = measured.With(new GazeRangeOptions());
            Assert.That(kept.HorizontalOuter.Output, Is.EqualTo(18f), "Nothing set keeps both sides as measured.");
            var sides = measured.With(new GazeRangeOptions { Side = 25 });
            Assert.That((sides.HorizontalInner.Input, sides.HorizontalInner.Output, sides.HorizontalOuter.Input, sides.HorizontalOuter.Output),
                Is.EqualTo((25f, 25f, 25f, 25f)));
            Assert.That(sides.VerticalUp.Output, Is.EqualTo(12f));
            var invalid = measured.With(new GazeRangeOptions { Up = float.NaN, Down = 400 });
            Assert.That((invalid.VerticalUp.Output, invalid.VerticalDown.Output), Is.EqualTo((12f, 9f)));
            var defaults = VrmLookAtExport.Ranges.Default.With(new GazeRangeOptions { Up = 20 });
            Assert.That((defaults.VerticalUp.Input, defaults.VerticalUp.Output, defaults.VerticalDown.Input, defaults.VerticalDown.Output),
                Is.EqualTo((20f, 20f, 90f, 10f)));
        }

        // The camera faces the avatar, so the preview on the screen's right
        // looks toward the avatar's -X: outward for the left eye, inward for the right.
        [Test]
        public void EachPreviewTurnsTheEyesTowardItsPlace()
        {
            var ranges = new VrmLookAtExport.Ranges
            {
                HorizontalInner = VrmLookAtExport.Range.Linear(14),
                HorizontalOuter = VrmLookAtExport.Range.Linear(18),
                VerticalDown = VrmLookAtExport.Range.Linear(9),
                VerticalUp = VrmLookAtExport.Range.Linear(12),
            };
            Assert.That(GazeRangeSection.EyeTurns(Direction.Straight, ranges), Is.EqualTo((Vector2.zero, Vector2.zero)));
            Assert.That(GazeRangeSection.EyeTurns(Direction.Up, ranges), Is.EqualTo((new Vector2(0, 12), new Vector2(0, 12))));
            Assert.That(GazeRangeSection.EyeTurns(Direction.Down, ranges), Is.EqualTo((new Vector2(0, -9), new Vector2(0, -9))));
            Assert.That(GazeRangeSection.EyeTurns(Direction.Right, ranges), Is.EqualTo((new Vector2(-18, 0), new Vector2(-14, 0))));
            Assert.That(GazeRangeSection.EyeTurns(Direction.Left, ranges), Is.EqualTo((new Vector2(14, 0), new Vector2(18, 0))));

            var root = new GameObject("Root");
            try
            {
                root.transform.rotation = Quaternion.Euler(0, 90, 0);
                var eye = new GameObject("Eye").transform;
                eye.SetParent(root.transform, false);
                eye.localRotation = Quaternion.Euler(0, 0, 7);
                var rest = eye.localRotation;
                GazeRangeSection.Turn(root.transform, eye, rest, new Vector2(0, 12));
                var forward = root.transform.InverseTransformDirection(eye.rotation * Quaternion.Inverse(root.transform.rotation * rest) * root.transform.forward);
                Assert.That(Vector3.Angle(forward, Vector3.forward), Is.EqualTo(12).Within(.01f));
                Assert.That(forward.y, Is.GreaterThan(0), "Up turns the eye upward in the avatar's frame.");
                GazeRangeSection.Turn(root.transform, eye, rest, new Vector2(-18, 0));
                forward = root.transform.InverseTransformDirection(eye.rotation * Quaternion.Inverse(root.transform.rotation * rest) * root.transform.forward);
                Assert.That(forward.x, Is.LessThan(0), "The screen's right is the avatar's -X.");
                Assert.That(Vector3.Angle(forward, Vector3.forward), Is.EqualTo(18).Within(.01f));
                GazeRangeSection.Turn(root.transform, eye, rest, Vector2.zero);
                Assert.That(Quaternion.Angle(eye.localRotation, rest), Is.LessThan(.01f), "Zero puts the eye back at rest.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheNoteSaysWhereTheInitialAngleComesFrom()
        {
            Assert.That(GazeRangeSection.InitialNote(new GazeRangeOptions { Up = 4, Down = 4.2f, Side = 3.8f }, true),
                Is.EqualTo(string.Format(ExporterLocalization.T(GazeRangeSection.EyeLookNote), "4°")));
            Assert.That(GazeRangeSection.InitialNote(new GazeRangeOptions { Up = 10, Down = 10, Side = 10 }, false),
                Is.EqualTo(string.Format(ExporterLocalization.T(GazeRangeSection.VrmDefaultNote), "10°")));
            var mixed = GazeRangeSection.InitialNote(new GazeRangeOptions { Up = 14, Down = 11, Side = 20 }, true);
            Assert.That(mixed, Does.Contain("14°").And.Contain("11°").And.Contain("20°"));
        }

        // The notes of the section are read in every locale the package ships.
        [Test]
        public void EveryShippedLocaleTranslatesTheNotes()
        {
            var guid = UnityEditor.AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset")[0];
            var directory = System.IO.Path.GetDirectoryName(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var text = System.IO.File.ReadAllText(System.IO.Path.Combine(directory, "ExporterLocale_" + locale + ".json"));
                foreach (var source in new[] { GazeRangeSection.FollowNote, GazeRangeSection.VrmDefaultNote, GazeRangeSection.EyeLookNote })
                    Assert.That(text, Does.Contain("{\"source\":\"" + source + "\",\"translated\":\""), locale + ": " + source);
            }
        }

        [Test]
        public void ACellNamesItsDirectionAndAngle()
        {
            var shown = new GazeRangeOptions { Up = 12, Down = 9, Side = 14.4f };
            Assert.That(GazeRangeSection.CellLabel(Direction.Up, shown), Is.EqualTo("↑ " + ExporterLocalization.T("上") + " 12°"));
            Assert.That(GazeRangeSection.CellLabel(Direction.Right, shown), Is.EqualTo("→ " + ExporterLocalization.T("右") + " 14°"));
            Assert.That(GazeRangeSection.CellLabel(Direction.Straight, shown), Is.EqualTo(ExporterLocalization.T(GazeRangeSection.Straight)));
        }

        // The gaze has its own step between the expressions and the objects,
        // and the four steps still fit the row of the board's narrowest width.
        [Test]
        public void TheGazeStepComesAfterTheExpressions()
        {
            var names = StepIndicator.Names();
            Assert.That(names, Is.EqualTo(new[] { "1 表情の設定", "2 視線の設定", "3 除外するオブジェクト", "4 VRMの保存と転送" }));
            Assert.That(names[ExperimentalExpressionCaptureWindow.GazePage], Is.EqualTo("2 視線の設定"));
            Assert.That(names[ExperimentalExpressionCaptureWindow.ObjectsPage], Is.EqualTo("3 除外するオブジェクト"));
            Assert.That(names[ExperimentalExpressionCaptureWindow.InfoPage], Is.EqualTo("4 VRMの保存と転送"));
            var row = new Rect(16, 0, 600 - 32, StepIndicator.Height);
            var layout = StepIndicator.Compute(row);
            Assert.That(layout.Circles.Length, Is.EqualTo(4));
            for (var index = 0; index < 4; index++)
            {
                Assert.That(layout.Labels[index].xMax, Is.LessThanOrEqualTo(row.xMax + .5f));
                if (index > 0)
                {
                    Assert.That(layout.Circles[index].x, Is.GreaterThan(layout.Labels[index - 1].xMax));
                    Assert.That(layout.Lines[index - 1].width, Is.GreaterThanOrEqualTo(StepIndicator.MinLine - 1));
                }
            }
        }

        // A failed inspection shows 0°, which the disabled slider clamps to 1°;
        // only a slider the user can move saves a value.
        [Test]
        public void ADisabledSectionSavesNothingWhenItsSliderClamps()
        {
            var zero = new GazeRangeOptions();
            Assert.That(GazeRangeSection.Moved(false, zero, zero, zero, 1, 1, 1), Is.Null);
            var initial = new GazeRangeOptions { Up = 10, Down = 10, Side = 10 };
            Assert.That(GazeRangeSection.Moved(false, zero, initial, initial, 20, 10, 10), Is.Null);
            Assert.That(GazeRangeSection.Moved(true, zero, initial, initial, 10, 10, 10), Is.Null, "Nothing moved.");
            var moved = GazeRangeSection.Moved(true, zero, initial, initial, 20, 10, 10);
            Assert.That((moved.Up, moved.Down, moved.Side), Is.EqualTo((20f, 0f, 0f)));
        }

        [Test]
        public void ANeutralFaceShownInPartGoesBack()
        {
            var face = "smile";
            var show = new System.Action<string>(next =>
            {
                face = next;
                if (next == "neutral") throw new System.InvalidOperationException("broken");
            });
            var neutral = GazeRangeSection.NeutralFace(() => face, show, () => "neutral");
            Assert.Throws<System.InvalidOperationException>(() => neutral());
            Assert.That(face, Is.EqualTo("smile"));

            face = "smile";
            var restore = GazeRangeSection.NeutralFace(() => face, next => face = next, () => "neutral")();
            Assert.That(face, Is.EqualTo("neutral"));
            restore();
            Assert.That(face, Is.EqualTo("smile"));
        }

        // A drag changes the value on every move and saves once, when it lets go.
        [Test]
        public void ADragSavesOnceWhenItLetsGo()
        {
            var avatar = new GameObject("Avatar");
            var held = GUIUtility.hotControl;
            try
            {
                var unsaved = new GazeRangeSection.Unsaved();
                Assert.That(unsaved.Take(), Is.Null, "Nothing changed.");
                unsaved.Changed(avatar);
                GUIUtility.hotControl = 42;
                Assert.That(unsaved.Take(), Is.Null, "Still dragging.");
                Assert.That(unsaved.Take(now: true), Is.SameAs(avatar), "Leaving the avatar or closing saves now.");
                Assert.That(unsaved.Take(now: true), Is.Null);
                unsaved.Changed(avatar);
                GUIUtility.hotControl = 0;
                Assert.That(unsaved.Take(), Is.SameAs(avatar));
                Assert.That(unsaved.Take(), Is.Null, "Saved once.");
            }
            finally
            {
                GUIUtility.hotControl = held;
                Object.DestroyImmediate(avatar);
            }
        }

        // Drawing the previews leaves the host's camera and face as they were,
        // also when showing the neutral face fails.
        [Test]
        public void ThePreviewsPutTheCameraAndTheFaceBack()
        {
            var preview = new UnityEditor.PreviewRenderUtility();
            var avatar = new GameObject("Avatar");
            var section = new GazeRangeSection();
            try
            {
                var camera = preview.camera.transform;
                camera.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 0));
                var face = "smile";
                var target = new GazeRangeSection.Target
                {
                    Source = avatar, Avatar = avatar, Preview = preview,
                    ShowNeutralFace = GazeRangeSection.NeutralFace(() => face, next => face = next, () => "neutral"),
                };
                try { section.Render(target, VrmLookAtExport.Ranges.Default); }
                // Without a graphics device drawing may fail; what it changed still goes back.
                catch (System.Exception exception) when (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Debug.Log(exception.Message); }
                Assert.That(face, Is.EqualTo("smile"));
                Assert.That(Vector3.Distance(camera.position, new Vector3(1, 2, 3)), Is.LessThan(1e-4f));
                Assert.That(Quaternion.Angle(camera.rotation, Quaternion.Euler(10, 20, 0)), Is.LessThan(.01f));

                section.Invalidate();
                section.Dispose();
                target.ShowNeutralFace = GazeRangeSection.NeutralFace<string>(() => face, next =>
                {
                    face = next;
                    if (next == "neutral") throw new System.InvalidOperationException("broken");
                }, () => "neutral");
                Assert.Throws<System.InvalidOperationException>(() => section.Render(target, VrmLookAtExport.Ranges.Default));
                Assert.That(face, Is.EqualTo("smile"));
                Assert.That(Vector3.Distance(camera.position, new Vector3(1, 2, 3)), Is.LessThan(1e-4f));
            }
            finally
            {
                section.Dispose();
                preview.Cleanup();
                Object.DestroyImmediate(avatar);
            }
        }
    }
}
