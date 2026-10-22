# エクスポーターの各ウィンドウの見出しの右端にバージョンを出す

- 日付: 2026-10-23
- 対象: `Editor/Compatibility/ExporterWindowHeader.cs`（`Version`・`Draw`）・`Editor/LanTransfer/AppTransferWindow.uxml`・`Editor/LanTransfer/AppTransferWindow.uss`（`.version`）・`Editor/LanTransfer/AppTransferPanel.cs`・`Editor/ExportDetailsText.cs`（`Failure`）・`Editor/ExportRecovery.cs`（`BuildSupportText`）・`Editor/ExportFailureWindow.cs`・`Tests/Editor/ExportDetailsTextTests.cs`・`Tools/run-unity-compatibility.py`・`CHANGELOG.md`

## 背景

不具合の報告で届くスクリーンショットから、どのバージョンのエクスポーターで起きたかが分からなかった。バージョンが画面に出るのは **動作環境** の一覧だけで、書き出し・アプリに転送・失敗時のウィンドウには出ていなかった。書き出し結果の **全文をコピー** にはバージョンが入っていたが、失敗時のウィンドウの **全文をコピー**・**テキスト保存** と **共有用の診断をコピー** には入っていなかった。

## 決定

- 各ウィンドウが共通で使っている見出し（`ExporterWindowHeader`。ロゴと画面名の行）の右端に、灰色の小さい文字で `v0.12.0-beta.8` のように出す。見出しを使うウィンドウ（書き出し・書き出し結果・失敗時・瞬き・ポーズ・アプリでの見え方・スマホに送る・動作環境など）はこれ 1 か所の変更でそろう。UI Toolkit で作っている **アプリに転送** の見出しにも、同じ位置・同じ幅で出す。
- 表示の幅は 110px に固定し、画面名はその左までに収める（長い画面名は切れる）。バージョンの文字数やフォントで画面名の位置は変わらない。
- バージョンは `PackageInfo.FindForAssembly` で package.json の値を読む。手で書いた定数を足さないので、リリースの自動バンプでそのまま変わる。パッケージとして入っていないとき（`Assets` の下にコピーしたときなど）は何も出さない。
- 失敗時のウィンドウの全文の 1 行目に `VR Vlog Exporter <バージョン>` を、共有用の診断の 2 行目に `Exporter: <バージョン>` を足す。どちらも既存の行の並びは変えない。訳語の表を増やさないよう、どちらも訳さない固定の文字列にした。

## 確認

- `ExportDetailsTextTests.HeaderFailureTextAndSharedDiagnosticNameThePackageVersion` で、見出しの表示が `v` と package.json の `version`、失敗時の全文の 1 行目と共有用の診断にそのバージョンが入ることを確かめる。
- R09 の Unity 2022.3.22f1 で、変更前と変更後の各ウィンドウを幅を変えて撮って比べた（PR に画像）。

## 欠点

- 見出しの右端の 110px を常に取るので、幅の狭いウィンドウでは長い画面名が前より早く切れる。いちばん狭い **スマホに送る**（430px）でも、今の画面名は切れない。
