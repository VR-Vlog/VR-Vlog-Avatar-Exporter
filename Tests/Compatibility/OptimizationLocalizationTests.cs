using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class OptimizationLocalizationTests
    {
        [System.Serializable]
        sealed class LocaleEntry { public string source; public string translated; }

        [System.Serializable]
        sealed class LocaleTable { public LocaleEntry[] entries; }

        // Characters that do not belong in each table. English has no CJK characters or full-width
        // punctuation at all. Korean has no kana or kanji. Chinese has no kana; the kanji, 「」 and the
        // middle dot ・ are its own, so only kana other than ・ and half-width kana are left.
        [TestCase("en", @"[　-〿぀-ヿ一-鿿＀-￯]")]
        [TestCase("ko", @"[぀-ヿ一-鿿ｦ-ﾟ]")]
        [TestCase("zh-Hans", @"[぀-ヺー-ヿｦ-ﾟ]")]
        [TestCase("zh-Hant", @"[぀-ヺー-ヿｦ-ﾟ]")]
        public void TranslationsHoldNoJapaneseCharacters(string locale, string japanese)
        {
            var path = UnityEditor.AssetDatabase.FindAssets("ExporterLocale_" + locale + " t:TextAsset")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Single(found => found.EndsWith("/Editor/Locales/ExporterLocale_" + locale + ".json", System.StringComparison.Ordinal));
            var table = UnityEngine.JsonUtility.FromJson<LocaleTable>(System.IO.File.ReadAllText(path));
            Assert.That(table.entries.Length, Is.GreaterThan(1000), locale + ": the table was not read");
            var pattern = new System.Text.RegularExpressions.Regex(japanese);
            var bad = table.entries.Where(entry => pattern.IsMatch(entry.translated)).Select(entry => entry.translated).ToList();
            Assert.That(bad, Is.Empty, locale + ": Japanese left in the translations");
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
