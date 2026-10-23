# メニューの「VRMを書き出す...」と書き出し画面を消す

- 日付: 2026-10-23
- 対象: `Editor/Compatibility/ExporterMenu.cs`・`DependencyDiagnostics.cs`、`Editor/ExportFailureWindow.cs`・`ExportRecoveryComparisonWindow.cs`・`ExperimentalExpressionCaptureWindow.cs`・`StepIndicator.cs`・`AppTransferExport.cs`・`LanTransfer/AppTransferPanel.cs`、`Editor/Locales/ExporterLocale_*.json`、`Tests/Editor/VrVlogMenuExportTests.cs`（削除）・`ExperimentalExpressionCaptureTests.cs`・`QuickTransferMatchesDefaultNormalTests.cs`、`Tests/LanTransfer/VrVlogMenuTests.cs`、`Tests/Compatibility/DependencyStartupTests.cs`・`DependencyEnvironmentTests.cs`、`Tests/Startup/ExporterStartupProbe.cs`

## 背景

2026-10-23に、mkさんからメニューバーの **VR Vlog** とHierarchyの右クリックの **VR Vlog** の両方から **VRMを書き出す...** と、それで開く画面を消すよう依頼があった。

この画面は、表情ボード（`ExperimentalExpressionCaptureWindow`）を書き出しのモード（タブ名「VRM書き出し」）で開いたものだった。同じ表情ボードを転送のモードで開いたものが **アプリに転送（通常）** で、3ページ目には転送の画面と **VRMを保存** がある。VRMファイルを保存する手段は、書き出しのモードが無くても残る。

## 決定

- 両方のメニューから **VRMを書き出す...** を消す。メニューは **アプリに転送（かんたん）...** と **アプリに転送（通常）...** の2つだけになり、区切り線も出さない（優先度の差は1）。
- 表情ボードのクラスと書き出しの処理（`SaveVrm`・作者名の欄・ライセンス）は残し、書き出しのモードとその入口（`Open`・`OpenFor`・`ChooseForExport`・`LeaveTransferMode`・自前の3ページ目「3 VRMの情報」）を消す。表情ボードはいつも **アプリに転送（通常）** として開き、タブと見出しもその名前にする。保存したレイアウトから復元したボードも、描画のときに同じ名前に直す。
- `AppTransferExport.SelectAvatar` が、自前の3ページ目を出しているボードから作者名とライセンスを借りていた処理を消す。そのページが無くなったので、借りる相手が無い。
- 依存パッケージの診断（**VR Vlog 動作環境**）は、**アプリに転送** の画面にある **VR Vlog 動作環境** のボタンから開く。診断の画面の **書き出し画面を開く** は消す。本体の入口を探す処理は `DependencyDiagnostics.BackendReady` にし、キャッシュと見つからないときの文言は前と同じにする。
- 書き出しに失敗したあとUnityの再読み込みで確認内容が消えたときの画面から **書き出し画面を開く** を消し、文言を「Unityの再読み込みで確認内容が無効になりました。VR Vlogメニューの「アプリに転送」からもう一度書き出してください。」にする。4言語の翻訳を足す。使わなくなった翻訳の行は、未使用の行を検査する仕組みが無いので残す。
- VRMファイルだけを保存するときは、**アプリに転送（通常）** の3ページ目の **VRMを保存** を使う。README・ガイドもそう案内する。

## 確認

- 転送が使えない版では、**VR Vlog** のメニューに項目が1つも出ない。`CloudTransferAvailability.Enabled` は開発用のdefineがある場合を除き、`IsSupportedVersion` が真の版（安定版 **0.11.15** とプレリリース版）でだけ真になる。転送を採用していない次の安定版をそのまま出すと、書き出しの入口が無くなる。次の安定版を出すときは、転送を採用する（`IsSupportedVersion` を更新する）。
- `CloudTransferAvailability.DisabledMessage` の「VRMをファイルに書き出して」は、転送が使えない版では従えない案内になる。上の対応で転送が使える版だけを出すなら表示されないので、この変更では直していない。
- `ExportRecoveryComparisonWindow` の「書き出し画面から再度書き出してください」も、失敗画面と同じ文言に変えた（消した画面を指していたため）。
- 検証用のPCのUnity 2022.3で、関係するEditModeテストを回した。メニューに2項目だけ出ることは、メニューバーとHierarchyの右クリックの両方を画面で確かめた。
