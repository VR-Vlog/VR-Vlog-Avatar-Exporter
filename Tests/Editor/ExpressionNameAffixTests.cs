using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter.Tests
{
    public class ExpressionNameAffixTests
    {
        static string[] Split(string names) => names.Split('|');

        [TestCase("face_smile|face_sad|face_angry", "smile|sad|angry")]
        [TestCase("avatar_face_smile|avatar_face_sad", "smile|sad")]
        [TestCase("smile_L|sad_L", "smile|sad")]
        [TestCase("smile_face_L|whistle_face_L", "smile|whistle")]
        [TestCase("face_smile_L|face_sad_L", "smile|sad")]
        [TestCase("face-smile|face-sad", "smile|sad")]
        [TestCase("face.smile|face.sad", "smile|sad")]
        [TestCase("face smile|face sad", "smile|sad")]
        [TestCase("face_smile|face_smile|face_sad", "smile|smile|sad")]
        [TestCase("face_x_1|face_x_2", "x_1|x_2")]
        public void ASharedPrefixOrSuffixIsLeftOutByWholeWords(string names, string expected) =>
            Assert.That(ExpressionNameAffix.Trim(Split(names)), Is.EqualTo(Split(expected)));

        [TestCase("face_smile")]
        [TestCase("face_smile|face_smile")]
        [TestCase("face_|face_sad")]
        [TestCase("face|face_sad")]
        [TestCase("smile_1|smile_2")]
        [TestCase("face_１|face_２")]
        [TestCase("faceSmile|faceSad")]
        [TestCase("_smile|_sad")]
        [TestCase("smile|sad")]
        public void NamesStayWhenNothingCanBeLeftOut(string names) =>
            Assert.That(ExpressionNameAffix.Trim(Split(names)), Is.EqualTo(Split(names)));

        [Test]
        public void NamesStayWhenAShorterNameIsAlreadyTaken()
        {
            var names = Split("face_smile|face_sad");
            Assert.That(ExpressionNameAffix.Trim(names, name => name == "smile"), Is.EqualTo(names));
        }

        [Test]
        public void TheLongerValidPartGoesWhenBothCannot()
        {
            // Without both one name would be digits only; the prefix is longer.
            Assert.That(ExpressionNameAffix.Trim(Split("face_1_L|face_smile_L")), Is.EqualTo(Split("1_L|smile_L")));
        }

        // Add writes the shorter names everywhere the file names a face: the
        // custom expressions and the expression animation extension (its list
        // and its animations), and hands the same names on through
        // RegisteredName and MergedInto (gestures, the result list) and the
        // emotion candidates, which still read the original names.
        [Test]
        public void AddWritesTheShorterNamesEverywhere()
        {
            var smile = new VrmMenuExpressions.Expression { Name = "face_smile",
                Animation = new ExpressionAnimationData { Duration = 1, Loop = false } };
            var channel = new ExpressionAnimationData.Channel { Curve = Linear(1, 100) };
            channel.Points.Add(new ExpressionAnimationData.Point { Value = 0 });
            channel.Points.Add(new ExpressionAnimationData.Point { Value = 100, Target = "A" });
            smile.Animation.Channels.Add(channel);
            var expressions = new List<VrmMenuExpressions.Expression> { smile, Face("face_wave", "B"), Face("face_other", "B") };
            var bytes = VrmMenuExpressions.Add(Output("A", "B"), expressions);
            var json = GlbDocument.Read(bytes).Json;
            Assert.That(Custom(json).Keys, Is.EquivalentTo(new[] { "smile", "wave" }));
            Assert.That(VrmMenuExpressions.RegisteredNames(bytes), Is.EquivalentTo(new[] { "smile", "wave" }));
            var document = ExpressionAnimationData.ReadDocument(((Dictionary<string, object>)json["extensions"])[ExpressionAnimationData.Extension]);
            Assert.That(document.Animations.Select(animation => animation.Expression), Is.EqualTo(new[] { "smile" }));
            Assert.That(expressions.Select(e => e.RegisteredName), Is.EqualTo(new[] { "smile", "wave", null }));
            Assert.That(expressions.Select(e => e.MergedInto), Is.EqualTo(new[] { null, null, "wave" }));
            Assert.That(VrmMenuExpressions.EmotionCandidates(expressions),
                Is.EquivalentTo(new Dictionary<string, string> { ["smile"] = "face_smile", ["wave"] = "face_wave" }));
        }

        static ExpressionAnimationData.Curve Linear(double duration, double value)
        {
            var slope = value / duration;
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 0, InTangent = slope, OutTangent = slope });
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = duration, Value = value, InTangent = slope, OutTangent = slope });
            return curve;
        }

        // "happy" and "sad" are VRM presets; the app would drop the extension.
        [Test]
        public void AddKeepsTheNamesWhenAShorterNameIsAPreset()
        {
            var expressions = new List<VrmMenuExpressions.Expression> { Face("face_happy", "A"), Face("face_sad", "B") };
            var json = GlbDocument.Read(VrmMenuExpressions.Add(Output("A", "B"), expressions)).Json;
            Assert.That(Custom(json).Keys, Is.EquivalentTo(new[] { "face_happy", "face_sad" }));
        }

        [Test]
        public void AddKeepsASingleName()
        {
            var expressions = new List<VrmMenuExpressions.Expression> { Face("face_smile", "A") };
            var json = GlbDocument.Read(VrmMenuExpressions.Add(Output("A"), expressions)).Json;
            Assert.That(Custom(json).Keys, Is.EquivalentTo(new[] { "face_smile" }));
            Assert.That(expressions.Single().RegisteredName, Is.EqualTo("face_smile"));
        }

        static VrmMenuExpressions.Expression Face(string name, string target)
        {
            var expression = new VrmMenuExpressions.Expression { Name = name };
            expression.Targets.Add(target);
            return expression;
        }

        static byte[] Output(params string[] targets)
        {
            var names = targets.Cast<object>().ToList();
            var root = new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["nodes"] = new List<object> { new Dictionary<string, object> { ["name"] = "Face", ["mesh"] = 0L } },
                ["meshes"] = new List<object> { new Dictionary<string, object>
                    { ["extras"] = new Dictionary<string, object> { ["targetNames"] = names },
                      ["primitives"] = new List<object> { new Dictionary<string, object>
                          { ["targets"] = names.Select(_ => (object)new Dictionary<string, object>()).ToList() } } } },
                ["extensions"] = new Dictionary<string, object> { ["VRMC_vrm"] = new Dictionary<string, object>
                    { ["expressions"] = new Dictionary<string, object> { ["custom"] = new Dictionary<string, object>() } } }
            };
            return GlbDocument.Create(root, Array.Empty<byte>()).Write();
        }

        static Dictionary<string, object> Custom(Dictionary<string, object> json) =>
            (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)json["extensions"])["VRMC_vrm"])["expressions"])["custom"];
    }
}
