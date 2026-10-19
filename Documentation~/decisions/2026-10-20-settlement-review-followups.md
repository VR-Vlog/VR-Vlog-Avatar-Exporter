# 時間で決めるメニューの表情の、レビューで見つかった抜けを埋める

- 日付: 2026-10-20
- 対象: `Editor/ExpressionSettlement.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/ExperimentalInstalledExpressionPlayer.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExpressionSettlementTests.cs`・`Tests/Editor/NeutralLayerControlTests.cs`・`Tools/run-unity-compatibility.py`・`CHANGELOG.md`

## 背景

2026-10-19 の決定（`2026-10-19-expression-settlement.md`）で、2 秒で止まらないメニューの表情を Animator の時間の流れから決めて書き出すようにした。その実装のレビューで、次の抜けが見つかった。

1. 最終状態に exit time の遷移が残っていて今の入力では起きない表情にも「時間遷移から確定しました」の警告が付き、そのうえ 3 回の検査点（その後も顔が変わらないかの確認）を飛ばしていた。遷移をたどっていないのに、たどったときと同じ扱いになっていた。
2. 速度が 0 や負のステートでも、動くクリップを「最後のキーで保存しました」として通していた。時計が進まない・逆に進むので、最後のキーには届かない。
3. 中断できる遷移（Interruption Source が None 以外）を、遷移の終わりまで 1 回で進めていた。実機では毎フレーム別の遷移に割り込まれうるので、割り込みを飛ばして別の顔になることがあった。
4. 同じステートへ戻る遷移（自分自身への exit time の遷移、Can Transition To Self の Any State の遷移）を見分けていなかった。ステートに入るたびに動く Parameter Driver が、待つたびにもう一度動く。
5. 長さの違うループが複数あるとき、短い方の 1 つだけを最初のフレームに合わせ、ほかのループは途中のフレームのまま「最初のフレームで保存しました」と出していた。
6. 遷移の回数・繰り返しの上限に当たったときや、遷移の時間が無限大・NaN のときに「遷移が循環するため」として保存していた。実際には循環していないので、理由が合っていない。
7. 1 回の進めごとに、ステートマシン全体を最初からたどってステートを探していた。
8. 1 分を超えて待たないと届かない顔でも、何も言わずに保存していた。

## 決定

- 入れたもの:
  - `Settle` が時間で決めたと返したときだけ 3 回の検査点を飛ばす。最終状態に残った exit time の遷移が今の入力で起きないことは、遷移をたどらずに分かることなので、検査点は従来どおり行い、警告も「最終状態に時間遷移が残っていますが、今の入力では起きないため現在の状態で保存しました」に分けた。
  - 「最後のキー」「最初のフレーム」で保存するのは、ステートの速度が 0 より大きいときだけにした。速度 0・負のステートで動くクリップは、#234 より前と同じく書き出しから外す。試験的な一括収録も同じ。
  - 中断できる遷移の途中は、1 フレームずつ進める。Unity からは今どの遷移を走っているかが分からないので、元のステート（と親の Any State）から行き先が同じ遷移のどれかに Interruption Source があれば中断できるものとみなす。ステートが特定できないときも中断できるものとして扱う。
  - 同じステートへ戻る遷移は、遷移中の行き先が今のステートと同じとき、または待っていた exit time のあとでステートの時計が巻き戻ったときに見分ける。見分けたら「同じ状態へ戻る遷移があるため、その状態で保存しました」を付け、そのレイヤーの exit time はそれ以上たどらない。Parameter Driver は 1 回戻った分だけ動き、実機で 1 回待ったときと同じ顔になる。
  - ループは 1 つの時計で進めるので、長さの違うループを全部最初のフレームにはそろえられない。1 つを合わせたあとで、ほかのループが最初のフレーム（前後 1 フレーム以内）にないときは「複数のループが重なるため、いずれかのループの最初のフレームで保存しました」を出し、「最初のフレームで保存しました」は出さない。
  - 遷移の回数（64 回）・繰り返しの回数・1 フレームずつ進めた回数の上限に当たったとき、遷移の時間が無限大・NaN のときは、保存せずに表情を書き出しから外す（`InvalidOperationException`。#234 より前と同じ扱い）。「遷移が循環するため」は、同じステートに本当に戻ったときだけ出す。
  - `Settle` の最初に、（レイヤー, ステートのハッシュ）から（ステート, 親のステートマシン）を引く表を 1 回だけ作る。Synced Layer は元のレイヤーのステートマシンで、レイヤー名とルートのステートマシン名の両方で引く。2 つのステートに当たるハッシュは表から外し、特定できないものとして扱う。
  - 進めた時間の合計が 60 秒を超えたら、保存したうえで「60秒を超える時間遷移の後の状態で保存しました」を付ける。
  - 新しい警告 4 つと、外すときの理由 2 つ（「時間遷移を最後までたどれないため、表情の最終状態を確定できません。」「遷移の時間が不正なため、表情の最終状態を確定できません。」）に、英語・韓国語・中国語（簡体・繁体）の訳を足した。警告 4 つは書き出し結果の一覧で「近似」の行になる。
