using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // These are SDK-shaped serialized fields, read by the production traversal.
    // SDK/Animator/export integration is covered separately below, not simulated
    // by inventing a VRCAvatarDescriptor with the SDK's full type name.
    public sealed class MenuImportRecoveryTests
    {
        [TestCase(0)]
        [TestCase(256)]
        [TestCase(257)]
        public void EveryLeafIsReadWithoutTruncation(int count)
        {
            var menu = Leaves(count);
            var result = VrChatExpressionMenu.ReadMenu(menu);
            Assert.That(result.Entries.Count, Is.EqualTo(count));
            Assert.That(result.Messages, Is.Empty);
            Assert.That(menu.controls.Count, Is.EqualTo(count));
            if (count > 0) Assert.That(result.Entries.Last().Name, Is.EqualTo("Leaf " + (count - 1)));
        }

        [Test]
        public void EmptyAndNullSubmenusAddNoEntries()
        {
            var menu = Leaves(256);
            menu.controls.Add(Branch("Empty", new Menu()));
            menu.controls.Add(Branch("Null", null));
            Assert.That(VrChatExpressionMenu.ReadMenu(menu).Entries.Count, Is.EqualTo(256));
        }

        [Test]
        public void AMenuSplitIntoEightControlPagesIsReadInFull()
        {
            var menu = EightControlTree(300);
            Assert.That(AllMenus(menu).All(m => m.controls.Count <= 8), Is.True, "Each SDK-shaped menu respects the individual eight-control limit.");
            var result = VrChatExpressionMenu.ReadMenu(menu);
            Assert.That(result.Entries.Count, Is.EqualTo(300));
            Assert.That(result.Entries.Select(e => e.Id).Distinct().Count(), Is.EqualTo(300));
            Assert.That(result.Messages, Is.Empty);
        }

        [TestCase("RadialPuppet", true)]
        [TestCase("FourAxisPuppet", true)]
        [TestCase("Toggle", false)]
        public void UnsupportedPuppetsAndMissingParametersAreStillExplicitCandidates(string type, bool puppet)
        {
            var menu = Leaves(256);
            menu.controls.Add(new Control { name = "Unsupported", type = type });
            var entries = VrChatExpressionMenu.ReadMenu(menu).Entries;
            Assert.That(entries.Count, Is.EqualTo(257));
            Assert.That(entries.Last().Error, puppet ? Does.Contain("Puppet") : Does.Contain("パラメーター"));
            menu.controls.Add(new Control { name = "Another unsupported", type = type });
            Assert.That(VrChatExpressionMenu.ReadMenu(menu).Entries.Count, Is.EqualTo(258));
        }

        [Test]
        public void SharedMenuThroughFivePathsKeepsIndependentGates()
        {
            var shared = Leaves(52);
            var menu = new Menu();
            for (var i = 0; i < 5; i++) menu.controls.Add(Branch("Shared", shared, "Gate" + i, i + 1));
            var result = VrChatExpressionMenu.ReadMenu(menu);
            Assert.That(result.Entries.Count, Is.EqualTo(260));
            for (var i = 0; i < 5; i++)
            {
                var entries = result.Entries.Where(e => e.Id.StartsWith("/" + i + "/", StringComparison.Ordinal)).ToArray();
                Assert.That(entries.Length, Is.EqualTo(52));
                Assert.That(entries.All(e => e.Parameters["Gate" + i] == i + 1 && e.Parameters.Count == 2), Is.True,
                    "A shared asset's other parent gates must not leak into this route.");
            }
            Assert.That(shared.controls.Count, Is.EqualTo(52));
            Assert.That(menu.controls.Count, Is.EqualTo(5));
        }

        [Test]
        public void DuplicateBranchLabelsAreReadAsSeparateRoutes()
        {
            var menu = new Menu();
            menu.controls.Add(Branch("Same name", Leaves(129), "FirstGate", 1));
            menu.controls.Add(Branch("Same name", Leaves(129), "SecondGate", 2));
            var result = VrChatExpressionMenu.ReadMenu(menu);
            Assert.That(result.Entries.Count, Is.EqualTo(258));
            Assert.That(result.Entries.Where(e => e.Id.StartsWith("/0/", StringComparison.Ordinal))
                .All(e => e.Parameters["FirstGate"] == 1 && !e.Parameters.ContainsKey("SecondGate")), Is.True);
            Assert.That(result.Entries.Where(e => e.Id.StartsWith("/1/", StringComparison.Ordinal))
                .All(e => e.Parameters["SecondGate"] == 2 && !e.Parameters.ContainsKey("FirstGate")), Is.True);
        }

        [Test]
        public void NestedSubmenusKeepEveryAncestorParameterGate()
        {
            var nested = new Menu();
            nested.controls.Add(Branch("Faces", Leaves(2), "FaceGate", 4));
            nested.controls.Add(Branch("Clothes", Leaves(256), "ClothesGate", 5));
            var menu = new Menu(); menu.controls.Add(Branch("Outer", nested, "OuterGate", 3));
            var result = VrChatExpressionMenu.ReadMenu(menu);
            Assert.That(result.Entries.Count, Is.EqualTo(258));
            var faces = result.Entries.Where(e => e.Id.StartsWith("/0/0/", StringComparison.Ordinal)).ToArray();
            Assert.That(faces.Length, Is.EqualTo(2));
            Assert.That(faces.All(e => e.Parameters["OuterGate"] == 3 && e.Parameters["FaceGate"] == 4 && !e.Parameters.ContainsKey("ClothesGate")), Is.True);
            Assert.That(result.Entries.Except(faces).All(e => e.Parameters["OuterGate"] == 3 && e.Parameters["ClothesGate"] == 5), Is.True);
        }

        [Test]
        public void ExplicitWholeMenuOmissionCanBeClearedWithoutEditingAnySourceControls()
        {
            var menu = EightControlTree(257);
            var before = AllMenus(menu).Select(m => m.controls.ToArray()).ToArray();
            Assert.That(VrChatExpressionMenu.ReadMenu(menu, new VrChatMenuImportPolicy { SkipAll = true }).Entries, Is.Empty);
            Assert.That(VrChatExpressionMenu.ReadMenu(menu, new VrChatMenuImportPolicy()).Entries.Count, Is.EqualTo(257));
            var after = AllMenus(menu).Select(m => m.controls.ToArray()).ToArray();
            Assert.That(after.Length, Is.EqualTo(before.Length));
            for (var i = 0; i < before.Length; i++) Assert.That(after[i], Is.EqualTo(before[i]));
        }

        [Test]
        public void SixteenLevelsOfSharedSubmenusAreReadOnceEach()
        {
            // Two routes to the same next level on every level: 2^16 routes to the bottom.
            var level = Leaves(3);
            for (var i = 15; i >= 0; i--)
            {
                var menu = new Menu();
                menu.controls.Add(new Control { name = "Level " + i, type = "Toggle", parameter = new Parameter { name = "Level" }, value = i });
                menu.controls.Add(Branch("A", level, "Open", 1));
                menu.controls.Add(Branch("B", level, "Open", 1));
                level = menu;
            }
            var watch = Stopwatch.StartNew();
            var result = VrChatExpressionMenu.ReadMenu(level);
            watch.Stop();
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            var expected = Enumerable.Range(0, 16).Select(i => "Level " + i).Concat(Enumerable.Range(0, 3).Select(i => "Leaf " + i)).ToArray();
            Assert.That(result.Entries.Select(e => e.ExportName), Is.EquivalentTo(expected));
            Assert.That(result.Entries.All(e => !e.Name.Contains("B /") && !e.Name.EndsWith(" / B", StringComparison.Ordinal)), Is.True,
                "The second route to a submenu read with the same values adds nothing.");
            Assert.That(result.Messages, Is.Empty);
        }

        [Test]
        public void DeepNestingHasNoDepthLimit()
        {
            var menu = Leaves(2);
            for (var i = 0; i < 40; i++) { var wrapper = new Menu(); wrapper.controls.Add(Branch("Deep", menu)); menu = wrapper; }
            var result = VrChatExpressionMenu.ReadMenu(menu);
            Assert.That(result.Entries.Select(e => e.ExportName), Is.EqualTo(new[] { "Leaf 0", "Leaf 1" }));
            Assert.That(result.Messages, Is.Empty);
        }

        [Test]
        public void ACyclicMenuStopsAndReadsEachLeafOnce()
        {
            var a = new Menu(); var b = new Menu();
            a.controls.Add(new Control { name = "A face", type = "Toggle", parameter = new Parameter { name = "Face" }, value = 1 });
            a.controls.Add(Branch("To B", b));
            b.controls.Add(new Control { name = "B face", type = "Toggle", parameter = new Parameter { name = "Face" }, value = 2 });
            b.controls.Add(Branch("Back to A", a));
            var result = VrChatExpressionMenu.ReadMenu(a);
            Assert.That(result.Entries.Select(e => e.Name), Is.EqualTo(new[] { "A face", "To B / B face" }));
            Assert.That(result.Messages.Single(), Does.Contain("循環しているサブメニューを省略しました"));
            Assert.That(result.Messages.Single(), Does.StartWith("To B / Back to A"));
        }

        [Test]
        public void ASubmenuCutShortByACycleIsReadAgainFromARouteOutsideTheCycle()
        {
            var a = new Menu(); var s = new Menu(); var root = new Menu();
            a.controls.Add(new Control { name = "A face", type = "Toggle", parameter = new Parameter { name = "Face" }, value = 1 });
            a.controls.Add(Branch("To S", s, "Side", 1));
            s.controls.Add(Branch("To A", a, "Mode", 1));
            root.controls.Add(Branch("Open A", a));
            root.controls.Add(Branch("Open S", s, "Side", 1));
            var result = VrChatExpressionMenu.ReadMenu(root);
            var underBoth = result.Entries.Where(e => e.Name == "Open S / To A / A face").ToList();
            Assert.That(underBoth, Has.Count.EqualTo(1));
            Assert.That(underBoth[0].Parameters["Side"], Is.EqualTo(1));
            Assert.That(underBoth[0].Parameters["Mode"], Is.EqualTo(1));
        }

        [Test]
        public void ABackLinkToTheTopDoesNotMakeSharedSubmenusReadOncePerRoute()
        {
            var root = new Menu();
            var next = Leaves(2); next.controls.Add(Branch("Back", root));
            for (var level = 0; level < 16; level++)
            {
                var menu = new Menu();
                menu.controls.Add(Branch("Left", next)); menu.controls.Add(Branch("Right", next));
                menu.controls.Add(Branch("Back", root));
                next = menu;
            }
            root.controls.Add(Branch("Start", next));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = VrChatExpressionMenu.ReadMenu(root);
            Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(2));
            Assert.That(result.Entries, Has.Count.EqualTo(2));
        }

        [Test]
        public void ASharedSubmenuUnderDifferentParentValuesIsReadForEachValue()
        {
            var shared = Leaves(2);
            var menu = new Menu();
            menu.controls.Add(Branch("Smile", shared, "Mode", 1));
            menu.controls.Add(Branch("Angry", shared, "Mode", 2));
            menu.controls.Add(Branch("Smile again", shared, "Mode", 1));
            var result = VrChatExpressionMenu.ReadMenu(menu);
            Assert.That(result.Entries.Select(e => e.Name), Is.EqualTo(new[]
                { "Smile / Leaf 0", "Smile / Leaf 1", "Angry / Leaf 0", "Angry / Leaf 1" }));
            Assert.That(result.Entries.Take(2).All(e => e.Parameters["Mode"] == 1), Is.True);
            Assert.That(result.Entries.Skip(2).All(e => e.Parameters["Mode"] == 2), Is.True);
            Assert.That(result.Entries.Select(e => e.Id).Distinct().Count(), Is.EqualTo(4));
        }

        [Test]
        public void SuccessfulResultKeepsAnIndependentActionSnapshot()
        {
            var source = new GameObject("Snapshot source"); var renderer = source.AddComponent<MeshRenderer>();
            try
            {
                var action = new ExportRecoveryAction { Id = "renderer-id", Kind = ExportRecoveryActionKind.ExcludeHiddenRenderer, Renderer = renderer };
                var options = new ExportRecoveryOptions(); options.Actions.Add(action);
                var diagnostic = new ExportRecoveryDiagnostic { Id = "diagnostic-id", Code = "hidden-renderer", Stage = "変換",
                    Target = "Original label", Reason = "Original reason", Remedy = "Original remedy", LostEffect = "Original lost effect", Action = action };
                var result = new ExportRecoverySession.SuccessfulAttempt(new byte[] { 1 }, new List<string>(), options,
                    ExportRecoverySourceStamp.Capture(source), new[] { diagnostic });
                action.Id = "changed-id"; action.Renderer = null; action.Kind = ExportRecoveryActionKind.DisableAudioLink;
                diagnostic.Target = "Changed label"; diagnostic.LostEffect = "Changed effect"; options.Actions.Clear();
                var captured = result.Options.Actions.Single();
                Assert.That(captured.Id, Is.EqualTo("renderer-id")); Assert.That(captured.Renderer, Is.SameAs(renderer));
                Assert.That(captured.Kind, Is.EqualTo(ExportRecoveryActionKind.ExcludeHiddenRenderer));
                Assert.That(captured, Is.Not.SameAs(action));
                Assert.That(result.Diagnostics.Single().Action, Is.SameAs(captured));
                Assert.That(result.Diagnostics.Single().Target, Is.EqualTo("Original label"));
                Assert.That(result.Diagnostics.Single().LostEffect, Is.EqualTo("Original lost effect"));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        private static Menu Leaves(int count)
        {
            var menu = new Menu();
            for (var i = 0; i < count; i++) menu.controls.Add(new Control { name = "Leaf " + i, type = "Toggle", parameter = new Parameter { name = "Face" }, value = i });
            return menu;
        }
        private static Control Branch(string name, Menu menu, string parameter = null, float value = 0)
            => new Control { name = name, type = "SubMenu", subMenu = menu, parameter = parameter == null ? null : new Parameter { name = parameter }, value = value };
        private static Menu EightControlTree(int count)
        {
            if (count <= 8) return Leaves(count);
            var menu = new Menu(); var remaining = count;
            while (remaining > 0)
            {
                var group = Math.Min(count <= 64 ? 8 : 64, remaining);
                menu.controls.Add(Branch("Page " + menu.controls.Count, EightControlTree(group)));
                remaining -= group;
            }
            return menu;
        }
        private static IEnumerable<Menu> AllMenus(Menu root)
        { yield return root; foreach (var control in root.controls.Where(c => c.type == "SubMenu" && c.subMenu != null)) foreach (var child in AllMenus(control.subMenu)) yield return child; }
        private sealed class Menu { public List<Control> controls = new List<Control>(); }
        private sealed class Parameter { public string name; }
        private sealed class Control { public string name, type; public Parameter parameter; public float value; public Menu subMenu; }
    }
}
