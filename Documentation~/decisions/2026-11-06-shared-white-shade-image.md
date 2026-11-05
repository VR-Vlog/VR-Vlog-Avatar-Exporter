# 白の影画像を作る処理を 1 か所にまとめる

- 日付: 2026-11-06
- 対象: `Editor/ShaderApprox/ShadeLayers.cs`・`Editor/UnsupportedShaderSubstitution.Toon.cs`・`Editor/ShaderApprox/SilentCelShadingApproximation.cs`

## 背景

lilToon は影の画像が無いと主画像の色に影の色を掛けるので、影の色をそのまま塗る置き換えでは、4x4 の白の画像を影の画像に入れる。この白の画像を、`ShadeLayers.Absolute`（UTS・XSToon）、Poiyomi の `Toon.cs`、Silent's Cel Shading の 3 か所で別々に作っていた。名前も `__VRVlog_WhiteShade` と `__VRVlog_Shade` で揃っておらず、Silent's Cel Shading は影の 1 段目と 2 段目で 1 枚ずつ作っていた。

## 決定

- `ShadeLayers.White(origin, textures, ref white)` を足し、3 か所ともこれで作る。1 枚目を作ったら `white` に持ち、同じマテリアルの他の段では同じ画像を使う。名前は `__VRVlog_WhiteShade` にそろえる。
- 白の画像を入れるかどうかの条件は各シェーダーのまま変えない。Silent's Cel Shading で 2 段とも白にするときに画像が 1 枚になるほかは、書き出す値は変わらない。

## 確かめたこと

置き換えと近似の 9 クラス（402 件）を Gamma と Linear の最小のプロジェクトで回し、どちらも 0 件失敗。Silent's Cel Shading で 2 段が同じ白の画像を使うことを既存のテストに足した。
