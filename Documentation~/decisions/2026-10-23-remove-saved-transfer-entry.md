# 表情ボードの保存結果から転送ウィンドウを開く入口を消す

- 日付: 2026-10-23
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`（`OpenSavedTransfer`）・`Editor/LilToonExporterWindow.cs`（`SavedVrmTransferMethod`）・`Editor/SavedExpressionVrm.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Tests/LanTransfer/CloudTransferWindowTests.cs`

## 背景

古い書き出しウィンドウを消したとき（2026-10-23-remove-legacy-window-export）に、表情ボードで保存したVRMを開発用の転送ウィンドウで開く入口（`OpenSavedTransfer` と、それをリフレクションで探す `SavedVrmTransferMethod`）が残った。どのボタン・メニューからも呼ばれておらず、テストだけが呼んでいた。`SavedExpressionVrm` には、この入口のために保存したファイルが差し替えられていないかを確かめる処理（ハッシュ・`VerifiedPath`）があった。

## 決定

- `OpenSavedTransfer`・`SavedVrmTransferMethod` と、`SavedExpressionVrm` のハッシュと `VerifiedPath` を消す。`SavedExpressionVrm` は保存したファイルのパスと表情・ポーズ・足りないシェーダーの数だけを持つ。
- 消した入口だけを確かめていたテスト（`LastSavedOutputCannotTransferAReplacedOrMissingFile`・`ExpressionCaptureSavedResultOpensAnImmutableCopyWithoutUploading`）と、`OptionalExporterEntryMatchesInstalledReleaseChannel` の中の `SavedVrmTransferMethod` の確認を消す。
- 開発用の転送ウィンドウ自身が使う `CloudVrmTransferWindow.OpenSavedVrmForDevelopment` は残す。
- 画面・書き出す内容は変わらないので、CHANGELOG には書かない。

## 確認

- `OpenSavedTransfer`・`SavedVrmTransferMethod`・`VerifiedPath` がリポジトリのどこからも参照されていないことを grep で確かめた。
- `ExperimentalExpressionCaptureTests` と `CloudTransferTests` が Unity 2022.3 で通ることを確かめた。
