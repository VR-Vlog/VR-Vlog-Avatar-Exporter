# 色を描かない材質（ColorMask 0）を描かないまま書き出す

- 日付: 2026-10-22
- 対象: `Editor/UnsupportedShaderSubstitution.cs`（`FromGeneric`・`DrawsNoColor`・`ShaderDrawsNoColor`・`ColorPasses`）・`Editor/LilToonMaterialReader.cs`（`DrawsNoColor`）・`Editor/UniVrmOneClickExporter.cs`（`CreateMToonFallback`・`FallbackColor`）・`Tests/Editor/MaterialFallbackTests.cs`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/StencilWriterProbe.shader`

## 背景

100 体の無料アバターで見え方を比べたところ、1 体で体の上に不透明な白い殻がかぶって書き出されていた。

## 原因

- その殻は、髪の上から顔を透かすためにステンシルだけを書く材質だった。シェーダーは SubShader に `ColorMask 0` と `ZWrite Off` を置き、唯一のパスがそれを引き継ぐので、元のアバターでは何も描かれない。
- lilToon 以外のシェーダーを lilToon へ置き換えるとき、`ColorMask` を見ていなかった。置き換え先は lilToon の既定（`_ColorMask` 15＝RGBA、`_ZWrite` 1）の不透明になり、白で描かれていた。
- 標準 MToon の互換表示は `ColorMask` もステンシルも持たないので、lilToon の `_ColorMask` が 0 の材質も描いてしまう。

## 決定

シェーダーの定義と材質の値から読めることだけで決める。アバター・材質・シェーダーの名前では分けない。

- 置き換え: 元のシェーダーのファイル（`.shader`）の最初の SubShader を読み、色を描くパスがすべて `ColorMask 0` なら、置き換え先の lilToon の `_ColorMask` を 0 にし、`_ZWrite` を元のシェーダーに合わせる（判定は半透明の深度と同じ `WritesDepth`）。
  - パスの `ColorMask`、なければ SubShader の `ColorMask`、どちらもなければ Unity の既定（RGBA）で決める。`ColorMask [_Prop]` はその材質の値で決め、値がなければ 15 とみなす。
  - 影（ShadowCaster）・Meta・深度だけのパスは色を描かないので見ない。色を描くパスが 1 つもないシェーダーも色を描かないものとする。
  - `UsePass` で別のシェーダーのパスを借りる SubShader は、借りた先を読めないので色を描くものとする。
  - Pass のないサーフェスシェーダーは SubShader の `ColorMask` だけで決める。
  - 読み方（最初の SubShader・コメントの除去・パスと SubShader の分け方）は深度の判定（`ShaderWritesDepth`）と共通の `ColorPasses` にまとめた。
- MToon の互換表示: lilToon の `_ColorMask` が 0 の材質は、FakeShadow と同じく、材質の枠は残して色のアルファを 0 にし、深度を書かない半透明にする（アウトラインも出さない）。lilToon の拡張には元の値をそのまま渡すので、lilToon で描くときは `_ColorMask` 0 のとおり何も描かれない。

## 成り立たない場合

- `#include` した別ファイルやマクロの中で `ColorMask` を切り替えるシェーダーは、ファイル本体に書かれた命令だけで判定する。
- 色を描かずに深度だけを書く材質（深度マスク）は、lilToon では元のとおり深度を書くが、MToon の互換表示では深度も書かないので、その後ろの面が見える。
- lilToon の `_ColorMask` が 0 でも、アウトラインのパスが色を描く材質では、MToon の互換表示からアウトラインも消える。
- ステンシルを読んで色を描く材質（ステンシルが一致した所だけ乗算・加算で描くもの）はこの変更の対象外で、従来どおり置き換えて描く。

## 検証

- EditMode: `UnsupportedShaderSubstitutionTests.AStencilWriterStaysWithoutColorAndDepth`・`TheFirstSubShaderDecidesWhetherAnyPassDrawsColor`（15 通り。サーフェスシェーダーが自前の影パスを持つ場合を含む）・`TheFirstSubShaderDecidesWhetherColorPassesWriteDepth`（共通化後も同じ 11 通り）、`MaterialFallbackTests.ColorMaskZeroFallbackIsInvisible`（`_ColorMask` 0 と 15）。
- 白い殻が出ていたアバターと数体で、書き出した VRM の該当材質が描かれないことを確かめた。
