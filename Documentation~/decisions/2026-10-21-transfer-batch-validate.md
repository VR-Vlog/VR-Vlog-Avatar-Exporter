# アプリに転送（通常）の書き出しで元のアバターの確認を 1 回の処理につき 1 回にする

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`CHANGELOG.md`

## 背景

表情ボードのセッション（`ExperimentalExpressionCaptureSession`）は、操作のたびに `RequireValid` で元のアバターが準備したときから変わっていないかを確かめる。確かめるには元のアバター全体の `ExportSourceFingerprint.Compute` とメッシュのハッシュを取り直すので、実際のアバターでは 1 回に時間がかかる。

表情ボード自身のボタンの操作は `Advance` が `ExperimentalExpressionCaptureSession.Batch()` で包むので、1 つの Batch の中では 2 回目以降のハッシュを省く。Batch の中では同期で処理が進み、エディタのほかの操作は入らないので、元のアバターは変わらない。表情ボードの **保存** ボタンもこの Batch の中で書き出す。

**アプリに転送（通常）** の書き出しは、転送の画面のボタンから `AppTransferExport.ExportDetailed` → `ExperimentalExpressionCaptureWindow.TransferExport` → `Save` と直接呼ばれ、Batch の外で動いていた。そのため、`Save` の中で表情ボードの一覧を記録する `ReplaceClipRecords`、`Export` の中で標準の表情（喜怒哀楽・まばたき・口の形の枠）ごとに呼ぶ `BuildPresetPose` と `PresetShapeOptions` が、それぞれ元のアバター全体をハッシュし直していた。計測では 1 回の転送で、候補 32 個のアバターが 31 回、候補 77 個のアバターが 17 回ハッシュを取っていた（1 回あたり約 0.18 秒と約 1.6 秒）。

書き出しに失敗したあと、復旧の画面（`ExportFailureWindow`）から書き出し直すときも、`EditorApplication.delayCall` から `ExportRecoverySession.Attempt` を通して `Save` の書き出しの処理を呼ぶので、同じく Batch の外で標準の表情ごとにハッシュし直していた。

## 決定

- `TransferExport` の `Save` の呼び出しを `ExperimentalExpressionCaptureSession.Batch()` で包む。表情ボードの **保存** ボタンと同じ扱いになる。
- `Save` が `LilToonExporterWindow.ExportAndSaveNormally` に渡す書き出しの処理（`create`）の中身も Batch で包む。復旧の画面から書き出し直すときも、標準の表情は `Export` が取ったハッシュを使い、枠ごとにハッシュし直さない。表情ボードの保存と転送の 1 回目の書き出しでは外側の Batch の中に入れ子になるだけで、動きは変わらない。
- Batch は同期の範囲だけを包む。`Save` の最後に呼ぶ `ExportFailureWindow.Show` は復旧の画面を開くだけで、画面のボタンからの書き出し直しはあとのフレームで動くので、`Save` の Batch の外になる。結果一覧で使う人に尋ねてから保存する場合（`ExportFallbackConfirmation`）も、あとで動くのはファイルの書き込みと転送の開始だけで、元のアバターは確かめない。

## 変えないこと

- `Export` の最初の `RequireValid(always: true)` は残す。Batch の中でも、書き出すときは必ず元のアバターをハッシュし直す。書き出したファイルが、準備したときと違うアバターから作られるのを防ぐ最後の確認で、表情ボードを開いたあとに元のアバターを編集してから転送すると、これまでどおり「元のアバターが変更されました」で止まる。
- 1 回の転送では、表情ボードの一覧を記録するときの 1 回と `Export` の 1 回の、計 2 回だけ確かめる。表情メニューから読んだ表情が一覧より少なく、一覧の表情を記録して書き出し直すとき（`ExportRecorded`）は、その `Export` の分 1 回増える。
- 書き出す内容は変わらない。

## 確認したこと

- Unity 2022.3 の EditMode テストを回した（件数と結果は PR 本文）。足したテストは次の 3 つ。
  - `ATransferChecksTheSourceForTheRecordsAndTheExportOnly`: 標準の表情を 3 つ割り当てた表情ボードから `TransferExport` で 1 回転送すると、元のアバターの確認（`ExpressionBoardTimings` の `validate`）が 2 回以下で、ファイルが保存される。
  - `ATransferAfterAnEditOfTheSourceStops`: 表情ボードを開いたあとに元のアバターを編集してから転送すると、「元のアバターが変更されました」で止まり、何も保存しない。
  - `ExportInABatchStillSeesAnEditOfTheSource`: Batch の中で一覧を記録したあとに元のアバターを編集すると、標準の表情の確認は省かれるが、`Export` は編集に気づいて止まる。
- 実際のアバター 30 体（成功 27 体・失敗 3 体）を、変更前と変更後のパッケージで 2 回ずつ書き出し、`Tools/compare-exports.py` で突き合わせた。60 組とも、成否・失敗文・書き出し詳細の警告・表情の名前と数と全フィールド・glb の JSON と BIN のハッシュが一致した。軽いアバターと重いアバターを Unity を起動し直して 5 回続けて書き出した 10 組も一致した。
- 同じ 2 体の 2〜5 回目の書き出し時間の中央値は、軽いアバター（候補 32 個）が 19.3 秒から 12.9 秒、重いアバター（候補 77 個）が 96.3 秒から 64.8 秒になった。ハッシュを取る回数は 31 回から 3 回、17 回から 2 回に減った。
- 同じアバターを続けて書き出すと 1 回ずつ遅くなるように見えた件は、ハッシュでたどるオブジェクトの数とアセットの数が回ごとに変わらず、1 回あたりの時間もほぼ一定だった。計測に使った PC で別の Unity が動いていた負荷によるぶれと見て、ここでは扱わない。

## 書き出し結果の突き合わせ

以後の速度の改善でも書き出す内容が変わっていないことを確かめられるように、`Tools/run-export-regression.ps1`・`Tools/ExportRegressionRunner.cs`・`Tools/compare-exports.py` を足した。使い方は `CONTRIBUTING.md` の「Checking that a change keeps the export identical」。同じパッケージで 2 回書き出すと、焼き込んだ発光の画像の名前の末尾に付く GUID だけが毎回変わるので、比較ではその末尾だけを置き換えてから突き合わせる。
