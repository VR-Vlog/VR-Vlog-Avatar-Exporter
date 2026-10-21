# 書き出しの対策の一覧を読む式を `ExportRecoveryOptions.ActionsOrEmpty` にまとめる

- 日付: 2026-10-21
- 対象: `Editor/ExportRecovery.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`

## 背景

書き出しに失敗したときに選ぶ対策（`ExportRecoveryOptions.Actions`）を読む側は、`ExportRecoveryOptions` が `null` で渡されることに備えて、`options?.Actions ?? new List<ExportRecoveryAction>()` と、`null` なら空の一覧を作る同じ式を 5 か所に書いていた（`ExportRecovery.cs` に 4 か所、`ExperimentalExpressionCaptureSession.Export` に 1 か所）。

## 決定

`ExportRecoveryOptions` に `internal static List<ExportRecoveryAction> ActionsOrEmpty(ExportRecoveryOptions options)` を足し、5 か所をその呼び出しに替える。中身は置き換えた式そのまま。

## 置き換えなかった箇所

- `ExportFailureWindow.RefreshSession` の `session.Report?.Diagnostics ?? new List<ExportRecoveryDiagnostic>()`（2 か所）。対策ではなく診断（`ExportRecoveryReport.Diagnostics`）の一覧で、持ち主の型が違うので、そのままにした。
- `ExportRecovery.Apply` の `skipAllMenus`（`options?.Actions.Any(...) == true`）。`options` が `null` でなく `Actions` が `null` のとき、今の式は例外になり、`ActionsOrEmpty(options).Any(...)` に替えると例外にならず `false` になる。挙動が変わるので、そのままにした。

## 書き出す内容が変わらない理由

- `ActionsOrEmpty` は置き換えた式と同じく、`options` が `null` でなければ `options.Actions` のリストそのものを、`null` なら呼ぶたびに新しい空のリストを返す。戻り値の型も `List<ExportRecoveryAction>` のままなので、`foreach` の列挙のしかたも変わらない。
- 5 か所とも、返った一覧を `foreach` で読むか `Where` で絞るだけで、その一覧に追加・削除しない（`ExperimentalExpressionCaptureSession.Export` で追加するのは別に作った `mappedRecovery.Actions`）。
- 各箇所で `options` を読む回数と読む時点は置き換え前と同じ（材料スロットごとのループの中の箇所は、置き換え前と同じくスロットの数だけ読む）。

## 確認したこと

- 確認: 検証用の PC での書き出し突き合わせと EditMode テスト（PR 本文に記載）
