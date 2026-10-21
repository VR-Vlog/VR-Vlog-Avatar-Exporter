# 通常の書き出しで表情メニューの表情が一覧より多いときは件数だけを表示する

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Documentation~/ExperimentalExpressionCapture.md`・`CHANGELOG.md`

## 背景

`2026-10-21-board-other-faces-record-fallback.md` で、通常の書き出しで表情メニューから入った表情（`VrmMenuExpressions.CountRegistered`）が **その他の表情** の件数より少ないとき、同じ保存を記録する経路でやり直すようにした。逆に表情メニューの方が多いときは何も表示していなかった。同じ決定の記録にあるとおり、2つの数は数えている範囲が同じではない。**その他の表情** はControllerにあるクリップを数え、`CountRegistered` は表情メニューの項目から登録した表情を数える。このため、VRMには一覧より多い表情が入ることがある。利用者からは、一覧の件数とアプリで選べる表情の数が合わない理由が分からない。

保存した後の **VRMを保存しました** の欄の「表情 ○件」（`SavedExpressionVrm.Expressions`）も、通常の書き出しでは実際の表情の数を表していなかった。この値は保存の時点で記録している表情の数（`expressionCount`）で、記録する経路では記録した表情の数になる。通常の書き出しは何も記録しないので、VRMに表情メニューの表情が入っていても0件と表示していた。

## 決定

- 書き出す内容は変えない。表情メニューの方が多いときも、表情メニューの結果のまま保存する。
- 通常の書き出し（最後の `replaceCustomFaces` が false）では、一覧の件数にかかわらず出力の `CountRegistered` を数える。これまでは一覧が1件以上のときだけ数えていた。少ないときのやり直し（`menuFaces < listedFaces` の分岐）はそのまま残す。
- 表情メニューの方が多いときは、書き出し結果の一覧に「表情メニューから{0}件を書き出しました・その他の表情の一覧は{1}件です」を出す。表情を省いたわけではないので `report.Fallbacks`（省いた項目）には入れない。同じ件数なら何も出さない。訳は4言語に足した。
- 「表情 ○件」は、保存したバイト列に入ったカスタム表情の数にする。数え方は経路で分ける。
  - 記録する経路（`replaceCustomFaces` が true か、手で記録した表情・読み込んだ表情が残っている）: 記録した表情の数（`expressionCount`）。これまでと同じ。セッションはこのとき表情メニューを書き出さず、記録した表情を1つずつカスタム表情にする。
  - 通常の書き出し（どちらでもない）: 出力の `CountRegistered`。表情メニューの項目から登録した表情の数で、書き出し結果の「VRChat表情 ○件」と同じ数え方である。
  - どちらも、VRMの表情5枠などの標準表情の枠に入れた表情は数えない。値は書き出しが終わるたびに決め直すので、回復の書き出しで保存したときもその書き出しの数になる。

## 検討した代替案と不採用理由

- B: 一覧のとおりに書き出す切り替えを置き、オンのときは表情メニューの方が多くても一覧の表情だけを書き出す: 通常の書き出しが表情メニューから入れている表情のうち、ボードが一覧に取り出せないものが書き出されなくなる。切り替えの意味を利用者に説明する文言も要る。今は件数を表示するだけにする。
- C: 表情メニューの方が多いときも常に一覧を記録して書き出す: Bと同じく、ボードが取り出せない表情メニューの項目がVRMから消える。何も変えていない利用者の出力が変わる点は、`2026-10-18-expression-board-ui.md` で常に記録する経路にするのを不採用にした理由と同じである。

## 影響

- 表情メニューの方が多いアバターでは、保存のたびに結果の一覧に件数の行が1行増える。出力は変わらない。
- 「表情 ○件」が通常の書き出しで0件と出なくなる。記録する経路の値は変わらない。

## 出典

- `2026-10-21-board-other-faces-record-fallback.md`（表情メニューの方が少ないときのやり直しと、比べる2つの数の範囲の違い）
- `2026-10-18-expression-board-ui.md`（通常の書き出しと記録する経路の分け方）
- `Tests/Editor/ExperimentalExpressionCaptureTests.cs` の `OrdinaryRouteSaysWhenTheMenuGivesMoreFacesThanTheList`（表情メニューを作るのでVRChat SDKが要る）・`OrdinaryRouteRecordsTheListWhenTheMenuGivesFewerFaces`・`RecordRouteSavesRepeatedNamesNumbered`
