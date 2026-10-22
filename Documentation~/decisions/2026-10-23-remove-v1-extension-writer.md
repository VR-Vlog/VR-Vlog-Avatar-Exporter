# 以前の形式（スキーマ1.x）のlilToon拡張データを書き込む処理を消す

- 日付: 2026-10-23
- 対象: `Editor/LilToonGlbExtension.cs`・`Editor/GlbTextureDownsizer.cs`・`Editor/MobileTextureEncoder.cs`・`Tests/Editor/`・`Tools/`・`CHANGELOG.md`

## 背景

書き出しは、lilToonの材質を `LilToonFullSnapshot.Inject`（スキーマ2.0の材質一式）でVRMへ書き込んでいる。2.0の書き込みは `6b0bf8d8`（#43、2026-09-10）で入り、`2e235aff`（#47、2026-09-12）で書き出し画面から `LilToonGlbExtension.Inject`（材質ごとの1.x形式）を呼ぶ箇所が無くなった。それ以降、1.x形式を書き込む `LilToonGlbExtension.Inject` と、その中だけで使う画像の縮小（`GlbTextureDownsizer`）は、テストと `Tools/` のハーネスだけが呼んでいた。

一方で、保存前の検査（`ExportRecoverySession`）と **Assets > VR Vlog > Validate lilToon VRM** は `LilToonGlbExtension.Validate` を使っており、以前のバージョンで書き出した1.x形式のファイルも検証する。検証の側は書き出しの経路にあるので残す。

消す前に、メンバーごとに `Editor/`・`Tests/`・`Tools/`・`.github/`・`Documentation~/` を名前で検索し、`Tools/validate.py` のソースの文字列の確認と、Unity 無しで回すハーネス（`Tools/run-behavior-tests.ps1`・`Tools/run-bake-tests.ps1`）がコンパイルするファイルの一覧も確かめた。

## 決定

次を消す。どれも書き出しの経路から呼ばれていないので、書き出すVRMは変わらない。

- `LilToonGlbExtension.Inject` と、`Inject` だけが使っていた `ToDom`・`Names`・`ResolveTexture`・`FindTexture`・`MaterialTexture`・`AddWarning`・`MagFilter`・`MinFilter`・`Wrap`・`UniqueIndex`・`SameName`・`Strip`。
- `GlbTextureDownsizer`（ファイルごと）。
- `MobileTextureEncoder.EncodeSource`（`Inject` だけが呼んでいた）と `ResizeEncoded`（`GlbTextureDownsizer` だけが呼んでいた）。書き出しが使う `EncodeConverted` と `MobileTextureSerializer` は残す。
- `Tests/Editor/Fixtures/MaterialBindingFixture.InjectedMaterials`（`Inject` の出力を読むためだけのもの）。
- `Tools/validate.py` の、消したコードの文字列を確かめる行と、`Tools/run-behavior-tests.ps1` の `GlbTextureDownsizer.cs`。

## 消したテストと、書き出しの経路で残っている確認

- `RendererSelectionTests.HiddenOutfitDoesNotRequireAFallbackMaterial`（3ケース）と `Tools/BehaviorTests.cs` の `CheckHiddenMaterialInjection`: 隠れた衣装の材質を `Inject` が読まないことを確かめていた。書き出しでの隠れたレンダラーの扱いは `InactiveRootExportTests` が確かめている。
- `TextureResizeTests.SharedEncodedColorAndDataImagesResizeWithDifferentCorrectFiltering` と `Tools/TextureResizeBehaviorTests.cs` の `TestGlbGraph`: `GlbTextureDownsizer` だけを確かめていた。書き出しの画像の縮小は `TextureResizeTests.ConvertedFourKTexturePreservesColorAlphaAndSource`・`FourKNormalMapIsConvertedBeforeResizeWithoutReimportingSource`（`MobileTextureSerializer` 経由）が、色と数値の画像で縮小のしかたを変えることは `Tools/TextureResizeBehaviorTests.cs` の `TextureResizePolicy` の確認が見ている。
- `TextureResizeTests.ExtensionOnlyFourKBacklightTextureIsResizedDuringInjection`・`EmissionMaskInjectionStoresSampledLinearRgbaWithoutChangingSource`（2ケース）: 1.x形式で拡張にだけ入る画像の縮小と色空間を確かめていた。2.0の書き込みでは `FullTextureResizeTests.CompleteExportCapsAllFallbackAndFullImagesIncludingExtensionOnlySlots` が拡張にだけ入る画像（逆光・発光マスクを含む）の縮小を確かめている。
- `ExportFlowTests.FreshNormalExportSavesAndReloadsWithoutARecoveryStep` の `fullLilToon: false` のケース: 書き出し後に `Inject` を通す以前の経路だった。2.0で書き込むケース（書き出しと同じ経路）は残した。

テストメソッドは残し、`Inject` で作っていた入力だけを置き換えたもの:

- `ExportFlowTests`（5か所）・`ExportFallbackConfirmationTests`（2か所）・`ExportRecoveryFailureSeedTests`（1か所）・`ExportRecoverySessionTests`（2か所）: 保存の検査を通るVRMが要るだけだったので、書き出しが使う `LilToonGlbExtension.MarkWithoutLilToon` で作るようにした。テストのメソッド名とケースは変わらない。
- `Tools/LightingBehaviorTests.cs`: 1.x形式の拡張を書くテスト用の関数（`LegacyExtension`、消した `ToDom` と同じ形）を置き、`Validate` が1.x形式の照明の項目を受け付ける・拒否する確認をそのまま残した。書き込み側の性質だった「新しく書くと1.3になる」の確認だけを消した。

## そのままにしたこと

- `LilToonGlbExtension.Validate` の1.x形式の検証、`FromDom`、`LilToonExtensionModel.cs`、`LilToonExtensionValidator.cs`: 以前のバージョンで書き出したファイルの検証に要る。`LilToonMaterialReader.Read`（書き出しの警告に使う）もこの型を使う。
- `UniVrmOneClickExporter.Export` の `exporterVersion == null` の分岐: 書き出し画面と表情ボードは常にバージョンを渡すので通らないが、`Editor/UniVrmOneClickExporter.cs` は別の変更と重なるため今回は触らない。次に消せる候補。
- `Tools/MaterialTestShim.cs`・`Tools/BakeTestShim.cs` の、`EncodeSource` のために置いていた `GL`・`RenderTexture` などの代用品: ほかのファイルが使っていないか確かめきれていないので残した。
- `Editor/Locales/ExporterLocale_*.json` の、消した処理だけが出していた2つの文言（画像を読み込めなかったとき・同名候補が複数あったとき）の訳: 使われない訳が残っても表示は変わらないので、今回は消していない。
- `Tools/run-unity-compatibility.py` の `RendererSelectionTests` の件数（3）: このクラスは今回の変更前から6ケースあり、件数と合っていなかった。`HiddenOutfitDoesNotRequireAFallbackMaterial` の3ケースを消して3ケースになり、件数と合う。

## 確認したこと

- Unity 無しのハーネス: `Tools/run-behavior-tests.ps1`（照明854件・テクスチャー縮小31件ほか）と `Tools/run-bake-tests.ps1`（392件）がすべて通った。
- `Tools/test-compatibility-tooling.py` が通った。
- Unity の EditMode でのコンパイルとテストは CI で確かめる。
