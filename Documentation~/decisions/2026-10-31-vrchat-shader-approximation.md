# VRChat SDK に入っているシェーダーを、シェーダーごとの表で公式 lilToon へ写す

- 日付: 2026-10-31
- 対象: `Editor/UnsupportedShaderSubstitution.VRChat.cs`・`Editor/UnsupportedShaderSubstitution.cs`・`Editor/UnsupportedShaderSubstitution.Toon.cs`・`Editor/Locales/*.json`・`Tests/Editor/VRChatShaderSubstitutionTests.cs`・`Tests/Editor/VRChatToonStandardProbe.shader`・`Tests/Editor/VRChatMobileProbe.shader`

## 背景

VRChat SDK（3.10）には 19 個のシェーダーが入っている。これまでは、どれも非対応シェーダーの汎用の置き換え（`2026-10-18-unsupported-shader-fallback.md`）を通っていた。汎用の置き換えが写すのは主画像・色・法線・発光・カリングだけで、影を付けない。名前に `Unlit` が無ければライトを受ける材質にし、描画順が 2500 を超えれば半透明にしていた。そのため次のことが起きていた。

- Toon Standard の影のランプ・スペキュラー・マットキャップ・リム・ディテール・アウトライン・色相・カラーマスクがすべて消える。
- Diffuse・Bumped・Standard Lite も影が付かず、のっぺりした見た目になる。
- 加算のパーティクルが普通の半透明になり、背景に色が重なって暗く濁る。Sprites・UI はライトを受けない形に、Lightmapped はライトマップの代わりに普通のライトを受ける形になっていなかった。

## 決定

- VRChat SDK のシェーダーは、シェーダー名の完全一致で表（`VRChatShaders`）の 1 行を引く。1 行に書くのは光の受け方（Lambert／Flat／Unlit）、ブレンド（不透明／アルファ合成／加算／乗算）、両面描画、アウトラインの有無、機能ごとの写し方の 4 つで、シェーダーごとの特例は書かない。表に無い名前は今までどおり汎用の置き換えを通る。
- 判定の順は「lilToon 派生 → VRChat の表 → 他のトゥーンシェーダー → 汎用」。
- 光の受け方は 3 種類にまとめる。
  - Lambert（`albedo × (ライト × N·L + 環境光)`）: lilToon の影を `_ShadowBorder` 0.75・`_ShadowBlur` 0.5 にする。lilToon は境界を `N·L × 0.5 + 0.5` の軸で測るので、この値で `saturate(N·L)` と同じ明るさの変化になる。影の色は黒にし、`_ShadowEnvStrength` 1 で影側に環境光だけを残す。
  - Flat: 影を付けない。
  - Unlit: `_AsUnlit` を 1 にする。
- ブレンドは lilToon の半透明の premultiplied 出力に合わせる。
  - 加算: `_SrcBlend`／`_DstBlend` を One／One にし、ライトごとの加算パスは Zero／One で何も足さない。
  - 乗算: `2026-10-31-multiply-shader-to-liltoon-multiply.md` の設定を使う。
- 色相とカラーマスクは lilToon に同じ式が無いので、結果を新しい主画像（と発光画像）へ焼き込み、`_Color` を白にする。焼き込みに使う画像のタイリングとオフセットがすべて同じなら画像の空間で焼いてその値を残し、UV が 0〜1 を越えるメッシュでも元と同じ周期で繰り返す。違うときはメッシュの UV の 0〜1 の範囲で焼く。焼いた画像は最大 2048 px にする。
- 写せない効果は、材質名とその効果名を警告「元のシェーダーの効果のうち公式lilToonに無いものは書き出していません」に出す。表で写した材質は、新しい警告「VRChatの公式シェーダーの設定を公式lilToonの同じ役割の項目へ写しました」に並べる。文言は、他のトゥーンシェーダー用の既存の警告と同じ形にそろえた。
- アプリ（VR Vlog）は lilToon の値をそのまま読むので、アプリ側の変更はいらない。かんたん転送も同じ置き換えを通るので、通常の書き出しと中身は変わらない。

