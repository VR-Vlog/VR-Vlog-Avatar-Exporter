# lilToon 2.0.0〜2.3.4 の対応範囲を文書に書く

- 日付: 2026-10-14
- 関連: Epic #145、#150（WBS 1-11）

## 背景

#146 で lilToon 2.0.0〜2.3.4 を書き出せるようにし、#147 で導入済みの版を `asset.extras.VRVLOG_lilToonInstalledVersion` に記録した。README・Website・互換性の文書は「lilToon 2.3.4」のままだった。範囲外の版で止まったときに利用者が次に何をすればよいかと、古い版で作ったマテリアルの見た目が lilToon を 2.3.4 へ上げたときと同じになることも、どこにも書いていなかった。

## 決定

- 対応範囲は README・README.en・`Documentation~/README.md`・Website・CHANGELOG で「lilToon 2.0.0〜2.3.4」と書く。英語・中国語・韓国語の Website では範囲を `2.0.0–2.3.4`（en dash）で書く。
- 止まる版と対処の表、`sourceLilToonVersion` と `asset.extras.VRVLOG_lilToonInstalledVersion` の意味、lilToon 側の描画の変更は `Compatibility/README.md` の「Supported lilToon versions」に 1 か所でまとめる。README には要点とリンクだけを置く。
- 描画の変更として書くのは、2.3.0 のバックライトの A（`_BacklightColor` とテクスチャの A が強度に掛かる）、2.1.0 の `_MatCapBlendUV1` の UV1 の 0〜1 への制限と lilToonLite の影色と環境光の混ぜ方（主光源の色が 1 未満のときだけ出る）、Outline Lit の計算（2.1.9 までは頂点単位、2.1.10 からピクセル単位）。実物で描いた比較は #172（`Compatibility/Evidence/LilToonVersionVisual`）。補正しない理由は「版ごとの変換を Exporter で保守しない」の 1 文にする。
- 2.1.9 以前の `_EnvRimBorder` の既定値 0.85 は、描画の違いの一覧に入れない。Exporter はこのプロパティを書き出さない（カタログで `external`）ので、書き出したファイルに差が出ないことだけを書く。Epic #145 の「0.85 のまま書き出される」は Exporter の挙動と違う。
- 書き出しの前にシェーダーの構成を 2.3.4 と照合して止めることも書く（#148 の後半で入る）。
- `ThirdPartyNotices/lilToon.md` の出典は 2.3.4 のままにする。同梱のベイク用シェーダーは 2.3.4 の処理を改変したもので、導入済みの版で変わらないため。
- パッケージに入る `CHANGELOG.md` からは `Compatibility/README.md` へ相対リンクを張らず、ファイル名と見出しを書く（パッケージに `Compatibility/` が入らないため）。

## 検討した代替案と不採用理由

- 描画の違いを README に全部書く: README が長くなり、英語の README と内容がずれやすい。互換性の文書に 1 か所で書くほうが直しやすい。
- Website の導入ダイアログにも 1.x の更新案内を書く: Website は短くする方針。止まったときは Exporter が理由と対処を出すので、Website には範囲だけを書く。

## 影響

- Website は main へのマージで公開されるので、Exporter の次のリリースと同時にマージする。
- #148 の後半（構成の照合）がリリースに入らない場合は、README・CHANGELOG・`Compatibility/README.md` の照合の記述を外す。

## 未決

- 2.3.4 より新しい版の扱い（Epic #145 の未決 1）。今の文書は「止まる。2.3.4 に戻すか対応した Exporter を待つ」と書いている。続行に変えたら、表の該当行と README の段落を直す。

## 出典

- Epic #145 の本文（範囲を決めた実測・方針・未決）
- `Editor/Compatibility/DependencyPolicy.cs` の停止の文言（#146）
- `Documentation~/decisions/2026-10-14-liltoon-installed-version.md`（#147）
- lilToon 2.2.1 と 2.3.4 の `Shader/Includes/lil_common_frag.hlsl` の `lilBacklight`（2.3.4 で `backlightColor.a` が強度に掛かる）と、lilToon の CHANGELOG（2.3.0「逆光ライトの透明度が反映されていない」）
