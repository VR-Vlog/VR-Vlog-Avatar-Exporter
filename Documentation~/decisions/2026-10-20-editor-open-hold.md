# 表情ボードを開いた直後の約 20 秒の「Hold on」をなくす

- 日付: 2026-10-20
- 対象: `Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`CHANGELOG.md`

## 背景

**アプリに転送（通常）** を開くと、Unity の「Hold on」が約 20 秒出ていた。スタックは `ExperimentalExpressionCaptureWindow.Advance` を指していたが、表情の読み込みとサムネイルのどちらに時間がかかっているかは分かっていなかった。

## 原因

サムネイルではなく、表情ボードの準備（`Prepare`）だった。`SuggestPresets` と `RefreshPresetErrors` が候補と 13 枠のそれぞれで `PresetError` → `BuildPresetPose` を呼び、その中の 2 つの処理が重かった。

1. `HasGeometryChange` が、形が変わるかを確かめるために顔のメッシュを BlendShape ごと複製していた（1 回 580 ms・`Prepare` で 30 回）。
2. `RequireValid` が呼ばれるたびに、元のアバター全体とメッシュのハッシュを取り直していた（1 回 70〜500 ms）。

60k 頂点・53 BlendShape の顔を持つテスト用のアバターで、`Prepare` は 21.4 秒かかっていた。

## 決定

- `HasGeometryChange` は、BlendShape の差分を先に計算し、すべて有限で頂点が動くときは複製せずに「変わる」と答える。それ以外は従来の複製による確認をそのまま行うので、答えは変わらない。
- 1 回の更新の中の処理（`Advance` のキューの処理・`AdvanceIdle`・`Prepare`）を `Batch()` で囲み、その中ではアバター全体とメッシュのハッシュを 1 回だけ取る。レンダラーが無い・無効・メッシュが差し替わった、の安い確認は毎回行う。書き出し（`Export`）は囲みの中でも必ずハッシュを取り直す。

同じテスト用のアバターで `Prepare` は 2.0 秒になった。

## 入れなかったもの

- 準備を複数の更新に分けてウィンドウの中に進み具合を出すこと。残りの 2 秒の大半は NDMF のコピーを作る 1 回の呼び出しで分けられず、`Prepare` を同期で呼ぶテストの作りも変わるため。重いアバターで短い「Hold on」が残る場合は、改めて段階的に準備する形を考える。
- 頂点が動かず法線だけ変わる表情の高速化。複製による確認のままで、時間がかかるだけで結果は同じ。

## 確認したこと

- `TheGeometryCheckAgreesWithTheMeshCopy`: 頂点が動く・法線だけ・動かない、の 3 つで、速い確認と複製による確認の答えが同じ。
- `ABatchChecksTheSourceOnceAndTheNextOneSeesAnEdit`: 囲みが終わった後・次の囲み・囲みの外で、元のアバターの変更を見つける。入れ子の囲みも正しく終わる。
- `ExperimentalExpressionCaptureTests`・`ExpressionBoardTests`・`ExpressionSlotLayoutTests` の 162 件のうち 158 件が通過。落ちた 4 件はテスト用のプロジェクトに入っていない別パッケージを要求するもので、この変更と関係しない。
- 実際のアバターでの再現と計測はしていない（テスト用のアバターでの計測だけ）。
