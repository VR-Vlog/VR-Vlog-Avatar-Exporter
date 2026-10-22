# 単独では色を描かない材質（ColorMask 0・ステンシル待ち）を描かないまま書き出す

- 日付: 2026-10-22
- 対象: `Editor/UnsupportedShaderSubstitution.cs`（`FromGeneric`・`DrawsNoColor`・`ShaderDrawsNoColor`・`ColorPasses`）・`Editor/LilToonMaterialReader.cs`（`DrawsNoColor`）・`Editor/UniVrmOneClickExporter.cs`（`CreateMToonFallback`・`FallbackColor`）・`Tests/Editor/MaterialFallbackTests.cs`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/StencilWriterProbe.shader`

## 背景

100 体の無料アバターで見え方を比べたところ、1 体で体の上に不透明な白い殻がかぶって書き出されていた。

## 原因

- 殻になっていたのは、体の輪郭を壁越しに見せるための材質だった。3 つのパスのうち 2 つは `ColorMask 0` でステンシルを書いて消すだけで、色を描く 3 つ目のパスは `Stencil { Ref 121 Comp Equal }` なので、ステンシルが 121 の所にしか描かない。前の 2 つのパスが書いた値は 2 つ目で消すので、単独では何も描かれない。
- 同じアバターには、髪の上から顔を透かすためにステンシルだけを書く材質もある。SubShader に `ColorMask 0` と `ZWrite Off` を置き、唯一のパスがそれを引き継ぐ。
- lilToon 以外のシェーダーを lilToon へ置き換えるとき、`ColorMask` もステンシルも見ていなかった。置き換え先は lilToon の既定（`_ColorMask` 15＝RGBA、`_ZWrite` 1）の不透明になり、白で描かれていた。
- 標準 MToon の互換表示は `ColorMask` もステンシルも持たないので、lilToon の `_ColorMask` が 0 の材質も描いてしまう。

## 決定

シェーダーの定義と材質の値から読めることだけで決める。アバター・材質・シェーダーの名前では分けない。

- 置き換え: 元のシェーダーのファイル（`.shader`）の最初の SubShader を読み、色を描くパスがすべて「`ColorMask 0`」か「ほかの材質がステンシルを書いていない所では必ず不合格になるステンシルテスト」なら、置き換え先の lilToon の `_ColorMask` を 0 にし、`_ZWrite` を元のシェーダーに合わせる（判定は半透明の深度と同じ `WritesDepth`）。
  - パスの `ColorMask`、なければ SubShader の `ColorMask`、どちらもなければ Unity の既定（RGBA）で決める。`ColorMask [_Prop]` はその材質の値で決め、値がなければ 15 とみなす。
  - 影（ShadowCaster）・Meta・深度だけのパスは色を描かないので見ない。色を描くパスが 1 つもないシェーダーも色を描かないものとする。
  - `UsePass` で別のシェーダーのパスを借りる SubShader は、借りた先を読めないので色を描くものとする。
  - ステンシルはパスの `Stencil` ブロック、なければ SubShader のブロックで決める。ステンシルが 0 の状態から、パスを順に流して、ステンシルが取りうる値の集合を追う。`Ref`・`ReadMask`・`WriteMask`・`Comp`・`Pass`・`Fail`・`ZFail`（`Front`・`Back` 付きを含む）を読み、`[_Prop]` はその材質の値で決める。
  - 色を描くパスは、その時点で取りうるどの値でも、表と裏の両方の比較が不合格のときだけ描かないとする。自分の前のパスが書いた値に一致して描くパスは描くものとする。
  - 1 つの画素に同じパスの三角形が何度も重なりうるので、ステンシルを書く操作は新しい値が出なくなるまで繰り返して集合に足す。深度テストのあるパス（`ZTest` が `Always`・`Off` 以外）は描かれない画素があるので、前の値も残し、`ZFail` の値も足す。`ZTest Always` で `Cull Off` のパスはメッシュのすべての画素を通るので、前の値はそのパスが書いた値に置き換わる。ただし `clip`・`discard`・`AlphaToMask On` のあるパスは書かない画素が残るので、前の値も残す。そのパスまでのステンシルを書くパスがすべて同じ `clip`・`discard` の文を持つときは、前のパスも同じ画素を飛ばしているので、飛ばした画素は誰も書いていない 0 のままとみなし、前の値は残さない（白い殻が出ていたシェーダーはこの形で、80ef932 の版では殻がまた出ていた）。
  - Pass のないサーフェスシェーダーは SubShader の `ColorMask` とステンシルだけで決める。
  - 読み方（最初の SubShader・コメントの除去・パスと SubShader の分け方）は深度の判定（`ShaderWritesDepth`）と共通の `ColorPasses` にまとめた。
- MToon の互換表示: lilToon の `_ColorMask` が 0 の材質は、FakeShadow と同じく、材質の枠は残して色のアルファを 0 にし、深度を書かない半透明にする（アウトラインも出さない）。lilToon の拡張には元の値をそのまま渡すので、lilToon で描くときは `_ColorMask` 0 のとおり何も描かれない。

## 成り立たない場合

- `#include` した別ファイルやマクロの中で `ColorMask` を切り替えるシェーダーは、ファイル本体に書かれた命令だけで判定する。
- 色を描かずに深度だけを書く材質（深度マスク）は、lilToon では元のとおり深度を書くが、MToon の互換表示では深度も書かないので、その後ろの面が見える。
- lilToon の `_ColorMask` が 0 でも、アウトラインのパスが色を描く材質では、MToon の互換表示からアウトラインも消える。
- 前のパスが書いたステンシルを、深度テストのあるパスや片面だけのパスで消す材質は、消し残しがありうるとみなして描く。
- 判定は 1 つの材質の中だけで行う。ほかの材質が先に書いたステンシルは 0 とみなす。
- lilToon の材質のステンシル（`_StencilComp`・`_StencilRef`）は見ていない。ステンシルを待つ設定の lilToon の材質は、MToon の互換表示では描かれる。
- `ColorMask 0 1` のように描画先を指定した `ColorMask 0` も、色を描かないとみなす。HLSL のプリプロセッサの分岐の中に波かっこがあると、パスの範囲を読み違えることがある。
- ステンシルを読んで、一致しない所（`NotEqual` と 0 以外の参照値など）に描く材質は従来どおり描く。ほかの材質がステンシルを書いた所も MToon では描くので、元より広く見える。

