# 標準MToonの互換表示の輪郭線にベースの色を掛けない

- 日付: 2026-10-23
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/MaterialFallbackTests.cs`・`CHANGELOG.md`

## 背景

標準MToon（VRM 1.0の`VRMC_materials_mtoon`。このExporterはVRM 1.0だけを書き出す）の互換表示の材質を作るとき、`OutlineLightingMixFactor`にlilToonの`_OutlineEnableLighting`をそのまま入れていた。2つは同じ名前の項目に見えるが、意味が違う。

- lilToon 2.3.4の`_OutlineEnableLighting`（既定値1）は、輪郭線の色にシーンの光の色を掛けるかどうか。`lil_pass_forward_normal.hlsl`では`lerp(col, col * min(lightColor + addLightColor, _LightMaxLimit), _OutlineEnableLighting)`で、`col`は`_OutlineColor`と`_OutlineTex`の色。白い光なら輪郭線の色はほぼそのまま出る。
- MToon10の`_OutlineLightingMix`は、同梱のUniVRMの`vrmc_materials_mtoon_lighting_mtoon.hlsl`で`_OutlineColor * lerp(1, baseCol, _OutlineLightingMix)`。`baseCol`は主画像とベースの色に光と影を掛けた本体の色なので、1にすると輪郭線の色に本体の色が掛かる。

lilToonの輪郭線の色は暗めの色が多く、そこへ本体の色が掛かるので、既定値のままの材質でも輪郭線がほぼ黒になっていた。

## 決定

- `OutlineLightingMixFactor`は常に0にし、`_OutlineEnableLighting`を読まない。輪郭線は`_OutlineColor`の色で出る。白い光の下のlilToonの見た目に近い。
- `MaterialFallbackTests.OutlineKeepsAuthoredColorInsteadOfMultiplyingByAlbedo`で、公式lilToonの既定値（`_OutlineEnableLighting`が1）の材質から作った互換表示の`_OutlineLightingMix`が0であることを確かめる。

lilToonの拡張に書く材質の値は変えない。

## 検討した代替案と不採用理由

- `_OutlineEnableLighting`を0から1の間の別の値に変換する: MToonには光の色だけを輪郭線に掛ける項目が無く、どの値でも本体の色が混ざる。

## 欠点

- 暗い場所でもlilToonでは暗くなる輪郭線が、互換表示では明るいまま出る。
- `_OutlineTex`で輪郭線に色を付けた材質は、これまでどおり互換表示に色が出ない（MToonの輪郭線の画像は幅のマスクで、色に使えない）。
