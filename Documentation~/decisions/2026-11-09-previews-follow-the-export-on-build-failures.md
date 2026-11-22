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

- `tolerateLostMorphs` も合わせる: 両方のプレビューは `__VRVlog_*` のモーフを焼かないので確かめる対象が無く、渡しても動きは変わらない。
- ビルド処理全体が失敗したときに処理を外して続ける（表情ボードの `WithoutBuildPassesWarning` の経路）: プレビューの中身が書き出しと違うものになるので、今までどおりエラーで止める。
- Optimizing の処理だけが失敗するときに Optimizing を外してやり直す（書き出しの `SeparateOptimizing` の経路）: 要らない。両方のプレビューは `afterTransforming` を渡さないので、表情ボードと同じく Transforming の後で止まり、Optimizing の処理を走らせない。Optimizing だけで失敗するアバターでも、プレビューは止まらない（2026-11-10 に訂正。この記録は最初「プレビューはエラーで止まり、書き出しとの差として残る」と書いていたが、誤りだった）。

## 確かめたこと

- `NdmfLostMorphFollowTests.ThePosePreviewAndTheBlinkPreviewLeaveOutAPassFailingInTransforming`: Transforming で失敗する NDMF の検証用プラグインを入れたアバターで、既定の設定を切ると両方止まること、既定では両方とも止まらず、Modular Avatar の処理を残し、失敗したパスの名前を `Notices` に持つこと、瞬きのプレビューで目を閉じる操作が効くこと、「適用しました」が `Notices` に入らないこと。
- `NdmfLostMorphFollowTests.ThePosePreviewAndTheBlinkPreviewDoNotRunAFailingOptimizingPass`（2026-11-10 に追加）: Optimizing で失敗する NDMF の検証用プラグインを入れたアバターで、厳密な設定の書き出しは止まるが、両方のプレビューは厳密な設定でも既定でも止まらず、Modular Avatar の処理を残し、失敗したパスを `Notices` に持たないこと。`NdmfPreparationTests.CallbackFreePreparationStopsAfterTransformingAndRetainsOwnedAssetLifetime` は、コールバックの無い準備が Optimizing を呼ばないことを NDMF の代わりの部品で確かめている。
