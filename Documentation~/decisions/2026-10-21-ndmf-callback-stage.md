# ビルド処理の途中で動く自前の処理の失敗を、その処理の段階として扱う

- 日付: 2026-10-21
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/ExportStageGuardTests.cs`・`CHANGELOG.md`

## 背景

書き出しは、Modular Avatar / NDMF のビルド処理（`NdmfExportPreparation.Prepare`）が終わった直後に呼ばれる `afterTransforming` の中で、表情・瞬き・同梱ポーズ・最適化ツールの照合の準備をしている。この呼び出し全体は `guard.Watch(ExportStage.BuildProcess, ...)` の中にある。中の処理のうち段階（`ExportStage`）を付けていたのは一部だけで、`RecordTrimmedPoses`・`NeutralShapeSnapshot.Capture`・`AuthoredExpressionEndpoints.Capture`・`RecordLostMenuExpressions`・`RecordSettledMenuExpressions`・`AvatarBaseShape.Preserve`・`authoredEndpoints.Bake`・`RecordExpressionsEndingNeutral`・`UnifiedExpressionPreparation` の作成・`ExportOptimizationBindings.Capture` は段階を付けずに呼んでいた。

段階の無い処理が例外を出すと、外側の `Watch(BuildProcess)` がビルド処理の失敗として包む。「失敗した段階を省いて続ける」設定では、ビルド処理を省いて書き出し直すため、衣装などが Modular Avatar で組み込まれていない VRM ができ、結果の一覧にはビルド処理が「近似」になった 1 行しか出ない。失敗したのはこちらの後処理で、ビルド処理は成功している。

## 決定

`afterTransforming` の中の段階の無い処理を、既存の段階の `guard.Watch` で包んだ。新しい段階は足していない。

- `RecordTrimmedPoses` は同梱ポーズ（`PoseCapture`）。
- 基本の顔・体形の値の正規化、`NeutralShapeSnapshot.Capture`、作者が選んだ表情の取り込みと焼き込み（`AuthoredExpressionEndpoints`）、`AvatarBaseShape.Preserve`、メニュー由来の表情の記録（`RecordLostMenuExpressions`・`RecordSettledMenuExpressions`・`RecordExpressionsEndingNeutral`）は、メニュー由来の表情（`Expressions`）。
- 焼き込んだ後の `UnifiedExpressionPreparation` の作成はフェイストラッキング（`TrackingOutput`）。
- `ExportOptimizationBindings.Capture` は最適化ツールの材質の照合（`OptimizerEvidence`）。

`Watch` は段階を省いた後も中身を動かすので、省いた段階で同じ失敗がもう一度起きたときは書き出しを止める。メニューの内容が原因の失敗なら、メニュー由来の表情を省いた書き出し直しで通る。

ビルド処理が変えた階層・形を追いかける処理（`RebindPrepared`・`FollowPrepared` など）は、ビルド処理の結果に左右されるのでこれまでどおりビルド処理の失敗として扱う。

## 入れなかったもの

- 新しい段階。基本の顔・体形のように省けない処理は、本来は段階に入れず書き出しを止めるのが筋だが、`afterTransforming` の中から外側の `Watch(BuildProcess)` を素通りさせる仕組みが要る。今回はメニュー由来の表情の段階に入れ、2 回目も失敗すれば止まる形にした。止まるまでに 1 回書き出し直す。

## 確認したこと

- `ExportStageGuardTests.AFailureInsideTheBuildCallbackKeepsTheBuildPasses` で、`afterTransforming` の中の `OptimizerEvidence`・`TrackingOutput` の処理で失敗させると、省かれるのはその段階だけで、ビルド処理は省かれずにファイルができることを確かめた。直す前はこの 2 つの段階の処理が `afterTransforming` の中に無く、テストは失敗する。
- 既存の `EveryStageCanFailAndTheFileIsStillWritten`・`StrictExportsStillStopAtTheFirstFailure` が通ることを確かめた。
