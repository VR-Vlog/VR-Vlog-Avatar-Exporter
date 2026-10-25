# Lock がオフの VRChat のコンストレイントも VRM のコンストレイントに変換する

- 日付: 2026-10-24
- 対象: `Editor/ConstraintExport.cs`・`Editor/Locales/*.json`・`Tests/Editor/ConstraintExportTests.cs`・`CHANGELOG.md`
- 改める記録: `2026-10-22-unlocked-constraint-listed.md` の「変換しない理由」と、Lock がオフの VRChat のコンストレイントを省略として一覧に出す決定

## 背景

`2026-10-22-unlocked-constraint-listed.md` では、VRChat のコンストレイントで **Lock** がオフのものは VRChat でも対象を動かさないとして、VRM のコンストレイントに変換せず、「Lockがオフのため、VRChatでも対象を動かしません」の理由で一覧に出していた。

配布されている衣装を書き出して調べると、変換されずに今の姿勢のまま残ったコンストレイントのうち、この理由のものが最も多かった。衣装 37 件で `VRCRotationConstraint` が 904 件あり、腕のねじれを補うボーンや、衣装を体に沿わせるボーンが多い。どれも VRChat で動く前提で付けられている。

そこで VRChat SDK を入れた Unity の再生中に確かめた。

- **Lock** がオフ・**IsActive** がオンの `VRCRotationConstraint` を作り、ソースを 60 度回すと、対象も 60 度回った。**Lock** がオンのものと同じ。
- 素体のアバターに付いている **Lock** がオフのコンストレイント（腕のねじれを補うボーン、重み 0.5 や 0.9）も、ソースを回すと重みどおりに回った。
- **IsActive** がオフのものは動かなかった。

VRChat SDK のコンストレイントは、**Lock** を「編集中に対象を動かしたとき At Rest と Offset を測り直すか」にだけ使う。再生中はロックしたものとして解く。VRChat のドキュメントにある「**Lock** がオフのあいだは対象を動かさない」は、編集中の動きの説明だった。Unity のコンストレイントで **Lock** が再生中に効かないのと同じ扱いである。

## 決定

- VRChat のコンストレイントも **Lock** を見ずに変換する。Unity のコンストレイントはすでにそうしているので、両方そろう。
- 内部の判定（`Plan`）から `Locked` を外し、理由の「Lockがオフのため、VRChatでも対象を動かしません。今の姿勢のまま書き出しました」と、その英語・韓国語・中国語（簡体・繁体）の訳を消した。
- **Lock** がオフだったものは、ほかのコンストレイントと同じ判定（種類・設定・基準の向き・循環など）を通る。VRM で表せないものは、これまでどおりその理由で今の姿勢のまま書き出し、一覧に出す。
- オフ（コンポーネントが無効・**IsActive** がオフ・対象が非表示）のものは、これまでどおり変換せず一覧に出す。

## 書き出す VRM の変わり方

**Lock** がオフの VRChat のコンストレイントがあるアバターでは、書き出す VRM が変わる。これまで今の姿勢のまま止まっていた対象のうち、VRM で表せるものに `VRMC_node_constraint` が付き、アプリで体の動きについてくるようになる。**Lock** がオフのコンストレイントが無いアバターの書き出す内容は変わらない。

## 変えなかったもの

- 書き出し元のアバターの比較（`ExportSourceFingerprint`）は、**Lock** がオンのコンストレイントが動かす回転だけを比較から外している。編集中は **Lock** がオフのコンストレイントは対象を動かさないので、ここは今のままで正しい。
- 重みが 0、ソースがない、対象がないものは、これまでどおり何も出さない。

## 検討した代替案と不採用理由

- **Lock** がオフのものを省略のまま、理由の文だけ直す: VRChat では動く対象が、アプリでは止まったままになる。直す必要があるのは文ではなく判定である。
- **Lock** がオフのものだけ別の理由で一覧に出す: 変換できたものは、ロックしたものと同じく変換済みとして数えるほうが、作者にとって VRChat との違いが分かりやすい。

## 確認したこと

- `ConstraintExportTests` の `AnUnlockedOrOffVrchatConstraintIsListed` を `AnUnlockedVrchatConstraintIsConvertedUnlessOff` に変え、**Lock** がオフ・**IsActive** がオンの `VRCRotationConstraint` は VRM の回転コンストレイントになり、変換 1 件・省略 0 件と数えること、**IsActive** がオフのものはこれまでどおり省略 1 件として一覧に出ることを確かめる。VRChat SDK が無いプロジェクトでは、これまでどおりこのテストは飛ばす。
- Unity 2022.3.22f1 で EditMode テストを回した。VRChat SDK なしのプロジェクトで `ConstraintExportTests` と `ExportFallbackConfirmationTests` が 58 件中 54 件成功・失敗 0・SDK が要る 4 件は飛ばした。VRChat SDK（Avatars）を足したプロジェクトで `ConstraintExportTests` が 42 件すべて成功し、飛ばしたものは無い。