### シェーダーごとの表

| シェーダー | これまで | 今回 |
|---|---|---|
| VRChat/Mobile/Toon Standard | 汎用（影なし） | Lambert・不透明＋Toon Standard の写し方 |
| VRChat/Mobile/Toon Standard (Outline) | 汎用（影なし・アウトラインなし） | 同上＋アウトライン |
| VRChat/Mobile/Standard Lite | 汎用（影なし） | Lambert・不透明＋GGX スペキュラーと反射 |
| VRChat/Mobile/Diffuse | 汎用（影なし） | Lambert・不透明 |
| VRChat/Mobile/Bumped Diffuse | 汎用（影なし） | Lambert・不透明（法線は汎用の部分で写す） |
| VRChat/Mobile/Bumped Mapped Specular | 汎用（影なし・ハイライトなし） | Lambert・不透明＋Blinn-Phong のハイライト |
| VRChat/Mobile/Toon Lit | 汎用 | Flat・不透明 |
| VRChat/Mobile/MatCap Lit | 汎用（マットキャップなし） | Flat・不透明＋乗算のマットキャップ |
| VRChat/Mobile/Lightmapped | 汎用（ライトを受ける） | Unlit・不透明（アバターにライトマップは無い） |
| VRChat/Mobile/Particles/Additive | 汎用（アルファ合成） | Unlit・加算・両面 |
| VRChat/Mobile/Particles/Alpha Blended | 汎用（ライトを受ける） | Unlit・アルファ合成・両面 |
| VRChat/Mobile/Particles/Multiply | 汎用（乗算） | Unlit・乗算・両面 |
| VRChat/Mobile/Worlds/Supersampled UI | 汎用（ライトを受ける） | Unlit・アルファ合成・両面 |
| VRChat/Sprites/Default | 汎用（ライトを受ける） | Unlit・アルファ合成・両面 |
| VRChat/Sprites/Diffuse | 汎用（影なし） | Lambert・アルファ合成・両面 |
| Video/RealtimeEmissiveGamma | 汎用（主画像を色として描く） | Flat・黒の地＋主画像を発光 |
| VRChat/Panosphere | 汎用 | Unlit・不透明。パノラマの投影は写せない |
| VRChat/Mobile/Skybox | 汎用 | Unlit・不透明・両面。キューブマップは写せない |
| FX/MirrorReflection | 汎用 | Unlit・不透明。鏡の映り込みは写せない |

### プロパティごとの写し方

全シェーダー共通で、汎用の部分が `_MainTex`・`_Color`・`_BumpMap`・`_BumpScale`・発光（`_EmissionMap`・`_EmissionColor`）・`_Cull` を写す。下の表は、それに足すもの。

