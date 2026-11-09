# かんたん転送の「VRMを保存」でもボードが止めた理由を出す

- 日付: 2026-11-09
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`

## 背景

かんたん転送では表情ボードは画面に出ない。QR の転送（`TransferExport`）はボードが書き出しを止めたときに「何を直すか」を添えて返すが、同じ画面の「VRMを保存」（`SaveTransferCopy`）は「表情の割り当てを直すと作れます」の一文だけを返していた。ボードが見えないので、利用者はどこを直せばよいか分からない。

## 決定

- 止めたときの文を `BlockedText` に 1 か所にまとめ、`SaveTransferCopy` は理由を付ける。
- `TransferExport` の分岐は今のまま（`interactive` のときだけ理由を付けない）。`SaveTransferCopy` は対話の経路だが、開いているボードではボタンが押せないので、ここに届くのは隠れたボードだけ。
- 転送画面の行は 1 行目だけを出し、全文はツールチップに出る（`AppTransferPanel`）。ほかの複数行の失敗と同じ扱いで、画面の高さは変わらない。

## 検討した代替案と不採用理由

- 保存ダイアログの前に止める: ボードの状態の確認の順番が変わり、変更が大きくなる。今回は文だけを直す。

## 確かめたこと

`AppTransferQuickExportTests.ASaveFromABlockedHiddenBoardSaysWhatToFix` は修正前に失敗し（理由の行が無い）、修正後に成功する。書き出す中身には触っていない。
