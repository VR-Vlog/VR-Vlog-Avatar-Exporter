# 見た目の変更のまとめは Editor の言語で書く注記も拾う

- 日付: 2026-11-06
- 対象: `Editor/ExportAppearanceReport.cs`

## 背景

書き出しのあとに出る「見た目の変更」のまとめ（`ExportAppearanceReport.Changes`）は、注記に「省略」「近似」「縮小」などの語が入っているかで拾う。注記のほとんどは Editor の言語によらず日本語で書くが、次の注記は Editor の言語で書く。

- 書き出しの一部を省略したとき（`RecordOmissions`）
- Modular Avatar / NDMF の処理で表情用の BlendShape が失われたとき（`NdmfExportPreparation.LostGeneratedMorphs`）
- 非対応のシェーダーを公式lilToon で近似したとき、近似できなかったとき（`UnsupportedShaderSubstitution.Warning`・`Failed`）

英語・韓国語・中国語の Editor ではこれらに日本語の語が入らず、まとめから落ちていた。画像の縮小の注記だけは以前に `T("縮小")` を足して拾っていた。

## 決定

日本語の語で拾う決まりはそのままにし、Editor の言語で書く注記は、注記の先頭の固定の文を `ExporterLocalization.T` で訳した文で拾う。対象の文は `Translated` に並べる。

## 考えた別案

語そのものを訳して探す案（`T("省略")` など）は採らなかった。英訳では "left out" や "Approximated" のように文ごとに語が変わり、短い語を探すと名前に同じ綴りがある別の注記まで拾う。

## 確かめ方

5 言語それぞれで、上の 4 種類の注記がまとめに入り、同じく Editor の言語で書くがまとめに入らない注記（`KeptForExpressions`）は入らないことを EditMode テストで確かめる。
