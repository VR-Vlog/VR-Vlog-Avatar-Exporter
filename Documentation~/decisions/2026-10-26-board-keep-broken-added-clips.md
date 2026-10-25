# 追加したあとで書き出せなくなった表情ファイルを、理由つきでボードに残す

- 日付: 2026-10-26
- 対象: `Editor/ExpressionBoard.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Tests/Editor/ExpressionBoardTests.cs`・`CHANGELOG.md`
- [追加したあとで書き出せなくなった表情ファイルを追加した一覧から外す](2026-10-21-board-drop-broken-added-clips.md) を置き換える。削除されたファイルの扱い（[2026-10-21-board-drop-deleted-added-clips](2026-10-21-board-drop-deleted-added-clips.md)）は変えない。

## 背景

表情ボードに自分で追加した `.anim` を、あとで編集したりアバターを変えたりして書き出せなくなる（`ClipError` が付く）と、確かめ直すところ（`RefreshCandidateValidation`）で追加した一覧（`clipInputs`）から外し、状態の行に 1 回だけ理由を出していた。次の問題があった。

- 状態の行は次の操作で消えるので、利用者はファイルがどこへ行ったか分からない。直しても自動では戻らず、もう一度追加し直すしかない。
- 利用者がその表情を置いた枠は、知らないうちに空になる。
- ボードの表示（`ItemsIn` が `Usable` で絞る）と、書き出しの判定・件数が別の条件で一覧を見ていたので、見えない表情が判定に入る経路が残っていた（PR #273・#296 のレビューの P2）。

## 決定

表示・件数・書き出しの判定が同じ一覧を見るようにし、書き出せない追加ファイルはその一覧に理由つきで残す。

- `ExpressionBoard.Shown` を足し、`ItemsIn` と件数（`OtherCount` など）はこれで絞る。使える表情と、クリップのある追加ファイル（理由があっても）が出る。アバターの表情で書き出せないものは、#273 の決定どおり出さない。
- その他の表情に書き出せない追加ファイルがあるあいだは（`ExpressionBoard.Unexported`）、保存を止めて下の行に「その他の表情の「{0}」は書き出せません。ファイルを直すか、書き出さない表情へ移してください。{1}」を出す。末尾の {1} が理由。黙って抜いた VRM を書かない。
- 書き出せないタイルは、顔の代わりにエラーのアイコンを出して赤い枠で囲む。64px のタイルに文字は入らないので、理由はマウスを重ねたときのツールチップの先頭に出し、最後の行に外し方（「右クリックの「ボードから外す」で外せます」）を書く。
- ファイルを直すと、確かめ直したときに元の場所で使える表情に戻る。利用者が置いた枠もそのまま。
- 右クリックのメニューに「ボードから外す」を足す（追加したファイルだけ）。追加した一覧・保存した記録（`fileRecords` と `session.Expressions`）・利用者が置いた枠・**書き出さない表情** の指定・付けた名前をまとめて外す。同じクリップがアバターの表情でもあるときは、記録だけを外し、アバターの表情としての置き場所と名前は残す。`.anim` 自体は消さない。
- その他の表情の × は、これまでどおり「書き出さない表情へ移す」。外す操作を × に重ねると、移すつもりで押して記録まで消えるので分けた。
- 削除されたファイル（クリップが null）は、これまでどおり一覧から外して状態の行に出す。表示も直すものもないため。

## 入れなかったもの

- 外したあとの取り消し。もう一度追加すれば戻るので今回は入れない（Codex の提案）。
- 削除されたファイルもボードに残すこと。表示するものも直すものもなく、枠はすでに「見つからない」と出して保存を止めている（Codex の提案）。
- クリックで開く詳細の欄。クリックで理由がエラーとして出るので足さない（Codex の提案）。

## 確認したこと

- `ExperimentalExpressionCaptureTests.AnAddedFileThatBecomesUnsupportedStaysOnTheBoardWithItsReason`: 書き出せなくなったファイルが一覧とその他の表情に理由つきで残り、件数が 1、保存が止まって理由の文が出ること。書き出さない表情へ移すと止まらないこと。直すと使える表情に戻ること。
- `ExperimentalExpressionCaptureTests.AnAddedFileInASlotStaysThereAndStopsTheSave`: 枠に置いたファイルは枠に残り、保存が止まること。
- `ExperimentalExpressionCaptureTests.RemovingAnAddedFileTakesItsSlotExclusionNameAndRecordWithIt`: 「ボードから外す」で一覧・記録・書き出さない表情の指定・名前・枠が外れること。
- `ExpressionBoardTests.ABrokenAddedFileIsShownCountedAndStopsTheSave`: ボード単体で、書き出せない追加ファイルが表示・件数・判定に同じように入ること。
- `ExperimentalExpressionCaptureTests.ATransferStopsWhenTheRebuiltCopyDropsAnAddedFile`: 転送で作り直したコピーでファイルが使えなくなったとき、転送が止まり、ファイルが理由つきで残ること（これまでは外れることを確かめていた）。
