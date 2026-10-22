# 旧LAN転送のサーバーとプロトコルのテストを消す

- 日付: 2026-10-23
- 対象: `Tests/LanTransfer/LanTransferTests.cs`・`Tests/LanTransfer/CloudTransferTests.cs`・`Tests/Editor/ExportFlowTests.cs`

## 背景

mk が 10/23 に「テストって肥大化しすぎているかな？もしそうであれば、明らかに不要なものは削除しておいてほしい」と依頼した。`Tests/` と `Tools/` のテスト用ハーネス（192ファイル・テストメソッド1,275個・`[TestCase]` 2,895個）を棚卸しし、次のどれかに当たるものだけを「明らかに不要」とした。

- もう無い機能・コード経路のテスト
- ずっと Ignore・スキップのままで直す予定がないテスト
- 同じものを同じ入力で確かめている重複
- 同じ分岐しか通らない `[TestCase]` の組み合わせ

迷うもの、不具合の再発を防ぐ回帰テストは残す。

## 決定

- 旧LAN転送（**スマホに送る**）のサーバーとプロトコルのテスト13メソッド・25ケースを消す。LAN転送は0.11系で無効にし（`LanTransferAvailability.Enabled` は常に `false`）、QRのクラウド転送に置き換え済み。消したテストは、本番では起動しないサーバーをテスト専用の入口（`StartLoopbackForTests`）で起こして確かめていた。
- 残すのは「無効のまま」を守る2メソッド・3ケース（`DisabledProductionTransferRejectsBeforeOpeningAMissingSnapshot`・`DisabledProductionTransferKeepsTheCallerSnapshotAndOwnership`）。誤って有効に戻すとここで落ちる。
- 消したテストのうち、クラウド転送でも使っている部分は次のとおり他のテストに残っている。
  - 名前の256文字の上限とQRのサイズ上限: `CloudTransferTests.MaximumUnicodeCloudNameMatchesTheWorkerUtf16LengthBound`・`ImportLinkAcceptsEveryNameTheCloudDisplayBoundAllows`
  - QRの符号化と読み戻し: `AppTransferFlowTests`（`TransferQrCode.Encode` と `QRCodeReader`）
  - 対になっていないサロゲートを落とす処理は他で確かめていなかったので、`MaximumUnicodeCloudNameMatchesTheWorkerUtf16LengthBound` に1行移した（`CloudTransferProtocol.DisplayName` は `LanTransferProtocol.DisplayName` を使っている）。
- `ExportFlowTests.ExportAndSaveNormally` の中の、書き出したVRMをLANサーバーへ流して読み戻す4行を消す。保存先のファイルの中身が書き出し結果と一致することはこのテストの前半で確かめている。
- 製品コードの `LanVrmTransferServer` などは残す（このPRはテストだけ）。LAN転送を戻すときは、このPRの1つ前のコミットからテストを戻す。

## 消さなかったもの

- `[TestCase]` の多いクラス（`UnifiedExpressionExportTests` 256ケースなど）は、行ごとに入力と期待値が違う場面で、同じ分岐しか通らないとは言い切れないので残した。完全に同じ行の重複は無かった。
- 本文の似ているテストの組（類似度0.9以上が31組）は、入力が違う別の場面だった。
- `Assert.Ignore` はどれも SDK・lilToon・グラフィックスデバイスなどが無い環境でだけ飛ばすもので、ずっと飛んでいるテストは無かった。
- `Tools/` のハーネスは同じテストをUnity無しで回すためのもので、重複ではない。
- 走行中のPRが触っているテストファイルは触っていない。

## 件数

- EditMode のケース数: 4,539 → 4,514（−25）
- `LanTransferTests` の行数: 409 → 42
