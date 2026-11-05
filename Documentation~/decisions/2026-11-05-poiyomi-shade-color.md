# Poiyomi Toon の ShadeMap の置き換えで、影色を主画像に掛けない

- 日付: 2026-11-05
- 対象: `Editor/UnsupportedShaderSubstitution.Toon.cs`・`Tests/Editor/SectionedToonSubstitutionTests.cs`

## 背景

Poiyomi Toon の書き出しを本物のシェーダーで確かめた。Opaque・Cutout・TransClipping・Fade・Transparent・両面、Flat・Multilayer Math・Texture Ramp・ShadeMap の影、アウトライン、発光、リム、マットキャップ、デカール、色の 22 ケースを、元のシェーダーと書き出し後でピクセル比較した。

ずれていたのは ShadeMap の Replace（`_ShadingShadeMapBlendType` 0）だけだった。Replace の影は `_1st_ShadeMap × _1st_ShadeColor` で、主画像を使わない。これまでは影の画像が無いとき、影色を主色との比で lilToon に渡していたので、主画像があると影色が主画像に掛かって、元より暗く濁っていた。

## 決定

- 主画像があり、`_Use_BaseAs1st` も Multiply も使わず、影の画像が無い層では、4×4 の白い画像を `_ShadowColorTex`（2 層目は `_Shadow2ndColorTex`）に入れる。lilToon は不透明な画像のある所で主画像の代わりに画像 × `_ShadowColor` を使うので、影色が主画像に掛からなくなる。
- `Texture2D.whiteTexture` は使わない。`LilToonMaterialReader` と `UniVrmOneClickExporter.ShadeTexture` が「影の画像なし」として読み飛ばすので、書き出した先で効かない。
- 主画像が無いときはこれまでどおり主色との比にする。画像が要らず、結果も同じになる。

## 残る差

lilToon は影を明るい側の色より明るくしない（`indirectCol = min(indirectCol, directCol)`）。この比較はチャンネルごとなので、影色が主画像より明るいチャンネルは主画像の値で止まり、元では一色の影に主画像の模様が残る。明るさは元に近づくが、色味は揃わない。ShadeMap の既定の影色は白なので、これを変えていないマテリアルでは元の白い影が出ない。式を変えられないので受け入れる。

## 比較の結果

元のシェーダーと書き出し後の差の平均（0〜255）。

| ケース | 変更前 | 変更後 |
|---|---|---|
| 影色 1 層 | 3.9 | 1.6 |
| 影色 2 層 | 5.4 | 3.1 |
| `_Use_BaseAs1st` | 0.7 | 0.7 |
| Multiply | 0.7 | 0.7 |
| 既定の白い影 | 6.6 | 6.6 |

他の 17 ケースは 0.3〜0.8 で、今回の変更の対象外。
