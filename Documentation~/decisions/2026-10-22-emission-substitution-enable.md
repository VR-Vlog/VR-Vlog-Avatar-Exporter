# lilToon 以外のシェーダーの発光を、そのシェーダーのスイッチと画像で決める

- 日付: 2026-10-22
- 対象: `Editor/UnsupportedShaderSubstitution.cs`（`ApplyEmission`）・`Editor/LilToonEmissionPolicy.cs`（`GradientFactor`）・`Editor/TextureColors.cs`・`Editor/UniVrmOneClickExporter.cs`（`FallbackColor`）・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/MaterialFallbackTests.cs`・`Tests/Editor/EmissionSwitchProbe.shader`・`Tests/Editor/EmissionNoSwitchProbe.shader`

## 背景

100 体の無料アバターで見え方を比べたところ、5 体で体や服の全体が白や灰色に光り、元の色が分からなくなっていた。4 体は lilToon 以外のシェーダーを lilToon へ置き換えたもの（`UnsupportedShaderSubstitution`）、1 体は lilToon の発光にグラデーションを使ったもの。

## 原因

- 発光のオン・オフを `_ES_Enable`（キーワード `_ES_ENABLE`）で切り替えるシェーダーがある。この名前を知らなかったので、スイッチがないものとして扱い、発光色（既定値の白）があるだけで発光をオンにしていた。発光していない材質まで白く光った。
- 発光のマスク画像を `_Emission` という名前で持つシェーダーがある。この名前を知らなかったので、マスクなしで発光色（灰色）を全面に足していた。
- 発光に画像を使わず、基本色を掛ける設定（`_EmissionBaseColorAsMap`）を読んでいなかったので、白い発光を全面に足していた。
- lilToon 2.3.4 は、`_EmissionUseGrad` がオンのとき発光色に `_EmissionGradTex` の (時間 × `_EmissionGradSpeed`, 0.5) の色を掛ける。MToon の互換表示にはこれがなく、発光色をそのまま一定の発光にしていた。

## 決定

Unity とシェーダーの定義から読めることだけで決める。アバターや材質の名前では分けない。

- 発光のスイッチに `_ES_Enable`、発光のキーワードに `_ES_ENABLE` を加える（`_EMISSION` と同じ扱い）。スイッチがあればスイッチで、なければ宣言されたキーワードで決める。
- 発光の画像の名前に `_Emission` を加える。テクスチャとして宣言されたものだけを読むので、同じ名前の数値とは取り違えない。
- スイッチもキーワードもないシェーダーでは、発光の画像があるか、発光色がシェーダーの既定値から変えてあるときだけ発光をオンにする。既定値の色だけでは作者が光らせたとは言えない。シェーダーが見つからない材質は既定値が分からないので、これまでどおり色があればオンにする。
- `_EmissionBaseColorAsMap` がオンなら、置き換え先の lilToon の `_EmissionMainStrength` を 1 にする。lilToon はこれで発光に基本色（画像×色）を掛け、MToon の互換表示では既存の `EmissionMaskBaker` が基本色を発光の画像へ焼き込む。
- lilToon の `_EmissionUseGrad` がオンのとき、MToon の互換表示の発光色に、グラデーション画像の v = 0.5 の行の平均（アルファで重み付け、リニア）を掛ける。時間で一周する間の平均の明るさになる。`_EmissionGradSpeed` が 0 のときは、時間 0 で読む位置（最初と最後の画素の間）の値を使う。画像は GPU で読むので、読み取り不可の画像でも使える（`TextureColors`）。lilToon 拡張に書く値は変えない。

## 成り立たない場合

- ここに挙げた以外の名前のスイッチ・画像を使うシェーダーは、これまでどおりの判定になる。
- 発光色を既定値のまま、スイッチなしで常に光らせるシェーダーは、画像がなければ光らなくなる。発光色の既定値は黒のものが多く、白などを既定値にして常に全面を光らせるシェーダーは見当たらない。
- グラデーションの色の変化（点滅や色の移り変わり）は再現しない。平均の明るさの一定の発光になる。AudioLink でグラデーションを動かす設定は、書き出しの前に AudioLink を切っているので時間の変化だけで計算する。

## 検証

- EditMode: `UnsupportedShaderSubstitutionTests` の `AShaderSwitchDecidesTheEmissionEvenWithAWhiteColor`・`WithoutASwitchTheShaderDefaultColorAloneDoesNotGlow`・`AnEmissionImageNamedEmissionMasksTheColor`・`EmissionTintedByTheBaseColorUsesTheAlbedo`、`MaterialFallbackTests.GradatedEmissionUsesTheAverageOfTheGradation`。
- 100 体の見え方の比較（元のアバター／修正前／修正後、正面・45°・背面）で、白や灰色に光っていたアバターが元の色に近づき、ほかのアバターは悪化しないことを確かめた。
