# ヒューマノイドでないアバターは書き出しの前に止める

- 日付: 2026-10-18
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/LilToonExporterWindow.cs`・`Editor/AppTransferExport.cs`・`Editor/Compatibility/ExporterLocalization.cs`・`Editor/Locales/*.json`・`Tests/Editor/PhysBoneSpringExportTests.cs`・`CHANGELOG.md`

## 背景

プロジェクト内のアバターとシーンを書き出し画面と同じ経路でまとめて書き出したところ、ルートの Animator が Generic のアバターが 1 件あった。このアバターは表情・瞬き・揺れ物・材質のすべての処理を通ったあと、保存前の確認で `VRMC_vrm required human bone 'hips' is missing.` という英語のメッセージを出して止まった。

VRM 1.0 はヒューマノイドが前提で、このアバターは何をしても保存できない。それでも重い処理を最後まで回し、原因の分からない英語のメッセージを出していた。

NDMF と最適化ツールが書き出し用の複製から Animator やボーンを消した場合の検査（`ValidatePreparedHumanoid`）はすでにあるが、元のアバターがヒューマノイドでない場合は検査を飛ばしていた。

## 決定

- 書き出し画面と **アプリに転送** の書き出し処理の最初で、選んだアバターを確かめる（`UniVrmOneClickExporter.ValidateSourceHumanoid`）。止めるのは次の 2 つ。
  - ルートにヒューマノイドの Avatar を持つ Animator がない。メッセージで、モデルの Rig を Humanoid にしてルートの Animator に設定するよう案内する。
  - Unity がヒューマノイドに必須とする 15 本のボーンのどれかが解決できない。足りないボーンの名前を出す。
- 文言は日本語を原文にし、英語・韓国語・簡体字・繁体字の訳を足した。ボーン名が続くほうは、可変部分を残して訳すために `DynamicErrorPrefixes` に加えた。
- アバターのルートが無効のときはボーンを解決できないので、ボーンの検査は飛ばす。無効なルートは、その後の既存の検査が止める。

## 検討した代替案と不採用理由

- `UniVrmOneClickExporter.Export` の先頭で確かめる: 自動テストの多くは Generic の小さなアバターで `Export` を呼び、出力のバイト列や途中の失敗を確かめている。保存の確認（`ExportOutputWriter`）は通さないので、ここで止めると、ヒューマノイドと関係のないテストの意図が変わる。
- `ExportAndSaveNormally` の先頭で確かめる: 保存経路のテストも Generic のアバターに作ったバイト列を渡して確かめているため、同じ理由で採らなかった。
- 書き出しボタンを押せなくする: 画面の変更は別の作業の範囲なので、この変更では止める位置とメッセージだけを直す。

## 影響

- ヒューマノイドのアバターの書き出しは変わらない。
- ヒューマノイドでないアバターは、書き出しの処理を始める前に、理由と直し方を表示して止まる。これまでも保存はできなかった。
- 試験的な表情の書き出し画面は別の作業の範囲なので変えていない。保存前の確認で従来どおり止まる。
