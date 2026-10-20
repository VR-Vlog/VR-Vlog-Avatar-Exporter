# コンストレイントの変換で循環を書かず、VRChat のコンストレイントを書き出し用のコピーに残す

- 日付: 2026-10-21
- 対象: `Editor/ConstraintExport.cs`・`Editor/NdmfExportPreparation.cs`・`Editor/Locales/*.json`・`Tests/Editor/ConstraintExportTests.cs`・`CHANGELOG.md`

## 背景

2026-10-19 に入れたコンストレイントの変換（`2026-10-19-vrm-node-constraint.md`）のレビューで、2 つの問題が見つかった。

1 つ目は循環。変換は、ソースが自分自身のときと、対象に既にVRMのコンストレイントがあるときだけを省いていた。兄弟の A と B の基準の向きが同じで、A に作者が付けた VRM の回転コンストレイント（ソースは B）があり、B に VRChat か Unity の回転コンストレイント（ソースは A）があると、B <- A も書き出す。`VRMC_node_constraint` は循環を禁じているので、このファイルは仕様に反する。同じ書き出しの中で変換した 2 つ以上のコンストレイントどうしでも同じことが起きる。

2 つ目は、VRChat のコンストレイント（`VRC.SDK3.Dynamics.Constraint.Components.VRC*Constraint`）が実際の書き出しでは 1 つも変換されていなかったこと。書き出し用のコピーを作る `InstantiateOwnedCopy` は、`ExecuteInEditMode` が付いた MonoBehaviour のうち `IEditorOnly` でも書き出しに使うもの（`PreservesExportBehaviour`）でもないものを消す。VRChat のコンストレイントの基底クラスはこれに当たるので、変換の前にコピーから消えていた。`ConstraintExportTests` の VRChat のテストはコピーを経ずに `ConstraintExport.Convert` を直接呼んでいたため、気づかなかった。

## 決定

- 変換で VRM のコンストレイントを足す前に、作者が付けた VRM のコンストレイントと、同じ書き出しで先に変換したものを合わせた「対象 -> ソース」の辺をたどる。ソースからたどって対象に戻るなら足さず、今の姿勢のまま書き出して、書き出し結果の一覧に理由を出す。どちらを残すかは、作者が付けたものを優先し、そのあとはこれまでと同じ並び順（`GetComponentsInChildren` の順）で先に来たものを残す。
- 理由の文は既存のどれにも当てはまらないので、1 つ足した。
  - 日本語: 「ソースのコンストレイントをたどるとこのオブジェクトに戻り、VRMはこの循環を書けないため、今の姿勢のまま書き出しました」
  - 英語・韓国語・中国語（簡体・繁体）の訳も足した。隣の「同じオブジェクトに別のコンストレイントが既にあるため、今の姿勢のまま書き出しました」と同じく、何が起きたかと理由を書く形にそろえた。
- `PreservesExportBehaviour` が、型名が `VRC.SDK3.Dynamics.Constraint.Components.` で始まるコンポーネントも残すようにした。VRChat のコンストレイントがコピーの上で動いても、元のアバターがシーンで既に取っている姿勢と同じ姿勢にコピーを動かすだけなので、残して害はない。PhysBone も同じ理由で残している。Unity のコンストレイント（`RotationConstraint` など）は MonoBehaviour ではないので、もともと消されておらず、この変更の影響も受けない。

## 入れなかったもの

- 循環のうちどの辺を外すかを、重さや動きの大きさから選ぶこと。どれを外しても今の姿勢のまま書き出す点は同じで、選び方を増やすほど結果が読みにくくなるため、先に来たものを残す決め方にした。
- 表情ボードのプレビューなど、`InstantiateOwnedCopy` を使うほかの画面で VRChat のコンストレイントだけを止めること。書き出しと同じく元の姿勢をなぞるだけなので、分けていない。

## 確認したこと

- 修正の前に、次の 3 件のテストが落ちることを確かめた。
  - `AConstraintThatClosesACycleWithAnAuthoredVrmConstraintKeepsThePose`: 作者が付けた A <- B があるとき、B <- A を書き出していた。
  - `ConstraintsThatSourceEachOtherConvertOnlyOneSide`: A <- B・B <- C・C <- A の 3 つを全部書き出していた。
  - `AFullExportWritesAVrchatRotationConstraint`: 実際の書き出し（`UniVrmOneClickExporter.Export`）で、`VRCRotationConstraint` の対象のノードに `VRMC_node_constraint` が無かった。
- 修正後、`ConstraintExportTests`・`ConstraintSourceFingerprintTests`・`NeutralFallbackExportTests`・`NeutralShapeSamplerTests`・`ExportFallbackConfirmationTests`・`NonLilToonExportTests` の 248 件がすべて通過した。
- `ExperimentalExpressionCaptureTests` ほかの 171 件のうち 167 件が通過。落ちた 4 件は別パッケージの版に依存するもので、`PreservesExportBehaviour` の変更を戻しても同じように落ちる。
