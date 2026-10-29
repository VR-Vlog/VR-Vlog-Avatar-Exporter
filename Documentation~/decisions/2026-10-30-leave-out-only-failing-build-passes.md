# 失敗したビルド処理だけを外して書き出し直す

- 日付: 2026-10-30
- 対象: `Editor/ExportStageGuard.cs`・`Editor/NdmfExportPreparation.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/ExpressionBoard.cs`・`Editor/Locales`・`Tests/Editor/ExportStageGuardTests.cs`・`Tests/Editor/NdmfLostMorphFollowTests.cs`・`Tests/Editor/ExpressionBoardTests.cs`・`Tests/NdmfPlugin/LostMorphTestPlugin.cs`

## 背景

2026-10-29-board-preview-before-optimizing で表情ボードが Optimizing の前で止まるようにしたあと、書き出しが失敗する経路をコードと手元の書き出しの組み合わせで洗い出した。残った不具合は 3 つあった。

- 失敗した段階を外す書き出しでは、NDMF のどのフェーズで失敗しても `ExportStage.BuildProcess` を外していた。Optimizing のパスだけが失敗するアバターでも、Modular Avatar の衣装の取り付けやメッシュの編集（Generating・Transforming）まで外れ、衣装が体に付かない VRM になっていた。最適化後に表情・マテリアル・ヒューマノイドが失われたときも同じだった。
- 表情ボードのコピーは Transforming まで走らせる。Transforming のパスが失敗するアバターでは、ボードを作れずにアプリに転送（かんたん）が VRM を書かずに止まっていた。書き出しのほうはビルド処理を外して書けるアバターだった。
- ボードで使えない項目（動かすシェイプキーが表示中のメッシュに無い表情など）は、アバター自身の表情なら一覧に出さず、書き出しからも黙って外していた。手元の組み合わせでは、顔のメッシュを非表示にしたアバターで表情がほとんど書き出されず、警告も出なかった。

## 決定

- `ExportStage.Optimizing` を足す。表示名は「骨格: 最適化ツールの処理」、外したときの扱いは `BuildProcess` と同じ近似（Approximated）。
- `NdmfExportPreparation.ProcessClone` は、Generating と Transforming が成功して Optimizing を始めたあとの失敗に印を付ける（`FailedInOptimizing`）。書き出しはその失敗を `ExportStage.Optimizing` の失敗として扱い、`stopBeforeOptimizing` で書き出し直す。それ以外のビルド処理の失敗は、これまでどおり `BuildProcess` を外す。
- 最適化後の検査（ヒューマノイド・表情とマテリアルの照合・出力のヒューマノイド）は、Optimizing を走らせたときは `Optimizing` の段階、外したあとは `BuildProcess` の段階で見る。Optimizing を外しても直らなければ、次にビルド処理を外す。
- 表情ボードは `ExperimentalExpressionCaptureSession.Create` で作る。ビルド処理が失敗し、失敗した段階を外す設定（`ExportStageGuard.ContinueByDefault`）が有効なときは、ビルド処理を外したコピーでボードを作り直し、そのことを警告に足す。書き出しは自分のコピーでビルド処理をもう一度走らせ、失敗すれば書き出し側の仕組みで外す。厳密な設定ではこれまでどおりボードの作成で止まる。
- 書き出しのとき、ボードで使えないため外した表情の件数と名前（5 件まで）を、理由ごとにまとめて結果一覧の注意に 1 行で出す（`ExpressionBoard.UnusableCustomNote`）。理由の違う表情が混ざっても、名前ごとの理由を取り違えない。書き出す中身は変えない。
- 通常とかんたんはどちらも `PrepareWith` と `Save` を通るので、両方に同じように効く。`interactive` は確認を出すかどうかだけを決める決まりのまま。

## 確認

- `ExportStageGuardTests.EveryStageCanFailAndTheFileIsStillWritten`（`Optimizing` を含む全段階）・`AnExpressionLostByTheOptimizerLeavesOutOnlyOptimizing`: 最適化後に表情が失われると `Optimizing` だけが外れる。
- `NdmfLostMorphFollowTests.AFailingOptimizingPassStopsNeitherTheBoardNorTheLenientExport`: Optimizing で失敗するテスト用のパスでは `Optimizing` だけが外れ、Modular Avatar で頭に付けたメッシュが VRM でも頭の下にある。
- `NdmfLostMorphFollowTests.ABoardWhoseBuildPassesFailIsMadeWithoutThem`: Transforming で失敗するテスト用のパスでは、厳密な設定ではボードの作成が止まり、失敗した段階を外す設定ではビルド処理を外したボードが警告つきで作られ、書き出しは `BuildProcess` を外して VRM を書く。
- `ExpressionBoardTests.UnusableFacesAreNamedForTheResultList`: 使えない項目だけを数え、ユーザーが外した項目は数えない。理由の違う項目は理由ごとにまとめる。
