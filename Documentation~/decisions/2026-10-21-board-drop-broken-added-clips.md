# 追加したあとで書き出せなくなった表情ファイルを追加した一覧から外す

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`CHANGELOG.md`
- [書き出せない表情をボードに並べない](2026-10-21-hide-unsupported-expressions.md) の「入れなかったもの」の 1 つ目を置き換える。

## 背景

表情ボードに自分で追加した `.anim` を、あとでクリップを編集したりアバターを変えたりして書き出せなくなる（`ClipError` が付く）と、ボードからは消えるが、追加した一覧（`clipInputs`）には残っていた。そのため次のことが起きていた。

- 見えないファイルが 64 件の上限に数えられる。
- `ExpressionBoard.UsesRecordRoute` が `item.Added` で真のままになり、記録して書き出す方に倒れたままになる。
- ボードに出ないので、利用者には外す手段が無い。通知も出ない。

## 決定

書き出せるかを確かめ直すところ（`RefreshCandidateValidation`）で、理由の付いた追加ファイルを追加した一覧から外し、追加できなかったときと同じ文（`RejectedMessage`「「{0}」は追加できません。{1}」）を状態の行に出す。新しい文言は足していない。

- 利用者がその表情を置いた枠（自動ではないもの）は空にする。ボードで枠から表情を外したときと同じく、明示的に空にする。自動で入っていた枠は、続く `ApplyAutomaticPresets` が使える表情から選び直す。
- 同じクリップがアバターの表情（候補）でもあるときは、枠はそのまま残す。アバターのほかの書き出せない表情と同じ扱いで、保存はその枠の理由で止まる。
- **書き出さない表情** の指定と付けた名前は消さない。一覧から外れたクリップはボードの件数や判定に入らず、ファイルを直してもう一度追加したときに元の指定に戻る。
- 保存した記録（`fileRecords`）は触らない。次の保存で今の一覧から記録を作り直すので、外したファイルの記録は残らない。先に記録を外すと、アバターが変わったかの確認（記録に無い表情があるか）が誤って働くため。
- 一覧から外したので、ファイルを直しても自動では戻らない。もう一度追加する。

## 入れなかったもの

- 削除された `.anim`（クリップが null になったもの）を外すこと。今回は理由の付いたファイルに絞った。
- ボードに追加ファイルを 1 件ずつ外すボタンを足すこと。

## 確認したこと

- `ExperimentalExpressionCaptureTests.AnAddedFileThatBecomesUnsupportedIsRemovedAndItsReasonIsShown`: 追加して枠に置いたファイルにボーンのカーブを足して確かめ直すと、追加した一覧から外れ、`UsesRecordRoute` が偽になり、枠が空になり、状態の行に `RejectedMessage` の文が出ること。
- `ExperimentalExpressionCaptureTests.ReimportRefreshesExplicitRowsAndRecordsTheRepairedFile` は、書き出せないあいだも一覧に残ることを前提にしていたので、書き出せるまま編集したファイルが確かめ直しで新しい値で記録されることだけを確かめる形にした。
