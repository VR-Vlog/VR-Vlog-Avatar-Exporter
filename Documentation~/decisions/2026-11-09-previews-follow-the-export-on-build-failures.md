# ポーズと瞬きのプレビューもビルド処理の失敗を書き出しと同じように扱う

- 日付: 2026-11-09
- 対象: `Editor/NdmfExportPreparation.cs`・`Editor/PoseReviewWindow.cs`・`Editor/BlinkPreviewWindow.cs`・`Editor/PreviewRendering.cs`

## 背景

2026-11-08 の決定（`2026-11-08-keep-build-results-when-a-pass-fails.md`）で、書き出しと表情ボードはビルド処理が 1 つ失敗してもその処理だけを外して続けるようになった。ポーズの確認（`PoseReviewWindow`）と瞬きの確認（`BlinkPreviewWindow`）は `tolerateFailedPasses` を渡しておらず、同じアバターでプレビューだけが例外で止まった。外部参照を外した通知も出なかった。ポーズの確認は `warnings` 自体を渡していなかった。

## 決定

- 両方のプレビューは `NdmfExportPreparation.PrepareForPreview` で準備する。`tolerateFailedPasses` は表情ボードと同じ `ExportStageGuard.ContinueByDefault`（既定の設定を切った人は今までどおり止まる）。
- 準備で出た注意を、ウィンドウの上の「書き出しの注意」に出す。「適用しました」の 2 行（`BuildApplied`・`VrcFuryApplied`）は処理が動いたことしか言わないので出さない。
- 注意は `PreviewRendering.DrawExportNotices` で高さを 76 に抑えてスクロールさせる。ポーズの確認は適用ボタンとプレビューが同じウィンドウにあり、注意が増えても押し出さないようにする。
- 文面は書き出しの結果一覧と同じものを使う。プレビューで別の文を持つと、結果一覧とずれる。見出しで書き出しの話だと分かるようにした。

## 検討した代替案と不採用理由

- `tolerateLostMorphs` も合わせる: 表情ボードも渡しておらず、プレビューはモーフが無いときの表示を別に扱っていない。今回は表情ボードと揃える範囲にとどめる。
- ビルド処理全体が失敗したときに処理を外して続ける（表情ボードの `WithoutBuildPassesWarning` の経路）: プレビューの中身が書き出しと違うものになるので、今までどおりエラーで止める。

## 確かめたこと

- `NdmfLostMorphFollowTests.ThePosePreviewAndTheBlinkPreviewLeaveOutAPassFailingInTransforming`: Transforming で失敗する NDMF の検証用プラグインを入れたアバターで、既定の設定を切ると両方止まること、既定では両方とも止まらず、Modular Avatar の処理を残し、失敗したパスの名前を `Notices` に持つこと、瞬きのプレビューで目を閉じる操作が効くこと、「適用しました」が `Notices` に入らないこと。