| シェーダー | プロパティ | 写し先 | 写さない理由 |
|---|---|---|---|
| Toon Standard | `_Ramp`・`_ShadowBoost`・`_ShadowAlbedo` | `_ShadowColor`（`_ShadowAlbedo`）・`_ShadowMainStrength` 1・`_ShadowStrength`・境界とぼかしをランプの明るさに合わせる | ランプの色は灰色として読む（lilToon の影は 1 色） |
| | `_LimitBrightness`・`_MinBrightness` | `_LightMaxLimit`（1 か 10）・`_LightMinLimit` | |
| | `USE_SPECULAR`・`_MetallicStrength`・`_GlossStrength`・`_Reflectance`・`_MetallicMap`・`_GlossMap` | `_UseReflection`・`_Metallic`（ガンマへ変換）・`_Smoothness`・`_Reflectance`（0.16 × r² をガンマへ変換。lilToon は `[Gamma]`）・`_MetallicGlossMap`・`_SmoothnessTex` | `_SpecularSharpness` は lilToon に同じ役割の項目が無い |
| | `USE_MATCAP`・`_Matcap`・`_MatcapStrength`・`_MatcapType`・`_MatcapMask` | `_UseMatCap`・`_MatCapTex`・`_MatCapBlend`・`_MatCapBlendMode`（加算 1／乗算 3）・`_MatCapBlendMask`・`_MatCapZRotCancel` 1 | |
| | `USE_RIMLIGHT`・`_RimColor`・`_RimIntensity`・`_RimRange`・`_RimSharpness`・`_RimAlbedoTint`・`_RimEnvironmental` | `_UseRim`・`_RimColor`（強さを掛ける）・`_RimBorder`（1 − range）・`_RimBlur`（2 × sharpness）・`_RimMainStrength`・`_RimEnableLighting` | |
| | `USE_DETAIL_MAPS`・`_DetailAlbedoMap`・`_DetailMode`・`_DetailMask`・`_DetailNormalMap`・`_DetailUV` | `_Main2ndTex`（通常／加算／乗算）・`_Main2ndBlendMask`・`_Bump2ndMap`・`_Bump2ndScaleMask`・UV 0／1 | `_DetailMode` の MultiplyX2 と `_DetailHueShift` は lilToon に無い |
| | `USE_HUE_SHIFT`・`_HueShift`・`_HueShiftMask`・`_EmissionHueShift` | 主画像と発光画像へ焼き込む | 発光画像が UV1 のときはマスクと同じ UV で焼けないので写さない |
| | `USE_COLOR_MASK`・`_ColorMask`・`_ColorMaskColor1〜4`・`_ColorMaskBlendMode`・`_ColorMaskEmissionStrength1〜4` | 主画像と発光画像へ焼き込む | 同上 |
| | `_OutlineThickness`・`_OutlineColor`・`_OutlineFromAlbedo`・`_OutlineMask` | `_OutlineWidth`・`_OutlineColor`・`_OutlineWidthMask`・ライトを受けない。`_OutlineFromAlbedo` が 0 と 1 の間なら、輪郭線の色と主画像を画素ごとに混ぜた画像を `_OutlineTex` に焼き、1 なら主画像をそのまま使う | |
| | `_VertexColor`・`USE_OCCLUSION_MAP` | なし | 頂点カラーを掛ける項目とオクルージョンが lilToon に無い（警告に出す） |
| Standard Lite | `_Metallic`・`_Glossiness`・`_MetallicGlossMap` | `_Metallic`・`_Smoothness`・`_MetallicGlossMap`・`_SmoothnessTex`（A を焼き出す） | |
| | `_SPECULARHIGHLIGHTS_OFF`・`_GLOSSYREFLECTIONS_OFF` | `_ApplySpecular`・`_ApplyReflection` | SDK の既定はどちらもオフ（ToggleOff の 0）なので、新しい材質はハイライトも反射も出ない。キーワードに従う |
| | `_DETAIL`・`_DetailNormalMap`・`_DetailMask`・`_UVSec` | `_Bump2ndMap`・`_Bump2ndScaleMask`・UV 0／1 | `_DetailAlbedoMap`（×2）と `_OcclusionMap` は lilToon に無い |
| | 頂点カラー・`_MONOSH_*`・スペキュラーの AA | なし | lilToon に同じ役割の項目が無い |
| Bumped Mapped Specular | `_Shininess`・`_SpecColor`・主画像の A（光沢） | `_Smoothness`（1 − √(1/n)、n = shininess × 128）・トゥーンのスペキュラー（境界 0.5・ぼかし 1）・`_ReflectionColor`・`_ReflectionColorTex` | |
| MatCap Lit | `_MatCap` | 乗算のマットキャップ（ライトを受ける） | |
| Video | `_MainTex`・`_Emission`・`_ApplyGamma` | 地を黒・`_EmissionMap`・`_EmissionColor`（`_Emission` をガンマへ） | `_ApplyGamma` の変換は lilToon に無い |
| Particles・Sprites・UI | `_TintColor` など | 汎用の部分で写す | 頂点カラー（パーティクルの色）は lilToon に掛ける項目が無い |

## 確認したこと

