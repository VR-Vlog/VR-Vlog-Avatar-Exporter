# MToon 0.x の影の範囲を lilToon の境界へ正しく写す

- 日付: 2026-11-09
- 対象: `Editor/UnsupportedShaderSubstitution.cs` の `ApplyShade`

## 背景

対応していないシェーダーを lilToon へ近似するとき、MToon 0.x の `_ShadeShift`・`_ShadeToony` から `_ShadowBorder`・`_ShadowBlur` を作っている。式は `border = .5 - shift * .5`、`blur = 1 - toony` だった。

MToon 0.x は N·L を -1〜1 のまま見て、`shift` から `lerp(1, shift, toony)` の間で影を切る（同梱の `MToon10Migrator` の `GetShadingRange0X` と同じ）。`shift` を下げると影は狭くなる。修正前の式は `shift` の符号が逆で、`shift` が -0.5 のとき影の境界が 0.75 になり、本来より影が広がっていた。`blur` は `shift` に依らず半ランバートの幅にもなっていなかった。

## 決定

lilToon は半ランバート `N·L * .5 + .5` を見て `border` を中心に `blur` の幅で切るので、範囲の端 `min`・`max` から `border = (min + max) * .25 + .5`、`blur = (max - min) * .5` を求める。

`_BaseColor_Step`・`_BaseShade_Feather` を持つシェーダーは今までどおりその値を使う。この式を使うのは、シェーダー名に `MToon` を含み `_ShadeShift`・`_ShadeToony` の両方を持つときだけにする。同じ名前のプロパティを別の意味で使うシェーダーがあるので、それ以外とシェーダーが見つからないものは従来の式のまま出力を変えない。

## 検討した代替案と不採用理由

- 名前を問わず同じプロパティ名なら新しい式にする: 別の式で影を作るシェーダーでは、今より明るくなる面が出る。
- MToon 1.0 の `_ShadingShiftFactor` と同じ式に揃える: 1.0 の値は 0.x と定義が違い、そのままでは同じ見た目にならない。

## 確かめたこと

`ShaderAlphaFamilyTests.MToonShadeRangeBecomesTheSameLilToonBorder` は修正前の式で 5 件とも失敗し、修正後に通る。`OtherShadersKeepTheEarlierShadeMapping` が MToon 以外と片方だけの場合に従来の値を固定する。シェーダー近似の関連テスト 207 件は通る。
