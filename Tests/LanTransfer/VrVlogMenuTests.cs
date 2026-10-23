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
    // The "VR Vlog" menu as Unity registers it: both menus list the two transfers and nothing
    // else, no entry opens the app preview, and every entry is greyed out without exactly one
    // avatar root. The Project window's context menu (Assets) lists the Hierarchy's entries
    // for a prefab.
    public sealed class VrVlogMenuTests
    {
        private const string MenuBar = "VR Vlog/", Hierarchy = "GameObject/VR Vlog/", Assets = "Assets/VR Vlog/";
        // The check of a .vrm file in the Assets menu does not act on an avatar.
        private const string ValidateVrm = Assets + "Validate lilToon VRM";
        private static readonly string[] Entries = { "アプリに転送（かんたん）...", "アプリに転送（通常）..." };

        private static (MethodInfo Method, MenuItem Item)[] Registered(string prefix) => TypeCache.GetMethodsWithAttribute<MenuItem>()
            .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().Select(item => (method, item)))
            .Where(entry => entry.item.menuItem.StartsWith(prefix, StringComparison.Ordinal) && entry.item.menuItem != ValidateVrm).ToArray();

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
            foreach (var prefix in new[] { MenuBar, Hierarchy, Assets })
            {
                // The two transfers in order, 1 apart, so no separator is drawn between them.
                var listed = Registered(prefix).Where(entry => !entry.Item.validate).OrderBy(entry => entry.Item.priority).ToArray();
                Assert.That(listed.Select(entry => entry.Item.menuItem), Is.EqualTo(Entries.Select(name => prefix + name)), prefix);
                Assert.That(listed[1].Item.priority - listed[0].Item.priority, Is.EqualTo(1), prefix);
            }
            var validated = Registered(MenuBar).Concat(Registered(Hierarchy)).Concat(Registered(Assets)).Where(entry => entry.Item.validate).ToArray();
            Assert.That(validated.Select(entry => entry.Item.menuItem), Is.EquivalentTo(Entries.Select(name => Hierarchy + name)
                .Concat(Entries.Select(name => MenuBar + name)).Concat(Entries.Select(name => Assets + name))));
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
                    Assert.That(Enabled(first), Is.EqualTo(CloudTransferAvailability.Enabled), item.menuItem);
                }
            }
            finally
            {
                Selection.objects = selection;
                Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(plain);
            }
        }

        // The same entries at the same relative priorities as the Hierarchy, each running the
        // Hierarchy entry's method; the .vrm check comes last, after a separator.
        [Test]
        public void TheProjectWindowListsTheHierarchyEntries()
        {
            (string Name, int Priority, MethodInfo Method)[] Listed(string prefix) => Registered(prefix).Where(entry => !entry.Item.validate)
                .Select(entry => (entry.Item.menuItem.Substring(prefix.Length), entry.Item.priority, entry.Method)).OrderBy(entry => entry.priority).ToArray();
            var hierarchy = Listed(Hierarchy);
            var assets = Listed(Assets);
            Assert.That(assets.Select(entry => entry.Name), Is.EqualTo(hierarchy.Select(entry => entry.Name)));
            Assert.That(assets.Select(entry => entry.Priority - assets[0].Priority), Is.EqualTo(hierarchy.Select(entry => entry.Priority - hierarchy[0].Priority)));
            Assert.That(assets.Select(entry => entry.Method), Is.EqualTo(hierarchy.Select(entry => entry.Method)));
            var validate = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .Single(item => item.menuItem == ValidateVrm && !item.validate);
            Assert.That(validate.priority, Is.GreaterThan(assets.Last().Priority + 10), "A separator, then the .vrm check.");
        }

        // In the Project window: the root of an avatar prefab, not a prefab without an
        // Animator, an object inside an avatar prefab, two prefabs or a folder.
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
                var validated = Registered(Assets).Where(entry => entry.Item.validate).ToArray();
                Assert.That(validated.Length, Is.EqualTo(Entries.Length));
                foreach (var (method, item) in validated)
                {
                    bool Enabled(params Object[] selected)
                    {
                        Selection.objects = selected;
                        return (bool)method.Invoke(null, null);
                    }
                    // The transfers are listed only in versions that transfer.
                    Assert.That(Enabled(avatar), Is.EqualTo(CloudTransferAvailability.Enabled), item.menuItem);
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
