# 翻訳された注記を転送後のページで読み戻す経路をEditorテストで押さえる

- 日付: 2026-11-02
- 対象: `Tests/LanTransfer/TransferNotesTests.cs`（本体は変えない）

## 背景

`TransferNotes.Read` は、注記の文面を日本語のテンプレートとEditorの言語に訳したテンプレート（`ExporterLocalization.T`）の両方で照合する。CLIのテスト（`Tools/run-lan-transfer-tests.ps1`）は `VRVLOG_LAN_TRANSFER_CLI` で訳した側の分岐を外してビルドするので、訳した文面で照合する経路にはテストが無かった。

## 決定

- `TransferNotesReadTheConversionWrittenInTheEditorLanguage` を `#if !VRVLOG_LAN_TRANSFER_CLI` の中に足し、en・ko・zh-Hans・zh-Hantの4言語で回す。
- 注記の文はエクスポートと同じ作り方で作る。lilToonの注記は `DependencyPolicy.LilToonWarning(version, ExporterLocalization.T)` を呼ぶ。表情の注記は `VrChatExpressionBaker` と同じく、`T` に通したテンプレートへ数を入れる。lilToonの版が読めない場合（`unknown`）は版の表示も訳されるので、`unknown` と `1.0.0` の2通りを回す。
- 作った文が日本語の文と違うことを先に確かめる。訳が無く日本語のまま書き出されると、テストは日本語の照合で通ってしまい、訳した側の経路を確かめたことにならないため。
- PhysBoneと画像の縮小の注記は日本語のまま書き出されるので、このテストの対象にしない。
