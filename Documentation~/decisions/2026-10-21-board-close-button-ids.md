# 表情ボードのカードの × がどのイベントでも control id を取るようにする

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`CHANGELOG.md`

## 背景

表情ボードのカード（`DrawTile`）の × は、マウスがカードの上にあるときだけ表示する。これまでは Repaint のときにマウスがカードの外にあると `GUI.Button` 自体を呼ばず、それ以外のイベントでは呼んでいた。コメントには「ほかのイベントではボタンを残すので、後ろのコントロールの id は変わらない」とあったが、IMGUI の control id は Layout で振った順番を Repaint でもたどるので、Repaint だけボタンを飛ばすと、後ろにある同じ種類のコントロール（次のカードの × など）が 1 つ前の id を受け取っていた。

## 決定

× を `CloseButton` に切り出し、どのイベントでも `GUI.Button` を呼ぶ。Repaint でマウスがカードの外にあるときは、空の内容（`GUIContent.none`）と何も描かないスタイル（`GUIStyle.none`）で呼ぶ。見た目は前と同じで、表示しているときの文字・ツールチップ・スタイルも変えていない。

Repaint で描かないボタンもクリックの判定は Layout・MouseDown などのイベントで行われるので、押したときの動きも変わらない。

## 確認したこと

- `ExperimentalExpressionCaptureTests.TheHiddenCloseButtonKeepsTheIdsOfTheControlsAfterIt`: 小さな EditorWindow で × の前後に `GUI.Button` と同じ hint の id を取り、その差をイベントごとに比べる。前の書き方では Repaint（マウスがカードの外）だけ差が変わることを確かめたうえで、新しい書き方では Repaint（マウスが外・中）と MouseDown の差が Layout と同じになること。
- ウィンドウごとに id の値そのものは違うので、差だけを比べる。IMGUI は hint の違う id を飛ばして合わせ直すため、hint を付けずに id を取ると前の書き方でもずれが見えない。テストでは `GUI.Button` と同じ hint を使う。
