# アプリに転送（かんたん）にも「軽量化」の行を出す

- 日付: 2026-11-26
- 対象: `Editor/LightweightChoice.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`（`Lightweight`・`DrawLightweight`）・`Editor/AppTransferExport.cs`（`IAppTransferExport.LightweightNote`・`DrawLightweight`）・`Editor/LanTransfer/AppTransferPanel.cs`・`Editor/LanTransfer/AppTransferWindow.uxml`・`.uss`・`Editor/Locales/ExporterLocale_en.json`・`Tests/Editor/LightweightChoiceTests.cs`・`Tests/Editor/QuickTransferMatchesDefaultNormalTests.cs`

## 背景

「軽量化」（なし・標準・最大、`2026-11-23-lightweight-levels.md`）は表情ボードの 1 ページ目にしか無かった。かんたんはアバターに保存した段で書き出すが、画面にはどの段で書き出すかが出ず、変えるには表情ボードを開く必要があった。

## 決定

かんたんのウィンドウの名前の下に「軽量化」の行を足す（mk が案 A を選んだ）。

- 行は表情ボードと同じ 3 つのボタン（80pt × 3 の 240pt 固定）と、その下の説明 1 行。説明は表情ボードと同じ文で、入らないときは省略し、全文はツールチップに出す。
- 値はアバターごとに保存した 1 つの値（`ExportSettings.Lightweight`）を両方の画面で読み書きする。押すとその場で保存する。
- 英語のラベルは「Optimization」から「Optimize」にする（表情ボードも同じ文字列を使う）。
- 通常（表情ボードの 4 ページ目に埋め込む転送の画面）には出さない。表情ボードの 1 ページ目に同じ行がある。
- ウィンドウの最小の高さを 400 から 446 にする（行 22 と余白 8 と説明 16）。

### 表情ボードが保存した値を直接読む

表情ボードは開いたときに保存した段を自分のフィールドに写し、書き出しはそのフィールドを使っていた。かんたんの行で段を変えても、開いている表情ボードは古い段のまま表示し、通常で書き出してしまう。

フィールドをやめ、表情ボードの `Lightweight` は保存した設定（`SavedSettings()`、保存のたびに読み直す）から読む。表情ボードとかんたんの書き出しが同じ値を使う作りになり、「かんたんは表情ボードを何も変えずに書き出した通常と同じ」の約束（`QuickTransferMatchesDefaultNormalTests`）を、片方の画面で段を変えたあとも保てる。

- 退けた案: かんたんで保存したときに開いている表情ボードへ値を知らせる。知らせる経路が増えるだけで、知らせ忘れた画面があると同じずれが起きる。
- 退けた案: かんたん側で段を書き出しに渡す。CLAUDE.md の「かんたん側に項目ごとの処理を書かない」に反する。

ボタンの組み立て・保存・説明は `LightweightChoice` にまとめ、表情ボード（IMGUI のレイアウト）とかんたん（UI Toolkit の `IMGUIContainer`）の両方から使う。Avatar Optimizer を使うかどうかは部品をたどるので、かんたんはアバターが変わったときだけ調べる。
