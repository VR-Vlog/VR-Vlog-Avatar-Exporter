# ポーズ確認の画面を、アバターが無くなったら閉じる

- 日付: 2026-11-03
- 対象: `Editor/PoseReviewWindow.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Tests/Editor/PoseReviewWindowTests.cs`・`Tools/run-unity-compatibility.py`

## 背景

シーンを開くと、表情ボードやポーズ確認の画面を開いたまま、元のアバターが消える。同じ種類の不具合を調べるなかで、シーンを開いたあとに全画面を動かしたところ、ポーズ確認の画面だけは作り直しのときに Unity の英語の文言がそのまま error 欄に出た。コンソールのエラーにはならない。

まばたきの設定の画面（`BlinkConfigurationWindow`）は、`OnGUI` の最初で元のアバターを見て、無ければ閉じている。

## 決定

ポーズ確認の画面も `OnGUI` の最初で `source` を見て、無ければ閉じる。まばたきの設定の画面と同じ書き方にする。

表情ボードが持つこの画面への参照は、自動で閉じたあとも残る。`poseReview?.Close()` は Unity の破棄の判定を通らないので、`!= null` で見てから閉じる。

## 採らなかった案

- 分かりやすい文言を出して画面を開いたままにする: 開いていても操作できることが無い。文言を足すより、画面を閉じるほうが単純で、他の画面と揃う。

## できないこと

閉じる前に確認は出さない。ポーズの編集結果は元のアバターと一緒に無くなっているので、残す先が無い。

## 確かめたこと

- `WindowClosesWhenItsAvatarIsGone`: 画面を開いたままアバターを消すと、次の描画で画面が閉じる。
