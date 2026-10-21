# FX の固定ポーズの確認で、同じ式をローカル関数にまとめる

- 日付: 2026-10-21
- 対象: `Editor/VrChatExpressionSampler.cs`（`ValidateFixedPose`）

## 背景

`ValidateFixedPose` は、評価中の FX の Clip の曲線を 1 本ずつ確かめる。その中で、次の 2 つの式が同じ形で何度も書かれていた。

- 曲線の場所を示す文字列 `layers[layer].name + " / " + info.clip.name + " / " + binding.propertyName`（`ValidateNativeParameterCurve` に渡す。5 か所）
- Animator の曲線が Controller のパラメーターの名前ではないかを見る `!controller.parameters.Any(parameter => parameter.name == binding.propertyName)`（2 か所）

## 決定

曲線ごとのループ（`foreach (var binding in AnimationUtility.GetCurveBindings(info.clip))`）の先頭に、ローカル関数 `CurveLabel()` と `NamesParameter()` を置き、上の 7 か所をその呼び出しに替える。

最後の `throw Unstable(...)` の文字列は `binding.path` も含む別の形なので、そのままにした。

## 書き出す内容が変わらない理由

- ローカル関数は、その場で呼び出すだけで、デリゲートにして後で呼ぶことはない。呼んだ時点の `layer`・`info`・`binding` を読むので、置き換え前にその場で式を評価していたのと同じ値になる。ループの中でこれらの変数は書き換えない。
- 文字列を作るのは、これまでどおり `ValidateNativeParameterCurve` を呼ぶ直前（引数の評価の順番も同じ）で、呼ばない分岐では作らない。
- `NamesParameter()` は、置き換え前と同じく `&&` の右側にあり、左側が偽なら呼ばれない。`controller.parameters` を読む回数も変わらない。

## 確認したこと

- 確認: 検証用の PC での書き出し突き合わせと EditMode テスト（PR 本文に記載）
