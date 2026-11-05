using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The shared merge behind the FaceEmo common face and the baker. What a
    // missing path or a null renderer does is the caller's choice: the baker's
    // lookup throws, FaceEmo keeps an unplaced channel but never compares it.
    public sealed class ExpressionChannelMergeTests
    {
        GameObject owner;
        SkinnedMeshRenderer renderer;

        [SetUp] public void SetUp()
        {
            owner = new GameObject("Face", typeof(SkinnedMeshRenderer));
            renderer = owner.GetComponent<SkinnedMeshRenderer>();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(owner);

        static VrChatExpressionMenu.Entry Entry(params (string Path, float Weight)[] values)
        {
            var entry = new VrChatExpressionMenu.Entry { Name = "Smile" };
            foreach (var value in values)
                entry.Values.Add(new VrChatExpressionMenu.MorphValue { Path = value.Path, Shape = "Open", Weight = value.Weight });
            return entry;
        }

        static Exception Conflict(ExpressionChannelMerge.Channel channel) => new InvalidOperationException("conflict: " + channel.Path);

        [Test]
        public void MissingNullAndDuplicatePathsFollowTheCallersChoice()
        {
            // Missing: the caller's lookup error reaches the caller unchanged.
            var lookup = new Dictionary<string, SkinnedMeshRenderer> { ["Body"] = renderer };
            ExpressionChannelMerge.Channel Lookup(string path) => new ExpressionChannelMerge.Channel { Renderer = lookup[path] };
            Assert.Throws<KeyNotFoundException>(() => ExpressionChannelMerge.Groups(Entry(("Missing", 10)), Lookup, Conflict).ToArray());

            // Null: skipped channels are neither compared nor grouped; without
            // the skip, the same disagreement is a conflict.
            var unplaced = Entry(("Unplaced", 10), ("Unplaced", 20));
            ExpressionChannelMerge.Channel Unresolved(string path) => new ExpressionChannelMerge.Channel();
            Assert.That(ExpressionChannelMerge.Groups(unplaced, Unresolved, Conflict, skipUnresolved: true), Is.Empty);
            Assert.That(() => ExpressionChannelMerge.Groups(unplaced, Unresolved, Conflict).ToArray(),
                Throws.InvalidOperationException.With.Message.EqualTo("conflict: Unplaced"));

            // Duplicate: two authoring paths on one final renderer merge into
            // one group led by the first path, and must agree.
            ExpressionChannelMerge.Channel Merged(string path) => new ExpressionChannelMerge.Channel { Renderer = renderer };
            var pairs = new List<(string, string)>();
            var groups = ExpressionChannelMerge.Groups(Entry(("Front", 30), ("Back", 30)), Merged, Conflict,
                sameGroup: (first, other) => pairs.Add((first.Path, other.Path))).ToArray();
            Assert.That(groups.Select(group => (group.Path, group.Weight)), Is.EqualTo(new[] { ("Front", 30f) }));
            Assert.That(pairs, Is.EqualTo(new[] { ("Front", "Back") }));
            Assert.That(() => ExpressionChannelMerge.Groups(Entry(("Front", 30), ("Back", 40)), Merged, Conflict, skipUnresolved: true).ToArray(),
                Throws.InvalidOperationException.With.Message.EqualTo("conflict: Front"));
        }
    }
}
