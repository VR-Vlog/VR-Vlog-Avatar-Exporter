# 区画を切り替えるトゥーンシェーダーのマスクを、元のシェーダーが読むチャンネルで焼き込む

- 日付: 2026-10-23
- 対象: `Editor/UnsupportedShaderSubstitution.Toon.cs`（`CopyMask`・`BakeMask`）・`Editor/UnsupportedShaderSubstitution.cs`（`Create`・`Apply`）・`Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/SectionedToonSubstitutionTests.cs`・`Tests/Editor/SectionedToonProbe.shader`・`Documentation~/decisions/2026-10-22-sectioned-toon-mapping.md`・`CHANGELOG.md`

## 背景

2026-10-22 の `2026-10-22-sectioned-toon-mapping.md` では、マットキャップ 1・2、リム、発光のマスクは、R チャンネルを選んだ反転していない灰色で不透明なものだけを写し、それ以外は写さずに報告していた。効果そのものは有効のまま残したので、マスクが無い状態で材質全体に掛かった。置換のマットキャップでは材質全体がマットキャップの色で塗られ、実際の書き出しでも服や顔の一部だけに付けていたマットキャップが全体を黄色や銀色にしていた。

元のシェーダーはマスクの R・G・B・A のどれか 1 チャンネルを読み（`*MaskChannel` が 0〜3）、`*MaskInvert` で反転する。lilToon はマットキャップのマスクを RGB、リムと発光のマスクを RGBA で掛けるので、元の画像をそのまま渡すと色や透明が効果に混ざる。

## 決定

- R チャンネルを選んだ反転していない灰色で不透明なマスクは、これまでどおり同じ画像をそのまま写す。グラフィックスデバイスが無くて灰色か確かめられないときも写す。
- それ以外のマスクは、元のシェーダーが読むチャンネルの値（反転していれば 1 から引いた値）を R・G・B に入れ、A を 1 にした画像を GPU で読み戻して作り、元のマスクの拡大縮小とずらしを付けて写す。大きさは元の画像と同じで、長い辺が 2048 を超えるときだけ縮める。画像は元のマスクと同じ色空間（sRGB かリニアか）で持ち、シェーダーが読む値が元と同じになるようにする。名前は元のマスクの名前の後ろに `__VRVlog_Mask` とチャンネル名（反転していれば `_Inverted`）を付ける。
- 作った画像は書き出しの一時画像の一覧（主画像の焼き込みと同じ `temporaryTextures`）に入れ、書き出しが終わったら書き出し用のコピーと一緒に破棄する。`Create` に一覧を渡さない呼び出し（lilToon の構造の確認）では焼き込まない。
- 焼き込めないとき（グラフィックスデバイスが無い、読み戻しに失敗した、一覧が無い）は、効果を切って（`_UseMatCap`・`_UseMatCap2nd`・`_UseRim`・`_UseEmission` を 0）、これまでと同じ名前（Matcap Mask・Matcap 2 Mask・Rim Mask・Emission Mask）で報告する。材質全体に掛かるより、効果が無いほうが元の見た目に近い。

## 確認

- `SectionedToonSubstitutionTests.AGreyRedChannelMaskIsCopied` で、灰色で不透明な R チャンネルのマスクが同じ画像のまま写ることを確かめる。
- `SectionedToonSubstitutionTests.AColoredRedChannelMaskBecomesAGreyMaskInsteadOfTintingTheRim` で、色のある R チャンネルのマスクが R の値の灰色の画像になり、リムが有効のまま報告されないことを確かめる。
- `SectionedToonSubstitutionTests.AnInvertedGreenChannelMaskKeepsAReplacingMatcapToItsArea` で、置換のマットキャップの G チャンネルを反転したマスクが、各画素 1 − G の灰色で不透明な新しい画像になり、拡大縮小とずらしが残り、報告されないことを確かめる。

## 欠点

- マスクと同じ大きさの画像を材質ごとに 1 枚ずつ作るので、同じマスクを使う材質が多いと書き出すファイルが大きくなる。
- 焼き込んだ画像にはミップマップを作らない。
