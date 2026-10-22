# 半透明の材質が深度を書くかどうかを MToon と置き換え先へ引き継ぐ

- 日付: 2026-10-22
- 対象: `Editor/UniVrmOneClickExporter.cs`（`CreateMToonFallback`）・`Editor/UnsupportedShaderSubstitution.cs`（`FromGeneric`・`WritesDepth`・`ShaderWritesDepth`）・`Tests/Editor/MaterialFallbackTests.cs`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/DepthWriteProbe.shader`

## 背景

100 体の無料アバターで見え方を比べたところ、全部の材質が半透明のシェーダーを使う 1 体で、後ろから顔が透けて見え、服の内側など隠れているはずの部分が見えていた。

## 原因

- lilToon 2.3.4 の半透明シェーダー（`lts_trans.shader`）は `_ZWrite` の既定値が 1 で、深度を書きながら半透明で描く。手前の面を描いたあとは奥の面が描かれないので、メッシュの奥側は透けない。
- 標準 MToon の互換表示（`CreateMToonFallback`）は半透明を `TransparentWithZWriteMode.Off` のまま書き出していた。MToon は深度を書かずに半透明で描くので、同じメッシュの奥の面が手前に重なって見える。
- lilToon 以外のシェーダーを lilToon の半透明へ置き換えるとき、元のシェーダーが深度を書くかを見ていなかった。
- サーフェスシェーダーが影のパスだけを自分で書いていると、書かれたパスがすべて影なので深度を書かないと読んでいた。色のパスは Unity が作り、SubShader の `ZWrite` が掛かる。

## 決定

Unity とシェーダーの定義から読めることだけで決める。アバターや材質の名前では分けない。

- MToon の互換表示: アルファモードが半透明で、lilToon の `_ZWrite` が 0.5 より大きいとき、`TransparentWithZWriteMode.On` にする。FakeShadow は従来どおり深度を書かない。
- 置き換え: 半透明に置き換えるとき、lilToon の `_ZWrite` を元のシェーダーに合わせる。
  - 元のシェーダーが `_ZWrite` を宣言していれば、その値を使う（Standard の Fade・Transparent など）。
  - 宣言がなければ、シェーダーのファイル（`.shader`）の最初の SubShader を読む。色を描くパスのうち 1 つでも、パス自身の `ZWrite`、なければ SubShader の `ZWrite`、どちらもなければ Unity の既定値（On）で深度を書くなら 1 にする。影（ShadowCaster）・Meta・深度だけのパスは色を描かないので見ない。`ZWrite [_Prop]` はその材質の値で決める。コメントは除いてから読む。
  - `ZWrite` の値は `On`・`True`・`1` を深度を書く、`Off`・`False`・`0` を書かないとして読む。大文字と小文字は区別しない。
  - `SubShader`・`Pass`・`#pragma surface` も大文字と小文字を区別せずに探す（ShaderLab のキーワードは区別しない）。`UsePass`・`GrabPass`・ステンシルの `Pass Replace` などは Pass として数えない。
  - サーフェスシェーダー（Pass の外に `#pragma surface` がある SubShader）は、Unity が作る色のパスに SubShader の `ZWrite` が掛かる。SubShader の `ZWrite` が On なら、影のパスを自分で書いていても 1 にする。Pass がなく `ZWrite` もなければ 0 にする。
  - ただし `#pragma surface` の行にアルファブレンドのオプション（`alpha`・`alpha:auto`・`alpha:fade`・`alpha:premul`・`alpha:blend`・`decal:add`・`decal:blend`）があれば、Unity が作る色のパスは `ZWrite Off` になるので、SubShader の `ZWrite` では 1 にしない。同じ SubShader に自分で書いた色のパスがあれば、そのパスで決める。`keepalpha`・`alphatest:…` はブレンドしないので、ここには含めない。
  - ファイルを読めないシェーダー（Unity の組み込み・見つからないシェーダー）は 0 にする。Unity 組み込みの半透明シェーダーは深度を書かない。読み込みが失敗したとき（ほかのプロセスが開いている・権限がない）も 0 にして、書き出しは止めない。

## 成り立たない場合

- `#include` した別ファイルやマクロの中で `ZWrite` を切り替えるシェーダーは、ファイル本体に書かれた命令だけで判定する。
- 最初の SubShader を対象の GPU が使えず、別の SubShader が使われる場合は、その SubShader とは違う判定になることがある。
- サーフェスシェーダーで `ZWrite` を書いていないものは、深度を書かないものとして扱う。
- `UsePass` で別のシェーダーから借りたパスは読みに行かないので、そのパスの `ZWrite` は判定に入らない。
- `#if` などのプリプロセッサの分岐で `{` と `}` の数が合っていないと、SubShader や Pass の範囲を読み違えることがある。
- MToon の深度を書く半透明は描画順（キュー 2501 から）が lilToon の半透明（3000）より前になるので、深度を書かない別の半透明との重なりの順序が変わることがある。

## 検証

- EditMode: `MaterialFallbackTests.TransparentFallbackWritesDepthWhenLilToonDoes`（`_ZWrite` 1 と 0）、`UnsupportedShaderSubstitutionTests.ABlendedSourceKeepsWhetherItWritesDepth`・`TheFirstSubShaderDecidesWhetherColorPassesWriteDepth`（17 通り）。
- 100 体の見え方の比較（元のアバター／修正前／修正後、正面・45°・背面）。
