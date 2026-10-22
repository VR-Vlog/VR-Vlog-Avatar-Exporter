using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEngine;
using UnityEngine.TestTools;
using Session = VRVlog.LilToonExporter.ExperimentalExpressionCaptureSession;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExpressionPresetStoreTests
    {
        string path;
        string previous;

        [SetUp]
        public void SetUp()
        {
            path = Path.Combine(Path.GetTempPath(), "vrvlog-presets-" + Guid.NewGuid().ToString("N") + ".json");
            previous = ExpressionPresetStore.PathOverride;
            ExpressionPresetStore.PathOverride = path;
        }

        [TearDown]
        public void TearDown()
        {
            ExpressionPresetStore.PathOverride = previous;
            if (File.Exists(path)) File.Delete(path);
        }

        static Session.PresetAssignment[] Angry() => new[] { Session.PresetAssignment.None(ExpressionPreset.angry) };

        [Test]
        public void AFileThatCannotBeReadIsNotOverwrittenBySavingAnotherAvatar()
        {
            // Another avatar's choices, cut off in the middle.
            const string damaged = "{\"Version\":1,\"Avatars\":[{\"Avatar\":\"avatar-b\",\"Presets\":[{\"Preset\":\"happy\",\"ClipGuid\":\"\",\"ClipLocalId\":0,\"Path\":\"Front\",\"Shape\":\"Smi";
            File.WriteAllText(path, damaged, Encoding.UTF8);
            var before = File.ReadAllBytes(path);
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(path)));
            ExpressionPresetStore.Save("avatar-a", Angry());
            ExpressionPresetStore.Save("avatar-a", Angry());
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), "Other avatars' choices may still be in the file.");
            Assert.That(ExpressionPresetStore.Load("avatar-a", out _), Is.Empty, "The board still opens with nothing chosen.");

            var large = "{\"Version\":1,\"Avatars\":[]}" + new string(' ', 4 * 1024 * 1024);
            File.WriteAllText(path, large, Encoding.UTF8);
            before = File.ReadAllBytes(path);
            ExpressionPresetStore.Save("avatar-a", Angry());
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), "A file over the size limit is not replaced either.");
        }

        [Test]
        public void MissingEmptyAndReadableFilesAreStillSaved()
        {
            ExpressionPresetStore.Save("avatar-a", Angry());
            Assert.That(File.Exists(path), Is.True, "A missing file is created.");
            ExpressionPresetStore.Save("avatar-b", new[] { Session.PresetAssignment.None(ExpressionPreset.sad) });
            Assert.That(ExpressionPresetStore.Load("avatar-a", out _).Single().Preset, Is.EqualTo("angry"));
            Assert.That(ExpressionPresetStore.Load("avatar-b", out _).Single().Preset, Is.EqualTo("sad"));

            File.WriteAllText(path, "  \n", Encoding.UTF8);
            ExpressionPresetStore.Save("avatar-a", Angry());
            Assert.That(ExpressionPresetStore.Load("avatar-a", out _).Single().Preset, Is.EqualTo("angry"), "An empty file is replaced.");
        }
    }
}
