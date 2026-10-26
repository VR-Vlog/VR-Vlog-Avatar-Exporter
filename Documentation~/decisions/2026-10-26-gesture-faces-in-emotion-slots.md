# 感情の枠に入ったハンドジェスチャーの顔も custom に書く

- 日付: 2026-10-26
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`

## 背景

表情ボードは、表情の名前から喜怒哀楽の枠に顔を自動で入れる（`SuggestPresets`）。`Nagomi` のような名前の顔は relaxed に入る。感情の枠に入った顔は `ExpressionBoard.ExportedCustom` に入らず、VRM のプリセットにだけ書かれる。動く顔だけは、動きを残すために custom にも書き足していた。

ハンドジェスチャーの表（`VRVLOG_expression_gestures`）は custom の名前だけを指す（`Documentation~/ExpressionGestures.md`）。このため、ハンドジェスチャーで出る静止した顔が感情の枠に自動で入ると、その顔は custom に無く、表のそのジェスチャーのマスは null になっていた。VRChat ではそのジェスチャーで顔が変わるのに、アプリでは変わらない。あるアバターでは、左手のサムズアップで出る顔のマス 2 つがこの形で null だった。

## 決定

- 感情の枠に入った顔のうち、アバターのコントローラーでハンドジェスチャーが再生する顔（`ExperimentalExpressionCaptureSession.IsGestureClip`。ボードが「ジェスチャーの顔」として扱う集合と同じ）は、静止していても custom にも書く。動く顔はこれまでどおり custom にも書く。
- プリセットはそのまま残す。表の仕様（custom の名前だけを指す）とアプリは変えない。
- 自動で入ったか手で入れたかは問わない。どちらでも表はその顔を custom の名前で指すので、custom に無いと同じようにマスが null になる。
- 手の握り具合で進む顔（Motion Time で再生する顔）も、ハンドジェスチャーが再生する顔なので同じく custom に書く。
- 名前の重複・予約名の扱いは、動く顔を書き足すときと同じにする（同じ名前の顔が先に custom にあれば書き足さない）。

表にプリセット名も許す案は、表の仕様とアプリの再生の両方を変える必要があるので採らなかった。

## 確認

- `ExperimentalExpressionCaptureTests.ListOnlyExportKeepsAStillGestureSlotFaceInTheGestureTable`: 左手のサムズアップで静止した `Nagomi` を出す FX のアバターで、`Nagomi` が relaxed に自動で入り、保存した VRM の custom にも入り、表の左手サムズアップの 8 マスがすべて `Nagomi` を指す。
- 背景に書いたアバターを修正前と修正後で書き出して比べた。修正前は null だった 2 マスが、修正後は新しく custom に入った顔を指す。ほかのマス・プリセットは変わらず、custom が 1 つ増えただけだった。

## 欠点

- その顔はプリセットと custom の両方に入るので、VRM の表情の数が 1 つ増える。
