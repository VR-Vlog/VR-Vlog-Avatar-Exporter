# Unity と VRChat のコンストレイントを VRM のコンストレイントへ変換する

- 日付: 2026-10-19
- 対象: `Editor/ConstraintExport.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExportStageGuard.cs`・`Editor/ExportRecovery.cs`・`Editor/Locales/*.json`・`Tests/Editor/ConstraintExportTests.cs`

## 背景

これまでの書き出しは、Unity のコンストレイント（`RotationConstraint` など）と VRChat のコンストレイント（`VRCRotationConstraint` など）を VRM に入れていなかった。書き出したときの姿勢のまま固定されるので、袖・スカート・ねじれ補助のボーンのように、ほかのボーンの回転に合わせて動くはずの部分がアプリでは動かなかった。手元の検証では、複数のアバターでこの欠けを確認した。

VRM 1.0 には `VRMC_node_constraint` があり、UniVRM 0.131 では `Vrm10RotationConstraint`・`Vrm10RollConstraint`・`Vrm10AimConstraint` の 3 種類を書き出せる。Unity や VRChat のコンストレイントを VRM のものへ変換する処理は、UniVRM にも NDMF にも Modular Avatar にもない（Modular Avatar の `Convert Constraints` は Unity から VRChat への変換）。そのため、変換は最小限の範囲でこのパッケージに置く。

## 決定

- 揺れ物の変換の直後に新しい段階 `ExportStage.Constraints`（「コンストレイントの変換」）を置き、`guard.Step` で囲む。対象は NDMF の処理を通したあとの書き出し用コピーだけで、元のアバターには書き込まない。失敗したらこの段階を省き、従来どおり今の姿勢のまま書き出す。
- VRChat SDK への参照は持たないので、VRChat のコンストレイントは型の名前で見分け、`SerializedObject` でフィールドを読む（`IsActive`・`Locked`・`GlobalWeight`・`SolveInLocalSpace`・`FreezeToWorld`・`TargetTransform`・`AffectsRotationX/Y/Z`・`AimAxis`・`Sources`）。
- 無効・ロックなし・強さ 0・ソースなしのコンストレイントは何も動かさないので、変換も記録もしない。
- 変換の規則は次のとおり。VRM で 1 つのノードに置けるコンストレイントは 1 つだけなので、既に VRM のコンストレイントがあるノードと、先に変換したノードには追加しない。
  - 回転（3 軸）: ソースが 1 つで、ソースと対象の親が同じ（VRChat のローカル空間なら親は問わない）なら `Vrm10RotationConstraint` にする。強さはコンストレイントの強さ × ソースの強さ。
    - VRM の回転コンストレイントは、ソースのローカルの回転の変化を対象自身の向きで足す。元の Unity の計算と一致するのは、両者の基準のローカルの向きが同じときだけ。基準の向きの差が 1° 以下で強さが 1 なら「保持」、60° 未満なら「近似」として変換する。60° 以上では、軸がずれて固定したままより大きくずれるので変換しない。
    - ソースと対象の親が違うワールド空間の回転は、VRM では親の動きを二重に受けるので変換しない。
  - 回転（1 軸）: `Vrm10RollConstraint` にして「近似」とする。Unity は Euler の 1 成分、VRM はその軸まわりのねじれを取り出すので、結果が少し違う。2 軸だけの回転は変換しない。
  - エイム・ルックアト: 向ける軸が ±X・±Y・±Z のどれかで、今の姿勢でその軸がソースを向いている（2° 以内）なら `Vrm10AimConstraint` にして「近似」とする。上方向の扱いが違い、ねじれが元と変わることがある。今の姿勢でソースを向いていないものは、VRM が書き出した直後からソースへ向けて回してしまうので変換しない。
  - 親子・位置・スケールのコンストレイント、ソースが 2 つ以上、ワールドに固定する設定、アバターの外か非表示のオブジェクトを参照するものは、VRM で表せないので変換しない。
- 変換しなかったものは、これまでと同じく今の姿勢のまま書き出す。止めずに続け、結果の一覧に「コンストレイント」の項目として 1 件ずつ、保持・近似・省略と理由を残す。この記録は一覧に並べるだけで、保存前の確認は求めない。
- 元の Unity・VRChat のコンストレイントはコピーに残す（UniVRM は書き出さない）。

## 確認したこと

- `Tests/Editor/ConstraintExportTests.cs` で次を確かめた。
  - 基準の向きが同じ兄弟の回転は `Vrm10RotationConstraint` になり「保持」となる。親を回してソースを曲げても、VRM の式の結果が元の計算と 0.01° 以内で一致する。
  - 基準の向きが違うか強さが 1 未満なら「近似」、差が 60° 以上・親が違う・2 軸・ソースが 2 つ・親子／位置／スケール・外部参照・既存の VRM コンストレイントは「省略」で、VRM のコンストレイントを足さない。
  - 1 軸の回転は対応する軸の `Vrm10RollConstraint` に、ソースを向いたエイムは対応する軸の `Vrm10AimConstraint` になる。
  - 何も動かさないコンストレイントは記録に出ない。元のアバターは変わらない。
  - VRChat SDK がある環境では、`VRCRotationConstraint` を SDK への参照なしで読み取れる。
  - アバター全体の書き出しで、対象のノードに `VRMC_node_constraint` の回転が入り、ソースのノードを指す。
  - 追加した文言が 4 言語の翻訳にそろっている。
