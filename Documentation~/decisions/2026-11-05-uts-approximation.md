# Unity-chan Toon Shader（UTS2・UTS3）の影・リム・マットキャップ・輪郭線を写す

- 日付: 2026-11-05
- 対象: `Editor/ShaderApprox/UtsApproximation.cs`・`Editor/ShaderApprox/ShadeLayers.cs`・`Tests/Editor/ShaderApprox/UtsApproximationTests.cs`

## 背景

UTS のマテリアルは透過（2026-10-18 の決定）と主画像だけが写り、影は lilToon の既定値で描かれていた。UTS は影を 2 段の色と画像で持つので、影の色が元と大きく変わっていた。

## 決定

`IShaderApproximation` の形で 1 クラスにする。透過は既存の処理のままで、次を足す。

- 影: `_BaseColor_Step` と `_BaseShade_Feather` を 1 段目、`_ShadeColor_Step` と `_1st2nd_Shades_Feather` を 2 段目の境界とぼかしに写す（ShadingGradeMap の版は `_1st_ShadeColor_Step` などの同じ役割の名前）。UTS も lilToon も half-Lambert で判定する。UTS のぼかしは step の手前から始まり、lilToon は境界を中心にするので、ぼかしの半分だけ境界をずらす。
- 影の色: `_Use_BaseAs1st`・`_Use_1stAs2nd` が有効なら主画像に掛ける色として、無効なら「画像 × 色」をそのまま影の色として写す。
- 影の画像のアルファ: lilToon は影の画像のアルファで主色と混ぜ、UTS は RGB だけを読む。アルファのある画像は不透明に描き直す。
- `_Use_BaseAs1st` の影: 主色との比にすれば画像は要らない。影の色が主色より明るいときは比が 1 で止まるので、主画像を影の画像にして色をそのまま写す。
- 影の位置: `_Set_1st_ShadePosition` と `_ShadingGradeMap` は `_ShadowBorderMask` に写す。黒と白は元と一致し、中間の値は lilToon の掛け算になるので近似になる。
- 影の画像と位置マスクのタイリング: lilToon は主画像の UV で読むので写せない。主画像と違うときは名前を警告に出す。
- リム: `_RimLight_Power` を lilToon のフレネルの指数に変換する。マスクは `_RimColorTex` に写す。`_LightDirection_MaskOn` は half-Lambert を引く処理で影とは関係ないので、`_RimDirStrength` と `_RimDirRange` に写す。
- マットキャップ: 加算・乗算の切り替えと、影で隠す量を写す。
- 輪郭線: `_Outline_Width` が 0 より大きいとき輪郭線付きの lilToon にし、幅・色・`_Is_BlendBaseColor` を写す。
- lilToon に無いもの（ハイカラー、エンジェルリング、発光のアニメーションなど）は名前を警告に出す。

同じ警告の一覧に並ぶ文で「材質」と書いていた 3 文を、用語を揃えて「マテリアル」にした。

## 確認したこと

- EditMode テストで、バリアントごとの描画モードと写した値を確かめた。配布物のシェーダー本体は入れず、プロパティ名だけを宣言した代わりのシェーダーを使う。
- 配布物のシェーダーを入れた検証用プロジェクトで、不透明・ShadingGradeMap・カットアウト・半透明・輪郭線・リムとマットキャップ・両面を実際の書き出し経路で書き出し、元のシェーダーと比べた。

## 残る差

- MToon は影の位置の画像を持たないので、ShadingGradeMap の影の出方は lilToon だけで再現する。
- UTS2 の TransClipping は不透明な輪郭線のパスで裏面も描くので、背景が透けにくい。書き出し後はアルファのとおりに背景が透ける。主画像の透過を優先した。
- UTS3 は輪郭線のパスの有効・無効をマテリアルの値から読めないので、幅だけで判定する。
- `_Is_BlendBaseColor` の輪郭線は元が主画像を 2 回掛ける。lilToon の画像は 1 枚なので、2 回目は色だけ掛ける。濃い部分は元より明るい。
- 2 段目の影に画像があるとき、MToon は画像を読まずに色だけを畳むので、2 段目の帯が元より明るくなる。lilToon のマテリアルでも同じで、アプリは lilToon の情報で描くので、共有の処理は変えない。
- 両面（UTS2 の両面のバリアント）は、MToon で裏面の陰影の差が残る。#674 の検証用プロジェクトの比較で、元のシェーダーとの差は lilToon が 5.22 から 3.33 に減ったが、MToon は 9.62 から 9.26 とほとんど減らなかった。比較画像で MToon が元と違って見えたのは、球の内側（裏面が見える所）の陰影である。
  - 裏面の法線の扱いがシェーダーで違う。UTS2 は `VFACE` を受け取るが使わず、裏面も表の法線のまま陰影を計算する（`UCTS_DoubleShadeWithFeather.cginc` の `frag`）。lilToon は `_FlipNormal` が 0 なら反転しない（`lil_common_frag.hlsl`）。UTS の置き換えは `_FlipNormal` を書かないので、既定の 0 のままである。MToon 1.0 は裏面で法線を必ず反転する（UniVRM の `vrmc_materials_mtoon_geometry_normal.hlsl`）。MToon のプロパティにはカリングの `_DoubleSided` しか無く、反転をやめる項目が無いので、書き出しでは合わせられない。
  - この違いが MToon の差の主な理由と見ているが、裏面の扱いだけを変えた比較はしていない。

## 検討した代替案と不採用理由

- 半透明に輪郭線付きの lilToon を当てて UTS2 の裏面を再現する: 幅が 0 でも輪郭線の分の描画が増え、MToon には写せない。
- 影を画像に焼き込む: 色と画像の組み合わせは lilToon の項目でそのまま持てるので、焼く理由がない。
