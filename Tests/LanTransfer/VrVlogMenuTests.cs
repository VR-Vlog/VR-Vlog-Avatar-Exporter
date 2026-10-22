using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    // The "VR Vlog" menu as Unity registers it: no entry opens the app preview, and the
    // entries that act on an avatar are greyed out without exactly one avatar root.
    public sealed class VrVlogMenuTests
    {
        private const string MenuBar = "VR Vlog/", Hierarchy = "GameObject/VR Vlog/";
        private const string Export = "VRMを書き出す...";
        private static readonly string[] Entries = { "アプリに転送（かんたん）...", "アプリに転送（通常）...", Export };

        private static (MethodInfo Method, MenuItem Item)[] Registered(string prefix) => TypeCache.GetMethodsWithAttribute<MenuItem>()
            .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().Select(item => (method, item)))
            .Where(entry => entry.item.menuItem.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

        [Test]
        public void NoMenuOpensTheAppPreview()
        {
            Assert.That(TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .Where(item => item.menuItem.Contains("アプリでの見え方")), Is.Empty);
        }

        [Test]
        public void EveryEntryActingOnAnAvatarIsGreyWithoutExactlyOneAvatarRoot()
        {
            // The menu bar export opens the window with nothing chosen, as it always did.
            var validated = Registered(MenuBar).Concat(Registered(Hierarchy)).Where(entry => entry.Item.validate).ToArray();
            Assert.That(validated.Select(entry => entry.Item.menuItem), Is.EquivalentTo(Entries.Select(name => Hierarchy + name)
                .Concat(Entries.Where(name => name != Export).Select(name => MenuBar + name))));
            var selection = Selection.objects;
            var first = new GameObject("vrvlog-menu-first", typeof(Animator));
            var second = new GameObject("vrvlog-menu-second", typeof(Animator));
            var plain = new GameObject("vrvlog-menu-plain");
            var nested = new GameObject("vrvlog-menu-nested", typeof(Animator));
            nested.transform.SetParent(first.transform);
            try
            {
                foreach (var (method, item) in validated)
                {
                    bool Enabled(params Object[] selected)
                    {
                        Selection.objects = selected;
                        return (bool)method.Invoke(null, null);
                    }
                    Assert.That(Enabled(), Is.False, item.menuItem);
                    Assert.That(Enabled(plain), Is.False, item.menuItem);
                    Assert.That(Enabled(nested), Is.False, item.menuItem);
                    Assert.That(Enabled(first, second), Is.False, item.menuItem);
                    // The transfers are listed only in versions that transfer.
                    var expected = item.menuItem.EndsWith(Export, StringComparison.Ordinal) || CloudTransferAvailability.Enabled;
                    Assert.That(Enabled(first), Is.EqualTo(expected), item.menuItem);
                }
            }
            finally
            {
                Selection.objects = selection;
                Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(plain);
            }
        }
    }
}
