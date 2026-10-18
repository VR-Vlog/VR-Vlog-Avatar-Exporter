using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class DeterministicNamesTests
    {
        GameObject root;
        readonly List<GameObject> copies = new List<GameObject>();
        readonly List<Object> owned = new List<Object>();
        readonly List<Mesh> generated = new List<Mesh>();

        [SetUp] public void SetUp() => root = new GameObject("avatar");
        [TearDown] public void TearDown()
        {
            foreach (var value in copies) if (value != null) Object.DestroyImmediate(value);
            Object.DestroyImmediate(root);
            foreach (var value in generated) if (value != null) Object.DestroyImmediate(value);
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            copies.Clear(); generated.Clear(); owned.Clear();
        }

        [Test] public void SameInputsGiveTheSameName()
        {
            Assert.That(DeterministicNames.Hash("Blink", "Body", "eye_close", "blink"),
                Is.EqualTo(DeterministicNames.Hash("Blink", "Body", "eye_close", "blink")));
            // Pinned so that a change of the input encoding is a visible decision.
            Assert.That(DeterministicNames.Hash("Blink", "Body", "eye_close", "blink"), Is.EqualTo("2af70eb0506f1cdc52ec00b717b83d83"));
        }

        [Test] public void PathShapeAndPurposeEachChangeTheName()
        {
            var name = DeterministicNames.Hash("Blink", "Body", "eye_close", "blink");
            Assert.That(DeterministicNames.Hash("Blink", "Head/Body", "eye_close", "blink"), Is.Not.EqualTo(name));
            Assert.That(DeterministicNames.Hash("Blink", "Body", "eye_close_2", "blink"), Is.Not.EqualTo(name));
            Assert.That(DeterministicNames.Hash("Blink", "Body", "eye_close", "blinkLeft"), Is.Not.EqualTo(name));
            Assert.That(DeterministicNames.Hash("BlinkNone", "Body", "eye_close", "blink"), Is.Not.EqualTo(name));
        }

        [Test] public void PartBoundariesArePartOfTheInput()
        {
            Assert.That(DeterministicNames.Hash("a", "bc"), Is.Not.EqualTo(DeterministicNames.Hash("ab", "c")));
            Assert.That(DeterministicNames.Hash("a", ""), Is.Not.EqualTo(DeterministicNames.Hash("a")));
        }

        [Test] public void HashHasTheLengthOfTheReplacedGuid()
        {
            Assert.That(DeterministicNames.Hash("Menu", "Body", "smile"), Does.Match("^[0-9a-f]{32}$"));
            Assert.That(DeterministicNames.Hash(), Does.Match("^[0-9a-f]{32}$"));
        }

        [Test] public void RepeatedNamesGetNumberedSuffixesInOrder()
        {
            var used = new HashSet<string> { "taken" };
            Assert.That(DeterministicNames.Unique("free", used), Is.EqualTo("free"));
            Assert.That(DeterministicNames.Unique("taken", used), Is.EqualTo("taken_2"));
            Assert.That(DeterministicNames.Unique("taken", used), Is.EqualTo("taken_3"));
            Assert.That(DeterministicNames.Unique("free", used), Is.EqualTo("free_2"));
            Assert.That(used, Is.EquivalentTo(new[] { "taken", "free", "taken_2", "taken_3", "free_2" }));
        }

        [Test] public void BlinkBakeGivesTheSameNamesOnEveryExport()
        {
            var skin = Skin("close", "close_L", "close_R");
            var options = new BlinkExportOptions { Mode = BlinkExportMode.Manual };
            options.Both.Add(new BlinkShapeBinding { Renderer = skin, Shape = "close", Weight = 100 });
            options.Left.Add(new BlinkShapeBinding { Renderer = skin, Shape = "close_L", Weight = 100 });
            options.Right.Add(new BlinkShapeBinding { Renderer = skin, Shape = "close_R", Weight = 100 });
            using var resolved = BlinkExportSession.Resolve(root, options);
            var first = BakeBlink(resolved);
            var second = BakeBlink(resolved);
            Assert.That(first, Has.Length.EqualTo(3));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.Unique);
            Assert.That(first.All(name => Regex.IsMatch(name, "^__VRVlog_Blink_[0-9a-f]{32}$")), Is.True, string.Join(", ", first));
            skin.gameObject.name = "head";
            Assert.That(BakeBlink(resolved).Intersect(first), Is.Empty, "The renderer path is part of the name.");
        }

        [Test] public void DisabledBlinkGivesTheSameNoOpNameOnEveryExport()
        {
            Skin("Blink");
            using var resolved = BlinkExportSession.Resolve(root, new BlinkExportOptions { Mode = BlinkExportMode.None });
            var first = BakeBlink(resolved);
            Assert.That(first.Distinct().Single(), Does.Match("^__VRVlog_BlinkNone_[0-9a-f]{32}$"));
            Assert.That(BakeBlink(resolved), Is.EqualTo(first));
        }

        SkinnedMeshRenderer Skin(params string[] names)
        {
            var go = new GameObject("face"); go.transform.SetParent(root.transform, false);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            foreach (var name in names)
                mesh.AddBlendShapeFrame(name, 100, new[] { Vector3.up * .2f, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
            owned.Add(mesh);
            var skin = go.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
            return skin;
        }

        string[] BakeBlink(BlinkExportSession resolved)
        {
            var copy = Object.Instantiate(root); copies.Add(copy);
            using var session = resolved.ForClone(root, copy);
            session.Bake(copy, generated);
            return session.Slots.SelectMany(slot => slot.Select(binding => binding.Shape)).ToArray();
        }
    }
}
