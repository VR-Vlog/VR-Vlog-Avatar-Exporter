# 逆光ライトの色のアルファを、MToon のリムの強さに入れる

- 日付: 2026-10-24
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/MaterialFallbackTests.cs`・`Tools/validate.py`

## 背景

黒に近いマテリアルに、薄い逆光ライト（Backlight、色 (0.74, 0.72, 0.87)・アルファ 0.28）を付けたアバターを書き出すと、MToon のフォールバックでは全身が薄紫に光って見えた。lilToon では暗い面のふちがわずかに明るくなるだけの設定である。

## 原因

`FallbackColor` の `rimColor` は、リムライト（`_UseRim`）が有効なときは `MobileMaterialMath.MatcapColor(_RimColor, 1f)` で色のアルファを RGB に掛けていた。一方、リムライトが無効で逆光ライト（`_UseBacklight`）が有効なときは `_BacklightColor` をそのまま返し、アルファを無視していた。lilToon 2.3.0 以降の `lilBacklight` は `backlight * backlightColor.a * backlightColor.rgb` を足すので、アルファが強さを決める。アルファ 0.28 の逆光ライトが、強さ 1 の MToon のパラメトリックリムとして書き出されていた。

## 決定

逆光ライトもリムライトと同じく `MobileMaterialMath.MatcapColor(_BacklightColor, 1f)` を通し、アルファを RGB に掛けてから MToon の `parametricRimColorFactor` に入れる。

- アルファ 1 の逆光ライトの書き出しは変わらない（線形空間で 1 を掛けて sRGB に戻すだけ）。
- アルファ 0 の逆光ライトは黒になる。lilToon でも何も足さないので、これが正しい。このとき `parametricRim.maxColorComponent <= 0f` になるため、`RimMultiplyTexture` には逆光ライトの代わりに MatCap の近似の重みが入る。リムライトも逆光ライトも描かれないマテリアルと同じ扱いである。
- フレネルの指数・`_BacklightColorTex` のアルファ・MatCap のマスクの条件は変えていない。

## 成り立たない場合

- lilToon 2.3.0 より前はアルファを無視していた（`Compatibility/README.md`）。アプリは 2.3.4 の規則で描くので、書き出しも 2.3.4 に合わせる。

## 検証

- EditMode: `MaterialFallbackTests.BacklightRimIsScaledByBacklightColorAlpha`（リムライト無効・逆光ライト有効で、アルファ 0.25 のとき `MatcapColor(color, 1)` と一致して元の色より暗いこと、アルファ 1 のとき元の色と一致すること、アルファ 0 のとき黒になることを確かめる）。Unity 2022.3.22f1・lilToon 2.3.4 の空のプロジェクトで `MaterialFallbackTests` を回し、このテストは通った。同じクラスの `GradatedEmissionUsesTheAverageOfTheGradation`（2 件）と `OutlineMaskConvertsRedToGreenAndRetainsSource` は修正前の develop でも同じく失敗し、この変更とは関係しない。
- `Tools/validate.py` の実装チェックを新しい式に合わせた。
