# 表情ボードがコピーを作り直しても、まばたきとポーズのウィンドウを閉じない

- 日付: 2026-11-02
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`（`RebuildKeepingAssignments`・`PrepareWith`・`Cleanup`・`CloseChildWindows`・`ChangeAvatar`・`OnDisable`）・`Editor/ExperimentalExpressionCaptureSession.cs`（`Create`・コンストラクタ・`PoseOptions`）・`Tests/Editor/AppTransferQuickExportTests.cs`

## 背景

表情ボードは、書き出さないオブジェクトを変えたときや、元のアバターがプレビューのコピーと違っていたとき（`FollowSource`）に、選んだ内容を残したままコピーを作り直す（`RebuildKeepingAssignments`）。このとき、開いていた「確認・調整」（まばたき）と「ポーズを確認」のウィンドウが閉じていた。

## 原因

- 作り直しは `PrepareWith` を通り、その最初で `Cleanup` を呼ぶ。`Cleanup` はコピーとそこから読んだ内容を捨てる処理で、まばたきとポーズのウィンドウを閉じる処理も入っていた。アバターを変えるとき・ボードを閉じるときと、同じアバターのコピーを作り直すときを分けていなかった。
- ポーズのウィンドウは、ボードの `session.PoseOptions` を参照で持ち、その中身を直接書き換える。作り直すと新しい session が新しい `PoseExportOptions` を持ち、古い中身を写していた。ウィンドウを開いたままにすると、ウィンドウの変更は古いほうに入り、書き出しに入らなくなる。ウィンドウを閉じていたので表に出ていなかった。

## 決定

- ウィンドウを閉じる処理を `Cleanup` から `CloseChildWindows` に分けた。呼ぶのはアバターを変えるとき（`ChangeAvatar`）、ボードを閉じるとき（`OnDisable`）、作り直しに失敗してコピーが無くなったときだけにした。どちらのウィンドウも読むのは元のアバターで、ボードのコピーではないので、同じアバターのまま作り直しても開いたままでよい。
- ポーズの一覧（`PoseExportOptions`）は作り直しても同じものを使う。session を作るときに一覧を渡せるようにし（`poseOptions`）、作り直しでは前の session の一覧をそのまま渡す。保存された設定からポーズを読むのは、一覧を渡さない最初の準備のときだけにした。これまでの「写して入れ直す」処理は消した。
- まばたきのウィンドウは自分で設定のコピーを持ち、決めたときにボードの値へ書き戻すだけなので、変えていない。

## テスト

- `AppTransferQuickExportTests.ARebuildKeepsTheBlinkAndPoseWindowsOpen` を足した。まばたきとポーズのウィンドウを表示した状態で作り直し、両方のウィンドウが開いたままであること、新しいコピーのポーズの一覧がポーズのウィンドウが書き換えるものと同じであること、アバターを変えると両方閉じることを確かめる。修正前の develop では「まばたきのウィンドウが閉じる」で落ちる。
- 既存の `TheHiddenBoardUsesTheSavedPosesOnce`（作り直しで保存されたポーズが二重に入らない）はそのまま通る。
