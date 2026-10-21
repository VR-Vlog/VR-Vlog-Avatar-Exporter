# 表情ボードで記録する一覧の同じ名前に番号を付けて全部記録する

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ExpressionBoard.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExpressionBoardTests.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Documentation~/ExperimentalExpressionCapture.md`・`Documentation~/decisions/2026-10-18-expression-board-ui.md`・`Documentation~/decisions/2026-10-21-board-other-faces-record-fallback.md`・`CHANGELOG.md`

## 背景

`2026-10-21-board-other-faces-record-fallback.md` で、通常の書き出しで表情メニューから入った表情が **その他の表情** より少ないとき、同じ保存を記録する経路でやり直すようにした。ただし、やり直すのは一覧が記録の条件（64件以下・同じ名前なし。`ExpressionBoard.CanRecordList`）を満たすときだけだった。

20体のアバターで確かめたところ、一覧に別々のクリップで同じ名前のもの（左手と右手のジェスチャーの表情がどちらも `Smile` など）があるアバターは、やり直しに入れなかった。そのうち1体は、一覧に29件が並んでいるのにVRMのカスタム表情が0件だった。記録する経路でも、同じ名前があると保存を止めていた（`HasDuplicateNames`）。

同じ名前で止めていた理由は、セッションの `ReplaceClipRecords` が名前の重複で例外を出すことだけだった。最後にVRMの表情名を決めるところでは、すでに `VrmMenuExpressions.UniqueName` で重なった名前に ` (2)` を付けていて、表情メニューの経路（`Happy (2)` など）と同じ付け方になっている。

## 決定

- `ReplaceClipRecords` は、名前が残す記録か先に並ぶ記録と重なるとき、例外を出さずに `VrmMenuExpressions.UniqueName` で ` (2)`・` (3)`… を付けて記録する。残す記録（手で記録した表情・読み込んだ表情）の名前も重なりの判定に入れるので、手で記録した表情と重なった場合も同じ付け方になる。残す記録の名前は変えない。
- `ExpressionBoard.DuplicateNames`・`HasDuplicateNames` をなくし、`CanRecordList` は64件以下だけを見る。記録する経路の保存も、同じ名前では止めない。下のバーと保存の前の検査から同じ名前の理由を外した。
- 記録できないときの通知は「表情メニューから書き出せた表情は{0}件でした・その他の表情{1}件のとおりに書き出すには64件以下にする」にした。訳は4言語とも、使わなくなった「同じ名前の表情が{0}組あります・…」「表情名が重複しています。…」の2件を外し、通知の文を差し替えた。

## 検討した代替案と不採用理由

- やり直しのときだけ番号を付け、記録する経路では同じ名前で保存を止めたままにする: 同じ一覧が、**書き出さない表情** に何も入れていなければ保存でき、1件入れると保存できなくなる。経路によって保存できるかが変わる理由が利用者には分からない。
- ボードで名前を変えられるようにする: カードは1枚の表情を名前だけで表していて、名前を変える欄を置く場所が無い（`2026-10-18-expression-board-ui.md` で表情名の変更をなくした理由と同じ）。

## 影響

- 同じ名前のクリップを両方とも書き出せる。ファイル名を変えなくてよい。
- どのカードに ` (2)` が付くかは一覧の並び順（追加したファイルが先、次に見つかった表情）で決まり、ボードには表示しない。左右のどちらに番号が付いたかは、アプリで表情を選んで確かめるしかない。

## 出典

- `2026-10-21-board-other-faces-record-fallback.md`（通常の書き出しから記録する経路へのやり直し）
- `2026-10-18-expression-board-ui.md`（記録する経路の同じ名前の止め方と、表情名の変更をなくした理由）
- `2026-10-19-expression-plain-names.md`（`UniqueName` の番号の付け方）
- `Tests/Editor/ExperimentalExpressionCaptureTests.cs` の `ReplaceClipRecordsNumbersRepeatedNames`・`RecordRouteSavesRepeatedNamesNumbered`・`OrdinaryRouteRecordsTheListWhenTheMenuGivesFewerFaces`、`Tests/Editor/ExpressionBoardTests.cs` の `ListCanBeRecordedOnlyWithinTheRecordRules`
