using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class OptimizationLocalizationTests
    {
        [Test]
        public void EnglishTranslationsHoldNoJapaneseCharacters()
        {
            var guid = UnityEditor.AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var text = System.IO.File.ReadAllText(path);
            var bad = new System.Collections.Generic.List<string>();
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, "\"translated\":\"((?:[^\"\\\\]|\\\\.)*)\""))
                if (System.Text.RegularExpressions.Regex.IsMatch(match.Groups[1].Value, "[぀-ヿ一-鿿]")) bad.Add(match.Groups[1].Value);
            Assert.That(bad, Is.Empty, "Japanese left in the English translations");
        }

        [TestCase("en")]
        [TestCase("ko")]
        [TestCase("zh-Hans")]
        [TestCase("zh-Hant")]
        public void OptimizationAndPermanentFxErrorsTranslateWithoutChangingAvatarLabels(string locale)
        {
            var localeField = typeof(ExporterLocalization).GetField("_locale", BindingFlags.NonPublic | BindingFlags.Static);
            var messagesField = typeof(ExporterLocalization).GetField("_messages", BindingFlags.NonPublic | BindingFlags.Static);
            var previousLocale = localeField.GetValue(null);
            var previousMessages = messagesField.GetValue(null);
            try
            {
                localeField.SetValue(null, locale);
                messagesField.SetValue(null, null);
                const string label = "顔/Body/EyeShrink(Clone)";
                foreach (var prefix in new[]
                {
                    "最適化で書き出し用の表情が失われました: ",
                    "最適化で元のVRM表情の適用量が衝突しました: ",
                    "マテリアルの移動先を解決できません: ",
                    "変形しない表情の出力先メッシュがありません: ",
                    "常時適用するFXのBlendShapeが見つかりません: ",
                    "追跡表情の対象が最終VRMにありません: ",
                    "FaceEmoの表情アニメーションが準備前の登録内容と一致していません: ",
                    "衣装・体形の処理後にFaceEmoの表情対象が残っていません: ",
                    "FaceEmoの表情参照が処理後に重複しています: "
                })
                {
                    var translatedPrefix = ExporterLocalization.T(prefix);
                    Assert.That(translatedPrefix, Is.Not.EqualTo(prefix), locale + ": missing error translation");
                    var translated = ExporterLocalization.T(prefix + label);
                    Assert.That(translated, Is.EqualTo(translatedPrefix + label));
                    Assert.That(translated, Does.EndWith(label), "Authored mesh and shape labels must remain identifiable.");
                }
                const string faceEmoFailure = "FaceEmoの表情参照は書き出し用コピーと一致していません。";
                Assert.That(ExporterLocalization.T(faceEmoFailure), Is.Not.EqualTo(faceEmoFailure));
            }
            finally
            {
                localeField.SetValue(null, previousLocale);
                messagesField.SetValue(null, previousMessages);
            }
        }
    }
}
