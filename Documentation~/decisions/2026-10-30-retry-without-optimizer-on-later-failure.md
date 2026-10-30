# ビルド処理のあとの失敗も最適化ツールを外して書き出し直す

- 日付: 2026-10-30
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/NdmfLostMorphFollowTests.cs`

## 背景

2026-10-30-leave-out-only-failing-build-passes で、ビルド処理と最適化後の検査が失敗したときは Optimizing、次にビルド処理を外して書き出し直すようにした。書き出し直すのは、段階（`ExportStage`）を持つ処理が `ExportStageFailure` を投げたときだけだった。

ビルド処理のあとには段階を持たない処理がある。UniVRM による VRM への変換と、その前後の組み立てである。最適化ツールが残したメッシュやボーンの形が原因でここが例外を投げると、失敗した段階を外す設定でも書き出し全体が止まっていた。ビルド処理を外せば書けるアバターでも VRM を書けなかった。

## 決定

- 書き出しのコピーでビルド処理を走らせたあと、どの段階のものでもない例外が出たら、走らせたほうの段階（Optimizing、Optimizing を外したあとは `BuildProcess`）の失敗として扱う。仕組みは既存のものを使い、その段階を外して書き出し直す。
- 対象は、失敗した段階を外す設定（`ContinueOnFailure`）のときだけ。取り消し・マテリアルの上限超過（`MatcapApproximation.OverLimit`）・すでに段階の付いた失敗は、これまでどおりに扱う。
- ビルド処理を走らせていない書き出しで出た例外は、これまでどおり書き出しを止める。外せるものが残っていないので、同じ失敗を繰り返さない。
- 厳密な設定では変えない。通常とかんたんはどちらも同じ `Export` を通るので、両方に同じように効く。

## 確認

- `NdmfLostMorphFollowTests.AFailureAfterTheBuildPassesWritesTheFileAgainWithoutTheOptimizer`: テストからの注入で、変換の直前に 1 回目だけ例外を投げる。2 回目で VRM が書け、外れたのは `Optimizing` だけで、Modular Avatar で頭に付けたメッシュは頭の下にある。毎回投げると 3 回目（ビルド処理なし）の例外がそのまま出て止まる。修正前のコードではこのテストは失敗する。
- 最適化ツールの 2 つのバージョンについて、全コンポーネントを 1 つずつ付けたアバターを書き出した。修正前と比べ、結果が変わったケースは 0 件だった。
