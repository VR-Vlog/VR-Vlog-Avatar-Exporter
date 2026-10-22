# 停止中の旧LAN転送のコードと暗号ライブラリを消す

- 日付: 2026-10-23
- 対象: `Editor/LanTransfer/LanVrmTransferServer.cs`・`LanVrmTransferWindow.cs`・`LanTransferProtocol.cs`・`TransferTempDirectory.cs`・`Dependencies/BouncyCastle.Cryptography.dll`・`VRVlog.LanTransfer.Editor.asmdef`、`Tests/LanTransfer/LanTransferTests.cs`・`PhoneTransferDisabledTests.cs`・`CloudTransferTests.cs`・`CloudTransferWindowTests.cs`・`VRVlog.LanTransfer.Editor.Tests.asmdef`、`Tools/run-lan-transfer-tests.ps1`・`LanTransferTestHarness.cs`・`build-package.py`・`test-package.py`、`ThirdPartyNotices/LanTransfer.md`・`BouncyCastle-LICENSE.txt`

## 背景

同じLANのiPhoneへ直接送る旧LAN転送（**スマホに送る**）は0.11系で無効にし（`LanTransferAvailability.Enabled` は常に `false`）、QRのクラウド転送に置き換えた。無効にしたあとも、待受サーバー・転送ウィンドウ・TLS用の暗号ライブラリ Bouncy Castle（`BouncyCastle.Cryptography.dll`、約4.9MB）がパッケージに残っていた。Bouncy Castle を使っていたのはLAN転送のTLSだけで、クラウド転送は .NET 標準の暗号処理を使っている。

## 決定

- `LanVrmTransferServer.cs`（`LanTransferAvailability`・LAN転送だけが使う `TransferState` を含む）と `LanVrmTransferWindow.cs` を消す。
- クラウド転送も使っている `LanVrmTransferServer.TryDelete` は `TransferTempDirectory.TryDelete` に移し、呼び出し元（`AppTransferFlow`・`CloudDevelopmentSnapshot`・`CloudTransferEncryption`・`CloudVrmTransferSession`・`CloudVrmTransferWindow`・`TransferTempDirectory`・`CloudTransferTests`）を書き換える。中身は変えない。
- `LanTransferProtocol` からLAN転送だけが使っていた `IsPrivateIPv4`・`RandomBytes`・v1の `QrPayload`・`Request`（`Authenticated`）・`ReadRequest`・`MaximumSize`・`MaximumHeaderBytes`・`MaximumQrBytes` を消す。クラウド転送が使う `QrPrefix`・`Hex`・`Base64Url`・`DisplayName`・`JsonString` は残す。
- `BouncyCastle.Cryptography.dll` とその `.meta`・`ThirdPartyNotices/BouncyCastle-LICENSE.txt` を消し、2つの asmdef の `precompiledReferences` から外す。`ThirdPartyNotices/LanTransfer.md` は ZXing.Net の分だけ残す。
- `Tools/build-package.py` の固定ハッシュの表から Bouncy Castle を外す（zxing.dll だけを固定する）。

消したテストと、代わりに守っているもの:

- `LanTransferTests.DisabledProductionTransferRejectsBeforeOpeningAMissingSnapshot`: 無効のLAN転送を始めようとすると例外になることの確認。始める入口（`LanVrmTransferServer.Start`）ごと消したので、守る対象が無い。
- `LanTransferTests.DisabledProductionTransferKeepsTheCallerSnapshotAndOwnership`（2ケース）: 無効のLAN転送が呼び出し元の一時VRMを消さないことの確認。同上で守る対象が無い。クラウド転送の一時VRMの扱いは `CloudTransferTests`・`CloudTransferWindowTests` が確かめている。
- `PhoneTransferDisabledTests.PublicTransferEntryPointsCannotOpenAWindowOrUseTheLocalExport`: LANの転送ウィンドウを開く入口が例外になることの確認。ウィンドウのクラスごと消したので、守る対象が無い。
- `PhoneTransferDisabledTests.RestoredTransferWindowCannotStartFromStaleSnapshotOrAddressState`: Unityが前回のLAN転送ウィンドウを復元しても待受を始めないことの確認。クラスが無いと復元されないので、守る対象が無い。
- `PhoneTransferDisabledTests.DisabledRestoredWindowStopsAnOldListenerAndReleasesItsSnapshot`（2ケース）: 復元されたウィンドウが古い待受を止めることの確認。同上で守る対象が無い。
- `CloudTransferTests.CloudDevelopmentAvailabilityRequiresBothEditorAndExplicitDevelopmentDefine` と `CloudTransferWindowTests.TransferAvailabilityRemainsEnabledWithoutAddingAnotherToolbarMenu` からは、`LanTransferAvailability.Enabled` が `false` であることを確かめる1行ずつを消す。テストの残りはそのまま。

## そのままにしたこと

- ファイル名・クラス名・フォルダ名・asmdef名の `LanTransfer`（`LanTransferProtocol`・`Editor/LanTransfer/`・`VRVlog.LanTransfer.Editor` など）は変えない。名前の付け替えは別のPRで行う。
- `Tools/run-lan-transfer-tests.ps1` と `Tools/LanTransferTestHarness.cs` は消さない。Unity無しでクラウド転送のテスト（`CloudTransferTests`）を回すのにも使っているので、LAN転送の部分だけを外した。`Tools/run-validation.py` と CI のワークフローは変えない。
- `Tools/build-package.py` の除外の規則（`TRANSFER_DLLS` と `TRANSFER_REFERENCE`）には `BouncyCastle` の名前を残す。同じ名前のDLLや参照がまた入っても、パッケージに入らないようにするため。
- 翻訳ファイルに残っているLAN転送の文言は消さない。
- `Documentation~/LanTransfer.md` は開発記録として残し、冒頭にコードを消したことを書き足した。過去の決定記録は書き換えない。
