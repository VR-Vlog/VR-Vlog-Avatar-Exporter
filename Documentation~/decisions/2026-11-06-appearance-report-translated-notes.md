# 見た目の変更のまとめは Editor の言語で書く注記も拾う

- 日付: 2026-11-06
- 対象: `Editor/ExportAppearanceReport.cs`・`Editor/UniVrmOneClickExporter.cs`

## 背景

書き出しのあとに出る「見た目の変更」のまとめ（`ExportAppearanceReport.Changes`）は、注記に「省略」「近似」「縮小」などの語が入っているかで拾う。注記のほとんどは Editor の言語によらず日本語で書くが、次の注記は Editor の言語で書く。

- 書き出しの一部を省略したとき（`RecordOmissions`）
- Modular Avatar / NDMF の処理で表情用の BlendShape が失われたとき（`NdmfExportPreparation.LostGeneratedMorphs`）
- 非対応のシェーダーを公式lilToon で近似したとき、近似できなかったとき（`UnsupportedShaderSubstitution.Warning`・`Failed`）
- ビルド処理で表情の変形や表情トラッキング用の変形を追えなくなり、省略したとき（`UniVrmOneClickExporter.LostMorphOmittedWarning`・`LostExpressionOmittedWarning`・`LostTrackingWarning`）
- 同じ名前の表情が複数あり、表情の自動設定を省略したとき（`VrmExpressionBindings.DuplicateExpressionFormat`）
- lilToon 2.3.4 とプロパティが合わないマテリアルを MToon で近似したとき。理由（`ApproximatedLilToonReason`）だけが Editor の言語で「 — 」の後ろに付く

英語・韓国語・中国語の Editor ではこれらに日本語の語が入らず、まとめから落ちていた。画像の縮小の注記だけは以前に `T("縮小")` を足して拾っていた。

## 決定

日本語の語で拾う決まりはそのままにし、Editor の言語で書く注記は、注記の先頭の文を `ExporterLocalization.T` で訳した文で始まるかで拾う（`TranslatedStarts`）。`{0}` のように名前が入る文は、名前の位置をどの文字列でもよいとし、残りの固定の部分が順に並んでいるかで見る（英訳では `{1}` が `{0}` より前に来る文もある）。途中に含むかで見ると、別の注記の中に同じ文が入ったときにも拾う。lilToon の近似の理由は、注記が「 — 」と訳した理由で終わるかで拾う（`TranslatedReasons`）。同じ書き出しの注記でも理由が「公式の lilToon ではない」ときは日本語でも語が入らず、まとめに入らないので、どの言語でも入れない。画像の縮小の注記は今までどおり `T("縮小")` を含むかで拾う。省略の注記の固定の文は `UniVrmOneClickExporter.StageOmittedWarning` に置き、書く側と拾う側で同じ定数を使う。

## 考えた別案

語そのものを訳して探す案（`T("省略")` など）は採らなかった。英訳では "left out" や "Approximated" のように文ごとに語が変わり、短い語を探すと名前に同じ綴りがある別の注記まで拾う。

## 確かめ方

5 言語それぞれで、上の 9 種類の注記がまとめに入り、同じく Editor の言語で書くがまとめに入らない注記（`KeptForExpressions` と、理由が `UnofficialLilToonReason` のマテリアルの注記）は入らないことを EditMode テストで確かめる。英語と韓国語では、注記に日本語の語が入っていない（訳した文だけで拾っている）ことと、別の注記の途中に固定の文が入っても拾わないことも確かめる。中国語は「省略」「近似」を日本語と同じ字で書くので、日本語の語でも拾われる。
