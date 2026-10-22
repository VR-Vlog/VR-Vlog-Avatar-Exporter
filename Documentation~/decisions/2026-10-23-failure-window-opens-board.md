# 再読み込み後の「書き出し画面を開く」は表情ボードを開く

- 日付: 2026-10-23
- 対象: `Editor/ExportFailureWindow.cs`・`CHANGELOG.md`

## 背景

書き出しに失敗したときの画面（`ExportFailureWindow`）は、Unity の再読み込みで確認内容が消えると「書き出し画面から再度書き出してください」と **書き出し画面を開く** のボタンを出す。このボタンは旧書き出し画面（`LilToonExporterWindow`）を開いていた。

メニューの **VRMを書き出す...**、Hierarchy の右クリック、**アプリに転送** は、どれも表情ボード（`ExperimentalExpressionCaptureWindow`）から書き出す。ボードは `Session.Export` に空の `recordedExpressions`・`replaceCustomFaces: false`・ボードの一覧（`listFaces`）を渡し、表情はボードの一覧から作る（2026-10-22-list-only-expressions.md）。旧書き出し画面は `ExportSettings.Export` から `listFaces` を渡さずに書き出すので、表情メニューと FX を再生して表情を作る。このため、このボタンから書き出したときだけ、ボードで書き出したときと違う表情の VRM になることがあった。製品のコードから旧書き出し画面を開くのはこのボタンだけ。

## 決定

- ボタンは `DependencyDiagnostics.Open()` を呼ぶ。メニューの **VRMを書き出す...** と同じで、書き出せる状態ならボードを開き、パッケージが足りないときは対策画面を開く。
- 失敗画面が出ている時点で確認内容（`ExportRecoverySession`）は消えているので、引き継ぐものは無い。「段階が失敗しても続ける」の既定は、ボードが書き出すたびに自分で設定する（`ExperimentalExpressionCaptureWindow` の書き出しの直後）。旧書き出し画面の `ExportSettings.Export` が設定していたのと同じ値になる。
- 文言は変えない。

## そのままにしたこと

- 旧書き出し画面のコード（`LilToonExporterWindow` の画面・`ExportSettings.Export`）と、`Session.Export` のボード以外の分岐はこの変更では消さない。製品のコードから使われなくなったので、別の変更で消す。
- テストは足さない。旧書き出し画面を開く呼び出しは、画面のコードを消す変更でコンパイルできなくなり、そこで確かめられる。
