using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    public class ExportDiagnosticsTests
    {
        // Names the avatar, the VRM and the report hold. None may reach the text.
        static readonly string[] Names =
        {
            "SecretAvatarRoot", "SecretHairObject", "SecretHiddenObject", "SecretMeshAsset", "SecretMaterial", "SecretTexture",
            "SecretShape", "SecretAuthor", "SecretVrmTitle", "SecretExpression", "秘密の髪", "SecretClip", "someone",
            "SecretLayer", "Secret Share", "Secret Person", "髪",
        };

        GameObject root;
        Material material;
        Mesh mesh;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("SecretAvatarRoot");
            var hair = new GameObject("SecretHairObject");
            hair.transform.SetParent(root.transform);
            mesh = new Mesh { name = "SecretMeshAsset", vertices = new Vector3[3], triangles = new[] { 0, 1, 2 } };
            mesh.AddBlendShapeFrame("SecretShape", 100f, new Vector3[3], null, null);
            hair.AddComponent<MeshFilter>().sharedMesh = mesh;
            material = new Material(Shader.Find("Standard")) { name = "SecretMaterial" };
            hair.AddComponent<MeshRenderer>().sharedMaterial = material;
            var hidden = new GameObject("秘密の髪");
            hidden.transform.SetParent(root.transform);
            hidden.AddComponent<MeshRenderer>();
            hidden.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            ExportDiagnostics.Forget();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(mesh);
        }

        // A 4x4 PNG: the left half fully transparent, the right half opaque.
        static byte[] HalfTransparentPng()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[16];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, (byte)(i % 4 < 2 ? 0 : 255));
                texture.SetPixels32(pixels);
                texture.Apply();
                return texture.EncodeToPNG();
            }
            finally { Object.DestroyImmediate(texture); }
        }

        static byte[] Vrm()
        {
            var png = HalfTransparentPng();
            var json = new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["buffers"] = new List<object> { new Dictionary<string, object> { ["byteLength"] = (long)png.Length } },
                ["bufferViews"] = new List<object> { new Dictionary<string, object> { ["buffer"] = 0L, ["byteOffset"] = 0L, ["byteLength"] = (long)png.Length } },
                ["images"] = new List<object> { new Dictionary<string, object> { ["name"] = "SecretTexture", ["bufferView"] = 0L, ["mimeType"] = "image/png" } },
                ["textures"] = new List<object> { new Dictionary<string, object> { ["source"] = 0L } },
                ["accessors"] = new List<object>
                {
                    new Dictionary<string, object> { ["count"] = 24L, ["type"] = "VEC3", ["componentType"] = 5126L },
                    new Dictionary<string, object> { ["count"] = 36L, ["type"] = "SCALAR", ["componentType"] = 5123L },
                },
                ["materials"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "SecretMaterial", ["alphaMode"] = "MASK", ["alphaCutoff"] = 0.3, ["doubleSided"] = true,
                        ["pbrMetallicRoughness"] = new Dictionary<string, object> { ["baseColorTexture"] = new Dictionary<string, object> { ["index"] = 0L } },
                    },
                },
                ["meshes"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "SecretMeshAsset",
                        ["primitives"] = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                ["attributes"] = new Dictionary<string, object> { ["POSITION"] = 0L }, ["indices"] = 1L, ["material"] = 0L,
                                ["targets"] = new List<object> { new Dictionary<string, object> { ["POSITION"] = 0L } },
                            },
                        },
                    },
                },
                ["nodes"] = new List<object>
                {
                    new Dictionary<string, object> { ["name"] = "SecretAvatarRoot", ["children"] = new List<object> { 1L } },
                    new Dictionary<string, object> { ["name"] = "SecretHairObject", ["mesh"] = 0L },
                },
                ["extensions"] = new Dictionary<string, object>
                {
                    ["VRMC_vrm"] = new Dictionary<string, object>
                    {
                        ["meta"] = new Dictionary<string, object> { ["name"] = "SecretVrmTitle", ["authors"] = new List<object> { "SecretAuthor" } },
                        ["expressions"] = new Dictionary<string, object>
                        {
                            ["custom"] = new Dictionary<string, object> { ["SecretExpression"] = new Dictionary<string, object>() },
                        },
                    },
                },
            };
            return GlbDocument.Create(json, png).Write();
        }

        string Text()
        {
            var report = new ExportFallbackReport();
            report.Record(ExportOutcome.Omitted, "ブレンドシェイプ", "SecretHairObject",
                "SecretHairObject: 「SecretClip」を省きました C:/Work/someone/Desktop/a.anim").Path = "SecretAvatarRoot/SecretHairObject";
            report.Record(ExportOutcome.Approximated, "マテリアル", "SecretMaterial", "SecretMaterial: SecretTexture を縮小しました");
            var warnings = new[]
            {
                "表情 SecretExpression の動きを止めました",
                "秘密の髪 を書き出しませんでした",
                "Assets/SecretFolder/SecretTexture.png を縮小しました",
                // A name the VRM does not hold, paths with spaces, and a one-character name.
                "FX の SecretLayer を読めませんでした",
                @"\\fileserver\Secret Share\a.png を読めませんでした",
                "/Volumes/Secret Person/a.png を読めませんでした",
                "髪 を書き出しませんでした",
                // A fixed sentence of the exporter is written as it is.
                "元のVRMの瞬き設定が出力時に失われました。",
            };
            return ExportDiagnostics.Record(root, Vrm(), report, warnings, ExportDiagnostics.QuickTransfer).Text();
        }

        [Test]
        public void TheTextHasNoNameOrPath()
        {
            var text = Text();
            foreach (var name in Names) Assert.That(text, Does.Not.Contain(name), name);
            Assert.That(text, Does.Not.Contain("Secret"));
            Assert.That(text, Does.Not.Contain("C:/"));
            Assert.That(text, Does.Not.Contain("Assets/"));
            Assert.That(text, Does.Not.Contain("/Volumes/"));
            Assert.That(text, Does.Not.Contain(@"\\"));
        }

        [Test]
        public void TheTextHasTheVersionsAndTheExportType()
        {
            var text = Text();
            Assert.That(text, Does.Contain("Exporter " + LilToonExporterWindow.PackageVersion()));
            Assert.That(text, Does.Contain("Unity " + Application.unityVersion));
            Assert.That(text, Does.Contain(" / lilToon "));
            Assert.That(text, Does.Contain("書き出し方法 かんたん転送"));
            Assert.That(text, Does.Contain("マテリアルの形式 MToonのみ"));
        }

        [Test]
        public void TheTextNumbersEachMeshAndMaterial()
        {
            var text = Text();
            Assert.That(text, Does.Contain("合計 メッシュ 1 / 頂点 24 / マテリアル 1 / テクスチャ 1（最大 4px）"));
            Assert.That(text, Does.Contain("#1 頂点 24 / サブメッシュ 1 / ブレンドシェイプ 1 / インデックス 16bit / スキンなし / マテリアル #1"));
            Assert.That(text, Does.Contain("#1 元 Standard / 変換後 MToon / カットアウト Cutoff 0.3 / キュー 既定 / 両面"));
            Assert.That(text, Does.Contain("メイン PNG 4x4 アルファ 最小 0.00 平均 0.50 0.3未満 50.0% / テクスチャ 1（最大 4px）"));
            Assert.That(text, Does.Contain("元のアバター レンダラー 2 / 初期で非表示 1 / 無効 0"));
        }

        [Test]
        public void TheTextListsWhatTheConversionChanged()
        {
            var text = Text();
            Assert.That(text, Does.Contain("[変換で変えた項目]"));
            Assert.That(text, Does.Contain("省略 [ブレンドシェイプ] 文面は省略 / 対象 メッシュ #1"));
            Assert.That(text, Does.Contain("近似 [マテリアル] 文面は省略 / 対象 マテリアル #1"));
            Assert.That(text, Does.Contain("参考 [そのほかの知らせ] 文面は省略（7件）"));
            Assert.That(text, Does.Contain("参考 [そのほかの知らせ] 元のVRMの瞬き設定が出力時に失われました。"));
        }

        [Test]
        public void AnUnreadableVrmHasNoChangeSection()
        {
            var text = ExportDiagnostics.Record(root, new byte[] { 1, 2, 3 }, null, new[] { "SecretLayer" }, ExportDiagnostics.QuickTransfer).Text();
            Assert.That(text, Does.Contain("VRMを読めませんでした"));
            Assert.That(text, Does.Not.Contain("[変換で変えた項目]"));
            Assert.That(text, Does.Not.Contain("なし"));
            Assert.That(text, Does.Not.Contain("Secret"));
        }

        [Test]
        public void TheSameBytesAreRecordedOnce()
        {
            var bytes = Vrm();
            var first = ExportDiagnostics.Record(root, bytes, null, null, ExportDiagnostics.QuickTransfer);
            Assert.That(ExportDiagnostics.Record(root, bytes, null, null, ExportDiagnostics.BoardSave), Is.SameAs(first));
            Assert.That(ExportDiagnostics.Last.Text(), Does.Contain("書き出し方法 かんたん転送"));
        }

        [Test]
        public void ShadersShowOnlyTheirFamily()
        {
            Assert.That(ExportDiagnostics.ShaderText("Hidden/lilToonFurTwoPass"), Is.EqualTo("lilToon（ファー・2パス）"));
            Assert.That(ExportDiagnostics.ShaderText("_lil/[Optional] lilToonOutlineOnlyCutout"), Is.EqualTo("lilToon（輪郭線のみ・カットアウト）"));
            Assert.That(ExportDiagnostics.ShaderText("Standard"), Is.EqualTo("Standard"));
            Assert.That(ExportDiagnostics.ShaderText("SecretShop/SecretShader"), Is.EqualTo("その他（lilToon以外）"));
            Assert.That(ExportDiagnostics.ShaderText("ToonShader_Secret/Body"), Is.EqualTo("その他（lilToon以外）"));
            Assert.That(ExportDiagnostics.ShaderText("StandardSecret"), Does.Not.Contain("Secret"));
            Assert.That(ExportDiagnostics.ShaderText("Hidden/InternalErrorShader"), Is.EqualTo("シェーダーなし"));
        }
    }
}
