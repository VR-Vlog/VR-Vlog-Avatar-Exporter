# Lock がオフのコンストレイントとオフのコンストレイントを書き出し結果の一覧に出す

- 日付: 2026-10-22
- 対象: `Editor/ConstraintExport.cs`・`Editor/Locales/*.json`・`Tests/Editor/ConstraintExportTests.cs`・`CHANGELOG.md`

## 背景

コンストレイントの変換（[2026-10-19-vrm-node-constraint.md](2026-10-19-vrm-node-constraint.md)）は、コンポーネントが無効・IsActive がオフ・**Lock** がオフのコンストレイントを、何も表示せずに飛ばしていた。書き出し結果の一覧にも、「コンストレイント変換: 元N件…」の行にも出ない。

実例では、IsActive がオン・**Lock** がオフの `VRCRotationConstraint` が 3 個あるアバターで、`VRMC_node_constraint` は 0 個、警告の行も出なかった。作者からは、VRChat では付けたコンストレイントがなぜ VRM に入らなかったのか分からない。

## 決定

- **Lock** がオフでも、オフでも、VRM のコンストレイントには変換しない。代わりに省略（Omitted）として、対象の名前つきで書き出し結果の一覧に出し、件数に数える。件数に入るので「コンストレイント変換」の行も出る。
- 内部の判定（`Plan`）は、これまでの `Active` 1 つをやめ、`On`（コンポーネントが有効・IsActive がオン・オブジェクトが表示）と `Locked`（**Lock**）に分けた。Unity のコンストレイントは `constraintActive` と `locked`、VRChat のコンストレイントは `IsActive` と `Locked` を読む。
- 理由の文を 2 つ足した。どちらも隣の理由と同じく、何が起きたかと理由を書く形にそろえた。英語・韓国語・中国語（簡体・繁体）の訳も足した。
  - **Lock** がオフ: 「Lockがオフのため、VRChatでも対象を動かしません。今の姿勢のまま書き出しました」
  - オフ: 「オフになっているため、今の姿勢のまま書き出しました。アニメーションでオンにしてもVRMでは動きません」
- オフで **Lock** もオフのものは、オフの理由を出す。オンにしない限りどちらも動かないので、先に分かる方を出す。
- 重みが 0、ソースがない、対象がないものはこれまでどおり何も出さない。動かす元がないため。

## 変換しない理由

VRChat の Constraints のドキュメントでは、**Lock** がオフのあいだ、コンストレイントは対象を動かさず、ソースとの位置関係から At Rest と Offset を測り直すだけ、とされている。Unity のコンストレイントの **Lock** も同じ働きをする。VRM の拘束に変換すると、VRChat では動かなかった対象がアプリでは動くようになり、挙動が変わる。今の姿勢のまま書き出すのが VRChat の見た目と同じになる。

オフのものは、アニメーションでオンにする作りがありうる。VRM にはコンストレイントのオンとオフを切り替える仕組みがないので、変換すると常に動くことになり、これも挙動が変わる。今の姿勢のまま書き出し、一覧の理由で「VRMでは動きません」と伝える。

## 入れなかったもの

- **Lock** がオフのコンストレイントを、ロックしたものとして変換する選択肢。VRChat で動かないものを VRM でだけ動かすことになるため。
- 書き出し元のアバターの比較（`ExportSourceFingerprint`）の変更。一覧に出すかどうかだけの変更で、書き出す中身は変わらないため触っていない。
- `result.Kept + result.Approximated` が 0 のときに `Vrm10Instance` を足さない決まりは変えていない。

## 確認したこと

- `ConstraintExportTests` に、Unity の `RotationConstraint` で **Lock** がオフのもの（`AnUnlockedConstraintKeepsThePoseAndIsListed`）と、コンポーネントが無効・`constraintActive` がオフ・オブジェクトが非表示・`constraintActive` と **Lock** の両方がオフのもの（`AnOffConstraintKeepsThePoseAndIsListed`）のテストを足した。どれも VRM のコンストレイントが付かないこと、省略 1 件として理由・名前・パスつきで一覧に出ること、「コンストレイント変換: 元1件 / 変換0件（うち近似0件） / 省略1件。」の行が出ることを確かめる。VRChat SDK があるときは `VRCRotationConstraint` でも同じことを確かめる（`AnUnlockedOrOffVrchatConstraintIsListed`）。
- これまで **Lock** がオフ・オフのものも「何も出ない」としていた `ConstraintsThatNeverMoveAnythingAreNotListed` は、重みが 0 とソースがないものだけにした。
- この変更では Unity でテストを回していない。
