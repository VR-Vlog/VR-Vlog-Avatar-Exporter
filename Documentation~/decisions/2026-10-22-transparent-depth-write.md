# 半透明の材質が深度を書くかどうかを MToon と置き換え先へ引き継ぐ

- 日付: 2026-10-22
- 対象: `Editor/UniVrmOneClickExporter.cs`（`CreateMToonFallback`）・`Editor/UnsupportedShaderSubstitution.cs`（`FromGeneric`・`WritesDepth`・`ShaderWritesDepth`）・`Tests/Editor/MaterialFallbackTests.cs`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/DepthWriteProbe.shader`

## 背景

100 体の無料アバターで見え方を比べたところ、全部の材質が半透明のシェーダーを使う 1 体で、後ろから顔が透けて見え、服の内側など隠れているはずの部分が見えていた。

## 原因

- lilToon 2.3.4 の半透明シェーダー（`lts_trans.shader`）は `_ZWrite` の既定値が 1 で、深度を書きながら半透明で描く。手前の面を描いたあとは奥の面が描かれないので、メッシュの奥側は透けない。
- 標準 MToon の互換表示（`CreateMToonFallback`）は半透明を `TransparentWithZWriteMode.Off` のまま書き出していた。MToon は深度を書かずに半透明で描くので、同じメッシュの奥の面が手前に重なって見える。
- lilToon 以外のシェーダーを lilToon の半透明へ置き換えるとき、元のシェーダーが深度を書くかを見ていなかった。

## 決定

Unity とシェーダーの定義から読めることだけで決める。アバターや材質の名前では分けない。

- MToon の互換表示: アルファモードが半透明で、lilToon の `_ZWrite` が 0.5 より大きいとき、`TransparentWithZWriteMode.On` にする。FakeShadow は従来どおり深度を書かない。
- 置き換え: 半透明に置き換えるとき、lilToon の `_ZWrite` を元のシェーダーに合わせる。
  - 元のシェーダーが `_ZWrite` を宣言していれば、その値を使う（Standard の Fade・Transparent など）。
  - 宣言がなければ、シェーダーのファイル（`.shader`）の最初の SubShader を読む。色を描くパスのうち 1 つでも、パス自身の `ZWrite`、なければ SubShader の `ZWrite`、どちらもなければ Unity の既定値（On）で深度を書くなら 1 にする。影（ShadowCaster）・Meta・深度だけのパスは色を描かないので見ない。`ZWrite [_Prop]` はその材質の値で決める。コメントは除いてから読む。
  - Pass のないサーフェスシェーダーは SubShader の `ZWrite` だけで決め、なければ 0 にする。
  - ファイルを読めないシェーダー（Unity の組み込み・見つからないシェーダー）は 0 にする。Unity 組み込みの半透明シェーダーは深度を書かない。

## 成り立たない場合

- `#include` した別ファイルやマクロの中で `ZWrite` を切り替えるシェーダーは、ファイル本体に書かれた命令だけで判定する。
- 最初の SubShader を対象の GPU が使えず、別の SubShader が使われる場合は、その SubShader とは違う判定になることがある。
- サーフェスシェーダーで `ZWrite` を書いていないものは、深度を書かないものとして扱う。
- MToon の深度を書く半透明は描画順（キュー 2501 から）が lilToon の半透明（3000）より前になるので、深度を書かない別の半透明との重なりの順序が変わることがある。

## 検証

- EditMode: `MaterialFallbackTests.TransparentFallbackWritesDepthWhenLilToonDoes`（`_ZWrite` 1 と 0）、`UnsupportedShaderSubstitutionTests.ABlendedSourceKeepsWhetherItWritesDepth`・`TheFirstSubShaderDecidesWhetherColorPassesWriteDepth`（10 通り）。
- 100 体の見え方の比較（元のアバター／修正前／修正後、正面・45°・背面）。
