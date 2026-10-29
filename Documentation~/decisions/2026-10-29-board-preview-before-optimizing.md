# 表情ボードのプレビューは NDMF の Optimizing の前の状態を読む

- 日付: 2026-10-29
- 対象: `Editor/NdmfExportPreparation.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Tests/Editor/NdmfPreparationTests.cs`・`Tests/Editor/NdmfLostMorphFollowTests.cs`・`Tests/NdmfPlugin/LostMorphTestPlugin.cs`・`Tools/run-unity-compatibility.py`

## 背景

最適化系の非破壊ツールを入れたアバターで、表情がほとんど書き出されないという報告があった。

書き出しは、NDMF の Transforming が終わった時点（`afterTransforming`）でアバターの FX のアニメーションを読み、表情を作る。そのあとの Optimizing でシェイプキーの名前が変わったり、まとめられたり、消えたりしても、`ExportOptimizationBindings` が追従する。

表情ボードは、元のアバターの FX のアニメーションを一覧にし、各項目をプレビュー用のコピー（`ExperimentalExpressionCaptureSession.Copy`）のシェイプキーに当てて顔を作る。このコピーは `NdmfExportPreparation.Prepare` で作るが、ボードがコールバックを渡していたため、`ProcessClone` が Optimizing まで走らせていた。最適化系のツールがシェイプキーを並べ替えたりまとめたりすると、アニメーションが指すシェイプキーがコピーに無くなる。その項目はすべて「候補の BlendShape が表示中のメッシュにありません」のエラーになり、アプリに転送（かんたん）では使える項目だけを書き出す（`ExportedCustom`）ので、警告なしに表情から外れていた。

手元の 4 体で、最適化系のツールを足すと書き出される表情は 21→6・26→6・15→8・48→12 個に減っていた。テクスチャ圧縮のツールだけを足した場合は減らなかった（ボードの項目はテクスチャに関係しないため）。

## 決定

- `Prepare` と `ProcessClone` に `stopBeforeOptimizing` を足す。true のときはコールバックの後に Optimizing を走らせず、Finish だけを行う。
- 表情ボードのコピーは `stopBeforeOptimizing: true` で作る。ボードは、書き出しが表情を読むのと同じ Transforming の後の状態を見る。Optimizing での名前の変更・統合・削除への追従は、これまでどおり書き出し側（`ExportOptimizationBindings`）だけが行う。
- まばたきと姿勢のプレビュー（`BlinkPreviewWindow`・`PoseReviewWindow`）はコールバックを渡さないので、もともと Optimizing を走らせていない。これでプレビューはすべて Optimizing の前で止まり、Optimizing まで走らせるのは書き出しだけになる。
- 個々のツールを見分ける処理は足さない。どの Optimizing のパスでも同じ理由でボードの項目が壊れうるため。
- 書き出しは変えない。書き出しはこれまでどおり Optimizing まで走らせる。

## 確認

- `NdmfPreparationTests.PreviewWithCallbackStopsBeforeOptimizingAndStillFinishes`: `stopBeforeOptimizing: true` では Optimizing のパスが呼ばれず、コールバックは 1 回、Finish は 1 回、一時フォルダは消える。
- `NdmfLostMorphFollowTests.BoardReadsTheAvatarClipsBeforeOptimizingPassesRenameTheirShapes`: Optimizing でシェイプキーの名前をすべて変えるテスト用のパスを入れたアバター。書き出しの準備ではコピーのシェイプキーの名前が変わる（テスト用のパスが動いている）が、表情ボードの項目はエラーにならず、ボードのコピーは元の名前のまま。
- 手元の実アバター 7 体と実際のツールで、ツールの有無と設定の組み合わせ・ランダムな組み合わせを書き出し、例外・表情の数・マテリアルの欠けを比べた（結果は PR に記載）。

## 欠点

- 表情ボードのプレビューには Optimizing の結果（メッシュの統合・テクスチャの圧縮など）が映らない。ボードは顔を選ぶためのもので、書き出す VRM の見た目は書き出し側が Optimizing まで走らせて作るので、影響は見た目の確認だけにとどまる。
