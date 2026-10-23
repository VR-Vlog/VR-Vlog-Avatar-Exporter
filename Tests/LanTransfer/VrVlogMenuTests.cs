using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    // The "VR Vlog" menu as Unity registers it: no entry opens the app preview, and the
    // entries that act on an avatar are greyed out without exactly one avatar root. The
    // Project window's context menu (Assets) lists the Hierarchy's entries for a prefab.
    public sealed class VrVlogMenuTests
    {
        private const string MenuBar = "VR Vlog/", Hierarchy = "GameObject/VR Vlog/", Assets = "Assets/VR Vlog/";
        // The check of a .vrm file in the Assets menu does not act on an avatar.
        private const string ValidateVrm = Assets + "Validate lilToon VRM";
        private const string Export = "VRMを書き出す...";
        private static readonly string[] Entries = { "アプリに転送（かんたん）...", "アプリに転送（通常）...", Export };

        private static (MethodInfo Method, MenuItem Item)[] Registered(string prefix) => TypeCache.GetMethodsWithAttribute<MenuItem>()
            .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().Select(item => (method, item)))
            .Where(entry => entry.item.menuItem.StartsWith(prefix, StringComparison.Ordinal) && entry.item.menuItem != ValidateVrm).ToArray();

        private static (MethodInfo Method, MenuItem Item)[] Validated() =>
            Registered(MenuBar).Concat(Registered(Hierarchy)).Concat(Registered(Assets)).Where(entry => entry.Item.validate).ToArray();

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
            var validated = Validated();
            Assert.That(validated.Select(entry => entry.Item.menuItem), Is.EquivalentTo(Entries.Select(name => Hierarchy + name)
                .Concat(Entries.Select(name => Assets + name))
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

        // The same entries in the same order and with the same separators as the Hierarchy,
        // each running the Hierarchy entry's method; the .vrm check comes last.
        [Test]
        public void TheProjectWindowListsTheHierarchyEntries()
        {
            (string Name, int Priority, MethodInfo Method)[] Entries(string prefix) => Registered(prefix).Where(entry => !entry.Item.validate)
                .Select(entry => (entry.Item.menuItem.Substring(prefix.Length), entry.Item.priority, entry.Method)).OrderBy(entry => entry.priority).ToArray();
            var hierarchy = Entries(Hierarchy);
            var assets = Entries(Assets);
            Assert.That(assets.Select(entry => entry.Name), Is.EqualTo(hierarchy.Select(entry => entry.Name)));
            Assert.That(assets.Select(entry => entry.Priority - assets[0].Priority), Is.EqualTo(hierarchy.Select(entry => entry.Priority - hierarchy[0].Priority)));
            Assert.That(assets.Select(entry => entry.Method), Is.EqualTo(hierarchy.Select(entry => entry.Method)));
            var validate = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .Single(item => item.menuItem == ValidateVrm && !item.validate);
            Assert.That(validate.priority, Is.GreaterThan(assets.Last().Priority + 10), "A separator, then the .vrm check.");
        }

        // In the Project window: the root of an avatar prefab, not a prefab without an
        // Animator, an object inside an avatar prefab, or two prefabs.
        [Test]
        public void TheProjectWindowEntriesNeedOneAvatarPrefab()
        {
            var folder = "Assets/VRVlogMenuPrefabs-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var selection = Selection.objects;
            var source = new GameObject("vrvlog-menu-avatar", typeof(Animator));
            new GameObject("vrvlog-menu-inner", typeof(Animator)).transform.SetParent(source.transform);
            var plainSource = new GameObject("vrvlog-menu-plain");
            try
            {
                var avatar = PrefabUtility.SaveAsPrefabAsset(source, folder + "/avatar.prefab");
                var other = PrefabUtility.SaveAsPrefabAsset(source, folder + "/other.prefab");
                var plain = PrefabUtility.SaveAsPrefabAsset(plainSource, folder + "/plain.prefab");
                var inner = avatar.transform.GetChild(0).gameObject;
                foreach (var (method, item) in Validated().Where(entry => entry.Item.menuItem.StartsWith(Assets, StringComparison.Ordinal)))
                {
                    bool Enabled(params Object[] selected)
                    {
                        Selection.objects = selected;
                        return (bool)method.Invoke(null, null);
                    }
                    var expected = item.menuItem.EndsWith(Export, StringComparison.Ordinal) || CloudTransferAvailability.Enabled;
                    Assert.That(Enabled(avatar), Is.EqualTo(expected), item.menuItem);
                    Assert.That(Enabled(plain), Is.False, item.menuItem);
                    Assert.That(Enabled(inner), Is.False, item.menuItem);
                    Assert.That(Enabled(avatar, other), Is.False, item.menuItem);
                    Assert.That(Enabled(AssetDatabase.LoadAssetAtPath<Object>(folder)), Is.False, item.menuItem);
                }
            }
            finally
            {
                Selection.objects = selection;
                Object.DestroyImmediate(source); Object.DestroyImmediate(plainSource);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