## 検証

- EditMode: `UnsupportedShaderSubstitutionTests.AStencilWriterStaysWithoutColorAndDepth`・`TheFirstSubShaderDecidesWhetherAnyPassDrawsColor`（15 通り。サーフェスシェーダーが自前の影パスを持つ場合を含む）・`TheFirstSubShaderDecidesWhetherColorPassesWriteDepth`（共通化後も同じ 11 通り）、`MaterialFallbackTests.ColorMaskZeroFallbackIsInvisible`（`_ColorMask` 0 と 15）。
- EditMode: `AStencilTestThatFailsOnAClearedBufferDrawsNothing`（18 通り。`Ref`・`ReadMask`・`[Prop]`・`CompFront`／`CompBack`・パス側の上書き・ステンシルを待たない 2 つ目のパス・自分で書いた値に一致して描くパス・`ZTest Always` と `Cull Off` で消してから待つパス・深度テストのあるパスで消すパス・`IncrSat` で書く値・`clip` のあるパスで書く値・前のパスと同じ／違う `clip` を持つパスで消す場合）。全体では、この変更の前の develop と同じ 35 件だけが失敗する（いずれも検証環境に無いパッケージ・既知の失敗）。
- 白い殻が出ていたアバターと別の 4 体を書き出して描画した。白い殻が出ていたアバターは殻が消えて元のアバターとの画素差が平均 25 から 2 へ下がり、残りの 4 体は変更前と画素単位で同じだった。
