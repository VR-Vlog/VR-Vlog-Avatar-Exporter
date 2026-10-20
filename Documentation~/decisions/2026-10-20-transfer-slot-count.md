# アプリに転送の「VRMの表情 ○/5」を表情ボードの「割当○/13」と同じ数え方にする

- 日付: 2026-10-20
- 対象: `Editor/AppTransferExport.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExpressionSlotLayoutTests.cs`・`CHANGELOG.md`

## 背景

**アプリに転送（通常）** の設定の 1 行（`SettingsSummary`）は「VRMの表情2/5」のように、感情の 5 枠だけを数えていた。同じ表情ボードの見出しは、#247 で上段を感情・まばたき・口の形の 13 枠にしてから「割当8/13」と 13 枠を数えている。同じ割り当てを見て数字が食い違うので、どちらが正しいのか分からない。

## 決定

設定の 1 行も表情ボードの見出しと同じ 13 枠（`MainPresets`）を数え、分母も 13 枠の数から出す（「VRMの表情8/13」）。分母を文字列に固定しないので、上段の枠の数が変わっても見出しとずれない。

視線（4 枠）と無表情（1 枠）は、表情ボードの見出しと同じく数えない。折りたたみを開いたときだけ見える枠で、見出しも数えていないため。

## 入れなかったもの

- 「未確認○」（自動で入って確定していない枠の数）を設定の 1 行にも出すかどうか。1 行が長くなるので、今回は見出しにだけ出す。

## 確認したこと

- `ExpressionSlotLayoutTests` に、設定の 1 行の分母（`MainSlotCount`）が表情ボードの 13 枠と同じことを確かめる行を足した。
- `ExpressionSlotLayoutTests`・`ExportFallbackConfirmationTests`・`ExperimentalExpressionCaptureTests` の 157 件のうち 153 件が通過。落ちた 4 件は、この PC に入っていない別パッケージの版に依存するもので、統合ブランチでも落ちる。
- 英語・韓国語・中国語（簡体・繁体）の訳も、分母を `{1}` に、件数を `{2}` にずらした。
