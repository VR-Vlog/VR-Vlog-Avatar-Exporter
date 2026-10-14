# lilToon の版を 2.0.0〜2.3.4 の範囲で判定する

- 日付: 2026-10-14
- 対象: `Compatibility/dependencies.json`・`Tools/generate-compatibility.py`・`Editor/Compatibility/DependencyPolicy.cs`・`Editor/Compatibility/DependencyDiagnostics.cs`・`Editor/LilToonExporterWindow.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/LilToonFullSnapshot.cs`・`Editor/Locales`
- Issue: #146（Epic #145）

## 背景

これまでは導入済みの lilToon が 2.3.4 と文字列で一致するときだけ書き出せた。Epic #145 の実測で、2.0.0〜2.3.3 のマテリアルは lilToon 本体の移行処理だけで 2.3.4 と同じ構成（プロパティ名・型・次元・パス名・LightMode）になると分かったので、対応範囲を 2.0.0〜2.3.4 に広げる。

版の判定は 4 か所に分かれていた。診断画面の `DependencyPolicy.LilToonError`、書き出し画面の `RequireSupportedLilToon`、`LilToonFullSnapshot.Inject` の版の検査、表情記録の画面の `"2.3.4"` の直書き（こちらは導入済みの版を見ていなかった）。

## 決定

- `Compatibility/dependencies.json` の `lilToon.version`（2.3.4）は「書き出しが準拠するカタログの版」として残し、対応範囲を `lilToon.supported.minimum`／`maximum`（2.0.0／2.3.4）として別に持つ。生成スクリプトが `DependencyPolicy.LilToonMinimum`／`LilToonMaximum` を出す。生成時に「範囲の両端が `X.Y.Z` の数字 3 つで、カタログ版を含む」ことを確かめる。
- 判定は `DependencyPolicy.ClassifyLilToon` の 1 か所にまとめる。結果は 対応／未導入／読めない（プレリリース・`+build`・前後の空白・数字 3 つでない・先頭が 0 の数字）／古い／新しい の 5 つ。数字は整数として比べる（2.10.0 は 2.3.4 より新しい）。
- 書き出し画面・表情記録の画面・診断画面は、どれも `DependencyPolicy.LilToonError` で止める。止めたときの文言は理由と対処を書き、日英韓中（簡・繁）の訳を足す。1.x 以前は「2.0.0〜2.3.4 へ更新すると lilToon の移行処理でマテリアルが移行され、そのまま書き出せる」と案内する。
- 2.3.4 より新しい版は今までどおり止める（Epic #145 の未決 1）。続行に切り替えるときは `DependencyPolicy.AcceptsNewerLilToon` を `true` にする。切り替えは #148 の構成照合と組にする前提で、その旨をコードのコメントに書いた。
- 書き出しの `sourceLilToonVersion` は導入済みの版ではなくカタログ版（`DependencyPolicy.LilToonVersion`＝2.3.4）にする。`RequireSupportedLilToon` は範囲を確かめたうえでカタログ版を返す。`LilToonFullSnapshot.Inject` の検査は「カタログ版以外を書かない」不変条件として残す。
- 診断画面では、UniVRM のエラーにだけ従来の共通の対処文（`DependencyPolicy.Recovery`）を付ける。lilToon の文言は対処を自分で書いているので付けない。
- 動作環境の欄は、範囲外の版のときに「（対応範囲 2.0.0〜2.3.4 の外）」を添える。

## 検討した代替案と不採用理由

- 範囲を `Editor/Compatibility/DependencyPolicy.cs` に直書きする: ほかの版の情報（UniVRM・NDMF）はすべて `dependencies.json` から生成しており、上流の新しい版の確認（`check-upstream-dependencies.py`）も同じファイルを読む。1 か所にそろえる。
- 文字列の前方一致や辞書順で比べる: 2.10.0 が 2.3.4 より古いと判定される。
- 範囲内の版を `sourceLilToonVersion` にそのまま書く: アプリの契約（`LilToonFullContract.Validate`）は 2.3.4 以外を拒否するので、アプリを変えずには読めない。実際の版の記録は #147 で `asset.extras` に足す。
- プレリリースを「版の数字の部分」で判定して通す: プレリリースの構成は確かめていない。正式版だけを通す。

## 影響

- lilToon 2.0.0〜2.3.3 のプロジェクトでも書き出し前の版の検査で止まらなくなる。書き出したファイルは 2.3.4 で書き出したときと同じく `sourceLilToonVersion` が 2.3.4 になる。
- 表情記録の画面も導入済みの lilToon の版を確かめるようになった。範囲外なら書き出し前に止まる。
- Unity での版ごとの実行（2.0.0〜2.3.4 の各プロジェクト）は #149 で行う。README などの対応範囲の文書は #150 で直す。

## 未決

- 2.3.4 より新しい版の扱い（Epic #145 の未決 1）。
- 2.0.0〜2.2.1 のバックライトの A が 1 未満のマテリアルは補正しない（Epic #145 の未決 2）。

## 出典

- Epic #145 の本文（2.0.0〜2.3.3 の移行後の構成が 2.3.4 と一致した実測。Unity 2022.3.22f1・Built-in RP）
- `Editor/LilToonFullContract.cs` の `Validate`（`sourceLilToonVersion` が `LilToon234Catalogue.Version` 以外なら拒否）
