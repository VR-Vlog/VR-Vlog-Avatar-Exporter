# ConstraintExportが今の姿勢のまま残すConstraintを、設定値のまま新しい拡張に書く

- 日付: 2026-11-15
- 対象: `Editor/ConstraintExport.cs`、`Editor/AvatarConstraintsExport.cs`（新規）、`Formats/Runtime/AvatarConstraintsData.cs`（新規）、`Schema/VRVLOG_avatar_constraints.1.schema.json`（新規）、`Editor/AvatarAnimatorCapture.cs`、`Editor/UniVrmOneClickExporter.cs`、`Formats/Runtime/AvatarFormatVersions.cs`、`Formats/README.md`

## 背景

`ConstraintExport`は、UnityのConstraintとVRC Constraintのうち`VRMC_node_constraint`で表せるもの（Rotation・Aim・LookAtの単一ソース）をVRMのConstraintに、ParentとPositionをソースの下への付け替えに変える。それ以外（複数ソース、重みが1でないもの、Scale、Parentのオフセット、Solve In Local Spaceなど）は今の姿勢のまま書き出していた。アプリで同じ動きを出すには、そのConstraintの設定値がファイルに要る。FXでConstraintの有効無効を切るアニメーション（`m_Enabled`の曲線）も、行き先が無いので数えて捨てていた。

## 決めたこと

`ConstraintExport`が今の姿勢のまま残すと決めたConstraintを、新しい拡張`VRVLOG_avatar_constraints`に、エディターで見える設定値のまま書く。

- 1件が1つのコンポーネント。動かすノード、`type`（`position`・`rotation`・`scale`・`parent`・`aim`・`lookAt`）、`origin`（`unity`か`vrchat`）、コンポーネントの`enabled`、Is Active（`active`）、`locked`、`weight`、VRChatのSolve In Local Space（`localSpace`）とFreeze To World（`freeze`）、ソースのノードと重みを持つ
- 種類ごとに、その種類が持つ軸・Rest・Offset・Aimの設定だけを持ち、ほかのキーは持たない（Parentのオフセットはソースごと）。読み手（`AvatarConstraintsData.Read`）がこれを強制し、`Write`も同じ確認を通す。`origin`が`unity`のとき`localSpace`と`freeze`は偽
- ベクトルはUnityの座標のまま`[x, y, z]`、角度は度。アプリはUniVRMで同じ座標に読み戻す
- FXの`m_Enabled`の曲線は、`VRVLOG_avatar_animator`の`enabled`の対象（`component`が`constraint`、`node`がConstraintの動かすノード、`index`がこの拡張の表の番号）にする。今の読み手は知らない`component`として受け（`Target.Known`が偽）、書かない
- 表にしないもの（VRMのConstraintに変えたもの、ターゲット・ソース・Up Objectのどれかにノードが無いもの、自動除外で消えたもの）に向いた`m_Enabled`の曲線は、今までどおり`enabled`として数える
- `ConstraintExport`がソースの下に付け替えたターゲットのConstraintは書かない。設定値が付け替え前の座標のものだから
- 拡張を読む最初のアプリの版は仮に1.2.8とする（`AvatarFormatVersions`）。`VRVLOG_avatar_animator`と同じ扱い

設計書の段6は、この拡張のキーを`node`・`type`・`sources`・`weight`・`locked`・`offsets`・`freeze`と略して書いている。「UnityのConstraintとVRC Constraintの設定値のまま書く」を満たすため、`offsets`を種類ごとのRest・Offsetとソースごとのオフセットに分け、`origin`・`enabled`・`active`・`localSpace`・軸・Aimの設定を足した。

## 古い出力との関係

- VRMのConstraintに変えたものと付け替えは変えない。拡張に書いたConstraintも、VRMの中では今までどおり今の姿勢のまま残る。古いアプリは知らない拡張を読まないので、今と同じに出る
- FXの曲線は、クリップの容量（キー16384・トラック4096）を今ある種類のあとに使う。読むときは曲線をマテリアルの値の曲線より後に回し、トラックもマテリアルの対象より後に置く。今ある種類が押し出されることはない
- 拡張の組み立てや書き込みで例外が出たときは、ログに出して拡張を書かない。そのときのファイルは今までと同じになるので、結果の一覧には何も足さない

## 採らなかった案

- `VRVLOG_avatar_animator`の中に設定値を入れる: 動きの曲線ではなく部品の設定なので、設計書のとおり別の拡張にした
- VRMのConstraintに変えたものも表に入れる: アプリでVRMのConstraintと二重に解かれる。FXの曲線で切るなら、VRMのConstraintを書かずにこの表へ移す必要があり、古いアプリでの見え方が変わる
- 書き出しの段（`ExportStage`）を増やす: 段はすべてのテストのアバターで一度は動くことを`ExportStageGuardTests`が求める。拡張は無くても今と同じファイルになるので、try/catchで包むだけにした
- `AvatarAnimatorData.Components`に`constraint`を足す: 足すと今の読み手が`node`などのキーを確かめ、書き込み先を探す。アプリの単位6-4がソルバーと一緒に足す

## 確かめたこと

（テストの結果を書く）

## 残したこと

- アプリのソルバー（単位6-4・6-7）。Unityの各Constraintと同じ式で解き、FXの`enabled`で切る
- `AvatarAnimatorData.Components`への`constraint`の追加（単位6-4）
- 自動除外（`ExportEmptyNodes`）はConstraintのソースだけの空のオブジェクトを消すことがあり、そのConstraintは拡張に書かれない。消さないようにするとノードが増えて今の出力が変わるので、今回は変えていない
