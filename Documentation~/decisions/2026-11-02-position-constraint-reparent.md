# Position Constraintで追従する部品もソースの子に移して書き出す

- 日付: 2026-11-02
- 対象: `Editor/ConstraintExport.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ConstraintExportTests.cs`
- Issue: #597

## 背景

腕の装甲などを、胴の下に置いたまま Position Constraint（Unity と VRChat の両方）で手の位置へ追従させる作りがある。同じオブジェクトに Aim Constraint を付けて、肘などの方へ向ける作りも多い。VRM の `VRMC_node_constraint` には位置のコンストレイントが無く、これまでは「VRMに同じ働きのコンストレイントがない」として今の姿勢のまま書き出していた。アプリでは腕を動かしても部品が胴の横に残る。

[2026-10-23 の決定](2026-10-23-parent-constraint-reparent.md)で、Parent Constraint は対象をソースの子に移して書き出すようにした。Position Constraint も同じやり方で追従させられる。

## 決定

- Position Constraint も、Parent Constraint と同じ条件（ソースが1つ・全軸・ローカル空間でない・強さが1・人型のボーンを含まない・ほかのコンストレイントから参照されない・表情や一人称の設定が中を使わない）を満たせば、対象をソースの子に移す。軸は位置の軸（Unity の `translationAxis`、VRChat の `AffectsPositionX/Y/Z`）を見る。
- 子に移すと、位置だけでなく向きもソースと一緒に回る。元は位置だけが追従していたので、結果の一覧では「近似」とし、理由に「ソースが回ると一緒に回るため、向きと位置が元と少し異なる場合があります」と出す。
- 同じオブジェクトの Aim Constraint（と LookAt Constraint）を VRM のエイムコンストレイントに変換できたときも、対象は子に移す。VRM のエイムはソースへのワールドの方向で向きを決めるので、親が変わっても向ける軸は保たれる。回転のコンストレイントを変換できたときは、親が変わると基準の向きが変わるので、これまでどおり移さない。
- Scale Constraint はこれまでどおり今の姿勢のまま書き出す。

## 確認

- `ConstraintExportTests` に、移す・Aim と一緒に移す・移さない各条件（位置の軸・強さ・ソースの強さ・回転のコンストレイントが同じオブジェクトにある・回転のコンストレイントから参照される）のテストを足し、Position Constraint を「同じ働きがない」として扱っていたテストを Scale Constraint だけに直した。

## 欠点

- 元の Position Constraint は向きを変えないので、ソースが大きく回ると、子にした部品はソースの周りを回り、元の位置からずれる。ソースと部品が離れているほどずれは大きい。それでも、胴の横に残るよりは元に近い。
- 強さが1未満の Position Constraint（2つのボーンの中間に置く作りなど）は、これまでどおり今の姿勢のまま書き出す。
