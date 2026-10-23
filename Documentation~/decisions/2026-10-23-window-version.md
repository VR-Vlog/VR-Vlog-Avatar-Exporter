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
- 全文と共有用の診断のバージョンは、書き出し結果の全文がすでに使っている `LilToonExporterWindow.PackageVersion()` から取る。パッケージとして入っていないときは見出しには何も出ないが、全文と診断にはソースに書いた値（`Tools/validate.py` が package.json と一致するかを確かめている）が出る。見出しは `Compatibility` のアセンブリにあり `LilToonExporterWindow` を参照できないため、このずれは残した。
- 書き方は場所ごとに違う（見出しは `v0.12.0-beta.8`、全文は `VR Vlog Exporter 0.12.0-beta.8`、診断は `Exporter: 0.12.0-beta.8`）。画面ではほかの文字と区別できるよう `v` を付け、貼り付ける文字列では何のバージョンかを名前で示した。

## 確認

- `ExportDetailsTextTests.HeaderFailureTextAndSharedDiagnosticNameThePackageVersion` で、見出しの表示が `v` と package.json の `version`、失敗時の全文の 1 行目と共有用の診断にそのバージョンが入ることを確かめる。
- Unity 2022.3.22f1（エディタの拡大率 300%）で、変更前と変更後の各ウィンドウを幅を変えて撮って比べた（PR に画像）。`v0.12.0-beta.8` の描画幅は約 70px で、110px の枠に収まる。

## 検討して採らなかった案

- バージョンの幅を文字に合わせて変える案は採らなかった。フォントの差で画面名の位置が動くため。`-beta.10` のように伸びても 110px の枠に収まる。
- 見出しの下に 2 行目を足す案と、ウィンドウの下端に出す案は採らなかった。前者は見出しが高くなり、後者は上だけのスクリーンショットに写らないため。

## 欠点

- 見出しの右端の 110px を常に取るので、幅の狭いウィンドウでは長い画面名が前より早く切れる（省略記号なしで文字の途中で切れる）。最小幅がいちばん狭い **瞬きを確認** と **スマホに送る**（360px）では画面名に約 164px 残り、各言語でいちばん長い `휴대폰으로 보내기` も収まる。
- **アプリに転送** では、見出しの下の行（送るアバターの名前）も同じ幅だけ狭くなり、長い名前が前より早く「…」になる。最小幅 420px で約 342px から約 224px になる。
