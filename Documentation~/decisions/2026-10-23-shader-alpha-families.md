# lilToon以外のトゥーンシェーダーの透過を、シェーダーごとの式で公式lilToonへ写す

- 日付: 2026-10-23
- 対象: `Editor/UnsupportedShaderSubstitution.cs`・`Editor/UnsupportedShaderSubstitution.Alpha.cs`・`Editor/UnsupportedShaderSubstitution.Toon.cs`・`Tests/Editor/ShaderAlphaFamilyTests.cs`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`

## 背景

アプリのARとかんたん転送で、顔・帽子・服は見えるのに髪と肌の出た体だけが消えるという報告があった。アプリはアバターを透明でクリアした画像に描き、そのアルファで実写に重ねるので、書き出した材質のアルファが0になった所は消える。

lilToon以外のシェーダーを置き換えるとき、透過は「主画像のアルファ×色のアルファを、1つのしきい値で切り抜くか半透明にする」として写していた。VRChatのアバターでよく使われるトゥーンシェーダーには、これと違う式で透過を決めるものがあり、シェーダーのソースを読むと次の3つの経路でアルファが0になっていた。

1. 切り抜きマスクに値を足して0.5で切り抜くシェーダー（以下、レベル型）は、切り抜きにマスク画像（`_ClippingMask`）の赤と値（`_Clipping_Level`）を使い、基本色のアルファは使わない。置き換えはマスクと値を読まず、隠しプロパティの `_Cutoff`（0.5）で主画像のアルファ×基本色のアルファを切り抜いていた。基本色のアルファが0の材質は全部消え、マスクで切る髪は切り抜かれずに板になる。`_Cutoff` を持たない材質では値をしきい値に入れていたので、意味が逆（値を上げるほど消える）になる。
2. 生きているシェーダーでも材質のキーワードをそのまま読んでいたので、以前のシェーダー（Standardの半透明など）で付いた `_ALPHABLEND_ON` が残った材質を半透明と判定していた。主画像のアルファを使っていない髪・肌はアルファが不定になる。
3. カリングを `_Culling` で持つシェーダーと、両面描画をジオメトリシェーダーで行い `_UseDoubleSided` で切り替えるシェーダーの両面を読まず、片面になっていた（髪の裏側が消える）。

報告の材質は手元に無いので、どれに当たったかは確かめられていない。無料のアバター108体を書き出して調べた範囲では再現しなかった。

## 決定

シェーダーの系統をプロパティ名で見分け（材質に保存された値でも見分けるので、シェーダーが入っていない材質にも効く）、系統ごとの透過の式をlilToonのアルファマスク（`_AlphaMaskMode`・`_AlphaMask`・`_AlphaMaskScale`・`_AlphaMaskValue`）で組み直す。書き出しはアルファマスクを主画像のアルファに焼き込むので、アプリはマスクを読まない。lilToonのマスクは m = saturate(マスクの赤 × Scale + Value) で、モード1は a = m、2は a × m、3は a + m、4は a − m。

| 系統 | 見分け方 | 写し方 |
| --- | --- | --- |
| レベル型（旧版） | `_Clipping_Level` があり `_ClippingMode` が無い | 種類はシェーダー名（Transparentと、ToonColor・Mobile以外のTransClippingは半透明。Clippingと、ToonColor・MobileのTransClippingは切り抜き。シェーダーが無い材質は `_Clipping_Level` を持つのが切り抜きの版だけなので、描画順が半透明でなければ切り抜き）。マスクを `_ClippingMask`（無ければ白）にして a = m（反転は Scale −1・Value 1）、Valueに値を足し、しきい値0.5。マスクのタイリングは主画像からの相対に直す。主画像のアルファを使う設定は、反転なしならモード3・4で値だけ足し、反転ありなら反転したアルファを焼く。半透明は値の代わりに `_Tweak_transparency` を足し、しきい値を 0.5 − 値 + Tweak にする。基本色のアルファは1にする |
| レベル型（新版） | `_Clipping_Level` と `_ClippingMode` がある | 種類は `_ClippingMode`（2で半透明）と `_TransparentEnabled`。写し方は旧版と同じで、マスクは主画像と同じ座標。深度は `_ZWriteMode` を読む（`_ZWrite` は色のパスで使われない） |
| アルファの元を選ぶ型 | `_AL_Source` | 0は主画像×色、1はマスクの赤、2はマスクのアルファ（焼く）を a にする。`_AL_InvMaskVal` で反転。半透明は `_AL_Power` を掛ける |
| アルファ倍率と輝度マスクの型 | `_AlphaMaskStrength` と `_Culling` | 色のアルファに `_Alpha` を掛ける（1を超える分は、切り抜きならしきい値を割り、半透明なら1で止める）。マスクの輝度を lerp(1, 輝度, 強さ) で掛ける（モード2・Scale 強さ・Value 1 − 強さ。色付きのマスクは輝度を焼く）。カリングは `_Culling` |
| ジオメトリシェーダーで両面を描く型 | `_CutoutCutoutAdjust`、または `_UseDoubleSided` と `_DoubleSidedFlipBackfaceNormal` | しきい値は `_CutoutCutoutAdjust`。`_UseDoubleSided` で Cull Off。半透明は `_AlphaMask` の赤を掛ける（自分のタイリングを主画像からの相対に直す） |
| VRM 0.x の MToon | `_BlendMode` と `_ShadeToony` | `_BlendMode` 0〜3で不透明・切り抜き・半透明・深度を書く半透明 |

ほかに次を直した。

- しきい値の名前に `_CutoutCutoutAdjust`・`_CutoutThreshold`・`_MaskClipValue`（ノードエディターで作られたシェーダーの名前）を足した。
- 生きているシェーダーでは、描くサブシェーダー（`ShaderUtil.GetShaderData(...).ActiveSubshader`）のパスが使うキーワードだけを読む（`ShaderUtil.PassHasKeyword`）。`Shader.keywordSpace` には `FallBack` 先のキーワードも入るので、`FallBack "Standard"` のシェーダーでは残った `_ALPHABLEND_ON` も宣言済みに見える。シェーダーが無い材質は今までどおり保存されたキーワードを読む。
- カリングは `_Cull`・`_CullMode`・`_Culling` の順に読む。アルファの元を選ぶ型の半透明で `_CullMode` が無いもの（裏面と表面を別のパスで描く）は Cull Off にする。

焼き込みに失敗したとき（GPUが無いなど）は「消える」側でなく「見える」側に倒す。反転した主画像のアルファが焼けないときは全部を描く。

## 確認

- `ShaderAlphaFamilyTests` を足した。各系統の見分け方と写し方を、その系統のプロパティだけを持つテスト用シェーダーで確かめる（切り抜き・半透明・反転・主画像のアルファ・マスクのタイリング・倍率・輝度の焼き込み・両面・深度・ノードエディターのしきい値名・残ったキーワード・FallBack 先だけが宣言するキーワード）。
- 各系統の本物のシェーダーで作った材質を、元のシェーダー・修正前・修正後で描き比べ、修正後は板になる・消える・裏面が欠けることが無くなり、切り抜きの形が元に近づくことを目で確かめた（画像はリポジトリに入れない）。色と濃さの差は残る。下の欠点にあるレベル型の半透明の境界と、倍率の1を超える分の切り捨ては、テストで式を確かめただけで、見た目が元と一致するかは確かめていない。
- `UnsupportedShaderSubstitutionTests.ToonPropertyNamesMapToShadowClippingAndCull` は、レベル型の値をしきい値として期待していたので、アルファマスクのValueとして期待するように直した。

## 欠点

- 系統はプロパティ名で見分けるので、同じ名前を別の意味で使うシェーダーは誤って写す。名前の組を2つ以上見る系統はそうしている。
- レベル型の半透明は、混ぜる量（m + Tweak）とクリップの境界（m + 値 ≥ 0.5）を1つのマスクで表せないので、混ぜる量に合わせ、境界はしきい値で近づける。
- 倍率型で `_Alpha` が1を超える半透明は、1を超える分を捨てる。
