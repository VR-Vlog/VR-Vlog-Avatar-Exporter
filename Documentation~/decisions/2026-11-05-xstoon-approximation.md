# Xiexe XSToon の影・リム・マットキャップ・輪郭線・裏面を写す

- 日付: 2026-11-05
- 対象: `Editor/ShaderApprox/XsToonApproximation.cs`・`Tests/Editor/ShaderApprox/XsToonApproximationTests.cs`

## 背景

XSToon のマテリアルは主画像・色・カットオフは汎用の読み方で写るが、`_BlendMode` による描画モードと影の種類が読まれず、見た目が元と変わっていた。

## 決定

`IShaderApproximation` の形で 1 クラスにする。通常版と輪郭線付きは同じプロパティを宣言していて値では見分けられないので、シェーダー名で分けて 2 つ登録する。シェーダーが見つからないマテリアルは輪郭線なしとして描く。

- 描画モード: `_BlendMode` の 0 を不透明、1〜3 をカットアウト、4〜6 を半透明にする。加算（6）は `Additive Blend` として警告に出す。
- 影: `_ShadowType` を読む。
  - Ramp（0）: ランプの横 1 列を読んで明るさが変わる範囲を影の境界とぼかしにし、暗い端の色を影の色にする。白いランプは影を描かない。`_RampSelectionMask` で別の行を選んでいるときは写せないので `Ramp Selection` として警告に出す。
  - Shade Map（1）: 影で主画像の代わりに描くので、「画像 × `_ShadowColor`」をそのまま影の色にする。
  - Solid（2）: `_ShadowColor` を主画像に掛ける色にする。
  - Shade Map と Solid の境界は `_ShadowRange`、ぼかしは `_ShadowSharpness` から求める（幅は range ± (1 - sharpness)）。
- 裏面: XSToon は裏面の法線を反転するので `_FlipNormal` を有効にする。カリングは汎用の読み方が `_Culling` を読む。
- 色調補正: `_Hue`・`_Saturation`・`_Value` を `_MainTexHSVG` に写す。元は主画像 × `_Color` を補正し、lilToon は画像だけを補正してから色を掛けるので、`_HSVMask` が無いときは `_Color` にも色相と彩度を掛ける。明度は二重にしないよう掛けない。
- リム: XSToon のリムは (1 - N·V) × N·L^threshold で、`_RimRange` ± `_RimSharpness` でぼける。`_RimDirStrength` を 1 にし、`_RimDirRange` を N·L = .25 で元の N·L^threshold と同じ値になるように決める（threshold が 1 なら -0.5 で N·L そのもの）。光の側だけに出す。影側のリムは描かない。
- マットキャップ: `_ReflectionMode` が Matcap のときだけ写す。加算は環境光と直接光の半分で照らされるので、lilToon では `_MatCapBlend` を半分にして照明を有効にする。乗算は照明を受けないので照明を切る。減算は `Matcap (Subtractive)` として警告に出す。PBR とベイクしたキューブマップは `Reflection` として警告に出す。
- 輪郭線: 輪郭線付きのシェーダーで `_OutlineWidth` が 0 より大きいときに輪郭線付きの lilToon にし、幅・色・`_OutlineAlbedoTint`・`_OutlineLighting` を写す。
- lilToon に無いもの（スペキュラー、サブサーフェス、ハーフトーン、ディゾルブなど）は名前を警告に出す。

## 確認したこと

- EditMode テストで、写した値を確かめた。配布物のシェーダー本体は入れず、プロパティ名だけを宣言した代わりのシェーダーを使う。
- 配布物のシェーダーを入れた検証用プロジェクトで、Solid・Ramp・Shade Map・カットアウト・半透明・輪郭線・リムとマットキャップ・両面を実際の書き出し経路で書き出し、元のシェーダーと比べた。

## 残る差

- MToon のリムは光の向きを見ないので、XSToon のリムは MToon では全周に出る。アプリは lilToon の情報で描くので光の側に出る。
- `_RimThreshold` は 1 点で合わせているので、N·L が 0 の近くでは元と形が違う。裏側は影のマスクで隠す。
- 加算のマットキャップは環境光の分まで半分にするので、元より少し暗い。

## 検討した代替案と不採用理由

- 通常版と輪郭線付きを `_OutlineWidth` の値で見分ける: 通常版も同じ名前を宣言しているので、輪郭線の無いマテリアルに輪郭線が出る。
- ランプをそのまま影の画像にする: lilToon の影の画像は UV で引くので、half-Lambert で引くランプの代わりにならない。
