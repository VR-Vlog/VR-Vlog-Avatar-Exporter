# 標準MToonの影の色を主色との積にする

- 日付: 2026-10-22
- 対象: `Editor/MobileMaterialMath.cs`・`Editor/UniVrmOneClickExporter.cs`

## 背景

標準MToonへの代替出力は、影の色（`ShadeColorFactor`）を `lerp(_Color, _ShadowColor, _ShadowStrength)` で作っていた。MToonは影側を `ShadeColorTexture × ShadeColorFactor` で描き、影の画像には主画像を渡している。lilToon 2.3.4は影の色の画像が無いとき、影側を `albedo × _ShadowColor`（albedo は主画像 × `_Color`）で描き、`_ShadowStrength` で明るい側の色へ近づける。つまり主色は影の係数に掛かる。従来の式は主色と影の色を混ぜるだけなので、主色を暗くした材質（黒い服など）では影の係数が影の色（灰色）に引っ張られて主色より明るくなり、影が明るい側より白っぽく描かれていた。

## 決定

- 影の色の画像（`_ShadowColorTex`、未設定・組み込みの白画像を除く）が無いときは、影の係数を lilToon と同じ `_Color × lerp(1, 影の色, _ShadowStrength)` にする。計算は線形空間で行う。影の色は従来どおり2影の色を混ぜたもの（`Shadow2ndShade`）。
- lilToon は影を明るい側より明るくしない（`min(影, 明るい側)`）ので、`lerp(1, 影の色, 強さ)` の各成分は1を上限にする。
- 影の色の画像があるときは、MToonの影の画像がその画像に置き換わり主画像が掛からないので、従来の `lerp(_Color, 影の色, 強さ)` のままにする。
- `FallbackColor` が返す影の色を変えるので、表情の基準値（`UnifiedExpressionPreparation`）と書き出す係数は同じ値のまま揃う。lilToonの拡張に保存する値は変えない。

## 検討した代替案と不採用理由

- 影の画像に主色を焼き込む: 係数だけで lilToon と同じ式になるので、画像を増やす理由がない。
- 影の色の画像があるときも積にする: その画像は主画像を含まない独立の色なので、主色を掛けると lilToon より暗くなる。
