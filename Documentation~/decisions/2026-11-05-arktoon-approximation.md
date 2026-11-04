# Arktoon の影・リム・マットキャップ・輪郭線・裏面を写す

- 日付: 2026-11-05
- 対象: `Editor/ShaderApprox/ArktoonApproximation.cs`・`Tests/Editor/ShaderApprox/ArktoonApproximationTests.cs`

## 背景

Arktoon のマテリアルは透過（2026-10-18 の決定）と主画像だけが写り、影やリムは lilToon の既定値で描かれていた。

## 決定

`IShaderApproximation` の形で 1 クラスにする。透過は既存の処理のままで、次を足す。

- 影: `_Shadowborder`・`_ShadowborderBlur`・`_ShadowStrength` を写し、`_ShadowIndirectIntensity` を影の明るさにする。
- Plan B の影: `_ShadowPlanBUsePlanB` が有効なら、カスタム画像と色、または主画像の明るさと色相の変更を影の色として写す。色相の変更は影の画像に焼く。GPU が使えず焼けないときは `Plan B Color Shift` として警告に出す。
- 2 段目の影: 2 段目の境界は 1 段目に対する割合なので、掛けて lilToon の境界にする。
- リム: `_RimFresnelPower`・`_RimTexture`・`_RimUseBaseTexture` を写し、加算で描く。
- マットキャップ: `_MatcapBlendMode` の 0 を加算、1（Lighten）と 2（Screen）を lilToon のスクリーンに写す。シャドウキャップは 2 つ目のマットキャップの乗算に写す。
- 輪郭線: `_UseOutline` が有効で幅が 0 より大きいときに輪郭線付きの lilToon にする。元は「主画像 × rate + 色 × 輪郭線の画像 × (1 - rate)」で描く。lilToon の画像は 1 枚なので、rate が .5 以上なら主画像、未満なら輪郭線の画像にし、色は rate で混ぜる。主画像にして輪郭線の画像を使わなかったときだけ `Outline Image` を警告に出す。
- 裏面: 両面のとき `_DoubleSidedFlipBackfaceNormal` を `_FlipNormal` に写す。裏面の明るさ `_DoubleSidedBackfaceLightIntensity` は、低いほど影として描くように `_BackfaceForceShadow` に写す。
- lilToon に無いもの（グロス、反射、発光の視差など）は名前を警告に出す。

## 確認したこと

- EditMode テストで、写した値を確かめた。配布物のシェーダー本体は入れず、プロパティ名だけを宣言した代わりのシェーダーを使う。
- 配布物のシェーダーを入れた検証用プロジェクトで、不透明・カットアウト・半透明・輪郭線・Plan B・リムとマットキャップ・両面を実際の書き出し経路で書き出し、元のシェーダーと比べた。

## 残る差

- 裏面の明るさは影の色で近づけているので、元より裏面がやや明るい。MToon には裏面だけを暗くする項目が無く、表と同じ明るさになる。
- `_ShadowUseStep` の段階的な影は lilToon に無いので、`Shadow Steps` として警告に出す。
- カスタムの影の画像にタイリングがあるときは写せないので `Shade Map Tiling` として警告に出す。アルファのある画像は不透明に描き直す。
- マットキャップの Lighten は Screen に写すので、白に近いところが元より明るい。

## 検討した代替案と不採用理由

- 裏面の明るさを元に合わせて影の色を暗くする: 表面の影まで暗くなる。
- 影の色相の変更を常に警告にして写さない: よく使う設定で影の色が大きく変わるので、焼いて写す。
