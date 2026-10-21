using System.Collections.Generic;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.Tests
{
    // The board reads the menu only for emotion names. Over the read limit it
    // takes none from it and says so beside the emotion slots. The SDK-shaped
    // menus here are read by the production traversal without the VRChat SDK.
    public sealed class BoardMenuOverLimitTests
    {
        [Test]
        public void MenuOverTheItemLimitGivesNoPathsAndANoteWithTheCount()
        {
            var menu = Leaves(300);
            var paths = ExperimentalExpressionCaptureSession.MenuClipPaths(() => VrChatExpressionMenu.ReadMenu(menu), out var overLimit);
            Assert.That(paths, Is.Empty);
            Assert.That(overLimit, Is.Not.Null);
            Assert.That(overLimit.LimitReason, Is.EqualTo("candidates"));
            Assert.That(overLimit.CandidateCount, Is.EqualTo(300));
            Assert.That(ExperimentalExpressionCaptureSession.MenuOverLimitNote(overLimit),
                Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureSession.MenuOverLimitItemsFormat), 300, 256)));
        }

        [Test]
        public void MenuWithinTheLimitGivesNoNote()
        {
            var menu = Leaves(256);
            ExperimentalExpressionCaptureSession.MenuClipPaths(() => VrChatExpressionMenu.ReadMenu(menu), out var overLimit);
            Assert.That(overLimit, Is.Null);
            Assert.That(ExperimentalExpressionCaptureSession.MenuOverLimitNote(null), Is.Null);
        }

        [Test]
        public void OtherReadFailuresGiveNoNote()
        {
            ExperimentalExpressionCaptureSession.MenuClipPaths(() => throw new System.InvalidOperationException("broken"), out var overLimit);
            Assert.That(overLimit, Is.Null);
        }

        [Test]
        public void TooManySubmenusSayItIsTheSubmenus()
        {
            // The root and 520 empty submenus pass the 512 menu visits with no item at all.
            var menu = new Menu();
            for (var i = 0; i < 520; i++) menu.controls.Add(new Control { name = "Sub " + i, type = "SubMenu", subMenu = new Menu() });
            ExperimentalExpressionCaptureSession.MenuClipPaths(() => VrChatExpressionMenu.ReadMenu(menu), out var overLimit);
            Assert.That(overLimit, Is.Not.Null);
            Assert.That(overLimit.LimitReason, Is.EqualTo("menu-visits"));
            Assert.That(ExperimentalExpressionCaptureSession.MenuOverLimitNote(overLimit),
                Is.EqualTo(ExporterLocalization.T(ExperimentalExpressionCaptureSession.MenuOverLimitSubmenus)));
        }

        [Test]
        public void AnIncompleteCountSaysAtLeast()
        {
            var summary = new VrChatMenuImportSummary { CandidateCount = 400, IsComplete = false, LimitReason = "candidates" };
            Assert.That(ExperimentalExpressionCaptureSession.MenuOverLimitNote(summary),
                Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureSession.MenuOverLimitItemsAtLeastFormat), 400, 256)));
        }

        private static Menu Leaves(int count)
        {
            var menu = new Menu();
            for (var i = 0; i < count; i++) menu.controls.Add(new Control { name = "Leaf " + i, type = "Toggle", parameter = new Parameter { name = "Face" }, value = i });
            return menu;
        }

        private sealed class Menu { public List<Control> controls = new List<Control>(); }
        private sealed class Parameter { public string name; }
        private sealed class Control { public string name, type; public Parameter parameter; public float value; public Menu subMenu; }
    }
}
