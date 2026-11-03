using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    // Opening a scene unloads the assets no scene uses. The app transfer panel keeps drawing with its own
    // font after that, so the font's material and every atlas texture, including the ones the font adds
    // while drawing, have to survive the unload together with the font.
    public sealed class AppTransferFontLifetimeTests
    {
        [Test]
        public void TextFontKeepsMaterialAndAtlasesWhenASceneOpens()
        {
            var fonts = PanelFonts();
            var text = new StringBuilder();
            for (var c = 0x4E00; c < 0x4E00 + 300; c++) text.Append((char)c);
            foreach (var font in fonts) font.TryAddCharacters(text.ToString(), out _);
            Assert.That(fonts[0].atlasTextureCount, Is.GreaterThan(1), "The test text did not add an atlas texture.");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var font in fonts)
            {
                Assert.That(font != null, font.name);
                Assert.That(font.material != null, font.name + " material");
                Assert.That(font.atlasTextures.Take(font.atlasTextureCount).All(t => t != null), font.name + " atlas textures");
            }
            Assert.That(AppTransferWindow.TextFont(), Is.SameAs(fonts[0]));
        }

        [Test]
        public void TextFontIsMadeAgainWhenItsMaterialIsGone()
        {
            var broken = PanelFonts()[0];
            Object.DestroyImmediate(broken.material);

            var font = AppTransferWindow.TextFont();

            Assert.That(broken == null, "The broken font was left behind.");
            Assert.That(font != null && font.material != null, "No usable font was made.");
            Assert.That(Resources.FindObjectsOfTypeAll<FontAsset>().Count(f => f.name == font.name), Is.EqualTo(1));
        }

        private static List<FontAsset> PanelFonts()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) Assert.Ignore("The panel uses its own font only on Windows.");
            var font = AppTransferWindow.TextFont();
            if (font == null) Assert.Ignore("The OS font the panel uses is not installed.");
            return new[] { font }.Concat(font.fallbackFontAssetTable).ToList();
        }
    }
}
