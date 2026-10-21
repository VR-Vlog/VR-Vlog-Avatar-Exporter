# 表情メニューが一覧より少ないときの判断を関数に分けてテストする

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`

## 背景

#274（`2026-10-21-board-other-faces-record-fallback.md`）で、表情ボードの通常の書き出しで表情メニューの表情が **その他の表情** より少ないとき、一覧を記録して書き出し直すようにした。次の 2 つの経路はテストが無かった。

- 書き出し直しで例外が出たとき、記録を戻して通常の書き出しをもう一度行い、その結果を保存する経路
- 回復の途中で一覧が変わったとき、件数と記録の条件をその時点のボードで決め直す経路と、ボードを閉じたあとに「記録できませんでした」と件数だけを出す経路

どちらも保存の処理（`Save`）のラムダの中に書かれていて、実際に例外を起こしたり回復の途中でボードを変えたりしないと通らない。

## 決定

判断の部分を `ExperimentalExpressionCaptureWindow.ResolveMenuShort` に分けた。ボードの作り直し・一覧の記録・通常の書き出しのやり直しは引数の関数で受け取り、保存するバイト列・判断に使った件数・結果の一覧に出す文（記録した／例外の文／記録しなかった理由）を返す。警告への追加と `ExportFallbackReport` への記録は、これまでどおり `Save` が行う。

`Save` の動きは変えていない。`recordedList` と `listedFaces` は、これまでと同じ時点（記録の前・ボードを作り直した直後）に `Save` 側の関数の中で書き換える。例外で途中から抜けたときにも、次の回復の試みが同じ値を見るようにするためである。

追加したテストは VRChat SDK もアバターも使わない。

- `MenuShortSaveExportsTheMenuAgainWhenRecordingThrows`: 記録で例外が出ると通常の書き出しを 1 回やり直してそのバイト列を返し、例外の文と「記録できませんでした」を出す。`OperationCanceledException` はそのまま外へ出し、やり直さない。
- `MenuShortSaveDecidesAgainFromTheBoardAtThatTime`: 作り直したボードが記録できないときは条件の文をその時点の件数で出し、記録できるときはその件数で記録する。ボードを閉じたあとは何も記録せず、最初の件数で「記録できませんでした」を出す。

## 確認したこと

- `ExperimentalExpressionCaptureTests`・`ExpressionBoardTests` を Unity の EditMode テスト（Unity 2022.3.22f1）で流した。168 件中 164 件成功・4 件失敗で、追加した 2 件と #274 の `OrdinaryRouteRecordsTheListWhenTheMenuGivesFewerFaces` は成功した。失敗した 4 件は `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` の APL の組み合わせで、APL が入っていない環境が原因であり develop でも同じく失敗する。
- 分ける前は、記録した文の警告への追加と `ExportFallbackReport` への記録も `try` の中にあった。今はその 2 つが `try` の外にある。どちらも例外を出す処理ではないので、結果は変わらない。
