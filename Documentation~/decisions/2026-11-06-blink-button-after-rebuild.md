# 表情ボードが作り直したあとも結果のウィンドウから瞬きの設定を開く

- 日付: 2026-11-06
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`

## 背景

表情ボードからアプリに転送したあと、書き出しの結果のウィンドウの「瞬きを設定」ボタンは、表情ボードが書き出し用のコピーを作り直すと何も起きなくなっていた。ボタンは書き出したときのセッションと今のセッションが同じオブジェクトか（`ReferenceEquals(session, target)`）を見ていて、ファイルを確かめ直す・割り当てを保ったまま作り直すたびに別のオブジェクトになるためだった。

## 決定

瞬きの設定はアバターのものなので、ボタンは今の表情ボードが同じアバター（`source == target.Source`）を開いていれば開く。表情ボードが別のアバターに移ったときと閉じたときは開かない。保存の状態を結果のウィンドウへ返す処理は、書き出したときのセッションにだけ返すので今のまま `ReferenceEquals` で見る。

## 確かめたこと

検証環境（Unity 2022.3.22f1）で `ExperimentalExpressionCaptureTests` を回し、足した `TheResultListBlinkButtonStillOpensAfterARebuild`（作り直したあとは開く・別のアバターに移ったら開かない）が通った。183 件のうち失敗 2 件はファイルのドラッグのテストで、develop でも同じ環境で同じように落ちる。
