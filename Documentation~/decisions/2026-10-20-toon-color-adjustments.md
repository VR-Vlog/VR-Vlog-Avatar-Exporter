# 非対応シェーダーの色調補正と、MToon 表示の 2 影・乗算マットキャップ

- 日付: 2026-10-20
- 対象: `Editor/UnsupportedShaderSubstitution.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/MobileMaterialMath.cs`・`Editor/Locales/*.json`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/ToonColorAdjustProbe.shader`・`Tests/Editor/MaterialFallbackTests.cs`・`Tools/LightingBehaviorTests.cs`・`README.md`・`CHANGELOG.md`

## 背景

- 非対応シェーダーの材質を公式 lilToon に置き換えるとき（2026-10-18 の決定）、元のシェーダーの色調補正（主画像の明るさ・色相・彩度・ガンマなど）は捨てていた。主画像を暗くして使っている材質は、書き出すと元より明るくなっていた。捨てたことも表示していなかった。
- lilToon の材質の MToon 表示（他のビューアーと、アプリの MToon だけの表示）で、次の 2 つが lilToon と違っていた。
  - lilToon のマットキャップの合成方法が乗算（`_MatCapBlendMode` が 3）でも、MToon の加算のマットキャップとして書いていた。乗算で暗くするはずの部分が、MToon では光って見えた。
  - 2 影（`_Shadow2ndColor`）を見ていなかった。lilToon では影の濃い側が 2 影の色になるのに、MToon の影色は 1 影の色だけだった。

## 決定

### 1. 色調補正（置き換えた材質だけ）

公式 lilToon の色調補正 `_MainTexHSVG` は、主画像の色をガンマ（w）で累乗したあと HSV に直し、色相に x を足し、彩度に y、明度に z を掛ける（`saturate` 付き）。そのあと `_Color` を掛ける。書き出しでは `LilToonMainTextureBaker` がこの補正を主画像に焼き込む。

元のシェーダーの補正のうち、この式で同じ結果になるものだけを `_MainTexHSVG` に写す。明度を一律に掛ける補正は RGB の 3 チャンネルに同じ係数を掛けるのと同じなので、z に入れる。

- 主色に 0〜1 の明るさ係数を掛けるシェーダー（`_Bright` を持ち、出力段のガンマ補正 `_EnableGammaFix` か明るさ補正 `_EnableBlightFix` も持つもの）: `z = clamp01(_Bright)`。元の式は `色 × _Color × _Bright` なので、0〜1 の範囲では一致する。
- 主色のあとに色調補正の区画を持つシェーダー（`_MainColorAdjustToggle` が 1 のとき）の明るさ `_MainBrightness`（b）: マスクがないときだけ `z = max(0, 1 + b)`。元の式は `saturate(色 × (1 + b))` で、b が 0 以下なら一致する。b が正のときは、元はチャンネルごとに 1 で切り、lilToon は `_Color` を掛ける前に明度を 1 で切るので、明るい部分の飽和のしかたが少し違う（近い値になる）。
- 両方に当たるときは係数を掛け合わせる。
- z 以外（x・y・w）は書かない。

次の補正は、lilToon の HSV の式で同じ結果にならないので写さない。

- 色相: 色調補正の区画の色相 `_MainHueShift`・`_MainHueShiftSpeed`（`_MainHueShiftToggle` が 1 のとき）、`USE_HUE_SHIFT` のキーワードを持つ材質の `_HueShift`（ラジアン。2π の倍数以外。スライダーの最大値 6.283185 は 2π よりわずかに小さいので、2π の倍数との差が 0.0001 未満なら一周とみなす）。灰色の軸のまわりの回転で、HSV の色相の加算とは結果が違う。
- 彩度 `_Saturation`（0 以外）: 輝度（0.3, 0.59, 0.11）との線形補間で、HSV の彩度の倍率とは違う。
- グラデーション `_MainGradationStrength`（正で、`_ColorGradingToggle` が 1 のとき）: 対応する仕組みが lilToon の色調補正にない。
- マスク付きの明るさ: `_MainColorAdjustTexture` を割り当てた材質、または `_MainBrightnessGlobalMask` が正の材質。`_MainColorAdjustMask` は lilToon ではすべての補正にかかるマスクで、チャンネルの意味も違う。
- 出力段のガンマ補正（`_GammaR/G/B` のどれかが 1 以外）と明るさ補正（`_BlightOutput` が 1 以外か `_BlightOffset` が 0 以外）: ライティングを含めた出力に、チャンネルごとにかかる。lilToon の色調補正は主画像だけにかかる。

写さなかった材質は、保存前の確認の警告に「元のシェーダーの色調補正のうち公式lilToonで再現できないもの（色相・彩度・ガンマ・グラデーション・マスク付きの補正）は反映していません: 」に続けて材質名を出す。明るさだけ写せた材質は、この警告には出さない。

### 2. 乗算のマットキャップ（MToon 表示）

MToon10 のマットキャップは加算だけなので、乗算のマットキャップは近い表現がない。`_UseMatCap` が 1 で `_MatCapBlendMode` が 3 の材質は、MToon のマットキャップを空にする（画像なし、`_MatcapColor` は黒）。リムライトの `_RimLightingMix` は、マットキャップがないときと同じ既定値（1）にする。乗算以外（通常・加算・スクリーン）は従来どおり。lilToon 表示は変えない。この材質の VRM 表情に `matcapColor` の割り当てがあっても、MToon 表示では画像が無いので見た目は変わらない（lilToon 表示では従来どおり効く）。

### 3. 2 影（MToon 表示）

lilToon 2.3.4 の 2 影は次のとおり（`lil_common_frag.hlsl`）。

```text
shadow2nd = lerp(albedo, 2影の画像.rgb, 2影の画像.a) × _Shadow2ndColor.rgb
lns.y     = a2 - lns.y × a2              （a2 = _Shadow2ndColor.a）
indirect  = lerp(1影の色, shadow2nd, lns.y)
```

1 影の境界 b1（`_ShadowBorder`）より暗い側のうち、2 影の境界 b2（`_Shadow2ndBorder`）より暗い部分が 2 影の色になる。MToon の影色は 1 色なので、1 影の範囲に占める 2 影の範囲の割合を重みにして、1 影の色と 2 影の色を線形の色空間で混ぜる。

```text
重み w   = clamp01(a2) × clamp01(clamp01(b2) / max(clamp01(b1), 0.0001))
影色     = lerp(1影の色, 2影の色, w)   （線形の色空間で。アルファは 1 影の色のまま）
```

混ぜた影色を従来の `ShadeColor`（主色との掛け合わせと `_ShadowStrength`）に渡す。2 影の色は 1 影の色（`_ShadowColor`）と同じ扱いにし、2 影の画像（`_Shadow2ndColorTex`）と 3 影は見ない。

### 変わらないことの保証

- 色調補正: 元の値が既定（明るさ係数 1、明るさ 0、区画が無効、色相 0 など）なら z は 1 のままで、`_MainTexHSVG` に何も書かない。公式 lilToon の既定値（0, 1, 1, 1）のまま残り、焼き込みの判定（`NeedsBake`）も動かないので、書き出す画像は従来と同じになる。lilToon の材質と lilToon の派生版は `FromGeneric` を通らないので、この処理の対象外。
- 焼き込み用シェーダーの構造の確認（`LilToonStructureCheck.Check`）: 書き出す前の元のアバターで動くので、置き換える前の材質しか見ていなかった。明るさを写した置き換え材質は焼き込みに乗るので、置き換えが要る材質は置き換えを試して `NeedsBake` を数える（試した材質はすぐ消す）。焼き込みが要らない材質だけのアバターでは従来どおり確認しない。
- 乗算のマットキャップ: `_UseMatCap` が 0、または `_MatCapBlendMode` が 3 以外なら従来と同じ。
- 2 影: `_Shadow2ndColor` のアルファが 0、2 影の境界が 0、または `_Shadow2ndColor` を持たない材質では重みが 0 になり、1 影の色をそのまま返す（変換もしない）。

## 確認したこと

- EditMode テスト（`UnsupportedShaderSubstitutionTests`）で、明るさ係数が z になって焼き込みの対象になること、色調補正の区画の明るさ（-0.25 と 0.5）、区画が無効なら無視すること、既定値なら `_MainTexHSVG` が公式の既定値のままで焼き込みも起きないこと、写さない 7 種類の補正（彩度・色相・グラデーション・マスク付きの明るさ・ガンマ・明るさ補正・ラジアンの色相）が報告されること、警告に材質名が出ること、lilToon の材質の `_MainTexHSVG` を変えないことを確かめるテストを足した。
- EditMode テスト（`MaterialFallbackTests`）で、乗算のマットキャップを空にし、加算は残すこと、2 影のアルファが 0 なら従来とまったく同じ影色になること、アルファ 1・境界 0.25/0.5 で期待どおりに混ざること、影が無効なら主色を返すことを確かめるテストを足した。
- `Tools/run-behavior-tests.ps1` の照明の検査に、2 影の重みの計算（アルファ 0・境界 0・半分の範囲・アルファの倍率・2 影の境界が 1 影より広い場合）を足した。
- 手元のアバター群の材質を集計した。非対応シェーダーの材質のうち、明るさ係数を持つものは z で写せ、色調補正の区画を持つものは区画が無効だった。2 影は影を使う lilToon の材質の約 7 割で不透明（アルファ 1）、乗算のマットキャップは数件だった。

## 検討した代替案と不採用理由

- 色相・彩度も `_MainTexHSVG` の x・y に近似して入れる: 色相の回転や輝度の補間は HSV の加算・倍率と色によって結果が大きく違い、元より派手な色になることがある。写さずに報告するほうが利用者が判断できる。
- 色調補正を書き出し側で主画像に直接焼き込む（元のシェーダーの式で）: シェーダーごとの式を持つことになり、2026-10-18 の「シェーダーごとの個別変換は書かない」と矛盾する。既存の lilToon の焼き込みに乗せられる範囲に留めた。
- 乗算のマットキャップを、MToon の主色を暗くして近似する: マットキャップは視線の向きで変わるので、主色に一律に掛けると正面以外で暗くなりすぎる。外して従来の主色のままにした。
- 2 影の色を、範囲の割合を見ずにアルファだけで混ぜる: 2 影の境界は狭いことが多く（既定 0.15）、影全体が 2 影の色になって暗くなりすぎる。
