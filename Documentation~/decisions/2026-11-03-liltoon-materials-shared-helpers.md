# lilToon のマテリアルまわりで、写していた式・表・画像の作り方を 1 か所にまとめる

- 日付: 2026-11-03
- 対象: `Formats/Runtime/LilToon234Catalogue.cs`・`Tools/liltoon_catalogue.py`・`Editor/UnsupportedShaderSubstitution*.cs`・`Editor/ExportRendererSelection.cs`・`Editor/MobileMaterialMath.cs`・`Editor/LilToonMainTextureBaker.cs`・`Editor/LilToonMaterialReader.cs`・`Editor/LilToonFullSnapshot.cs`・`Editor/LilToonStructureCheck.Unity.cs`・`Editor/MatcapApproximation.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/BlinkExportSession.cs`・`Editor/ExportRecovery.cs`

## 背景

大規模リファクタリング（[2026-11-02-big-refactor.md](2026-11-02-big-refactor.md)）の監査で、lilToon のマテリアルを読む・置き換える・焼く側に同じ式と同じ表の写しが見つかった。書き出す VRM の中身・警告の文言・表情の集合は変えずに、写しを 1 か所にまとめる。

## 決定

- lilToon のカタログ（`LilToon234Catalogue.cs`）で、パス名の配列と LightMode の配列を別々に書くのをやめ、生成器が `(パス名, LightMode)` の並び 1 つ（`PassDeclarations`）を書く。`PassNames()`（序数で並べる）と `LightModes()` はそこから作る。生成し直さずに片方だけ直すとずれていたため。値と並びは変えない。読み戻す側（`Tools/liltoon_catalogue.py`）も同じ形で読む。
- 対応していないシェーダーの置き換えで、標本化（`Sampled`）・読み戻し（`ReadRow`）・画像の作成（`NewImage`）を `UnsupportedShaderSubstitution.cs` に 1 つずつ置き、Toon・VRChat・Decal の 3 つの部分クラスから使う。マスクの画像はリニア、VRChat と Decal の画像は sRGB で作っていたので、リニアかどうかは `NewImage` の引数で受ける。深度の比較の表 `DepthComparisons` はステンシルの表に "Off" を足したものと同じだったので、そう作る。
- UniVRM が書き出すメッシュのレンダラーの選び方（`SkinnedMeshRenderer` か `MeshFilter` 付きの `MeshRenderer` で、ジオメトリのあるもの）を `ExportRendererSelection.ExportedMeshRenderers` に置き、4 か所の写しとまばたきの空振り用の選び方（同じ結果だった）をそこへ寄せる。
- マテリアルのプロパティを読んで判定する式（無ければ既定値・0.5 より大きいか・0 でないか）を `MobileMaterialMath` の `Float`・`Enabled`・`NonZero` に寄せる。`> 0.5` と `!= 0` は 0 から 1 の間の値で結果が違うので別の関数のまま、呼び出し元ごとに元の判定と既定値（`_Cull` は 2、`EnableLighting` は 1、`_MatCapBlendMode` は Add、輪郭線の幅は `> 0.5`）を使う。
- 範囲外: lilToon 1.x の読み取り（612 行）は利用者がいるかを mk が判断するまで触らない。

## 確かめたこと

- 全 EditMode（Unity 2022.3.22f1・lilToon 2.3.4・VRChat SDK 3.10.1・NDMF 1.14.8・Modular Avatar 1.18.7・Avatar Optimizer 1.8.16）で新しい赤 0・消えたケース 0。
- カタログを lilToon 2.3.4 のソースから生成し直すと、コミット済みの `LilToon234Catalogue.cs` と同一。
- `Tools/validate.py`・`Tools/test-liltoon-catalogue.py`・`Tools/test-compatibility-tooling.py`・`Tools/run-behavior-tests.ps1`・`Tools/run-ndmf-preparation-tests.ps1` が緑。
