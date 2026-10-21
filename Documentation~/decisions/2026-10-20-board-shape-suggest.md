# 表情ボードの自動の候補にも、動かす BlendShape から喜怒哀楽を決める段を入れる

- 日付: 2026-10-20
- 対象: `Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Tests/Editor/ExpressionPresetTests.cs`・`CHANGELOG.md`

## 背景

#246 で、**アプリに転送（かんたん）** と一括書き出し画面は、名前で決まらないメニューの表情（`1`・`Face 3` のような名前だけのもの）を、その表情が動かす BlendShape の組み合わせから喜怒哀楽に割り当てるようになった。一方、「VRM書き出し」と **アプリに転送（通常）** の表情ボードの自動の候補（`SuggestPresets`）は名前だけで決めていた。同じアバターでも、かんたんと通常で入る表情が違っていた。

## 決定

`SuggestPresets` にも同じ段を入れる。名前の段で埋まらなかった感情の枠に、名前が何も言っていないクリップを、最後のフレームで基準の顔から動く BlendShape を `ExpressionShapeNames.Classify` で分けて入れる。支持数の大きいもの、次にクリップの順で埋める。作者が設定した表情と名前から入れた候補は変えない。

基準の顔との差を取るのは、ボードが他の確認で使っている `BuildPresetPose` と `BlinkMorphs` を使い回す。かんたんの経路はメッシュの静止時の重さと比べるが、基準の顔を取り直していなければ同じ値になる。

## 入れなかったもの

- 表情ボードに、その候補がどの段で入ったかを表示すること。ボードは自動かどうかだけを表示しているため。

## 確認したこと

- `ExpressionPresetTests` に 3 件を追加（名前の無いクリップが喜に入る・感情の名前のクリップが勝つ・作者が設定した喜は上書きしない）。
- `ExpressionPresetTests`・`ExpressionNameInferenceTests`・`ExpressionBoardTests`・`ExpressionSlotLayoutTests` の 179 件が通過。
