# 表情ボードの書き出しでも 8,192 フレームを超える手動の動くポーズを外して続ける

- 日付: 2026-10-23
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Tests/Editor/QuickTransferMatchesDefaultNormalTests.cs`・`CHANGELOG.md`

## 背景

`PoseExportSession.CollectPreparedIncrementally` は、1 本で 8,192 フレームを超える動くポーズに `Error` と `OverFrameLimit` を付けて残す。`PoseLimitChooser.Apply` はこの行を一覧に出し、ファイルには入れない。LilToonExporter ウィンドウからの書き出しはこのまま続く。

表情ボードの書き出し（`InjectSourcePoses`、**アプリに転送** の通常とかんたんも通る）は、その前に「手動で追加したポーズに `Error` があれば止める」検査をしていた。`OverFrameLimit` の行も `Error` を持つので、この検査で書き出し全体が止まっていた。

## 決定

- `InjectSourcePoses` の検査から `OverFrameLimit` の行を除く。ほかの理由で失敗した手動のポーズはこれまでどおり止める。
- 回帰テストは新しく足さず、上限を超えたときに通常とかんたんで同じ中身になることを確かめている `PosesOverTheAppLimitMatch` に 140 秒（8,401 フレーム）の手動ポーズを 1 本足す。両方が書き出せること、画面に「8,192 フレーム超え」の行として出ること、書き出すポーズが変わらないことを見る。

## 決めなかったこと・そのままにしたこと

- ほかの理由で失敗した手動のポーズで表情ボードの書き出しを止める動きは変えない。LilToonExporter ウィンドウとの差はこの変更の範囲外。