- EditMode テスト（`VRChatShaderSubstitutionTests`・26 本）で、SDK の全シェーダー名に表の行があること、表に無い名前が汎用の置き換えのままであること、光の受け方とブレンドの 3 種類ずつ、Toon Standard の各機能（ランプ・スペキュラー・マットキャップ・リム・ディテール・アウトライン・色相とカラーマスクの焼き込み・カラーマスクの発光・頂点カラーとオクルージョンの警告）、Standard Lite・Blinn-Phong・MatCap Lit・Video・投影系の写し方を確かめた。Linear の Unity 2022.3 で、関係するテスト 158 本がすべて通る。
- 球と板に SDK のシェーダーを付けて、元の描画と、書き出して読み込み直し lilToon のプレビューで描いたものを 3 倍の解像度で並べた。比較画像はリポジトリには入れていない。元との平均の差（0〜255）は次のとおり。

| 例 | これまで | 今回 |
|---|---|---|
| Toon Standard（ランプ） | 4.8 | 2.8 |
| Toon Standard（スペキュラー・リム） | 15.3 | 3.0 |
| Toon Standard（マットキャップ・色相・カラーマスク） | 39.3 | 1.5 |
| Toon Standard（アウトライン） | 5.1 | 2.8 |
| Toon Standard（アウトラインの色を主画像と半々に混ぜる） | 5.1 | 2.8 |
| Toon Standard（明るさが一定のランプ） | 11.9 | 0.5 |
| Standard Lite | 14.8 | 15.6 |
| Diffuse／Bumped Diffuse／Bumped Mapped Specular | 19.3〜19.5 | 12.6 |
| Toon Lit | 5.2 | 5.2 |
| MatCap Lit | 28.8 | 2.9 |
| Lightmapped | 3.6 | 0.0 |
| Particles（加算／アルファ合成／乗算） | 10.2／1.3／0.0 | 0.0／0.1／0.0 |
| Supersampled UI／Sprites Default／Sprites Diffuse | 1.3／1.3／7.1 | 0.1／0.0／2.0 |
| Video | 4.6 | 1.0 |
| Panosphere／Mirror | 5.7／3.6 | 5.1／0.0 |

- この比較は Unity の組み込みの描画で撮ったので、ライトごとの加算パス（ForwardAdd）が唯一のライトをもう一度描く。アプリの描画（ライトをまとめて 1 パスで描く）には無い描き方なので、元と書き出しの両方でこのパスを切って撮った。

## 分かっている制限

- Diffuse・Bumped・Standard Lite は、元より明るい面が暗い。元は直接光に環境光を足して 1 を超えるが、lilToon は明るい面で直接光と環境光を足さない（`_LightMaxLimit` を上げても変わらないことを確かめた）。影の付く向きは合っている。Standard Lite の平均の差がこれまでとほぼ同じなのはこのためで、これまでは影が付かないぶん全体が明るく、明るさだけは元に近かった。
- Toon Standard のランプの色は灰色として読む。色の付いたランプは、影の色が 1 色になる。
- 頂点カラーを掛けるシェーダー（パーティクル・Sprites・Standard Lite・Toon Standard の Apply Vertex Color）は、頂点カラーを掛けない。
- Panosphere の投影・Skybox のキューブマップ・鏡の映り込みは写せないので、主画像をそのまま貼る。警告に効果名を出す。

## 検討した代替案と不採用理由

- 汎用の置き換えのなかで、シェーダー名ごとに分岐を足す: 機能が増えるたびに特例が増え、どのシェーダーに何が効くかが読めなくなる。行ごとに光の受け方とブレンドを持つ表にすれば、新しいシェーダーも 1 行で足せる。
- 色相とカラーマスクを lilToon の色調補正（`_MainTexHSVG`）で近似する: Toon Standard の色相はマスク付きで、カラーマスクは 4 色の置き換えなので、lilToon の色調補正の式では表せない。画像へ焼き込めば、マスクも含めて元と同じ色になる。
- Lambert の材質で、環境光のぶん影の境界を明るい側へずらす: 環境光の強さは場面ごとに違うので、決めた値で境界をずらすと、別の場面で影が薄くなりすぎる。