- 60 秒にした理由: メニューの表情は手で選んで数秒〜数十秒見せるもので、1 分を超えて同じ項目を選び続けることは少ない。1 分を超えてから届く顔は、使う人が実際に見る顔と違うことが多いので、保存はしたうえで知らせる。上限ではないので、60 秒を超えても書き出しは止めない。1 フレームずつ進める回数の上限も同じ 60 秒分（3600 フレーム）にした。
- 入れなかったもの:
  - 中断できる遷移で、実際に走っている遷移だけを見る案: Unity の `AnimatorTransitionInfo` からは元の `AnimatorStateTransition` を引けないので、行き先が同じ遷移をまとめて見る。中断できないものを 1 フレームずつ進めても値は変わらない（遅くなるだけ）ので、安全側に倒した。

## これまで書き出せた表情の値と警告を変えないこと

変えたのは、#234 で新しく書き出せるようになった表情の扱いだけ。#234 より前から書き出せた表情は、`Settle` が 1 回も進めずに戻る経路を通る。`SettleDoesNotStepAStationaryFace` で、止まっている顔では `Settle` が `false` を返し、PlayableGraph を進めたあとの処理が 1 回も呼ばれず、警告も付かないことを直接確かめる。速度 0・負のステートの表情は #234 より前と同じく外れる。

## 確認したこと

- `ExpressionSettlementTests` に次を足した。
  - 起きない exit time の遷移が残る表情が値を保ち、警告が「残っていますが」の 1 つだけになる（`AHeldExitTimeIsReportedSeparatelyAndStillChecked`）。
  - 速度 0・-1 の動くクリップが、メニューの読み取りでも試験的な一括収録でも外れる（`AMovingClipOnAStoppedOrReversedClockIsNotSaved`）。
  - 5 秒で始まる 1 秒の中断できる遷移が、5.3 秒の別の遷移に割り込まれて、割り込んだ先の顔になる（`AnInterruptibleTransitionIsSteppedFrameByFrame`）。
  - 自分自身への exit time の遷移と、Can Transition To Self の Any State の遷移で、Add の Parameter Driver が 1 回戻った分だけ動き、「同じ状態へ戻る」が付き、「循環」「残っていますが」は付かない（`ASelfTransitionIsFollowedOnceAndReported`）。
  - 1 秒と 0.7 秒のループで「複数のループが重なる」だけが付く（`LoopsOfDifferentLengthsAreReportedAsOverlapping`）。
  - 70 段の exit time の連鎖が「最後までたどれない」で外れる（`AChainLongerThanTheHopLimitIsNotSaved`）。
  - 70 秒後の exit time と 90 秒のクリップで「60秒を超える」が付き、5 秒後の exit time では付かない（`AFaceAfterMoreThanAMinuteIsReported`・`AStateReachedByALateExitTimeIsFollowed`）。
  - 止まっている顔で `Settle` が何も進めない（`SettleDoesNotStepAStationaryFace`）。
  - 循環のテスト（`CyclicExitTimesStopAtTheFirstRevisitedState`）は、最初に戻った A の値 10 だけを正解にした。
- `NeutralLayerControlTests.SelectedExpressionsCannotCertifyUnprovenFutureRelayControls` の Blend Tree の入力のケースは、遷移の条件が今の入力で偽になるので、期待する警告を「残っていますが」に直した。
- `Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES` に `ExpressionSettlementTests` を足し、SDK が入った環境でだけ動く `AMenuItemFollowedThroughAnExitTimeIsListedAsApproximated` を互換性の CI でも必ず通すようにした。
- Unity 2022.3.22f1 の EditMode テストで、表情・既定表情・FX の候補・レイヤー制御・工程まわりの 23 クラス（1350 件）を回し、1344 件が通った。2 件は APL が入っていないときに飛ばすテスト。落ちた 4 件は `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` の APL のケースで、テスト用のプロジェクトに APL が入っていないことによる（この変更の前から落ちる）。
- `Tools/test-compatibility-tooling.py` の 64 件が通った。
