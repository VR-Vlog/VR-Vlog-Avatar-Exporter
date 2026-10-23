# 表情ボードは元のアバターと食い違っても止めずに複製を作り直す

- 日付: 2026-10-23
- 対象: `Editor/ExperimentalExpressionCaptureSession.cs`（`SourceChangedException`・`SourceDifference`・`RequireSourceForBatch`・`TrustCopyForBatch`）・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`（`RunFollowingSource`・`FollowSource`・`ExportOnTheAvatarAsItIs`）・`Editor/ExportRecovery.cs`（`ExportRecoverySourceStamp.Capture`・`Difference`）・`Editor/ExportSourceFingerprint.cs`・`Editor/LanTransfer/AppTransferFlow.cs`（`SentencePerLine`）・`Editor/LanTransfer/AppTransferPanel.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Tests/LanTransfer/AppTransferFlowTests.cs`・`CHANGELOG.md`

## 背景

**アプリに転送（通常）** の3ページ目で書き出すと、アバターを何も変えていないのに「書き出せませんでした　元のアバターが変更されました。「変更」からアバターを指定し直してください」と出て止まる報告があった。最後の「。」だけが次の行に折り返されていた。

表情ボードは開いたときに元のアバター全体（参照するアセットを含む）のハッシュを取り、書き出す前に今のハッシュと比べて、違えば止めていた。報告のときは実際にハッシュが食い違っていた（比較の不具合ではない）が、ボードを開いてから書き出すまでに元のアバターのアセットのファイルは変わっていなかった。同じ手順（再コンパイル後にボードを開く・数分おいて書き出す・2回続けて書き出す・除外のプレビュー・ページの移動・3ページ目からの転送）を流しても、どれも一致したまま書き出せた。食い違った値は、変わる前の状態が残っていないので特定できていない。候補は、一部の軸だけを動かす回転のコンストレイントが付いた回転（1e-7 の幅で比べている）と、再コンパイルの直後にエディタが読み直すアセットの値。

判定は、ボードを開いたあとに元のアバターが変わり、ボードの記録（メッシュのパスと BlendShape 名）が書き出す中身と合わなくなるのを防ぐためにある。VRM はボードの複製からではなく、書き出すときに元のアバターから作り直すため。

## 決定

- 元のアバターが複製と食い違っていたら、止めずに、ボードの選択（割り当て・除外・ポーズ・まばたき）を残したまま複製を今のアバターから作り直し、同じ操作をもう一度行う。作り直しは除外の変更で使っている `RebuildKeepingAssignments` を使う。ユーザーが元のアバターを本当に変えたときも、その変更がそのまま書き出しに入る。
- 作り直したときは、最初に食い違った値（`SkinnedMeshRenderer 'Body'` のような型と名前、増えた・消えたもの、回転の値など）を Console に出す。次に同じことが起きたときに原因を特定するため。ハッシュは、詳しく取る場合だけ部分ごとに区切りを記録し、区切りごとの値を比べて最初に違う部分を出す。
- 作り直しても戻せないもの（ファイルから読んだもの以外の記録した表情）があるときだけ、これまでどおり止める。
- 作り直した結果、追加したアニメーションファイルが今のアバターでは使えなくなったときは、ファイルを確かめ直したときと同じくそのファイルをボードから外して理由を出し、書き出しはその理由で止める。そのファイルの表情が抜けた VRM を黙って書き出さないため。
- 何度読んでも違う値になるものがあっても作り直しを繰り返さないよう、作り直したあと次の操作までは、まばたきの説明・サムネイル・スロットのメニューは複製をそのまま使う（`TrustCopyForBatch`）。書き出しと `RequireCurrentSource` は毎回ハッシュを比べる。
- ボードを閉じたあとの **書き出しに失敗したとき** の画面からの再試行は、元のアバターをそのまま書き出す（`ExportOnTheAvatarAsItIs`）。
- 失敗の文は、アプリに転送のほかの文と同じく1行に1文とし、行末の「。」を外す（`AppTransferFlow.SentencePerLine`）。かぎ括弧の中の「。」は残す。

## 自分で食い違いを作りうる経路と、それを確かめるテスト

`Tests/Editor/ExperimentalExpressionCaptureTests.cs` に1経路1テストで置いた。

| 経路 | テスト |
|---|---|
| 3ページ目からの転送・保存 | `ATransferAfterAnEditOfTheSourceFollowsItWithTheSameChoices` |
| 書き出しの途中の変更は見逃さない | `ExportInABatchStillSeesAnEditOfTheSource` |
| 表情の割り当て（キューに積んだ操作） | `AChoiceAfterAnEditOfTheSourceRunsOnARebuiltCopy` |
| 何もしていないときの確認 | `CheckingTheFilesAfterAnEditRebuildsTheCopyWithoutAnError` |
| まばたきの説明の行 | `TheBlinkLineNeverShowsTheChangedSourceMessage` |
| スロットのメニュー | `ThePresetMenuWaitsForTheRebuiltCopy` |
| 何度読んでも違う値（作り直しのくり返し） | `AValueThatReadsDifferentlyEachTimeRebuildsOncePerAction` |
| 失敗時の画面からの再試行 | `ARetryFromTheRecoveryWindowAfterAnEditExportsTheAvatarAsItIs` |
| 作り直しで、追加したアニメーションファイルが使えなくなった | `ATransferStopsWhenTheRebuiltCopyDropsAnAddedFile` |
| 除外の切り替え | `ExcludingAnObjectDoesNotLookLikeAnEditOfTheSource` |
| ページの移動・除外のプレビュー用の複製・別の複製の作成と破棄 | `PagesAndOtherCopiesLeaveTheSourceAsItWas` |
| 取り直したハッシュ（再読み込みのあとと同じ） | `ADetailedStampMatchesTheSameAvatarAndNamesWhatChanged` |
| 失敗の文の「。」 | `CloudTransferTests.FailureReadsOneSentencePerLine` |

## 検討して採らなかった案

- 判定を外す案は採らなかった。開いたあとに BlendShape を消したりメッシュを差し替えたりすると、ボードの記録と書き出す中身が合わなくなるため。
- ボードを開いたときに元のアバターを丸ごと複製し、以後の変更を無視する案は採らなかった。ボードの複製は NDMF をかけたあとのもので書き出しには使えない。元のアバターを複製してもマテリアル・メッシュ・アニメーションなどのアセットは共有なので、アセットの変更は防げない。大きいアバターでは複製に数秒かかる。
- 回転の許容幅を広げる案は採らなかった。食い違った値が特定できていないので、それで直る根拠がない。

## 欠点

- 食い違うと複製を作り直す分（大きいアバターで数秒）だけ書き出しが遅くなる。
- 食い違った原因そのものは直していない。Console の値から次に特定する。
